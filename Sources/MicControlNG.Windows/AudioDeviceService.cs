using System.Runtime.InteropServices;
using MicControlNG.Platform;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MicControlNG.Windows;

/// <summary>Enumerates Core Audio endpoints and reports device changes. Must be created and used on the UI (STA) thread.</summary>
public sealed class AudioDeviceService : IAudioDevices, IMMNotificationClient, IDisposable
{
    private readonly IUiDispatcher dispatcher;
    private readonly MMDeviceEnumerator enumerator = new();
    private int refreshQueued;

    public AudioDeviceService(IUiDispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        enumerator.RegisterEndpointNotificationCallback(this);
    }

    public event EventHandler? DevicesChanged;

    public IReadOnlyList<AudioDeviceInfo> GetDevices(AudioFlow flow) =>
        [.. GetActiveDevices(flow).Select(d =>
        {
            using (d)
            {
                return new AudioDeviceInfo(d.ID, d.FriendlyName);
            }
        })];

    public IAudioEndpointGroup CreateGroup(AudioFlow flow) => new AudioEndpointGroup(this, flow, dispatcher);

    internal IReadOnlyList<MMDevice> GetActiveDevices(AudioFlow flow)
    {
        try
        {
            return [.. enumerator.EnumerateAudioEndPoints(flow == AudioFlow.Capture ? DataFlow.Capture : DataFlow.Render, DeviceState.Active)];
        }
        catch (COMException)
        {
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

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) => QueueRefresh();

    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) => QueueRefresh();

    void IMMNotificationClient.OnDeviceRemoved(string deviceId) => QueueRefresh();

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
    }

    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
    }
}
