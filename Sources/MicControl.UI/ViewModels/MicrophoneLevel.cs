using CommunityToolkit.Mvvm.ComponentModel;

namespace MicControl.ViewModels;

/// <summary>One microphone's bar in the "All microphones" level meter.</summary>
public sealed partial class MicrophoneLevel(string id, string name) : ObservableObject
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    [ObservableProperty]
    public partial double Level { get; set; }
}