# Full Rumble development log

## Current goal

Ship a Windows x64 Full Rumble package that players can extract and run without installing the
.NET SDK, runtime, Git, or Python. Players supply their own NASCAR Rumble USA/Canada disc dump;
all game-derived recompilation output is produced locally and is never shipped.

## Project and test paths

- Working repository: project root
- Release ZIP: `publish\FullRumble-Windows.zip`
- Release checksum: `publish\SHA256SUMS`
- Player config and generated game library: `%APPDATA%\FullRumble`
- Test dump: private local test data; not included in the repository
- Pinned RecompOne revision: `4edc6899a4516f292facfd35b77a2fd2d79e03c1`

## Route

The self-contained host bundles the pinned RecompOne runtime/recompiler, the Roslyn compiler
already used by the runtime, the original hook sources, and the recompilation config/function map.
On first run, the app validates and stages the player's dump, runs the recompiler in-process,
compiles the generated C# locally, caches the library under `%APPDATA%\FullRumble\build`, then
launches it. Later runs reuse the cached library when its version matches the host.

## Verified

- `dotnet test tests\NascarRumbleRecomp.Tests -c Release --nologo`: 25 passed.
- `dotnet build NascarRumbleRecomp -c Release --nologo`: succeeded with no warnings or errors.
- Self-contained `win-x64` publish contains the runtime/compiler and original recompile support,
  but no generated game library or disc data.
- The published executable printed its version with `PATH` empty.
- With `PATH` empty and `DOTNET_ROOT` pointing to a nonexistent directory, the published
  executable validated the known test dump, staged a local copy, recompiled the game (2,004
  functions), and produced `%APPDATA%\FullRumble\build\NascarRumbleRecompLib.dll`.
- Launched the packaged app through its normal first-run UI; it completed recompilation and
  reached the NASCAR Rumble main menu. Captured the setup and game windows for inspection, then
  closed only the process started for this check.
- Repository, publish-folder, and ZIP-member legal lint passed. ZIP lint inspects archive members
  and excludes compressed archive size from the payload file-size check.
- Release archive SHA-256 after input settings update: `34b3ffe13c85254b45e92a6ed856e606462e8837f896c8a8095ce888256dcd98`

## Input settings update

- Added a Mouse tab with per-player mouse-button and wheel bindings, plus opt-in horizontal mouse
  steering with sensitivity and deadzone controls (disabled by default).
- Retained keyboard and gamepad remapping. The gamepad tab now displays the connected device and
  offers Auto, PlayStation, Xbox, Nintendo Switch, and Generic button-label layouts.
- SDL-recognized controllers continue through SDL's standardized game-controller path. Unrecognized
  USB devices are now opened as raw joysticks so buttons, axes, and hats can be captured and bound.
- Regenerated `NascarRumbleRecompLib\patches\recompone-runtime.patch`; it reverse-checks against the
  modified submodule and applies cleanly to a temporary worktree at the pinned RecompOne revision.
- Runtime and host builds succeeded; `dotnet test tests\NascarRumbleRecomp.Tests -c Release` passed
  25/25, and repository legal lint passed. The runtime build still reports four warnings in unrelated
  pre-existing files.
- Physical DualShock, DualSense, Switch, Xbox, and generic USB hardware were not available for
  verification; device detection and live remapping still need a real-controller check.

## Notes and remaining verification

- The tracked runtime patch needs `git apply --unidiff-zero`; standard context application fails
  because one original hunk's line context does not match the pinned source. A clean checkout
  test confirmed the zero-context patch applies.
- The `thirdparty\RecompOne` submodule already had local edits when this continuation started.
  Preserve them; do not reset or discard them.
- The main-menu boot is visually verified. In-game race play, audio, saves, and the newly added
  mouse/gamepad settings UI remain unverified.
- No tag, push, or GitHub release has been created.
