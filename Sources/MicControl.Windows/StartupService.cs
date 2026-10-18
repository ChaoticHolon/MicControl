using System.Diagnostics;
using System.Security.Principal;
using System.Xml;
using System.Xml.Linq;
using MicControl.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace MicControl.Windows;

/// <summary>
/// Registers MicControl to start at sign-in. Elevated processes are blocked from the Run key at logon,
/// so when running elevated a scheduled task with highest privileges is used instead.
/// </summary>
public sealed partial class StartupService(ILogger<StartupService> logger) : IStartupRegistration
{
    public const string AutostartArgument = "--autostart";
    private const string Name = "MicControl";
    private const string LegacyName = "MicSwitch";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly XNamespace TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public static bool IsElevated { get; } = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    private static string Command => $"\"{Environment.ProcessPath}\" {AutostartArgument}";

    /// <summary>
    /// Each user's own task, in a "MicControl" folder, so users of a shared all-users install don't replace each
    /// other's. Uninstall removes them all through <see cref="RemoveAllFor"/>.
    /// </summary>
    private static string TaskName => $@"{Name}\{Environment.UserName} ({Environment.UserDomainName})";

    public bool IsEnabled => HasRunKey(Name) || HasScheduledTask(TaskName);

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            if (IsElevated)
            {
                var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
                var exitCode = Schtasks($"/Create /F /TN \"{TaskName}\" /TR \"{Command.Replace("\"", "\\\"", StringComparison.Ordinal)}\" /SC ONLOGON /RL HIGHEST /RU \"{user}\" /IT");
                LogAutostartTaskCreated(TaskName, Command, exitCode);
            }
            else
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
                key.SetValue(Name, Command);
                LogAutostartRunKeySet(Command);
            }

            return;
        }

        using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
        {
            key?.DeleteValue(Name, throwOnMissingValue: false);
        }

        if (HasScheduledTask(TaskName) && Schtasks($"/Delete /F /TN \"{TaskName}\"") != 0)
        {
            throw new InvalidOperationException("Run MicControl as administrator to remove the elevated startup task.");
        }

        LogAutostartRemoved();
    }

    /// <summary>
    /// For uninstall: removes every "Start with Windows" entry that starts <paramref name="executablePath"/>: each
    /// user's task in the \MicControl\ folder (an all-users uninstall runs as admin, so it can remove them all), and
    /// the current user's Run key value. Other users' Run key values live in their own
    /// registry hives; left behind, they just point at a missing program.
    /// </summary>
    public static void RemoveAllFor(string executablePath)
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
        {
            if (Starts(key?.GetValue(Name) as string, executablePath))
            {
                key!.DeleteValue(Name, throwOnMissingValue: false);
            }
        }

        if (Type.GetTypeFromProgID("Schedule.Service") is not { } type)
        {
            return;
        }

        dynamic scheduler = Activator.CreateInstance(type)!;
        scheduler.Connect();
        dynamic root = scheduler.GetFolder(@"\");
        try
        {
            dynamic folder = scheduler.GetFolder(@"\" + Name);
            dynamic tasks = folder.GetTasks(1);
            for (int i = tasks.Count; i >= 1; i--)
            {
                dynamic task = tasks.Item(i);
                if (Starts((string)task.Definition.Actions.Item(1).Path, executablePath))
                {
                    folder.DeleteTask((string)task.Name, 0);
                }
            }

            root.DeleteFolder(Name, 0);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException)
        {
            // No folder, or it still holds tasks of another copy.
        }

        static bool Starts(string? command, string exe) =>
            string.Equals(AutostartCommand.GetExecutable(command), exe, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Replaces autostart entries left by MicSwitch with MicControl ones (keeping autostart on if it was).
    /// Only entries whose executable is gone are taken over; an installed MicSwitch (e.g. 1.x) keeps its own.
    /// </summary>
    public void MigrateLegacyEntries()
    {
        var runKey = GetRunKey(LegacyName);
        var taskCommand = GetScheduledTaskCommand(LegacyName);
        var staleRunKey = AutostartCommand.IsStale(runKey, File.Exists);
        var staleTask = AutostartCommand.IsStale(taskCommand, File.Exists);
        LogLegacyEntries(runKey ?? "none", staleRunKey, taskCommand ?? "none", staleTask);
        if (!staleRunKey && !staleTask)
        {
            return;
        }

        if (staleRunKey)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(LegacyName, throwOnMissingValue: false);
        }

        if (staleTask)
        {
            // Needs elevation; if it fails the old task just points at a missing executable.
            var exitCode = Schtasks($"/Delete /F /TN \"{LegacyName}\"");
            LogLegacyTaskDeleted(exitCode);
        }

        if (!IsEnabled)
        {
            SetEnabled(true);
        }
    }

    private static bool HasRunKey(string name) => GetRunKey(name) is not null;

    private static string? GetRunKey(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(name) as string;
    }

    private static bool HasScheduledTask(string name) => Schtasks($"/Query /TN \"{name}\"") == 0;

    /// <summary>The program the task's first action runs, or null if there is no such task.</summary>
    private static string? GetScheduledTaskCommand(string name)
    {
        var (exitCode, output) = SchtasksWithOutput($"/Query /TN \"{name}\" /XML");
        if (exitCode != 0)
        {
            return null;
        }

        try
        {
            return XDocument.Parse(output).Descendants(TaskNamespace + "Exec").Elements(TaskNamespace + "Command").FirstOrDefault()?.Value;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static int Schtasks(string arguments) => SchtasksWithOutput(arguments).ExitCode;

    private static (int ExitCode, string Output) SchtasksWithOutput(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        errors.Wait();
        return (process.ExitCode, output);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Autostart: scheduled task {Task} created for {Command} (schtasks exit {ExitCode})")]
    private partial void LogAutostartTaskCreated(string task, string command, int exitCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Autostart: Run key set to {Command}")]
    private partial void LogAutostartRunKeySet(string command);

    [LoggerMessage(Level = LogLevel.Information, Message = "Autostart removed")]
    private partial void LogAutostartRemoved();

    [LoggerMessage(Level = LogLevel.Information, Message = "MicSwitch autostart entries: Run key {RunKey} (program gone: {RunKeyStale}); task {Task} (program gone: {TaskStale})")]
    private partial void LogLegacyEntries(string runKey, bool runKeyStale, string task, bool taskStale);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed the MicSwitch scheduled task (schtasks exit {ExitCode})")]
    private partial void LogLegacyTaskDeleted(int exitCode);
}
