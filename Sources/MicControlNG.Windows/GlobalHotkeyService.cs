using System.Collections.Immutable;
using System.Runtime.InteropServices;
using MicControlNG.Hotkeys;
using MicControlNG.Platform;
using MicControlNG.Settings;
using Microsoft.Extensions.Logging;
using static MicControlNG.Windows.NativeMethods;

namespace MicControlNG.Windows;

/// <summary>
/// System-wide keyboard and mouse hotkeys via low-level hooks running on a dedicated thread.
/// Handlers are invoked on the UI thread with <c>true</c> on press and <c>false</c> on release.
/// </summary>
public sealed partial class GlobalHotkeyService : IGlobalHotkeys, IDisposable
{
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    private readonly IUiDispatcher dispatcher;
    private readonly ILogger<GlobalHotkeyService> logger;
    private readonly Thread hookThread;
    private readonly HookProc keyboardProc;
    private readonly HookProc mouseProc;
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
    }

    public bool IsPaused
    {
        get => Volatile.Read(ref isPaused);
        set => Volatile.Write(ref isPaused, value);
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
        if (gestures.Length == 0)
        {
            return EmptyRegistration.Instance;
        }

        var binding = new Binding(gestures, settings.Suppress, settings.IgnoreModifiers, handler);
        ImmutableInterlocked.Update(ref bindings, b => b.Add(binding));
        return new Registration(() => ImmutableInterlocked.Update(ref bindings, b => b.Remove(binding)));
    }

    public void Dispose()
    {
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

        while (GetMessage(out _, 0, 0, 0) > 0)
        {
        }

        UnhookWindowsHookEx(keyboardHook);
        UnhookWindowsHookEx(mouseHook);
    }

    private nint KeyboardHook(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && !IsPaused)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var isDown = wParam is WM_KEYDOWN or WM_SYSKEYDOWN;
            if (Process(t => !t.Gesture.IsMouse && t.VirtualKey == data.vkCode, isDown, (int)data.vkCode, isWheel: false))
            {
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
                    binding.IsPressed = true;
                    Dispatch(binding, true);
                }
            }
            else if (binding.IsPressed)
            {
                binding.IsPressed = false;
                suppress |= binding.Suppress;
                Dispatch(binding, false);
            }
        }

        return suppress;
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hotkey handler failed")]
    private partial void LogHandlerFailed(Exception exception);

    private sealed record Trigger(HotkeyGesture Gesture, int VirtualKey);

    private sealed class Binding(Trigger[] triggers, bool suppress, bool ignoreModifiers, Action<bool> handler)
    {
        public Trigger[] Triggers { get; } = triggers;
        public bool Suppress { get; } = suppress;
        public bool IgnoreModifiers { get; } = ignoreModifiers;
        public Action<bool> Handler { get; } = handler;
        public bool IsPressed { get; set; }
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
