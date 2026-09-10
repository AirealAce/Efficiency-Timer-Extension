using System.Reflection;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private static void TestHotkeyRegistrationMatrix()
    {
        var chords = new[] {
            (Key: 0x54u, Id: 0x5254, Field: "focusShortcut", Label: "Ctrl+Alt+T"),
            (Key: 0xC0u, Id: 0x5255, Field: "endEarlyShortcut", Label: "Ctrl+Alt+`"),
            (Key: 0xBFu, Id: 0x5256, Field: "compactShortcut", Label: "Ctrl+Alt+/"),
            (Key: 0xBEu, Id: 0x5257, Field: "compactFocusShortcut", Label: "Ctrl+Alt+."),
            (Key: 0xBCu, Id: 0x5258, Field: "reflectionFocusShortcut", Label: "Ctrl+Alt+,")
        };
        foreach (var blocked in chords) Test("all five shortcuts register independently when unavailable: " + blocked.Label, () => {
            // Never reserve real global chords or modify the running user's app.
            var api = new FakeHotKey { BlockedKey = blocked.Key };
            WithEndEarlyApp((app, _) => {
                app.EnableGlobalShortcut(api); app.EnableGlobalShortcut(api);
                var (main, _, _, _) = TimerShortcutControls(app);
                Is(!main.Visible); Equal(5, api.Registrations.Count);
                foreach (var chord in chords) {
                    var call = api.Registrations.Single(x => x.Id == chord.Id);
                    Equal(chord.Key, call.Key); Equal(0x4003u, call.Modifiers); Is(call.Window != 0);
                    var shortcut = (GlobalShortcut)typeof(TimerApplication).GetField(chord.Field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
                    Equal(chord.Key != blocked.Key, shortcut.IsRegistered);
                    Is(!shortcut.Dispatch(GlobalShortcut.HotKeyMessage, -1));
                }
                Equal(5, api.Registrations.Select(x => x.Window).Distinct().Count());
                var unavailable = (GlobalShortcut)typeof(TimerApplication).GetField(blocked.Field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
                Is(!unavailable.Dispatch(GlobalShortcut.HotKeyMessage, blocked.Id));
                Is(Descendants(main).OfType<Label>().Any(x => x.Text.StartsWith(blocked.Label) &&
                    (x.Text.Contains("unavailable") || x.Text.Contains("could not be registered"))));
                var log = app.Log.Recent();
                Equal(4, log.Count(x => x.Event == "shortcut.registered"));
                Equal(1, log.Count(x => x.Event == "shortcut.unavailable"));
                Is(!app.Engine.Snapshot.Timer.IsRunning); Equal(0, app.Engine.Snapshot.Prompts.Count);
            });
            Equal(4, api.Unregistrations.Count);
            Is(!api.Unregistrations.Any(x => x.Id == blocked.Id));
        });
        Test("tray check-in remains usable when the comma shortcut is reserved", () => {
            WithEndEarlyApp((app, _) => {
                app.EnableGlobalShortcut(new FakeHotKey { BlockedKey = 0xBC });
                app.Engine.Start(150, false, 0);
                var before = app.Engine.Snapshot.Timer;
                var tray = (NotifyIcon)typeof(TimerApplication).GetField("tray", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
                tray.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(x => x.Text == "Check in to current session").PerformClick();
                Application.DoEvents();
                var popup = Application.OpenForms.OfType<ReflectionWindow>().Single();
                Is(ReflectionResponse(popup).ContainsFocus);
                Is(app.Engine.Snapshot.Prompts.Single().IsCheckIn);
                Equal(before, app.Engine.Snapshot.Timer);
            });
        });
    }
}
