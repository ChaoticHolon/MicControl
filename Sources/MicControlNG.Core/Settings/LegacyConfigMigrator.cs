using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MicControlNG.Hotkeys;

namespace MicControlNG.Settings;

/// <summary>
/// Imports settings from MicSwitch 1.x (<c>%APPDATA%\MicSwitch\release\config.cfg</c>), which stored several
/// Newtonsoft-serialized config objects wrapped in type metadata.
/// </summary>
public static class LegacyConfigMigrator
{
    private static readonly JsonDocumentOptions DocumentOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    /// <param name="legacyDirectory">The MicSwitch data folder (<c>%APPDATA%\MicSwitch</c>).</param>
    /// <param name="iconDirectory">Where custom icons embedded in the old config are written.</param>
    public static AppSettings? TryMigrate(string legacyDirectory, string iconDirectory)
    {
        var candidate = new[] { Path.Combine(legacyDirectory, "release", "config.cfg"), Path.Combine(legacyDirectory, "config.cfg") }
            .FirstOrDefault(File.Exists);
        if (candidate is null)
        {
            return null;
        }

        try
        {
            // 1.x wrote UTF-16 with a BOM; ReadAllText detects the encoding.
            return Migrate(File.ReadAllText(candidate), iconDirectory);
        }
        catch (Exception ex) when (ex is JsonException or IOException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    public static AppSettings Migrate(string legacyJson, string? iconDirectory)
    {
        // 1.x defaults: toggle mode and exclusive hotkeys; setup is considered done for existing users.
        var settings = new AppSettings { SetupCompleted = true };
        settings.Microphone.MuteMode = MuteMode.ToggleMute;
        var items = JsonNode.Parse(legacyJson, documentOptions: DocumentOptions)?["Items"]?.AsArray() ?? [];
        var configs = items
            .Where(x => x?["TypeName"] is not null && x["ConfigValue"] is JsonObject)
            .ToDictionary(x => x!["TypeName"]!.GetValue<string>().Split('.')[^1], x => x!["ConfigValue"]!.AsObject(), StringComparer.Ordinal);

        if (configs.TryGetValue("MicSwitchConfig", out var main))
        {
            ApplyMain(settings, main, iconDirectory);
        }

        if (configs.TryGetValue("MicSwitchHotkeyConfig", out var hotkeys))
        {
            var mic = settings.Microphone;
            mic.Hotkey = Hotkey(hotkeys["Hotkey"]) ?? mic.Hotkey;
            mic.MuteMode = Enum<MuteMode>(hotkeys["MuteMode"]) ?? mic.MuteMode;
            mic.InitialState = Enum<MicrophoneState>(hotkeys["InitialMicrophoneState"]) ?? mic.InitialState;
            var enabled = Bool(hotkeys["EnableAdvancedHotkeys"]) ?? false;
            AddExtra(settings, HotkeyAction.ToggleMute, hotkeys["HotkeyForToggle"], enabled);
            AddExtra(settings, HotkeyAction.Mute, hotkeys["HotkeyForMute"], enabled);
            AddExtra(settings, HotkeyAction.Unmute, hotkeys["HotkeyForUnmute"], enabled);
            AddExtra(settings, HotkeyAction.PushToTalk, hotkeys["HotkeyForPushToTalk"], enabled);
            AddExtra(settings, HotkeyAction.PushToMute, hotkeys["HotkeyForPushToMute"], enabled);
        }

        if (configs.TryGetValue("MicSwitchOverlayConfig", out var overlay))
        {
            ApplyOverlay(settings.Overlay, overlay, iconDirectory);
        }

        if (configs.TryGetValue("MicSwitchVolumeControlConfig", out var output))
        {
            var enabled = Bool(output["IsEnabled"]) ?? false;
            settings.SpeakerDeviceId = String(output["DeviceId"]?["LineId"]) ?? settings.SpeakerDeviceId;
            AddExtra(settings, HotkeyAction.SpeakerToggleMute, output["HotkeyForToggle"], enabled);
            AddExtra(settings, HotkeyAction.SpeakerMute, output["HotkeyForMute"], enabled);
            AddExtra(settings, HotkeyAction.SpeakerUnmute, output["HotkeyForUnmute"], enabled);
            AddExtra(settings, HotkeyAction.SpeakerVolumeUp, output["HotkeyForVolumeUp"], enabled);
            AddExtra(settings, HotkeyAction.SpeakerVolumeDown, output["HotkeyForVolumeDown"], enabled);
        }

        return settings;
    }

    private static void ApplyMain(AppSettings settings, JsonObject main, string? iconDirectory)
    {
        var mic = settings.Microphone;
        mic.DeviceId = String(main["MicrophoneLineId"]?["LineId"]) ?? mic.DeviceId;
        mic.VolumeControlEnabled = Bool(main["VolumeControlEnabled"]) ?? false;
        mic.Volume = Float(main["Volume"]);

        // Oldest format: hotkey settings lived in the main config.
        if (String(main["MicrophoneHotkey"]) is not null || String(main["MicrophoneHotkeyAlt"]) is not null)
        {
            mic.Hotkey = new HotkeySettings
            {
                Key = Gesture(main["MicrophoneHotkey"]),
                AlternativeKey = Gesture(main["MicrophoneHotkeyAlt"]),
                Suppress = Bool(main["SuppressHotkey"]) ?? true,
            };
        }

        mic.MuteMode = Enum<MuteMode>(main["MuteMode"]) ?? mic.MuteMode;

        var n = settings.Notifications;
        if (main["Notifications"] is JsonObject notifications)
        {
            n.WhenUnmuted = String(notifications["On"]);
            n.WhenMuted = String(notifications["Off"]);
        }
        else if (main["Notification"] is JsonObject reversed)
        {
            // The very first format stored these swapped.
            n.WhenUnmuted = String(reversed["Off"]);
            n.WhenMuted = String(reversed["On"]);
        }

        n.Volume = Float(main["NotificationVolume"]) ?? n.Volume;

        var w = settings.Window;
        w.AppMode = AppMode.Tray;
        w.Bounds = Bounds(main["MainWindowBounds"]);

        ApplyOverlay(settings.Overlay, main, iconDirectory);
        if (Bool(main["OverlayEnabled"]) == false)
        {
            settings.Overlay.Visibility = OverlayVisibilityMode.Never;
        }
    }

    private static void ApplyOverlay(OverlaySettings overlay, JsonObject source, string? iconDirectory)
    {
        overlay.Visibility = Enum<OverlayVisibilityMode>(source["OverlayVisibilityMode"]) ?? overlay.Visibility;
        overlay.Bounds = Bounds(source["OverlayBounds"]) ?? overlay.Bounds;
        if (overlay.Visibility != OverlayVisibilityMode.Never)
        {
            overlay.LastVisibleMode = overlay.Visibility;
        }
        if (Float(source["OverlayOpacity"]) is > 0 and var opacity)
        {
            overlay.Opacity = opacity;
        }

        overlay.MutedIconPath = SaveIcon(source["MutedMicrophoneIcon"], iconDirectory, "muted.icon") ?? overlay.MutedIconPath;
        overlay.UnmutedIconPath = SaveIcon(source["MicrophoneIcon"], iconDirectory, "unmuted.icon") ?? overlay.UnmutedIconPath;
    }

    private static string? SaveIcon(JsonNode? node, string? directory, string fileName)
    {
        if (directory is null || String(node) is not { Length: > 0 } base64)
        {
            return null;
        }

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, Convert.FromBase64String(base64));
        return path;
    }

    private static void AddExtra(AppSettings settings, HotkeyAction action, JsonNode? node, bool enabled)
    {
        if (Hotkey(node) is { IsEmpty: false } hotkey)
        {
            settings.ExtraHotkeys.Add(new ExtraHotkey { Action = action, IsEnabled = enabled, Hotkey = hotkey });
        }
    }

    private static HotkeySettings? Hotkey(JsonNode? node) => node is JsonObject o
        ? new HotkeySettings
        {
            Key = Gesture(o["Key"]),
            AlternativeKey = Gesture(o["AlternativeKey"]),
            Suppress = Bool(o["Suppress"]) ?? true,
            IgnoreModifiers = Bool(o["IgnoreModifiers"]) ?? false,
        }
        : null;

    private static HotkeyGesture Gesture(JsonNode? node) => HotkeyGesture.TryParse(String(node), out var g) ? g : HotkeyGesture.Empty;

    /// <summary>Accepts "x, y, w, h" (TypeConverter format) or an object with X/Y/Left/Top/Width/Height.</summary>
    internal static WindowBounds? Bounds(JsonNode? node)
    {
        double[]? values = null;
        if (String(node) is { } text)
        {
            var parts = text.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4 && parts.All(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
            {
                values = [.. parts.Select(p => double.Parse(p, CultureInfo.InvariantCulture))];
            }
        }
        else if (node is JsonObject o)
        {
            values = [Float(o["X"] ?? o["Left"]) ?? 0, Float(o["Y"] ?? o["Top"]) ?? 0, Float(o["Width"]) ?? 0, Float(o["Height"]) ?? 0];
        }

        return values is [var l, var t, var w, var h] && w > 0 && h > 0 ? new WindowBounds(l, t, w, h) : null;
    }

    private static string? String(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;

    private static bool? Bool(JsonNode? node) => node is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    private static float? Float(JsonNode? node) => node is JsonValue v && v.TryGetValue<double>(out var d) ? (float)d : null;

    private static T? Enum<T>(JsonNode? node) where T : struct, Enum
    {
        if (node is not JsonValue v)
        {
            return null;
        }

        if (v.TryGetValue<string>(out var s) && System.Enum.TryParse<T>(s, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return v.TryGetValue<int>(out var i) && System.Enum.IsDefined(typeof(T), i) ? (T)(object)i : null;
    }
}
