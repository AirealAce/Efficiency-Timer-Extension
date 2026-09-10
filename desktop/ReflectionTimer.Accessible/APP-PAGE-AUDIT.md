# App view comparison — accessibility preview 0.4

Compared against the original 3.12.9 `MainWindow.cs`, `Controls.cs`, `AudioControls.cs`, `SetupWindow.cs`, and `ReflectionWindow.cs`. This is an inventory of corresponding features and the corrections made, not a claim of pixel-identical rendering or completed screen-reader acceptance.

| Original page | Corresponding features in 0.4 | Correction / verification |
| --- | --- | --- |
| Timer | Countdown/status, Hours/Minutes/Seconds, Start/Pause/Resume, Reset | Shared duration draft and timer behavior tests pass. Visual countdown remains quiet for readers. |
| Timer | Start timer at and Schedule session | Label, field, and button restored to one horizontal row. Geometry checked at the original App width. |
| Timer | Auto-start next session and optional cutoff | Centered checkbox/date row; enabling a cutoff enables repeat. Disabling repeat clears the cutoff. |
| Timer | Low on time audio, inherited/individual threshold, sound selection, preview, custom MP3 | Present. Threshold drafts survive background updates; individual sound and threshold are sent together. |
| Timer | App sound slider | Restored and synchronized with the Settings slider. Keyboard changes reach the master-volume command. |
| Timer | Test reflection prompt, Pending reflections, Mark issue, pending/unsent counts, floating visibility, Quit | Present. Pending reflections opens all waiting prompts. Closing App still hides to tray; Quit flushes drafts. |
| Scheduling | Overlap policy and explanation | Restored to this tab, with the original three choices. |
| Scheduling | Start time, Duration, Auto-start, Auto-start cutoff, Sound, Low on time, Status | Original seven columns restored. Selection retains native table cells and stable row identities. |
| Scheduling | Edit selected, Remove selected, Import extension schedules | One action row below the table. No repeated action buttons or added action column. |
| Scheduling | Start date/time, three duration fields, repeat/cutoff, low-time settings, App sound, Add/Save, Cancel edit / new session | Present. Edit retains the entry ID. Reset restores the initial editor values. Hours/minutes/seconds are preserved in the saved duration. |
| Scheduling | Start / Wait / Skip for a due appointment needing a decision | Shared conditional controls operate on the selected appointment. |
| Outbox | Saved locally, Destination, Status, Attempts | Original four columns restored; newest entries first. |
| Outbox | Selected reflection and metadata below the table | Readable region on the page, including actual/allotted time, check-in/early-end details, reason, retry time, and review information. |
| Outbox | Send pending now, Retry selected…, Already in Sheet, Open Google Sheet | Original shared action row restored. Retry uses a labeled review dialog and returns focus on cancel. Already in Sheet acts on the selected review entry. |
| Settings | Theme choice, preview and explanation | Present for all four palettes. Windows contrast colors remain supported by CSS and the host. |
| Settings | Guided setup / another PC and Setup guide | Guided setup opens a separate three-step dialog; closing it restores the same draft fields to Settings. The packaged original guide opens locally. |
| Settings | Compact visibility/position, reflection position, placement explanations | Present. App opens centered; default floating and popup corners are retained. Popup monitor selection follows the mouse pointer, as in the original. |
| Settings | Keyboard shortcut information and availability | Present for all five shortcuts. Start/end shortcut now starts or resumes when stopped and ends early when running. Reopening App preserves a selected Settings tab and does not move focus behind a setup dialog. |
| Settings / Audio | Playback explanation; default low-time warning | Original text and position restored. Enter leaves the threshold field; Save settings / Ctrl+Enter commits it. |
| Settings / Audio | App sound slider and explanation | Restored separately in Settings, sharing the same master volume as Timer. |
| Settings / Audio | Success messages; Failure messages; Low on time audio; Session end · time limit reached | Original four-group order restored. |
| Settings / Audio | Track, Disruptive / Assertive / Polite, Preview audio, Choose MP3… | Present in every group. Defaults/None/bundled/custom selections are supported; unavailable saved tracks remain represented. Native MP3 dialogs require interactive acceptance. |
| Settings / Audio | Per-event Volume slider; Fade out after; seconds; fade explanation | Restored in every group. Fade seconds are disabled when unchecked. Keyboard slider, playback, and fade changes are verified to autosave to the correct event. |
| Settings / Audio | Stop all app audio; autosave and mixing explanation | One shared stop button restored and connected to the backend. |
| Settings | Duplicate-timer confirmation, connection URLs/token/destination, fixed tab name, Save & test connection | Present. Fixed tab name is enabled only for fixed mode. Incomplete settings can be saved; explicit connection testing uses an authenticated ping. |
| Settings | Sign-in startup and local diagnostic recording | Both controls restored. Startup targets only the preview executable/profile with a separate registry value, off by default. Argument/name construction and preference preservation are tested; sign-in itself was not exercised. |
| Settings | Save settings button and Ctrl+Enter | Available only on Settings, matching the original. Visibility checked on all five tabs; shortcut checked outside Settings as well. |
| Diagnostics | Explanation, recording/event/storage summary, recent history, Mark issue, Refresh, Export, Clear log | Present. Summary/history are readable text. Export uses the existing redaction rules; clearing requires the existing confirmation. |

## Verification and remaining boundaries

52 engine/storage/service checks and 64 browser behavior/semantic checks pass. Browser tests use a synthetic bridge, exercise the restored controls, and render the App tabs and audio groups. C# tests cover timer/delivery behavior, isolated storage, startup command construction, and read-only connection testing. Packaged audio is checked separately against source hashes.

This preview keeps its separate profile and local example records. Those records remain local, with a collapsed simulation action separate from the original Outbox toolbar. The production profile and Windows startup entry are never adopted. The guided setup uses file export/display for private setup material; its clipboard commands and production installer are outside this App-page restoration.

Full native file-dialog, Windows sign-in, high-DPI/multiple-monitor, and JAWS/NVDA/Narrator acceptance remain manual checks. The prior legacy suite's native focus/theme failures are documented in PREVIEW-README.md; these are not represented as passing. No GitHub main update or production release is part of this change.
