using System.Reflection;
using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private static void TestSettingsInteractions()
    {
        Test("None follows Default in every sound picker and programmatic binding never previews", () => {
            foreach (var kind in Enum.GetValues<SoundEvent>()) {
                using var source = new SoundSourceControl(kind);
                var combo = Descendants(source).OfType<ComboBox>().Single();
                Equal("None", combo.Items[1]);
                var previews = new List<(SoundSetting Setting, bool Automatic)>();
                source.PreviewRequested += (setting, automatic) => previews.Add((setting, automatic));
                source.LoadSelection(new() { Track = LibrarySound.None }); Equal(1, combo.SelectedIndex); Equal(0, previews.Count);
                combo.SelectedItem = SoundLibrary.Name(LibrarySound.LevelUp);
                Equal(LibrarySound.LevelUp, source.Selection.Track); Equal(1, previews.Count); Is(previews[0].Automatic);
                combo.SelectedIndex = 1; Equal(LibrarySound.None, source.Selection.Track); Equal(2, previews.Count);
                Is(SoundLibrary.Resolve(kind, source.Selection) is null);
            }
            using var low = new LowTimeControl();
            var picker = Descendants(low).OfType<ComboBox>().Single(); Equal("None", picker.Items[1]);
            picker.SelectedIndex = 1; Equal(LibrarySound.None, low.Selection.Track);
            var audio = new AudioSettings { LowTime = new() { Track = LibrarySound.None } };
            Is(SoundLibrary.Resolve(SoundEvent.LowTime, audio.ForLowTime(new())) is null);
            Equal(LibrarySound.LevelUp, audio.ForLowTime(new() { Track = LibrarySound.LevelUp }).Track);
            var f = new Fixture(); f.Engine.SetSound(SoundEvent.SessionEnd, new() { Track = LibrarySound.None });
            f.Engine.SetLowTime(new() { Enabled = true, Track = LibrarySound.None });
            Equal(LibrarySound.None, AudioSettings.From(f.Restart().Snapshot).SessionEnd.Track);
            Equal(LibrarySound.None, f.Restart().Snapshot.Timer.LowTime.Track);
        });
        Test("autosaves are quiet, Ctrl+Enter explicitly saves, and Enter leaves the threshold", () => {
            // This fixture never uses real audio, Google credentials or startup registration.
            var directory = Path.Combine(Path.GetTempPath(), "ReflectionTimer-QA-settings-" + Guid.NewGuid().ToString("N"));
            var store = new EncryptedStore(directory); store.Save(new AppState { ExtensionDisabledConfirmed = true });
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var app = new TimerApplication(store, directory, show, enableAudio: true, audioBackend: new FakeAudio());
            var failSave = false;
            using var main = new MainWindow(app, _ => { if (failSave) throw new IOException("Test save failure"); });
            main.Render(app.Engine.Snapshot); main.Show();
            var tabs = Descendants(main).OfType<TabControl>().Single(); tabs.SelectedIndex = 3;
            var audio = Descendants(main).OfType<AudioSettingsControl>().Single();
            var number = Descendants(audio).OfType<NumericUpDown>().Single();
            bool Key(Keys keys) => (bool)typeof(MainWindow).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(main, [Message.Create(0, 0, 0, 0), keys])!;
            int Requests() => app.Log.Recent().Count(x => x.Event == "sound.requested");
            var count = Requests();
            var behavior = Descendants(audio).OfType<ComboBox>().Single(x => x.AccessibleName == "Success playback behavior");
            behavior.SelectedIndex = 2;
            Descendants(main).OfType<ComboBox>().Single(x => x.AccessibleName == "Reflection popup position").SelectedIndex = 2;
            Descendants(main).OfType<ComboBox>().Single(x => x.AccessibleName == "App theme").SelectedIndex = 1;
            Equal(count, Requests());
            Descendants(audio).OfType<ComboBox>().Single(x => x.AccessibleName == "Success sound").SelectedItem = SoundLibrary.Name(LibrarySound.LevelUp);
            Equal(count + 1, Requests()); Equal(1, app.Log.Recent().Count(x => x.Event == "sound.preview"));
            count = Requests(); // Exactly the selected sound's preview, not an extra autosave success chime.
            number.Focus(); number.Text = "123"; Is(Key(Keys.Enter)); Is(!number.ContainsFocus);
            Equal(count, Requests()); Equal(60, AudioSettings.From(app.Engine.Snapshot).LowTimeThresholdSeconds);
            var save = Descendants(main).OfType<Button>().Single(x => x.Text == "Save settings");
            Is(save.Visible); Is(save.Parent is TableLayoutPanel); Is(save.Parent!.Dock == DockStyle.Bottom);
            number.Focus(); Is(Key(Keys.Control | Keys.Enter));
            Equal(123, AudioSettings.From(app.Engine.Snapshot).LowTimeThresholdSeconds); Equal(count + 1, Requests());
            Equal(10 + (int)SoundBehavior.Polite, app.Log.Recent().Last(x => x.Event == "sound.requested").Value);
            failSave = true; Is(Key(Keys.Control | Keys.Enter));
            Equal(count + 2, Requests()); Equal(20, app.Log.Recent().Last(x => x.Event == "sound.requested").Value);
            tabs.SelectedIndex = 0; Is(!save.Visible);
            Key(Keys.Control | Keys.Enter); Equal(count + 2, Requests());
        });
    }

    private static async Task TestPreviewAudio()
    {
        await TestAsync("previews stop at five seconds and restore other audio without truncating it", async () => {
            var clock = new PreviewClock(); var backend = new ControlledAudio();
            using var player = new AlertSoundPlayer(backend, timeProvider: clock);
            var regular = player.PlayAsync("regular", 80, SoundBehavior.Polite, SoundEvent.SessionEnd);
            var baseVoice = await backend.Wait("regular"); Is(clock.Timer is null);
            var preview = player.PlayAsync("preview", 50, SoundBehavior.Assertive, SoundEvent.Success, preview: true);
            await backend.Wait("preview"); Equal(.2f, baseVoice.Level.Gain);
            clock.Expire(); Equal(AlertSoundResult.PreviewFinished, await preview.WaitAsync(TimeSpan.FromSeconds(2)));
            Equal(.8f, baseVoice.Level.Gain); Is(!regular.IsCompleted);
            baseVoice.Finish.TrySetResult(); Equal(AlertSoundResult.Played, await regular);
        });
        await TestAsync("new previews replace older previews even with Polite and None never falls back", async () => {
            var backend = new ControlledAudio(); using var player = new AlertSoundPlayer(backend);
            var regular = player.PlayAsync("regular", 70, SoundBehavior.Polite, SoundEvent.SessionEnd); await backend.Wait("regular");
            var first = player.PlayAsync("first", 50, SoundBehavior.Polite, SoundEvent.Success, preview: true); await backend.Wait("first");
            var second = player.PlayAsync("second", 50, SoundBehavior.Polite, SoundEvent.Success, preview: true); await backend.Wait("second");
            Equal(AlertSoundResult.Cancelled, await first); Is(!regular.IsCompleted);
            Equal(AlertSoundResult.Muted, await player.PlayAsync(null, 50, SoundBehavior.Disruptive, SoundEvent.Success, preview: true));
            Equal(AlertSoundResult.Cancelled, await second); Is(!regular.IsCompleted);
            Equal(AlertSoundResult.Muted, await player.PlayAsync(null, 50, SoundBehavior.Disruptive, SoundEvent.Failure));
            Is(!regular.IsCompleted); player.Stop(); await regular;
        });
        await TestAsync("fallback shares the same five-second preview budget", async () => {
            var clock = new PreviewClock(); var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var backend = new FakeAudio { Play = async (path, token) => {
                if (path == "missing") throw new IOException();
                reached.TrySetResult(); await Task.Delay(Timeout.Infinite, token);
            } };
            using var player = new AlertSoundPlayer(backend, timeProvider: clock);
            var preview = player.PlayAsync("missing", 50, SoundBehavior.Polite, SoundEvent.Success, "fallback", preview: true);
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Equal(1, clock.Timer!.Changes); clock.Expire(); Equal(AlertSoundResult.PreviewFinished, await preview);
            Equal(2, backend.Calls.Count);
        });
    }
    private sealed class PreviewClock : TimeProvider
    {
        public PreviewTimer? Timer;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => Timer = new(callback, state);
        public void Expire() { Equal(TimeSpan.FromSeconds(5), Timer!.Due); Timer.Callback(Timer.State); }
    }
    private sealed class PreviewTimer(TimerCallback callback, object? state) : ITimer
    {
        public TimerCallback Callback = callback;
        public object? State = state;
        public TimeSpan Due;
        public int Changes;
        public bool Change(TimeSpan dueTime, TimeSpan period) { Due = dueTime; ++Changes; return true; }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
