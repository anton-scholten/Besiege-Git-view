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

**Confirmed in game** on one run: the mod loads, the compare button appears on
slots with history, pressing it closes the browser and loads the newest version,
the window opens with thumbnails and counts, the counting coroutine fills the
rows, and the overlay draws (`Particles/Alpha Blended` is the shader it found).
`Machine.IsLoadingMachine` was enough of a wait, and retargeting the Window
prefab's scroll view worked.

Three things that run found, all since fixed:

- **Repainting a copied slot button took four attempts**, three of them silent
  failures: `material.mainTexture` (Besiege's shader need not sample `_MainTex`);
  a quad of our own parented to the face (invisible); and assigning a material to
  the button's own renderer, which found no renderer at all — because **a clone
  has no face in the frame it is cloned**, whatever builds it running in an Awake
  of its own. `Paint` now retries across sweeps and handles every kind of
  renderer, which is how the answer finally arrived: **a slot button's face is a
  `SpriteRenderer`**, so the thing to set is its `sprite`, sized against the one
  it replaces (see `IconSprite`). The material and quad paths stay as the
  fallback for buttons that are not sprites.
- **The placement heuristic was wrong, twice.** It stepped one "pitch" left of
  the leftmost button, where pitch was the smallest gap between any two — and a
  slot has nine buttons, eight of them inactive, two 0.018 apart. Only one is
  ever on show, so the place is its mirror through the middle of the slot.
- **The header and the status line were anchored to the window**, which is wider
  than the scrolling area the rows are laid out across, so every column drifted.
  Both are now placed by measuring the viewport (`UIBuild.PlaceStrip`).
- **The status line was then placed correctly and immediately un-placed**, by
  stretching its label to fill its parent — which works on a `Text Button`, whose
  label is a child, and destroys a `Text`, which *is* the label. `UIF.Label`
  encodes the distinction so it only has to be got right once.

Still not confirmed:

- **whether the branch glyph reads at button size**, and which of `Paint`'s
  routes got it there. `BrowserWatch` logs the button's renderers once, with
  their kind, mesh size and readability, and says separately if it fell back to a
  quad of its own — read that first if the icon is still wrong.
- **whether a machine *file* slot** (rather than an AutoSave folder) shows more
  than one button, which would change where ours goes. The same log line says.

Errors appear in `Player.log`, and in the in-game console with `show_logs true`.
