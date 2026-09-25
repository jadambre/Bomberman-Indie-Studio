# Bomberman Indie Studio verification record

Date: 25 September 2026. Engine: Unity 6, 6000.3.23f1.

This record distinguishes automated and editor checks from the more limited standalone startup check.

## Automated tests: 38 passed

Source of record: `QA/all-tests.json`. The Unity editor test run completed in 0.56 seconds with **38 passed, 0 failed, 0 skipped, and 0 inconclusive** results. This comprises 19 simulation test cases, nine save-repository cases and ten localization cases.

### Simulation coverage: 19 test cases

- Explosion rays stop at pillars and the first crate; destroyed crates expose their configured drop.
- Chained bombs detonate transitively, and an intervening bomb stops the original ray.
- Character movement interpolates between cells and cannot enter a solid tile.
- A bomb's placer can leave its cell, loses walk-out permission after leaving, and cannot re-enter it.
- Planting during movement uses the character's visible position and permits the ongoing escape movement.
- Owned bomb capacity becomes available after detonation.
- Bomb Up, Speed Up, Fire Up, and Wall Pass apply their effects and caps; Wall Pass still respects pillars and bombs.
- Friendly fire kills a human, while a surviving partner keeps the cooperative round active; losing the last human ends the round.
- A save made during movement preserves armed bombs, flames, walk-out permissions, and identical simulation state throughout 480 subsequent fixed ticks with the same commands.
- Invalid saved movement is rejected.
- Five stages increase the bot count from two to six. At each transition all humans, including revived partners, start with one bomb, fire range two, speed 3.4, and Wall Pass off; campaign score is retained. Four regression cases cover solo/co-op with and without save restoration before advancing. Restoring the same arena retains its upgrades. The completed fifth stage does not advance again.
- Running out of time loses the stage.
- Procedural arenas provide safe initial floor space, all four hidden upgrade kinds, and connected space after destructible crates are removed. This check covers 40 seeds across all five stages, or 200 arena generations.
- Matching seeds recreate the same state, while the checked different seeds vary the layout.
- A bot plants a bomb to clear a crate and survives its explosion.
- A bot refuses to plant a suicidal bomb in a sealed corridor.

### Persistence coverage: nine test cases

- Writing and reading preserve the live session and its independent retry checkpoint, including an active fuse, intermediate movement, upgrades, a dead partner, flames, pickups, score, and timers. Completed writes leave no temporary file.
- A corrupt primary file recovers the previous complete save and checkpoint from the backup.
- A missing save returns no campaign without an error notice or creating a file.
- An invalid primary file and invalid backup return no campaign with a helpful notice.
- Four separate cases reject a character outside the arena, duplicate actor identifiers, invalid speed, and invalid movement progress.
- A rejected write leaves the previous valid campaign unchanged and readable.

### Localization coverage: ten test cases

- Switching English to French and back updates menus and notices.
- Both stored language choices survive a temporary in-memory change and reload from PlayerPrefs.
- Missing or invalid preferences fall back to English.
- All five stage names, subtitles and briefings translate and switch back.
- Decimal formatting follows the selected language; missing translations and empty text remain safe.
- Language changes leave the serialized live co-op campaign unchanged.

## Bilingual interface integration: passed

Source: `QA/localization-integration.txt`; helper: `Assets/Editor/LocalizationQA.cs`.

Actual Unity UI button callbacks switch language in the main menu and pause menu, preserve keyboard focus and the settings page, persist/reload the preference, and leave an active bomb, upgrades and campaign state unchanged. Existing notifications and save summaries change language immediately. Tests use a separate QA save directory and restore the user's language preference.

The layout audit reports zero potentially clipped labels across 36 English/French screen states, including settings, controls, overwrite confirmation, all five HUDs and pause screens, countdown, defeat, stage clear and final victory. French settings, controls and gameplay were additionally captured and inspected at 16:9 / 4:3. These are editor checks, not physical-keyboard or standalone interaction tests.

## Editor integration smoke check: passed

Source of record: `QA/integration-smoke.txt`; implementation: `Assets/Editor/QATools.cs`.

The editor smoke check exercised cooperative campaign creation, pause, saving and loading an exact active session with a bomb and upgrades, restoring the retry checkpoint, constructing all five stage views with a valid camera, stage-clear and next-stage transitions, the final victory screen, suppression of Continue for a completed campaign, defeat and retry, and the presence of interactive main-menu buttons.

The stage-clear, defeat, and victory branches were exercised through editor test hooks that set the outcome. This verifies those application transitions; it is not a naturally played five-stage campaign. The interactive-menu assertion checks that buttons exist, rather than proving every mouse and keyboard interaction.

