using CommunityToolkit.Mvvm.ComponentModel;
using MicControlNG.Hotkeys;
using MicControlNG.Settings;

namespace MicControlNG.ViewModels;

/// <summary>Edits one <see cref="HotkeySettings"/> in place and reports every change.</summary>
public sealed class HotkeyEditorViewModel(string title, HotkeySettings settings, Action changed, bool canSuppress = true, bool supportsMouse = true) : ObservableObject
{
    public bool CanSuppress { get; } = canSuppress;

    public bool SupportsMouse { get; } = supportsMouse;

    public string Title { get; } = title;

    public HotkeySettings Settings { get; } = settings;

    public HotkeyGesture Key
    {
        get => Settings.Key;
        set => Update(Settings.Key, value, v => Settings.Key = v);
    }

    public HotkeyGesture AlternativeKey
    {
        get => Settings.AlternativeKey;
        set => Update(Settings.AlternativeKey, value, v => Settings.AlternativeKey = v);
    }

    public bool Suppress
    {
        get => Settings.Suppress;
        set => Update(Settings.Suppress, value, v => Settings.Suppress = v);
    }

    public bool IgnoreModifiers
    {
        get => Settings.IgnoreModifiers && !HasModifiers;
        set => Update(Settings.IgnoreModifiers, value, v => Settings.IgnoreModifiers = v);
    }

    /// <summary>"Ignore modifiers" only makes sense when neither gesture includes modifiers.</summary>
    public bool HasModifiers => Key.Modifiers != HotkeyModifiers.None || AlternativeKey.Modifiers != HotkeyModifiers.None;

    private void Update<T>(T oldValue, T newValue, Action<T> assign, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(oldValue, newValue))
        {
            return;
        }

        assign(newValue);
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(HasModifiers));
        OnPropertyChanged(nameof(IgnoreModifiers));
        changed();
    }
}
