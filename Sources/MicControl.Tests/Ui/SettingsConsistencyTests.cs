using System.Collections;
using System.ComponentModel;
using System.Reflection;
using Avalonia.Headless.XUnit;
using MicControl.Platform;
using MicControl.Settings;
using MicControl.ViewModels;

namespace MicControl.Tests.Ui;

/// <summary>
/// A setting can be shown in more than one place (setup, Home, About, the tray menu), so any change to what a
/// property returns must raise PropertyChanged for it, or the other places keep showing the old value.
/// </summary>
public sealed class SettingsConsistencyTests
{
    public static TheoryData<string> SettableProperties()
    {
        var data = new TheoryData<string>();
        foreach (var property in Settable())
        {
            data.Add(property.Name);
        }

        return data;
    }

    [AvaloniaTheory]
    [MemberData(nameof(SettableProperties))]
    public void EveryChange_IsAnnounced(string name)
    {
        var property = typeof(MainViewModel).GetProperty(name)!;
        var changes = 0;

        // From the defaults and from non-default choices, so a radio-style switch (true only selects) changes too.
        foreach (var nonDefault in new[] { false, true })
        {
            using var harness = new TestHarness(s =>
            {
                s.Notifications.CustomSoundsEnabled = true;
                s.CustomHotkeysEnabled = true;
                if (nonDefault)
                {
                    s.Microphone.MuteMode = MuteMode.ToggleMute;
                    s.Window.Theme = AppTheme.Dark;
                    s.Window.AppMode = AppMode.Window;
                }
            });
            harness.Devices.Microphones.Add(new("mic-2", "Webcam Microphone"));
            harness.Devices.RaiseDevicesChanged();
            var vm = harness.ViewModel;
            var announced = new List<string?>();
            vm.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

            foreach (var candidate in Candidates(vm, property))
            {
                var before = property.GetValue(vm);
                announced.Clear();
                property.SetValue(vm, candidate);
                if (!Equals(before, property.GetValue(vm)))
                {
                    changes++;
                    Assert.True(announced.Contains(name), $"{name} changed from {before} to {property.GetValue(vm)} without PropertyChanged");
                }
            }
        }

        Assert.True(changes > 0, $"No candidate value changed {name}; add one to {nameof(Candidates)}");
    }

    private static IEnumerable<PropertyInfo> Settable() => typeof(MainViewModel)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.SetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0)
        .Where(p => p.PropertyType == typeof(bool) || p.PropertyType == typeof(double) || p.PropertyType == typeof(string) || p.PropertyType.IsEnum);

    private static List<object?> Candidates(MainViewModel vm, PropertyInfo property)
    {
        var type = property.PropertyType;
        if (type == typeof(bool))
        {
            return [true, false, true];
        }

        if (type.IsEnum)
        {
            return [.. Enum.GetValues(type).Cast<object>(), .. Enum.GetValues(type).Cast<object>()];
        }

        if (type == typeof(double))
        {
            return [0.0, 0.25, 0.5, 1.0, 2.0, 50.0, 0.0];
        }

        // Strings: every id or value the view model offers in its lists (devices, sounds).
        return typeof(MainViewModel).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => typeof(IEnumerable).IsAssignableFrom(p.PropertyType) && p.PropertyType != typeof(string))
            .SelectMany(p => (p.GetValue(vm) as IEnumerable)?.Cast<object?>() ?? [])
            .Select(item => item switch
            {
                AudioDeviceInfo device => (object?)device.Id,
                Choice { Value: string value } => value,
                _ => null,
            })
            .Where(value => value is not null)
            .Distinct()
            .ToList();
    }
}
