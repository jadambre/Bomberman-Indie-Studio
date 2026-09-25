# Windows release v1.0.0 verification

Date: 25 September 2026.

- Game source: commit `be1b115b633a6b8e99132a56de981ab540522495`; later release documentation does not change the game code.
- Build: Unity 6000.3.23f1, Windows x64, Mono, release configuration. The build succeeded with zero errors; all 38 EditMode tests and the editor integration smoke check passed for this game source.
- Archive: `Bomberman-Indie-Studio-v1.0.0-Windows-x64.zip`, 52,280,895 bytes, 148 entries, with a single `Bomberman-Indie-Studio-Windows` root folder.
- SHA-256: `72b653f626ad22241231ce595ec275f97bfb0592e0c25fdd3c76bbe7b2336ab5`.
- The package contains the executable, matching `_Data` directory, Unity DLLs, Mono runtime, D3D12 support, crash handler and a bilingual launch guide. Obsolete players, project caches, local saves and logs are not included.
- The ZIP was extracted into a separate directory. Every extracted file matched the staged payload by SHA-256.
- The extracted executable was launched independently of the editor. Assemblies, Direct3D 12, physics and input initialized, and the process remained responsive. No managed startup exception or missing-script error was found. The graphics driver emitted the same info-queue diagnostic seen in earlier local startup checks, then initialized successfully.
- The test process was closed. This was a startup smoke check, not a complete human play session or a test on a second computer.

Download and launch instructions are at the top of the repository README and inside the ZIP. Generated release archives remain outside Git; they are distributed as GitHub Release assets.
