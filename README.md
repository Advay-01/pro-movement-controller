# Movement Controller v2.1.1 — testing prototype

The v2.1.1 layout patch recognizes the smaller, translated battle panel observed in the live client. Its command, own-Pokémon and battle-log regions follow that panel rather than fixed screen percentages. This also prevents the visible party HUD behind a battle from being treated as walking permission. Unknown panel/menu evidence stays paused. A trainer caption cannot authorize wild-catching controls or validate an old capture message. The read-only Seedot inspection was interrupted by manual input and a later trainer battle; move selection, party switching and live capture remain unverified.

## Optional auto-catch settings

Click **Catch settings…** in each profile. Choose the catcher's displayed name and party slot, ordered preparation/False Swipe move slots and use counts, ball, maximum balls per target, optional expected leading Pokémon after capture, and **Resume hunting after a confirmed catch**. Save, then enable **Auto-catch exact targets**. It is off on every app launch. Each profile keeps its own local recipe. Changing protected targets requires reviewing the settings again. Draft recipes can be saved; starting requires the tested-sequence checkbox.

Before starting, manually leave PRO's backpack on its **Balls** tab with the selected ball visible. The controller reads the label and stock; it does not scroll the bag or buy balls. A ball click attempts to use it immediately. Configure your actual catcher and move positions; the app does not infer a team setup. For Mimikyu, use your tested Ghost/Disguise preparation sequence. False Swipe uses are configured and bounded; the app does **not** measure exactly 1 HP. It does not automatically reapply status moves after ball failures or replenish PP.

Two fresh exact target readings enter a separate catching workflow. Movement, Right Shift and Run clicks are blocked throughout it. Every action requires a recognized target and matching menu/control, positive PP or stock where applicable, then a second screen check. The selected catcher must match its displayed name and party slot. A busy-to-ready transition or scoped action message acknowledges each submitted move. Ball failures retry within the configured limit. Near-target and unreadable names remain protected.

Only explicit capture text for the protected species followed by a stable clear world can resume hunting. The optional leading-Pokémon field verifies the party; it does not rearrange it. Unexpected battle exit, fainting, immunity/failure messages, unreadable controls/PP/stock, exhausted limits, slow recognition and focus changes stop for manual attention. Menu navigation, action waits and overall capture duration are bounded. **F8** and **F10** cancel pending input.

Scheduled dual-window handoffs invalidate stale readings and preserve completed progress. If a handoff interrupts an action awaiting its final screen check, that profile stops. Catch recognition may extend a foreground turn by up to 2.5 seconds; other recognition keeps its earlier 0.8-second allowance. Busy catch animations yield to the other hunt.

**Check catch screen (no input)** reports the recognized view, species, catcher and controls through **Show last screen reading**. Per-profile files `catch-settings.json`, `catch-settings-window-1.json` and `catch-settings-window-2.json` stay local and are excluded from Git and release ZIPs.

Validation: 67 local check groups plus Windows OCR fixtures cover the workflow, wrong species, move order/PP, stock/budgets, capture confirmation, persistence, lease/epoch cancellation, dual turn allowance and second-screen validation. The settings dialog and both profile modes were inspected, and the backpack was inspected without throwing a ball. **A live target capture has not been verified.** Use the read-only diagnostic and supervise the first configured capture. Other layouts or wording may stop for manual handling. Historical escape timings below describe earlier releases, not catching performance.

## Two-window hunting

Click **Two-window hunting mode** in the single controller, or launch `MovementController.exe --dual`. Select a different PRO window in each profile, enter its protected species, and enable input and encounter handling in each. Each profile has independent keys, timing, targets, Repel renewal, alerts and activity records.

**F8** starts/stops both profiles. **F10** emergency-stops both and latches input off; use the shared Reset button before starting again. The **Turn (sec)** field controls the foreground turn duration, from 0.40 to 5 seconds, with 0.80 seconds as the default. Mouse buttons or modifiers pause input; persistent manual input stops the shared run. Both PRO windows must remain restored. Switching can visibly raise them, and normal Windows focus restrictions can stop the run.

