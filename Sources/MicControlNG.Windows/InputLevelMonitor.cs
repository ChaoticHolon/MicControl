using System.Runtime.InteropServices;
using MicControlNG.Platform;
using MicControlNG.Settings;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MicControlNG.Windows;

/// <summary>Measures microphone peaks with a shared-mode WASAPI capture stream on a background (MTA) thread.</summary>
public sealed partial class InputLevelMonitor(ILogger<InputLevelMonitor> logger) : IInputLevelMonitor
{
    public IDisposable Start(string? deviceId, Action<float> onPeak)
    {
        var session = new Session(deviceId, onPeak, logger);
        _ = Task.Run(session.Run);
        return session;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Input level monitoring failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    private sealed class Session(string? deviceId, Action<float> onPeak, ILogger logger) : IDisposable
    {
        private readonly CancellationTokenSource stop = new();

        public async Task Run()
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using var device = string.IsNullOrEmpty(deviceId) || deviceId == AppSettings.AllDevices
                    ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
                    : enumerator.GetDevice(deviceId);
                using var capture = new WasapiCapture(device);
                var format = capture.WaveFormat;
                capture.DataAvailable += (_, e) => onPeak(Peak(e.Buffer, e.BytesRecorded, format));
                capture.StartRecording();
                try
                {
                    await Task.Delay(Timeout.Infinite, stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }

                capture.StopRecording();
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
            {
                LogFailed(logger, ex);
            }
        }

        public void Dispose()
        {
            stop.Cancel();
            stop.Dispose();
        }

        private static float Peak(byte[] buffer, int count, WaveFormat format)
        {
            var peak = 0f;
            if (format.Encoding == WaveFormatEncoding.IeeeFloat || (format.Encoding == WaveFormatEncoding.Extensible && format.BitsPerSample == 32))
            {
                var samples = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, count - (count % 4)));
                foreach (var sample in samples)
                {
                    peak = Math.Max(peak, Math.Abs(sample));
                }
            }
            else if (format.BitsPerSample == 16)
            {
                var samples = MemoryMarshal.Cast<byte, short>(buffer.AsSpan(0, count - (count % 2)));
                foreach (var sample in samples)
                {
                    peak = Math.Max(peak, Math.Abs(sample / 32768f));
                }
            }

            return Math.Min(peak, 1);
        }
    }
}
