using ReflectionTimer.Desktop;

namespace ReflectionTimer.Accessible;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        (string Profile, bool Tray) launch;
        try { launch = PreviewStartup.Parse(args); }
        catch (ArgumentException error) { MessageBox.Show(error.Message, "Accessibility preview"); return; }
        var profile = launch.Profile;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReflectionTimerAccessibilityPreview", profile);
        using var mutex = new Mutex(true, @"Local\ReflectionTimerAccessibilityPreview-" + profile.ToLowerInvariant(), out var first);
        if (!first) { if(!launch.Tray) MessageBox.Show("This preview profile is already open. Switch to its window using Alt+Tab.", "Accessibility preview"); return; }
        try {
            var store = new EncryptedStore(root);
            if (!File.Exists(Path.Combine(root, "state.dat"))) store.Save(PreviewSession.SampleState(DateTimeOffset.Now));
            var session=new PreviewSession(store);
            using var app = new PreviewApplication(session, root, store.RecoveryNotice, launch.Tray);
            Application.Run(app);
        }
        catch {
            MessageBox.Show("The accessibility preview could not open its local test data. The installed Reflection Timer profile has not been accessed.", "Accessibility preview", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { mutex.ReleaseMutex(); }
    }
}
