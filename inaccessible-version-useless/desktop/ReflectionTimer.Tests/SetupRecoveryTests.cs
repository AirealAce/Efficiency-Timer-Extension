using System.Reflection;
using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private static void TestSetupRecovery()
    {
        foreach (var missing in new[] { "sheet", "receiver", "both-invalid" }) Test("incomplete first setup can be completed with a running timer draft and unsent entry: " + missing, () => {
            var f = new Fixture();
            var incomplete = missing switch {
                "sheet" => Connection with { SheetUrl = "" },
                "receiver" => Connection with { WebAppUrl = "" },
                _ => Connection with { SheetUrl = "https://docs.google.com/spreadsheets/d/partial", WebAppUrl = "https://script.google.com/" }
            };
            f.Engine.SaveSettings(incomplete, true, false, true); f.Engine.Start(300, false, 0); var id = f.Queue();
            var before = f.Engine.Snapshot.Outbox.Single(); var prompt = f.Engine.TestPrompt(); f.Engine.SaveDraft(prompt, "Preserve this draft");
            f.Engine.CompleteSetup(Connection, true); var after = f.Engine.Snapshot.Outbox.Single();
            Equal(id, after.Id); Equal(before.Message, after.Message); Equal(before.SubmittedAt, after.SubmittedAt); Equal(0, after.Attempts);
            Equal(Connection.SheetUrl, after.SheetUrl); Equal(Connection.WebAppUrl, after.ReceiverUrl);
            Is(f.Engine.Snapshot.Timer.IsRunning); Equal("Preserve this draft", f.Engine.Snapshot.Prompts.Single().Draft);
        });
        Test("completing setup cannot change its already known account or receiver", () => {
            foreach (var incomplete in new[] { Connection with { WebAppUrl = "" }, Connection with { SheetUrl = "" } }) {
                var f = new Fixture(); f.Engine.SaveSettings(incomplete, true, false, true); f.Engine.Start(300, false, 0); f.Queue();
                var other = Connection with { SheetUrl = "https://docs.google.com/spreadsheets/d/other-synthetic-spreadsheet-id/edit", WebAppUrl = "https://script.google.com/macros/s/other/exec" };
                Throws<InvalidOperationException>(() => f.Engine.CompleteSetup(other, true)); Equal(incomplete, f.Engine.Snapshot.Connection);
            }
        });
        Test("an attempted incomplete destination is not rewritten during setup", () => {
            var f = new Fixture(); f.Engine.SaveSettings(Connection with { WebAppUrl = "" }, true, false, true); f.Queue();
            f.Engine.BeginUpload(); f.Engine.FinishUpload(f.Engine.Snapshot.Outbox.Single().Id, false, "settings_required");
            Throws<InvalidOperationException>(() => f.Engine.CompleteSetup(Connection, true)); Equal("", f.Engine.Snapshot.Outbox.Single().ReceiverUrl);
        });
        Test("closing setup saves edited credentials and existing-receiver choice without activating them", () => {
            WithEndEarlyApp((app, directory) => {
                using (var window = new SetupWindow(app, new())) {
                    window.Show(); Application.DoEvents();
                    SetupField(window, "Your Google Sheets URL").Text = Connection.SheetUrl;
                    Descendants(window).OfType<CheckBox>().Single(x => x.Text.StartsWith("Use an existing receiver")).Checked = true;
                    SetupField(window, "Your private setup token").Text = Connection.ApiToken;
                    window.Close(); Application.DoEvents();
                }
                var state = new EncryptedStore(directory).Load(); Equal("", state.Connection.SheetUrl);
                Equal(Connection.SheetUrl, state.SetupDraft!.SheetUrl); Equal(Connection.ApiToken, state.SetupDraft.ApiToken); Equal(true, state.SetupDraftUsesExistingReceiver);
                using var reopened = new SetupWindow(app, state.SetupDraft);
                Is(!SetupField(reopened, "Your private setup token").ReadOnly);
                Equal(Connection.ApiToken, SetupField(reopened, "Your private setup token").Text);
            });
        });
        Test("private setup code exports the saved connection instead of an unfinished alternative", () => {
            WithEndEarlyApp((app, _) => {
                app.Engine.CompleteSetup(Connection, true);
                using var window = new SetupWindow(app, Connection with { WebAppUrl = "https://script.google.com/macros/s/unverified-alternative/exec" });
                Equal(Connection, ConnectionSetup.Import(window.SavedConnectionCode()));
            });
            WithEndEarlyApp((app, _) => { using var window = new SetupWindow(app, Connection); Throws<InvalidOperationException>(() => window.SavedConnectionCode()); });
        });
        foreach (var accept in new[] { false, true }) Test("replacing a draft token requires confirmation: " + accept, () => {
            WithEndEarlyApp((app, _) => {
                var original = Connection with { WebAppUrl = "", ApiToken = ConnectionSetup.NewToken() }; app.Engine.SaveSetupDraft(original, false);
                var questions = 0;
                using var window = new SetupWindow(app, original, confirm: _ => { questions++; return accept; });
                window.GeneratePrivateToken(); Equal(1, questions);
                var token = SetupField(window, "Your private setup token").Text;
                Equal(accept, token != original.ApiToken); Equal(token, app.Engine.Snapshot.SetupDraft!.ApiToken);
            });
        });
        Test("a failed token save retains the old token in both the window and stored draft", () => {
            WithEndEarlyApp((app, directory) => {
                var original = Connection with { WebAppUrl = "", ApiToken = ConnectionSetup.NewToken() }; app.Engine.SaveSetupDraft(original, false);
                using var window = new SetupWindow(app, original, confirm: _ => true);
                Directory.CreateDirectory(Path.Combine(directory, "state.dat.tmp"));
                Throws<UnauthorizedAccessException>(() => window.GeneratePrivateToken());
                Equal(original.ApiToken, SetupField(window, "Your private setup token").Text); Equal(original, app.Engine.Snapshot.SetupDraft);
            });
        });
        foreach (var discard in new[] { false, true }) Test("failed close-time draft save requires an explicit discard: " + discard, () => {
            WithEndEarlyApp((app, directory) => {
                var questions = 0; using var window = new SetupWindow(app, new(), confirm: _ => { questions++; return discard; });
                window.Show(); Application.DoEvents(); SetupField(window, "Your Google Sheets URL").Text = Connection.SheetUrl;
                Directory.CreateDirectory(Path.Combine(directory, "state.dat.tmp"));
                window.Close(); Application.DoEvents(); Equal(1, questions); Equal(!discard, window.Visible); Equal("", app.Engine.Snapshot.Connection.SheetUrl);
                if (!discard) Equal(Connection.SheetUrl, SetupField(window, "Your Google Sheets URL").Text);
            });
        });
        Test("closing during a connection check cancels it and ignores a late successful result", () => {
            WithEndEarlyApp((app, _) => {
                var pending = new TaskCompletionSource<SheetReply>(); var calls = 0; CancellationToken cancellation = default;
                using var window = new SetupWindow(app, Connection, (_, token, _) => { calls++; cancellation = token; return pending.Task; });
                window.Show(); Application.DoEvents();
                var method = typeof(SetupWindow).GetMethod("CheckConnection", BindingFlags.NonPublic | BindingFlags.Instance)!;
                var check = (Task)method.Invoke(window, [])!; var duplicate = (Task)method.Invoke(window, [])!; Is(duplicate.IsCompleted); Equal(1, calls);
                window.Close(); Application.DoEvents(); Is(cancellation.IsCancellationRequested);
                pending.SetResult(new(true, "", "Synthetic", Target: "Synthetic / test", SupportsSafeRetry: true, SupportsCheckIns: true));
                var limit = DateTime.UtcNow.AddSeconds(3);
                while (!check.IsCompleted && DateTime.UtcNow < limit) Application.DoEvents();
                Is(check.IsCompletedSuccessfully); Equal(1, calls); Is(!window.Connected); Equal("", app.Engine.Snapshot.Connection.SheetUrl);
            });
        });
        TestInstallationRecovery();
    }
    private static TextBox SetupField(SetupWindow window, string name) => Descendants(window).OfType<TextBox>().Single(x => x.AccessibleName == name);
    private static void TestInstallationRecovery()
    {
        Test("installer retains its validated manifest if the download changes during installation", () => {
            var root = Path.Combine(Path.GetTempPath(), "ReflectionTimer-QA-manifest-snapshot-" + Guid.NewGuid().ToString("N"));
            var source = Path.Combine(root, "download"); var target = Path.Combine(root, "Programs", "ReflectionTimerDesktop");
            Directory.CreateDirectory(source); WriteTestPackage(source); var checks = 0;
            LocalInstallation.InstallFiles(source, target, () => {
                if (++checks == 2) File.WriteAllText(Path.Combine(source, LocalInstallation.ManifestName), "{damaged after verification");
                return false;
            });
            LocalInstallation.ReadManifest(target); Equal(2, checks);
        });
        foreach (var failRestore in new[] { false, true }) Test("failed installation retains old binaries with recoverable rollback: " + failRestore, () => {
            var root = Path.Combine(Path.GetTempPath(), "ReflectionTimer-QA-rollback-" + Guid.NewGuid().ToString("N"));
            var source = Path.Combine(root, "download"); var target = Path.Combine(root, "Programs", "ReflectionTimerDesktop");
            Directory.CreateDirectory(source); Directory.CreateDirectory(target); WriteTestPackage(source);
            File.WriteAllText(Path.Combine(target, "ReflectionTimer.exe"), "old app");
            string? error = null;
            try { LocalInstallation.InstallFiles(source, target, () => false, (from, to) => {
                if (from.Contains("-staging-") || (failRestore && from.Contains("-backup-"))) throw new IOException("Synthetic rename failure");
                Directory.Move(from, to);
            }); } catch (IOException exception) { error = exception.Message; }
            Is(error is not null);
            if (failRestore) {
                var backup = Directory.GetDirectories(Path.Combine(root, "Programs"), "*-backup-*").Single();
                Is(error!.Contains(backup)); Equal("old app", File.ReadAllText(Path.Combine(backup, "ReflectionTimer.exe")));
            } else Equal("old app", File.ReadAllText(Path.Combine(target, "ReflectionTimer.exe")));
        });
        Test("shortcut failure reports partial success and still attempts the remaining shortcut", () => {
            var root = Path.Combine(Path.GetTempPath(), "ReflectionTimer-QA-shortcuts-" + Guid.NewGuid().ToString("N"));
            var source = Path.Combine(root, "download"); var target = Path.Combine(root, "Programs", "ReflectionTimerDesktop");
            Directory.CreateDirectory(source); WriteTestPackage(source); var calls = new List<string>();
            var result = LocalInstallation.InstallWithShortcuts(source, target, () => false, ["first.lnk", "second.lnk"], (path, _) => {
                calls.Add(path); if (path == "first.lnk") throw new IOException("Synthetic conflicting shortcut");
            });
            Equal(2, calls.Count); Equal(1, result.ShortcutWarnings.Count); Is(result.ShortcutWarnings[0].Contains("first.lnk"));
            LocalInstallation.ReadManifest(target); Is(File.Exists(Path.Combine(target, "ReflectionTimer.exe")));
        });
    }
}
