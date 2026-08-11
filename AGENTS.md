# Working on this repository

Notes for anyone — human or AI — changing this mod. The [README](README.md) is
for people who just want to use it; nothing here needs repeating there.

Besiege API notes live in [docs/MODDING-NOTES.md](docs/MODDING-NOTES.md). Add to
them when you learn something the hard way.

## Layout

```
GitView/                     the folder Besiege loads
  Mod.xml                    manifest: assembly, keys
  GitViewScripts.dll         built by tools/build.sh (not in git)
  GitViewScripts/*.cs        mod source
tools/build.sh               compiles with Besiege's own compiler
tools/verify-build.sh        the check to run after editing any .cs
tools/run-tests.sh           the headless tests (tools/tests/)
tools/install.sh             builds and installs into the game
docs/MODDING-NOTES.md        the Besiege APIs this stands on
```

The sources divide along one line, and it is worth keeping:

| engine-free — tested headless | needs a running game |
| --- | --- |
| `BlockRecord.cs` the model and its tolerances | `VersionScan.cs` reads the autosave folder |
| `BlockDiff.cs` the three-pass matcher | `GhostView.cs` draws the overlay |
| `MachineSnapshot.cs` a version's blocks | `HistoryView.cs`, `UIF.cs`, `UIBuild.cs` the window |
| `VersionEntry.cs` names, stamps, counts | `BrowserWatch.cs` the load-screen button |
| `RowSort.cs` the column ordering | `IconArt.cs`, `GitViewMod.cs` |

The left column is where the mod is actually right or wrong, and `run-tests.sh`
compiles **only** those files. That is deliberate: if the diff ever needs a
running Besiege to be checked, the test build breaks before anything else does.

## Hard rules

**Never change `<ID>` in `Mod.xml`.** The game generates it on first load, and
changing it breaks every saved machine that references the mod.

**Run `./tools/verify-build.sh` after editing any `.cs`.** Besiege's compiler is
ancient, and a bad enough failure kills the game on startup rather than reporting
an error. The script reproduces those failures in about a second.

**Run `./tools/run-tests.sh` after touching anything in the left column.**

**Respect the mod loader's blacklist.** `System.Xml`, `System.Reflection`,
`System.Net`, `System.IO` bar a few carve-outs, and more are refused at load, per
*member*, and one reference in one method rejects the whole assembly.
`build.sh` scans every build for it.

## Why it is built the way it is

**The diff is three passes, not one.** Pairing blocks by the guid in the save
file is the obvious implementation and is quietly wrong: Besiege reissues a
block's guid when it is copied, mirrored or restored by an undo, and the block is
otherwise untouched. Trusting it reports a deletion and an identical addition in
the same place, which is the one thing a diff must never do. The second and third
passes — identical in every other respect, then same type and nearest position —
are what stop that. `BlockDiff`'s own comment has the numbers.

**Everything is compared with a tolerance, including settings.** Coordinates are
decimal text read back through a fast float parser, so an untouched block comes
back a hair off. Settings are worse: some of them hold live physics values. See
`BlockRecord.SettingsDecimals`.

**The overlay is Besiege's own placement ghosts, not a tint.** Blocks share
their materials, so tinting one girder tints every girder, and putting the
original back means being right about what it was. A ghost is removed by
deleting it.

**Nothing reads a file directly.** Not politeness — `System.Xml` and `System.IO`'s
file classes are blacklisted, so a mod cannot parse its own saves. Listing goes
through the browser's virtual folders and parsing through `XmlLoader`.

**The load-screen button measures the slot rather than assuming it.** The private
fields naming each of a slot's buttons are unreachable, so the button to copy is
chosen by what it *is* (active, has a renderer) and placed by reading the spacing
of the buttons already there. A hardcoded offset would be a guess that breaks on
the next Besiege.

**The history is counted in a coroutine.** A folder can hold a hundred versions
of a five-hundred-block machine. Doing that in one frame is a visible freeze;
done a version per frame the window is usable immediately and the numbers arrive
behind it. Only two snapshots are held at once.

**Thumbnails load and unload with the scroll.** They are 512x512 PNGs, a
megabyte each as a texture.

## Verification status

The block diff was checked against the six machines in a real
`SavedMachines/AutoSave` folder — 153 version pairs, 2 to 466 blocks — by
compiling the shipped `BlockDiff` into a command-line harness that parses the
.bsg files directly. It agrees **exactly** with an independent implementation of
the same rules written from the file format: +743 added, ~485 changed, -615
removed, 6465 unchanged, on every folder. That check found one real bug, since
fixed and covered by a test: settings compared as written reported a piston as
changed once a minute because its start-position wobbled in the eighth decimal
place.

74 assertions cover the matcher, the tolerances, the quantisation, the timestamp
parsing and the sorting. The mod compiles under Besiege's own compiler and
references nothing the loader forbids.

**Runtime behaviour has not been confirmed in game.** Nothing here has been run
inside Besiege. The parts most worth watching on a first run:

- **where the compare button lands.** The placement is derived from the spacing
  of a slot's existing buttons, which has not been seen. `BrowserWatch` logs the
  button count and the position it chose, once, at Info level — read that first
  if the button is somewhere silly, and adjust `StepFactor` or the anchor it
  steps from.
- **whether the ghost prefabs take a colour.** `GhostView` replaces their
  materials with one built from the first transparent shader it can find, and
  logs which. If no candidate shader is in the build it falls back to tinting
  Besiege's own ghost material, which may ignore the tint — in which case added,
  changed and removed would all look the same, and the shader list is where to
  look.
- **whether `Machine.IsLoadingMachine` is enough of a wait** before the overlay is
  drawn over freshly rebuilt blocks.
- **whether the Window prefab's scroll view survives being retargeted** the way
  `FindScrollView` does, and whether the header lands above it.

Errors appear in `Player.log`, and in the in-game console with `show_logs true`.
