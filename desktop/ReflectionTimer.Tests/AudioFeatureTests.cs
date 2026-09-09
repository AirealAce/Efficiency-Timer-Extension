using System.Collections.Concurrent;
using NAudio.Wave;
using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private static void TestLowTime()
    {
        Test("audio migration keeps existing session-end MP3 and defaults all behaviors to Disruptive", () => {
            var state = new AppState { AlertSoundPath = Path.Combine(Path.GetTempPath(), "existing.mp3") };
            var settings = AudioSettings.From(state); Equal(state.AlertSoundPath, settings.SessionEnd.Mp3Path);
            foreach (var kind in Enum.GetValues<SoundEvent>()) Equal(SoundBehavior.Disruptive, settings.For(kind).Behavior);
            Equal(15, settings.LowTimeThresholdSeconds); Is(state.Timer.LowTime.Enabled);
            Equal(LibrarySound.LevelUp, SoundLibrary.DefaultFor(SoundEvent.Success));
            Equal(LibrarySound.OutOfHealth, SoundLibrary.DefaultFor(SoundEvent.Failure));
            Equal(LibrarySound.TrainerBattle, SoundLibrary.DefaultFor(SoundEvent.LowTime));
        });
        Test("each sound and global threshold persist independently without touching the timer", () => {
            var f = new Fixture(); f.Engine.Start(900, true, 42); var timer = f.Engine.Snapshot.Timer;
            foreach (var kind in Enum.GetValues<SoundEvent>()) f.Engine.SetSound(kind, new() { Track = LibrarySound.LevelUp, Behavior = SoundBehavior.Polite });
            f.Engine.SetLowTimeDefault(300); var reloaded = f.Restart().Snapshot;
            Equal(timer, reloaded.Timer); Equal(300, AudioSettings.From(reloaded).LowTimeThresholdSeconds);
            foreach (var kind in Enum.GetValues<SoundEvent>()) Equal(SoundBehavior.Polite, AudioSettings.From(reloaded).For(kind).Behavior);
            f.Store.Fail = true; Throws<IOException>(() => f.Engine.SetSound(SoundEvent.Success, new()));
            Equal(SoundBehavior.Polite, AudioSettings.From(f.Engine.Snapshot).Success.Behavior);
        });
        Test("invalid sound and low-time options reject atomically", () => {
            var f = new Fixture();
            Throws<ArgumentException>(() => f.Engine.SetSound((SoundEvent)99, new()));
            Throws<ArgumentException>(() => f.Engine.SetSound(SoundEvent.Success, new() { Behavior = (SoundBehavior)99 }));
            Throws<ArgumentException>(() => f.Engine.SetSound(SoundEvent.Success, new() { Track = (LibrarySound)99 }));
            Throws<ArgumentException>(() => f.Engine.SetLowTimeDefault(0));
            Throws<ArgumentException>(() => f.Engine.SetLowTime(new() { ThresholdSeconds = -1 }));
            Throws<ArgumentException>(() => f.Engine.SetLowTime(new() { Mp3Path = "https://example.com/private.mp3" }));
            Equal(0, f.Store.Writes);
        });
        Test("low-time uses the global threshold once and does not write on every tick", () => {
            var f = new Fixture(); f.Engine.SetLowTimeDefault(10); f.Engine.SetLowTime(new() { Enabled = true });
            var alerts = new List<TimerState>(); f.Engine.LowTimeReached += alerts.Add; f.Engine.Start(30, false, 31);
            f.Move(19); f.Engine.Advance(); Equal(0, alerts.Count);
            f.Move(1); f.Engine.Advance(); Equal(1, alerts.Count); Equal(31, alerts[0].Volume); Is(f.Engine.Snapshot.Timer.LowTimePlayed);
            var writes = f.Store.Writes; f.Move(1); f.Engine.Advance(); Equal(writes, f.Store.Writes); Equal(1, alerts.Count);
        });
        Test("per-timer threshold overrides global and can revert to following it", () => {
            var f = new Fixture(); f.Engine.SetLowTimeDefault(20);
            f.Engine.Start(30, false, 0, lowTime: new() { Enabled = true, ThresholdSeconds = 5 });
            f.Move(10); f.Engine.Advance(); Is(!f.Engine.Snapshot.Timer.LowTimePlayed);
            f.Engine.SetLowTime(new() { Enabled = true }); f.Engine.Advance(); Is(f.Engine.Snapshot.Timer.LowTimePlayed);
            f.Engine.SetLowTime(new() { Enabled = false }); f.Engine.SetLowTime(new() { Enabled = true });
            var count = 0; f.Engine.LowTimeReached += _ => count++; f.Engine.Advance(); Equal(0, count);
        });
        Test("editing global threshold affects followers but does not rearm a played warning", () => {
            var f = new Fixture(); f.Engine.SetLowTimeDefault(2); f.Engine.Start(30, false, 0, lowTime: new() { Enabled = true });
            f.Move(10); f.Engine.Advance(); Is(!f.Engine.Snapshot.Timer.LowTimePlayed);
            f.Engine.SetLowTimeDefault(25); f.Engine.Advance(); Is(f.Engine.Snapshot.Timer.LowTimePlayed);
            var writes = f.Store.Writes; f.Engine.Advance(); Equal(writes, f.Store.Writes);
        });
        Test("low-time completion marker survives pause and restart and reset rearms it", () => {
            var f = new Fixture(); f.Engine.Start(20, false, 0, lowTime: new() { Enabled = true, ThresholdSeconds = 10 });
            f.Move(10); f.Engine.Advance(); f.Engine.Pause();
            var restored = f.Restart(); var count = 0; restored.LowTimeReached += _ => count++;
            restored.Resume(); restored.Advance(); Equal(0, count);
            restored.Reset(); restored.Start(20, false, 0); f.Move(10); restored.Advance(); Equal(1, count);
        });
        Test("paused timers never warn and missed deadlines never emit stale low-time audio", () => {
            var f = new Fixture(); var count = 0; f.Engine.LowTimeReached += _ => count++;
            f.Engine.Start(10, false, 0, lowTime: new() { Enabled = true }); f.Engine.Pause(); f.Move(60); f.Engine.Advance(); Equal(0, count);
            f.Engine.Resume(); f.Move(86400); f.Engine.Advance(); Equal(0, count); Equal(1, f.Engine.Snapshot.Prompts.Count);
        });
        Test("short sessions and sleep within threshold warn only on a live tick", () => {
            var f = new Fixture(); var count = 0; f.Engine.LowTimeReached += _ => count++;
            f.Engine.Start(15, false, 0, lowTime: new() { Enabled = true }); Equal(0, count);
            f.Engine.Advance(); f.Engine.Advance(); Equal(1, count);
            f.Engine.Start(120, false, 0); f.Move(104); f.Engine.Advance(); Equal(1, count);
            f.Move(1); f.Engine.Advance(); Equal(2, count);
        });
        Test("failed low-time commit emits nothing and retries on the next tick", () => {
            var f = new Fixture(); var count = 0; f.Engine.LowTimeReached += _ => count++;
            f.Engine.Start(15, false, 0, lowTime: new() { Enabled = true }); f.Store.Fail = true;
            Throws<IOException>(() => f.Engine.Advance()); Equal(0, count); Is(!f.Engine.Snapshot.Timer.LowTimePlayed);
            f.Store.Fail = false; f.Engine.Advance(); Equal(1, count);
        });
        Test("each repeat and scheduled takeover retains its own low-time settings", () => {
            var f = new Fixture(); var counts = new List<TimerState>(); f.Engine.LowTimeReached += counts.Add;
            var options = new LowTimeOptions { Enabled = true, ThresholdSeconds = 2, Track = LibrarySound.ChampionBattle };
            var id = f.Engine.SaveSchedule(null, f.Time.AddSeconds(5), 10, true, 27, lowTime: options);
            Equal(options, f.Restart().Snapshot.Schedules.Single().LowTime);
            f.Move(5); f.Engine.Advance(); Equal(options, f.Engine.Snapshot.Timer.LowTime);
            f.Move(8); f.Engine.Advance(); Equal(1, counts.Count); Equal(27, counts[0].Volume);
            f.Move(2); f.Engine.Advance(); Is(!f.Engine.Snapshot.Timer.LowTimePlayed);
            f.Move(8); f.Engine.Advance(); Equal(2, counts.Count);
        });
        Test("low-time can coincide with auto-start cutoff without losing the warning", () => {
            var f = new Fixture(); var count = 0; f.Engine.LowTimeReached += _ => count++;
            f.Engine.Start(30, true, 0, f.Engine.Now + 20000, new() { Enabled = true, ThresholdSeconds = 10 });
            f.Move(20); f.Engine.Advance(); Equal(1, count); Is(!f.Engine.Snapshot.Timer.AutoRestart); Is(f.Engine.Snapshot.Timer.IsRunning);
        });
        Test("per-session source overrides only audio source, not global playback behavior", () => {
            var settings = new AudioSettings { LowTime = new() { Track = LibrarySound.TrainerBattle, Behavior = SoundBehavior.Assertive } };
            Equal(settings.LowTime, settings.ForLowTime(new()));
            var selected = settings.ForLowTime(new() { Track = LibrarySound.LevelUp });
            Equal(LibrarySound.LevelUp, selected.Track); Equal(SoundBehavior.Assertive, selected.Behavior);
        });
        Test("low-time UI binding is quiet and per-session drafts follow default changes", () => {
            using var control = new LowTimeControl(); var count = 0; control.UserChanged += () => count++;
            var options = new LowTimeOptions { Enabled = true, Track = LibrarySound.ChampionBattle };
            control.LoadOptions(options, 60); control.LoadOptions(options, 300); Equal(0, count); Equal(options, control.Selection);
            var inherited = Descendants(control).OfType<CheckBox>().Single(x => x.Text == "Use default threshold");
            inherited.Checked = false; Equal(300, control.Selection.ThresholdSeconds); Equal(1, count);
            control.LoadOptions(control.Selection, 100); Equal(300, control.Selection.ThresholdSeconds);
        });
        Test("all available library copies decode, and every event resolves its default", () => {
            foreach (var track in SoundLibrary.Tracks) {
                var path = Path.Combine(AppContext.BaseDirectory, SoundLibrary.FileName(track));
                if (File.Exists(path)) Equal(path, Mp3AudioBackend.ValidateCustomFile(path));
            }
            foreach (var kind in Enum.GetValues<SoundEvent>())
                Is(SoundLibrary.Resolve(kind, new())!.EndsWith(SoundLibrary.FileName(SoundLibrary.DefaultFor(kind))));
        });
    }

    private static async Task TestAudioPolicies()
    {
        await TestVolumePlayback();
        await TestFadeOutPlayback();
        await TestPreviewAudio();
        await TestAsync("Polite voices overlap without changing each other's volumes", async () => {
            var backend = new ControlledAudio(); using var player = new AlertSoundPlayer(backend);
            var first = player.PlayAsync("a", 80, SoundBehavior.Polite, SoundEvent.LowTime); var a = await backend.Wait("a");
            var second = player.PlayAsync("b", 40, SoundBehavior.Polite, SoundEvent.Success); var b = await backend.Wait("b");
            Equal(.8f, a.Level.Gain); Equal(.4f, b.Level.Gain); Is(!first.IsCompleted);
            b.Finish.TrySetResult(); Equal(AlertSoundResult.Played, await second); Is(!first.IsCompleted);
            a.Finish.TrySetResult(); Equal(AlertSoundResult.Played, await first);
        });
        await TestAsync("Assertive ducks existing and new voices and restores exact levels", async () => {
            var backend = new ControlledAudio(); using var player = new AlertSoundPlayer(backend);
            var t1 = player.PlayAsync("base", 80, SoundBehavior.Polite, SoundEvent.LowTime); var original = await backend.Wait("base");
            var t2 = player.PlayAsync("assert", 60, SoundBehavior.Assertive, SoundEvent.Success); var assert = await backend.Wait("assert");
            Equal(.2f, original.Level.Gain); Equal(.6f, assert.Level.Gain);
            var t3 = player.PlayAsync("new", 40, SoundBehavior.Polite, SoundEvent.SessionEnd); var newer = await backend.Wait("new"); Equal(.1f, newer.Level.Gain);
            assert.Finish.TrySetResult(); await t2; Equal(.8f, original.Level.Gain); Equal(.4f, newer.Level.Gain);
            player.Stop(); await Task.WhenAll(t1, t3);
        });
        await TestAsync("newest Assertive has priority and earlier Assertive resumes after it", async () => {
            var backend = new ControlledAudio(); using var player = new AlertSoundPlayer(backend);
            var ta = player.PlayAsync("older", 80, SoundBehavior.Assertive, SoundEvent.LowTime); var a = await backend.Wait("older");
            var tb = player.PlayAsync("newer", 40, SoundBehavior.Assertive, SoundEvent.Success); var b = await backend.Wait("newer");
            Equal(.2f, a.Level.Gain); Equal(.4f, b.Level.Gain);
            b.Finish.TrySetResult(); await tb; Equal(.8f, a.Level.Gain);
            player.Stop(); Equal(AlertSoundResult.Cancelled, await ta);
        });
        await TestAsync("Disruptive ends all prior voices before starting its own playback", async () => {
            var backend = new ControlledAudio(); using var player = new AlertSoundPlayer(backend);
            var ta = player.PlayAsync("a", 80, SoundBehavior.Polite, SoundEvent.LowTime); await backend.Wait("a");
            var tb = player.PlayAsync("b", 50, SoundBehavior.Assertive, SoundEvent.Success); await backend.Wait("b");
            var tc = player.PlayAsync("c", 60, SoundBehavior.Disruptive, SoundEvent.Failure); var c = await backend.Wait("c");
            Equal(AlertSoundResult.Cancelled, await ta); Equal(AlertSoundResult.Cancelled, await tb); Equal(1, backend.Active);
            c.Finish.TrySetResult(); Equal(AlertSoundResult.Played, await tc);
        });
        await TestAsync("muted Disruptive does not silence existing music", async () => {
            var backend = new ControlledAudio(); using var player = new AlertSoundPlayer(backend);
            var task = player.PlayAsync("music", 40, SoundBehavior.Polite, SoundEvent.LowTime); var music = await backend.Wait("music");
            Equal(AlertSoundResult.Muted, await player.PlayAsync("silent", 0, SoundBehavior.Disruptive, SoundEvent.Success));
            Is(!task.IsCompleted); Equal(.4f, music.Level.Gain); player.Stop(); await task;
        });
        await TestAsync("failed Assertive and fallback restore other sounds without recursive playback", async () => {
            var backend = new ControlledAudio(); using var player = new AlertSoundPlayer(backend);
            var musicTask = player.PlayAsync("music", 80, SoundBehavior.Polite, SoundEvent.LowTime); var music = await backend.Wait("music");
            var failed = player.PlayAsync("broken", 50, SoundBehavior.Assertive, SoundEvent.Failure, "fallback"); var broken = await backend.Wait("broken");
            broken.Finish.TrySetException(new IOException()); var fallback = await backend.Wait("fallback"); Equal(.2f, music.Level.Gain);
            fallback.Finish.TrySetException(new IOException()); Equal(AlertSoundResult.Failed, await failed); Equal(.8f, music.Level.Gain);
            player.Stop(); await musicTask;
        });
        await TestAsync("stopping low-time alone leaves unrelated Polite sounds playing", async () => {
            var backend = new ControlledAudio(); using var player = new AlertSoundPlayer(backend);
            var low = player.PlayAsync("low", 50, SoundBehavior.Polite, SoundEvent.LowTime); await backend.Wait("low");
            var success = player.PlayAsync("success", 50, SoundBehavior.Polite, SoundEvent.Success); await backend.Wait("success");
            player.Stop(SoundEvent.LowTime); Equal(AlertSoundResult.Cancelled, await low); Is(!success.IsCompleted);
            player.Dispose(); Equal(AlertSoundResult.Cancelled, await success);
        });
        Test("live gain provider attenuates actual PCM samples and restores without altering source", () => {
            var level = new AudioLevel(80); var provider = new LiveGainProvider(new ConstantSamples(), level); var samples = new float[8];
            Equal(8, provider.Read(samples)); Is(samples.All(x => x == .8f));
            level.Duck(true); provider.Read(samples); Is(samples.All(x => x == .2f));
            level.Duck(false); provider.Read(samples); Is(samples.All(x => x == .8f));
        });
    }

    private sealed class ControlledAudio : IAlertAudioBackend
    {
        public sealed record Playing(AudioLevel Level)
        {
            public readonly TaskCompletionSource Finish = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        private readonly ConcurrentDictionary<string, Playing> playing = new();
        private int active;
        public int Active => Volatile.Read(ref active);
        public async Task PlayAsync(string path, AudioLevel level, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); var voice = new Playing(level); Interlocked.Increment(ref active);
            try { Is(playing.TryAdd(path, voice)); await voice.Finish.Task.WaitAsync(token); }
            finally { Interlocked.Decrement(ref active); }
        }
        public async Task<Playing> Wait(string path)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!playing.TryGetValue(path, out _)) await Task.Delay(10, timeout.Token);
            return playing[path];
        }
    }
    private sealed class ConstantSamples : ISampleProvider
    {
        public WaveFormat WaveFormat => WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        public int Read(Span<float> buffer) { buffer.Fill(1); return buffer.Length; }
        public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    }
}
