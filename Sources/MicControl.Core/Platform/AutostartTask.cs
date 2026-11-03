using System.Xml;
using System.Xml.Linq;

namespace MicControl.Platform;

/// <summary>
/// The Task Scheduler definition for "Start with Windows" when running elevated. <c>schtasks /Create /SC ONLOGON</c>
/// alone leaves the scheduler's defaults: a 72-hour time limit (the scheduler ends the app three days after sign-in,
/// leaving the microphone muted), no start or a forced stop on battery, and below-normal priority.
/// </summary>
public static class AutostartTask
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <summary>Runs as long as the session lasts.</summary>
    public const string NoTimeLimit = "PT0S";

    /// <summary>Normal process priority (the scheduler's default, 7, is below normal).</summary>
    public const int NormalPriority = 4;

    /// <summary>The task XML for <c>schtasks /Create /XML</c>: starts <paramref name="executable"/> at <paramref name="userId"/>'s sign-in, with highest privileges.</summary>
    public static string CreateXml(string executable, string arguments, string userId)
    {
        var task = new XElement(Ns + "Task",
            new XAttribute("version", "1.2"),
            new XElement(Ns + "RegistrationInfo",
                new XElement(Ns + "Description", "Starts MicControl when you sign in.")),
            new XElement(Ns + "Triggers",
                new XElement(Ns + "LogonTrigger",
                    new XElement(Ns + "Enabled", true),
                    new XElement(Ns + "UserId", userId))),
            new XElement(Ns + "Principals",
                new XElement(Ns + "Principal",
                    new XAttribute("id", "Author"),
                    new XElement(Ns + "UserId", userId),
                    new XElement(Ns + "LogonType", "InteractiveToken"),
                    new XElement(Ns + "RunLevel", "HighestAvailable"))),
            new XElement(Ns + "Settings",
                new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(Ns + "DisallowStartIfOnBatteries", false),
                new XElement(Ns + "StopIfGoingOnBatteries", false),
                new XElement(Ns + "AllowHardTerminate", true),
                new XElement(Ns + "StartWhenAvailable", false),
                new XElement(Ns + "RunOnlyIfNetworkAvailable", false),
                new XElement(Ns + "IdleSettings",
                    new XElement(Ns + "StopOnIdleEnd", false),
                    new XElement(Ns + "RestartOnIdle", false)),
                new XElement(Ns + "AllowStartOnDemand", true),
                new XElement(Ns + "Enabled", true),
                new XElement(Ns + "Hidden", false),
                new XElement(Ns + "RunOnlyIfIdle", false),
                new XElement(Ns + "WakeToRun", false),
                new XElement(Ns + "ExecutionTimeLimit", NoTimeLimit),
                new XElement(Ns + "Priority", NormalPriority)),
            new XElement(Ns + "Actions",
                new XAttribute("Context", "Author"),
                new XElement(Ns + "Exec",
                    new XElement(Ns + "Command", executable),
                    new XElement(Ns + "Arguments", arguments))));
        return new XDeclaration("1.0", "UTF-16", null) + Environment.NewLine + task;
    }

    /// <summary>
    /// True when a task made by an older MicControl (plain <c>schtasks /Create</c>) would be ended by the scheduler
    /// or held back: a time limit, battery rules or below-normal priority. Unreadable XML counts as fine, so nothing
    /// is replaced by guesswork.
    /// </summary>
    public static bool NeedsRepair(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return false;
        }

        XElement? settings;
        try
        {
            settings = XDocument.Parse(xml).Root?.Element(Ns + "Settings");
        }
        catch (XmlException)
        {
            return false;
        }

        if (settings is null)
        {
            return false;
        }

        // Missing elements take the scheduler's defaults: 72 hours, battery rules on, priority 7.
        var limit = (string?)settings.Element(Ns + "ExecutionTimeLimit") ?? "PT72H";
        var priority = (string?)settings.Element(Ns + "Priority") ?? "7";
        return limit.Trim() != NoTimeLimit
            || IsTrue(settings.Element(Ns + "DisallowStartIfOnBatteries"), byDefault: true)
            || IsTrue(settings.Element(Ns + "StopIfGoingOnBatteries"), byDefault: true)
            || !int.TryParse(priority, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value) || value > NormalPriority;

        static bool IsTrue(XElement? element, bool byDefault) =>
            element is null ? byDefault : string.Equals(element.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }
}
