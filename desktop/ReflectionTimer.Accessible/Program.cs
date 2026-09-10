using System.Text.RegularExpressions;
using ReflectionTimer.Desktop;

namespace ReflectionTimer.Accessible;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var profile = args is ["--profile", var name] ? name : args.Length == 0 ? "review" : "";
        if (!Regex.IsMatch(profile, @"\A[a-zA-Z0-9-]{1,40}\z")) {
            MessageBox.Show("Use --profile followed by a short name containing letters, digits, or hyphens.", "Accessibility preview"); return;
        }
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReflectionTimerAccessibilityPreview", profile);
        using var mutex = new Mutex(true, @"Local\ReflectionTimerAccessibilityPreview-" + profile.ToLowerInvariant(), out var first);
        if (!first) { MessageBox.Show("This preview profile is already open. Switch to its window using Alt+Tab.", "Accessibility preview"); return; }
        try {
            var store = new EncryptedStore(root);
            if (!File.Exists(Path.Combine(root, "state.dat"))) store.Save(PreviewSession.SampleState(DateTimeOffset.Now));
            var session=new PreviewSession(store);
            using var app = new PreviewApplication(session, root, store.RecoveryNotice);
            Application.Run(app);
        }
        catch {
            MessageBox.Show("The accessibility preview could not open its local test data. The installed Reflection Timer profile has not been accessed.", "Accessibility preview", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { mutex.ReleaseMutex(); }
    }
}
