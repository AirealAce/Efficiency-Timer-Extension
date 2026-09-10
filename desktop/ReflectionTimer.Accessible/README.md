# Reflection Timer accessibility preview 0.1

An isolated WebView2 prototype using the production C# timer engine. It implements the first acceptance checkpoint of the accessibility plan, based on the three findings in the 3.12.9 audit. It is not a replacement for the installed app yet.

## Run the preview

Extract the entire portable folder and run `ReflectionTimer.AccessibilityPreview.exe`. The portable build includes .NET. It needs the Microsoft Edge WebView2 Evergreen Runtime, normally present on Windows 11. If missing, the preview displays readable installation instructions.

The preview uses a separate encrypted profile under `%LOCALAPPDATA%\ReflectionTimerAccessibilityPreview\review`. It does not read the installed Reflection Timer's profile, register global shortcuts, change startup registration, install itself, or send data to Google Sheets. Optional `--profile review-name` selects another profile under the same dedicated preview root; arbitrary data-directory arguments are not accepted.

The timer and Scheduling actions use the real engine. Reflection drafts persist, and saving a reflection moves it into the local Outbox. “Simulate success” updates an Outbox row without making a network request. The two initial schedules and two initial Outbox records are examples in this test profile.

Close the main window to quit the preview. Close the compact window to leave the timer running in the main window. Main-window exit waits for open reflection windows to save their latest drafts. Existing saved timer deadlines and drafts survive reopening.

## What to try with your reader

1. Tab to Minutes. Leave the edit field using your reader's normal forms-mode command, then read surrounding text with reading commands. Read headings and paragraphs at your own pace.
2. Start a short session. Continue reading for at least a minute, first in this app and then another app. The visual countdown should not generate speech or move focus. “Read remaining time” should report current time and state once. The “Time checked” text is explicitly a snapshot, not a silently stale live value.
3. Open the compact window and use “Time-only view,” “Read remaining time,” and “Show timer controls.” These actions remain available without hovering. This prototype keeps ordinary window chrome and Alt+Tab visibility to make the windows discoverable.
4. Navigate Scheduling and Outbox with the reader's table commands. Verify the individual cells, column/row headers, and action names. Simulate an Outbox update and check that reading position remains stable.
5. Open a practice reflection. Read the context, type a draft, choose Later, and reopen it. Then save it to the local Outbox and open its Details dialog. Try Escape and verify return focus.
6. Try enlarged text, browser zoom, narrow windows, and Windows contrast themes.

Record the app, OS, WebView2 runtime, and reader versions, relevant reader settings, exact keys, expected behavior, and observed speech/focus. A useful result distinguishes keyboard focus from the screen reader's reading cursor.

## Current scope

Included: main timer; a separate compact/time-only window; on-demand time speech through a status region; one-window announcements for significant events; local reflection/check-in forms; persistent drafts; native HTML tables with stable rows/cells; schedule addition/removal; local Outbox and details; local simulation of a status change.

Deferred until the reader checkpoint passes: production audio playback, real Sheets delivery, setup/connection screens, full settings and diagnostics, existing-profile migration, production tray/hotkey integration, and final window presentation. The production project and release metadata remain at 3.12.9.

The host restricts web messages to its packaged local origin and validates command inputs. It blocks other navigation, new windows, downloads, and web permission requests. Connection credentials and custom audio paths are excluded from the view data. The prototype deliberately has no Sheets client.

## Development and verification

Build using the .NET 10 SDK:

```text
dotnet build desktop/ReflectionTimer.Accessible -c Release
dotnet run --project desktop/ReflectionTimer.Accessible.Tests -c Release
dotnet publish desktop/ReflectionTimer.Accessible -c Release -r win-x64 --self-contained true -o desktop/artifacts/accessibility-preview
```

The WebView2 SDK is pinned to 1.0.4191.47. Its unused WPF assembly reference is removed before resolving references, since this host uses WinForms.

The browser checks require Playwright available to Node and Microsoft Edge installed:

```text
node desktop/ReflectionTimer.Accessible.Tests/ui.cjs
```

If using a shared dependency runtime, set `NODE_PATH` to its `node_modules` directory. The browser fixture loads the actual packaged HTML/JS/CSS with a synthetic bridge and does not contact an external page.

Validation on September 10, 2026:

| Check | Result |
| --- | --- |
| C# build and portable publish | Passed without warnings |
| Engine/adapter/storage tests | 19 passed |
| Browser behavior/semantics tests | 18 passed |
| Windows WebView2 startup and visual inspection | Preview loaded successfully |
| Screen-reader speech and reading navigation | Pending manual acceptance; not established by the automated checks |

The browser tests include 120 clock updates without DOM focus changes, stable table row/cell/action identities, native headings and table roles, narrow-window reflow, dialog return focus, keyboard access to compact controls, and flushing the final draft before close acknowledgment. The storage tests include invalid input, failed saves, encrypted persistence, clock boundaries, and excluded connection fields.

Windows computer-use tooling returned no accessibility tree for this WebView2 window, including after launching installed JAWS 2026. Its launcher also failed before creating the process; direct executable launch succeeded. These are limitations of the observed verification, not a screen-reader pass or proof of a WebView2 accessibility defect. Actual JAWS/NVDA/Narrator results determine whether to continue with this host or compare Electron.

## Design references

- [WebView2 in WinForms](https://learn.microsoft.com/en-us/microsoft-edge/webview2/get-started/winforms)
- [WebView2 security](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security)
- [Native table semantics](https://www.w3.org/WAI/ARIA/apg/patterns/table/)
- [Live-region behavior, including focused regions](https://www.w3.org/TR/wai-aria-1.2/#aria-live)
- [Status messages](https://www.w3.org/WAI/WCAG22/Understanding/status-messages.html)

Development stays on `accessibility/webview2-prototype`. Do not merge into master, change the public README, publish a release, or update an installed copy until functionality and reader acceptance have been reviewed.
