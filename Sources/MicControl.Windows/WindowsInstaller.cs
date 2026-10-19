using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using MicControl.Installation;
using MicControl.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace MicControl.Windows;

/// <summary>
/// Installs this program on Windows. The setup file is the app published as one file, so installing copies this very
/// executable into place, writes out the built-in sounds it carries as resources, registers Settings → Apps and adds a
/// Start menu shortcut. All users: Program Files + HKLM + the common Start menu (admin). Just me: the user's Programs
/// folder + HKCU + their Start menu.
/// </summary>
public sealed partial class WindowsInstaller(ILogger<WindowsInstaller> logger) : IInstaller
{
    /// <summary>Built-in sounds are embedded in the executable under this prefix (see MicControl.csproj).</summary>
    public const string SoundResourcePrefix = "Sounds/";
    public const string LegalResourcePrefix = "Legal/";

    private static readonly string ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static string ExecutablePath => Environment.ProcessPath ?? throw new InvalidOperationException("Can't tell where MicControl is running from.");

    private static string Version => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    public bool IsElevated => StartupService.IsElevated;

    public InstalledCopy? Current
    {
        get
        {
            var directory = Path.GetDirectoryName(ExecutablePath)!;
            return InstallLayout.ScopeOf(directory, ProgramFiles, LocalAppData) is { } scope
                ? new InstalledCopy(scope, directory, Find(scope)?.Version)
                : null;
        }
    }

    public string DirectoryFor(InstallScope scope) => InstallLayout.DirectoryFor(scope, ProgramFiles, LocalAppData);

    public InstalledCopy? Find(InstallScope scope)
    {
        using var hive = OpenHive(scope);
        using var key = hive.OpenSubKey(InstallLayout.UninstallKey);
        return key?.GetValue("InstallLocation") is string location && File.Exists(Path.Combine(location, InstallLayout.ExecutableName))
            ? new InstalledCopy(scope, location, key.GetValue("DisplayVersion") as string)
            : null;
    }

    public bool RestartElevated(SetupCommand command)
    {
        try
        {
            var arguments = command.ToArguments();
            using var process = Process.Start(new ProcessStartInfo(ExecutablePath, arguments) { UseShellExecute = true, Verb = "runas" });
            LogRestartedElevated(arguments);
            return process is not null;
        }
        catch (Win32Exception ex)
        {
            // 1223: the user declined the admin prompt.
            LogElevationDeclined(ex.NativeErrorCode);
            return false;
        }
    }

