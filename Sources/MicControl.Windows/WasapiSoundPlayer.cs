using System.Runtime.InteropServices;
using MicControl.Platform;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MicControl.Windows;

/// <summary>Plays notification sounds through WASAPI and converts files with Media Foundation.</summary>
public sealed partial class WasapiSoundPlayer(ILogger<WasapiSoundPlayer> logger) : ISoundPlayer
{
    private CancellationTokenSource? current;

    public void ConvertToWav(string source, string target)
    {
        using var reader = new MediaFoundationReader(source);
        WaveFileWriter.CreateWaveFile(target, reader);
    }

    public Task PlayAsync(string file, float volume, string? outputDeviceId)
    {
        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref current, cts)?.Cancel();
        return Task.Run(() => Play(file, volume, outputDeviceId, cts.Token), cts.Token);
    }
    private async Task Play(string file, float volume, string? outputDeviceId, CancellationToken token)
    {
        try
        {
            // Runs on a pool (MTA) thread with its own enumerator so COM objects never cross apartments.
            using var enumerator = new MMDeviceEnumerator();
            using var device = ChosenDevice(enumerator, outputDeviceId) ?? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            // WAV directly, anything else (e.g. an MP3 in the library) through Media Foundation.
            using WaveStream reader = string.Equals(Path.GetExtension(file), ".wav", StringComparison.OrdinalIgnoreCase)
                ? new WaveFileReader(file)
                : new MediaFoundationReader(file);
            var samples = new SampleChannel(reader) { Volume = Math.Clamp(volume, 0, 1) };
            using var output = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: 100);
            var stopped = new TaskCompletionSource();
            output.PlaybackStopped += (_, _) => stopped.TrySetResult();
            output.Init(samples);
            output.Play();
            await using (token.Register(output.Stop))
            {
                await stopped.Task.ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is COMException or IOException or InvalidOperationException or FormatException)
        {
            LogPlaybackFailed(ex, file);
        }
    }

    /// <summary>
    /// The chosen playback device, or <c>null</c> (play on the default one) when it's unplugged or gone, so a
    /// disconnected headset doesn't silence the cues.
    /// </summary>
    private MMDevice? ChosenDevice(MMDeviceEnumerator enumerator, string? outputDeviceId)
    {
        if (outputDeviceId is null)
        {
            return null;
        }

        try
        {
            var device = enumerator.GetDevice(outputDeviceId);
            if (device.State == DeviceState.Active)
            {
                return device;
            }

            device.Dispose();
        }
        catch (COMException)
        {
        }

        LogDeviceUnavailable(outputDeviceId);
        return null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sound device {DeviceId} isn't connected; playing on the default device")]
    private partial void LogDeviceUnavailable(string deviceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to play notification {File}")]
    private partial void LogPlaybackFailed(Exception exception, string file);
}
