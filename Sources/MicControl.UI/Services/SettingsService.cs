using Avalonia.Threading;
using MicControl.Settings;

namespace MicControl.Services;

/// <summary>Holds the live settings and writes them to disk shortly after the last change.</summary>
public sealed class SettingsService
{
    private readonly SettingsStore store;
    private readonly DispatcherTimer saveTimer;

    public SettingsService(SettingsStore store)
    {
        this.store = store;
        Current = store.Load();
        saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        saveTimer.Tick += (_, _) => Flush();
    }

    public AppSettings Current { get; }

    public string Directory => store.Directory;

    /// <summary>True on the first run that imported MicSwitch 1.x settings.</summary>
    public bool MigratedFromLegacy => store.MigratedFromLegacy;

    /// <summary>True when this user had no settings to load or import: first-run defaults apply.</summary>
    public bool IsFirstRun => store.IsFirstRun;

    public void ScheduleSave()
    {
        saveTimer.Stop();
        saveTimer.Start();
    }

    public void Flush()
    {
        saveTimer.Stop();
        store.Save(Current);
    }
}
