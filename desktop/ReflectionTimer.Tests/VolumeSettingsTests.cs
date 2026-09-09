using System.Text.Json;
using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private static void TestVolumeSettings()
    {
        Test("per-event volumes default to full level, persist independently and validate atomically", () => {
            Equal(100, JsonSerializer.Deserialize<SoundSetting>("{}", DataJson.Options)!.Volume);
            var f = new Fixture(); var before = f.Engine.Snapshot.Timer;
            foreach (var kind in Enum.GetValues<SoundEvent>()) f.Engine.SetSound(kind, new() { Volume = 10 + (int)kind });
            foreach (var kind in Enum.GetValues<SoundEvent>()) Equal(10 + (int)kind, AudioSettings.From(f.Restart().Snapshot).For(kind).Volume);
            Equal(before, f.Engine.Snapshot.Timer);
            var sounds = AudioSettings.From(f.Engine.Snapshot); var writes = f.Store.Writes;
            foreach (var invalid in new[] { -1, 101 }) Throws<ArgumentException>(() => f.Engine.SetSound(SoundEvent.Success, new() { Volume = invalid }));
            Equal(writes, f.Store.Writes); Equal(sounds, AudioSettings.From(f.Engine.Snapshot));
            Equal(sounds.LowTime.Volume, sounds.ForLowTime(new() { Track = LibrarySound.ChampionBattle }).Volume);
            f.Store.Fail = true; Throws<IOException>(() => f.Engine.SetSound(SoundEvent.Success, sounds.Success with { Volume = 5 }));
            Equal(sounds, AudioSettings.From(f.Engine.Snapshot));
        });
        Test("App sound only changes volume and survives restart and failed saves", () => {
            var f = new Fixture(); f.Engine.Start(90, true, 50, f.Time.AddMinutes(1).ToUnixTimeMilliseconds());
            f.Move(61); // An expired cutoff must not prevent changing the master volume.
            var before = f.Engine.Snapshot.Timer; f.Engine.SetAppVolume(35);
            Equal(before with { Volume = 35 }, f.Engine.Snapshot.Timer); Equal(35, f.Restart().Snapshot.Timer.Volume);
            var writes = f.Store.Writes;
            foreach (var invalid in new[] { -1, 101 }) Throws<ArgumentException>(() => f.Engine.SetAppVolume(invalid));
            Equal(writes, f.Store.Writes); f.Store.Fail = true;
            Throws<IOException>(() => f.Engine.SetAppVolume(70)); Equal(35, f.Engine.Snapshot.Timer.Volume);
        });
        Test("settings volumes are placed above aligned fade controls and master sliders synchronize quietly", () => {
            var directory = Path.Combine(Path.GetTempPath(), "ReflectionTimer-QA-volumes-" + Guid.NewGuid().ToString("N"));
            var store = new EncryptedStore(directory); store.Save(new AppState { ExtensionDisabledConfirmed = true });
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
            var backend = new FadeRecordingAudio();
            using var app = new TimerApplication(store, directory, show, enableAudio: true, audioBackend: backend, updateStartup: _ => { });
            using var main = new MainWindow(app, _ => { }); main.Render(app.Engine.Snapshot); main.Show();
            app.Engine.Changed += () => main.Render(app.Engine.Snapshot);
            var tabs = Descendants(main).OfType<TabControl>().Single(); tabs.SelectedIndex = 3;
            var audio = Descendants(main).OfType<AudioSettingsControl>().Single();
            var master = Descendants(audio).OfType<VolumeControl>().Single(x => x.AccessibleName == "Settings app sound volume");
            var timerMaster = Descendants(tabs.TabPages[0]).OfType<VolumeControl>().Single();
            var threshold = Descendants(audio).OfType<NumericUpDown>().Single(x => x.AccessibleName == "Default low-time threshold in seconds");
            threshold.Value = 123;
            var successHeading = Descendants(audio).OfType<Label>().Single(x => x.Text == "Success messages");
            Is(master.Bottom <= successHeading.Top); Equal(5, Descendants(audio).OfType<VolumeControl>().Count());
            foreach (var kind in Enum.GetValues<SoundEvent>()) {
                var volume = Descendants(audio).OfType<VolumeControl>().Single(x => x.AccessibleName == kind + " audio volume");
                var fade = Descendants(audio).OfType<FadeOutControl>().Single(x => Descendants(x).OfType<CheckBox>().Single().AccessibleName == kind + " fade out after");
                Equal(volume.Parent, fade.Parent); Equal(volume.Parent!.Controls.GetChildIndex(volume) + 1, fade.Parent!.Controls.GetChildIndex(fade));
                Is(volume.Bottom <= fade.Top); Equal(100, volume.Value);
                var slider = Descendants(volume).OfType<TrackBar>().Single();
                var sliderLabel = Descendants(volume).OfType<RowLabel>().Single();
                Is(slider.Top >= sliderLabel.Bottom); Equal(sliderLabel.Left, slider.Left);
                Is(volume.ClientRectangle.Contains(volume.RectangleToClient(slider.RectangleToScreen(slider.ClientRectangle))));
                var check = Descendants(fade).OfType<CheckBox>().Single();
                var text = Descendants(fade).OfType<RowLabel>().Single();
                var number = Descendants(fade).OfType<NumericUpDown>().Single();
                Equal(ContentAlignment.MiddleLeft, check.TextAlign); Equal(ContentAlignment.MiddleLeft, check.CheckAlign);
                Equal(text.Height, check.Height); Equal(text.Top, check.Top); Equal(number.Parent!.Height, check.Height);
                Descendants(volume).OfType<TrackBar>().Single().Value = 30 + (int)kind;
                Equal(30 + (int)kind, AudioSettings.From(store.Load()).For(kind).Volume);
            }
            Equal(0, backend.Levels.Count); // Volume edits do not preview or play success feedback.
            Descendants(master).OfType<TrackBar>().Single().Value = 25;
            Equal(25, timerMaster.Value); Equal(25, store.Load().Timer.Volume);
            Is(Descendants(master).OfType<Label>().Any(x => x.Text == "App sound (25%)"));
            tabs.SelectedIndex = 0;
            var duration = Descendants(tabs.TabPages[0]).OfType<DurationControl>().Single();
            var parts = Descendants(duration).OfType<DurationPartInput>().ToArray(); parts[2].Text = "120";
            Descendants(timerMaster).OfType<TrackBar>().Single().Value = 65;
            Equal(65, master.Value); Equal("120", parts[2].Text); Is(duration.Dirty); Equal(123, audio.DefaultThresholdSeconds);
            Is(!app.Engine.Snapshot.Timer.IsRunning); Equal(0, backend.Levels.Count);
            tabs.SelectedIndex = 3;
            foreach (var kind in Enum.GetValues<SoundEvent>()) {
                var playback = app.PlaySound(kind, app.Engine.Snapshot.Timer.Volume, preview: true, announcePreview: false);
                PumpUntil(() => playback.IsCompleted);
                var level = backend.Levels.Last(); Equal(30 + (int)kind, level.SoundVolume);
                Is(Math.Abs(level.Gain - .65f * ((30 + (int)kind) / 100f)) < .000001f);
            }
            using (var blockedSave = new FileStream(Path.Combine(directory, "state.dat.tmp"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) {
                Descendants(master).OfType<TrackBar>().Single().Value = 80;
                Equal(65, master.Value); Equal(65, timerMaster.Value); Equal(65, app.Engine.Snapshot.Timer.Volume);
                var volume = Descendants(audio).OfType<VolumeControl>().Single(x => x.AccessibleName == "Success audio volume");
                var previous = volume.Value; Descendants(volume).OfType<TrackBar>().Single().Value = 80;
                Equal(previous, volume.Value); Equal(previous, AudioSettings.From(app.Engine.Snapshot).Success.Volume);
            }
            Equal(123, audio.DefaultThresholdSeconds);
        });
    }

    private static async Task TestVolumePlayback()
    {
        await TestAsync("master and event levels multiply, update playing voices and combine with ducking", async () => {
            var backend = new ControlledAudio(); using var player = new AlertSoundPlayer(backend);
            var settings = new AudioSettings { SessionEnd = new() { Volume = 50 }, Success = new() { Volume = 80 } };
            var music = player.PlayAsync("volume-music", 50, SoundBehavior.Polite, SoundEvent.SessionEnd, soundVolume: 50);
            var musicVoice = await backend.Wait("volume-music"); Equal(.25f, musicVoice.Level.Gain);
            var alert = player.PlayAsync("volume-alert", 50, SoundBehavior.Assertive, SoundEvent.Success, soundVolume: 80);
            var alertVoice = await backend.Wait("volume-alert"); Equal(.4f, alertVoice.Level.Gain); Equal(.0625f, musicVoice.Level.Gain);
            player.UpdateVolumes(100, settings); Equal(.8f, alertVoice.Level.Gain); Equal(.125f, musicVoice.Level.Gain);
            settings = settings with { Success = settings.Success with { Volume = 0 } };
            player.UpdateVolumes(100, settings); Equal(0f, alertVoice.Level.Gain); Equal(.5f, musicVoice.Level.Gain);
            player.UpdateVolumes(0, settings); Equal(0f, musicVoice.Level.Gain); Is(!music.IsCompleted);
            player.UpdateVolumes(25, settings); Equal(.125f, musicVoice.Level.Gain);
            alertVoice.Finish.TrySetResult(); await alert; Equal(.125f, musicVoice.Level.Gain);
            player.Stop(); Equal(AlertSoundResult.Cancelled, await music);
        });
        await TestAsync("zero event volume is silent and non-disruptive and fallback retains combined levels", async () => {
            var backend = new ControlledAudio(); using var player = new AlertSoundPlayer(backend);
            var music = player.PlayAsync("audible", 50, SoundBehavior.Polite, SoundEvent.LowTime); var voice = await backend.Wait("audible");
            Equal(AlertSoundResult.Muted, await player.PlayAsync("muted-event", 50, SoundBehavior.Disruptive, SoundEvent.Success, soundVolume: 0));
            Is(!music.IsCompleted); Equal(.5f, voice.Level.Gain);
            var failing = player.PlayAsync("broken-volume", 50, SoundBehavior.Polite, SoundEvent.Success, "volume-fallback", fadeOutAfterSeconds: 2, soundVolume: 50);
            var broken = await backend.Wait("broken-volume"); broken.Finish.TrySetException(new IOException());
            var fallback = await backend.Wait("volume-fallback"); Equal(.25f, fallback.Level.Gain); Equal(2, fallback.Level.FadeOutAfterSeconds);
            fallback.Finish.TrySetResult(); Equal(AlertSoundResult.DefaultFallback, await failing); player.Stop(); await music;
        });
        Test("per-event gain composes with PCM fade and preserves very quiet levels", () => {
            var level = new AudioLevel(50, 1, 50); var provider = new LiveGainProvider(new FadeSamples(10, 1), level);
            var samples = new float[20]; Equal(20, provider.Read(samples)); Equal(.25f, samples[0]); Equal(.125f, samples[15]);
            Is(Math.Abs(new AudioLevel(1, soundVolume: 1).Gain - .0001f) < .000001f);
        });
    }
}
