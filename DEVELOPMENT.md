# Development guide

## Environment

Use **Unity 6.3 LTS, 6000.3.23f1**, as recorded in `ProjectSettings/ProjectVersion.txt`. Install and open the editor through Unity Hub. The project uses the built-in render pipeline, the legacy input system with physical keys enabled, and UGUI.

Run `git lfs install` and `git lfs pull` when setting up a clone. Audio files under `Assets/Resources/Audio` are stored through Git LFS. Unity restores the dependencies declared in `Packages/manifest.json` and `Packages/packages-lock.json` during project import.

Open `Assets/Scenes/EmberGrid.unity` and press Play. This minimal scene starts `GameApp`; the arena and interface are created at runtime. The namespace, assembly names and scene retain `EmberGrid` as their internal identity. The public game title is Bomberman Indie Studio.

## Architecture

| Location | Responsibility |
|---|---|
| `Assets/Scripts/Core/GameTypes.cs` | Serializable campaign state, commands, events and shared gameplay definitions. |
| `Assets/Scripts/Core/Simulation.cs` | Procedural arenas, movement, bombs, explosions, pickups, bots and stage outcomes. |
| `Assets/Scripts/App/GameApp.cs` | Input, fixed-step simulation, screen transitions, checkpoints and coordination of presentation, UI and audio. |
| `Assets/Scripts/App/SaveRepository.cs` | Versioned JSON saves, validation, cloning, atomic writes and backup recovery. |
| `Assets/Scripts/App/GameAudio.cs` | Music transitions, pooled interaction sounds and generated footsteps/ticks. |
| `Assets/Scripts/App/GameText.cs` | Language selection, translated text and culture-aware formatting. |
| `Assets/Scripts/UI/GameText.Interface.cs` | Additional interface translations. |
| `Assets/Scripts/Presentation/ArenaView.cs` | Procedural meshes and materials, camera framing, character animation and visual effects. |
| `Assets/Scripts/UI/GameUI.cs` | Menus, HUD, settings, controls, keyboard navigation and screen layouts. |
| `Assets/Editor` | Scene configuration, Windows build commands and editor QA helpers. |
| `Assets/Tests/EditMode` | Simulation, save-repository and localization tests. |

`EmberGrid.Runtime` contains the game code. `EmberGrid.Editor` and `EmberGrid.EditMode` are editor-only assemblies; the latter contains the tests.

### Simulation and application flow

`GameApp` collects player input and advances `Simulation` at a fixed 1/60-second step while play is active. The simulation owns `SessionState` and emits gameplay events for presentation and audio. It does not access scene objects, keyboard input or audio playback. Keep new gameplay rules in this layer so they can be exercised without rendering a scene.

`ArenaView` renders the state and consumes events. `GameUI` presents the current application screen and calls `GameApp` actions. Pausing and language changes must not advance or reset the simulation.

The campaign has five stages on a 15 × 11 grid. One or two human players cooperate against bots, with friendly fire enabled. Winning with at least one surviving human allows progression; losing every human or running out of time ends the round. On the next stage, fallen partners return and all four upgrades reset to their base values. The campaign score persists.

### Persistence and localization

A save contains both the live session and the arena's starting checkpoint. Continuing restores the live session, including movement, active bombs, flames and upgrades. Retrying restores the checkpoint. Changes to state serialization must preserve or deliberately migrate the versioned save format and retain validation of loaded data.

Windows saves continue to use `%USERPROFILE%\AppData\LocalLow\EmberGrid\EMBERGRID\campaign-v1.json`. Do not change this internal path as part of a display-name change. Language, sound levels and screen shake are separate PlayerPrefs settings.

Use `GameText.T` and `GameText.Format` for visible strings. Add corresponding French translations when introducing English source text. Use the selected language's culture for displayed numbers. Language switching must refresh existing menus, notices and save summaries without altering campaign data.

## Scene and build workflow

The checked-in launch scene is ready to use. To regenerate it and apply the project's input, rendering and audio-import settings, choose **Bomberman Indie Studio → Configure project and create launch scene**. This command creates a new launch scene; save any unrelated scene work before using it.

Choose **Bomberman Indie Studio → Build Windows release** to create a Windows x64 Mono release in `Builds/Windows`. The builder preserves shaders used by runtime-created materials and writes a local summary to `QA/build-report.txt`.

Run `Play Bomberman Indie Studio.cmd` after a successful build. When distributing a build, keep its executable, `_Data` directory and Unity DLLs together. Build output is not committed to the source repository.

The official Unity Pipeline package supports optional Unity CLI editor automation without MCP. This is additional tooling; ordinary editor use, Test Runner and the build menu remain available without the CLI.

## Verification

Open **Window → General → Test Runner → EditMode → Run All**. The recorded baseline has **38 passing tests**: 19 simulation cases, nine persistence cases and ten localization cases. The historical evidence and its limits are documented in [QA/verification.md](QA/verification.md).

For gameplay changes, exercise the relevant simulation tests and check solo/co-op behavior, friendly fire, chain reactions, stage progression and upgrade resets. For persistence changes, verify that the live session and retry checkpoint remain independent and that corrupt saves recover safely. For localization and UI changes, inspect both languages at 16:9 and 4:3 and verify mouse and keyboard navigation.

Editor helpers in `QATools.cs` and `LocalizationQA.cs` exercise application transitions and interface callbacks using separate QA save directories. These helpers can change the active editor session; run them during a dedicated QA session. Their generated logs, saves and screenshots are local outputs.

Automated tests and editor captures do not replace human playtesting. Before a release, play cooperatively with a physical keyboard, inspect sound and animation in the standalone build, and verify a save/close/reopen cycle. Record which checks were actually completed.

## Repository practices

- Commit Unity assets with their matching `.meta` files. The project uses visible metadata and text serialization; preserve GUIDs when moving assets.
- Keep `Packages/manifest.json`, `Packages/packages-lock.json` and `ProjectSettings` under version control.
- Keep binary audio under Git LFS. The selected clips are already in `Assets/Resources/Audio`; the unversioned original `Sounds` collection is not a runtime or build dependency.
- Do not commit `Library`, `Temp`, generated builds, local editor preferences, campaign saves or machine-specific QA logs.
- Keep gameplay changes, presentation changes and tooling/documentation changes in focused commits. Include relevant validation in the commit or pull-request description.
