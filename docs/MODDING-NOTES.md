# Besiege modding notes

What this mod stands on, read out of the game's own assemblies
(`Besiege_Data/Managed/`) with Mono.Cecil rather than from documentation. Every
member named here was confirmed to exist. Add to this when you learn something
the hard way.

Target: Besiege on Unity **5.4.0f3**, built-in mod loader. The sibling repos
(Clippy, AI Link) carry longer notes on the loader, the blacklist and the UI;
this file is about the parts specific to machine history.

## Besiege's own autosave

`AutoSave.MachineAutosaveController` is the base game's, not a mod's. It writes
to `StaticSettings.MachineAutosavePath`, which is `SavedMachines/AutoSave`:

```
SavedMachines/AutoSave/<machine name>/aut yy.MM.dd HH-mm-ss.bsg
SavedMachines/AutoSave/<machine name>/ver yy.MM.dd HH-mm-ss.bsg
SavedMachines/AutoSave/<machine name>/Thumbnails/<same name>.png
```

- `aut` is the timer. `AutosaveIntervalSeconds` is **60**, and a save is only
  taken if the machine changed (`MachineUpdatedSinceLastSave`).
- `ver` is `VersionMachine`, called when you save over an existing machine: the
  file that is about to be overwritten is copied here first. Gated on
  `OptionsMaster.BesiegeConfig.SavePreviousVersionsEnabled`, and skipped
  entirely for machines already inside the AutoSave folder.
- Both are pruned, by count and by age (`PruneFileCount`, `PruneOldFiles`).
- Thumbnails are 512x512 PNGs — a megabyte each as a texture, which is why the
  history window only ever holds the ones on screen.
- The machine's own name is in the file too, as `<String key="AutoSave">` in the
  machine's `Data`, which is `MachineAutosaveController.DATA_KEY`.

The two kinds interleave in time and are the same thing to a diff: a snapshot
with a time on it. Sorting them into one timeline is the only way the counts
mean anything, since a manual save lands between two timed ones.

## The load screen already has a versions button

`FileBrowserSlot.SetupVersionsButton` shows one when
`Directory.Exists(MachineAutosavePath/<name>)`, and `OnVersionsButtonClick` ends
in `FileBrowserView.OnPageViewSlotVersions`, which walks up to the collection
root, finds the `AutoSave` folder, opens it, finds the folder named after the
machine and opens *that* in the browser. So it is a shortcut into the folder,
nothing more.

That method is also the documented route to the folder, and this mod copies it
rather than joining `MachineAutosavePath` to a name: the browser can be showing
local files, a Steam collection or mod.io, and only the `IVirtualObject` it hands
you knows which.

## The file browser is mesh UI, not uGUI

Slots are world-space mesh objects. `FileBrowserSlot` extends
`HoverableClickBehaviour`; its labels are `TextMesh`, and its buttons are
`SimpleUIButton` with colliders. There is no layout group to add a child to and
no prefab of Besiege's to instantiate, so a mod that wants a button there has to
copy one of the slot's own.

What is public and useful:

| Member | Notes |
| --- | --- |
| `FileBrowserSlot.VirtualObject` | the file or folder the slot stands for |
| `FileBrowserSlot.VersionsClicked` etc. | `Action<FileBrowserSlot>` fields, assignable |
| `SimpleUIButton.ResetDelegates()` | clears every handler on a copied button |
| `SimpleUIButton.Click` | an **event whose type is also called `Click`** |
| `FileBrowserView.Close()`, `.IsOpen` | closing the screen from outside |

The private fields (`versionsButton`, `deleteButton`, …) are not reachable —
reflection is blacklisted — so which button is which cannot be asked. This mod
copies whichever `SimpleUIButton` is active and has a renderer, and works out
where to put the copy by reading the spacing of the slot's existing buttons.

`SimpleUIButton.Click` is worth flagging. A *member named after its own type* is
the construct that sends Besiege's in-game compiler into infinite recursion and
kills the game — but that is about **declaring** one. Referring to somebody
else's, as `button.Click += new Click(handler)`, compiles fine, including under
Besiege's own compiler.

Instantiating a copy brings the prefab's serialised state but not its delegates,
since C# delegates are not serialised — so a copied delete button does not
delete anything. `ResetDelegates()` anyway, against a future Besiege that wires
them up differently.

Assign the icon through `Renderer.material`, never `sharedMaterial`: the
original is shared with the button it was copied from and with every other
slot's copy of it, so writing to it puts your icon on the game's own buttons.

## Reading a .bsg without being allowed to read files

The mod loader refuses any assembly that references `System.Xml` at all, or
`System.IO`'s file classes (`Path` and the stream types are the carve-outs). So a
mod cannot open a saved machine, let alone parse one. Three public statics do it
for you:

| Call | Gives |
| --- | --- |
| `XmlLoader.LoadFromFullPath(path, dummyLoad, auth)` | a `MachineInfo` |
| `Besiege.AssetImporter.LoadTexture(path, mipmap, nonReadable)` | a `Texture2D` |
| `VirtualFolder.GetObjects()` | the directory listing, with dates and thumbnails |