Windows input is shared, so each game gets a short foreground turn. All owned movement and Right Shift input is released before a focus request. A later stable foreground observation grants the next profile permission to send input. The profiles have no competing focus-recovery loops. Focus changes cancel pending recognition and invalidate cached identity evidence; returning to a battle requires fresh matching species reads. Escape and Repel retry limits are preserved across scheduled turns. Walking performs no periodic OCR.

While one game is waiting for battle dialogue or an escape animation, it yields its turn to the other hunt. The busy game is revisited for a brief pixel-based readiness check, with no repeated name OCR while its controls are still busy. Once ready, fresh name verification precedes Run. Movement holds can be shortened by a handoff; consecutive walking turns alternate their starting direction to keep interrupted pulses balanced. [Windows can refuse normal focus requests](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow); refusal stops the run.

With Auto-catch off, a target stops its profile and alerts you while the other hunt can continue. Press F8 to stop switching before catching or interacting manually. Auto-catch is configured independently in each profile as described above. Closed, changed or minimized clients stop their profile; failed focus acquisition stops the shared run.

The prepared local setup protects **Mimikyu** in window 1 and **Ralts** in window 2, with **Continue Repel enabled only for window 2**, a 0.30-second hold, a 0.05-second gap, and continuous operation. Process IDs in the window selectors identify the two client instances. `--prepare-dual Mimikyu Ralts <process1> <process2>` prepares that mapping and waits for F8; process IDs must be refreshed when clients restart.

The visual world check supports the observed three-to-six-member party HUD layouts. Insufficient or ambiguous HUD evidence keeps input paused. See **performance-test-results.json**, **ui-test-results.txt**, and **visual-stats-window-1.json / visual-stats-window-2.json** for v2.0 verification. Each managed profile has its own bounded activity log; runtime logs and screenshots are excluded from the release ZIP.

## Continue Repel

Enable **Continue Repel** while the controller is stopped. In PRO, enable its Repel prompt and use the first Repel yourself. When the **Repel Expired** dialog asks whether to use another Repel, Super Repel or Max Repel, the controller pauses walking, verifies the exact wording and Yes/No controls twice, then clicks **Yes**. It resumes walking after the dialog closes and the world is clear.

The checkbox is off by default and is independent of encounter handling. It renews the type offered by PRO; it does not open the bag, change types, buy items or start the first Repel. Keep stock available. With encounter handling off, battles pause movement for manual attention.

The cheap visual watcher triggers OCR only when matching dialog geometry appears. Regular walking still performs no OCR. Unrelated prompts, purchases, releases, unreadable text or missing buttons never authorize a renewal. Keep the pointer away from the wording so it can be read. Two ignored click requests or a persistent unrecognized prompt stop the run; F8 and F10 cancel pending recognition. The final pixel check covers the opaque prompt interior so animated scenery does not block a valid renewal.

## Faster escape after dialogue

The old four-second escape retry timer could ignore the first request during a battle introduction, then wait several seconds after the dialogue ended. v1.8 watches the actual battle controls: red means busy; gray or the blue hover highlight means ready. After two matching, known-species readings confirm a non-target, Run follows the ready transition without that fixed wait.

The verified opponent heading may be reused within the same continuously observed battle only when every pixel in its OCR-bounded caption is identical. A changed heading requires fresh recognition. New battles, changed window geometry and focus resets clear this evidence. Exact targets and names resembling a target never authorize escape. With Auto-catch off they stop the controller; with it on, only exact targets enter the configured workflow.

If a click is ignored while controls remain ready, requests are spaced by at least 200 ms. An observed busy transition acknowledges the request and suppresses retries until controls return or the world appears. Three acknowledged failed escapes, 40 unacknowledged requests, or 15 seconds of unresolved requests stop further input when controls are ready. Unreadable species never authorize fleeing. Recognition that takes too long stops the run.

