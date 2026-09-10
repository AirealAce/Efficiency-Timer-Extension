using NAudio.Wave;
using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private static void TestBundledAudio(string directory)
    {
        foreach (var track in Enum.GetValues<LibrarySound>().Where(x => x is not (LibrarySound.Default or LibrarySound.None)))
            Test("bundled MP3 decodes audible samples: " + track, () => {
                var path = Path.Combine(directory, SoundLibrary.FileName(track));
                Is(File.Exists(path)); Equal(path, Mp3AudioBackend.ValidateCustomFile(path));
                using var reader = new AudioFileReader(path);
                Is(reader.TotalTime > TimeSpan.Zero);
                var buffer = new byte[16384]; var audible = false; long decoded = 0;
                while (reader.Read(buffer, 0, buffer.Length) is var count && count > 0) {
                    decoded += count; audible |= buffer.Take(count).Any(value => value != 0);
                }
                Is(decoded > 0 && audible);
            });
        Test("fresh-user defaults resolve to the four bundled MP3s without changing settings", () => {
            var settings = AudioSettings.From(new AppState());
            var expected = new Dictionary<SoundEvent, string> {
                [SoundEvent.Success] = "pokemon-level-up.mp3", [SoundEvent.Failure] = "kirby-out-of-health.mp3",
                [SoundEvent.LowTime] = "pokemon-battle-trainer.mp3", [SoundEvent.SessionEnd] = "popup.mp3"
            };
            foreach (var (kind, file) in expected) {
                var selection = settings.For(kind);
                Equal(LibrarySound.Default, selection.Track); Equal("", selection.Mp3Path);
                Equal(SoundBehavior.Disruptive, selection.Behavior);
                Equal(Path.Combine(directory, file), SoundLibrary.Resolve(kind, selection, directory));
                Equal<string?>(null, SoundLibrary.Resolve(kind, selection with { Track = LibrarySound.None }, directory));
                var custom = Path.Combine(directory, "user-selected.mp3");
                Equal(custom, SoundLibrary.Resolve(kind, selection with { Mp3Path = custom }, directory));
            }
        });
    }
}
