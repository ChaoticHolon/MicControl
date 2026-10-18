using MicControl.Settings;

namespace MicControl.Platform;

// Contracts implemented per operating system (MicControl.Windows today, a Linux backend later).
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

    /// <summary>
    /// Replaces MicSwitch autostart entries whose program is gone with MicControl ones.
    /// Entries for a MicSwitch that is still installed are left alone.
    /// </summary>
    void MigrateLegacyEntries();
}

/// <summary>An installed copy: its scope, folder and version (from Settings → Apps).</summary>
public sealed record InstalledCopy(Installation.InstallScope Scope, string Directory, string? Version);

/// <summary>
/// Installs and uninstalls this program (the setup file is the app itself; see <see cref="Installation.SetupCommand"/>).
/// Windows-only for now: Linux and macOS use their package managers.
/// </summary>
public interface IInstaller
{
    /// <summary>Whether this process may write to Program Files and HKLM.</summary>
    bool IsElevated { get; }

    /// <summary>The installed copy this process is running from, if any.</summary>
    InstalledCopy? Current { get; }

    /// <summary>The copy installed in a scope, if any.</summary>
    InstalledCopy? Find(Installation.InstallScope scope);

    /// <summary>The folder a scope installs to.</summary>
    string DirectoryFor(Installation.InstallScope scope);

    /// <summary>Starts this program again as administrator with the given command; false if the user declined.</summary>
    bool RestartElevated(Installation.SetupCommand command);

    /// <summary>
    /// Closes any running copy in the target folder, then copies this program there, registers it in Settings → Apps
    /// and adds a Start menu shortcut. Progress runs from 0 to 1.
    /// </summary>
    Task InstallAsync(Installation.InstallScope scope, IProgress<double> progress, CancellationToken cancellationToken);

    /// <summary>
    /// Removes <see cref="Current"/>: closes it everywhere, removes "Start with Windows", the shortcut and the Settings →
    /// Apps entry, then deletes the program folder once this process has exited. Each user's settings are kept.
    /// </summary>
    Task UninstallAsync(IProgress<double> progress, CancellationToken cancellationToken);

    /// <summary>Starts the installed copy of a scope.</summary>
    void Launch(Installation.InstallScope scope);
}

public interface IUpdateService
{
    /// <summary>False for portable or development builds, which can't update themselves.</summary>
    bool IsInstalled { get; }

    /// <returns>The newer version, or <c>null</c> when up to date.</returns>
    Task<string?> CheckAsync();

    /// <summary>
    /// Downloads the update found by <see cref="CheckAsync"/>, verifies it and starts the installer.
    /// Returns true when the installer is running: the app must then exit so its files can be replaced
    /// (the installer starts it again afterwards).
    /// </summary>
    Task<bool> InstallAsync();
}

/// <summary>Platform-specific window behavior the UI toolkit doesn't provide.</summary>
public interface IWindowInterop
{
    /// <summary>Makes an overlay window ignore mouse input (click-through) and stay out of Alt+Tab.</summary>
    void ConfigureOverlay(nint handle, bool clickThrough);

    /// <summary>
    /// Reports close and session-end messages a window receives (close requests, session ending, destruction).
    /// The handler runs on the window's thread; returning <c>true</c> handles the message so the window's own code
    /// never sees it: a close request is refused, and a session-end query is answered "yes, the session may end".
    /// The window may belong to another thread of this process; the handler then runs on that thread.
    /// Dispose the result to stop watching. Returns <c>null</c> if the window can't be watched (already watched,
    /// gone, or the OS refused).
    /// </summary>
    IDisposable? WatchLifecycle(nint handle, string window, Func<WindowLifecycleMessage, bool> handler);

    /// <summary>
    /// Hidden helper windows of this process, on any thread, that the app needs but didn't create itself: Avalonia's
    /// dispatcher and tray icon window (if it's closed, the UI stops processing work while the process stays alive),
    /// the hidden owner of windows kept off the taskbar (closing it destroys the overlay), and the
    /// input method windows.
    /// </summary>
    IReadOnlyList<ToolkitHelperWindow> FindToolkitHelperWindows();
}

/// <summary>A hidden helper window, with a name for the log (e.g. "UI message window").</summary>
/// <param name="OnUiThread">False for a window on another thread: its messages are handled on that thread.</param>
public sealed record ToolkitHelperWindow(nint Handle, string Name, int ThreadId, bool OnUiThread);

public enum WindowLifecycleKind
{
    /// <summary>A request to close the window (e.g. WM_CLOSE, or Close from the system menu).</summary>
    CloseRequest,

    /// <summary>The OS asks whether the session may end (shutdown, restart, sign-out, or an app restart request).</summary>
    SessionEndQuery,

    /// <summary>The session is ending now: the OS ends the process soon after this message returns.</summary>
    SessionEnd,

    /// <summary>A session end that was asked about was cancelled (e.g. another app vetoed it); the session goes on.</summary>
    SessionEndCancelled,

    /// <summary>The window is being destroyed.</summary>
    Destroyed,
}

/// <summary>A lifecycle message a window received, with the context needed to tell who sent it.</summary>
/// <param name="Window">Which of our windows received it.</param>
/// <param name="Message">The raw message name, e.g. "WM_CLOSE".</param>
/// <param name="Details">Message-specific flags, e.g. "restart or shutdown", "close app (Restart Manager)".</param>
/// <param name="Origin">"sent from another thread or process", "posted or sent by this thread", …</param>
/// <param name="ForegroundProcess">The process owning the foreground window at that moment, if known.</param>
public sealed record WindowLifecycleMessage(
    WindowLifecycleKind Kind, string Window, string Message, string Details, string Origin, string? ForegroundProcess);

/// <summary>What the current platform can do, so the UI can hide unsupported options.</summary>
public sealed record PlatformCapabilities
{
    /// <summary>Hotkeys can be hidden from other applications.</summary>
    public bool CanSuppressHotkeys { get; init; }

    public bool SupportsMouseHotkeys { get; init; }

    /// <summary>Shown as a warning banner when set (e.g. hotkeys limited without elevation).</summary>
    public string? LimitationNotice { get; init; }
}
