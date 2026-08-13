# Working on this repository

Notes for anyone — human or AI — changing this mod. The [README](README.md) is
for people who just want to use it; nothing here needs repeating there.

Besiege API notes live in [docs/MODDING-NOTES.md](docs/MODDING-NOTES.md). Add to
them when you learn something the hard way.

## Layout

```
GitView/                     the folder Besiege loads
  Mod.xml                    manifest: assembly, keys, icons
  GitViewScripts.dll         built by tools/build.sh (not in git)
  GitViewScripts/*.cs        mod source
  Resources/icon.png,        the mods-menu logo and the Workshop
    thumb.png                  thumbnail, by tools/make_icon.py
tools/build.sh               compiles with Besiege's own compiler
tools/verify-build.sh        the check to run after editing any .cs
tools/run-tests.sh           the headless tests (tools/tests/)
tools/install.sh             builds and installs into the game
tools/make_icon.py           redraws the icons (needs Pillow)
docs/MODDING-NOTES.md        the Besiege APIs this stands on
```

The sources divide along one line, and it is worth keeping:

| engine-free — tested headless | needs a running game |
| --- | --- |
| `BlockRecord.cs` the model and its tolerances | `VersionScan.cs` reads the autosave folder |
| `BlockDiff.cs` the three-pass matcher | `GhostView.cs` draws the overlay |
| `MachineSnapshot.cs` a version's blocks | `HistoryView.cs`, `UIF.cs`, `UIBuild.cs` the window |
| `VersionEntry.cs` names, stamps, counts | `BrowserWatch.cs` the load-screen button |
| `RowSort.cs` the column ordering | `MapperWatch.cs` the autosave nudge |
| `SurfaceShape.cs` a build surface's corners | `OptionsView.cs` choosing the colours |
| `DiffPalette.cs` the four colours | `Selection.cs`, `SlotMark.cs` picking machines |
| | `Prefs.cs` what is remembered |
| | `ClickShield.cs` keeps clicks off the game |
| | `IconArt.cs`, `Relative.cs`, `GitViewMod.cs` |

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
deleting it. It is also not inert when it arrives — `GhostView.Sterilise` is
what stops every diff raising a screenful of INTERSECTION warnings.

**The mod nudges Besiege's autosave.** Retuning a block — a key, a slider, a
toggle — does not set `MachineUpdatedSinceLastSave`, so Besiege never writes a
version of it and there is nothing for a diff to find. `MapperWatch` compares the
block mapper's settings across an open and raises the game's own
`onMachineModified` if they differ. This is the one place the mod changes what
the game does rather than only looking at it, and it is deliberate: without it
"which blocks changed" cannot answer the most common kind of change.

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

**The overlay redraws itself after a level change.** The shells are parented into
the machine and a level load destroys it, so `GhostView` keeps the diff it drew
and `HistoryView.RedrawOverlay` puts it back once the new machine has blocks —
Besiege carries the machine across levels, so the diff is still true of it.
`GhostView.Lost` is what tells "destroyed under us" from "cleared".

**There is one colour per category, not two.** `DiffPalette` holds it and both
the list and the overlay read from there, so a count and the blocks it is
counting can never be written in different greens. The alpha is the only thing
that differs by use: shells honour it, text forces it to 1. The overlay keeps one
material per category so that dragging a slider recolours every shell on screen
with one assignment.

**A colour at zero opacity is off, not faint.** There are four categories and the
fourth is every block the save left alone, which on a large machine is nearly all
of them — so `GhostView` spawns nothing for a category whose alpha is 0 rather
than several hundred shells nobody can see. That makes a colour change able to
mean two different things, and `_faded` is what tells them apart: crossing zero
in either direction redraws that one category, anything else is still a material
assignment.

**Choosing machines lives outside the browser.** `Selection` is static and holds
paths, because the load screen destroys and rebuilds its slots whenever the page
or the folder changes — anything kept on a slot is gone the moment the player
goes looking for the second machine to compare. `SlotMark` is what one slot is
currently wearing, and `BrowserWatch.Reconcile` brings the two into line every
sweep rather than at the moment of a click. The window needs no telling that
these are machines rather than versions of one: a row is a file, a time and a
picture either way (`Selection.AsRows`).

**The diff never knew what "the previous version" was.** `BlockDiff.Compare`
takes two snapshots and has no opinion about where they came from or which way
round they are in time, which is what let the pin button be a field and a lookup
(`HistoryView._base`, `Baseline`) rather than a second code path. Loading a
version and working out its diff are separate for the same reason: pinning
re-answers the question about the version already on screen, and reloading it to
say so would throw the build area away and put back exactly what was there.

**A brace is not where its ghost is.** Braces, fuel lines and winches keep their
two ends in the block's data as `start-position` / `end-position`, in the block's
own local space; `GhostView.DrawSpan` puts a cylinder between them. Recognised by
those two keys rather than by block type, so a modded block that drags the same
way is drawn too. The reconstruction was checked against 1900 real endpoints —
see MODDING-NOTES.

**Preferences go through `Modding.Configuration`,** which is Besiege's own answer
to where a mod keeps data between restarts — an `XDataHolder` per mod, written to
`Besiege_Data/Mods/Config/GitView_<id>.xml`, the same file the loader already
keeps this mod's key binding in. The loader reads it at mod load and writes it on
quit. `Prefs` is the only thing that knows the keys; `DiffPalette` reads them the
first time a colour is asked for and the window reads its position when it is
built.

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

85 assertions cover the matcher, the tolerances, the quantisation, key bindings,
the timestamp parsing and the sorting. The mod compiles under Besiege's own compiler and
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

