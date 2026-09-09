using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace ReflectionTimer.Desktop;

internal record PackageFile(string Path, string Sha256);
internal record PackageManifest(int FormatVersion, string Version, string Runtime, List<PackageFile> Files, string? RuntimeVersion = null);
internal record InstallationResult(string? Backup, IReadOnlyList<string> ShortcutWarnings);

internal static class LocalInstallation
{
    internal const string ManifestName = "package-manifest.json";
    internal static string SafeChild(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':')
            || relative.Split('/', '\\').Any(x => x is ".." or "." or "" || x != x.TrimEnd(' ', '.')
                || x.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || System.Text.RegularExpressions.Regex.IsMatch(x, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
            throw new InvalidDataException("Invalid package path.");
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid package path.");
        return full;
    }
    private static void NoRedirects(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("A setup path is redirected. Extract the ZIP into a regular local folder and try again; the current install was not changed.");
    }
    internal static PackageManifest ReadManifest(string source)
    {
        var file = Path.Combine(source, ManifestName);
        NoRedirects(file);
        if (!File.Exists(file) || new FileInfo(file).Length > 2 * 1024 * 1024)
            throw new InvalidDataException("This is not a complete download package. Extract the public release ZIP first, or use the documented developer installer.");
        PackageManifest? manifest;
        try { manifest = JsonSerializer.Deserialize<PackageManifest>(File.ReadAllText(file), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch (JsonException) { throw new InvalidDataException("The download manifest is damaged. Extract a fresh copy of the public release ZIP."); }
        if (manifest is null || manifest.FormatVersion != 1 || manifest.Runtime != "win-x64" || manifest.Files is null || manifest.Files.Count is < 4 or > 2000
            || !Version.TryParse(manifest.Version, out _) || manifest.Files.Any(x => x is null || string.IsNullOrWhiteSpace(x.Path))
            || !manifest.Files.Any(x => x.Path == "ReflectionTimer.exe") || manifest.Files.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Files.Count)
            throw new InvalidDataException("The download manifest is invalid.");
        foreach (var entry in manifest.Files) {
            var path = SafeChild(source, entry.Path);
            NoRedirects(path);
            if (!new[] { ".exe", ".dll", ".json", ".txt", ".html", ".gs" }.Contains(Path.GetExtension(path).ToLowerInvariant())
                || !System.Text.RegularExpressions.Regex.IsMatch(entry.Sha256 ?? "", "^[a-fA-F0-9]{64}$")) throw new InvalidDataException("Unexpected package contents.");
            if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("A package file is missing or redirected.");
            using var stream = File.OpenRead(path);
            if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Download verification failed. Extract a fresh copy of the release ZIP.");
        }
        return manifest;
    }
    internal static string? InstallFiles(string source, string target, Func<bool> targetRunning, Action<string, string>? moveDirectory = null)
    {
        moveDirectory ??= Directory.Move;
        source = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
        target = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar);
        if (source.Equals(target, StringComparison.OrdinalIgnoreCase)) return null;
        if (source.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || target.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Keep the extracted download separate from the installed folder.");
        var parent = Path.GetDirectoryName(target) ?? throw new InvalidDataException("Invalid install folder.");
        NoRedirects(target);
        if (targetRunning()) throw new InvalidOperationException("Quit the installed Reflection Timer from its tray menu, then try again. X only hides it.");
        if (Directory.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("The install folder is redirected; choose the portable app instead.");
        var manifest = ReadManifest(source);
        var suffix = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(parent, Path.GetFileName(target) + "-staging-" + suffix);
        var backup = Path.Combine(parent, Path.GetFileName(target) + "-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + suffix[..8]);
        // All three are explicit siblings under the same per-user Programs directory.
        Directory.CreateDirectory(staging);
        foreach (var entry in manifest.Files) {
            var destination = SafeChild(staging, entry.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(SafeChild(source, entry.Path), destination, false);
            using var copied = File.OpenRead(destination);
            if (!Convert.ToHexString(SHA256.HashData(copied)).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("Copy verification failed; the existing install was not replaced.");
        }
        // Persist the manifest already validated above, not a second unchecked
        // read of a download file that may have changed during the copy.
        File.WriteAllText(Path.Combine(staging, ManifestName), JsonSerializer.Serialize(manifest));
        // Preserve this user's optional local soundtrack files, never put them in a shared ZIP.
        if (Directory.Exists(target)) foreach (var mp3 in Directory.EnumerateFiles(target, "*.mp3", SearchOption.TopDirectoryOnly)) {
            if ((File.GetAttributes(mp3) & FileAttributes.ReparsePoint) != 0) continue;
            File.Copy(mp3, Path.Combine(staging, Path.GetFileName(mp3)), false);
        }
        if (targetRunning()) throw new InvalidOperationException("The installed app reopened during setup. Quit it and try again; the existing install is unchanged.");
        var hadPrevious = Directory.Exists(target);
        if (hadPrevious) moveDirectory(target, backup);
        try { moveDirectory(staging, target); }
        catch (Exception installError) {
            if (hadPrevious && !Directory.Exists(target)) {
                try { moveDirectory(backup, target); }
                catch (Exception restoreError) {
                    throw new IOException("The update could not be installed or restored automatically. Your previous app files are retained at: " + backup
                        + ". Your reflection data is unchanged. Close other copies and recover that folder before trying again.", new AggregateException(installError, restoreError));
                }
            }
            throw;
        }
        return hadPrevious ? backup : null;
    }
    internal static InstallationResult InstallWithShortcuts(string source, string target, Func<bool> targetRunning,
        IEnumerable<string> shortcuts, Action<string, string>? createShortcut = null)
    {
        var backup = InstallFiles(source, target, targetRunning);
        var warnings = new List<string>();
        foreach (var path in shortcuts) {
            try { (createShortcut ?? CreateShortcut)(path, Path.Combine(target, "ReflectionTimer.exe")); }
            catch (Exception error) { warnings.Add(Path.GetFileName(path) + ": " + error.Message); }
        }
        return new(backup, warnings);
    }
    internal static void CreateShortcut(string path, string exe)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Windows shortcuts are unavailable.");
        dynamic shell = Activator.CreateInstance(type)!;
        object? raw = null;
        try {
            raw = shell.CreateShortcut(path); dynamic shortcut = raw;
            if (File.Exists(path) && !string.Equals((string)shortcut.TargetPath, exe, StringComparison.OrdinalIgnoreCase))
                throw new IOException("A different shortcut already uses this name; it was not overwritten.");
            shortcut.TargetPath = exe; shortcut.WorkingDirectory = Path.GetDirectoryName(exe); shortcut.Description = "Reflection Timer — your spreadsheet, your data";
            shortcut.IconLocation = exe + ",0"; shortcut.Save();
        } finally { if (raw is not null) Marshal.FinalReleaseComObject(raw); Marshal.FinalReleaseComObject(shell); }
    }
    internal static void InstallInteractive(IWin32Window owner)
    {
        var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ReflectionTimerDesktop");
        var exe = Path.Combine(target, "ReflectionTimer.exe");
        if (MessageBox.Show(owner, "Install/update Reflection Timer for your Windows account and create Desktop/Start menu shortcuts?\n\nYour saved settings and reflections are retained. An existing installed folder will be backed up. No administrator access is required.", "Install Reflection Timer", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
        try {
            var result = InstallWithShortcuts(AppContext.BaseDirectory, target, () => {
                foreach (var process in Process.GetProcessesByName("ReflectionTimer")) using (process) {
                    if (process.Id == Environment.ProcessId) continue;
                    try { if (string.Equals(process.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase)) return true; }
                    catch { throw new InvalidOperationException("Could not verify the other Reflection Timer process. Close other copies before installing."); }
                }
                return false;
            }, [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Reflection Timer Desktop.lnk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Reflection Timer Desktop.lnk")]);
            var instructions = result.ShortcutWarnings.Count == 0 ? "Installed. Close this copy using Quit desktop app, then open the Reflection Timer Desktop shortcut."
                : "App files installed, but some shortcuts could not be created. Close this copy using Quit desktop app, then open:\n" + exe
                    + "\n\nExisting conflicting shortcuts were not overwritten.\n" + string.Join("\n", result.ShortcutWarnings);
            MessageBox.Show(owner, instructions + "\n\nSettings and data are unchanged." + (result.Backup is null ? "" : "\n\nPrevious binaries: " + result.Backup), "Reflection Timer installed");
        } catch (Exception error) { MessageBox.Show(owner, "Installation could not complete: " + error.Message + "\n\nYour local settings and reflection data were not deleted. You can keep using the extracted app.", "Reflection Timer setup", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
