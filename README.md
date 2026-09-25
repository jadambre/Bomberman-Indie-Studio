# Bomberman Indie Studio

A five-arena bomb-battler for **Unity 6.3 LTS (6000.3.23f1)**. One or two players share a keyboard and cooperate against bots in procedural 3D arenas with grid-based 2D gameplay. The interface supports English and French.

## Play on Windows — no Unity or Git required

For **64-bit Windows**, download the ready-to-play game from the [latest release](https://github.com/jadambre/Bomberman-Indie-Studio/releases/latest).

1. Under **Assets**, download **[Bomberman-Indie-Studio-v1.0.0-Windows-x64.zip](https://github.com/jadambre/Bomberman-Indie-Studio/releases/download/v1.0.0/Bomberman-Indie-Studio-v1.0.0-Windows-x64.zip)**. The **Source code** archives contain the development project, not the playable game.
2. Right-click the downloaded ZIP and choose **Extract All**.
3. Open the extracted **`Bomberman-Indie-Studio-Windows`** folder and double-click **`BombermanIndieStudio.exe`**.

Keep all the extracted files and folders together. Launch the game from the extracted folder, not from inside the ZIP archive.

## Play on macOS — Apple Silicon

For **Macs with an Apple Silicon chip (M1 or later), running macOS 12 or later**, download **[Bomberman-Indie-Studio-v1.0.0-macOS-AppleSilicon.zip](https://github.com/jadambre/Bomberman-Indie-Studio/releases/download/v1.0.0/Bomberman-Indie-Studio-v1.0.0-macOS-AppleSilicon.zip)** from the same release. Unity, Git and Rosetta are not required. This build does not support Intel Macs.

1. Double-click the ZIP in Finder to extract it.
2. Open the extracted **`Bomberman-Indie-Studio-macOS`** folder.
3. Drag **`Bomberman Indie Studio.app`** into **Applications**, then open it.

**Mac testing status:** the ARM64 build and archive checks passed on Windows; launching, graphics, audio and gameplay still need verification on a real Apple Silicon Mac. The application is **not Developer ID signed or notarized by Apple**, so macOS may block its first launch. If you trust this project's download, try opening it once, then use **System Settings → Privacy & Security → Open Anyway**, when available. See [Apple's instructions](https://support.apple.com/en-us/102445). Do not disable Gatekeeper globally.

Keep the `.app` bundle intact. The ZIP includes an English launch guide, and the release includes a separate SHA-256 checksum. Download the platform-specific ZIP, not the **Source code** archives.

## Developer setup — clone and open in Unity

Install Git, Git LFS and Unity Hub, then run:

```powershell
git lfs install
git clone https://github.com/jadambre/Bomberman-Indie-Studio.git
cd Bomberman-Indie-Studio
git lfs pull
```

The selected audio assets use Git LFS. Complete the LFS download before opening the project so Unity imports the actual audio files.

1. In Unity Hub, install **Unity 6000.3.23f1** and add the cloned project folder.
2. Open the project and let Unity restore packages and import assets.
3. Open `Assets/Scenes/BombermanIndieStudio.unity` and press **Play**.

The scene, assemblies and code use `BombermanIndieStudio`; the displayed game title is **Bomberman Indie Studio**.

## Developer build — Windows

In the Unity editor, choose **Bomberman Indie Studio → Build Windows release**. The build targets Windows x64 with Mono and writes `Builds/Windows/BombermanIndieStudio.exe`. If Windows build support is unavailable, add the appropriate module to this editor installation through Unity Hub.

After building, double-click **`Play Bomberman Indie Studio.cmd`** or run the executable directly. Keep the entire `Builds/Windows` folder together, including the `_Data` folder and Unity DLLs.

Generated builds are excluded from Git: **a fresh clone does not include an executable**.

## Developer build — macOS Apple Silicon

Install **Mac Build Support (Mono)** for Unity 6000.3.23f1, then choose **Bomberman Indie Studio → Build macOS Apple Silicon release**. This creates `Builds/macOS/Bomberman Indie Studio.app` with native ARM64 binaries and the bundled Mono runtime. The build can be produced on Windows or macOS; running it requires a Mac.

To package the application on either system, use Python 3.10 or later:

```sh
python Tools/package_macos.py --app "Builds/macOS/Bomberman Indie Studio.app" --readme Docs/macOS-README.md --output Builds/Releases/Bomberman-Indie-Studio-v1.0.0-macOS-AppleSilicon.zip
```

The packaging script preserves Unix execution permissions, verifies the bundle and archive, and writes a `.sha256` file. Upload both files as assets of the GitHub release. Test the downloaded archive on an Apple Silicon Mac before marking Mac validation complete. A distribution signed with your Apple Developer ID and notarized by Apple requires an additional signing/notarization workflow before packaging; this build does not include that workflow.

## Controls

| Action | Player 1 | Player 2 (AZERTY) |
|---|---|---|
| Move | Arrow keys | Z Q S D |
| Place bomb | Space | F |
| Pause / back | Escape | Escape |
| Quick save | F5 | F5 |

Menus support the mouse, arrow keys / Tab, and Enter. Player 2 uses the physical WASD key positions, corresponding to ZQSD on AZERTY. Player 1's directional keys and Space work independently of keyboard layout.

On Mac keyboards where the top row controls system features, use **Fn + F5** for quick save, or choose **Pause → Save & Menu**.

## Language

Open **Settings → Language** from the main menu or pause menu to switch between English and French. The interface changes immediately. Your choice is saved for the next launch without changing the campaign. English is the default until another language is selected.

## Rules

- Eliminate every bot before the timer ends. Either surviving player can win for the team.
- Every explosion is dangerous, including your own and your teammate's. Spawn shields provide a brief safe start.
- Blasts extend in four directions. Solid pillars block them; a crate absorbs the blast and breaks. Bombs caught in a blast trigger a chain reaction.
- Walk away from a newly placed bomb immediately. Once you leave it, the bomb blocks your path.
- Broken crates can reveal **Bomb Up**, **Speed Up**, **Fire Up**, and **Wall Pass**. Walk over a pickup to apply it automatically. Wall Pass allows movement through crates, but does not protect against flames or bypass pillars and bombs.
- All four upgrades last for the **current arena only** and reset at the start of the next stage. Fallen teammates return at the next stage. Retry restores the starting checkpoint of the current arena.
- Complete Mossbound, Sunstone, Frostline, Afterglow, and The Core to finish the campaign. New campaigns generate new layouts.

## Saving

The game saves at the start of each arena. Use **F5** or **Pause → Save & Menu** to preserve the current state, including the arena's upgrades, destroyed blocks, bots, moving actors, bomb timers and active fire. Closing an active game also attempts a save. **Continue campaign** resumes the same arena with its saved upgrades.

One campaign slot is shared between solo and co-op; the menu confirms before replacing an existing campaign. Saves are versioned JSON with an atomic write and previous-save backup.

On Windows, the save location is:

```text
%USERPROFILE%\AppData\LocalLow\EmberGrid\EMBERGRID\campaign-v1.json
```

This legacy folder is deliberately preserved so existing campaigns remain available. The Unity company identifier also remains unchanged to preserve existing preferences. These compatibility settings are separate from the current scene, assembly and namespace names. Language, volume and screen-shake preferences are stored separately through Unity PlayerPrefs.

## Development and verification

The project uses the built-in 3D render pipeline, UGUI, Unity Test Framework and Unity Pipeline. Editor automation uses Unity CLI directly, without MCP; the editor's normal Play, test and build workflows do not require installing Unity CLI.

To run the tests, open **Window → General → Test Runner**, select **EditMode**, then **Run All**. The recorded verification run passed **38 tests**, covering simulation, save handling and localization. This is historical validation, not a claim that tests have run on your machine. See [QA/verification.md](QA/verification.md) for recorded checks and human playtesting limits.

See [DEVELOPMENT.md](DEVELOPMENT.md) for the architecture, build workflow and contribution checks.

## Assets and repository contents

Arena geometry, characters, materials, animation and UI are generated in code. The 18 selected audio clips are included in `Assets/Resources/Audio` through Git LFS; footsteps and warning ticks are generated in code. The original local `Sounds` collection is not versioned and is not needed to run or build the game.

Unity caches, local settings, generated builds, temporary QA outputs and campaign saves are excluded from Git. Unity asset metadata, project settings and package manifests are versioned so a clone can recreate the project.
