using System.Runtime.InteropServices;
using MicControl.Platform;
using MicControl.Settings;
using NAudio.CoreAudioApi;

namespace MicControl.Windows;

/// <summary>
/// Core Audio implementation of <see cref="IAudioEndpointGroup"/>. Every (re)bound device gets the mute state and the
/// kept volume the app last set: a microphone plugged back in mid push-to-talk must not come back live.
/// </summary>
internal sealed class AudioEndpointGroup : IAudioEndpointGroup
{
    private readonly AudioDeviceService devices;
    private readonly AudioFlow flow;
    private readonly IUiDispatcher dispatcher;
    private List<MMDevice> endpoints = [];
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
                endpoints.ForEach(e => Try(() => e.AudioEndpointVolume.MasterVolumeLevelScalar = volume));
            }
        }
    }

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
                Try(() => endpoint.AudioEndpointVolume.MasterVolumeLevelScalar = volume);
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
        dispatcher.Post(() => StateChanged?.Invoke(this, EventArgs.Empty));

    private void Release()
    {
        foreach (var endpoint in endpoints)
        {
            Try(() => endpoint.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification);
            endpoint.Dispose();
        }

        endpoints = [];
    }

    // Devices can disappear at any moment; COM calls on a removed device fail and are ignored.
    private static void Try(Action action)
    {
        try
        {
            action();
        }
        catch (COMException)
        {
        }
    }

    private static T Try<T>(Func<T> func, T fallback)
    {
        try
        {
            return func();
        }
        catch (COMException)
        {
            return fallback;
        }
    }
}
