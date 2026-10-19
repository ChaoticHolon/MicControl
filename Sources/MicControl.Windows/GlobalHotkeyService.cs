using System.Collections.Immutable;
using System.Runtime.InteropServices;
using MicControl.Hotkeys;
using MicControl.Platform;
using MicControl.Settings;
using Microsoft.Extensions.Logging;
using static MicControl.Windows.NativeMethods;

namespace MicControl.Windows;

/// <summary>
/// System-wide keyboard and mouse hotkeys via low-level hooks running on a dedicated thread.
/// Handlers are invoked on the UI thread with <c>true</c> on press and <c>false</c> on release.
/// </summary>
public sealed partial class GlobalHotkeyService : IGlobalHotkeys, IDisposable
{
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_LMENU = 0xA4, VK_RMENU = 0xA5;
    private const ushort VK_MASK = 0xE8; // unassigned: no app or Windows itself acts on it
    private const uint WM_CHECK_HELD = 0x8000 + 1; // WM_APP + 1, posted to the hook thread
    private static readonly TimeSpan HeldCheckInterval = TimeSpan.FromMilliseconds(250);

    private readonly IUiDispatcher dispatcher;
    private readonly ILogger<GlobalHotkeyService> logger;
    private readonly Thread hookThread;
    private readonly HookProc keyboardProc;
    private readonly HookProc mouseProc;
    private readonly Timer heldCheck;
    private ImmutableArray<Binding> bindings = [];
    private uint hookThreadId;
    private bool isPaused;

    public GlobalHotkeyService(IUiDispatcher dispatcher, ILogger<GlobalHotkeyService> logger)
    {
        this.dispatcher = dispatcher;
        this.logger = logger;
        // Delegates must stay referenced for as long as the hooks are installed.
        keyboardProc = KeyboardHook;
        mouseProc = MouseHook;
        hookThread = new Thread(RunHookLoop) { IsBackground = true, Name = "Global hotkey hook" };
        hookThread.Start();
        heldCheck = new Timer(_ => RequestHeldCheck(), null, HeldCheckInterval, HeldCheckInterval);
    }

    public bool IsPaused
    {
        get => Volatile.Read(ref isPaused);
        set
        {
            if (value != IsPaused)
            {
                LogPaused(value);
            }

            Volatile.Write(ref isPaused, value);
        }
    }

    public IDisposable Register(HotkeySettings settings, Action<bool> handler)
    {
        var gestures = new[] { settings.Key, settings.AlternativeKey }
            .Where(g => !g.IsEmpty)
            // A bare left/right click would hijack (and possibly block) all normal clicking.
            .Where(g => g.Mouse is not (MouseHotkey.Left or MouseHotkey.Right) || g.Modifiers != HotkeyModifiers.None)
            .Select(g => new Trigger(g, ToVirtualKey(g)))
            .Where(t => t.Gesture.IsMouse || t.VirtualKey != 0)
            .ToArray();
        var requested = (settings.Key.IsEmpty ? 0 : 1) + (settings.AlternativeKey.IsEmpty ? 0 : 1);
        if (gestures.Length < requested)
        {
            LogGesturesSkipped(settings.Key, settings.AlternativeKey, requested - gestures.Length);
        }

        if (gestures.Length == 0)
        {
            return EmptyRegistration.Instance;
        }

        LogRegistered(settings.Key, settings.AlternativeKey, settings.Suppress, settings.IgnoreModifiers);
        var binding = new Binding(gestures, settings.Suppress, settings.IgnoreModifiers, handler);
        ImmutableInterlocked.Update(ref bindings, b => b.Add(binding));
        return new Registration(() => ImmutableInterlocked.Update(ref bindings, b => b.Remove(binding)));
    }

    public void Dispose()
    {
        heldCheck.Dispose();
        if (hookThreadId != 0)
        {
            PostThreadMessage(hookThreadId, WM_QUIT, 0, 0);
        }
    }

    private static int ToVirtualKey(HotkeyGesture gesture) => VirtualKeys.FromKeyName(gesture.Key);

