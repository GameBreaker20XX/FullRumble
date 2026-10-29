# FullRumble

FullRumble is an unofficial, work-in-progress static recompilation of **NASCAR Rumble**
(PlayStation, EA, 2000, USA/Canada, serial SLUS-01068) for modern PCs. The game's original
MIPS executable is translated ahead of time into C# that runs against a PS1 hardware runtime;
nothing is interpreted at play time.

**This project does not include any game assets.** You must supply a dump of a disc you own.
Everything derived from the disc is produced on your machine, from your copy.

## Table of contents

- [Status](#status)
- [Minimum system requirements](#minimum-system-requirements)
- [How to install](#how-to-install)
- [Supplying your disc](#supplying-your-disc)
- [Features](#features)
- [Known issues](#known-issues)
- [FAQ](#faq)
- [Building from source](#building-from-source)
- [Credits](#credits)
- [Legal](#legal)

## Status

Early milestone build. The table below is updated every milestone and only lists what has
actually been run and observed on a real dump of SLUS-01068.

| Area | State | Notes |
| --- | --- | --- |
| Build with no disc present | Verified | Host and legal lint build and pass without any game file. |
| Disc validation (cue/bin/iso, ISO9660, SYSTEM.CNF, SHA-256) | Not yet in this repo | Lands with the disc library milestone. |
| Native port boot (intro, menu, race) | Verified in the development tree | Reproduced on Windows from the upstream ModSDK work; being re-landed here milestone by milestone. |
| First-run setup screen | Not yet | Lands with the setup milestone. |

Anything not listed as *Verified* must be treated as unvalidated.

## Minimum system requirements

- Windows 10/11 x64 (other platforms are not built yet)
- .NET 10 runtime (the release zip is self-contained; building needs the .NET 10 SDK)
- OpenGL 3.3+ or OpenGL 4.x graphics driver (the runtime renders through OpenGL)
- 4 GB RAM, gamepad or keyboard
- A dump of NASCAR Rumble (USA/Canada), SLUS-01068 — see [Supplying your disc](#supplying-your-disc)

## How to install

1. Download the latest release **only from the official Releases page of this repository**.
   Builds are only distributed on the official Releases page; archives found anywhere else
   are not ours and may be unsafe.
2. Extract the zip to a folder of your choice (not `System32`, not a synced cloud folder).
3. Run `FullRumble.exe`. On first launch the setup screen asks for your disc dump and builds
   the game code locally from it.
4. If Windows SmartScreen warns about the unsigned build, see [FAQ](#faq).

## Supplying your disc

Accepted inputs:

- a `.cue` + `.bin` pair (raw 2352-byte sectors, single data track),
- a bare raw `.bin` (2352-byte sectors),
- a cooked 2048-byte `.iso`,
- or a folder containing any of the above.

Rejected with a plain-language message: archives (`.zip`, `.7z`, `.rar`), `.chd`/`.pbp`,
dumps of other regions or other games, and modified or truncated dumps.

See [docs/DUMPING.md](docs/DUMPING.md) for how to create a `.bin`/`.cue` from a disc you own.

## Features

Planned, in milestone order: playable race with the original frontend, keyboard and controller
support with remapping, memory card saves, resolution scaling and widescreen-friendly windowing,
and the original intro movies with skip. The [Status](#status) table is the source of truth for
what works today.

## Known issues

- This milestone is a skeleton: it validates configuration and builds; it does not play yet.
- Unsigned builds can trip SmartScreen or over-eager antivirus products; see [FAQ](#faq).

## FAQ

**Does this repo contain the game?**
No. This project does not include any game assets. The recompiled game code is generated on
your machine from your own dump and is never committed or shipped.

**Windows SmartScreen says the build is untrusted. Is it safe?**
Release builds are unsigned, so SmartScreen shows its "unknown publisher" warning. Verify the
download against `SHA256SUMS` on the Releases page before extracting (see
[docs/BUILDING.md](docs/BUILDING.md#verifying-a-release)). If your antivirus flags a build
downloaded from the official Releases page, it is a false positive; builds from anywhere else
are not ours.

**Where is my configuration stored?**
`%APPDATA%\FullRumble\config.json` on Windows.

**Can I play with a keyboard?**
Yes, once the runtime milestone lands; a gamepad is recommended.

**Why is my dump rejected?**
The validator checks the ISO9660 volume, `SYSTEM.CNF` (it must boot `SLUS_010.68`) and the
SHA-256 of the executable. Other regions, other games, edited or truncated dumps fail on
purpose, with a message that says why.

## Building from source

See [docs/BUILDING.md](docs/BUILDING.md). The short version:

```
git clone --recurse-submodules https://github.com/GameBreaker20XX/FullRumble.git
cd FullRumble
dotnet build NascarRumbleRecomp -c Release
```

The build succeeds with no disc present.

## Credits

- [andreigor17](https://github.com/andreigor17) — NASCAR Rumble ModSDK (MIT): reverse-engineering
  notes, function map and the native-port hooks this project re-lands with attribution.
- [BlackLabelHQ](https://github.com/BlackLabelHQ) — RecompOne (MIT): the PS1 static recompiler
  and hardware runtime.
- [hedge-dev/UnleashedRecomp](https://github.com/hedge-dev/UnleashedRecomp) (GPL-3.0) — structure,
  first-run flow and documentation shape only. No code or text is copied from it.

## Legal

FullRumble is a fan project for preservation and education. It is not affiliated with,
endorsed by, or sponsored by NASCAR, Electronic Arts or Sony Interactive Entertainment, and
none of them are involved in this project. NASCAR, EA and Sony marks and the game itself belong
to their respective owners. This repository contains no game code, no game assets and no
disc-derived output; see [docs/LEGAL.md](docs/LEGAL.md).
