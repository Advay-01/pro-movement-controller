# Changes

## v1.9

Added the optional **Continue Repel** checkbox. When PRO displays its expired-Repel renewal dialog, walking pauses, the exact prompt and Yes/No buttons are verified twice, and Yes is clicked. Walking resumes after the prompt closes and the world is clear. Repel, Super Repel and Max Repel are supported; the first item must be used manually with PRO's Repel prompts enabled.

Regular walking uses a visual trigger rather than periodic OCR. Unknown text and unrelated confirmation dialogs do not authorize item use. Pending renewal is cancelled by stop, emergency stop, focus loss or changed geometry. Retry attempts are bounded. Local validation now includes 50 check groups, positive renewal OCR and an obscured-prompt rejection fixture.

## v1.8

Run now follows the battle controls becoming ready instead of waiting for a fixed four-second retry timer. A verified non-target caption can be reused within the same battle only when its pixels remain exactly identical. Changed identities still require fresh recognition. Gray controls and the blue hover state count as ready; red busy controls block Run.

Ignored requests retry at bounded intervals. Target and near-target protection, F8 start/stop, F10 emergency stop, visible foreground input, configurable movement timing and continuous operation remain available.

Validation: 44 local check groups and Windows OCR fixtures passed. The tested Windows build completed 12 live escapes across five species, with clicks delivered 82–102 ms after readiness was observed. World movement resumed in 101–199 ms, with zero world OCR calls. These timings exclude the game's dialogue and escape animation. No target Pokémon appeared during this final session; target protection passed local action-gate checks.
