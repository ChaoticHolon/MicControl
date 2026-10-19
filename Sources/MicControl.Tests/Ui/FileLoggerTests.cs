using Avalonia.Headless.XUnit;
using MicControl.Services;
using MicControl.Settings;
using MicControl.ViewModels;
using Microsoft.Extensions.Logging;

namespace MicControl.Tests.Ui;

public sealed class FileLoggerTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("miccontrol-logs").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void WritesToMicControlFile_AndFollowsTheLevelSwitch()
    {
        var levels = new LogLevelSwitch { Minimum = LogLevel.Information };
        var provider = new FileLoggerProvider(directory, levels);
        var logger = provider.CreateLogger("MicControl.Tests.Sample");

        Write(logger, LogLevel.Debug, "hidden at information");
        Write(logger, LogLevel.Information, "shown at information");
        levels.Minimum = LogLevel.Debug;
        Write(logger, LogLevel.Debug, "shown at debug");
        Assert.False(logger.IsEnabled(LogLevel.Trace));
        provider.Dispose();

        Assert.Equal($"MicControl-{DateTime.Now:yyyyMMdd}.log", Path.GetFileName(provider.FilePath));
        var text = File.ReadAllText(provider.FilePath);
        Assert.DoesNotContain("hidden at information", text, StringComparison.Ordinal);
        Assert.Contains("[Information] Sample: shown at information", text, StringComparison.Ordinal);
        Assert.Contains("[Debug] Sample: shown at debug", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Flush_WritesEverythingQueued_AndKeepsLogging()
    {
        var provider = new FileLoggerProvider(directory, new LogLevelSwitch());
        var logger = provider.CreateLogger("MicControl.Tests.Sample");
        for (var i = 0; i < 100; i++)
        {
            Write(logger, LogLevel.Information, $"line {i}");
        }

        Assert.True(provider.Flush(TimeSpan.FromSeconds(5)));
        Assert.Contains("line 99", ReadShared(provider.FilePath), StringComparison.Ordinal);

        // A session end flushes, then the app still logs its last lines.
        Write(logger, LogLevel.Information, "after the flush");
        provider.Dispose();
        Assert.Contains("after the flush", File.ReadAllText(provider.FilePath), StringComparison.Ordinal);
    }

    [Fact]
    public void TwoCopies_ShareTheDaysFile()
    {
        // The setup (or a second copy) logs to the same folder while the app runs; a locked file made it crash.
        var app = new FileLoggerProvider(directory, new LogLevelSwitch());
        var setup = new FileLoggerProvider(directory, new LogLevelSwitch());
        Write(app.CreateLogger("MicControl.App"), LogLevel.Information, "from the app");
        Write(setup.CreateLogger("MicControl.Setup"), LogLevel.Information, "from the setup");
        setup.Dispose();
        app.Dispose();

        var text = File.ReadAllText(app.FilePath);
        Assert.Contains("App: from the app", text, StringComparison.Ordinal);
        Assert.Contains("Setup: from the setup", text, StringComparison.Ordinal);
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void DefaultLevel_IsDebug()
    {
        Assert.Equal(LogLevel.Debug, new LogLevelSwitch().Minimum);
        Assert.Equal(LogDetail.Debug, new AppSettings().LogLevel);
    }

    [Fact]
    public void KeepsTheLastSevenFiles()
    {
        // Ten older files, oldest first.
        var files = Enumerable.Range(1, 10)
            .Select(i => Path.Combine(directory, $"MicControl-202609{i:00}.log"))
            .ToList();
        for (var i = 0; i < files.Count; i++)
        {
            File.WriteAllText(files[i], "old");
            File.SetLastWriteTimeUtc(files[i], DateTime.UtcNow.AddDays(-20 + i));
        }

        new FileLoggerProvider(directory, new LogLevelSwitch()).Dispose();

        // Today's file counts as one of the seven.
        Assert.All(files.Take(4), f => Assert.False(File.Exists(f), $"{Path.GetFileName(f)} should have been deleted"));
        Assert.All(files.Skip(4), f => Assert.True(File.Exists(f), $"{Path.GetFileName(f)} should have been kept"));
    }

    [Fact]
    public void StartsANewFileAtMidnight()
    {
        var now = new DateTime(2026, 10, 17, 23, 59, 59, DateTimeKind.Local);
        var provider = new FileLoggerProvider(directory, new LogLevelSwitch(), () => now);
        var logger = provider.CreateLogger("MicControl.Tests.Sample");

        Write(logger, LogLevel.Information, "before midnight");
        now = now.AddSeconds(2);
        Write(logger, LogLevel.Information, "after midnight");
        provider.Dispose();

        var first = File.ReadAllText(Path.Combine(directory, "MicControl-20261017.log"));
        var second = File.ReadAllText(Path.Combine(directory, "MicControl-20261018.log"));
        Assert.Contains("before midnight", first, StringComparison.Ordinal);
        Assert.DoesNotContain("after midnight", first, StringComparison.Ordinal);
        Assert.Contains("00:00:01.000 [Information] Sample: after midnight", second, StringComparison.Ordinal);
        Assert.Equal("MicControl-20261018.log", Path.GetFileName(provider.FilePath));
    }

    [Fact]
    public void LogLevelSetting_RoundTrips_AndOlderFilesDefaultToDebug()
    {
        var store = new SettingsStore(directory);
        File.WriteAllText(store.FilePath, """{ "version": 4 }""");
        Assert.Equal(LogDetail.Debug, store.Load().LogLevel);

        var settings = new AppSettings { LogLevel = LogDetail.Warning };
        store.Save(settings);

        Assert.Equal(LogDetail.Warning, store.Load().LogLevel);
        Assert.Contains("\"logLevel\": \"Warning\"", File.ReadAllText(store.FilePath), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void LogDetail_ChangesTheSettingAndNotifies()
    {
        using var harness = new TestHarness();
        var changed = new List<string?>();
        harness.ViewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        harness.ViewModel.LogDetail = LogDetail.Trace;

        Assert.Equal(LogDetail.Trace, harness.Settings.Current.LogLevel);
        Assert.Contains(nameof(MainViewModel.LogDetail), changed);
        Assert.Equal(5, harness.ViewModel.LogLevels.Count);
    }

    [Fact]
    public void LogsFolderCantBeCreated_StillStarts_AndDropsLines()
    {
        // A file where the folder goes (or a redirected AppData that's offline) used to stop every start.
        var path = Path.Combine(directory, "logs");
        File.WriteAllText(path, "not a folder");
        using var provider = new FileLoggerProvider(path, new LogLevelSwitch());
        Write(provider.CreateLogger("MicControl.Tests.Sample"), LogLevel.Information, "dropped");
        Assert.True(provider.Flush(TimeSpan.FromSeconds(5)));
        Assert.Equal("not a folder", File.ReadAllText(path));
    }

    private static void Write(ILogger logger, LogLevel level, string message) => logger.Log(level, default, message, null, (s, _) => s);
}
