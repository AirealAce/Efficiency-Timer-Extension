# Reflection Timer

A Manifest V3 Chrome extension for running focus sessions, prompting for a short reflection, and logging each response to Google Sheets.

## What it does

- Keeps timers accurate through popup closure, service-worker suspension, browser restarts, and computer sleep by storing an absolute deadline and using `chrome.alarms`.
- Supports pause/resume, reset, auto-restart, and scheduled starts.
- Shows an accessible reflection dialog on the active webpage when time expires.
- Automatically selects the dated tab using the computer's local date when you send, not when the timer started or finished.
- Writes newest-first timestamp/activity pairs in `A:B`. Entirely empty top rows are reused; otherwise whole sheet rows are inserted. Contents, notes and formatting in all columns move down together so full-width hour bands stay aligned.
- Gives every cell across a new entry's entire row thin white top, bottom, and side borders, matching column B (including the lines between columns). Other columns' backgrounds and text styling are preserved. Hour dividers keep their colored top/bottom-only borders.
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

## Configure Google Sheets

The included [`google-sheets-script.gs`](google-sheets-script.gs) is the only component allowed to write to the Sheet.

1. Open the target Google Sheet and choose **Extensions → Apps Script**.
2. Replace the Apps Script editor contents with `google-sheets-script.gs`.
3. In **Project Settings → Script properties**, add:
   - `SPREADSHEET_ID`: `synthetic-spreadsheet-id-for-tests`
   - `REFLECTION_API_TOKEN`: a long random value. The extension settings can generate one.
4. Choose **Deploy → New deployment → Web app**.
5. Set **Execute as** to yourself and allow anyone to access the web app. Requests still require the secret token and are restricted to the configured spreadsheet.
6. Copy the deployed URL ending in `/exec` into the extension settings.
7. Leave **Choose the tab** on automatic date matching (the default), then select **Save & test**. Tab names may use `MM/DD/YYYY` or `MM/DD/YY`, with or without leading zeros (for example, `09/05/2026` or `9/5/26`). If both match, a four-digit year takes priority. To opt out, choose **Always use a specific tab** and enter its name.

If the day's tab is missing, the first reflection creates `MM/DD/YYYY` from `Template`. Set the optional Apps Script property `TEMPLATE_SHEET_NAME` if your template has another name (for example, `Temp`). The copy keeps the template's formatting and layout. All copied A:B log contents/notes are cleared so test entries cannot leak into a new day; old-format timestamp/activity pairs in the first 16 rows of C:GR (up to the copy's existing complete pairs) are cleared too. The original template and existing daily tabs are never cleared. A missing template produces an error instead of saving elsewhere. Tab creation and inserting new entries share a lock.

Date routing also applies to requests from older extension versions, even if their saved tab name is still `Template`. Reload the unpacked extension to see the new mode selector. Existing entries are not moved between tabs or retroactively reordered. Connection tests use the same date routing but never write cells or create tabs; they report which tab would be created. When a client supplies no local time zone, the receiver uses the spreadsheet's time zone ([Apps Script reference](https://developers.google.com/apps-script/reference/spreadsheet/spreadsheet#getSpreadsheetTimeZone())).

After upgrading, reload the extension and refresh webpages to update the test-dialog wording. Clients from 2.2 already send the test flag and will be routed to `test` by the updated receiver, even if an old dialog still says Temp. Earlier content scripts do not send the flag and must be refreshed before testing. Hour markers and entries carry timestamp notes for rollover detection. Native [whole-row insertion](https://developers.google.com/apps-script/reference/spreadsheet/sheet#insertRowsBefore(Integer,Integer)) preserves row alignment. Old conditional-format rules are excluded only from the new A:B entry and full-width hour divider so they cannot override those colors; neighboring rules/ranges remain intact. Border formatting uses the [Apps Script Range API](https://developers.google.com/apps-script/reference/spreadsheet/range).

When the Apps Script changes, use **Deploy → Manage deployments**, edit the deployment, and select a new version. Saving code alone does not update an existing deployment.

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
