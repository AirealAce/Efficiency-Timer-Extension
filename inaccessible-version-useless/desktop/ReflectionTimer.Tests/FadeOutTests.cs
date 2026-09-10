using System.Collections.Concurrent;
using System.Text.Json;
using NAudio.Wave;
using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private static void TestFadeOutSettings()
    {
        Test("fade settings migrate off, persist independently and validate atomically", () => {
            var legacy = JsonSerializer.Deserialize<SoundSetting>("{}", DataJson.Options)!;
            Is(!legacy.FadeOutEnabled); Equal(10, legacy.FadeOutAfterSeconds);
            var f = new Fixture(); var timer = f.Engine.Snapshot.Timer;
            foreach (var kind in Enum.GetValues<SoundEvent>())
                f.Engine.SetSound(kind, new() { FadeOutEnabled = true, FadeOutAfterSeconds = 7 + (int)kind });
            foreach (var kind in Enum.GetValues<SoundEvent>()) {
                var saved = AudioSettings.From(f.Restart().Snapshot).For(kind);
                Is(saved.FadeOutEnabled); Equal(7 + (int)kind, saved.FadeOutAfterSeconds);
            }
            Equal(timer, f.Engine.Snapshot.Timer);
            var before = AudioSettings.From(f.Engine.Snapshot); var writes = f.Store.Writes;
            foreach (var invalid in new[] { 0, -1, TimerEngine.MaxDuration + 1 })
                Throws<ArgumentException>(() => f.Engine.SetSound(SoundEvent.Success, new() { FadeOutEnabled = true, FadeOutAfterSeconds = invalid }));
            Equal(before, AudioSettings.From(f.Engine.Snapshot)); Equal(writes, f.Store.Writes);
            var overrideSound = before.ForLowTime(new() { Track = LibrarySound.ChampionBattle });
            Is(overrideSound.FadeOutEnabled); Equal(before.LowTime.FadeOutAfterSeconds, overrideSound.FadeOutAfterSeconds);
            f.Store.Fail = true;
            Throws<IOException>(() => f.Engine.SetSound(SoundEvent.Success, before.Success with { FadeOutEnabled = false }));
            Equal(before, AudioSettings.From(f.Engine.Snapshot));
        });
        Test("each audio row has an independent silent fade editor and preview uses its setting", () => {
            var directory = Path.Combine(Path.GetTempPath(), "ReflectionTimer-QA-fade-" + Guid.NewGuid().ToString("N"));
            var store = new EncryptedStore(directory); store.Save(new AppState { ExtensionDisabledConfirmed = true });
            var backend = new FadeRecordingAudio();
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var app = new TimerApplication(store, directory, show, enableAudio: true, audioBackend: backend, updateStartup: _ => { });
            using var main = new MainWindow(app, _ => { }); main.Render(app.Engine.Snapshot); main.Show();
            app.Engine.Changed += () => main.Render(app.Engine.Snapshot);
            Descendants(main).OfType<TabControl>().Single().SelectedIndex = 3;
            var audio = Descendants(main).OfType<AudioSettingsControl>().Single();
            var threshold = Descendants(audio).OfType<NumericUpDown>().Single(x => x.AccessibleName == "Default low-time threshold in seconds");
            threshold.Value = 123;
            Equal(4, Descendants(audio).OfType<FadeOutControl>().Count());
            foreach (var kind in Enum.GetValues<SoundEvent>()) {
                var fade = Descendants(audio).OfType<FadeOutControl>().Single(x => Descendants(x).OfType<CheckBox>().Single().AccessibleName == kind + " fade out after");
                var check = Descendants(fade).OfType<CheckBox>().Single();
                var number = Descendants(fade).OfType<NumericUpDown>().Single();
                Is(!check.Checked); Is(!number.Enabled); Equal("Fade out after", check.Text);
                var source = Descendants(audio).OfType<SoundSourceControl>().Single(x => Descendants(x).OfType<ComboBox>().Any(c => c.AccessibleName == kind + " sound"));
                Equal(source.Parent, fade.Parent);
                Equal(source.Parent!.Controls.GetChildIndex(source) + 2, fade.Parent!.Controls.GetChildIndex(fade));
                Is(fade.Top >= source.Bottom);
                var before = AudioSettings.From(app.Engine.Snapshot);
                var requests = app.Log.Recent().Count(x => x.Event == "sound.requested");
                check.Checked = true; Is(number.Enabled); number.Value = 7 + (int)kind;
                check.Checked = false; Is(!number.Enabled); Equal(7 + (int)kind, fade.Seconds);
                check.Checked = true;
                var behavior = Descendants(audio).OfType<ComboBox>().Single(x => x.AccessibleName == kind + " playback behavior");
                behavior.SelectedIndex = (int)SoundBehavior.Polite;
                Equal(requests, app.Log.Recent().Count(x => x.Event == "sound.requested"));
                var saved = AudioSettings.From(store.Load());
                foreach (var other in Enum.GetValues<SoundEvent>().Where(x => x != kind)) Equal(before.For(other), saved.For(other));
                Is(saved.For(kind).FadeOutEnabled); Equal(7 + (int)kind, saved.For(kind).FadeOutAfterSeconds);
                Equal(SoundBehavior.Polite, saved.For(kind).Behavior); Equal(123, audio.DefaultThresholdSeconds);
                var count = backend.Levels.Count;
                Descendants(source).OfType<Button>().Single(x => x.Text == "Preview audio").PerformClick();
                PumpUntil(() => backend.Levels.Count > count);
                Equal(7 + (int)kind, backend.Levels.Last().FadeOutAfterSeconds);
                check.Checked = false;
                var task = app.PlaySound(kind, 50);
                PumpUntil(() => task.IsCompleted);
                Is(backend.Levels.Last().FadeOutAfterSeconds is null);
                var restored = AudioSettings.From(store.Load()).For(kind);
                Is(!restored.FadeOutEnabled); Equal(7 + (int)kind, restored.FadeOutAfterSeconds);
            }
        });
    }

    private static async Task TestFadeOutPlayback()
    {
        Test("PCM fade waits for the chosen audio time, fades stereo together and ends playback", () => {
            var level = new AudioLevel(80, 2); var provider = new LiveGainProvider(new ConstantSamples(), level);
            var delay = new float[44100 * 2 * 2]; Equal(delay.Length, provider.Read(delay)); Is(delay.All(x => x == .8f));
            var fade = new float[44100 * 2 + 16]; Array.Fill(fade, -1f);
            Equal(44100 * 2, provider.Read(fade));
            Equal(.8f, fade[0]); Equal(.4f, fade[44100]); Is(fade[44100 * 2 - 1] < .0001f);
            for (var i = 0; i < 44100 * 2; i += 2) Equal(fade[i], fade[i + 1]);
            Equal(-1f, fade[^1]); Equal(0, provider.Read(fade)); Equal(0, provider.Read(fade));
        });
        Test("fade works across uneven reads, respects ducking and leaves unchecked audio full length", () => {
            var level = new AudioLevel(80, 1); var provider = new LiveGainProvider(new FadeSamples(10, 2), level);
            var buffer = new float[30]; Array.Fill(buffer, -1f);
            Equal(19, provider.Read(buffer, 3, 19)); Is(buffer.Take(3).All(x => x == -1));
            Equal(5, provider.Read(buffer, 2, 5)); Equal(.8f, buffer[2]); Equal(.8f, buffer[3]); Equal(.8f, buffer[4]);
            level.Duck(true); Equal(8, provider.Read(buffer, 0, 8)); Is(Math.Abs(buffer[0] - .16f) < .00001f);
            level.Duck(false); Equal(8, provider.Read(buffer, 0, buffer.Length)); Is(Math.Abs(buffer[0] - .32f) < .00001f);
            Equal(0, provider.Read(buffer));
            var full = new LiveGainProvider(new FadeSamples(10, 1, 50), new AudioLevel(50));
            Equal(30, full.Read(buffer)); Equal(20, full.Read(buffer)); Equal(0, full.Read(buffer));
            var shortTrack = new LiveGainProvider(new FadeSamples(10, 1, 5), new AudioLevel(50, 2));
            Equal(5, shortTrack.Read(buffer)); Is(buffer.Take(5).All(x => x == .5f)); Equal(0, shortTrack.Read(buffer));
        });
        await TestAsync("fade reaches fallback audio while preview limits and stop remain effective", async () => {
            var backend = new ControlledAudio(); var clock = new PreviewClock();
            using var player = new AlertSoundPlayer(backend, timeProvider: clock);
            var task = player.PlayAsync("broken-fade", 80, SoundBehavior.Assertive, SoundEvent.LowTime, "fade-fallback", preview: true, fadeOutAfterSeconds: 20);
            var broken = await backend.Wait("broken-fade"); Equal(20, broken.Level.FadeOutAfterSeconds);
            broken.Finish.TrySetException(new IOException()); var fallback = await backend.Wait("fade-fallback");
            Equal(20, fallback.Level.FadeOutAfterSeconds); clock.Expire(); Equal(AlertSoundResult.PreviewFinished, await task);
            var regular = player.PlayAsync("regular-fade", 80, SoundBehavior.Polite, SoundEvent.Success, fadeOutAfterSeconds: 1);
            await backend.Wait("regular-fade"); player.Stop(); Equal(AlertSoundResult.Cancelled, await regular);
        });
    }

    private static void PumpUntil(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!done() && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(1); }
        Is(done()); Application.DoEvents();
    }
    private sealed class FadeRecordingAudio : IAlertAudioBackend
    {
        public readonly ConcurrentQueue<AudioLevel> Levels = new();
        public Task PlayAsync(string path, AudioLevel level, CancellationToken token) { token.ThrowIfCancellationRequested(); Levels.Enqueue(level); return Task.CompletedTask; }
    }
    private sealed class FadeSamples(int sampleRate, int channels, int remaining = int.MaxValue) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        public int Read(Span<float> buffer) { var read = Math.Min(buffer.Length, remaining); buffer[..read].Fill(1); remaining -= read; return read; }
        public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    }
}
