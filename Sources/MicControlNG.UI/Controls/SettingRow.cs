using Avalonia;
using Avalonia.Controls;

namespace MicControlNG.Controls;

/// <summary>A Windows 11 Settings-style card: glyph, header and description on the left, control on the right.</summary>
public sealed class SettingRow : ContentControl
{
    public static readonly StyledProperty<string?> HeaderProperty = AvaloniaProperty.Register<SettingRow, string?>(nameof(Header));

    public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<SettingRow, string?>(nameof(Description));

    public static readonly StyledProperty<string?> GlyphProperty = AvaloniaProperty.Register<SettingRow, string?>(nameof(Glyph));

    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>Optional icon-font glyph shown before the header.</summary>
    public string? Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }
}
