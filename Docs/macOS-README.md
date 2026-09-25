# Bomberman Indie Studio — macOS Apple Silicon

Version 1.0.0. For Apple Silicon Macs (M1 or later) running macOS 12 or later.
Intel Macs are not supported by this download. Unity, Git and Rosetta are not required.

## Install and play

1. Download `Bomberman-Indie-Studio-v1.0.0-macOS-AppleSilicon.zip` from the project's [GitHub release](https://github.com/jadambre/Bomberman-Indie-Studio/releases/tag/v1.0.0).
2. Double-click the ZIP in Finder to extract it.
3. Open `Bomberman-Indie-Studio-macOS`.
4. Drag `Bomberman Indie Studio.app` into Applications, then open it.

Keep the application bundle intact. The `.app` already contains the game data and runtime.

## First-launch security notice

This application has not been signed with an Apple Developer ID certificate or notarized by Apple. macOS may block its first launch.

If you trust the download from this project's GitHub release, try opening the app once, then go to **System Settings → Privacy & Security → Open Anyway**, when available, and confirm **Open**. Follow [Apple's guidance](https://support.apple.com/en-us/102445). Do not disable Gatekeeper globally. If macOS reports that the app is damaged, verify the download and report the exact message instead of changing system-wide security settings.

## Controls

| Action | Player 1 | Player 2 |
|---|---|---|
| Move | Arrow keys | ZQSD on AZERTY (physical WASD positions) |
| Place a bomb | Space | F |
| Pause / back | Escape | Escape |
| Quick save | F5 | F5 |

On Mac keyboards with system controls on the top row, use **Fn + F5**, or **Pause → Save & Menu**.
Menus also support mouse, Tab / arrow keys and Enter.

Choose one or two players from the main menu. Both players cooperate against bots, and every explosion is dangerous. Walk over power-ups to collect them; upgrades reset when the next arena starts. Complete all five arenas to finish the campaign.

Use **Settings → Language** to switch between English and French. English is the default. Start a campaign with **Start new adventure** and resume a saved campaign with **Continue**. Use **Pause → Save & Menu** to save before quitting.

## Validation status

The release was compiled on Windows using Unity 6000.3.23f1 with the Mac Mono module and ARM64 target. Build, architecture, archive integrity and Unix execution permissions were checked. The application has **not yet been launched or playtested on an Apple Silicon Mac**. Performance, graphics, audio, keyboard input and save/relaunch behavior need to be checked on that platform.

The `.zip.sha256` file on GitHub contains the archive checksum. If both downloads are in the same folder, Terminal can check it with:

```sh
shasum -a 256 -c Bomberman-Indie-Studio-v1.0.0-macOS-AppleSilicon.zip.sha256
```

For troubleshooting, include your macOS version, Mac model and the exact error. The Unity player log is at `~/Library/Logs/EmberGrid/Bomberman Indie Studio/Player.log`; `EmberGrid` is the preserved internal company identifier.

Source code and development instructions: [Bomberman Indie Studio on GitHub](https://github.com/jadambre/Bomberman-Indie-Studio).
