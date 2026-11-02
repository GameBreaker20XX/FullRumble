# Building FullRumble

The host build succeeds with **no disc present**. Game-derived code is generated later, on the
machine that owns the dump, by [the recompile pipeline](#the-recompile-pipeline); it lands in the
git-ignored `NascarRumbleRecompLib/generated/` folder and is never committed or released.

## Requirements

- Windows 10/11 x64 (only platform built today)
- .NET SDK 10.0 or newer (`dotnet --version` must print `10.x`)
- Git with submodule support
- Python 3.10+ for the legal lint tool

No CMake or vcpkg is needed: the stack is C#/.NET on the RecompOne runtime.

## Clone

```
git clone --recurse-submodules https://github.com/GameBreaker20XX/FullRumble.git
cd FullRumble
```

If you cloned without `--recurse-submodules`, run the helper instead:

- Windows: `update_submodules.bat`
- Linux/macOS: `./update_submodules.sh`

## Build and run (no disc)

Developers need the .NET 10 SDK to build Full Rumble. The release package is self-contained, so
players do not need the .NET SDK or runtime installed:

```
dotnet publish NascarRumbleRecomp -c Release -r win-x64 --self-contained true -o publish/app
publish\app\FullRumble.exe --version
```

You can also run the helper script in `tools/publish_windows.ps1`, which creates a ZIP that users can
extract and run directly. The executable prints the version, the no-assets notice, and whether a
disc is configured in `%APPDATA%\FullRumble\config.json`.

The host provides an in-game **Settings > Updates** page. The package script also publishes
`FullRumbleUpdater` as a standalone Windows x64 Native AOT helper (`FullRumbleUpdateHelper.exe`), which
waits for the game to close before replacing installed files and relaunching it. Players do not need to
install .NET. `CONTROLS.md` is copied into the package as a player-facing controls reference.

## Legal lint (local)

```
python tools/legal_lint.py .
```

It fails if any tracked-or-staged artifact contains a forbidden disc extension, a generated or
recompiled path, or a payload file above the size limit. ZIP archives are inspected member by
member; the compressed archive's total size is not treated as a payload file. The same script
runs in CI on every pull request, and again on release artifacts before publish.

## Verifying a release

Every release on the official Releases page ships a `SHA256SUMS` file next to the archives.
On Windows:

```
certutil -hashfile FullRumble-Windows.zip SHA256
```

Compare the printed digest with the line for that zip in `SHA256SUMS`. Unsigned builds can
trigger SmartScreen; a digest match against the Releases page is the check that matters.

## The recompile pipeline

The host (`NascarRumbleRecomp`) and the game library (`NascarRumbleRecompLib`) are separate
projects. The host builds and runs without a disc; the library holds the recompiled game and is
produced **on your machine, from a disc you own**. Nothing generated is committed or released.

### Developer path (command line)

1. Put your dump in the git-ignored `NascarRumbleRecompLib/private/` folder (one level deep is
   scanned), or pass it explicitly with `--cue`. Accepted: a `.cue`+`.bin` pair, a raw 2352-byte
   `.bin`, or a cooked 2048-byte `.iso`. See [DUMPING.md](DUMPING.md).
2. Run the pipeline:

   ```
   python tools/build_lib.py                       # uses private/
   python tools/build_lib.py --cue "path\to\game.cue"
   ```

The script:

- validates the disc with the host (`FullRumble.exe --check-disc`): ISO9660 volume, `SYSTEM.CNF`
  boot id `SLUS-01068`, and the pinned SHA-256 of `SLUS_010.68`. It refuses to recompile an
  invalid or unknown disc;
- checks out the pinned RecompOne submodule commit (`NascarRumbleRecompLib/recompone.lock.json`)
  and applies `NascarRumbleRecompLib/patches/recompone-runtime.patch` to its working tree (the
  submodule commit itself is never modified);
- builds `RecompOne.Recompiler`, then runs it over the disc using the merged build config in
  `generated/nascar.build.json` and the function map `config/nascar_funcmap.json`;
- re-runs the recompiler up to six times, each time adding any literal dispatcher targets that
  `tools/augment_funcmap.py` finds missing from the function map, until it converges;
- builds `NascarRumbleRecompLib.dll` from `src/` plus the generated `generated/Recompiled/*.cs`.

Generated C# lands in `NascarRumbleRecompLib/generated/` (about 6-7 MB, git-ignored). A successful
run prints `PASS - NascarRumbleRecompLib.dll built from <disc>`. The generated code compiles with
roughly 53 `CS0162` "unreachable code" warnings; these come from the recompiler's output and are
expected, not errors.

### Player path (setup screen)

On first launch, the setup screen accepts a file or folder, validates the dump, stages a local copy,
and runs the recompiler and C# compiler inside the app. It uses the runtime compiler bundled in the
release; it does not invoke `dotnet`, Python, or Git on the player's machine. The generated library
and intermediate sources stay under `%APPDATA%\FullRumble\build`. The original dump is never moved
or modified. First setup can take several minutes; progress and failures are shown in the setup log.

For troubleshooting or an advanced developer workflow, the self-contained executable also accepts
`FullRumble.exe --build-library "path\to\disc.cue"` after validating and staging the player's own dump.
