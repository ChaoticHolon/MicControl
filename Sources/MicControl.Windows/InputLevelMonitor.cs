using System.Runtime.InteropServices;
using MicControl.Platform;
using MicControl.Settings;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MicControl.Windows;

/// <summary>
/// Measures microphone peaks on a background (MTA) thread: with a shared-mode WASAPI capture stream, or by polling
/// the endpoint's peak meter (<c>IAudioMeterInformation</c>), which opens no stream and so isn't "microphone in use".
/// </summary>
public sealed partial class InputLevelMonitor(ILogger<InputLevelMonitor> logger) : IInputLevelMonitor
{
    private static readonly TimeSpan MeterInterval = TimeSpan.FromMilliseconds(33);

    public IDisposable Start(string? deviceId, bool openDevice, Action<string, float> onPeak)
    {
        var session = new Session(deviceId, onPeak, logger);
        _ = Task.Run(openDevice ? session.Record : session.ReadMeter);
        return session;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Input level monitoring failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    private sealed class Session(string? deviceId, Action<string, float> onPeak, ILogger logger) : IDisposable
    {
        private readonly CancellationTokenSource stop = new();

        public async Task ReadMeter()
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var devices = GetDevices(enumerator);
                try
                {
                    var meters = devices.Select(d => (d.ID, Meter: d.AudioMeterInformation)).ToList();
                    using var timer = new PeriodicTimer(MeterInterval);
                    while (await timer.WaitForNextTickAsync(stop.Token).ConfigureAwait(false))
                    {
                        foreach (var (id, meter) in meters)
                        {
                            onPeak(id, Math.Clamp(meter.MasterPeakValue, 0, 1));
                        }
                    }
                }
                finally
                {
                    devices.ForEach(d => d.Dispose());
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                // Stopped (possibly before this started).
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
            {
                LogFailed(logger, ex);
            }
        }

        public async Task Record()
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var devices = GetDevices(enumerator);
                var captures = new List<WasapiCapture>();
                try
                {
                    // One stream per microphone. A device that fails to start is skipped.
                    foreach (var device in devices)
                    {
                        var id = device.ID;
                        try
                        {
                            var capture = new WasapiCapture(device);
                            captures.Add(capture);
                            var format = capture.WaveFormat;
                            capture.DataAvailable += (_, e) => onPeak(id, Peak(e.Buffer, e.BytesRecorded, format));
                            capture.RecordingStopped += (_, _) => onPeak(id, 0);
                            capture.StartRecording();
                        }
                        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
                        {
                            LogFailed(logger, ex);
                        }
                    }

                    try
                    {
                        await Task.Delay(Timeout.Infinite, stop.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
                    {
                    }

                    foreach (var capture in captures)
                    {
                        capture.StopRecording();
                    }
                }
                finally
                {
                    captures.ForEach(c => c.Dispose());
                    devices.ForEach(d => d.Dispose());
                }
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
            {
                LogFailed(logger, ex);
            }
        }

        /// <summary>"All microphones" is every active one; otherwise the chosen device, or the default communications microphone.</summary>
        private List<MMDevice> GetDevices(MMDeviceEnumerator enumerator) => deviceId switch
        {
            AppSettings.AllDevices => [.. enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)],
            null or "" => [enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)],
            _ => [enumerator.GetDevice(deviceId)],
        };

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
