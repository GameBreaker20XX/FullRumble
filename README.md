# Full Rumble
 <img width="1536" height="672" alt="NASCAR Full Rumble" src="https://github.com/user-attachments/assets/b86ccb27-2c5a-4e88-90f9-7c1a8d69e3f5" />
<img width="32" height="32" alt="NASCAR Full Rumble 32" src="https://github.com/user-attachments/assets/64effb20-85c7-46bc-b4f4-bc4872ef793b" />

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

### Updating Full Rumble

Open **Settings > Updates** in Full Rumble to check for a newer official stable release. The app asks before
downloading; it verifies the package checksum and prepares the update in the background. Close Full Rumble when
you are ready to install: a small background helper applies the update, preserves the local `disc/` folder and
`%APPDATA%\FullRumble` settings/build cache, and reopens the game. Updates are never installed automatically,
and prereleases are not offered. Race 1.0 users need to download and extract Race 1.1 once to get the in-app
updater.

## Input settings

Open **Settings > Input** to remap keyboard, gamepad, and mouse controls for either player. SDL automatically
uses standard mappings for supported PlayStation, Xbox, and Nintendo controllers; unrecognized USB gamepads
remain usable through manual button, axis, and hat binding. The Gamepad tab shows the connected device and lets
you choose how button labels are displayed. The Mouse tab supports mouse buttons and wheel directions, with
optional horizontal mouse steering disabled by default.

### Default controls (Player 1)

The complete keyboard, standard controller, generic USB joystick, mouse, and Player 2 reference is in
[CONTROLS.md](CONTROLS.md) (also included in the Windows ZIP).

| Game action | Keyboard | Standard gamepad | Generic USB joystick* |
|---|---|---|---|
| Cross | Z | South face (Xbox A / PlayStation Cross / Switch B) | Button 1 |
| Circle | X | East face (Xbox B / PlayStation Circle / Switch A) | Button 2 |
| Square | A | West face (Xbox X / PlayStation Square / Switch Y) | Button 3 |
| Triangle | S | North face (Xbox Y / PlayStation Triangle / Switch X) | Button 4 |
| L1 | Q | L1 / LB | Button 5 |
| R1 | W | R1 / RB | Button 6 |
| L2 | E | L2 / LT | Button 7 |
| R2 | R | R2 / RT | Button 8 |
| L3 | F | Left stick click | Button 9 |
| R3 | G | Right stick click | Button 10 |
| Start | Enter | Start / Menu | Button 11 |
| Select | Right Shift | Select / Back / View | Button 12 |
| Up | Up Arrow | D-pad Up | Hat 1 Up |
| Down | Down Arrow | D-pad Down | Hat 1 Down |
| Left | Left Arrow | D-pad Left | Hat 1 Left |
| Right | Right Arrow | D-pad Right | Hat 1 Right |

*For an unrecognized USB joystick, connect it and choose **Reset to Defaults** in the Gamepad tab to apply
the generic button/hat map. The default mouse bindings are unassigned; horizontal mouse steering is off.
Player 2 has no keyboard, gamepad, or mouse bindings until configured.

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

The public release name is separate from the four-part technical file version. **Race 1.1** is technical
version `0.2.0.0` (tag `v0.2.0.0`); **Race 1.0** was `0.1.0.0` (tag `v0.1.0.0`). Builds are
distributed only on the official GitHub Releases page. This project does not include any game assets, and
all derived output is generated on the player's machine from a disc they own.

## Changelog

### Race 1.1 — technical version 0.2.0.0

- Added an in-game **Settings > Updates** page for checking and installing newer stable releases.
- Update downloads are checksum-verified; a background helper preserves the player's staged `disc/` folder
  and restarts Full Rumble after the player closes it.
- Added the standalone controls reference in `CONTROLS.md` and included it in the Windows package.

### Race 1.0 — technical version 0.1.0.0

- First self-contained Windows release with first-run disc selection and local recompilation.
- Added keyboard, gamepad, and mouse remapping with generic USB joystick support.

## Legal disclaimer

This project is not affiliated with, endorsed by, or sponsored by NASCAR, Electronic Arts, or Sony Interactive Entertainment. NASCAR, EA, and Sony marks, and NASCAR Rumble itself, are the property of their respective owners.

The repository contains original code, original art, and open-source tooling under their own licenses. It does not include any copyrighted game assets, reproductions of the game executable, or extracted game data.
