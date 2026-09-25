using System.Runtime.InteropServices;
using MicControlNG.Platform;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MicControlNG.Windows;

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
            using var device = outputDeviceId is null
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                : enumerator.GetDevice(outputDeviceId);
            using var reader = new AudioFileReader(file) { Volume = Math.Clamp(volume, 0, 1) };
            using var output = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: 100);
            var stopped = new TaskCompletionSource();
            output.PlaybackStopped += (_, _) => stopped.TrySetResult();
            output.Init(reader);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to play notification {File}")]
    private partial void LogPlaybackFailed(Exception exception, string file);
}
