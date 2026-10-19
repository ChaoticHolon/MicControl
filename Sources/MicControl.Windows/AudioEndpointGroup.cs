using System.Runtime.InteropServices;
using MicControl.Platform;
using MicControl.Settings;
using NAudio.CoreAudioApi;

namespace MicControl.Windows;

/// <summary>
/// Core Audio implementation of <see cref="IAudioEndpointGroup"/>. Every (re)bound device gets the mute state and the
/// kept volume the app last set: a microphone plugged back in mid push-to-talk must not come back live. A kept volume
/// is also restored when something else changes it (Windows' or a voice app's automatic gain).
/// </summary>
internal sealed class AudioEndpointGroup : IAudioEndpointGroup
{
    private readonly AudioDeviceService devices;
    private readonly AudioFlow flow;
    private readonly IUiDispatcher dispatcher;
    private List<MMDevice> endpoints = [];
    // What each device actually holds after the app set the kept volume (a device with coarse volume steps rounds
    // it): only a change away from that is someone else's, so restoring it can never loop.
    private readonly Dictionary<MMDevice, float> keptLevels = [];
    private string deviceId = AppSettings.AllDevices;
    private bool? lastMute;
    private float? lastVolume;

    public AudioEndpointGroup(AudioDeviceService devices, AudioFlow flow, IUiDispatcher dispatcher)
    {
        this.devices = devices;
        this.flow = flow;
        this.dispatcher = dispatcher;
        devices.DevicesChanged += OnDevicesChanged;
    }

    public event EventHandler? StateChanged;

    public string DeviceId
    {
        get => deviceId;
        set
        {
            if (deviceId != value)
            {
                deviceId = value;
                Rebind();
            }
        }
    }

    public bool IsConnected => endpoints.Count > 0;

    public bool? Mute
    {
        get => IsConnected ? endpoints.All(e => Try(() => e.AudioEndpointVolume.Mute, false)) : null;
        set
        {
            if (value is not { } mute)
            {
                return;
            }

            lastMute = mute;
            endpoints.ForEach(e => Try(() => e.AudioEndpointVolume.Mute = mute));
        }
    }

    public float? Volume
    {
        get => endpoints.Count > 0 ? Try(() => (float?)endpoints[0].AudioEndpointVolume.MasterVolumeLevelScalar, null) : null;
        set
        {
            lastVolume = value is { } v ? Math.Clamp(v, 0, 1) : null;
            if (lastVolume is { } volume)
            {
                endpoints.ForEach(e => Keep(e, volume));
            }
        }
    }

    public void ChangeVolume(float delta) =>
        endpoints.ForEach(e => Try(() => e.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(e.AudioEndpointVolume.MasterVolumeLevelScalar + delta, 0, 1)));

    public void Rebind()
    {
        Release();
        foreach (var device in devices.GetActiveDevices(flow))
        {
            if (deviceId == AppSettings.AllDevices || Try(() => device.ID, (string?)null) == deviceId)
            {
                endpoints.Add(device);
            }
            else
            {
                device.Dispose();
            }
        }

        foreach (var endpoint in endpoints)
        {
            Try(() => endpoint.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification);
            if (lastMute is { } mute)
            {
                Try(() => endpoint.AudioEndpointVolume.Mute = mute);
            }

            if (lastVolume is { } volume)
            {
                Keep(endpoint, volume);
            }
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        devices.DevicesChanged -= OnDevicesChanged;
        Release();
    }

    private void OnDevicesChanged(object? sender, EventArgs e) => Rebind();

    // Arrives on a COM worker thread.
    private void OnVolumeNotification(AudioVolumeNotificationData data) =>
        dispatcher.Post(() =>
        {
            RestoreKeptVolume();
            StateChanged?.Invoke(this, EventArgs.Empty);
        });

    private void RestoreKeptVolume()
    {
        if (lastVolume is not { } volume)
        {
            return;
        }

        foreach (var endpoint in endpoints)
        {
            var current = Try(() => (float?)endpoint.AudioEndpointVolume.MasterVolumeLevelScalar, null);
            if (current is { } level && (!keptLevels.TryGetValue(endpoint, out var kept) || Math.Abs(level - kept) > 0.001f))
            {
                Keep(endpoint, volume);
            }
        }
    }

    private void Keep(MMDevice endpoint, float volume) => Try(() =>
    {
        endpoint.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
        keptLevels[endpoint] = endpoint.AudioEndpointVolume.MasterVolumeLevelScalar;
    });

    private void Release()
    {
        foreach (var endpoint in endpoints)
        {
            Try(() => endpoint.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification);
            endpoint.Dispose();
        }

        endpoints = [];
        keptLevels.Clear();
    }

    // Devices can disappear at any moment; calls on a removed device fail and are ignored. Besides COMException
    // (e.g. AUDCLNT_E_DEVICE_INVALIDATED), a driver can answer E_ACCESSDENIED or E_INVALIDARG, which .NET turns into
    // other exception types: none of them may crash the app from a device notification.
    private static bool IsDeviceFailure(Exception ex) =>
        ex is ExternalException or InvalidComObjectException or UnauthorizedAccessException or ArgumentException or InvalidCastException;

    private static void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (IsDeviceFailure(ex))
        {
        }
    }

    private static T Try<T>(Func<T> func, T fallback)
    {
        try
        {
            return func();
        }
        catch (Exception ex) when (IsDeviceFailure(ex))
        {
            return fallback;
        }
    }
}
