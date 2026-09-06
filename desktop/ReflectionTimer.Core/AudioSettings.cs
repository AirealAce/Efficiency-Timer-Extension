namespace ReflectionTimer.Core;

public enum SoundEvent { SessionEnd, Success, Failure, LowTime }
public enum SoundBehavior { Disruptive = 0, Assertive = 1, Polite = 2 }
public enum LibrarySound { Default, SessionEnd, ObtainedItem, LevelUp, PokemonHealed, KeyItem, TrainerBattle, ChampionBattle, OutOfHealth }

public record SoundSetting
{
    public string Mp3Path { get; init; } = "";
    public LibrarySound Track { get; init; }
    public SoundBehavior Behavior { get; init; } = SoundBehavior.Disruptive;
}

public record LowTimeOptions
{
    public bool Enabled { get; init; }
    public int? ThresholdSeconds { get; init; } // Null follows Settings, including future changes.
    public string Mp3Path { get; init; } = "";
    public LibrarySound Track { get; init; } // Default follows the global low-time sound.
}

public record AudioSettings
{
    public SoundSetting SessionEnd { get; init; } = new();
    public SoundSetting Success { get; init; } = new();
    public SoundSetting Failure { get; init; } = new();
    public SoundSetting LowTime { get; init; } = new();
    public int LowTimeThresholdSeconds { get; init; } = 60;

    public static AudioSettings From(AppState state) => state.Audio ?? new() { SessionEnd = new() { Mp3Path = state.AlertSoundPath } };
    public SoundSetting For(SoundEvent kind) => kind switch {
        SoundEvent.SessionEnd => SessionEnd, SoundEvent.Success => Success, SoundEvent.Failure => Failure, SoundEvent.LowTime => LowTime,
        _ => throw new ArgumentException("Choose a sound event.")
    };
    public AudioSettings With(SoundEvent kind, SoundSetting setting) => kind switch {
        SoundEvent.SessionEnd => this with { SessionEnd = setting }, SoundEvent.Success => this with { Success = setting },
        SoundEvent.Failure => this with { Failure = setting }, SoundEvent.LowTime => this with { LowTime = setting },
        _ => throw new ArgumentException("Choose a sound event.")
    };
    public SoundSetting ForLowTime(LowTimeOptions options) => options.Mp3Path.Length > 0 || options.Track != LibrarySound.Default
        ? LowTime with { Mp3Path = options.Mp3Path, Track = options.Track } : LowTime;

    public static void ValidateSource(string path, LibrarySound track)
    {
        if (!Enum.IsDefined(track)) throw new ArgumentException("Choose a sound from the library.");
        if (path.Length > 0 && (!Path.IsPathFullyQualified(path) || !Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Choose a local MP3 file.");
        if (path.Length > 0 && track != LibrarySound.Default) throw new ArgumentException("Choose either a library sound or a custom MP3.");
    }
    public static void Validate(SoundSetting setting)
    {
        ValidateSource(setting.Mp3Path, setting.Track);
        if (!Enum.IsDefined(setting.Behavior)) throw new ArgumentException("Choose Disruptive, Assertive, or Polite.");
    }
    public static void Validate(LowTimeOptions options)
    {
        ValidateSource(options.Mp3Path, options.Track);
        if (options.ThresholdSeconds is { } seconds) TimerEngine.ValidateDuration(seconds);
    }
}
