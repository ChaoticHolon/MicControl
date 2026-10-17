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

/// <summary>
/// Minimal file logger so users can attach logs to bug reports: one file per day (MicControlNG-yyyyMMdd.log, named by
/// the date of the lines in it, so a long-running copy starts a new file at midnight), keeping the last 7.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    public const string FilePrefix = "MicControlNG-";
    private const int KeepFiles = 7;

    // Files written before the rename; they count towards KeepFiles so they age out.
    private const string LegacyFilePrefix = "micswitch-";

    private readonly BlockingCollection<(DateTime Time, string Line)> queue = new(4096);
    private readonly LogLevelSwitch levels;
    private readonly Func<DateTime> now;
    private readonly string directory;
    private readonly Thread writer;
    private volatile string filePath;

    public FileLoggerProvider(string directory, LogLevelSwitch levels, Func<DateTime>? clock = null)
    {
        this.levels = levels;
        this.directory = directory;
        now = clock ?? (() => DateTime.Now);
        Directory.CreateDirectory(directory);
        var start = now();
        filePath = PathFor(start);
        Prune();
        writer = new Thread(() => Write(start)) { IsBackground = true, Name = "Log writer" };
        writer.Start();
    }

    /// <summary>The file being written to now.</summary>
    public string FilePath => filePath;

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    /// <summary>Writes everything queued so far and stops; used on exit and before a crash takes the process down.</summary>
    public void Dispose()
    {
        queue.CompleteAdding();
        writer.Join(TimeSpan.FromSeconds(2));
    }

    private string PathFor(DateTime day) => Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"{FilePrefix}{day:yyyyMMdd}.log"));

    private void Prune()
    {
        var old = Directory.GetFiles(directory, FilePrefix + "*.log")
            .Concat(Directory.GetFiles(directory, LegacyFilePrefix + "*.log"))
            .Where(f => !string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(File.GetLastWriteTimeUtc)
            .SkipLast(KeepFiles - 1);
        foreach (var file in old)
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Still open elsewhere (another user's copy can't share this folder, but a viewer might); try next time.
            }
        }
    }

    private void Write(DateTime start)
    {
        var day = start.Date;
        var stream = new StreamWriter(filePath, append: true) { AutoFlush = true };
        try
        {
            foreach (var (time, line) in queue.GetConsumingEnumerable())
            {
                if (time.Date > day)
                {
                    day = time.Date;
                    stream.Dispose();
                    filePath = PathFor(day);
                    stream = new StreamWriter(filePath, append: true) { AutoFlush = true };
                    Prune();
                }

                stream.WriteLine(line);
            }
        }
        finally
        {
            stream.Dispose();
        }
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

            var time = provider.now();
            var line = string.Create(CultureInfo.InvariantCulture, $"{time:HH:mm:ss.fff} [{logLevel}] {shortCategory}: {formatter(state, exception)}");
            try
            {
                provider.queue.TryAdd((time, exception is null ? line : $"{line}{Environment.NewLine}{exception}"));
            }
            catch (InvalidOperationException)
            {
                // The provider was disposed (exiting); late messages are dropped.
            }
        }
    }
}
