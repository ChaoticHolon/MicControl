using System.Text.Json;
using System.Text.Json.Serialization;
using MicControlNG.Hotkeys;

namespace MicControlNG.Settings;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    Converters = [typeof(HotkeyGestureJsonConverter)])]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
    /// <summary>Keeps characters such as '+' unescaped so the file stays readable when edited by hand.</summary>
    public static SettingsJsonContext Readable => field ??= new(new JsonSerializerOptions(Default.Options)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}

/// <summary>Stores hotkeys as their human-readable text form, e.g. "Ctrl+F1".</summary>
internal sealed class HotkeyGestureJsonConverter : JsonConverter<HotkeyGesture>
{
    public override HotkeyGesture Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return HotkeyGesture.TryParse(reader.GetString(), out var gesture) ? gesture : HotkeyGesture.Empty;
    }

    public override void Write(Utf8JsonWriter writer, HotkeyGesture value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
