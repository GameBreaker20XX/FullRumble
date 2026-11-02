# Full Rumble

Full Rumble is a native Windows PC static recompilation of NASCAR Rumble (USA/Canada, SLUS-01068). It is built from a dump you make from a disc you own and run on your own machine.

This project does not include any game assets.

## Player install goal

The player-facing build is designed to be installed like a normal Windows game:

- no .NET SDK required on the user's machine
- no developer toolchain required
- no `dotnet install` or `dotnet run` step for the end user
- a self-contained Windows x64 build that includes the .NET runtime

The official release package is a ZIP containing the publish output; extract it and run `FullRumble.exe` directly.
On first launch, choose your own disc dump. Full Rumble validates and stages it, then recompiles the game library
on your machine using its bundled compiler. This first setup can take several minutes. No .NET SDK, Git, or Python
installation is required on the player's machine.

## What is included

- Original host code and UI written for this project
- Recompilation support for the game runtime and launcher flow
- Legal validation and staging tools to accept a player's own dump
- Documentation for dumping, building, and releasing

## What is not included

- No disc images or game media
- No extracted assets or generated game bytes
- No `SLUS_010.68` executable or copyrighted materials copied from the game
- Nothing derived from the disc is committed or shipped in this repository

## How it works

The host builds without a disc. On first run, it validates the player's own dump, copies it into
Full Rumble's local disc folder, and runs the recompiler and runtime C# compiler inside the app.
The generated library and intermediate sources are stored under `%APPDATA%\FullRumble\build`;
repository-only developer outputs remain in git-ignored `generated/` and `private/` folders.

The project validates a disc image, accepts a `.cue` + `.bin` pair, a bare `.bin`, or a cooked `.iso`, and stages an accepted disc into the game's own local `disc/` folder for one-time setup.

## Install and play

1. Download the Windows ZIP from the [official Releases page](https://github.com/GameBreaker20XX/FullRumble/releases).
2. Extract the ZIP to a folder you can write to and launch `FullRumble.exe`.
3. Add the dump you made from your own NASCAR Rumble disc.
4. Wait while Full Rumble recompiles the game locally, then it launches. Later launches reuse that local library.

The original disc dump is copied, never moved or deleted. Full Rumble stores its configuration and generated game
library in `%APPDATA%\FullRumble`. The released app is self-contained and does not require the .NET SDK, .NET
runtime, Git, or Python to be installed separately.

## Input settings

Open **Settings > Input** to remap keyboard, gamepad, and mouse controls for either player. SDL automatically
uses standard mappings for supported PlayStation, Xbox, and Nintendo controllers; unrecognized USB gamepads
remain usable through manual button, axis, and hat binding. The Gamepad tab shows the connected device and lets
you choose how button labels are displayed. The Mouse tab supports mouse buttons and wheel directions, with
optional horizontal mouse steering disabled by default.

## Developer build and validation

Contributors need the .NET 10 SDK to create a self-contained Windows x64 package:

```bash
dotnet publish NascarRumbleRecomp -c Release -r win-x64 --self-contained true -o publish/app
```

For development and tests:

```bash
dotnet build NascarRumbleRecomp -c Release
dotnet test tests/NascarRumbleRecomp.Tests -c Release --nologo
python tools/legal_lint.py .
```

A local legal lint run is mandatory before committing or publishing.

## Dumping your own disc

See [docs/DUMPING.md](docs/DUMPING.md) for the accepted input formats and how to validate a disc dump you made from a legally owned copy of NASCAR Rumble.

## Documentation

- [docs/BUILDING.md](docs/BUILDING.md) — build, run, and release workflow
- [docs/DUMPING.md](docs/DUMPING.md) — how to create and validate a dump
- [docs/LEGAL.md](docs/LEGAL.md) — repository and distribution rules
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — runtime and project structure

## Release policy

Builds are distributed only on the official GitHub Releases page. This project does not include any game assets, and all derived output is generated on the player's machine from a disc they own.

## Legal disclaimer

This project is not affiliated with, endorsed by, or sponsored by NASCAR, Electronic Arts, or Sony Interactive Entertainment. NASCAR, EA, and Sony marks, and NASCAR Rumble itself, are the property of their respective owners.

The repository contains original code, original art, and open-source tooling under their own licenses. It does not include any copyrighted game assets, reproductions of the game executable, or extracted game data.
