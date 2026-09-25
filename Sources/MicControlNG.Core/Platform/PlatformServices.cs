using MicControlNG.Settings;

namespace MicControlNG.Platform;

// Contracts implemented per operating system (MicControlNG.Windows today, a Linux backend later).
// The UI layer depends only on these.

/// <summary>Runs work on the UI thread; platform callbacks arrive on arbitrary threads.</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

public enum AudioFlow
{
    Capture,
    Render,
}

public sealed record AudioDeviceInfo(string Id, string Name);

/// <summary>Enumerates audio devices. Members are used on the UI thread.</summary>
public interface IAudioDevices
{
    /// <summary>Raised on the UI thread when devices are added, removed or change state.</summary>
    event EventHandler? DevicesChanged;

    IReadOnlyList<AudioDeviceInfo> GetDevices(AudioFlow flow);

    IAudioEndpointGroup CreateGroup(AudioFlow flow);
}

/// <summary>
/// Mute/volume control over one device or, with <see cref="AppSettings.AllDevices"/>, every active device of a flow.
/// Members are used on the UI thread.
/// </summary>
public interface IAudioEndpointGroup : IDisposable
{
    /// <summary>Raised on the UI thread when mute or volume changes, from this app or externally.</summary>
    event EventHandler? StateChanged;

    string DeviceId { get; set; }

    bool IsConnected { get; }

    /// <summary><c>true</c> only when every controlled device is muted; <c>null</c> when none is connected.</summary>
    bool? Mute { get; set; }

    /// <summary>Scalar volume 0..1.</summary>
    float? Volume { get; set; }

    /// <summary>Re-reads the device list and re-attaches to the selected device(s).</summary>
    void Rebind();
}

/// <summary>Reports the live input level of a microphone (for level meters and the speaking indicator).</summary>
public interface IInputLevelMonitor
{
    /// <summary>
    /// Starts listening to a capture device (<c>null</c> or <see cref="AppSettings.AllDevices"/> = the default
    /// communications microphone). <paramref name="onPeak"/> receives peaks 0..1 on a background thread.
    /// Dispose the result to stop. Opening the device shows the OS "microphone in use" indicator.
    /// </summary>
    IDisposable Start(string? deviceId, Action<float> onPeak);
}

public interface IGlobalHotkeys
{
    /// <summary>While true (a hotkey editor is capturing input) hotkeys neither fire nor get suppressed.</summary>
    bool IsPaused { get; set; }

    /// <summary>Registers a hotkey. The handler runs on the UI thread with <c>true</c> on press, <c>false</c> on release.</summary>
    IDisposable Register(HotkeySettings settings, Action<bool> handler);
}

public interface ISoundPlayer
{
    /// <summary>Plays a sound file, stopping any sound still playing. Failures are logged, not thrown.</summary>
    Task PlayAsync(string file, float volume, string? outputDeviceId);

    /// <summary>Decodes any supported audio file and writes it as WAV.</summary>
    void ConvertToWav(string source, string target);
}

public interface IStartupRegistration
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);

    /// <summary>Removes autostart entries created by MicSwitch and re-registers under the new name if one existed.</summary>
    void MigrateLegacyEntries();
}

public interface IUpdateService
{
    /// <summary>False for portable or development builds, which can't update themselves.</summary>
    bool IsInstalled { get; }

    /// <returns>The newer version, or <c>null</c> when up to date.</returns>
    Task<string?> CheckAsync();

    Task DownloadAndRestartAsync();
}

/// <summary>Platform-specific window behavior the UI toolkit doesn't provide.</summary>
public interface IWindowInterop
{
    /// <summary>Makes an overlay window ignore mouse input (click-through) and stay out of Alt+Tab.</summary>
    void ConfigureOverlay(nint handle, bool clickThrough);
}

/// <summary>What the current platform can do, so the UI can hide unsupported options.</summary>
public sealed record PlatformCapabilities
{
    /// <summary>Hotkeys can be hidden from other applications.</summary>
    public bool CanSuppressHotkeys { get; init; }

    public bool SupportsMouseHotkeys { get; init; }

    /// <summary>Shown as a warning banner when set (e.g. hotkeys limited without elevation).</summary>
    public string? LimitationNotice { get; init; }
}
