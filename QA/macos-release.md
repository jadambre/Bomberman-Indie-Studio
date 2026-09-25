# macOS Apple Silicon v1.0.0 verification

Date: 25 September 2026.

## Build and package checks

- Build tooling and configuration: commit `ce4cb02e8b882b208821f9d0c6e0c8a5bc8725ea`. Runtime game source, scene and resources are unchanged from the Windows v1.0.0 release.
- Host: Windows. Unity 6000.3.23f1 with Mac Build Support (Mono); native ARM64 release target. The build succeeded with zero errors and reported 86,750,220 bytes.
- Bundle: `Bomberman Indie Studio.app`. `Info.plist` identifies an `APPL` bundle, executable `Bomberman Indie Studio`, identifier `com.jadambre.bombermanindiestudio`, version `1.0.0` and minimum macOS `12.0`.
- Main executable, Unity player library and all three Mono native libraries were inspected: all five are ARM64 Mach-O binaries. There is no Intel-only native binary in this package.
- Archive: `Bomberman-Indie-Studio-v1.0.0-macOS-AppleSilicon.zip`, 42,757,960 bytes, 172 entries, 151 files and one `Bomberman-Indie-Studio-macOS` root folder. The complete app and an English README are included.
- SHA-256: `302a651e1b05cfdfc434478663be54a934fca4fc4d88020e364995a0c2b68ab1`.
- `Tools/package_macos.py` verified the archive's CRCs, entry list and contents against the inputs. ZIP entries have Unix attributes: directories and all Mach-O binaries use mode `0755`; ordinary files use `0644`. There are no symlinks in this bundle.
- An independent archive inspection confirmed all five ARM64 binaries and their execution permissions, the bundle metadata, runtime assembly, and that the embedded README exactly matches `Docs/macOS-README.md`. Windows executables, launch scripts, logs and debug symbol files are absent.
- The Python packaging helper also passed 24 in-memory checks covering thin/fat Mach-O parsing, invalid inputs, Unix modes and ZIP content validation. No test fixtures were added to the game.
- The new build command completed successfully, including restoration of the previous architecture and backend. Editor source uses reflection for the optional Mac architecture API and does not introduce a compile-time dependency on the Mac module.

## Limits and Mac test checklist

This is a Windows cross-build, **not a verified Mac play session**. Unity's generated code-signature data is present, but Developer ID signing, Apple notarization and signature validation by macOS have not been performed. Archive integrity is not a claim of notarization or Gatekeeper acceptance.

The owner has access to an Apple Silicon Mac for the remaining checks:

- Download the public ZIP through a browser, extract it in Finder and open the app from Applications. Record any Gatekeeper message.
- Verify a native launch on macOS 12 or later, both interface languages, camera framing and all menus.
- Play solo and local co-op, checking movement, bombs, simultaneous input, animation and sound.
- Verify the five arenas, progression and power-up resets.
- Save, quit, relaunch and continue the campaign; verify language and audio preferences persist.

The README and release notes disclose these limits. Builds remain excluded from Git and are provided as GitHub Release assets alongside the unchanged Windows package.