    private void RunHookLoop()
    {
        hookThreadId = GetCurrentThreadId();
        var module = GetModuleHandle(null);
        var keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, keyboardProc, module, 0);
        var mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, module, 0);
        if (keyboardHook == 0 || mouseHook == 0)
        {
            LogHookFailed(Marshal.GetLastPInvokeError());
        }
        else
        {
            LogHooksInstalled();
        }

        while (GetMessage(out var message, 0, 0, 0) > 0)
        {
            if (message.message == WM_CHECK_HELD)
            {
                ReleaseMissedKeyUps();
            }
        }

        UnhookWindowsHookEx(keyboardHook);
        UnhookWindowsHookEx(mouseHook);
        LogHooksRemoved();
    }

    private nint KeyboardHook(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && !IsPaused)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var isDown = wParam is WM_KEYDOWN or WM_SYSKEYDOWN;
            if (Process(t => !t.Gesture.IsMouse && t.VirtualKey == data.vkCode, isDown, (int)data.vkCode, isWheel: false))
            {
                if (isDown)
                {
                    MaskWinAndAlt((int)data.vkCode);
                }

                return 1;
            }
        }

        return CallNextHookEx(0, nCode, wParam, lParam);
    }

    private nint MouseHook(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && !IsPaused)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var high = (short)(data.mouseData >> 16);
            var (button, isDown) = (int)wParam switch
            {
                WM_LBUTTONDOWN => (MouseHotkey.Left, true),
                WM_LBUTTONUP => (MouseHotkey.Left, false),
                WM_RBUTTONDOWN => (MouseHotkey.Right, true),
                WM_RBUTTONUP => (MouseHotkey.Right, false),
                WM_MBUTTONDOWN => (MouseHotkey.Middle, true),
                WM_MBUTTONUP => (MouseHotkey.Middle, false),
                WM_XBUTTONDOWN => (high == 1 ? MouseHotkey.XButton1 : MouseHotkey.XButton2, true),
                WM_XBUTTONUP => (high == 1 ? MouseHotkey.XButton1 : MouseHotkey.XButton2, false),
                WM_MOUSEWHEEL => (high > 0 ? MouseHotkey.WheelUp : MouseHotkey.WheelDown, true),
                _ => (MouseHotkey.None, false),
            };
            if (button != MouseHotkey.None && Process(t => t.Gesture.Mouse == button, isDown, 0, isWheel: wParam == WM_MOUSEWHEEL))
            {
                return 1;
            }
        }

        return CallNextHookEx(0, nCode, wParam, lParam);
    }

    /// <returns>True when the input must be swallowed.</returns>
    private bool Process(Func<Trigger, bool> matchesInput, bool isDown, int ownVirtualKey, bool isWheel)
    {
        var suppress = false;
        foreach (var binding in bindings)
        {
            var trigger = binding.Triggers.FirstOrDefault(matchesInput);
            if (trigger is null)
            {
                continue;
            }

            if (isDown)
            {
                if (!binding.IgnoreModifiers && trigger.Gesture.Modifiers != CurrentModifiers(ownVirtualKey))
                {
                    continue;
                }

                suppress |= binding.Suppress;
                if (isWheel)
                {
                    Dispatch(binding, true);
                    Dispatch(binding, false);
                }
                else if (!binding.IsPressed)
                {
                    // Auto-repeat key-downs are ignored; only the first press is reported.
                    binding.PressedBy = trigger;
                    binding.IsPressed = true;
                    Dispatch(binding, true);
                }
            }
            else if (binding.IsPressed)
            {
                binding.Release();
                suppress |= binding.Suppress;
                Dispatch(binding, false);
            }
        }

        return suppress;
    }

    /// <summary>
    /// An exclusive hotkey with Win or Alt (Win+F1): the key is swallowed, so when Win or Alt comes up Windows thinks
    /// it was pressed alone and opens the Start menu, or the active app's menu bar takes the keyboard. A press of an
    /// unassigned key in between (as AutoHotkey does) tells Windows the modifier was used for something.
    /// </summary>
    private void MaskWinAndAlt(int swallowedKey)
    {
        if (swallowedKey is VK_LWIN or VK_RWIN or VK_MENU or VK_LMENU or VK_RMENU
            || !(IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN) || IsKeyDown(VK_LMENU) || IsKeyDown(VK_RMENU)))
        {
            return;
        }

        INPUT[] inputs =
        [
            new() { type = INPUT_KEYBOARD, u = new() { ki = new() { wVk = VK_MASK } } },
            new() { type = INPUT_KEYBOARD, u = new() { ki = new() { wVk = VK_MASK, dwFlags = KEYEVENTF_KEYUP } } },
        ];
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
        {
            var error = Marshal.GetLastPInvokeError();
            LogMaskFailed(error);
        }
    }

    // Thread pool: only wakes the hook thread while a hotkey is held.
    private void RequestHeldCheck()
    {
        if (hookThreadId != 0 && bindings.Any(b => b.IsPressed))
        {
            PostThreadMessage(hookThreadId, WM_CHECK_HELD, 0, 0);
        }
    }

    /// <summary>
    /// Releases a held hotkey whose key-up never reached the hook: Windows doesn't send it while the lock screen or a
    /// UAC prompt has the input (Win+L while holding push-to-talk), and a held push-to-talk must not stay live. Two
    /// checks in a row must find the key up. Exclusive hotkeys are left alone: a key the hook swallows never shows
    /// as down in the key state.
    /// </summary>
    private void ReleaseMissedKeyUps()
    {
        foreach (var binding in bindings)
        {
            if (!binding.IsPressed || binding.Suppress || binding.PressedBy is not { } trigger || VirtualKeyOf(trigger) is not { } key)
            {
                continue;
            }

            if (IsKeyDown(key))
            {
                binding.MissedChecks = 0;
            }
            else if (++binding.MissedChecks >= 2)
            {
                LogMissedKeyUp(trigger.Gesture);
                binding.Release();
                Dispatch(binding, false);
            }
        }

        static int? VirtualKeyOf(Trigger trigger) => trigger.Gesture.Mouse switch
        {
            MouseHotkey.None => trigger.VirtualKey,
            MouseHotkey.Left => 0x01,
            MouseHotkey.Right => 0x02,
            MouseHotkey.Middle => 0x04,
            MouseHotkey.XButton1 => 0x05,
            MouseHotkey.XButton2 => 0x06,
            _ => null,
        };
    }

    private void Dispatch(Binding binding, bool isPressed) =>
        dispatcher.Post(() =>
        {
            try
            {
                binding.Handler(isPressed);
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                LogHandlerFailed(ex);
            }
        });

    /// <summary>Modifiers currently held, not counting the key being processed (so e.g. LeftCtrl alone can be a hotkey).</summary>
    private static HotkeyModifiers CurrentModifiers(int excludeVirtualKey)
    {
        var result = HotkeyModifiers.None;
        if (IsHeld(VK_CONTROL, 0xA2, 0xA3)) result |= HotkeyModifiers.Control;
        if (IsHeld(VK_MENU, 0xA4, 0xA5)) result |= HotkeyModifiers.Alt;
        if (IsHeld(VK_SHIFT, 0xA0, 0xA1)) result |= HotkeyModifiers.Shift;
        if (IsHeld(VK_LWIN, VK_LWIN, VK_RWIN)) result |= HotkeyModifiers.Windows;
        return result;

        bool IsHeld(int generic, int left, int right) =>
            excludeVirtualKey != generic && excludeVirtualKey != left && excludeVirtualKey != right
            && (IsKeyDown(left) || IsKeyDown(right));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to install low-level input hooks, Win32 error {Error}")]
    private partial void LogHookFailed(int error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hotkey {Key} is no longer held but its release never arrived (lock screen or a UAC prompt?); released it")]
    private partial void LogMissedKeyUp(HotkeyGesture key);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hotkey handler failed")]
    private partial void LogHandlerFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not mask Win/Alt after an exclusive hotkey, Win32 error {Error}")]
    private partial void LogMaskFailed(int error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Low-level keyboard and mouse hooks installed")]
    private partial void LogHooksInstalled();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Low-level hooks removed")]
    private partial void LogHooksRemoved();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hotkeys paused: {Paused} (a hotkey box is capturing)")]
    private partial void LogPaused(bool paused);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hotkey registered: {Key} / {AlternativeKey} (exclusive: {Suppress}, ignore modifiers: {IgnoreModifiers})")]
    private partial void LogRegistered(HotkeyGesture key, HotkeyGesture alternativeKey, bool suppress, bool ignoreModifiers);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hotkey {Key} / {AlternativeKey}: {Count} key(s) can't be used (unknown key, or a bare left/right click)")]
    private partial void LogGesturesSkipped(HotkeyGesture key, HotkeyGesture alternativeKey, int count);

    private sealed record Trigger(HotkeyGesture Gesture, int VirtualKey);

    private sealed class Binding(Trigger[] triggers, bool suppress, bool ignoreModifiers, Action<bool> handler)
    {
        public Trigger[] Triggers { get; } = triggers;
        public bool Suppress { get; } = suppress;
        public bool IgnoreModifiers { get; } = ignoreModifiers;
        public Action<bool> Handler { get; } = handler;

        // Written on the hook thread only; IsPressed is also read by the held-key timer.
        public volatile bool IsPressed;
        public Trigger? PressedBy { get; set; }
        public int MissedChecks { get; set; }

        public void Release()
        {
            IsPressed = false;
            PressedBy = null;
            MissedChecks = 0;
        }
    }

    private sealed class Registration(Action dispose) : IDisposable
    {
        private Action? dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref dispose, null)?.Invoke();
    }

    private sealed class EmptyRegistration : IDisposable
    {
        public static readonly EmptyRegistration Instance = new();

        public void Dispose()
        {
        }
    }
}
