# Bundled soundtrack library

These seven MP3s are committed to the repository and copied next to ReflectionTimer.exe in both local builds and public release packages. They were downloaded from KHInsider on September 9, 2026 using the album and track identities from the original audio configuration. The downloaded audio files are unmodified.

| Bundled filename | Source track |
| --- | --- |
| pokemon-obtained-item.mp3 | [1-12. Obtained an Item!](https://downloads.khinsider.com/game-soundtracks/album/pok%C3%A9mon-diamond-pearl-platinum-restored-soundtrack-2006/1-12.%2520Obtained%2520an%2520Item%2521%25E2%2580%258E.mp3) |
| pokemon-level-up.mp3 | [1-36. Level Up!](https://downloads.khinsider.com/game-soundtracks/album/pok%C3%A9mon-diamond-pearl-platinum-restored-soundtrack-2006/1-36.%2520Level%2520Up%2521%25E2%2580%258E.mp3) |
| pokemon-healed.mp3 | [1-17. Pokémon Healed](https://downloads.khinsider.com/game-soundtracks/album/pok%C3%A9mon-diamond-pearl-platinum-restored-soundtrack-2006/1-17.%2520Pok%25C3%25A9mon%2520Healed%25E2%2580%258E.mp3) |
| pokemon-key-item.mp3 | [1-23. Obtained a Key Item!](https://downloads.khinsider.com/game-soundtracks/album/pok%C3%A9mon-diamond-pearl-platinum-restored-soundtrack-2006/1-23.%2520Obtained%2520a%2520Key%2520Item%2521%25E2%2580%258E.mp3) |
| pokemon-battle-trainer.mp3 | [1-20. Battle! (Trainer)](https://downloads.khinsider.com/game-soundtracks/album/pok%C3%A9mon-diamond-pearl-platinum-restored-soundtrack-2006/1-20.%2520Battle%2521%2520%2528Trainer%2529%25E2%2580%258E.mp3) |
| pokemon-battle-champion.mp3 | [2-67. Battle! (Champion)](https://downloads.khinsider.com/game-soundtracks/album/pok%C3%A9mon-diamond-pearl-platinum-restored-soundtrack-2006/2-67.%2520Battle%2521%2520%2528Champion%2529%25E2%2580%258E.mp3) |
| kirby-out-of-health.mp3 | [28 Out of Heath (source spelling)](https://downloads.khinsider.com/game-soundtracks/album/kirby-the-amazing-mirror/28%2520Out%2520of%2520Heath.mp3) |

The Pokémon tracks are from Pokémon Diamond, Pearl, & Platinum Restored Soundtrack (2006). The Kirby track is from Kirby & The Amazing Mirror (GBA gamerip, 2004). See [sources.json](sources.json) for album links, download URLs, byte sizes, and SHA-256 checksums.

Defaults are Level Up for Success, Out of Health for Failure, Battle (Trainer) for Low on time, and the existing root-level popup.mp3 for Session end. The other tracks remain selectable in Audio settings.

Full track lengths are retained, including both battle tracks. Playback is once through; the existing preview limit and optional fade settings still apply. Built-in tones remain a fallback if a bundled file is missing. The app does not download audio at runtime.
