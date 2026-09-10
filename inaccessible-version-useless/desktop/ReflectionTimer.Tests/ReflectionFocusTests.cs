using System.Reflection;
using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private static GlobalShortcut ReflectionFocusShortcut(TimerApplication app) =>
        (GlobalShortcut)typeof(TimerApplication).GetField("reflectionFocusShortcut", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
    private static void PressComma(TimerApplication app)
    {
        Is(ReflectionFocusShortcut(app).Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.ReflectionFocusId));
        Application.DoEvents();
    }
    private static TextBox ReflectionResponse(ReflectionWindow window) => Descendants(window).OfType<TextBox>().Single(x => x.AccessibleName == "Session reflection");
    private static void FocusPending(TimerApplication app) { app.ShowReflections(); Application.DoEvents(); }

    private static void TestReflectionFocus()
    {
        foreach (var source in new[] { "pending", "button", "automatic", "ctrl-enter" })
            Test("sending from " + source + " closes the popup without opening the pending backlog", () => {
                var prompts = Enumerable.Range(0, 3).Select(i => new ReflectionPrompt(Guid.NewGuid(),
                    DateTimeOffset.Now.ToUnixTimeMilliseconds() - 10000 + i, 30, 0, false, "Synthetic draft " + i)).ToList();
                WithEndEarlyApp((app, directory) => {
                    TimerShortcutControls(app);
                    if (source == "pending" || source == "ctrl-enter") { FocusPending(app); FocusPending(app); }
                    if (source == "button") app.ShowReflections();
                    var popup = Application.OpenForms.OfType<ReflectionWindow>().Single();
                    var before = app.Engine.Snapshot.Timer;
                    ReflectionResponse(popup).Text = "Synthetic submitted reflection";
                    if (source == "ctrl-enter") {
                        var key = new KeyEventArgs(Keys.Control | Keys.Enter);
                        typeof(Control).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(ReflectionResponse(popup), [key]);
                        Is(key.SuppressKeyPress);
                    }
                    else Descendants(popup).OfType<Button>().Single(x => x.Text == "Save & send").PerformClick();
                    Application.DoEvents();
                    Is(popup.IsDisposed); Equal(0, Application.OpenForms.OfType<ReflectionWindow>().Count());
                    Equal(before, app.Engine.Snapshot.Timer);
                    Equal(prompts[0].Id, app.Engine.Snapshot.Outbox.Single().Id);
                    Equal("Synthetic submitted reflection", app.Engine.Snapshot.Outbox.Single().Message);
                    Is(prompts.Skip(1).SequenceEqual(app.Engine.Snapshot.Prompts));
                    Is(prompts.Skip(1).SequenceEqual(new EncryptedStore(directory).Load().Prompts));
                    // Unrelated refreshes and finishing an upload must not reshow old prompts.
                    app.Engine.SetAppVolume(0); Application.DoEvents();
                    var queued = app.Engine.BeginUpload(true); Is(queued is not null);
                    app.Engine.FinishUpload(queued!.Id, true, tab: "test"); Application.DoEvents();
                    Equal(0, Application.OpenForms.OfType<ReflectionWindow>().Count());
                    FocusPending(app);
                    var next = Application.OpenForms.OfType<ReflectionWindow>().Single();
                    Equal(prompts[1].Draft, ReflectionResponse(next).Text);
                    Descendants(next).OfType<Button>().Single(x => x.Text == "Save & send").PerformClick(); Application.DoEvents();
                    Equal(0, Application.OpenForms.OfType<ReflectionWindow>().Count());
                    Equal(prompts[2], app.Engine.Snapshot.Prompts.Single()); Equal(2, app.Engine.Snapshot.Outbox.Count);
                }, new AppState { ExtensionDisabledConfirmed = true, Timer = new() { Volume = 0 }, Prompts = prompts });
            });
        Test("a failed reflection save keeps the same popup and every pending draft", () => {
            var prompts = Enumerable.Range(0, 2).Select(i => new ReflectionPrompt(Guid.NewGuid(),
                DateTimeOffset.Now.ToUnixTimeMilliseconds() + i, 30, 0, false, "Synthetic draft " + i)).ToList();
            WithEndEarlyApp((app, _) => {
                TimerShortcutControls(app); FocusPending(app);
                var popup = Application.OpenForms.OfType<ReflectionWindow>().Single(); ReflectionResponse(popup).Text = " ";
                Descendants(popup).OfType<Button>().Single(x => x.Text == "Save & send").PerformClick(); Application.DoEvents();
                Equal(popup, Application.OpenForms.OfType<ReflectionWindow>().Single()); Is(!popup.IsDisposed);
                Equal(2, app.Engine.Snapshot.Prompts.Count); Equal(0, app.Engine.Snapshot.Outbox.Count);
                ReflectionResponse(popup).Text = "Corrected synthetic reflection";
                Descendants(popup).OfType<Button>().Single(x => x.Text == "Save & send").PerformClick(); Application.DoEvents();
                Equal(0, Application.OpenForms.OfType<ReflectionWindow>().Count()); Equal(prompts[1], app.Engine.Snapshot.Prompts.Single());
            }, new AppState { ExtensionDisabledConfirmed = true, Timer = new() { Volume = 0 }, Prompts = prompts });
        });
        foreach (var newTest in new[] { false, true }) Test("new completion still opens after sending while older drafts stay deferred; test=" + newTest, () => {
            var prompts = Enumerable.Range(0, 2).Select(i => new ReflectionPrompt(Guid.NewGuid(),
                DateTimeOffset.Now.ToUnixTimeMilliseconds() + i, 30, 0, false, "Synthetic draft " + i)).ToList();
            WithEndEarlyApp((app, _) => {
                TimerShortcutControls(app); FocusPending(app);
                var popup = Application.OpenForms.OfType<ReflectionWindow>().Single();
                Descendants(popup).OfType<Button>().Single(x => x.Text == "Save & send").PerformClick(); Application.DoEvents();
                Equal(0, Application.OpenForms.OfType<ReflectionWindow>().Count());
                if (newTest) app.Engine.TestPrompt();
                else { app.Engine.Start(300, true, 0); app.EndTimerEarly(); }
                Application.DoEvents();
                var next = Application.OpenForms.OfType<ReflectionWindow>().Single();
                Equal("", ReflectionResponse(next).Text);
                Is(app.Engine.Snapshot.Prompts.Contains(prompts[1])); Equal(2, app.Engine.Snapshot.Prompts.Count);
                Is(app.Log.Recent().Last(x => x.Event == "prompt.shown").ItemId != prompts[1].Id);
                if (!newTest) Is(app.Engine.Snapshot.Timer.IsRunning && app.Engine.Snapshot.Timer.AutoRestart);
            }, new AppState { ExtensionDisabledConfirmed = true, Timer = new() { Volume = 0 }, Prompts = prompts });
        });
        Test("comma restores independent Ctrl Alt NoRepeat registration and releases exactly once", () => {
            var api = new FakeHotKey(); var used = 0;
            var shortcut = new GlobalShortcut(() => used++, api, GlobalShortcut.ReflectionFocusKey, GlobalShortcut.ReflectionFocusId);
            var call = api.Registrations.Single(); Equal(0xBCu, call.Key); Equal(0x4003u, call.Modifiers); Equal(0x5258, call.Id);
            Is(!shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.CompactFocusId));
            Is(!shortcut.Dispatch(0, call.Id)); Is(shortcut.Dispatch(GlobalShortcut.HotKeyMessage, call.Id)); Equal(1, used);
            shortcut.Dispose(); shortcut.Dispose(); Equal((call.Window, call.Id), api.Unregistrations.Single());
            Is(!shortcut.Dispatch(GlobalShortcut.HotKeyMessage, call.Id));
        });
        foreach (var kind in new[] { "complete", "early", "test" }) Test("Pending reflections focuses the " + kind + " reflection preserving draft and selection", () => {
            var prompt = new ReflectionPrompt(Guid.NewGuid(), DateTimeOffset.Now.ToUnixTimeMilliseconds(), 300, 0, kind == "test", "A saved synthetic draft") {
                EndedEarly = kind == "early", EarlyEndReason = kind == "early" ? "Synthetic reason" : ""
            };
            WithEndEarlyApp((app, directory) => {
                app.SetFloatingTimer(false);
                var (main, _, _, _) = TimerShortcutControls(app); main.Hide();
                app.Engine.Start(9000, true, 0, app.Engine.Now + 3600000); Application.DoEvents();
                var popup = Application.OpenForms.OfType<ReflectionWindow>().Single(); var response = ReflectionResponse(popup);
                response.Text = "An edited synthetic draft"; response.Select(3, 6);
                Descendants(popup).OfType<Button>().Single(x => x.Text == "Later").Focus();
                popup.WindowState = FormWindowState.Minimized;
                var before = app.Engine.Snapshot.Timer;
                for (var i = 0; i < 3; i++) {
                    if (i < 2) FocusPending(app);
                    else { app.ShowReflections(); Application.DoEvents(); }
                    Is(response.ContainsFocus); Is(popup.WindowState != FormWindowState.Minimized); Is(!main.Visible);
                    Equal("An edited synthetic draft", response.Text); Equal(3, response.SelectionStart); Equal(6, response.SelectionLength);
                    Equal(popup, Application.OpenForms.OfType<ReflectionWindow>().Single());
                    Equal(before, app.Engine.Snapshot.Timer); Equal(1, app.Engine.Snapshot.Prompts.Count); Equal(0, app.Engine.Snapshot.Outbox.Count);
                    Is(!app.Engine.Snapshot.ShowFloatingTimer);
                }
                if (kind == "early") Equal("Synthetic reason", Descendants(popup).OfType<TextBox>().Single(x => x.AccessibleName == "Reason for ending early").Text);
                Is(popup.PersistDraft());
                Equal("An edited synthetic draft", new EncryptedStore(directory).Load().Prompts.Single().Draft);
            }, new AppState { ExtensionDisabledConfirmed = true, Timer = new() { Volume = 0 }, Prompts = [prompt] });
        });
        Test("Pending reflections reopens Later with the existing draft and never duplicates or submits it", () => {
            var prompt = new ReflectionPrompt(Guid.NewGuid(), DateTimeOffset.Now.ToUnixTimeMilliseconds(), 30, 0, false, "Synthetic postponed draft");
            WithEndEarlyApp((app, _) => {
                TimerShortcutControls(app); Application.DoEvents();
                var popup = Application.OpenForms.OfType<ReflectionWindow>().Single();
                Descendants(popup).OfType<Button>().Single(x => x.Text == "Later").PerformClick(); Application.DoEvents();
                Equal(0, Application.OpenForms.OfType<ReflectionWindow>().Count());
                FocusPending(app);
                var reopened = Application.OpenForms.OfType<ReflectionWindow>().Single();
                Is(ReflectionResponse(reopened).ContainsFocus); Equal(prompt.Draft, ReflectionResponse(reopened).Text);
                Equal(prompt.Id, app.Engine.Snapshot.Prompts.Single().Id); Equal(0, app.Engine.Snapshot.Outbox.Count);
            }, new AppState { ExtensionDisabledConfirmed = true, Timer = new() { Volume = 0 }, Prompts = [prompt] });
        });
        foreach (var running in new[] { false, true }) Test("Pending reflections with no reflection does not create one or alter the timer; running=" + running, () => {
            WithEndEarlyApp((app, _) => {
                app.SetFloatingTimer(false);
                var (main, _, _, _) = TimerShortcutControls(app); main.Hide();
                if (running) app.Engine.Start(9000, true, 0);
                var before = app.Engine.Snapshot.Timer;
                FocusPending(app); FocusPending(app);
                Equal(before, app.Engine.Snapshot.Timer); Equal(0, app.Engine.Snapshot.Prompts.Count);
                Equal(0, app.Engine.Snapshot.Outbox.Count); Equal(0, Application.OpenForms.OfType<ReflectionWindow>().Count());
                Is(main.Visible); Is(Descendants(main).OfType<Label>().Any(x => x.Text.StartsWith("No reflections are waiting.")));
                Is(!app.Engine.Snapshot.ShowFloatingTimer);
            });
        });
        Test("comma conflict is reported and cleanup releases only the other four shortcuts", () => {
            var api = new FakeHotKey { BlockedKey = GlobalShortcut.ReflectionFocusKey };
            WithEndEarlyApp((app, _) => {
                app.EnableGlobalShortcut(api); app.EnableGlobalShortcut(api);
                Is(!ReflectionFocusShortcut(app).Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.ReflectionFocusId));
                var main = (MainWindow)typeof(TimerApplication).GetField("main", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
                Is(Descendants(main).OfType<Label>().Any(x => x.Text.StartsWith("Ctrl+Alt+, (comma) could not be registered")));
            });
            Equal(5, api.Registrations.Count); Equal(4, api.Unregistrations.Count);
            Is(!api.Unregistrations.Any(x => x.Id == GlobalShortcut.ReflectionFocusId));
        });
    }
}
