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

    /// <summary>Why the settings file couldn't be read (locked or not readable): defaults apply and the file is left alone.</summary>
    public Exception? LoadError => store.LoadError;

    public void ScheduleSave()
    {
        saveTimer.Stop();
        saveTimer.Start();
    }

    /// <summary>The last save's error (the file locked by a backup or antivirus tool, a full disk); <c>null</c> once a save works.</summary>
    public Exception? LastSaveError { get; private set; }

    /// <summary>Writes the settings now. A failed write is tried again shortly; the settings stay as they are in memory.</summary>
    /// <returns><c>false</c> if the file couldn't be written.</returns>
    public bool Flush()
    {
        saveTimer.Stop();
        try
        {
            store.Save(Current);
            LastSaveError = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastSaveError = ex;
            saveTimer.Start();
            return false;
        }
    }
}
