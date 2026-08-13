# Git View

See what changed between two versions of a machine, in Besiege.

Besiege already keeps a history of everything you build: a backup every minute
while you work, and another every time you save. It has kept one for every
machine in your `SavedMachines/AutoSave` folder for years. What it does not do
is tell you what is *in* those files — the built-in "versions" button drops you
in a folder of a hundred files called `aut 26.06.27 15-34-37` and leaves you to
guess.

This mod reads that history.

## What it does

In the load-machine screen, go into `AutoSave` — Besiege's own versions button on
a machine takes you there — and every machine's folder carries an extra button.
Press it and the newest version opens, with a window beside it listing every
version of that machine:

| version | | name | time | blocks | added | changed | removed |
| --- | --- | --- | --- | --- | --- | --- | --- |
| **9** | *thumbnail* | | 2026-06-27  15:42:38 | 151 | +11 | ~2 | — |
| **8** | *thumbnail* | | 2026-06-27  15:41:38 | 142 | +7 | — | -8 |
| **7** | *thumbnail* | SAVED | 2026-06-27  15:39:05 | 143 | +1 | ~8 | — |

Each row says what that save did to the machine — what it added, changed and
removed compared with the save before it — and how big the machine was at that
point. Every heading carries a pair of
arrows: the lit one is the order in force, so clicking a heading sorts by it and
clicking again reverses it. `version` sorts by the number, `name` by what the
machine is called and `time` by when it was saved — which are the same order for
one machine's history, and are not once the list holds machines you chose
yourself. `name` and `time` head the same column, because a machine whose name is
not just a timestamp shows that name on the first line and the timestamp under
it. A version out of an autosave folder is *only* a timestamp, so its first line
holds what its name says beyond the time — `SAVED` when you took that save
yourself rather than the timer — and the time itself stays on the line under the
`time` heading, in step with every other row. Machines the load screen cannot put a date to sort by name under `time`,
since "the order they happened to be in" is not an order anybody asked for. The first column is headed `selection` instead when the list is machines you
picked out by hand, since its number is then the order you picked them in — and
such a list opens in that order, most recently picked at the top, so each machine
is compared with the one you picked before it.

The number on the left is the version's place in the history — 1 is the oldest
and the largest is the newest. It belongs to the version rather than to the row,
so sorting by how much a save removed still leaves you able to see which version
is which, and that first heading puts the list back in that order. For
machines you chose yourself the number is the order you chose them in, which is
what the marks in the load screen said.

Those counts belong to the version too, not to where it currently sits: they are
what that save did, read once and then fixed, which is what makes `added`,
`changed` and `removed` columns you can actually sort by. They are read in the
background over the first few seconds — a row shows a dot until its numbers
arrive — so a heading clicked while the dots are still there sorts the list again
by itself once they are all in.

What *is* about the arrangement is the diff on screen: with nothing pinned, the
row you click is compared with **the row under it**. In the order the window opens
in, that is the save before it. Put any two rows next to each other, by sorting or
by pinning, and you can compare them.

Left of the number is a small circle, and pressing it pins that version as the
one everything is compared against — the source. Pin a version — its circle fills
red — and every version you click is
compared with *that* one instead, so you can ask what a whole afternoon of
building did rather than what one minute of it did. One version can be pinned, or
none; pressing the filled circle again lets go of it and the rows go back to
being compared with the row under them. Whichever version the diff on screen is
measured from is marked either way — filled red when you pinned it, an empty red
ring when it is simply the one that came before — so the far end of the
comparison is always something you can see. The status line at the bottom always
names what the version on screen was compared with.

The counts follow the pin: with a version pinned, every row says what that
version added, changed and removed **relative to the pinned one** rather than
relative to the save before it. They are recounted when you pin or unpin, which
takes about a second for a long history — the columns show a dot until each
row's new numbers arrive. The status line names what they were counted against.

Click a row and that version loads — the row it is on is outlined in red — with
its changes drawn over it:

- **green** — blocks this version added
- **orange** — blocks it moved, rotated, rescaled or retuned
- **red** — blocks it deleted, left standing where they used to be

What is drawn over a block is Besiege's own placement ghost of it — the
translucent preview it shows while you drag a block out of the menu — which is
already the right shape for that kind of block. Braces, hoses and ropes get a tube
along their length as well, since a dragged block is not where its ghost is. A
build surface is the exception: its ghost is a mark at one of its four corners,
so what is drawn over one is a copy of the mesh the machine generated for it,
curved edges and all, and a surface a save *deleted* is drawn as a flat slab
through the corners the file names.

A few block types have no ghost worth drawing — the drag panel is one. Those are
copied out of the machine itself where the block is still in it, and where it is
not, they borrow the look of another block of the same type. If there is no such
block either, a plain box marks the spot: a block counted as changed and pointed
at with nothing is the worst of the answers.

The window opens on its own top row with nothing pinned: the first row is loaded
and compared with the second, which is the same thing the counts beside it are
saying. So a machine's history opens on what its newest save did, and a handful of
machines opens on the difference between the last one you picked and the one
before. Anything wider is a pin away.

Both ends of a comparison are marked: the machine you clicked is framed in red,
the one it is being compared with is framed in the same red dashed, and an arrow
joins them — out of the filled circle of the one it is measured *from*, down the
outside of the list, and back in at the circle of the one you are looking at,
with a few heads down the run so that which way it goes is legible even when both
of its ends are off the top and bottom of a long list. It follows
the pin and the sort, so it always points at the pair the status line is
describing.

