# Changes

## v2.1.2

Fixes submenu priority and party selection after inspecting Fight and Pokémon in a wild Seedot battle. Persistent main command buttons no longer hide an open move list. The icon-only party grid requires matching HUD names, the configured slot and a consistent visible icon count; missing labels and covering overlays block selection. Leading-Pokémon verification is restricted to the first party row, and caption padding keeps the first log line in the log crop.

67 local groups and expanded Windows OCR fixtures pass. No attack, switch or ball was submitted. The wild battle ended before live Items inspection finished; live move execution and capture are still unverified.

## v2.1.1

Fixes recognition of the inset battle panel seen in the current client. Battle controls, the active Pokémon caption and battle log now follow the detected panel bounds. Uncertain headers stay paused even when the world party HUD is visible behind the battle. Run/Items OCR maps panel coordinates back to the game client. Catch commands require the two-row grid, and trainer captions reject wild catching and stale catch text.

67 local groups and Windows OCR fixtures cover the layout fix, control mapping, scoped capture messages and trainer rejection. The live Seedot screen exposed the layout mismatch; inspection was interrupted by manual input and a later trainer battle. No move, switch or ball was submitted; live catching remains unverified.

## v2.1

Adds optional, per-profile **Catch settings** and **Auto-catch exact targets**. Choose a catcher/party slot, ordered preparation and False Swipe turns, ball/ball limit and whether to resume after confirmed capture. Recipes persist independently; auto-catch stays off at launch. A read-only catching diagnostic is included.

Catching blocks movement and Run, confirms the target and visible control twice before input, requires PP/stock evidence, and uses bounded navigation/action/overall limits. Capture text plus stable world evidence is required before resuming. Stop, emergency and focus/handoff changes invalidate pending actions. Unknown screens stop for manual handling. Ball stock has a narrow OCR retry for isolated counts; users must leave the bag on Balls with the selected ball visible.

Validation includes 66 local groups, Windows OCR fixtures and UI inspection. No live target capture has been verified; the first configured capture needs supervision. Previous release escape performance reports remain historical.

## v2.0

Adds one coordinated controller with two hunting profiles. Each keeps its own client, protected species, movement settings, Repel option and alerts. The shared switcher grants one foreground turn at a time, releases owned input before handing off focus, and cancels stale recognition on every handoff. Normal Windows focus restrictions remain active.

F8 starts/stops both; F10 emergency-stops both. A busy battle yields immediately so the other hunt can walk; later visits perform brief readiness checks until name verification and Run are possible. Interrupted walking turns alternate their starting direction. A completed target stops its profile while the other may continue. Retry limits survive scheduled turns, and the world detector now accepts the observed three-member party HUD. Local verification includes 56 check groups plus Windows OCR and unreadable-prompt fixtures.

## v1.9

Added the optional **Continue Repel** checkbox. When PRO displays its expired-Repel renewal dialog, walking pauses, the exact prompt and Yes/No buttons are verified twice, and Yes is clicked. Walking resumes after the prompt closes and the world is clear. Repel, Super Repel and Max Repel are supported; the first item must be used manually with PRO's Repel prompts enabled.

Regular walking uses a visual trigger rather than periodic OCR. Unknown text and unrelated confirmation dialogs do not authorize item use. Pending renewal is cancelled by stop, emergency stop, focus loss or changed geometry. Retry attempts are bounded. Local validation now includes 50 check groups, positive renewal OCR and an obscured-prompt rejection fixture.

## v1.8

Run now follows the battle controls becoming ready instead of waiting for a fixed four-second retry timer. A verified non-target caption can be reused within the same battle only when its pixels remain exactly identical. Changed identities still require fresh recognition. Gray controls and the blue hover state count as ready; red busy controls block Run.

Ignored requests retry at bounded intervals. Target and near-target protection, F8 start/stop, F10 emergency stop, visible foreground input, configurable movement timing and continuous operation remain available.

Validation: 44 local check groups and Windows OCR fixtures passed. The tested Windows build completed 12 live escapes across five species, with clicks delivered 82–102 ms after readiness was observed. World movement resumed in 101–199 ms, with zero world OCR calls. These timings exclude the game's dialogue and escape animation. No target Pokémon appeared during this final session; target protection passed local action-gate checks.