## Procedural layout and AI sample

Source of record: `QA/balance-notes.md`.

In addition to the 200 map generations checked above, a C# simulation harness using the same source and Unity math assemblies ran eight seeds for each stage for 90 simulated seconds per seed. The human remained stationary with an extended shield. Average surviving bots were 1.875/2, 2.875/3, 3.750/4, 4.000/5, and 5.125/6 across the five stages. Average bombs placed ranged from 38 to 54, and average crates destroyed ranged from 27 to 35.

This sample demonstrates active route clearing and escape behavior under those conditions. It does not establish human win rates, cooperative difficulty, or a final balance rating. Full measurements and the relevant gameplay rules are recorded in `QA/balance-notes.md`.

## Editor captures

The repository includes these selected captures from editor verification:

- [Main menu](Screenshots/renamed-menu.png) with the final game title.
- [French settings](Screenshots/settings-fr.png) with the simplified header.
- [French gameplay at 4:3](Screenshots/gameplay-fr-4x3.png).
- Arena views for [stage 2](Screenshots/stage-2.png), [stage 3](Screenshots/stage-3.png), [stage 4](Screenshots/stage-4.png), and [stage 5](Screenshots/stage-5.png).

Visual inspections cover the displayed views and framing in the captured states. Static captures cannot validate every animation, screen transition, resolution, or audio interaction. Temporary captures under `Assets/QA` are not versioned.

## Validation limits

No extended play session by human players has been completed. The recorded evidence therefore does not certify long-session enjoyment, the full cooperative difficulty curve, physical keyboard rollover during two-player input, or exhaustive behavior on different hardware. The simulation sample uses an invulnerable idle human and cannot replace human gameplay feedback. The save tests verify file persistence and restoration in the editor, rather than a completed close-and-reopen cycle of the Windows executable.

The pause-menu F5 handler was corrected after read-only review. A separate input-level validation of that correction is not claimed by the test reports above.

## Windows build and standalone validation

- Final build: **Succeeded**, 2026-09-25 01:21:31 UTC, Unity 6000.3.23f1, Windows x64, Mono, release configuration. Build size: 108,814,368 bytes; zero errors. Includes the Bomberman Indie Studio name, stage-scoped power-ups, English/French language option and simplified settings header.
- Executable: `Builds/Windows/BombermanIndieStudio.exe`. Builds are generated locally and are not included in a clone. Keep the entire Windows directory together. `Play Bomberman Indie Studio.cmd` launches it from the project root after building; the previous launcher forwards to it.
- One expected build warning: Pipeline has no runtime configuration and is disabled in Player builds. Editor automation does not ship as a running game service.
- The earlier 2026-09-24 23:39:15 UTC executable was launched with a 1600 × 900 window and a dedicated log. Engine, assemblies, graphics, physics and input initialized; the process remained responsive during startup checks. The test process was then closed. This startup check was not repeated for the subsequent power-up and language updates.
- The local standalone log contained no managed exceptions or game errors. It included the graphics-driver diagnostic `d3d12: failed to query info queue interface (0x80004002)`; Direct3D 12 initialization subsequently completed. Machine-specific logs are not versioned.
- Standalone visual inspection, physical cooperative keyboard input, paused F5, and a standalone save/close/reopen cycle have **not** been exercised. Their corresponding gameplay and persistence paths were checked through the editor integration and unit tests described above.

The latest change adds immediate English/French switching in Settings, persisted independently of campaign saves. All 38 tests pass and the Windows release was rebuilt successfully. A final UI correction hides temporary notices behind settings/help/confirmation dialogs; a live check and a fresh French settings capture verified that a save notice no longer covers the title. Existing saves remain compatible, including stage-scoped power-up behavior.

Follow-up: removed both decorative settings-header phrases in English and French and reduced the panel height to close the vacated space. `QA/settings-copy-check.txt` records a live check in both languages with neither phrase present and zero potentially clipped labels. The updated French settings screenshot was inspected and the Windows build succeeded. The gameplay test suite was not repeated for this text/layout-only edit.

Rename verification: the visible menu and both victory translations use Bomberman Indie Studio. Unity product settings and the built `app.info` agree. A local check confirmed the original Windows campaign path still loaded successfully, with the existing French preference and volume settings retained. Its machine-specific output is not versioned. The menu was visually inspected and its layout audit found zero clipped labels. Source namespaces and the stable Windows save folder retain their internal identity. Both launchers now target the renamed executable; older generated build files remain unreferenced.
