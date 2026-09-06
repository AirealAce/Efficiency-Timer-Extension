# Reflection Timer Desktop 3.2.0

A native Windows tray app for focus sessions, standalone reflection prompts, and the existing Google Sheets receiver. It does not need Chrome to stay open and does not collect browser activity. The extension source remains available as a fallback.

The default dark theme uses charcoal surfaces, light text, mint action buttons, and readable warning/error colors across the timer, schedules, outbox, settings, and reflection window. It does not change your Windows-wide theme. Windows accessibility contrast colors are respected at launch. Native title bars, menus, and system controls use [.NET's Windows 11 dark-mode support](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.application.setcolormode?view=windowsdesktop-10.0); Windows-owned dialogs may vary by OS version.

## Install and switch over

Requires Windows and the **.NET 10 Desktop Runtime (x64)**. Building requires the .NET 10 SDK and restores the pinned [NAudio 3.0.1](https://www.nuget.org/packages/NAudio/3.0.1) dependency for MP3 playback. Playback uses Windows audio codecs/output and works offline.

From PowerShell in this folder:

```powershell
.\install.ps1
# Or select a separately installed SDK:
.\install.ps1 -DotNet 'C:\path\to\dotnet.exe'
```

The installer runs tests, publishes a framework-dependent build, installs under `%LOCALAPPDATA%\Programs\ReflectionTimerDesktop`, and creates **Reflection Timer Desktop** on your desktop. Existing app binaries are copied to a dated backup before replacement. Close the app via **Quit**, not its window X, before updating. Neither installation nor updates erase saved data or enable Windows startup.

1. If needed, export a diagnostic report from the extension first. The desktop app can import its **future scheduled sessions** from that report under **Scheduling session times → Import extension schedules**. It does not automatically transfer an active countdown, browser settings, or pending reflection drafts; finish those first.
2. At `chrome://extensions`, turn off **Reflection Timer**. Leave it installed for rollback. Close any old webpage reflection overlays; if one lingers, refresh that page. The desktop app cannot toggle the extension itself.
3. Launch **Reflection Timer Desktop**, open **Settings**, and confirm the extension is off. This confirmation enables desktop timer starts and scheduled-session processing; it does not actually disable Chrome's copy.
4. Enter the same Google Sheet URL, deployed Apps Script URL ending in `/exec`, and Reflection API token used by the extension. **Save & test connection** is read-only. It neither creates a daily tab nor writes a reflection.
5. Leave the destination on automatic dates. Your existing receiver handles dated tabs, copies from the configured `Temp` template, alternating timestamp backgrounds, whole-row white entry borders, and full-width hour themes. No receiver redeployment is required for this migration.
6. Optional: enable **Start in the tray when I sign in to Windows**. This affects only this app's per-user Windows startup entry. It is off by default.

## Using the app

- Opening the Timer tab selects the Hours field. Typing hours clears the untouched 25-minute preset; minutes you explicitly edit are preserved.
- Start, pause/resume, reset, auto-start next session, and sound level are available on the regular timer.
- The default alert is the extension's original **popup.mp3**, bundled unchanged with the desktop app. **Settings → Alert sound → Choose MP3** selects a custom file (up to 50 MB); it is validated and saved immediately, separately from connection settings. **Preview sound**, **Stop preview**, and **Use extension default** are alongside it. This global choice applies to regular, scheduled, and test reflections; their existing volume settings still apply, including mute at 0%. Previews use the Timer volume and do not create reflections or contact Sheets.
- Custom files stay at their original location, so keep them available. Missing, unreadable, or undecodable custom audio falls back to the bundled default with a visible warning. Audio runs off the timer thread, never overlaps another preview/alert, stops when the app quits, and does not stop a timer or discard a reflection if playback fails. The custom path is saved in encrypted state but excluded from diagnostic exports; neither the audio nor its path is uploaded.
- Each scheduled session has its own local start date/time (minute precision), duration, repeat option, and sound level. Up to 50 one-time appointments are retained. A due appointment takes over the current timer. Pausing/resetting does not cancel future appointments.
- After sleep or downtime, only the latest missed appointment starts with its full duration. Earlier missed appointments are skipped, and future ones remain. A completed live session retains its reflection; an unfinished timer replaced by a scheduled appointment does not generate one.
- Auto-start repeats the duration immediately after completion, independently of the reflection window. After a long sleep it resumes once, rather than generating a flood of missed sessions.
- **Disable auto-start at** sits below the auto-start option on both the Timer and scheduled-session forms. Checking it automatically enables auto-start. Enter a local cutoff date/time (minute precision); on the Timer, edits save when you leave the field or press Enter. The cutoff disables repeating, but lets the current session finish and show its reflection. It is an absolute, one-time cutoff that survives pause/resume, sleep, and app restarts, not a daily recurring rule. Unchecking the cutoff leaves auto-start on indefinitely; turning off auto-start also clears its cutoff.
- Each scheduled appointment has its own optional cutoff, which must be after its start. A cutoff does not cancel independent future appointments. If a missed appointment starts after its own cutoff, its initial session still runs once, with repeating disabled. Existing timers and schedules keep their current settings without a cutoff until you choose one.
- The window X hides the app to the tray. Double-click the tray icon to reopen it; right-click for pending reflections, an issue marker, or Quit. Quitting stops alerts until the app is opened again. Windows sleep, shutdown, lock-screen restrictions, and notification settings can delay alerts; this app does not wake a sleeping computer.
- A reflection is a separate Windows window, not injected into a webpage. Drafts save locally after a short pause while typing and on close. **Later** retains the draft. **Skip** discards that prompt. **Save & send** first commits locally and then attempts Sheets delivery. Ctrl+Enter saves.
- **Test reflection prompt** always sends to the existing tab named **test**, never `Temp`, `Template`, or a dated tab. The receiver reports an error if `test` is missing.

## Local saves and delivery

The encrypted outbox retains the local date, UTC offset, destination, and content from the moment you save, even if delivery happens later. Entries that have not been attempted send automatically when the connection is configured and available.

The existing receiver does **not** provide idempotent writes. A timeout, failure, or interrupted upload therefore becomes **NeedsReview**, not an automatic retry: the Sheet may already contain the entry. Check the Sheet, then choose **Already in Sheet** or **Retry selected**. Retrying an entry that already arrived can create a duplicate. There is no exactly-once delivery guarantee. A maximum of 200 previously sent entries is retained locally; unsent entries are never automatically pruned. Concurrent uploads are serialized.

An old backup can contain stale state, so recovery pauses timers, disables scheduled-session processing until reconfirmed, and holds unsent entries for review. Review future schedules and pending prompts too: an older backup may contain a prompt you already handled. An unreadable original is preserved for recovery. If neither state file can be read, the app refuses startup rather than resetting your data.

Data lives in `%LOCALAPPDATA%\ReflectionTimerDesktop`:

- `state.dat`: timer, schedules, connection, drafts, and outbox, encrypted with Windows DPAPI for the current Windows user.
- `state.dat.bak`: previous committed encrypted snapshot. Writes use a flushed temporary file and atomic replacement.
- `diagnostics.dat`: encrypted rolling activity metadata, plus a previous-write backup.

Encryption protects stored files at rest; it is not protection against malware running as your Windows account. Keep the Windows profile/DPAPI keys when backing up; these files alone are not a portable cross-account backup. Data is not stored in the repository or desktop shortcut. The app does not read Chrome profile files. Do not share the data files or connection token in a public issue.

## Debugging

Click **Mark issue** when something feels wrong, then **Diagnostics → Export diagnostic report**. Reports include timer actions, app focus, schedules, prompts, upload outcomes, resume/session events, and issue markers. They exclude reflection text, drafts, connection credentials, browsing URLs, window titles, and other-app activity. Timestamps, scheduled sessions, and entry IDs are still personal metadata; review before sharing. Nothing uploads diagnostics automatically.

The rolling log retains at most 1,200 events / 7 days and can be paused or cleared. Retention is enforced during recording/reporting; disk files update on a successful log write. Clearing replaces the current log; the previous encrypted backup can still contain older events. Logs are best effort and can have gaps during crashes or storage failure.

## Development

```powershell
dotnet run --project ReflectionTimer.Tests -c Release
dotnet build ReflectionTimer.Desktop -c Release
dotnet run --project ReflectionTimer.Desktop -- --data-dir 'C:\path\to\isolated-test-data'
```

The Windows test runner covers deadlines, auto-start cutoffs (including exact-boundary and paused expiration), restart/sleep catch-up, independent schedules, transaction failures, draft/queue durability, ambiguous delivery, safe HTTP redirects, request contracts, encrypted storage recovery, diagnostic privacy, duration editing, shared repeat/cutoff controls, MP3 decoding/validation, default/custom sound selection, mute, cancellation, and fallback. Routine tests do not emit audio or contact Google; audio output and HTTP are mocked. The existing extension/receiver tests remain runnable with `npm test` and `npm run check` at the repository root.

For explicit connection provisioning, with the app closed, the executable accepts `--import-connection` with a `ConnectionSettings` JSON object through **stdin**. It validates and encrypts the settings without logging the token. Never pass secrets as command-line arguments or commit a provisioning file. `--check-connection` performs a read-only ping and prints a credential-free result. Invoke the DLL with `dotnet ReflectionTimer.dll ...` for reliable console piping. `--tray` starts without the main window once migration is confirmed.

To roll back, quit the desktop app and then enable the old extension. Do not run both timer engines together. To uninstall, quit the app and remove its app-specific startup option first, then remove its shortcut and installed binaries; preserve the LocalAppData data folder if you want to retain history.