The cog in the window's title bar, beside the cross, opens **block colors**
alongside the list, tops level. At the top of it is `size`: how much larger than
its block each coloured shell is drawn, as a multiple of the block — 1 is the
block itself. The slider covers 0.9 to 1.3, which is where the answer is on any
ordinary machine, and the box beside it takes anything from 0.5 to 2 for a machine
that is not ordinary: half size to see a shell that is buried inside its block,
double to find one at all. Each shell grows about its own middle, so a block ends
up inside its mark rather than sliding out of one corner of it.
Under that are all
four at once — unchanged, added, changed, removed — each on the same colour slider
Besiege puts on a block, with its own opacity slider under it and a box beside
each for typing an exact value: `#RRGGBB` for the colour, a percentage for the
opacity. Whatever you choose
is used for both the counts in the list and the blocks over the machine — the text
always at full strength, the blocks at the opacity you set, so you can make a
change stand out against a dark machine or fade it back until you can see what is
underneath. The reset arrows in the corner of that window put every colour, and the size,
back.

Four colours rather than three: the first is everything the save left alone —
the blocks that were neither added, changed nor removed, which is most of what
the `blocks` column counts. It starts at no opacity, which is to say switched
off: it is most of the machine, and drawing it by default would bury the three
colours that answer the question. Give it an opacity and the changes have the
rest of the machine drawn around them, which is worth having when what changed is
buried inside a large build. At zero opacity it costs nothing — those blocks are
not drawn at all rather than drawn invisibly.

## Comparing two machines that are not versions of each other

The history window answers "what did this save do". The same window will compare
any two machines you point it at.

In the load screen every machine carries a small button in the corner of its
picture, above the leftmost of the icons along the bottom: the same branch as the
history button, with a **+** in the bottom-right corner, which is the one corner
the branch itself leaves empty. Press
it and that machine is picked out — the + becomes a **1**, the mark moves onto
the thumbnail at full size, and the picture behind it dims. Press the button on
another machine and it takes a **2**. Pressing a mark again puts that machine
back, and the numbers close up behind it.

Once two or more are picked, a compare button appears at the top of the screen,
to the right of the two load buttons: the same branch again, with no number on it
because it is about all of them. Hover it and it says how many machines it
will compare; press it and the load screen closes, and the history window opens
listing the machines you chose instead of the versions of one machine.
Everything in it works the same way from there: click a row to load that machine
and see what it changed, press a circle to pin that machine as the one
everything is compared against.

Autosave folders themselves cannot be picked — pressing the branch button on one
already means "every version of this machine". The versions inside one can be,
which is how you compare two versions that are not next to each other.

The colours and wherever you drag the window to are remembered — between
machines, between levels, and between one run of the game and the next. Neither
window can be dragged off the screen altogether: enough of the title bar is kept
in view to take hold of and pull it back.

The overlay goes when the machine does. Clearing the machine leaves the coloured
blocks hanging where it used to be, so that is noticed and they are dropped —
opening a *level* is not the same thing, and there the diff is drawn again over
the machine once it arrives.

`Ctrl+Y` hides and shows the window and its overlay. It also steps aside on its
own whenever the game puts up a menu — escape, load, options — and while you are
in the main menu or picking a level, and comes back when you do, the same way
Besiege's own block panel behaves. Opening a different level does not lose the
diff: the coloured blocks are drawn again over the machine once it arrives.

Retuning a block — remapping its keys, moving a slider — does not, on its own,
make Besiege take an autosave. This mod tells it to, so a change you cannot see
on the machine still gets a version of its own to show up in.

## Installing

It needs [UI Factory 3](https://steamcommunity.com/sharedfiles/filedetails/?id=2913469777)
(Workshop item `2913469777`), which is where its window comes from. Subscribe to
that and enable it first.

Then drop the `GitView` folder into `Besiege_Data/Mods/`, or from a clone:

```
./tools/install.sh          # symlink it into the game
./tools/install.sh --copy   # or copy it
```

The mod loads when you enter a level, the sandbox or the level editor, not at
startup. The editor is a build area like any other as far as this is concerned:
the same load screen, the same window. What it cannot do there is load a version
into an editor that has no machine in it — it says so rather than doing nothing.

## What counts as a change

Blocks are paired between two versions by the identifier Besiege writes into the
save file. That is right most of the time and quietly wrong the rest: Besiege
reissues a block's identifier when it is copied, mirrored, or restored by an
undo, and a diff that trusts it reports those as a block deleted and an
identical block added in the same place. So unpaired blocks get two more passes
— identical in every other respect, then same type and nearest position within
half a block — before anything is called an addition or a deletion.

Positions, rotations and block settings are compared with a tolerance, because
they are stored as decimal text and read back through a fast float parser, and
because some settings hold live physics values that come back a hair different
every save even when nobody has touched the machine.

A block is counted once. If the same identifier turns up as both an addition and
a change — which a machine holding two blocks with the same identifier can
produce, since Besiege reissues them — it counts as an addition, since "there is
something here that was not here before" is the larger fact.

Measured over six real machines and 153 version pairs, this agrees exactly with
an independent implementation of the same rules, and leaves about one
unexplained block per folder against nine per save for the identifier alone.

**Known gap:** a block whose *skin* changed may not be reported as changed. The
per-block skin is only resolved when a save is loaded for real, and the history
is read without doing that.

## Building

The mod ships a pre-built assembly. Building it needs no C# toolchain — it uses
Besiege's own compiler:

```
./tools/build.sh          # build the assembly
./tools/verify-build.sh   # compile without installing, after editing any .cs
./tools/run-tests.sh      # the headless tests
```

See [AGENTS.md](AGENTS.md) if you are changing it, and
[docs/MODDING-NOTES.md](docs/MODDING-NOTES.md) for the Besiege APIs it stands on.

## Licence

MIT. Besiege is Spiderling Studios'; nothing of theirs is redistributed here.
