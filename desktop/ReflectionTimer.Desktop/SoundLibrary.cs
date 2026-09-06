using ReflectionTimer.Core;

namespace ReflectionTimer.Desktop;

public static class SoundLibrary
{
    public static LibrarySound DefaultFor(SoundEvent kind) => kind switch {
        SoundEvent.Success => LibrarySound.LevelUp, SoundEvent.Failure => LibrarySound.OutOfHealth,
        SoundEvent.LowTime => LibrarySound.TrainerBattle, _ => LibrarySound.SessionEnd
    };
    public static string Name(LibrarySound track) => track switch {
        LibrarySound.SessionEnd => "Original extension sound", LibrarySound.ObtainedItem => "Obtained an Item — Pokémon",
        LibrarySound.LevelUp => "Level Up — Pokémon", LibrarySound.PokemonHealed => "Pokémon Healed",
        LibrarySound.KeyItem => "Obtained a Key Item — Pokémon", LibrarySound.TrainerBattle => "Battle (Trainer) — Pokémon",
        LibrarySound.ChampionBattle => "Battle (Champion) — Pokémon", LibrarySound.OutOfHealth => "Out of Health — Kirby",
        _ => "Default"
    };
    public static string FileName(LibrarySound track) => track switch {
        LibrarySound.ObtainedItem => "pokemon-obtained-item.mp3", LibrarySound.LevelUp => "pokemon-level-up.mp3",
        LibrarySound.PokemonHealed => "pokemon-healed.mp3", LibrarySound.KeyItem => "pokemon-key-item.mp3",
        LibrarySound.TrainerBattle => "pokemon-battle-trainer.mp3", LibrarySound.ChampionBattle => "pokemon-battle-champion.mp3",
        LibrarySound.OutOfHealth => "kirby-out-of-health.mp3", _ => "popup.mp3"
    };
    public static string Resolve(SoundEvent kind, SoundSetting setting) => setting.Mp3Path.Length > 0 ? setting.Mp3Path
        : Path.Combine(AppContext.BaseDirectory, FileName(setting.Track == LibrarySound.Default ? DefaultFor(kind) : setting.Track));
    public static string Fallback(SoundEvent kind)
    {
        var path = Path.Combine(AppContext.BaseDirectory, FileName(DefaultFor(kind)));
        return File.Exists(path) ? path : AlertSoundPlayer.BundledPath;
    }
    public static string Describe(SoundEvent kind, SoundSetting setting) => setting.Mp3Path.Length > 0 ? "Custom MP3 · " + Path.GetFileName(setting.Mp3Path)
        : (setting.Track == LibrarySound.Default ? "Default · " : "") + Name(setting.Track == LibrarySound.Default ? DefaultFor(kind) : setting.Track);
}
