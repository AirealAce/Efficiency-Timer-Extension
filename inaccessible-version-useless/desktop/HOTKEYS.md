# Keyboard shortcuts and upgrading another PC

The current repository is [AirealAce/Reflection-Timer](https://github.com/AirealAce/Reflection-Timer). The desktop update is 3.12.9; the old 3.6.4 build only had Ctrl+Alt+T. Downloading source or renaming a repository does not update an already installed app.

## Current mappings

All shortcuts require Reflection Timer to be running, including in the system tray. They do not depend on Chrome or Google Sheets connectivity.

| Shortcut | Action |
| --- | --- |
| Ctrl+Alt+T | If the main window is focused, hide it exactly like its X button. Otherwise bring it forward, preserving the selected tab; on Timer, select the first positive duration field. The timer and compact view continue unchanged. |
| Ctrl+Alt+backtick (`) | Start the specified timer, resume a paused timer, or end a running session early and show its reflection. Auto-start and its cutoff still apply. |
| Ctrl+Alt+/ | Cycle compact controls → time-only → hidden → controls. The countdown continues. |
| Ctrl+Alt+. | Select the compact duration. Press twice within 0.8 seconds to select it in the full Timer tab. |
| Ctrl+Alt+, | Open/focus a check-in for the running or paused session. Sending records elapsed active time without ending the timer. Use Pending reflections for older drafts. |

Duration focus chooses the first value above zero from the left, or Hours if all are zero. Running-session duration fields are read-only; pause to edit. Period double-press pairing resets after a different app shortcut or more than 0.8 seconds.

## Upgrade without replacing the Sheet connection

1. Finish or pause active work, save reflection drafts, and **Quit** the old app from its tray menu. Closing the main window normally leaves it running.
2. Extract the verified 3.12.9 Windows x64 release to a new folder. Do not overwrite files in a running app's folder.
3. Run the extracted ReflectionTimer.exe. To replace the regular per-user install, choose **Guided setup → step 1 → Install on this PC**. The installer retains local settings/data and existing MP3 files. Do not import someone else's connection code.
4. Launch the installed app and check its executable's **Properties → Details → Product version**. It should say 3.12.9. Existing desktop shortcuts should point to the installed copy, not an old extracted download.
5. Check **Settings → Keyboard shortcuts** for individual registration failures. Another running copy or another app can own a chord. Quit the conflicting copy/app and reopen Reflection Timer; there is no need to change the Sheets URL or token.

The update does not require changing an already working receiver deployment or credentials. Receiver 2.6.0 or newer is needed for sending check-in rows; if an older receiver is detected, the entry remains saved locally. A receiver source file in a download is not deployed automatically.

For a source checkout, update origin to `https://github.com/AirealAce/Reflection-Timer.git`, fetch, and inspect local changes before updating the branch. Do not reset or overwrite uncommitted work. Build the current source instead of reusing an old `bin`, `artifacts`, or installed executable.

## Verify behavior safely

- Hide the main app in the tray; Ctrl+Alt+T should restore it. Press again while the main window is focused: it should hide like X, without quitting or stopping the timer. Minimize it and repeat. From a reflection or compact window, T should bring the main app forward, not hide it. An owned modal stays in front.
- Use slash to cycle compact modes; period once selects compact duration and twice selects the full Timer duration.
- With a short disposable session, backtick starts the timer. Another press ends it early and opens a reflection; skip that test reflection instead of sending it to a live sheet.
- During a disposable running session, comma opens a check-in. The countdown should continue. Repeated presses preserve the draft. Skip the draft instead of sending if you are only testing.
- If comma is unavailable, the tray's **Check in to current session** invokes the same action. Other unavailable shortcuts also have normal app controls as alternatives.

Punctuation bindings currently use Windows US-keyboard virtual keys (OEM grave, slash, period, comma). Different keyboard layouts can label those keys differently; custom remapping is not implemented. No general keyboard hook records typed content. Local diagnostics record each shortcut's registration/usage result, not keystrokes, reflection text, or credentials.

## Developer regression gate

Run `dotnet run --project desktop/ReflectionTimer.Tests -c Release -- --hotkeys` on Windows. These isolated tests use synthetic state and an injected registration backend, leaving the running user's timer and real global chords alone. They cover all five exact virtual-key/modifier mappings, independent conflicts/disposal, hidden/minimized window behavior, focus selection, compact cycling, early endings, and check-ins. They do not prove that another PC's real chords are free; check that PC's status panel as well.

The public packaging script runs this gate before producing a ZIP, alongside onboarding/install, delivery-safety, receiver, and bundled-audio checks. It includes only the eight hash-verified approved MP3s and excludes local data, credentials, and additional personal audio.
