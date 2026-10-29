# Building FullRumble

The build succeeds with **no disc present**. Game-derived code is generated later, on the
machine that owns the dump; see [Step: recompile pipeline](#the-recompile-pipeline) once that
milestone lands (it is documented in this file from the milestone that introduces it).

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

```
dotnet build NascarRumbleRecomp -c Release
NascarRumbleRecomp\bin\Release\net10.0\FullRumble.exe --version
```

The executable prints the version, the no-assets notice, and whether a disc is configured in
`%APPDATA%\FullRumble\config.json`.

## Legal lint (local)

```
python tools/legal_lint.py .
```

It fails if any tracked-or-staged artifact contains a forbidden disc extension, a generated or
recompiled path, or a file above the size limit. The same script runs in CI on every pull
request, and again on release artifacts before publish.

## Verifying a release

Every release on the official Releases page ships a `SHA256SUMS` file next to the archives.
On Windows:

```
certutil -hashfile FullRumble-Windows.zip SHA256
```

Compare the printed digest with the line for that zip in `SHA256SUMS`. Unsigned builds can
trigger SmartScreen; a digest match against the Releases page is the check that matters.

## The recompile pipeline

Documented here from the milestone that introduces it. Until then, this section intentionally
does not exist further down: the pipeline is not in this tree yet.
