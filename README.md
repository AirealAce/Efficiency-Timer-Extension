# Reflection Timer

A Manifest V3 Chrome extension for running focus sessions, prompting for a short reflection, and logging each response to Google Sheets.

## What it does

- Keeps timers accurate through popup closure, service-worker suspension, browser restarts, and computer sleep by storing an absolute deadline and using `chrome.alarms`.
- Supports pause/resume, reset, auto-restart, and scheduled starts.
- Shows an accessible reflection dialog on the active webpage when time expires.
- Automatically selects the dated tab using the computer's local date when you send, not when the timer started or finished.
- Writes newest-first timestamp/activity pairs in `A:B`. Empty top cells are reused; otherwise only A:B shifts down. Existing entries are preserved, and other columns stay in place.
- Gives new entry cells thin white top, bottom, and side borders, including the divider between A and B. Hour dividers keep their colored top/bottom-only borders.
- Alternates timestamp backgrounds between white (black text) and `#595959` (white text), ignoring hour dividers when choosing the next color. Entry times are displayed as plain-text 12-hour labels (`6:21`); the cell note retains the full ISO timestamp and hour boundary.
- Inserts an hour divider beneath the first entry of each new hour, including the day's first entry. Each of the 24 hours has a distinct background/border theme; 6 PM is red. Dividers have top/bottom borders only, with automatically selected black/white text for contrast. Hours with no entries do not generate extra dividers.
- Routes **Test reflection prompt** submissions to `Temp`, never a dated or fixed live tab. The dialog labels test mode and shows the saved destination. A missing Temp tab is an error, not a fallback. **Save & test** remains a read-only connection check for the normal destination.
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

After upgrading to 2.2, reload the extension and refresh any webpage that still has an old reflection dialog before testing. Older content scripts do not send the test-mode flag. Hour markers and entries carry small timestamp notes for reliable rollover detection; native cell insertion preserves existing notes and formatting. Old conditional-format rules are excluded only from newly written cells so they cannot override the selected colors; neighboring rules/ranges remain intact. Cell insertion and border formatting use the [Apps Script Range API](https://developers.google.com/apps-script/reference/spreadsheet/range).

When the Apps Script changes, use **Deploy → Manage deployments**, edit the deployment, and select a new version. Saving code alone does not update an existing deployment.

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
