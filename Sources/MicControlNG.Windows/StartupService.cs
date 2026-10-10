using System.Diagnostics;
using System.Security.Principal;
using System.Xml;
using System.Xml.Linq;
using MicControlNG.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace MicControlNG.Windows;

/// <summary>
/// Registers MicControlNG to start at sign-in. Elevated processes are blocked from the Run key at logon,
/// so when running elevated a scheduled task with highest privileges is used instead.
/// </summary>
public sealed partial class StartupService(ILogger<StartupService> logger) : IStartupRegistration
{
    public const string AutostartArgument = "--autostart";
    private const string Name = "MicControlNG";
    private const string LegacyName = "MicSwitch";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly XNamespace TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public static bool IsElevated { get; } = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    private static string Command => $"\"{Environment.ProcessPath}\" {AutostartArgument}";

    public bool IsEnabled => HasRunKey(Name) || HasScheduledTask(Name);

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            if (IsElevated)
            {
                var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
                var exitCode = Schtasks($"/Create /F /TN \"{Name}\" /TR \"{Command.Replace("\"", "\\\"", StringComparison.Ordinal)}\" /SC ONLOGON /RL HIGHEST /RU \"{user}\" /IT");
                LogAutostartTaskCreated(Command, exitCode);
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

        if (HasScheduledTask(Name) && Schtasks($"/Delete /F /TN \"{Name}\"") != 0)
        {
            throw new InvalidOperationException("Run MicControl as administrator to remove the elevated startup task.");
        }

        LogAutostartRemoved();
    }

    /// <summary>
    /// Replaces autostart entries left by MicSwitch with MicControlNG ones (keeping autostart on if it was).
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Autostart: scheduled task created for {Command} (schtasks exit {ExitCode})")]
    private partial void LogAutostartTaskCreated(string command, int exitCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Autostart: Run key set to {Command}")]
    private partial void LogAutostartRunKeySet(string command);

    [LoggerMessage(Level = LogLevel.Information, Message = "Autostart removed")]
    private partial void LogAutostartRemoved();

    [LoggerMessage(Level = LogLevel.Information, Message = "MicSwitch autostart entries: Run key {RunKey} (program gone: {RunKeyStale}); task {Task} (program gone: {TaskStale})")]
    private partial void LogLegacyEntries(string runKey, bool runKeyStale, string task, bool taskStale);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed the MicSwitch scheduled task (schtasks exit {ExitCode})")]
    private partial void LogLegacyTaskDeleted(int exitCode);
}
