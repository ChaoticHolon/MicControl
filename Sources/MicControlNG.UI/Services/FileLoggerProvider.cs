using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace MicControlNG.Services;

/// <summary>The log file's minimum level; changing it applies immediately (Settings → About → Log detail).</summary>
public sealed class LogLevelSwitch
{
    private volatile int minimum = (int)LogLevel.Debug;

    public LogLevel Minimum
    {
        get => (LogLevel)minimum;
        set => minimum = (int)value;
    }
}

/// <summary>Minimal daily-rolling file logger (keeps the last 7 files) so users can attach logs to bug reports.</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    public const string FilePrefix = "MicControlNG-";
    private const int KeepFiles = 7;

    // Files written before the rename; they count towards KeepFiles so they age out.
    private const string LegacyFilePrefix = "micswitch-";

    private readonly BlockingCollection<string> queue = new(4096);
    private readonly LogLevelSwitch levels;
    private readonly Thread writer;

    public FileLoggerProvider(string directory, LogLevelSwitch levels)
    {
        this.levels = levels;
        Directory.CreateDirectory(directory);
        var old = Directory.GetFiles(directory, FilePrefix + "*.log")
            .Concat(Directory.GetFiles(directory, LegacyFilePrefix + "*.log"))
            .OrderBy(File.GetLastWriteTimeUtc)
            .SkipLast(KeepFiles);
        foreach (var file in old)
        {
            File.Delete(file);
        }

        FilePath = Path.Combine(directory, $"{FilePrefix}{DateTime.Now:yyyyMMdd}.log");
        writer = new Thread(() =>
        {
            using var stream = new StreamWriter(FilePath, append: true) { AutoFlush = true };
            foreach (var line in queue.GetConsumingEnumerable())
            {
                stream.WriteLine(line);
            }
        }) { IsBackground = true, Name = "Log writer" };
        writer.Start();
    }

    public string FilePath { get; }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    /// <summary>Writes everything queued so far and stops; used on exit and before a crash takes the process down.</summary>
    public void Dispose()
    {
        queue.CompleteAdding();
        writer.Join(TimeSpan.FromSeconds(2));
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        private readonly string shortCategory = category[(category.LastIndexOf('.') + 1)..];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= provider.levels.Minimum;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:HH:mm:ss.fff} [{logLevel}] {shortCategory}: {formatter(state, exception)}");
            try
            {
                provider.queue.TryAdd(exception is null ? line : $"{line}{Environment.NewLine}{exception}");
            }
            catch (InvalidOperationException)
            {
                // The provider was disposed (exiting); late messages are dropped.
            }
        }
    }
}
