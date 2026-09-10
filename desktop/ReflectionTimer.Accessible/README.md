# Reflection Timer accessibility preview 0.3

This local preview adds semantic HTML in WebView2 to the existing C# timer, storage, audio, and Sheets services. It retains the original window model and control layout. Development is on `accessibility/webview2-prototype`; production remains 3.12.9. Do not merge into master, update the public repository page, publish a release, or replace an installed copy until reviewed.

## The four original views

| View | Initial position | Behavior |
| --- | --- | --- |
| App | Center | Five tabs: Timer, Scheduling session times, Outbox, Settings, Diagnostics. Closing hides it in the tray. |
| Compact | Bottom left | Duration, Auto-start, App, reset/start/pause/end, and the original caption actions. Borderless and always on top. |
| Time-only | Bottom left | Another mode of the same floating window. Starts automatically with a running timer; controls can be expanded again. |
| Session end | Bottom right | Independent reflection prompt with text, optional early-end reason, Skip, Later, and Save & send. |

App, the floating timer, and session-end windows can coexist. Only Compact and Time-only are mutually exclusive. Switching between them keeps the bottom-left anchor. Saved placement choices are respected; App opens centered. Moving App does not force it back to center during ordinary updates.

The original four color palettes, flat App tabs, separate audio sections, three duration fields, and centered inline checkboxes are retained. Settings and connection controls belong to App, never Compact. Standard HTML fields and focus indicators support reading and keyboard access; this is not a claim of pixel-identical rendering.

## Run and exit

Extract the entire portable folder and run `ReflectionTimer.AccessibilityPreview.exe`. .NET is included; Microsoft Edge WebView2 Evergreen Runtime must be installed. A startup error explains how to obtain it if missing.

The default preview profile is `%LOCALAPPDATA%\ReflectionTimerAccessibilityPreview\review`. `--profile review-03` selects a separate fresh profile. Names accept only letters, digits, and hyphens. Only one process can use a profile. Earlier preview data can be reopened after quitting the earlier process using that profile.

Closing App hides it. Reopen it from the preview tray icon; choose **Quit desktop app** or **Quit accessibility preview** in the tray to exit. Open reflection drafts are flushed before exit. Closing Compact hides only the floating window. Compact/Time-only do not appear in Alt+Tab, matching the original; App and session-end windows do.

The original Ctrl+Alt shortcuts are registered when available: T for App, slash to shrink/hide floating controls, period to focus Compact (twice for App), comma for a check-in, and backtick to end early. Another running timer can already own those chords. Failures are recorded in diagnostics; they do not replace another app's registration. Tray and App controls remain available.

This build does not access the installed app's profile or change its startup registration. It does not install itself or start automatically with Windows.

## Functions available for review

- A 15-minute initial timer, compact visibility enabled, and low-time warning enabled at 15 seconds. Duration drafts are shared between App and Compact without starting or resetting a session.
- Start/pause/resume/reset, repeat and optional cutoff, check-ins, end early, practice prompts, saved drafts, and independent automatic session-end windows. No readiness toast is generated.
- Four audio events, eight existing MP3 files, custom MP3 selection, master/event volumes, mixing behavior, fade duration, preview and stop. Track changes preview automatically; audio and display changes save immediately. Save settings or Ctrl+Enter commits default threshold and diagnostics changes.
- Scheduling with hours/minutes/seconds, edit/remove, repeat/cutoff, volume, inherited or individual low-time threshold and sound, overlap choices, and extension schedule import.
- Connection instructions, receiver script/token generation, saved setup drafts, private setup-code import/export, opt-in verified delivery, Outbox details/retry/review, and opening the configured sheet.
- Local diagnostics, issue markers, readable report, export, and clear log.

## Accessible reading

Headings, labels, paragraphs, native tables, and native HTML dialogs expose readable structure. Table rows, cells, controls, and unchanged audio options retain their identities during updates. Ticks change only the visual countdown, excluded from the accessibility tree. A **Time checked** snapshot changes on explicit request or timer-status transition. In Compact/Time-only, the clock is a button with the stable name **Read remaining time**. Significant events use one window's status region.

The user reported that text and table navigation in 0.1 worked well. That supports continuing this approach; it is not full JAWS, NVDA, or Narrator acceptance.

## Preview data and connection

A fresh profile contains two example schedules and two example Outbox records. Reflections saved before a valid connection exists remain **Local preview only** permanently. Enabling a connection cannot adopt or upload them. **Simulate success** is available only for these records and makes no request.

