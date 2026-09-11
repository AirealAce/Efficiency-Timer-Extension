# Reflection Timer 4.1.2

The accessible interface is now the primary desktop app. It uses the existing encrypted ReflectionTimerDesktop profile and retains the original Sheets connection, settings, drafts, schedules and Outbox. No personal connection settings belong in the application package.

The four views, themes, audio controls, window positions and Ctrl+Alt shortcuts are retained. App and reflection prompts can coexist with either Compact or Time-only. Semantic HTML supports screen-reader web navigation; quiet countdown updates do not interrupt reading.

Settings provides separate always-on-top switches for Compact, Time-only, and reflection prompts, all enabled by default. Prompts are excluded from Alt+Tab. A new session-end prompt auto-sends the previous open session-end draft (even blank), preserving any early-end status and reason. An unsent check-in is promoted into its own session's completion prompt under the same ID, preserving saved text and in-flight edits. Other sessions' check-ins stay separate; already-submitted entries are never copied or resent. Auto-send first commits to Outbox; failed local saves leave the old window editable and the new prompt pending. Network delivery uses the existing connection and retry rules.

The reason field is available while a check-in's session is still running/paused and for a genuine early ending. It disappears after natural completion, independently of any next session. Provisional reasons stay with the draft and are submitted only for an actual early ending. New reflection windows load their saved text and theme before becoming visible.

At natural completion, early ending, and scheduled handoff, the session-end audio's behavior controls overlapping low-time audio: Polite mixes normally, Assertive reduces the other audio to 25% and restores it afterward, and Disruptive stops it. Explicit pause/reset/new-start, disabling low-time audio, or Stop all app audio can still stop playback.

Compact has a two-line Auto/Start toggle that lights up when enabled. Screen readers announce it as the "Auto-start next session" toggle button; Space or Enter switches it without starting the timer. The narrower editor retains all three duration fields and the App, reset, start/pause, and end-session actions.

The App button stays highlighted while App view is shown, even when another window has focus. It dims when App view is hidden or minimized. Its accessible description reports that state; clicking it always shows App view or brings it forward.

Open START-HERE.html for setup and keyboard help. Normal launches use the installed profile. Developers may use --profile NAME for isolated test data; old preview samples remain local. APP-PAGE-AUDIT.md records the earlier interface parity review.

Reflection prompts use Save to keep a local draft and close; Save & send remains the submission action. Ctrl+Alt+comma focuses the first reflection box when neither box is focused, and acts as Save when either box is focused. From another window it brings a reflection forward without saving, reopens the latest pending draft, or opens a running/paused-session check-in. It never redirects to App. Failed draft saves leave the text editable and the prompt open.
