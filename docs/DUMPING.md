# Dumping your own disc

FullRumble needs a dump of **NASCAR Rumble (USA/Canada), serial SLUS-01068**, from a disc you
legally own. This document explains how to make one; it never links to game content.

## What the validator accepts

| Input | Shape |
| --- | --- |
| `.cue` + `.bin` pair | raw 2352-byte sectors, one data track holding the game (the usual PS1 layout) |
| bare `.bin` | raw 2352-byte sectors |
| `.iso` | cooked 2048-byte sectors |
| folder | containing any of the above; scanned one level deep |

A multi-track `.cue` is accepted only when the game data lives in a single track; otherwise the
tool refuses it and says why. Audio tracks in other files listed by the same `.cue` are fine as
long as the data track is self-contained.

## What is rejected, and why

- Archives (`.zip`, `.7z`, `.rar`): extract them yourself first; the tool reads disc images only.
- `.chd` / `.pbp`: not implemented; convert to `.bin`/`.cue` or `.iso` with your own tooling.
- Other regions or other games: `SYSTEM.CNF` must boot `SLUS_010.68` and the executable's
  SHA-256 must match the pinned value for the USA/Canada disc.
- Modified or truncated dumps: the same hash check catches them.

## Making a dump on Windows

The reference tool is **DiscImageCreator** (free, open source), which produces redump-quality
images:

1. Put the disc in a drive that reads CD-ROM (most DVD drives do).
2. Run `DiscImageCreator.exe cd <drive letter> <output path>\nascar 4` (4 read passes).
3. Answer the prompts with defaults; when finished you get `.bin`/`.cue` (plus logs).
4. Keep the `.bin` and its `.cue` **in the same folder** — the pair is validated together.

Any other ripping tool works if it outputs raw 2352-byte sectors with a matching `.cue`, or a
cooked 2048-byte `.iso`.

## Checking your dump

```
FullRumble.exe --check-disc "path\to\nascar.cue"
```

It prints each check that passed (container, ISO9660 volume, `SYSTEM.CNF`, executable hash) or
the first reason the dump was refused, and exits non-zero on refusal. A folder works too: it is
scanned one level deep, and if several images are found they are listed so you can pass the
right one. `--extract IMAGE DISCPATH OUT` copies a single file out of a validated-style image
(for example `SLUS_010.68`) so you can inspect what the validator sees.
