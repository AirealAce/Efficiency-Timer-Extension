using System.Reflection;
using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private static void TestDurationInputs()
    {
        foreach (var (h, m, s, total, nh, nm, ns) in new[] {
            (1, 20, 100, 4900, 1, 21, 40), (1, 61, 59, 7319, 2, 1, 59),
            (0, 120, 120, 7320, 2, 2, 0), (0, 0, 3600, 3600, 1, 0, 0),
            (0, 525600, 0, TimerEngine.MaxDuration, 8760, 0, 0) })
            Test($"duration overflow {h}h {m}m {s}s normalizes without losing seconds", () => {
                using var control = new DurationControl(); control.LoadSeconds(0);
                var parts = Descendants(control).OfType<DurationPartInput>().ToArray();
                var previews = new List<int>(); control.UserChanged += () => { if (control.TryGetSeconds(out var n, out _)) previews.Add(n); };
                parts[0].Text = h.ToString(); parts[1].Text = m.ToString(); parts[2].Text = s.ToString();
                Equal(s.ToString(), parts[2].Text); Equal(total, control.Seconds); Equal(total, previews.Last());
                Equal(total, control.CommitSeconds()); Equal(nh.ToString(), parts[0].Text);
                Equal(nm.ToString(), parts[1].Text); Equal(ns.ToString(), parts[2].Text); Is(control.Dirty);
                var count = previews.Count; control.Normalize(); Equal(count, previews.Count);
            });
        Test("leaving duration fields or pressing Enter normalizes, but switching fields does not", () => {
            using var form = new Form { ClientSize = new(800, 300) };
            using var control = new DurationControl(); control.LoadSeconds(0);
            var done = new Button { Text = "Done", Top = 100 }; form.Controls.AddRange([control, done]);
            AppTheme.Apply(form); form.Show();
            var parts = Descendants(control).OfType<DurationPartInput>().ToArray();
            parts[1].Focus(); parts[1].Text = "120"; parts[2].Focus(); Equal("120", parts[1].Text);
            parts[2].Text = "100"; done.Focus();
            Equal("2", parts[0].Text); Equal("1", parts[1].Text); Equal("40", parts[2].Text); Is(control.Dirty);
            parts[2].Focus(); parts[2].Text = "100";
            var handled = (bool)typeof(DurationControl).GetMethod("ProcessCmdKey", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(control, [Message.Create(0, 0, 0, 0), Keys.Enter])!;
            Is(handled); Equal("2", parts[1].Text); Equal("40", parts[2].Text);
        });
        Test("oversized and invalid duration drafts stay intact rather than clipping or overflowing", () => {
            using var form = new Form(); using var control = new DurationControl(); control.LoadSeconds(0);
            var done = new Button { Top = 100 }; form.Controls.AddRange([control, done]); form.Show();
            var parts = Descendants(control).OfType<DurationPartInput>().ToArray();
            foreach (var invalid in new[] { "31536001", new string('9', 120), "-1", "1.5", "wrong" }) {
                parts[2].Focus(); parts[2].Text = invalid; done.Focus();
                Equal(invalid, parts[2].Text); Is(!control.TryGetSeconds(out _, out var error)); Is(!string.IsNullOrEmpty(error));
                Throws<ArgumentException>(() => control.CommitSeconds()); Equal(invalid, parts[2].Text);
            }
            parts[2].Focus(); parts[2].Text = ""; done.Focus(); Equal("0", parts[2].Text); Equal(0, control.Seconds);
        });
        Test("timer start and scheduled saves use normalized totals and preserve edited paused state", () => {
            var directory = Path.Combine(Path.GetTempPath(), "ReflectionTimer-QA-duration-" + Guid.NewGuid().ToString("N"));
            var store = new EncryptedStore(directory);
            store.Save(new AppState { ExtensionDisabledConfirmed = true, Timer = new() { DurationSeconds = 600, RemainingSeconds = 30, Volume = 0 } });
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var app = new TimerApplication(store, directory, show);
            using var main = new MainWindow(app, _ => { }); main.Render(app.Engine.Snapshot); main.Show();
            var tabs = Descendants(main).OfType<TabControl>().Single(); tabs.SelectedIndex = 0;
            var durations = Descendants(main).OfType<DurationControl>().ToArray();
            var regular = Descendants(durations[0]).OfType<DurationPartInput>().ToArray();
            regular[1].Text = "0"; regular[2].Text = "100";
            durations[0].Normalize(); main.Render(app.Engine.Snapshot);
            Is(durations[0].Dirty); Equal(100, durations[0].Seconds);
            Descendants(main).OfType<Button>().Single(x => x.Text == "Start").PerformClick();
            Equal(100, app.Engine.Snapshot.Timer.DurationSeconds); Is(app.Engine.Snapshot.Timer.IsRunning);
            app.Engine.Pause(); tabs.SelectedIndex = 1;
            var scheduled = Descendants(durations[1]).OfType<DurationPartInput>().ToArray();
            scheduled[0].Text = "0"; scheduled[1].Text = "120"; scheduled[2].Text = "120";
            Descendants(main).OfType<Button>().Single(x => x.Text == "Add session").PerformClick();
            Equal(7320, app.Engine.Snapshot.Schedules.Single().DurationSeconds);
            tabs.SelectedIndex = 0; regular[0].Text = new string('9', 100); main.RenderClock(); // No exception or clipped preview.
        });
    }
}
