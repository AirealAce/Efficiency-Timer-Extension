using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace ReflectionTimer.Accessible;

internal static class PreviewStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static bool ValidProfile(string profile) => Regex.IsMatch(profile, @"\A[a-zA-Z0-9-]{1,40}\z");
    internal static (string Profile, bool Tray) Parse(string[] args)
    {
        var profile = "review"; var tray = false; var hasProfile = false;
        for (var i = 0; i < args.Length; i++) {
            if (args[i] == "--tray" && !tray) tray = true;
            else if (args[i] == "--profile" && !hasProfile && i + 1 < args.Length) { profile = args[++i]; hasProfile = true; }
            else throw new ArgumentException("Use --profile followed by a short name, with optional --tray.");
        }
        if (!ValidProfile(profile)) throw new ArgumentException("Profile names accept only letters, digits, and hyphens.");
        return (profile, tray);
    }
    internal static string ValueName(string profile)
    {
        if (!ValidProfile(profile)) throw new ArgumentException("Invalid preview profile.");
        return "Reflection Timer accessibility preview (" + profile.ToLowerInvariant() + ")";
    }
    internal static string Command(string executable, string profile)
    {
        _ = ValueName(profile);
        if (executable.Contains('"') || !Path.IsPathFullyQualified(executable)) throw new ArgumentException("Invalid executable path.");
        return $"\"{executable}\" --profile {profile} --tray";
    }
    internal static void Set(string profile, bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName(profile), Command(Environment.ProcessPath!, profile));
        else key.DeleteValue(ValueName(profile), false);
    }
}
