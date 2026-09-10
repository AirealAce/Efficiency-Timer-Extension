# Security and privacy

The accessible desktop app is the primary source in this branch. Its local builds are not automatically published to [GitHub Releases](https://github.com/AirealAce/Reflection-Timer/releases/latest); the latest published release may still be the earlier native version. Packages are unsigned. They include .NET, a SHA-256 checksum and a file manifest; checksums detect corruption and do not establish publisher identity or guarantee safety. No administrator access is required.

Each person supplies their own Google spreadsheet, Apps Script deployment and randomly generated token. Fresh installations have no connection credentials. The desktop app sends reflections only to the configured Google Apps Script service, with HTTPS and restricted redirects. It has no analytics or automatic diagnostic uploads. The developer is not added to the user's spreadsheet.

Desktop settings, credentials, drafts and delivery queue are encrypted using Windows DPAPI in `%LOCALAPPDATA%\ReflectionTimerDesktop`. This protects files at rest; software running as the same Windows user may still access them. The older Chrome extension stores settings in Chrome local storage and does not have this desktop encryption guarantee.

Private setup codes are encoded, **not encrypted**. Treat a setup code, personalized setup script and API token as passwords. Never post them, state files, private spreadsheet links or unreviewed diagnostics in a public issue. A token allows submissions to its configured receiver. Restrict access to the spreadsheet and Apps Script project to trusted people.

If a token is exposed, disable the deployment or replace `REFLECTION_API_TOKEN` in the receiver's Script Properties, then update trusted clients. Removing a file from Git does not remove earlier commits or copies already downloaded. Old version-1 service-account keys should be revoked if they are still active.

For security reports, use GitHub's private vulnerability reporting option on the repository Security page when available. If unavailable, open an issue asking for a private reporting channel without including exploit details, credentials or personal data. Ordinary issues can include reproduction steps, app version and a reviewed diagnostic export.

Public desktop packages include only the eight MP3s listed and hashed in `desktop/Sounds/sources.json`, with redistribution permission confirmed by the project maintainer. Packaging rejects additional or changed audio files, scans for private data, and includes `AUDIO-NOTICES.txt`. Custom audio selections remain local and are preserved on upgrade; the app never uploads audio paths or recordings. Built-in tones remain available as a fallback when a default file is missing.

The primary interface uses semantic HTML in WebView2 with labels, headings, tables, keyboard focus and restrained announcements. Automated checks and initial screen-reader feedback support the approach; complete compatibility with every screen reader has not been verified. The older native app and extension are archived in their named folders and are not part of the current app package.
