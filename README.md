# Reflection Timer

A Manifest V3 Chrome extension for running focus sessions, prompting for a short reflection, and logging each response to Google Sheets.

## What it does

- Keeps timers accurate through popup closure, service-worker suspension, browser restarts, and computer sleep by storing an absolute deadline and using `chrome.alarms`.
- Supports pause/resume, reset, auto-restart, and scheduled starts.
- Shows an accessible reflection dialog on the active webpage when time expires.
- Automatically selects the dated tab using the computer's local date when you send, not when the timer started or finished.
- Writes timestamp/activity pairs into 16-row blocks (`A:B`, then `C:D`, and so on), matching the existing workbook layout.
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

If the day's tab is missing, the first reflection creates `MM/DD/YYYY` from `Template`. Set the optional Apps Script property `TEMPLATE_SHEET_NAME` if your template has another name (for example, `Temp`). The copy keeps the template's formatting and layout; only the first 16 rows of complete timestamp/activity column pairs (up to 200 columns) have copied contents cleared. The original template and existing daily tabs are never cleared. A missing template produces an error instead of saving elsewhere. Tab creation and appending share a lock to avoid duplicate daily tabs from simultaneous sends.

Date routing also applies to requests from older extension versions, even if their saved tab name is still `Template`. Reload the unpacked extension to see the new mode selector. Existing entries are not moved. Connection tests use the same date routing but never write cells or create tabs; they report which tab would be created. When a client supplies no local time zone, the receiver uses the spreadsheet's time zone ([Apps Script reference](https://developers.google.com/apps-script/reference/spreadsheet/spreadsheet#getSpreadsheetTimeZone())).

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
