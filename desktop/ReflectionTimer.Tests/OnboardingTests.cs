using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private static void TestOnboarding()
    {
        TestSetupRecovery();
        Test("fresh users have no developer spreadsheet, endpoint, secret, drafts or outbox", () => {
            var state = new AppState(); Equal("", state.Connection.SheetUrl); Equal("", state.Connection.WebAppUrl); Equal("", state.Connection.ApiToken);
            Is(state.SetupDraft is null); Equal(0, state.Prompts.Count); Equal(0, state.Outbox.Count);
        });
        Test("setup generates independent 256-bit tokens and accepts only exact Google Sheets file URLs", () => {
            var tokens = Enumerable.Range(0, 100).Select(_ => ConnectionSetup.NewToken()).ToArray();
            Equal(100, tokens.Distinct().Count()); Is(tokens.All(x => System.Text.RegularExpressions.Regex.IsMatch(x, "^[a-f0-9]{64}$")));
            Equal("synthetic-spreadsheet-id-for-tests", ConnectionSetup.SpreadsheetId(Connection.SheetUrl));
            foreach (var url in new[] { "http://docs.google.com/spreadsheets/d/synthetic-spreadsheet-id-for-tests/edit", "https://docs.google.com.evil.test/spreadsheets/d/synthetic-spreadsheet-id-for-tests/edit", "https://evil.test", "https://docs.google.com/spreadsheets/d/short", "https://user@docs.google.com/spreadsheets/d/synthetic-spreadsheet-id-for-tests/edit" })
                Throws<ArgumentException>(() => ConnectionSetup.SpreadsheetId(url));
        });
        Test("private setup codes roundtrip the user's connection but no timer or activity data", () => {
            var connection = Connection with { ApiToken = ConnectionSetup.NewToken(), SheetMode = "fixed", SheetName = "My own tab" };
            var code = ConnectionSetup.Export(connection); Equal(connection, ConnectionSetup.Import(code));
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(code[ConnectionSetup.CodePrefix.Length..]));
            Is(json.Contains(connection.ApiToken)); Is(!json.Contains("Timer") && !json.Contains("Prompts"));
            foreach (var bad in new[] { "not a setup code", ConnectionSetup.CodePrefix + "garbage", ConnectionSetup.CodePrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"ApiToken\":null}")), code + new string('x', 16000) })
                Throws<ArgumentException>(() => ConnectionSetup.Import(bad));
        });
        Test("setup script is self-contained, personalized only at the user's explicit copy, and rejects source injection", () => {
            var receiver = SetupWindow.ReadConnection(); Is(receiver.Contains("const APP_VERSION = '2.7.0'"));
            Is(!receiver.Contains("function setupReflectionTimer()"));
            var draft = Connection with { ApiToken = ConnectionSetup.NewToken() };
            var script = ConnectionSetup.BuildScript(receiver, draft);
            Is(script.Contains(draft.ApiToken)); Is(script.Contains(ConnectionSetup.SpreadsheetId(draft.SheetUrl)));
            Is(script.Contains("function setupReflectionTimer()"));
            Throws<ArgumentException>(() => ConnectionSetup.BuildScript(receiver, draft with { ApiToken = "'); evil(); //" }));
        });
        Test("setup draft is encrypted and does not activate a connection or appear in diagnostics", () => {
            WithEndEarlyApp((app, directory) => {
                var before = app.Engine.Snapshot.Connection; var draft = Connection with { ApiToken = ConnectionSetup.NewToken() };
                app.Engine.SaveSetupDraft(draft); Equal(before, app.Engine.Snapshot.Connection);
                Equal(draft, new EncryptedStore(directory).Load().SetupDraft);
                Is(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(directory, "state.dat"))).Contains(draft.ApiToken));
                var report = JsonSerializer.Serialize(app.Log.Report(app.Engine.Snapshot));
                Is(!report.Contains(draft.ApiToken)); Is(!report.Contains(draft.WebAppUrl)); Is(!report.Contains(draft.SheetUrl));
            });
        });
        Test("setup completion preserves themes audio schedules history and timer preferences", () => {
            var f = new Fixture(); var audio = new AudioSettings { LowTimeThresholdSeconds = 23, Success = new() { Volume = 47, Track = LibrarySound.None } };
            f.Store.Data = new AppState { Theme = AppColorTheme.Glamour, Audio = audio, ShowFloatingTimer = true, LoggingEnabled = false, StartAtLogin = true,
                Timer = new() { DurationSeconds = 80, RemainingSeconds = 80, Volume = 27, AutoRestart = true },
                Schedules = [new(Guid.NewGuid(), f.Engine.Now + 900000, 300, false, 40)] };
            var engine = f.Restart(); var before = engine.Snapshot;
            engine.SaveSetupDraft(Connection); engine.CompleteSetup(Connection, true); var after = engine.Snapshot;
            Equal(before.Timer, after.Timer); Equal(before.Theme, after.Theme); Equal(before.Audio, after.Audio);
            Is(before.Schedules.SequenceEqual(after.Schedules)); Is(after.StartAtLogin && !after.LoggingEnabled && after.ShowFloatingTimer);
            Equal(Connection, after.Connection); Is(after.ExtensionDisabledConfirmed); Is(after.SetupDraft is null);
        });
        foreach (var state in new[] { "running", "paused", "draft", "outbox" }) Test("setup cannot redirect " + state + " work to another account", () => {
            var f = new Fixture(); f.Engine.SaveSettings(Connection, true, false, true);
            if (state == "draft") f.Engine.TestPrompt();
            else if (state == "outbox") { var id = f.Engine.TestPrompt(); f.Engine.QueueReflection(id, "Synthetic private entry"); }
            else { f.Engine.Start(300, false, 0); if (state == "paused") { f.Move(10); f.Engine.Pause(); } }
            var before = JsonSerializer.Serialize(f.Engine.Snapshot);
            Throws<InvalidOperationException>(() => f.Engine.CompleteSetup(Connection with { SheetUrl = "https://docs.google.com/spreadsheets/d/a-different-synthetic-spreadsheet/edit" }, true));
            Equal(before, JsonSerializer.Serialize(f.Engine.Snapshot));
        });
        Test("failed setup save and missing extension confirmation preserve the old connection", () => {
            var f = new Fixture(); f.Engine.SaveSetupDraft(Connection);
            Throws<ArgumentException>(() => f.Engine.CompleteSetup(Connection, false));
            f.Store.Fail = true; Throws<IOException>(() => f.Engine.CompleteSetup(Connection, true));
            Equal("", f.Engine.Snapshot.Connection.SheetUrl); Equal(Connection, f.Engine.Snapshot.SetupDraft);
        });
        foreach (var state in new[] { "running", "paused", "draft", "outbox" }) Test("ordinary settings also protect " + state + " work from account changes", () => {
            var f = new Fixture(); f.Engine.SaveSettings(Connection, true, false, true);
            if (state == "draft") f.Engine.TestPrompt();
            else if (state == "outbox") f.Queue();
            else { f.Engine.Start(300, false, 0); if (state == "paused") f.Engine.Pause(); }
            var before = JsonSerializer.Serialize(f.Engine.Snapshot);
            Throws<InvalidOperationException>(() => f.Engine.SaveSettings(Connection with { SheetUrl = "https://docs.google.com/spreadsheets/d/another-synthetic-spreadsheet-id/edit" }, false, true, true, 17));
            Equal(before, JsonSerializer.Serialize(f.Engine.Snapshot));
            // Correcting the token and cosmetic URL suffix is safe: identity does not change.
            f.Engine.SaveSettings(Connection with { ApiToken = "replacement-synthetic-private-token", SheetUrl = Connection.SheetUrl + "?usp=sharing" }, false, false, true);
            Equal("replacement-synthetic-private-token", f.Engine.Snapshot.Connection.ApiToken);
        });
        foreach (var guided in new[] { true, false }) Test("first " + (guided ? "guided" : "manual") + " connection safely adopts never-sent offline work", () => {
            var f = new Fixture(); var id = f.Queue(); var before = f.Engine.Snapshot.Outbox.Single();
            f.Engine.Start(300, false, 0); var prompt = f.Engine.TestPrompt(); f.Engine.SaveDraft(prompt, "Preserve my local draft");
            if (guided) f.Engine.CompleteSetup(Connection, true);
            else f.Engine.SaveSettings(Connection, true, false, true);
            var after = f.Engine.Snapshot.Outbox.Single();
            Equal(id, after.Id); Equal(before.Message, after.Message); Equal(before.SubmittedAt, after.SubmittedAt);
            Equal(Connection.SheetUrl, after.SheetUrl); Equal(Connection.WebAppUrl, after.ReceiverUrl); Equal(0, after.Attempts);
            Equal("Preserve my local draft", f.Engine.Snapshot.Prompts.Single().Draft); Is(f.Engine.Snapshot.Timer.IsRunning);
        });
        Test("an incomplete manual save cannot strand wholly unbound offline entries", () => {
            var f = new Fixture(); f.Queue();
            f.Engine.SaveSettings(new() { SheetUrl = Connection.SheetUrl }, true, false, true);
            f.Engine.CompleteSetup(Connection, true);
            Equal(Connection.WebAppUrl, f.Engine.Snapshot.Outbox.Single().ReceiverUrl);
        });
        Test("attempted entries or entries bound to another account are never adopted by first setup", () => {
            foreach (var partial in new[] { true, false }) {
                var f = new Fixture(); f.Queue();
                f.Store.Data = f.Engine.Snapshot;
                f.Store.Data.Outbox[0] = f.Store.Data.Outbox[0] with { Attempts = partial ? 0 : 1, SheetUrl = partial ? "https://docs.google.com/spreadsheets/d/foreign-synthetic-spreadsheet-id/edit" : "" };
                var engine = f.Restart();
                Throws<InvalidOperationException>(() => engine.CompleteSetup(Connection, true));
                Equal("", engine.Snapshot.Connection.WebAppUrl);
            }
        });
        Test("changing a pending prompt's tab is blocked while outbox-only route changes retain snapshots", () => {
            var f = new Fixture(); f.Engine.SaveSettings(Connection, true, false, true); var id = f.Engine.TestPrompt();
            var next = Connection with { SheetMode = "fixed", SheetName = "Different tab" };
            Throws<InvalidOperationException>(() => f.Engine.CompleteSetup(next, true));
            f.Engine.QueueReflection(id, "Synthetic entry"); f.Engine.CompleteSetup(next, true);
            Equal("date", f.Engine.Snapshot.Outbox.Single().SheetMode);
        });
        Test("successful manual connection changes clear stale setup drafts but preference-only saves retain them", () => {
            var f = new Fixture(); f.Engine.SaveSetupDraft(Connection);
            f.Engine.SaveSettings(f.Engine.Snapshot.Connection, false, false, true); Equal(Connection, f.Engine.Snapshot.SetupDraft);
            f.Engine.SaveSettings(Connection, true, false, true); Is(f.Engine.Snapshot.SetupDraft is null);
        });
        Test("connection validation safely rejects null fields and unexpected endpoint decorations", () => {
            Is(SheetsClient.Validate(null) is not null);
            foreach (var value in new[] { Connection with { SheetUrl = null! }, Connection with { ApiToken = null! },
                Connection with { WebAppUrl = Connection.WebAppUrl + "?token=unsafe" }, Connection with { WebAppUrl = Connection.WebAppUrl + "#fragment" },
                Connection with { WebAppUrl = "https://script.google.com/macros/s/%2F/exec" }, Connection with { ApiToken = new string('x', 513) } })
                Is(SheetsClient.Validate(value) is not null);
        });
        Test("setup fits a small laptop working area and keeps navigation inside the window", () => {
            WithEndEarlyApp((app, _) => {
                using var window = new SetupWindow(app, Connection);
                window.Show(); Application.DoEvents(); var area = new Rectangle(0, 0, 800, 728); window.FitToWorkingArea(area); Application.DoEvents();
                if (!area.Contains(window.Bounds)) throw new Exception($"Setup bounds {window.Bounds}, minimum {window.MinimumSize}, exceed {area} at DPI {window.DeviceDpi}.");
                var tabs = Descendants(window).OfType<TabControl>().Single();
                foreach (var theme in Enum.GetValues<AppColorTheme>()) {
                    AppTheme.Change(theme);
                    for (var index = 0; index < tabs.TabPages.Count; index++) {
                        tabs.SelectedIndex = index; Application.DoEvents();
                        foreach (var button in Descendants(window).OfType<Button>().Where(x => x.Text is "Save and finish" or "Close setup")) {
                            var rect = window.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
                            Is(window.ClientRectangle.Contains(rect));
                        }
                        foreach (var frame in Descendants(tabs.SelectedTab!).OfType<InputFrame>())
                            Is(frame.Width <= frame.Parent!.ClientSize.Width);
                    }
                }
                AppTheme.Change(AppColorTheme.Dark);
            });
        });
        foreach (var scenario in new[] { "ok", "normal-failed", "test-failed", "old-receiver" })
            Test("guided setup checks both destinations without writing: " + scenario, () => {
                WithEndEarlyApp((app, _) => {
                    var calls = new List<bool>();
                    using var window = new SetupWindow(app, Connection, (connection, cancellation, test) => {
                        calls.Add(test); Equal(Connection, connection);
                        return Task.FromResult(new SheetReply(!(scenario == "normal-failed" || (test && scenario == "test-failed")), "rejected", "Synthetic check",
                            Target: test ? "Own spreadsheet / test" : "Own spreadsheet / today", SupportsSafeRetry: true, SupportsCheckIns: scenario != "old-receiver"));
                    });
                    ((Task)typeof(SetupWindow).GetMethod("CheckConnection", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [])!).GetAwaiter().GetResult();
                    var finish = Descendants(window).OfType<Button>().Single(x => x.Text == "Save and finish");
                    Equal(scenario == "ok", finish.Enabled); Equal("", app.Engine.Snapshot.Connection.SheetUrl);
                    if (scenario == "ok") {
                        Is(calls.Contains(false) && calls.Contains(true));
                        typeof(SetupWindow).GetMethod("Finish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, []);
                        Equal(Connection, app.Engine.Snapshot.Connection); Is(window.Connected);
                    }
                    else Is(!window.Connected);
                    Equal(0, app.Engine.Snapshot.Outbox.Count); Equal(0, app.Engine.Snapshot.Prompts.Count);
                });
            });
        Test("all guided setup themes preserve masked private fields and values", () => {
            WithEndEarlyApp((app, _) => {
                using var window = new SetupWindow(app, Connection);
                foreach (var theme in Enum.GetValues<AppColorTheme>()) {
                    AppTheme.Change(theme); Equal(AppTheme.Background, window.BackColor);
                    var token = Descendants(window).OfType<TextBox>().Single(x => x.AccessibleName == "Your private setup token");
                    Is(token.UseSystemPasswordChar); Equal(Connection.ApiToken, token.Text); Equal(AppTheme.Field, token.BackColor);
                }
                AppTheme.Change(AppColorTheme.Dark);
            });
        });
        Test("missing MP3 files fall back to distinct audible built-in tones", () => {
            foreach (var kind in Enum.GetValues<SoundEvent>()) {
                Equal("builtin:" + kind, SoundLibrary.Resolve(kind, new(), Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
                var tone = new BuiltInTone(kind); var buffer = new float[44100]; var count = tone.Read(buffer, 0, buffer.Length);
                Is(count > 0 && count <= buffer.Length); Is(buffer.Any(x => Math.Abs(x) > .01f)); Is(buffer.All(x => float.IsFinite(x) && Math.Abs(x) <= .18f));
                while (tone.Read(buffer, 0, buffer.Length) > 0) { }
                Equal(0, tone.Read(buffer, 0, buffer.Length));
                Is(SoundLibrary.Resolve(kind, new() { Track = LibrarySound.None }) is null);
            }
        });
        TestPackageInstallation();
    }
    private static void TestPackageInstallation()
    {
        Test("installer rejects traversal rooted paths and invalid or corrupted payloads", () => {
            var root = Path.Combine(Path.GetTempPath(), "ReflectionTimer-QA-package-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            foreach (var path in new[] { "../outside.exe", "..\\outside.exe", "C:\\outside.exe", "/outside.exe", "file:stream", "nested//file.dll", "folder./app.dll", "app.dll ", "CON.exe", "folder/LPT1.txt", "folder/quo\"te.dll" })
                Throws<InvalidDataException>(() => LocalInstallation.SafeChild(root, path));
            WriteTestPackage(root); LocalInstallation.ReadManifest(root);
            File.AppendAllText(Path.Combine(root, "ReflectionTimer.exe"), "changed"); Throws<InvalidDataException>(() => LocalInstallation.ReadManifest(root));
        });
        Test("installer rejects malformed and null manifest entries with a useful error", () => {
            var root = Path.Combine(Path.GetTempPath(), "ReflectionTimer-QA-manifest-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var path = Path.Combine(root, LocalInstallation.ManifestName);
            File.WriteAllText(path, "{broken"); Throws<InvalidDataException>(() => LocalInstallation.ReadManifest(root));
            WriteTestPackage(root);
            var valid = File.ReadAllText(path);
            File.WriteAllText(path, valid.Replace("\"Files\":[", "\"Files\":[null,"));
            Throws<InvalidDataException>(() => LocalInstallation.ReadManifest(root));
        });
        Test("installer backs up only its target and retains personal MP3s without touching data", () => {
            var root = Path.Combine(Path.GetTempPath(), "ReflectionTimer-QA-install-" + Guid.NewGuid().ToString("N"));
            var source = Path.Combine(root, "download"); var target = Path.Combine(root, "Programs", "ReflectionTimerDesktop");
            Directory.CreateDirectory(source); Directory.CreateDirectory(target); WriteTestPackage(source);
            File.WriteAllText(Path.Combine(target, "ReflectionTimer.exe"), "old app"); File.WriteAllText(Path.Combine(target, "popup.mp3"), "private local sound");
            var data = Path.Combine(root, "state.dat"); File.WriteAllText(data, "synthetic encrypted state");
            var backup = LocalInstallation.InstallFiles(source, target, () => false)!;
            Equal("old app", File.ReadAllText(Path.Combine(backup, "ReflectionTimer.exe")));
            Equal("private local sound", File.ReadAllText(Path.Combine(target, "popup.mp3")));
            Equal("synthetic encrypted state", File.ReadAllText(data)); LocalInstallation.ReadManifest(target);
            Throws<InvalidOperationException>(() => LocalInstallation.InstallFiles(source, target, () => true));
            Equal("private local sound", File.ReadAllText(Path.Combine(target, "popup.mp3")));
        });
        Test("installer updates bundled MP3s and preserves additional user tracks across repeated upgrades", () => {
            var root = Path.Combine(Path.GetTempPath(), "ReflectionTimer-QA-bundled-audio-" + Guid.NewGuid().ToString("N"));
            var source = Path.Combine(root, "download"); var target = Path.Combine(root, "Programs", "ReflectionTimerDesktop");
            Directory.CreateDirectory(source); Directory.CreateDirectory(target);
            WriteTestPackage(source, "popup.mp3", "pokemon-level-up.mp3");
            File.WriteAllText(Path.Combine(target, "popup.mp3"), "old session sound");
            File.WriteAllText(Path.Combine(target, "pokemon-level-up.mp3"), "old success sound");
            File.WriteAllText(Path.Combine(target, "my-custom-sound.mp3"), "user audio");
            var backup = LocalInstallation.InstallFiles(source, target, () => false)!;
            Equal("old session sound", File.ReadAllText(Path.Combine(backup, "popup.mp3")));
            Equal("old success sound", File.ReadAllText(Path.Combine(backup, "pokemon-level-up.mp3")));
            foreach (var name in new[] { "popup.mp3", "pokemon-level-up.mp3" })
                Equal(File.ReadAllText(Path.Combine(source, name)), File.ReadAllText(Path.Combine(target, name)));
            Equal("user audio", File.ReadAllText(Path.Combine(target, "my-custom-sound.mp3")));
            LocalInstallation.ReadManifest(target);
            LocalInstallation.InstallFiles(source, target, () => false);
            Equal("user audio", File.ReadAllText(Path.Combine(target, "my-custom-sound.mp3")));
            LocalInstallation.ReadManifest(target);
            File.AppendAllText(Path.Combine(source, "pokemon-level-up.mp3"), "corrupted audio");
            Throws<InvalidDataException>(() => LocalInstallation.ReadManifest(source));
        });
    }
    private static void WriteTestPackage(string directory, params string[] extraFiles)
    {
        var files = new[] { "ReflectionTimer.exe", "ReflectionTimer.dll", "START-HERE.html", "google-sheets-script.gs" }.Concat(extraFiles).Select(name => {
            var path = Path.Combine(directory, name); File.WriteAllText(path, "synthetic package payload " + name);
            return new PackageFile(name, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        }).ToList();
        File.WriteAllText(Path.Combine(directory, LocalInstallation.ManifestName), JsonSerializer.Serialize(new PackageManifest(1, "3.12.0", "win-x64", files)));
    }
    private static void RenderSetupPreview(string directory)
    {
        Directory.CreateDirectory(directory);
        WithEndEarlyApp((app, _) => {
            using var window = new SetupWindow(app, new()); window.Show(); Application.DoEvents();
            foreach (var theme in Enum.GetValues<AppColorTheme>()) {
                AppTheme.Change(theme);
                var tabs = Descendants(window).OfType<TabControl>().Single();
                for (var page = 0; page < 3; page++) {
                    tabs.SelectedIndex = page; Application.DoEvents();
                    using var image = new Bitmap(window.Width, window.Height); window.DrawToBitmap(image, new Rectangle(Point.Empty, window.Size));
                    image.Save(Path.Combine(directory, $"{theme}-step-{page + 1}.png"));
                }
            }
            window.FitToWorkingArea(new Rectangle(0, 0, 1024, 728));
            foreach (var theme in new[] { AppColorTheme.Dark, AppColorTheme.Glamour }) {
                AppTheme.Change(theme);
                var tabs = Descendants(window).OfType<TabControl>().Single();
                for (var page = 0; page < 3; page++) {
                    tabs.SelectedIndex = page; Application.DoEvents();
                    using var image = new Bitmap(window.Width, window.Height); window.DrawToBitmap(image, new Rectangle(Point.Empty, window.Size));
                    image.Save(Path.Combine(directory, $"{theme}-small-step-{page + 1}.png"));
                }
            }
            AppTheme.Change(AppColorTheme.Dark);
        });
    }
}
