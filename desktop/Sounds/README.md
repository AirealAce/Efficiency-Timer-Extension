# Personal soundtrack support

Public desktop downloads use original synthesized notification tones. Game soundtrack recordings are not included. Downloading a recording or paying for download access does not establish permission to redistribute it in this repository or a release.

Your existing local recordings and sound selections remain supported. Local developer builds copy MP3 files already present in this folder; public packaging excludes them. The installer preserves MP3s already present in an existing installation. You can also use **Choose MP3** in Audio settings to select your own local file.

The app recognizes these optional local filenames:

| Filename | Optional local track |
| --- | --- |
| pokemon-obtained-item.mp3 | Obtained an Item |
| pokemon-level-up.mp3 | Level Up |
| pokemon-healed.mp3 | Pokemon Healed |
| pokemon-key-item.mp3 | Obtained a Key Item |
| pokemon-battle-trainer.mp3 | Battle (Trainer) |
| pokemon-battle-champion.mp3 | Battle (Champion) |
| kirby-out-of-health.mp3 | Out of Health |

When these files exist locally, the established defaults remain Level Up for Success, Out of Health for Failure, Battle (Trainer) for Low on time, and `popup.mp3` for Session end. Otherwise Default uses a synthesized tone. The app never downloads recordings at runtime.

The repository's `notification.wav` is an original synthesized chime for the Chrome extension, reproducible with `node scripts/generate-notification.cjs`. The desktop generates its tones directly in `BuiltInTone.cs`.
