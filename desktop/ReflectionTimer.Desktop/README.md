# Reflection Timer 4.0.7

The accessible interface is now the primary desktop app. It uses the existing encrypted ReflectionTimerDesktop profile and retains the original Sheets connection, settings, drafts, schedules and Outbox. No personal connection settings belong in the application package.

The four views, themes, audio controls, window positions and Ctrl+Alt shortcuts are retained. App and reflection prompts can coexist with either Compact or Time-only. Semantic HTML supports screen-reader web navigation; quiet countdown updates do not interrupt reading.

Compact has a two-line Auto/Start toggle that lights up when enabled. Screen readers announce it as the "Auto-start next session" toggle button; Space or Enter switches it without starting the timer. The narrower editor retains all three duration fields and the App, reset, start/pause, and end-session actions.

The App button stays highlighted while App view is shown, even when another window has focus. It dims when App view is hidden or minimized. Its accessible description reports that state; clicking it always shows App view or brings it forward.

Open START-HERE.html for setup and keyboard help. Normal launches use the installed profile. Developers may use --profile NAME for isolated test data; old preview samples remain local. APP-PAGE-AUDIT.md records the earlier interface parity review.
