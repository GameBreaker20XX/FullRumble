# Legal

## What this repository contains

- Original code written for this project (MIT, see `LICENSE`).
- Third-party open-source code as submodules, each under its own licence (RecompOne, MIT).
- Recompiler *configuration*: function addresses, names and hook declarations for
  `SLUS_010.68`. These are facts and instructions, not game bytes.
- Original assets we created ourselves (icon, fonts, UI art) in `NascarRumbleRecompResources/`.

## What this repository never contains

- Disc images of any kind (`.bin`, `.cue`, `.iso`, `.img`, `.mdf`, `.mds`, `.chd`).
- The game executable (`SLUS_010.68`) or any extracted asset (textures, audio, video, saves,
  memory-card files).
- Recompiled or generated code, generated sources, or symbol files that embed game bytes.
- Anything produced from the disc: all of it is generated on the player's machine, from a disc
  the player owns, into git-ignored folders (`generated/`, `private/`).

The same rules apply to every release archive. `tools/legal_lint.py` enforces them locally and
in CI, on the repository tree and on release artifacts, and fails the build on any violation.

## Trademarks

This project is not affiliated with, endorsed by, or sponsored by NASCAR, Electronic Arts or
Sony Interactive Entertainment. NASCAR, EA and Sony marks, and NASCAR Rumble itself, belong to
their respective owners. Logos and art shipped here are original creations and contain no
NASCAR, EA or Sony marks and no game art.

## Why this is legal to distribute

The repository distributes tools and configuration, not copyrighted game content. Players who
own the game produce every derived artifact themselves. This is the same model used by other
static-recompilation projects.

## Licences of reused work

- RecompOne — MIT (BlackLabelHQ). Vendored as `thirdparty/RecompOne`.
- NASCAR Rumble ModSDK — MIT (andreigor17). Function map and reverse-engineering notes reused
  with attribution; no game bytes were copied from it (it distributes none).
- UnleashedRecomp — GPL-3.0 (hedge-dev). Structure, first-run flow and documentation shape were
  studied and re-implemented independently; **no code or text is copied**. Nothing from it is
  vendored, so no GPL code enters this MIT project.
