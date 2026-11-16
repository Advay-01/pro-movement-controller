# Movement Controller v1.8 — testing prototype

## Faster escape after dialogue

The old four-second escape retry timer could ignore the first request during a battle introduction, then wait several seconds after the dialogue ended. v1.8 watches the actual battle controls: red means busy; gray or the blue hover highlight means ready. After two matching, known-species readings confirm a non-target, Run follows the ready transition without that fixed wait.

The verified opponent heading may be reused within the same continuously observed battle only when every pixel in its OCR-bounded caption is identical. A changed heading requires fresh recognition. New battles, changed window geometry and focus resets clear this evidence. Exact targets and names resembling a target stop the controller before any escape action.

If a click is ignored while controls remain ready, requests are spaced by at least 200 ms. An observed busy transition acknowledges the request and suppresses retries until controls return or the world appears. Three acknowledged failed escapes, 40 unacknowledged requests, or 15 seconds of unresolved requests stop further input when controls are ready. Unreadable species never authorize fleeing. Recognition that takes too long stops the run.

Walking uses a pixel-based battle/world watcher and performs no periodic OCR. Battle detection releases movement before reading the caption. After escape, a clear world HUD must remain stable for at least 100 ms across multiple probes before walking resumes. Blank, obscured and unrecognized scenes keep input paused.

## Measurements

The final build completed 12 live escapes across five species. Completed Run clicks measured **82–102 ms** after ready controls were observed (95 ms average), with 11 exact verified-caption reuses. World movement resumed in **101–199 ms**, with **zero world OCR calls**. F8 stopped the test; the last Arbok encounter was left open. No Mimikyu appeared during this final session; target preservation passed the local action-gate checks.

See **performance-test-results.json** for the final-build live session, **visual-stats.json** for its watcher counters, and **ui-test-results.txt** for the verification summary. Ready-to-action timing is measured from the first observed ready controls to completed input delivery; it includes the visible click's 60 ms hold. The game can take several seconds to enable its controls during introductory dialogue, and still controls its escape animation. Neither interval can be removed by this controller.

The local suite has 44 passing check groups plus Windows OCR fixtures. It covers target protection, changed identity, exact heading signatures, busy/ready/hover controls, retry bounds, movement release, focus recovery, emergency stop and recognition scheduling. These checks complement the live test; other resolutions and UI layouts may pause the controller.

## Using it

1. Open PRO and log in yourself. Keep the game restored, visible and focused.
2. Open **MovementController.exe**, select the game, enter protected species, and enable keyboard input and encounter handling.
3. Press **F8** in PRO to start or stop. Clicking Start gives a three-second countdown.
4. **F10** emergency-stops and latches input off. Use Reset emergency stop before starting again.
5. **Until I stop it** enables continuous operation; uncheck it for a timed test.

**Use the recognised Run button immediately** is enabled by default. The button position needs a Run/Items pair or a consistent Fight/Pokemon/Items grid, followed by current battle geometry and ready-control checks. Input also checks the selected foreground process, confirmed non-target identity and released manual modifiers. The pointer moves visibly; mouse release is attempted in a finally block. If no confident position is available, the ordinary **Right Shift** shortcut is the bounded fallback. Disabling this option selects keyboard Run.

**Check encounter (no keys)** is a read-only diagnostic. **Show last screen reading** displays its latest OCR text. Normal walking does not use that full-screen diagnostic.

Optional focus return takes focus from other apps and can be refused by Windows. It releases owned input before bounded activation attempts. It does not support simultaneous typing elsewhere or minimized PRO operation. A closed/replaced/minimized target stops the run.

`MovementController.exe --prepare-pro Mimikyu` selects the single PROClient window and prepares continuous target-preserving mode, then waits for F8. Normal launches default to local practice. Reopening an existing controller stops its active run and brings it forward.

## Authorized scope and files

Use live input within explicit PRO staff-approved accounts, environments, actions and time periods. PRO's published rule 5 prohibits unauthorized automated software, including bots and macros. Selecting a window does not supply staff authorization. [Official rules](https://pokemonrevolution.net/forum/topic/108815-overall-game-rules/) checked November 16, 2026. This prototype uses ordinary visible input and local screenshots; it does not modify the game or bypass anti-cheat.

Keep **pokemon-names.txt** beside the executable. Runtime requirements are Windows 10/11 x64, .NET Framework 4.x and an English-capable Windows OCR language. Screenshots stay in memory and are not uploaded or saved. The bounded local activity log contains state/species/timing information rather than screenshots or full chat text. It is excluded from the distribution ZIP.

**build.ps1** uses the installed .NET Framework compiler and Windows SDK metadata. `--self-test <absolute-path>` and `--ocr-test <absolute-path>` run local checks without game input. [Windows OCR reference](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrengine).
