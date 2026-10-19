using System.Runtime.InteropServices;
using MicControl.Platform;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MicControl.Windows;

/// <summary>Enumerates Core Audio endpoints and reports device changes. Must be created and used on the UI (STA) thread.</summary>
public sealed partial class AudioDeviceService : IAudioDevices, IMMNotificationClient, IDisposable
{
    private readonly IUiDispatcher dispatcher;
    private readonly ILogger logger;
    private readonly MMDeviceEnumerator enumerator = new();
    private int refreshQueued;

    public AudioDeviceService(IUiDispatcher dispatcher, ILogger<AudioDeviceService> logger)
    {
        this.dispatcher = dispatcher;
        this.logger = logger;
        enumerator.RegisterEndpointNotificationCallback(this);
    }

    public event EventHandler? DevicesChanged;

    public IReadOnlyList<AudioDeviceInfo> GetDevices(AudioFlow flow) =>
        [.. GetActiveDevices(flow).Select(d =>
        {
            using (d)
            {
                try
                {
                    return new AudioDeviceInfo(d.ID, d.FriendlyName);
                }
                catch (COMException)
                {
                    return null; // removed while the list was read
                }
            }
        }).OfType<AudioDeviceInfo>()];

    public IAudioEndpointGroup CreateGroup(AudioFlow flow) => new AudioEndpointGroup(this, flow, dispatcher);

    internal IReadOnlyList<MMDevice> GetActiveDevices(AudioFlow flow)
    {
        try
        {
            return [.. enumerator.EnumerateAudioEndPoints(flow == AudioFlow.Capture ? DataFlow.Capture : DataFlow.Render, DeviceState.Active)];
        }
        catch (COMException ex)
        {
            LogEnumerationFailed(flow, ex);
            return [];
        }
    }

    public void Dispose()
    {
        enumerator.UnregisterEndpointNotificationCallback(this);
        enumerator.Dispose();
    }

    private void QueueRefresh()
    {
        // Callbacks arrive on a COM worker thread; bursts of them are collapsed into one UI update.
        if (Interlocked.Exchange(ref refreshQueued, 1) == 1)
        {
            return;
        }

        dispatcher.Post(() =>
        {
            Volatile.Write(ref refreshQueued, 0);
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        LogDeviceStateChanged(deviceId, newState);
        QueueRefresh();
    }

    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId)
    {
        LogDeviceAdded(pwstrDeviceId);
        QueueRefresh();
    }

    void IMMNotificationClient.OnDeviceRemoved(string deviceId)
    {
        LogDeviceRemoved(deviceId);
        QueueRefresh();
    }

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) =>
        LogDefaultDeviceChanged(flow, role, defaultDeviceId);

    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not list {Flow} devices")]
    private partial void LogEnumerationFailed(AudioFlow flow, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Audio device {DeviceId} is now {State}")]
    private partial void LogDeviceStateChanged(string deviceId, DeviceState state);

    [LoggerMessage(Level = LogLevel.Information, Message = "Audio device added: {DeviceId}")]
    private partial void LogDeviceAdded(string deviceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Audio device removed: {DeviceId}")]
    private partial void LogDeviceRemoved(string deviceId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Default {Flow} device for {Role} is now {DeviceId}")]
    private partial void LogDefaultDeviceChanged(DataFlow flow, Role role, string deviceId);
}
