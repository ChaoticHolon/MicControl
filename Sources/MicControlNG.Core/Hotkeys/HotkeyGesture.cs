namespace MicControlNG.Hotkeys;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

public enum MouseHotkey
{
    None,
    Left,
    Middle,
    Right,
    XButton1,
    XButton2,
    WheelUp,
    WheelDown,
}

/// <summary>
/// A keyboard or mouse gesture. <see cref="Key"/> is a key name as used by the WPF and Avalonia <c>Key</c> enums,
/// kept as a string so this type stays UI-framework independent.
/// The text format ("Ctrl+Shift+F1", "MouseXButton1") is compatible with MicControlNG 1.x configuration files.
/// </summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers = HotkeyModifiers.None, string? Key = null, MouseHotkey Mouse = MouseHotkey.None)
{
    public const char Delimiter = '+';
    private const string MousePrefix = "Mouse";

    public static readonly HotkeyGesture Empty = new();

    private static readonly Dictionary<string, HotkeyModifiers> ModifierNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Alt"] = HotkeyModifiers.Alt,
        ["Ctrl"] = HotkeyModifiers.Control,
        ["Control"] = HotkeyModifiers.Control,
        ["Shift"] = HotkeyModifiers.Shift,
        ["Win"] = HotkeyModifiers.Windows,
        ["Windows"] = HotkeyModifiers.Windows,
    };

    // Aliases emitted by MicControlNG 1.x for keys whose names are awkward to type.
    private static readonly Dictionary<string, string> KeyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["*"] = "Multiply",
        ["Num *"] = "Multiply",
        ["+"] = "OemPlus",
        ["="] = "OemPlus",
        ["-"] = "OemMinus",
        ["`"] = "OemTilde",
        ["Num +"] = "Add",
        ["Num -"] = "Subtract",
        ["/"] = "Divide",
        ["Num /"] = "Divide",
        ["Enter"] = "Return",
        ["0"] = "D0", ["1"] = "D1", ["2"] = "D2", ["3"] = "D3", ["4"] = "D4",
        ["5"] = "D5", ["6"] = "D6", ["7"] = "D7", ["8"] = "D8", ["9"] = "D9",
    };

    public bool IsEmpty => string.IsNullOrEmpty(Key) && Mouse == MouseHotkey.None;

    public bool IsMouse => Mouse != MouseHotkey.None;

    public static HotkeyGesture Parse(string? text)
    {
        return TryParse(text, out var result) ? result : throw new FormatException($"Unrecognized hotkey: '{text}'");
    }

    public static bool TryParse(string? text, out HotkeyGesture result)
    {
        result = Empty;
        var source = text?.Trim();
        if (string.IsNullOrEmpty(source) || source.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // The key itself may be '+', so split on the last delimiter that is not the final character.
        var split = source.Length > 1 ? source.LastIndexOf(Delimiter, source.Length - 2) : -1;
        var keyPart = split >= 0 ? source[(split + 1)..].Trim() : source;
        var modifiers = HotkeyModifiers.None;
        if (split > 0)
        {
            foreach (var name in source[..split].Split(Delimiter, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!ModifierNames.TryGetValue(name, out var modifier))
                {
                    return false;
                }

                modifiers |= modifier;
            }
        }

        if (keyPart.StartsWith(MousePrefix, StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse<MouseHotkey>(keyPart[MousePrefix.Length..], ignoreCase: true, out var mouse)
            && mouse is not (MouseHotkey.None or MouseHotkey.WheelUp or MouseHotkey.WheelDown))
        {
            result = new HotkeyGesture(modifiers, Mouse: mouse);
            return true;
        }

        if (keyPart.StartsWith("Wheel", StringComparison.OrdinalIgnoreCase) && Enum.TryParse(keyPart, ignoreCase: true, out mouse))
        {
            result = new HotkeyGesture(modifiers, Mouse: mouse);
            return true;
        }

        if (keyPart.Length == 0 || keyPart.Contains(' ', StringComparison.Ordinal) && !KeyAliases.ContainsKey(keyPart))
        {
            return false;
        }

        result = new HotkeyGesture(modifiers, KeyAliases.GetValueOrDefault(keyPart, keyPart));
        return true;
    }

    public override string ToString()
    {
        if (IsEmpty)
        {
            return "None";
        }

        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
        parts.Add(Mouse switch
        {
            MouseHotkey.None => Key!,
            MouseHotkey.WheelUp or MouseHotkey.WheelDown => Mouse.ToString(),
            _ => MousePrefix + Mouse,
        });
        return string.Join(Delimiter, parts);
    }
}
