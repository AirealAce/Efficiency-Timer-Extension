# Security and privacy

Use the latest Windows desktop package from [GitHub Releases](https://github.com/AirealAce/Reflection-Timer/releases/latest). Packages are unsigned. They include .NET, a SHA-256 checksum and a file manifest; checksums detect corruption and do not establish publisher identity or guarantee safety. No administrator access is required.

Each person supplies their own Google spreadsheet, Apps Script deployment and randomly generated token. Fresh installations have no connection credentials. The desktop app sends reflections only to the configured Google Apps Script service, with HTTPS and restricted redirects. It has no analytics or automatic diagnostic uploads. The developer is not added to the user's spreadsheet.

Desktop settings, credentials, drafts and delivery queue are encrypted using Windows DPAPI in `%LOCALAPPDATA%\ReflectionTimerDesktop`. This protects files at rest; software running as the same Windows user may still access them. The older Chrome extension stores settings in Chrome local storage and does not have this desktop encryption guarantee.

Private setup codes are encoded, **not encrypted**. Treat a setup code, personalized setup script and API token as passwords. Never post them, state files, private spreadsheet links or unreviewed diagnostics in a public issue. A token allows submissions to its configured receiver. Restrict access to the spreadsheet and Apps Script project to trusted people.

If a token is exposed, disable the deployment or replace `REFLECTION_API_TOKEN` in the receiver's Script Properties, then update trusted clients. Removing a file from Git does not remove earlier commits or copies already downloaded. Old version-1 service-account keys should be revoked if they are still active.

For security reports, use GitHub's private vulnerability reporting option on the repository Security page when available. If unavailable, open an issue asking for a private reporting channel without including exploit details, credentials or personal data. Ordinary issues can include reproduction steps, app version and a reviewed diagnostic export.

Public desktop packages use original synthesized tones and contain no soundtrack recordings. Local audio selections are preserved on upgrade. Download access to a third-party recording does not establish permission to redistribute it with an app.

Keyboard controls and accessible names exist, but full JAWS compatibility has not been verified. Do not describe this release as fully JAWS accessible.
