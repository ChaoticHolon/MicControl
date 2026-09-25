using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MicControlNG.Platform;

namespace MicControlNG.Services;

public sealed class AvaloniaDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}

/// <summary>File pickers and opening links/folders, abstracted so view models stay testable.</summary>
public interface IDialogService
{
    Task<string?> PickFileAsync(string title, string filterName, IReadOnlyList<string> patterns);

    /// <summary>Opens a URL in the browser or a folder in the file manager.</summary>
    Task OpenAsync(string target);
}

public sealed class DialogService : IDialogService
{
    public async Task<string?> PickFileAsync(string title, string filterName, IReadOnlyList<string> patterns)
    {
        if (TopLevel is not { } topLevel)
        {
            return null;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(filterName) { Patterns = patterns }, FilePickerFileTypes.All],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task OpenAsync(string target)
    {
        if (TopLevel is not { } topLevel)
        {
            return;
        }

        if (Directory.Exists(target))
        {
            await topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(target));
        }
        else
        {
            await topLevel.Launcher.LaunchUriAsync(new Uri(target));
        }
    }

    private static TopLevel? TopLevel =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
