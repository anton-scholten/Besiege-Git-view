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

| source | | time | name | blocks | added | changed | removed |
| --- | --- | --- | --- | --- | --- | --- | --- |
| **9** | *thumbnail* | 2026-06-27  15:42:38 | | 151 | +11 | ~2 | — |
| **8** | *thumbnail* | 2026-06-27  15:41:38 | | 142 | +7 | — | -8 |
| **7** | *thumbnail* | 2026-06-27  15:39:05  SAVED | | 143 | +1 | ~8 | — |

Each row says what that save did to the machine compared with the save before
it, and how big the machine was at that point. Every heading carries a pair of
arrows: the lit one is the order in force, so clicking a heading sorts by it and
clicking again reverses it. `source` sorts by the number, `time` by when the
machine was saved and `name` by what it is called — which are the same order for
one machine's history, and are not once the list holds machines you chose
yourself. `time` and `name` head the same column, because a machine whose name is
not just a timestamp shows that name on the first line and the timestamp under
it.

The number on the left is the version's place in the history — 1 is the oldest
and the largest is the newest. It belongs to the version rather than to the row,
so sorting by how much a save removed still leaves you able to see what came
before what, and the `source` heading puts the list back in that order. For
machines you chose yourself the number is the order you chose them in, which is
what the marks in the load screen said.

The number itself is a button, and pressing it pins that version as the one
everything is compared against — the source. Normally a row is compared with the
save before it, which is what you want for reading a history a minute at a time.
Pin a version — its number turns red — and every version you click is compared
with *that* one instead, so you can ask what a whole afternoon of building did
rather than what one minute of it did. One version can be pinned, or none;
pressing the pinned number again unpins it and the rows go back to being
compared with the save before. The status line at the bottom always names what
the version on screen was compared with.

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

Braces, fuel lines and winches are drawn along their whole length rather than
just at the end they are anchored to, since where the far end is is most of what
a brace *is*.

The block of colour beside each heading opens a picker for it: red, green, blue
and an opacity, each with a slider to drag and a box to type an exact number
into — 0 to 255 for the colours, 0 to 100 for the opacity.
Whatever you choose is used for both the counts
in the list and the blocks over the machine — the text always at full strength,
the blocks at the opacity you set, so you can make a change stand out against a
dark machine or fade it back until you can see what is underneath. `RESET` puts
one colour back, and clicking anywhere outside the picker puts it away.

The fourth picker, at the head of the `blocks` column, is for everything the save
left alone — the blocks that were neither added, changed nor removed, which is
most of what that column counts. It starts at no
opacity, which is to say switched off: it is most of the machine, and drawing it
by default would bury the three colours that answer the question. Give it an
opacity and the changes have the rest of the machine drawn around them, which is
worth having when what changed is buried inside a large build. At zero opacity
it costs nothing — those blocks are not drawn at all rather than drawn
invisibly.

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
and see what it changed, press a number to pin that machine as the one
everything is compared against.

Autosave folders themselves cannot be picked — pressing the branch button on one
already means "every version of this machine". The versions inside one can be,
which is how you compare two versions that are not next to each other.

The colours and wherever you drag the window to are remembered — between
machines, between levels, and between one run of the game and the next.

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

The mod loads when you enter a level or the sandbox, not at startup.

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