    public async Task<bool> RunElevatedAsync(SetupCommand command)
    {
        try
        {
            var arguments = command.ToArguments();
            using var process = Process.Start(new ProcessStartInfo(ExecutablePath, arguments) { UseShellExecute = true, Verb = "runas" });
            LogRestartedElevated(arguments);
            if (process is null)
            {
                return false;
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
            LogElevatedCopyExited(process.ExitCode);
            return process.ExitCode == 0;
        }
        catch (Win32Exception ex)
        {
            LogElevationDeclined(ex.NativeErrorCode);
            return false;
        }
    }

    public async Task InstallAsync(InstallScope scope, IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (scope == InstallScope.AllUsers && !IsElevated)
        {
            throw new InvalidOperationException("Installing for all users needs administrator permission.");
        }

        var directory = DirectoryFor(scope);
        var target = Path.Combine(directory, InstallLayout.ExecutableName);
        LogInstalling(Version, scope, directory);
        Directory.CreateDirectory(directory);
        progress.Report(0.02);

        await CloseRunningCopiesAsync(target, cancellationToken).ConfigureAwait(false);
        progress.Report(0.1);

        if (!InstallLayout.SameFolder(Path.GetDirectoryName(ExecutablePath), directory))
        {
            var staging = target + ".new";
            await CopyAsync(ExecutablePath, staging, p => progress.Report(0.1 + (0.75 * p)), cancellationToken).ConfigureAwait(false);
            await ReplaceAsync(staging, target, cancellationToken).ConfigureAwait(false);
        }

        WriteSounds(directory);
        WriteResources(LegalResourcePrefix, directory);
        progress.Report(0.9);

        Register(scope, directory);
        CreateShortcut(scope, target);
        progress.Report(1);
        LogInstalled(scope, directory);
    }

    public async Task UninstallAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (Current is not { } current)
        {
            throw new InvalidOperationException("This copy of MicControl isn't installed, so there's nothing to uninstall.");
        }

        if (current.Scope == InstallScope.AllUsers && !IsElevated)
        {
            throw new InvalidOperationException("Uninstalling for all users needs administrator permission.");
        }

        LogUninstalling(current.Scope, current.Directory);
        await CloseRunningCopiesAsync(ExecutablePath, cancellationToken).ConfigureAwait(false);
        progress.Report(0.3);

        StartupService.RemoveAllFor(ExecutablePath);
        progress.Report(0.5);

        File.Delete(ShortcutPath(current.Scope));
        using (var hive = OpenHive(current.Scope))
        {
            hive.DeleteSubKeyTree(InstallLayout.UninstallKey, throwOnMissingSubKey: false);
        }

        progress.Report(0.8);

        // This program is in the folder being removed, so a helper waits for it to exit, then deletes the folder (and
        // this user's unpacked-libraries cache). Each user's settings in %APPDATA%\MicControl are kept.
        var cache = Path.Combine(Path.GetTempPath(), ".net", Path.GetFileNameWithoutExtension(InstallLayout.ExecutableName));
        var script = $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; " +
            $"Remove-Item -LiteralPath '{Quote(current.Directory)}' -Recurse -Force -ErrorAction SilentlyContinue; " +
            $"Remove-Item -LiteralPath '{Quote(cache)}' -Recurse -Force -ErrorAction SilentlyContinue";
        using var cleanup = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{script}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
        });
        progress.Report(1);
        LogUninstalled(current.Directory);

        static string Quote(string path) => path.Replace("'", "''", StringComparison.Ordinal);
    }

    public void Launch(InstallScope scope)
    {
        var exe = Path.Combine(DirectoryFor(scope), InstallLayout.ExecutableName);
        LogLaunching(exe);
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = DirectoryFor(scope) })?.Dispose();
    }

    private static RegistryKey OpenHive(InstallScope scope) => scope == InstallScope.AllUsers
        ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
        : RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);

    private static string ShortcutPath(InstallScope scope) => InstallLayout.ShortcutFor(
        scope,
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        Environment.GetFolderPath(Environment.SpecialFolder.Programs));

    /// <summary>
    /// Asks MicControl to exit (the exit signal reaches copies in this sign-in session), waits, then ends any copy of
    /// <paramref name="executable"/> still running, such as another user's.
    /// </summary>
    private async Task CloseRunningCopiesAsync(string executable, CancellationToken cancellationToken)
    {
        if (EventWaitHandle.TryOpenExisting(WindowsPlatform.ExitSignalName, out var exitSignal))
        {
            using (exitSignal)
            {
                exitSignal.Set();
            }
        }

        for (var attempt = 0; attempt < 40; attempt++)
        {
            var running = RunningCopies(executable);
            if (running.Count == 0)
            {
                return;
            }

            if (attempt == 39)
            {
                foreach (var process in running)
                {
                    LogKilling(process.Id);
                    process.Kill();
                }
            }

            running.ForEach(p => p.Dispose());
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }
    }

    private static List<Process> RunningCopies(string executable)
    {
        var copies = new List<Process>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            try
            {
                if (process.Id != Environment.ProcessId && string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                {
                    copies.Add(process);
                    continue;
                }
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                // Another user's process we may not inspect without admin rights, or one that just exited.
            }

            process.Dispose();
        }

        return copies;
    }

    private static async Task CopyAsync(string source, string destination, Action<double> progress, CancellationToken cancellationToken)
    {
        var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, useAsync: true);
        await using (input.ConfigureAwait(false))
        {
            var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
            await using (output.ConfigureAwait(false))
            {
                var buffer = new byte[1 << 20];
                long copied = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    copied += read;
                    progress((double)copied / input.Length);
                }
            }
        }
    }

    /// <summary>Moves the new file over the old one, retrying while a just-closed copy still holds it.</summary>
    private static async Task ReplaceAsync(string staging, string target, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(staging, target, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 20)
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static void WriteSounds(string directory) =>
        WriteResources(SoundResourcePrefix, Directory.CreateDirectory(Path.Combine(directory, InstallLayout.SoundsFolder)).FullName);

    /// <summary>Writes the exe's embedded files named <paramref name="prefix"/>… into <paramref name="folder"/>.</summary>
    private static void WriteResources(string prefix, string folder)
    {
        var assembly = Assembly.GetEntryAssembly();
        foreach (var name in assembly?.GetManifestResourceNames() ?? [])
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            using var resource = assembly!.GetManifestResourceStream(name)!;
            using var file = File.Create(Path.Combine(folder, name[prefix.Length..]));
            resource.CopyTo(file);
        }
    }

    private static void Register(InstallScope scope, string directory)
    {
        var size = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        using var hive = OpenHive(scope);
        using var key = hive.CreateSubKey(InstallLayout.UninstallKey);
        foreach (var (name, value) in InstallLayout.UninstallEntry(directory, Version, size))
        {
            key.SetValue(name, value, value is int ? RegistryValueKind.DWord : RegistryValueKind.String);
        }
    }

    private static void CreateShortcut(InstallScope scope, string target)
    {
        var path = ShortcutPath(scope);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host isn't available to create the shortcut.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic link = shell.CreateShortcut(path);
        link.TargetPath = target;
        link.WorkingDirectory = Path.GetDirectoryName(target);
        link.IconLocation = target + ",0";
        link.Description = "Mute and unmute your microphone from anywhere";
        link.Save();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The elevated setup finished with exit code {ExitCode}")]
    private partial void LogElevatedCopyExited(int exitCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Installing MicControl {Version} for {Scope} into {Directory}")]
    private partial void LogInstalling(string version, InstallScope scope, string directory);

    [LoggerMessage(Level = LogLevel.Information, Message = "Installed for {Scope} in {Directory}")]
    private partial void LogInstalled(InstallScope scope, string directory);

    [LoggerMessage(Level = LogLevel.Information, Message = "Uninstalling the {Scope} copy in {Directory}")]
    private partial void LogUninstalling(InstallScope scope, string directory);

    [LoggerMessage(Level = LogLevel.Information, Message = "Uninstalled; {Directory} is removed once this process exits")]
    private partial void LogUninstalled(string directory);

    [LoggerMessage(Level = LogLevel.Information, Message = "Restarted elevated: {Arguments}")]
    private partial void LogRestartedElevated(string arguments);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin prompt declined or failed (Win32 error {Error})")]
    private partial void LogElevationDeclined(int error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MicControl (process {ProcessId}) didn't exit when asked; ending it")]
    private partial void LogKilling(int processId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting {Executable}")]
    private partial void LogLaunching(string executable);
}
