using MicControlNG.Platform;
using MicControlNG.Services;
using MicControlNG.Settings;

namespace MicControlNG.Tests.Ui;

internal sealed class FakeAudioDevices : IAudioDevices
{
    public List<AudioDeviceInfo> Microphones { get; } = [new("mic-1", "USB Microphone")];

    public List<AudioDeviceInfo> Speakers { get; } = [new("spk-1", "Speakers")];

    public FakeEndpointGroup Microphone { get; } = new();

    public FakeEndpointGroup Speaker { get; } = new();

    public event EventHandler? DevicesChanged;

    public IReadOnlyList<AudioDeviceInfo> GetDevices(AudioFlow flow) => flow == AudioFlow.Capture ? Microphones : Speakers;

    public IAudioEndpointGroup CreateGroup(AudioFlow flow) => flow == AudioFlow.Capture ? Microphone : Speaker;

    public void RaiseDevicesChanged() => DevicesChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class FakeEndpointGroup : IAudioEndpointGroup
{
    private bool mute;
    private float volume = 0.5f;

    public event EventHandler? StateChanged;

    public string DeviceId { get; set; } = AppSettings.AllDevices;

    public bool IsConnected { get; set; } = true;

    public bool? Mute
    {
        get => IsConnected ? mute : null;
        set
        {
            if (value is { } v && v != mute)
            {
                mute = v;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public float? Volume
    {
        get => IsConnected ? volume : null;
        set
        {
            if (value is { } v)
            {
                volume = Math.Clamp(v, 0, 1);
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public void Rebind() => StateChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
    }
}

internal sealed class FakeHotkeys : IGlobalHotkeys
{
    private readonly List<(HotkeySettings Settings, Action<bool> Handler)> registrations = [];

    public bool IsPaused { get; set; }

    public IDisposable Register(HotkeySettings settings, Action<bool> handler)
    {
        var entry = (settings, handler);
        registrations.Add(entry);
        return new Unregister(() => registrations.Remove(entry));
    }

    /// <summary>Simulates pressing or releasing every registered hotkey bound to these settings.</summary>
    public void Trigger(HotkeySettings settings, bool pressed) =>
        registrations.Where(r => ReferenceEquals(r.Settings, settings)).ToList().ForEach(r => r.Handler(pressed));

    private sealed class Unregister(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}

internal sealed class FakeSoundPlayer : ISoundPlayer
{
    public List<string> Played { get; } = [];

    public Task PlayAsync(string file, float volume, string? outputDeviceId)
    {
        Played.Add(Path.GetFileNameWithoutExtension(file));
        return Task.CompletedTask;
    }

    public void ConvertToWav(string source, string target) => File.Copy(source, target, overwrite: true);
}

internal sealed class FakeStartup : IStartupRegistration
{
    public bool IsEnabled { get; private set; }

    public void SetEnabled(bool enabled) => IsEnabled = enabled;

    public void MigrateLegacyEntries()
    {
    }
}

internal sealed class FakeInstaller : IInstaller
{
    public bool IsElevated { get; set; }

    public bool AcceptElevation { get; set; } = true;

    public InstalledCopy? Current { get; set; }

    public Dictionary<Installation.InstallScope, InstalledCopy> Installed { get; } = [];

    public Exception? Failure { get; set; }

    public List<Installation.SetupCommand> ElevatedRestarts { get; } = [];

    public List<Installation.InstallScope> Installs { get; } = [];

    public List<Installation.InstallScope> Launches { get; } = [];

    public int Uninstalls { get; private set; }

    public InstalledCopy? Find(Installation.InstallScope scope) => Installed.GetValueOrDefault(scope);

    public string DirectoryFor(Installation.InstallScope scope) =>
        scope == Installation.InstallScope.AllUsers ? @"C:\Program Files\MicControlNG" : @"C:\Users\me\AppData\Local\Programs\MicControlNG";

    public bool RestartElevated(Installation.SetupCommand command)
    {
        ElevatedRestarts.Add(command);
        return AcceptElevation;
    }

    public Task InstallAsync(Installation.InstallScope scope, IProgress<double> progress, CancellationToken cancellationToken)
    {
        Installs.Add(scope);
        return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
    }

    public Task UninstallAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        Uninstalls++;
        return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
    }

    public void Launch(Installation.InstallScope scope) => Launches.Add(scope);
}

internal sealed class FakeUpdates : IUpdateService
{
    public bool IsInstalled { get; set; }

    public string? Available { get; set; }

    public bool InstallerStarts { get; set; } = true;

    public int Installs { get; private set; }

    public Task<string?> CheckAsync() => Task.FromResult(IsInstalled ? Available : null);

    public Task<bool> InstallAsync()
    {
        Installs++;
        return Task.FromResult(InstallerStarts);
    }
}

internal sealed class FakeDialogs : IDialogService
{
    public List<string> Opened { get; } = [];

    public Task<string?> PickFileAsync(string title, string filterName, IReadOnlyList<string> patterns) => Task.FromResult<string?>(null);

    public Task OpenAsync(string target)
    {
        Opened.Add(target);
        return Task.CompletedTask;
    }
}

internal sealed class FakeLevelMonitor : IInputLevelMonitor
{
    private Action<float>? onPeak;

    public int ActiveSessions { get; private set; }

    public IDisposable Start(string? deviceId, Action<float> onPeak)
    {
        this.onPeak = onPeak;
        ActiveSessions++;
        return new Stop(() => ActiveSessions--);
    }

    public void Report(float peak) => onPeak?.Invoke(peak);

    private sealed class Stop(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}

internal sealed class FakeWindowInterop : IWindowInterop
{
    public void ConfigureOverlay(nint handle, bool clickThrough)
    {
    }

    public List<(string Window, Func<WindowLifecycleMessage, bool> Handler)> Watched { get; } = [];

    public IDisposable WatchLifecycle(nint handle, string window, Func<WindowLifecycleMessage, bool> handler)
    {
        Watched.Add((window, handler));
        return new Watch(() => Watched.RemoveAll(w => w.Handler == handler));
    }

    public IReadOnlyList<nint> FindToolkitHelperWindows() => [];

    private sealed class Watch(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