- **the block colors window.** Four colour sliders and four opacity sliders in a
  second UI Factory window, opened by the cog in the history window's title bar.
  Each colour slider is UI Factory's `Slider` with **both** its own bars turned
  off — the fill and the track — and our strip (`IconArt.Strip`) in their place,
  which is what Besiege's own colour slider is; see the notes for the two things
  that matter about the game's (pale picture, full-strength answer; a smooth hue
  ramp with no greys). It hides with the history window when the game puts a menu
  up, through `OptionsView.Allow`.
- **the arrow between the two machines being compared.** Three bars and a head in
  the scrolling content, down the strip to the left of the numbers, pointed by
  `PointArrow` off `Baseline` — so it says what the status line says. It is
  redrawn from `Restyle` and `RebuildRows`, which is every path that moves a row
  or changes what is compared with what.
- **the two drawn marks.** Both are the Clippy mod's, by request: the cog is that
  mod's settings mark (radii read off a screenshot of it) and the reset is its
  reload arrow (constant for constant out of its `UIBuilder`). Asking UI Factory
  for Besiege's own artwork was tried and taken out again — its bundle cannot be
  listed (`Make.Sprites` is not public, `Make.Sprite` only answers for a name you
  already know), so it amounted to guessing names.
- **that the title-bar marks land on their buttons.** Both windows' bar controls
  are UI Factory's `Icon Button` — the prefab the close cross itself is — with
  the sprite put on the child called `Icon`, squared against the bar's height by
  `UIBuild.SquareInBar`. If a mark is missing, that child is named something else
  now and the fallback tinted the wrong Image.

- **that the select button lands in the corner above the leftmost icon.**
  `AboveCorner` takes the bottom row of whatever the slot is showing and steps up
  from its leftmost button, rather than off one button found by name — the cloud
  it used to use is the *second* icon along, which is how it ended up over the
  middle of the row.

- **that stripping a ghost's behaviours silences the INTERSECTION warning.** The
  cause is not in doubt — `GhostTrigger.Update` is one of only four callers of
  `IntersectWarning`, and the other three are slider blocks — but that the strip
  happens early enough has not been watched.
- **that the mapper nudge produces a version.** Remap a key, change nothing else,
  wait out the sixty seconds, and a new row should appear with one changed block
  in it.
- **that the corner mark comes out in Besiege's font.** `IconArt.StampFont`
  copies the glyphs out of the font's own atlas — see MODDING-NOTES. If a number
  looks pixelled, the fallback ran: `Player.log` says why, unless the font itself
  was null, which is silent and means UI Factory had not loaded when the icon was
  drawn (that face is then not cached, so the next one should get it).
- **the compare-them-all button.** A whole copy of the rightmost load button with
  only `LoadSaveButton` and the localisation behaviours taken off it, so it keeps
  the plate, the swell and the tooltip that button already has — see the note in
  MODDING-NOTES for why stripping it down cost it all three. The plate is not part
  of the button, so `CopyPlate` copies that too and parents it where the original's
  is; `KeepPressable` forces the `SimpleUIButton` enabled every frame and watches
  for the press itself, because something switches that button off when the
  browser has nothing for it to act on.
- **the tooltip.** Besiege's own where one can be copied (`CopyTip` re-points the
  copy at a copy of its `tooltipParent`, which is what `Tooltip.Reset` then binds
  to), and a quad of ours carrying a picture of the words (`IconArt.Words`) where
  it cannot. In the second case `ShowTipOnHover` raycasts the button's own collider
  against the mouse every frame rather than using `SimpleUIButton`'s enter and exit
  events, which stop arriving the moment anything disables the collider.
- **where that button lands.** `RightOfRow` walks right from the rightmost
  `LoadSaveButton`, hopping to any active `SimpleUIButton` within two and a half
  widths on the same row, and puts ours one hop past the end. This replaced a
  pitch measured between the two `LoadSaveButton`s, which is not the pitch the
  row is drawn on — "load as selection" is not one of them — and left ours a
  button and a half out.
- **that a pinned row's circle reads as marked.** The circle is a picture inside
  the pin button rather than the button's own face, because tinting that did
  nothing in game — see the CustomMaterialHandler note in MODDING-NOTES.
  `BuildPin` switches the prefab's rounded-square face off, puts a disc behind
  (`IconArt.Disc`) and the ring over it, and `MarkPin` colours the ring.
- **that the frame around the chosen row clears its rounded corners.**
  `EdgeInset` is 3 against an assumed corner radius of about 5; if the corners
  poke out, raise it rather than thinning the bars.
- **that the preferences round trip.** The file to look at is
  `Besiege_Data/Mods/Config/GitView_90eacbcd-45ed-417d-8a3e-0456cbac0a59.xml`.
  Four Colors do appear there and did survive a restart; their **alpha did not**,
  because Besiege's Color XData has no alpha channel, so opacity now goes beside
  each one as a Single and the window position as two more. Those Singles, and
  `window-x` / `window-y` appearing at all, are what still wants looking at.
- **that a span is drawn where the brace is.** The arithmetic is checked offline
  against real files; that the cylinder lands on the machine rather than beside
  it has not been watched.
- **the default window position.** `WindowHome` was measured off a screenshot at
  3840x2160 — window box 106..1545 x 302..1422 in pixels, halved for the canvas,
  taken from the middle of the screen — and the anchors it is measured in are set
  by us rather than inherited, so it should hold. It has not been seen open.

Errors appear in `Player.log`, and in the in-game console with `show_logs true`.
