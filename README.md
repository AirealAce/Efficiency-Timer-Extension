# Reflection Timer

[Download for Windows x64](https://github.com/AirealAce/Reflection-Timer/releases/latest) · [Setup guide](desktop/START-HERE.html) · [Keyboard shortcuts](desktop/HOTKEYS.md) · [Privacy and security](SECURITY.md)

A focus timer with Google Sheets reflection logging. The new [Windows desktop version](desktop/README.md) provides a native tray timer, standalone prompts, independent scheduled sessions, encrypted local saves, and diagnostic exports. The Chrome extension below remains available as a fallback; disable it manually when switching to desktop so only one timer engine runs.

## Install the Windows app

1. Open [the latest release](https://github.com/AirealAce/Reflection-Timer/releases/latest) and download **ReflectionTimer-3.12.6-win-x64.zip** under Assets. GitHub's **Code → Download ZIP** and **Source code** downloads are for developers.
2. Right-click the ZIP → **Extract All**, then open the extracted folder and run **ReflectionTimer.exe**. The package includes .NET. No coding tools, Chrome extension or administrator account are required. It targets Windows 11 on Intel/AMD 64-bit PCs; other platforms are not verified.
3. On the first screen, **Install on this PC** optionally creates Desktop/Start menu shortcuts. Close the portable copy using **Quit desktop app**, then open the installed shortcut.
4. Follow **Guided setup** to connect **your own** Google spreadsheet. Allow about 5–10 minutes for the one-time Google Apps Script authorization and deployment. The included **START-HERE.html** walks through each step. Managed Google accounts may restrict web-app deployment.

The timer can run while setup is unfinished and reflections can remain saved locally; Google Sheets delivery requires completing the connection. Each person creates their own private token. Never send other users your private setup code or personalized script.

Fresh installations start at **15 minutes**, with the **compact view at bottom left**, **reflection popups at bottom right**, and **Low on time audio enabled at 15 seconds**. The public desktop download uses original synthesized sounds. You can select your own local MP3s in Audio settings. Upgrades preserve saved preferences and installed personal audio.

**Upgrading:** quit the old app from its tray menu, extract the new package, and use **Install on this PC**. Your existing Google connection, drafts and settings remain in place. Disable the Chrome timer extension manually if you switch to desktop.

**Before running:** this is an unsigned community app; Windows may show an unknown-publisher/reputation warning. Verify the GitHub source and release checksum and follow your organization's security policy. The app does not require disabling Windows protections. Full JAWS compatibility has not yet been verified.

See the [full setup guide](desktop/START-HERE.html), [desktop documentation](desktop/README.md), and [privacy details](SECURITY.md). The developer is not given access to your spreadsheet or reflections.

## Chrome extension features

- Keeps timers accurate through popup closure, service-worker suspension, browser restarts, and computer sleep by storing an absolute deadline and using `chrome.alarms`.
- Supports pause/resume, reset, auto-restart, and scheduled starts.
- Shows an accessible reflection dialog on the active webpage when time expires.
- Automatically selects the dated tab using the computer's local date when you send, not when the timer started or finished.
- Writes newest-first timestamps and activities in A/B. With receiver 2.5.0 and desktop 3.7.0, **C is actual time spent**, **D is allotted time**, **E is `ended early` or blank**, and **F is the optional early-end reason**. Durations have compact labels such as `15 secs`, `35 min 4 secs`, and `1 hr 50 min 20 secs`, omitting zero units, but remain numeric for calculations. Older clients/prompts without actual-time metadata leave C blank. Entirely empty top rows are reused; otherwise whole sheet rows are inserted. Contents, notes and formatting in all columns move down together so full-width hour bands stay aligned.
- Gives every cell across a new entry's entire row thin white top, bottom, and side borders, matching column B (including the lines between columns). Receiver **2.5.1** alternates column colors across each new entry: B/D/F/etc. use black backgrounds with white text, while C/E/G/etc. use white backgrounds with black text. This applies to inserted and reused rows, including blank cells, without changing A's independent alternation or full-width hour themes. Existing rows remain unchanged. C/D are widened to at least 220 pixels, E to 135, and F to 300, never shrinking wider layouts. Hour dividers keep their full-width colored top/bottom-only borders and have no duration or reason.
- Alternates timestamp backgrounds between white (black text) and `#595959` (white text), ignoring hour dividers when choosing the next color. Entry times are displayed as plain-text 12-hour labels (`6:21`); the cell note retains the full ISO timestamp and hour boundary.
- Inserts an hour divider beneath the first entry of each new hour, including the day's first entry. Its background, contrasting text, and colored top/bottom borders span every column in the sheet; there are no side or internal vertical borders. Each of the 24 hours has a distinct theme; 6 PM is red. Hours with no entries do not generate extra dividers.
- Routes **Test reflection prompt** submissions exclusively to the tab named `test`, never `Temp`, `Template`, a dated tab, or a configured fixed live tab. The dialog labels test mode and shows the saved destination. A missing `test` tab is an error, not a fallback. **Save & test** remains a read-only connection check for the normal destination. Daily-tab creation continues to use the configured template independently.
- Sends Sheet writes through a narrowly scoped Apps Script endpoint. No Google service-account private key is stored in the extension.

## Install the extension

1. Open `chrome://extensions`.
2. Turn on **Developer mode**.
3. Select **Load unpacked** and choose this folder.
4. Open the extension settings to configure Google Sheets.

Chrome blocks content scripts on its own internal pages. Keep a normal `http://` or `https://` tab open when a timer finishes or when testing the reflection prompt. The test button automatically reconnects the prompt to an older tab after an extension reload.

## Scheduling session times (2.5+)

Expand **Scheduling session times** in the popup to add multiple one-time sessions. Each entry has its own local start date/time, hours/minutes/seconds, **Auto-start next session**, and sound level (0% mutes the prompt sound). These controls are independent of the regular timer and remain editable while it runs. Use **Edit** or **Remove** on a saved entry; editing preserves its identity and does not replace other sessions. Up to 50 sessions can be saved, ordered by start time; identical start times are rejected.

A scheduled start takes over any current countdown, using that entry's full duration and options. Auto-start repeats the same duration immediately after completion, until paused/reset, disabled on the regular timer, or replaced by another scheduled start. It does not move the next scheduled appointment earlier. Appointments themselves run once; existing single-entry schedules migrate automatically on upgrade. Pausing or resetting the regular timer does not cancel future appointments—remove those entries explicitly.

After browser downtime or sleep, the latest missed appointment starts with its full duration, earlier missed appointments are skipped, and future entries remain scheduled. Schedule changes and timer/alarm actions are serialized; a stale completion alarm cannot end a new scheduled session. Starts, edits, removals, and skipped appointments appear in the local diagnostic log, and exports include the pending schedule's timing/options without connection settings or reflection text.

Reload the unpacked extension and refresh existing webpages to use the per-session sound settings. This update does not change the Google Sheets receiver or require an Apps Script redeployment.

## Configure Google Sheets

The included [`google-sheets-script.gs`](google-sheets-script.gs) is the only component allowed to write to the Sheet.

1. Open the target Google Sheet and choose **Extensions → Apps Script**.
2. Replace the Apps Script editor contents with `google-sheets-script.gs`.
3. In **Project Settings → Script properties**, add:
   - `SPREADSHEET_ID`: your own spreadsheet ID (the part between `/d/` and the next slash in its URL).
   - `REFLECTION_API_TOKEN`: a long random value. The extension settings can generate one.
4. Choose **Deploy → New deployment → Web app**.
5. Set **Execute as** to yourself and allow anyone to access the web app. Requests still require the secret token and are restricted to the configured spreadsheet.
6. Copy the deployed URL ending in `/exec` into the extension settings.
7. Leave **Choose the tab** on automatic date matching (the default), then select **Save & test**. Tab names may use `MM/DD/YYYY` or `MM/DD/YY`, with or without leading zeros (for example, `09/05/2026` or `9/5/26`). If both match, a four-digit year takes priority. To opt out, choose **Always use a specific tab** and enter its name.

If the day's tab is missing, the first reflection creates `MM/DD/YYYY` from `Template`. Set the optional Apps Script property `TEMPLATE_SHEET_NAME` if your template has another name (for example, `Temp`). The copy keeps the template's formatting and layout. All copied A:B log contents/notes are cleared so test entries cannot leak into a new day; old-format timestamp/activity pairs in the first 16 rows of C:GR (up to the copy's existing complete pairs) are cleared too. The original template and existing daily tabs are never cleared. A missing template produces an error instead of saving elsewhere. Tab creation and inserting new entries share a lock.

Date routing also applies to requests from older extension versions, even if their saved tab name is still `Template`. Reload the unpacked extension to see the new mode selector. Existing entries are not moved between tabs or retroactively reordered. Connection tests use the same date routing but never write cells or create tabs; they report which tab would be created. When a client supplies no local time zone, the receiver uses the spreadsheet's time zone ([Apps Script reference](https://developers.google.com/apps-script/reference/spreadsheet/spreadsheet#getSpreadsheetTimeZone())).

After upgrading, reload the extension and refresh webpages to update the test-dialog wording. Clients from 2.2 already send the test flag and will be routed to `test` by the updated receiver, even if an old dialog still says Temp. Earlier content scripts do not send the flag and must be refreshed before testing. Hour markers and entries carry timestamp notes for rollover detection. Native [whole-row insertion](https://developers.google.com/apps-script/reference/spreadsheet/sheet#insertRowsBefore(Integer,Integer)) preserves row alignment. Old conditional-format rules are excluded only from the new full-width entry and hour divider so they cannot override those colors; neighboring rules/ranges remain intact. Border formatting uses the [Apps Script Range API](https://developers.google.com/apps-script/reference/spreadsheet/range).

When the Apps Script changes, use **Deploy → Manage deployments**, edit the deployment, and select a new version. Saving code alone does not update an existing deployment.

Receiver **2.5.1** is a formatting-only update for new rows. Deploy it to the existing URL; desktop **3.7.1** needs no reinstall, restart, or settings changes.

Receiver **2.5.0** adds the C–F session details described above and duplicate-aware delivery for desktop **3.7.0**. Update the existing Apps Script deployment before installing the desktop update; no new URL, token, or Google service permissions are needed. Actual time excludes pauses and is captured before auto-start or scheduled replacement. Old Sheet rows remain unchanged; older clients still write their configured duration to D but leave unknown actual time in C blank. Duration is a fraction of a day, using [Google Sheets duration formats](https://developers.google.com/workspace/sheets/api/guides/formats); multiplying C or D by 86400 gives seconds.

The receiver reserves request IDs before writes, recognizes identical repeat requests, and holds interrupted partial writes for manual review. Desktop automatic retries require its advertised safe-delivery protocol and retain the original entry ID and receiver. Preserve timestamp notes and `RT_RECEIPT_` Script Properties; see [desktop delivery and recovery](desktop/README.md#local-saves-and-delivery) for limits and manual-review behavior. The fallback extension does not gain desktop overlap or elapsed-time tracking from a receiver-only update.

## Debugging intermittent timer behavior (2.4+)

1. Reload Reflection Timer at `chrome://extensions`, then refresh existing webpages once. No Apps Script redeployment is needed for this update.
2. Use the timer normally. If something feels wrong, click **Mark issue for debugging** in the popup as soon as possible. This captures the current timer/deadline, actual Chrome alarms, and active tab ID without changing the timer.
3. Open **Settings → Debugging & activity log → Export diagnostic report** and share the downloaded JSON with the person debugging (or attach it in your Codex conversation). Export soon after an issue, before older events roll out. Logs are not sent anywhere automatically, and the assistant cannot see them until you share the report.

Logging is enabled by default for this diagnostic feature. It records timer commands and resulting state, popup load/message failures, schedules, worker restarts/restoration, alarm lateness, tab activation/loading/discard/freeze events, window focus, page visibility, reflection prompt delivery/injection, save outcomes, and sound failures. The issue marker and report also compare expected deadlines with the actual alarm inventory. A new worker session does not by itself indicate a bug: Chrome normally suspends idle extension workers ([Chrome lifecycle documentation](https://developer.chrome.com/docs/extensions/develop/concepts/service-workers/lifecycle)).

The persistent local rolling log keeps at most **1,200 events from the past 7 days**. It is pruned on recording and reporting, not by a background heartbeat; old data on disk is removed on the next successful log write or clear. Switch logging off in settings to pause collection, or use **Clear log** to remove it. Disabling does not erase existing events. Export still includes a read-only current-state snapshot while logging is off. No new permissions, polling, timer keep-alive, or server upload are added.

Only allowlisted event names, numeric values, booleans, and fixed categories are saved. Reports never include browsing URLs, titles, page contents, reflection text, raw error messages/stacks, Sheet names, API tokens, or connection settings. Tab/window IDs and activity timestamps are included and may still be personal; review before sharing. Logs are best effort: they cannot reconstruct activity before installation/reload, and abrupt browser shutdown, storage failure, or an invalidated page context can leave gaps. Page-hide events are not guaranteed. This adds evidence for diagnosis; it does not claim to fix the intermittent behavior yet.

## Security migration

Version 1 stored a service-account email and private key in `chrome.storage.local`. Version 2 removes those values automatically and no longer requests access to the Google Sheets or OAuth APIs.

If the old service-account key was active, revoke/delete that key in Google Cloud and remove the obsolete local `.env` and service-account JSON files after confirming version 2 works. They are ignored by Git but still contain credentials on disk.

## Development checks

Requires a current Node.js version:

```sh
npm test
npm run check
```

After changes, reload the unpacked extension from `chrome://extensions` and use **Test reflection prompt** plus **Save & test** in settings.
