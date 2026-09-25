using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using MicControlNG.Hotkeys;

namespace MicControlNG.Controls;

/// <summary>
/// Captures a hotkey: click, then press a key combination or a middle/side mouse button (or scroll).
/// Backspace, Delete or Escape clears it. A lone modifier (e.g. Left Ctrl) is accepted when released.
/// </summary>
public sealed class HotkeyBox : TextBox
{
    public static readonly StyledProperty<HotkeyGesture> GestureProperty = AvaloniaProperty.Register<HotkeyBox, HotkeyGesture>(
        nameof(Gesture), HotkeyGesture.Empty, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> AllowMouseProperty = AvaloniaProperty.Register<HotkeyBox, bool>(nameof(AllowMouse), true);

    private Key pendingModifierKey = Key.None;

    public HotkeyBox()
    {
        IsReadOnly = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        MinWidth = 150;
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        ToolTip.SetTip(this, "Click, then press a key, key combination or mouse button. Backspace clears.");
        UpdateText();
    }

    /// <summary>Raised when any HotkeyBox starts or stops capturing, so global hotkeys can be paused meanwhile.</summary>
    public static event EventHandler<bool>? CapturingChanged;

    public HotkeyGesture Gesture
    {
        get => GetValue(GestureProperty);
        set => SetValue(GestureProperty, value);
    }

    /// <summary>False on platforms whose global shortcut API can't bind mouse buttons.</summary>
    public bool AllowMouse
    {
        get => GetValue(AllowMouseProperty);
        set => SetValue(AllowMouseProperty, value);
    }

    // Look like a regular TextBox; theme styles don't match derived types by default.
    protected override Type StyleKeyOverride => typeof(TextBox);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GestureProperty)
        {
            UpdateText();
        }
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        CapturingChanged?.Invoke(this, true);
        UpdateText();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        pendingModifierKey = Key.None;
        CapturingChanged?.Invoke(this, false);
        UpdateText();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var key = e.Key;
        var modifiers = ToModifiers(e.KeyModifiers, key);
        if (key == Key.Tab && modifiers == HotkeyModifiers.None)
        {
            // Let focus navigation happen.
            return;
        }

        e.Handled = true;
        if (modifiers == HotkeyModifiers.None && key is Key.Escape or Key.Back or Key.Delete)
        {
            Commit(HotkeyGesture.Empty);
            return;
        }

        if (IsModifier(key))
        {
            pendingModifierKey = key;
            Text = $"{Format(modifiers)}{key}…";
            return;
        }

        Commit(new HotkeyGesture(modifiers, key.ToString()));
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        e.Handled = true;
        if (pendingModifierKey != Key.None && e.Key == pendingModifierKey)
        {
            Commit(new HotkeyGesture(ToModifiers(e.KeyModifiers, e.Key), e.Key.ToString()));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var button = e.GetCurrentPoint(this).Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.MiddleButtonPressed => MouseHotkey.Middle,
            PointerUpdateKind.XButton1Pressed => MouseHotkey.XButton1,
            PointerUpdateKind.XButton2Pressed => MouseHotkey.XButton2,
            _ => MouseHotkey.None,
        };

        if (IsFocused && AllowMouse && button != MouseHotkey.None)
        {
            e.Handled = true;
            Commit(new HotkeyGesture(ToModifiers(e.KeyModifiers, Key.None), Mouse: button));
            return;
        }

        base.OnPointerPressed(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (!IsFocused || !AllowMouse)
        {
            base.OnPointerWheelChanged(e);
            return;
        }

        e.Handled = true;
        Commit(new HotkeyGesture(ToModifiers(e.KeyModifiers, Key.None), Mouse: e.Delta.Y > 0 ? MouseHotkey.WheelUp : MouseHotkey.WheelDown));
    }

    private void Commit(HotkeyGesture gesture)
    {
        pendingModifierKey = Key.None;
        SetCurrentValue(GestureProperty, gesture);
        UpdateText();
    }

    private void UpdateText() => Text = Gesture.IsEmpty
        ? IsFocused ? "Press a key…" : "Not set"
        : Gesture.ToString();

    private static bool IsModifier(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    /// <summary>Held modifiers, not counting the key itself (so Left Ctrl alone can be a hotkey).</summary>
    internal static HotkeyModifiers ToModifiers(KeyModifiers modifiers, Key key)
    {
        var result = HotkeyModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Control) && key is not (Key.LeftCtrl or Key.RightCtrl)) result |= HotkeyModifiers.Control;
        if (modifiers.HasFlag(KeyModifiers.Alt) && key is not (Key.LeftAlt or Key.RightAlt)) result |= HotkeyModifiers.Alt;
        if (modifiers.HasFlag(KeyModifiers.Shift) && key is not (Key.LeftShift or Key.RightShift)) result |= HotkeyModifiers.Shift;
        if (modifiers.HasFlag(KeyModifiers.Meta) && key is not (Key.LWin or Key.RWin)) result |= HotkeyModifiers.Windows;
        return result;
    }

    private static string Format(HotkeyModifiers modifiers) =>
        modifiers == HotkeyModifiers.None ? string.Empty : new HotkeyGesture(modifiers, "x").ToString()[..^1];
}