Walking uses a pixel-based battle/world watcher and performs no periodic OCR. Battle detection releases movement before reading the caption. After escape, a clear world HUD must remain stable for at least 100 ms across multiple probes before walking resumes. Blank, obscured and unrecognized scenes keep input paused.

## Measurements

The previous v1.8 build completed 12 live escapes across five species. Completed Run clicks measured **82–102 ms** after ready controls were observed (95 ms average), with 11 exact verified-caption reuses. World movement resumed in **101–199 ms**, with **zero world OCR calls**. These are v1.8 encounter measurements; v1.9 adds Repel handling without changing its ready-control escape logic.

The v1.9 Repel release was live-tested through two repeat renewals. Its historical reports remain available under the v1.9 Git tag. The current reports cover coordinated v2.0 operation. Escape ready-to-action timing is measured from the first observed ready controls to completed input delivery; it includes the visible click's 60 ms hold. A game waiting for its next foreground turn can add latency. Dialogue and escape animations remain controlled by the game.

The local suite has 56 passing check groups plus Windows OCR fixtures and an obscured-prompt rejection check. It covers shared input ownership, release-before-switch, busy-dialogue yielding, balanced interrupted pulses, fair turns, denied focus, independent completion and global emergency stop alongside Repel cancellation/retry budgets, target protection, identity freshness, movement release and the three-member HUD. These checks complement the live test; other resolutions and UI layouts may pause the controller.

## Using it

1. Open PRO and log in yourself. Keep the game restored, visible and focused.
2. Open **MovementController.exe**, select the game, enter protected species, and enable keyboard input and encounter handling.
3. Press **F8** in PRO to start or stop. Clicking Start gives a three-second countdown.
4. **F10** emergency-stops and latches input off. Use Reset emergency stop before starting again.
5. **Until I stop it** enables continuous operation; uncheck it for a timed test.
6. For automatic Repel renewal, check **Continue Repel**, enable Repel prompts in PRO, and apply the first Repel manually.

**Use the recognised Run button immediately** is enabled by default. The button position needs a Run/Items pair or a consistent Fight/Pokemon/Items grid, followed by current battle geometry and ready-control checks. Input also checks the selected foreground process, confirmed non-target identity and released manual modifiers. The pointer moves visibly; mouse release is attempted in a finally block. If no confident position is available, the ordinary **Right Shift** shortcut is the bounded fallback. Disabling this option selects keyboard Run.

**Check encounter (no keys)** is a read-only diagnostic. **Show last screen reading** displays its latest OCR text. Normal walking does not use that full-screen diagnostic.

Optional focus return takes focus from other apps and can be refused by Windows. It releases owned input before bounded activation attempts. It does not support simultaneous typing elsewhere or minimized PRO operation. A closed/replaced/minimized target stops the run.

`MovementController.exe --prepare-pro Mimikyu` selects the single PROClient window and prepares continuous target-preserving mode, then waits for F8. Add `--continue-repel` to prepare renewal too. Normal launches default to local practice with renewal off. Reopening an existing controller stops its active run and brings it forward.

## Authorized scope and files

Use live input within explicit PRO staff-approved accounts, environments, actions and time periods. PRO's published rule 5 prohibits unauthorized automated software, including bots and macros. Selecting a window does not supply staff authorization. [Official rules](https://pokemonrevolution.net/forum/topic/108815-overall-game-rules/) checked November 16, 2026. This prototype uses ordinary visible input and local screenshots; it does not modify the game or bypass anti-cheat.

Keep **pokemon-names.txt** beside the executable. Runtime requirements are Windows 10/11 x64, .NET Framework 4.x and an English-capable Windows OCR language. Screenshots stay in memory and are not uploaded or saved. The bounded local activity log contains state/species/timing information rather than screenshots or full chat text. It is excluded from the distribution ZIP.

**build.ps1** uses the installed .NET Framework compiler and Windows SDK metadata. `--self-test <absolute-path>` and `--ocr-test <absolute-path>` run local checks without game input. `--repel-image-test <image-path> <report-path>` validates a supplied image; add `--expect-reject` for an unreadable or obscured fixture. [Windows OCR reference](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrengine).
