# Architecture

FullRumble mirrors the layout of UnleashedRecomp (structure only; no code or text is copied
from that GPL-3.0 project): an application project, a "Lib" project that owns the recompiler
configuration and the generated code, and a resources project that holds only assets we made
ourselves.

## Folder map

| Path | Contents |
| --- | --- |
| `NascarRumbleRecomp/` | The application: host, first-run setup, configuration, UI. Builds and runs with no disc present. |
| `NascarRumbleRecompLib/` | Recompiler configuration (`config/`), the runtime patch, hook sources, and build rules for the generated code. Generated output lives in `generated/` and is git-ignored. |
| `NascarRumbleRecompResources/` | Only original assets we created: app icon, fonts, UI art, localisation. Nothing from the game. |
| `docs/` | BUILDING, DUMPING, ARCHITECTURE, LEGAL. |
| `tools/` | Repo tooling, including the legal lint. |
| `thirdparty/` | Submodules: RecompOne (MIT), the PS1 static recompiler and hardware runtime. |
| `.github/workflows/` | Build, release, and legal lint. |

## Runtime stack

- **Language/build**: C# on .NET 10, `dotnet build`. No CMake or vcpkg.
- **Recompiler**: RecompOne (MIT) translates the MIPS R3000A code of `SLUS_010.68` ahead of time
  into C# against a `CpuContext`; the same project supplies the PS1 hardware runtime (GPU with
  an OpenGL backend, GTE, SPU including XA audio, CD-ROM controller, MDEC, DMA, timers,
  controllers, memory cards, and an OpenBIOS-style BIOS, so no Sony BIOS is needed).
- **Function map**: `NascarRumbleRecompLib/config/nascar_funcmap.json`, derived from the
  MIT-licensed NASCAR Rumble ModSDK Ghidra analysis (attribution retained in LICENSE).

## Data flow (target state, milestone by milestone)

1. Player supplies `.cue`+`.bin`, raw `.bin`, or cooked `.iso`.
2. The validator reads the ISO9660 volume and `SYSTEM.CNF`, confirms `SLUS-01068`, and checks
   the SHA-256 of `SLUS_010.68`.
3. The recompiler runs **on the player's machine** and writes C# into
   `NascarRumbleRecompLib/generated/` (git-ignored).
4. That code compiles into `NascarRumbleRecompLib.dll`, a separate library the prebuilt host
   loads at startup. Host and runtime build and release without any disc.
5. At play time the host loads the library, hands it the validated disc image for streaming
   (video, audio, track data), and the runtime drives frames, input and saves.

Nothing derived from the disc ever enters the repository or a release archive; the legal lint
enforces this in CI.

## Configuration

User settings live in `%APPDATA%\FullRumble\config.json` (Windows). The disc path chosen in
setup is stored there; the app re-validates it on every launch.
