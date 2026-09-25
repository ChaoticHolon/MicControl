using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace MicControlNG.Services;

/// <summary>Minimal daily-rolling file logger (keeps the last 7 files) so users can attach logs to bug reports.</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly BlockingCollection<string> queue = new(1024);
    private readonly Thread writer;

    public FileLoggerProvider(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var old in Directory.GetFiles(directory, "micswitch-*.log").Order().SkipLast(7))
        {
            File.Delete(old);
        }

        var path = Path.Combine(directory, $"micswitch-{DateTime.Now:yyyyMMdd}.log");
        writer = new Thread(() =>
        {
            using var stream = new StreamWriter(path, append: true) { AutoFlush = true };
            foreach (var line in queue.GetConsumingEnumerable())
            {
                stream.WriteLine(line);
            }
        }) { IsBackground = true, Name = "Log writer" };
        writer.Start();
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        queue.CompleteAdding();
        writer.Join(TimeSpan.FromSeconds(2));
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        private readonly string shortCategory = category[(category.LastIndexOf('.') + 1)..];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:HH:mm:ss.fff} [{logLevel}] {shortCategory}: {formatter(state, exception)}");
            if (!provider.queue.IsAddingCompleted)
            {
                provider.queue.TryAdd(exception is null ? line : $"{line}{Environment.NewLine}{exception}");
            }
        }
    }
}
