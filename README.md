# Git View

See what changed between two versions of a machine, in [Besiege](https://store.steampowered.com/app/346010/Besiege/).

Besiege has kept a backup of your machine every minute and every save for years,
in `SavedMachines/AutoSave`. Its own versions button drops you in a folder of a
hundred files called `aut 26.06.27 15-34-37` and leaves you to guess. This mod
reads that history and shows you the blocks that changed.

**Requires [UI Factory](https://steamcommunity.com/sharedfiles/filedetails/?id=2913469777)**
(another Besiege mod which enables the nice UI, see workshop item `2913469777`) or the mod won't load.

## Install

Either subscribe to the mod on Steam, or if you don't use Steam you can clone the repo then:

```sh
./tools/install.sh              # symlink into Besiege_Data/Mods
./tools/install.sh --copy       # copy instead
./tools/install.sh --uninstall
```

Set `BESIEGE_DIR` if your install isn't found automatically. Start Besiege, enable
**GitView** in the mods menu, and enter a level, the sandbox or the level editor.

## Opening a history

In the load screen, go into `AutoSave` — Besiege's own versions button on a machine
takes you there — and every machine's folder carries a branch button. Press it and
the newest version loads with the history beside it:

| version | | name | time | blocks | added | changed | removed |
| --- | --- | --- | --- | --- | --- | --- | --- |
| **9** | *thumb* | | 2026-06-27  15:42:38 | 151 | +11 | ~2 | — |
| **8** | *thumb* | | 2026-06-27  15:41:38 | 142 | +7 | — | -8 |
| **7** | *thumb* | SAVED | 2026-06-27  15:39:05 | 143 | +1 | ~8 | — |

Click a row and that version loads, framed in red, with its changes drawn over the
machine: **green** added, **orange** moved or retuned, **red** deleted and left
standing where they were.

| Action | Control |
| --- | --- |
| Show what a version changed | click its row |
| Compare everything against one version | click the circle left of its number |
| Let go of it | click the filled circle again |
| Sort | click a heading; again to reverse |
| Block colors | cog in the title bar |
| Move the window | drag its title bar, or the strip under the list |
| Show / hide the window | `Ctrl+Y` |

## Reading the list

Each row is what that save did **compared with the row under it** — which in the
order the window opens in is the save before it. Put any two rows next to each
other, by sorting or by pinning, and you can compare them.

The number is the version's place in the history, 1 being the oldest. It belongs
to the version rather than the row, and so do the counts: they are read once and
fixed, which is what makes `added`, `changed` and `removed` sortable. They arrive
over the first few seconds — a row shows a dot until its numbers land.

`name` and `time` head the same column: an autosave is only a timestamp, so its
first line holds whatever the name says beyond the time (`SAVED` when you took
that save yourself) and the time stays on the second.

**Pinning.** The circle left of a number pins that version as the one everything
is compared against, so you can ask what an afternoon of building did rather than
what one minute of it did. Every count in the list is recounted against it. The
circle of whichever version the diff is measured from is always marked — filled
red when you pinned it, an empty red ring when it is simply the row below — and an
arrow joins the two ends of the comparison down the side of the list.

## Block colors

The cog opens the colours beside the list.

**Size** at the top is how much larger than its block each coloured shell is drawn,
so a block sits inside its mark. Then all four colours at once — unchanged, added,
changed, removed — each on Besiege's own colour slider with an opacity under it and
a box for typing an exact value (`#RRGGBB`, or a percentage).

Unchanged is the fourth colour: everything the save left alone. It starts at no
opacity, since it is most of the machine, and costs nothing until you give it one.

The size slider is clipped but out-of-range values can be typed: it slides 0.9–1.3×,
which is where the answer is on an ordinary machine, and takes anything from 0.5 to
2 for one that is not — half size to see a shell buried inside its block, double to
find one at all.

The reset arrow puts every colour and the size back. The colours, and where you
dragged the window to, are remembered between sessions.

## Comparing two machines that are not versions of each other

Every machine in the load screen carries a small **+** branch in the corner of its
picture. Press it and that machine is picked out — the + becomes a **1**, the mark
moves onto the thumbnail and the picture dims. Press it on another and it takes a
**2**.

With two or more picked, a compare button appears at the top of the screen beside
the load buttons. Press it and the history window opens listing the machines you
chose instead of the versions of one machine; everything in it works the same way.

Autosave folders cannot be picked — the branch on one already means "every version
of this machine". The versions inside one can be, which is how you compare two
versions that are not next to each other.

## What counts as a change

Blocks are paired by the identifier Besiege writes into the save file. That is
right most of the time and quietly wrong the rest — the game reissues an identifier
when a block is copied, mirrored or restored by an undo — so unpaired blocks get
two more passes, identical in every other respect and then same type within half a
block, before anything is called an addition or a deletion.

Positions, rotations and settings are compared with a tolerance: they are stored as
decimal text, and some settings hold live physics values that come back a hair
different every save. A block counted as both added and changed counts as added.

**Known gap:** a block whose *skin* changed may not be reported as changed. Skins
are only resolved when a save is loaded for real, and the history is read without
doing that.

## Notes

What is drawn over a block is Besiege's own placement ghost of it, so it is already
the right shape. Braces, hoses and ropes get a tube along their length as well; a
build surface is copied from the machine's own mesh, curved edges and all. The few
block types with no usable ghost — the drag panel is one — are copied from the block
itself, or from another block of the same type, or marked with a plain box.

The window steps aside whenever the game puts a menu up and comes back with it, the
way Besiege's own block panel behaves. Clearing the machine drops the overlay;
opening a level does not, and the diff is drawn again once the machine arrives.

Nothing behind the window can be clicked through it. Besiege's own popups and
buttons are not the same kind of interface as this one and do not know they are
covered, so while the pointer is over the window the game is told to ignore it.

Retuning a block does not, on its own, make Besiege take an autosave. This mod tells
it to, so a change you cannot see on the machine still gets a version to show up in.

Details land in `Player.log` and in the in-game console with `show_logs true`.

AI agent? see [AGENTS.md](AGENTS.md) for layout, build, and any relevant info.
[docs/MODDING-NOTES.md](docs/MODDING-NOTES.md) has some info on Besiege's modding API.

## Licence

MIT. Besiege is Spiderling Studios'; nothing of theirs is redistributed here.