No Sheets requests occur until the user saves a valid connection and explicitly enables delivery. Verification uses an authenticated ping. New connected reflections then use the existing request-ID/retry protocol; practice reflections use the receiver's test tab. Pausing prevents the next write but cannot retract a request already sent. Uncertain writes require explicit review before retrying or marking already sent. Entries retain their destination; the shared engine blocks unsafe account changes.

`LocalOnly` is enforced by upload selection and the HTTP client. It defaults to false, preserving normal production offline binding. Service tests use a simulated receiver and silent audio; no real spreadsheet was used for automated verification.

The host allows only exact packaged resources under its local virtual origin. It blocks external navigation, downloads, permission requests, new windows, and host objects. Pages use a restrictive Content Security Policy. Timer/compact/reflection updates omit connection credentials and custom file paths. Saved tokens are omitted from ordinary settings responses. Explicit setup-draft restoration and setup-code export can reveal the user's private token; setup codes are not encrypted.

## Verification recorded September 10, 2026

| Check | Result |
| --- | --- |
| Engine, adapter, storage, and service checks | 44 passed |
| Browser semantics, behavior, and view checks | 39 passed |
| Shared production delivery safety checks | 40 passed |
| Full legacy suite | 477 passed; 16 native focus/theme failures |
| Untouched 3.12.9 theme comparison | Same 13 theme failures |
| Legacy focus comparison | Untouched baseline 11/11; current rerun 10/11, with an owned-modal draft focus failure still unresolved |
| Initial 0.1 text/table reading checkpoint | Positive informal user feedback |
| Complete JAWS/NVDA/Narrator acceptance | Pending |

Preview checks cover quiet ticks, shared/unfinished drafts, table/option identity, dialog focus return, narrow reflow, the original Compact controls, exclusive Compact/Time-only mode, independent page state, placement calculations, reflection action fit, encrypted storage, failed-save behavior, isolated sample data, pause races, retry safety, and diagnostic redaction. Browser checks use a synthetic host bridge; they do not prove native focus, multi-monitor/DPI behavior, or reader speech.

The native computer-use tool could not reliably capture the foreground preview or return its WebView2 accessibility tree during earlier checks. These limitations and the legacy focus failure are reasons to keep this as a local preview.

## Reader and functional acceptance

1. Leave an input using your reader's forms-mode command. Read surrounding text, headings, and labels in every App tab and reflection prompt.
2. Run a timer while reading in this app and another app for a minute. Ticks must not speak or move focus. Request remaining time and check that it speaks once.
3. Open App, Compact, and a practice reflection together. Switch Compact to Time-only and back. Confirm other windows and drafts remain intact and default positions are correct.
4. Read individual Scheduling and Outbox cells. Edit an entry and check headers, action names, and retained reading position.
5. Try audio, low-time overrides, schedules, cutoff, setup drafts, diagnostics, and native file dialogs. Check foreground behavior when a dialog is already open and a global shortcut is pressed.
6. Check Later, Skip, Save & send, and exit with unfinished drafts. Use a tester-owned sheet only after deliberately enabling delivery.
7. Try JAWS, NVDA, Narrator, keyboard-only use, contrast themes, enlarged text, zoom, and different monitor/DPI arrangements. Record versions, exact keys, expected behavior, and observed speech/focus.

Before production adoption: resolve native focus findings, complete reader and functional acceptance, and implement reviewed existing-profile migration and installer/startup integration. Do not present this preview as complete production parity or universally screen-reader compatible.

## Development

```text
dotnet build desktop/ReflectionTimer.Accessible -c Release
dotnet run --project desktop/ReflectionTimer.Accessible.Tests -c Release
node desktop/ReflectionTimer.Accessible.Tests/ui.cjs
dotnet publish desktop/ReflectionTimer.Accessible -c Release -r win-x64 --self-contained true -o desktop/artifacts/accessibility-preview
```

Browser checks require Playwright on Node's module path and Microsoft Edge. `REFLECTION_PREVIEW_SCREENSHOTS` optionally selects a directory for the four view renderings.

References: [WebView2 WinForms](https://learn.microsoft.com/en-us/microsoft-edge/webview2/get-started/winforms), [WebView2 security](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security), [native tables](https://www.w3.org/WAI/ARIA/apg/patterns/table/), [ARIA live regions](https://www.w3.org/TR/wai-aria-1.2/#aria-live), [status messages](https://www.w3.org/WAI/WCAG22/Understanding/status-messages.html).
