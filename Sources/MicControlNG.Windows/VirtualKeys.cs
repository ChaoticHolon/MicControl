namespace MicControlNG.Windows;

/// <summary>
/// Maps key names (WPF/Avalonia <c>Key</c> enum names, as stored in hotkey settings) to Win32 virtual-key codes.
/// </summary>
internal static class VirtualKeys
{
    private static readonly Dictionary<string, int> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Cancel"] = 0x03, ["Back"] = 0x08, ["Tab"] = 0x09, ["Clear"] = 0x0C, ["Return"] = 0x0D, ["Enter"] = 0x0D,
        ["Pause"] = 0x13, ["Capital"] = 0x14, ["CapsLock"] = 0x14, ["Escape"] = 0x1B, ["Space"] = 0x20,
        ["Prior"] = 0x21, ["PageUp"] = 0x21, ["Next"] = 0x22, ["PageDown"] = 0x22, ["End"] = 0x23, ["Home"] = 0x24,
        ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28, ["Select"] = 0x29, ["Print"] = 0x2A,
        ["Execute"] = 0x2B, ["Snapshot"] = 0x2C, ["PrintScreen"] = 0x2C, ["Insert"] = 0x2D, ["Delete"] = 0x2E, ["Help"] = 0x2F,
        ["LWin"] = 0x5B, ["RWin"] = 0x5C, ["Apps"] = 0x5D, ["Sleep"] = 0x5F,
        ["Multiply"] = 0x6A, ["Add"] = 0x6B, ["Separator"] = 0x6C, ["Subtract"] = 0x6D, ["Decimal"] = 0x6E, ["Divide"] = 0x6F,
        ["NumLock"] = 0x90, ["Scroll"] = 0x91,
        ["LeftShift"] = 0xA0, ["RightShift"] = 0xA1, ["LeftCtrl"] = 0xA2, ["RightCtrl"] = 0xA3, ["LeftAlt"] = 0xA4, ["RightAlt"] = 0xA5,
        ["BrowserBack"] = 0xA6, ["BrowserForward"] = 0xA7, ["BrowserRefresh"] = 0xA8, ["BrowserStop"] = 0xA9,
        ["BrowserSearch"] = 0xAA, ["BrowserFavorites"] = 0xAB, ["BrowserHome"] = 0xAC,
        ["VolumeMute"] = 0xAD, ["VolumeDown"] = 0xAE, ["VolumeUp"] = 0xAF,
        ["MediaNextTrack"] = 0xB0, ["MediaPreviousTrack"] = 0xB1, ["MediaStop"] = 0xB2, ["MediaPlayPause"] = 0xB3,
        ["LaunchMail"] = 0xB4, ["SelectMedia"] = 0xB5, ["LaunchApplication1"] = 0xB6, ["LaunchApplication2"] = 0xB7,
        ["OemSemicolon"] = 0xBA, ["Oem1"] = 0xBA, ["OemPlus"] = 0xBB, ["OemComma"] = 0xBC, ["OemMinus"] = 0xBD,
        ["OemPeriod"] = 0xBE, ["OemQuestion"] = 0xBF, ["Oem2"] = 0xBF, ["OemTilde"] = 0xC0, ["Oem3"] = 0xC0,
        ["OemOpenBrackets"] = 0xDB, ["Oem4"] = 0xDB, ["OemPipe"] = 0xDC, ["Oem5"] = 0xDC,
        ["OemCloseBrackets"] = 0xDD, ["Oem6"] = 0xDD, ["OemQuotes"] = 0xDE, ["Oem7"] = 0xDE, ["Oem8"] = 0xDF,
        ["OemBackslash"] = 0xE2, ["Oem102"] = 0xE2, ["Attn"] = 0xF6, ["CrSel"] = 0xF7, ["ExSel"] = 0xF8,
        ["EraseEof"] = 0xF9, ["Play"] = 0xFA, ["Zoom"] = 0xFB, ["Pa1"] = 0xFD, ["OemClear"] = 0xFE,
    };

    /// <returns>The virtual-key code, or 0 when the name is unknown.</returns>
    public static int FromKeyName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return 0;
        }

        if (Named.TryGetValue(name, out var vk))
        {
            return vk;
        }

        // A..Z
        if (name.Length == 1 && char.IsAsciiLetter(name[0]))
        {
            return char.ToUpperInvariant(name[0]);
        }

        return (name.ToUpperInvariant(), name.Length) switch
        {
            ({ } n, 2) when n[0] == 'D' && char.IsAsciiDigit(n[1]) => 0x30 + (n[1] - '0'),
            ({ } n, 7) when n.StartsWith("NUMPAD", StringComparison.Ordinal) && char.IsAsciiDigit(n[6]) => 0x60 + (n[6] - '0'),
            ({ } n, 2 or 3) when n[0] == 'F' && int.TryParse(n.AsSpan(1), out var f) && f is >= 1 and <= 24 => 0x70 + f - 1,
            _ => 0,
        };
    }
}