`auth` is the machine's author; the game passes `string.Empty` from
`XmlLoader.Load`, and so does this mod. `dummyLoad` is true for reading history
and false for the version actually being loaded.

`LoadTexture` builds a fresh `Texture2D` per call and caches nothing, so what it
returns is yours to `Destroy` — which is what makes it safe for the history
window to drop thumbnails as they scroll out of view.

A `VirtualFolder` does not list its contents until `Open()` is called on it, so
every step down a path needs that. Opening one is a re-read of the directory, not
a navigation: the browser stays where the player left it.

## MachineInfo and BlockInfo

`MachineInfo.Blocks` is a `List<BlockInfo>`, and a `BlockInfo` carries `Guid`,
`ID` (a `BlockType`), `Position`, `Rotation`, `Scale`, `Flipped`, `Skin` and
`BlockData`.

**The guid is per block, not per block type** — two identical girders have
different ones — which makes it the obvious key for "the same block, one save
later". It is not stable, though. Over six real machines it moves for a handful
of blocks per save, on blocks that are otherwise untouched; copying, mirroring
and undo all appear to reissue it. See `BlockDiff` for what this mod does about
that.

`BlockData` is an `XDataHolder`. Read it with `ReadAll()` and each `XData`'s
`Key`, `Type` and `RawValue`:

- **Do not use `XDataHolder.Encode`.** It carries session flags —
  `WasLoadedFromFile`, `WasSimulationStarted` — which would make every block in
  every version read as changed.
- **Do not use `XData.Encode` either**, tempting as an exact digest is. Some
  settings hold live physics values: a piston's `start-position` came back as
  `5.96047E-08` one minute and `2.842171E-14` the next in a real autosave
  folder, with nobody having touched the machine. Comparing bytes calls that a
  change. Go through `RawValue` so the numbers can be rounded first.
- Test `RawValue` with `is`, never `GetType().Name`. `Type.Name` is
  `System.Reflection.MemberInfo.get_Name`, and one reference to it is enough for
  the loader to refuse the whole assembly. `is` compiles to `isinst`.

The per-block skin type is the nested `BlockSkinLoader.SkinPack.Skin`, with a
`path` and an `isDefault`. Whether it is resolved at all depends on how the file
was loaded, which is why this mod treats an unresolved skin as "no skin" rather
than guessing.

## Drawing a ghost block

`PrefabMaster.GetPrefab(BlockType, out BlockPrefab)` gives a `BlockPrefab`, and
`BlockPrefab.ghost` is the translucent preview Besiege shows while you drag a
block out of the menu. Every block type has one; it is already the right shape
and already visual-only — no colliders, no physics, nothing the game will
mistake for part of the machine.

That makes it the right thing to build a diff overlay out of. The alternative,
tinting the real blocks' renderers, does not work: Besiege's blocks share their
materials, so tinting one girder tints every girder.

Saved block coordinates are relative to whatever the machine's blocks are
parented to. The field holding that on `Machine` is not public, so take it off a
block — `machine.BuildingBlocks[0].transform.parent` — and fall back to
`machine.transform` for an empty machine. Ghosts parented there are plain
GameObjects with no `BlockBehaviour`, and saving walks `BuildingBlocks`, so they
cannot end up in a save.

`Shader.Find` only finds shaders that were included in the player's build, so
try several and have a fallback. `Particles/Alpha Blended` is the first choice
here.

## Loading a version

`Machine.Active().LoadMachineInfo(info, resetUndoActions)` is the same call the
load screen makes, so joints, clusters and physics are worked out exactly as they
are for any other machine and an interrupted load cannot leave half a machine
behind.

It does not finish in the frame it is called. `Machine.IsLoadingMachine` is
public; wait for it before touching anything that hangs off the machine's blocks,
because every one of them has just been destroyed and rebuilt.

## Compiler hazards, in brief

The full write-ups are in the sibling repos. The ones that matter here:

1. **Any `enum` declaration segfaults Besiege's compiler**, which is the compiler
   `tools/build.sh` uses. Use `int` constants — `RowSort` and `BlockDiff` do.
2. **Never name a member after its own type.** Legal C#, infinite recursion in
   this compiler, and the game dies with a SIGABRT during mod loading.
3. **Besiege bundles the mod.io SDK**, whose global `ModIO` namespace shadows
   `Modding.ModIO`. Fully qualify every `Modding` type.
4. **Besiege shadows four Unity types** in the global namespace:
   `UnityEngine.UI.Slider`, `Scrollbar`, `LOD` and `Particle`. Write those in
   full. `Text`, `Image`, `Button`, `RawImage`, `ScrollRect` are all safe.
5. Write C# 4: no interpolated strings, no `?.`, no `nameof`.

`tools/build.sh` runs the blacklist scan over every build, so a forbidden member
fails the build rather than making the mod silently not appear in game.
