# Reflection Timer

The primary app is now the accessible Windows desktop version, **4.0.5**. Its HTML interface runs inside a C# / WebView2 desktop host and uses the existing timer engine, encrypted storage, MP3 library, and Google Sheets receiver.

## Project folders

- `desktop/` — current accessible app, shared engine, tests, audio, installation and packaging.
- `extension-version-useless/` — archived Chrome extension.
- `inaccessible-version-useless/` — archived original native Windows app.
- `google-sheets-script.gs` — current shared Google Sheets receiver.
- `scripts/` and `test/` — current audio, source-package and receiver checks.

The archives are preserved for reference and rollback; they are not dependencies of the current app. Do not run the archived extension alongside the desktop timer.

## Install or upgrade

Windows x64 and Microsoft Edge WebView2 Evergreen Runtime are required. A self-contained package includes .NET. Open `desktop/START-HERE.html` for setup and keyboard help.

From source with the .NET 10 SDK:
```powershell
.\desktop\install.ps1
```

The installer backs up the old executable folder and encrypted data, installs all web assets, and retains the usual desktop shortcut. It does not publish anything. The app reads the existing `%LOCALAPPDATA%\ReflectionTimerDesktop` profile, including the same Sheet, receiver URL, token, routing, drafts, schedules, Outbox, audio choices, and preferences. Do not copy private configuration into this repository.

A fresh installation starts at 15 minutes with the compact timer enabled at bottom left, session-end prompts at bottom right, App centered, and low-time warnings enabled at 15 seconds. Existing choices take precedence. Dark, Light, High Contrast and Glamour themes and the original Ctrl+Alt hotkeys are available.

## Views and screen readers

App, the floating timer, and session-end prompts are separate windows. Compact and Time-only share one floating window. The interface uses headings, labels, real buttons, tables, keyboard focus, and restrained announcements; the countdown does not speak every tick. Use your screen reader's web reading and table commands. Compatibility still benefits from testing with your particular reader and version.

## Development checks

```powershell
dotnet run --project desktop/ReflectionTimer.Tests -c Release
node desktop/ReflectionTimer.Tests/ui.cjs
node --test test/apps-script.test.js test/receiver-setup.test.js test/release-assets.test.js
node scripts/check-public-source.cjs
```

Browser checks require Playwright and Microsoft Edge. `desktop/package-release.ps1` runs these checks and creates a verified self-contained ZIP locally. It never uploads a release.

Explicit `--profile NAME` launches remain isolated in the old accessibility-preview data location for development. Normal launches use the installed desktop profile. `ReflectionTimer.exe --check-connection` performs an authenticated read-only Sheets check and emits only its result and capabilities.

See [keyboard shortcuts](desktop/HOTKEYS.md), [audio sources](desktop/Sounds/README.md), and [security notes](SECURITY.md).
