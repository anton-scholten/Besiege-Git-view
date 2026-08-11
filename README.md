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

In the load-machine screen, any machine with autosaves behind it gets an extra
button. Press it and the newest version opens, with a window beside it listing
every version of that machine:

| | saved | added | changed | removed |
| --- | --- | --- | --- | --- |
| *thumbnail* | 2026-06-27  15:42:38 | +11 | ~2 | — |
| *thumbnail* | 2026-06-27  15:41:38 | +7 | — | -8 |
| *thumbnail* | 2026-06-27  15:39:05  SAVED | +1 | ~8 | — |

Each row says what that save did to the machine compared with the save before
it. Every heading carries a pair of arrows: the lit one is the order in force,
so clicking a heading sorts by it and clicking again reverses it.

Click a row and that version loads, with its changes drawn over it:

- **green** — blocks this version added
- **orange** — blocks it moved, rotated, rescaled or retuned
- **red** — blocks it deleted, left standing where they used to be

`Ctrl+Y` hides and shows the window and its overlay. It also steps aside on its
own whenever the game puts up a menu — escape, load, options — and comes back
when the menu does, the same way Besiege's own block panel behaves.

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
