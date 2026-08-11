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

### Retuning a block does not count as changing the machine

`MachineUpdatedSinceLastSave` is set by exactly one thing:
`ReferenceMaster.onMachineModified`, a plain `Action<Machine>` static field. Seven
places in the game raise it —

```
Machine.FinishDraggedBlocks   Machine.OnAnalyzeComplete
PlayerMachine.RemoveBlock     UndoSystem.PostUndoAction
AddPiece.AddBlockTypeNoSound  AddPiece.PostRemoveBlock
SymmetryController.AddSymBlocks
```

— and **none of them is in the block mapper**. Remap a key, drag a slider, flip a
toggle: the flag stays clear, the sixty-second timer finds nothing to do, and the
new setting is never written to a version at all.

It hides well, because a tuning session nearly always moves a block eventually
and the settings ride along with that save. It does not hide from a diff tool:
change only a block's keys, wait, and the folder has no new version in it.

`BlockMapper.onMapperOpen` and `onMapperClose` are public static `Action`s, and
`BlockMapper.Current` is the `SaveableDataHolder` being edited — its
`MapperTypes` are live, each with a `Serialize()` that gives the same `XData` the
save would. Fingerprint on open, compare on close, and raise
`onMachineModified` yourself if they differ. Raise the game's event rather than
setting the autosave's flag directly: everything else listening (centre of mass,
aerodynamics, the block counter) then hears what it would have heard anyway.

Order is on your side. `Open` sets `Current` before invoking `onMapperOpen`;
`Close` invokes `onMapperClose` before it tears anything down, and widgets apply
their values as they are changed, so both callbacks see the real thing.

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

### You cannot repaint a slot button by setting its texture

`Renderer.material.mainTexture = mine` on a copied slot button **does nothing
visible**. The copy keeps the icon it was cloned with, no error, no warning.
`mainTexture` writes `_MainTex`, and these buttons are drawn by one of Besiege's
own shaders (`Custom/Stencil/…` appears in `Player.log`) which need not have that
property or need not sample it — and there is no way to tell from outside.

Building your own quad instead does not work either, or at least not easily: the
second attempt at this parented a quad to the button's face and it came out
invisible. Which plane the face lies in, which way its normal points and how big
it is in its own local space all have to be right, and none of them can be
checked from outside a running game.

**A copied button has no face in the frame you copy it.** The third attempt
looked for a `MeshFilter` with a mesh on it and found none at all — on every
slot, every time. Whatever builds a slot button's visuals runs in an `Awake` or a
`Start` of its own, so the clone is bare in the frame `Instantiate` returns it
and furnished a frame or two later. Anything that repaints one has to be retried
across frames, and the log line that says "nothing to draw on" is what a
too-early look produces, not a button that draws nothing.

**A slot button's face is a `SpriteRenderer`.** That is the answer, arrived at by
logging the kind of every renderer on the clone. So the thing to set is
`spriteRenderer.sprite`, and no material or mesh comes into it.

Size it against the sprite it replaces. A sprite draws
`rect.height / pixelsPerUnit` units tall and `Sprite.Create` assumes 100 pixels
per unit unless told otherwise, so a texture that is not the size of Besiege's
comes out the wrong size on screen — half the trash can beside it, in this case.
Ask the old sprite for its world height and pick a density that matches it:

```csharp
float worldHeight = replacing.rect.height / replacing.pixelsPerUnit;
float density     = myTexture.height / worldHeight;
Sprite.Create(myTexture, rect, pivot, density);
```

For anything that is *not* a sprite, **assign a whole new material to the
renderer**. The geometry is then Besiege's — right plane, right winding, right
size — and the only thing that changed is the material, which is yours and whose
shader you chose:

```csharp
face.sharedMaterials = new Material[] { mine };   // not material.mainTexture
```

Guard one thing: a UI mesh's texture coordinates may point into a shared atlas
rather than spanning 0..1, and a quad cut out for a trash can would then show
that same corner of your texture. Copy the mesh and remap its `uv` from vertex
position within the mesh bounds — over the two axes the bounds actually have a
size in, so it works whichever plane the quad lies in. `Mesh.isReadable` says
whether that is possible at all; keep the original when it is not.

`Shader.Find` only finds shaders included in the player's build. Both
`Unlit/Transparent` and `Particles/Alpha Blended` are confirmed present in
Besiege's.

If you do assign a material anyway, go through `Renderer.material`, never
`sharedMaterial`: the original is shared with the button it was copied from and
with every other slot's copy of it, so writing to it puts your icon on the game's
own buttons.

### A slot has nine buttons and shows one

Measured on a real folder slot: nine `SimpleUIButton`s, of which exactly **one**
is active — the delete button, in a bottom corner at x = 1.2 in slot units. The
other eight (upload, cloud sync, confirm-delete, and so on) are inactive and
scattered, two of them 0.018 apart.

So "the smallest gap between any two buttons" is not a usable pitch, and any
scan for a free spot has to filter on `activeInHierarchy` first or it will find
the slot entirely full. With one visible button in a corner, its mirror through
the slot's middle is the obvious place for a second.

## Looking like a Besiege panel

Four things carry most of the resemblance, and all four can be taken rather than
approximated:

- **Spawn UI Factory's `Text` prefab for every label**, not a bare uGUI `Text`.
  It brings the game's font and its letter spacing with it, which is most of the
  difference between a panel that looks like Besiege's and one that looks like a
  mod's.
- **Rows are separate buttons, not a banded table.** Besiege's own panels are a
  stack of blocks with a margin either side and a gap between them. Edge-to-edge
  rows read as a spreadsheet.
- **The selected one goes solid red**, `Besiege.UI.Consts.C_BG_RED` =
  `(0.92, 0.13, 0.29)`, with white text on it. That is what the block panel does
  to the option in force. Give up any other text colours on that row; they are
  not worth reading on red.
- **Capitals.** Besiege writes its interface in them.

The rest of the palette, for reference:

| Constant | Value |
| --- | --- |
| `C_BG_BLACK` | `(0.03, 0.03, 0.044, 0.2)` |
| `C_BG_RED` | `(0.92, 0.13, 0.29)` |
| `C_BG_INPUT_FIELD` | `(0, 0, 0, 0.549)` |
| `C_BG_TOOLTIP` | `(0.106, 0.114, 0.132)` — the one opaque colour |
| `C_RESET` | `(0.012, 1, 0.847, 0.5)` — the cyan |
| `C_BG_SCROLLBAR` | `(1, 1, 1, 0.313)` |
| `C_BG_SCROLLBAR_BACK` | `(0.046, 0.048, 0.058, 0.414)` |

### The hover swell drags text sideways

UIFactory's buttons scale about their pivot when the pointer is over them, and
they are pivoted in the middle. On a control as wide as a table row that is very
visible: a left-aligned label slides left, a right-aligned one slides right, and
it reads as the text jumping rather than as the row lighting up. Pin the pivot to
whichever edge the text is aligned to and the swell happens entirely on the other
side. Moving a pivot moves the rect, so put the insets back afterwards.

`ScaleAnimation.Target` is public, so it can be checked; `ButtonHoverScale` and
`ButtonPressedScale` are not, so how much it swells is not yours to tune — only
where it grows from. For the record they default to **1.15** and **0.85**, which
is 15% of the control's whole width carried sideways.

A pivot only saves one edge, and a table row has text at both. Past a certain
width the swell is simply the wrong animation: switch it off — `scale.enabled =
false`, since a disabled behaviour is not a valid handler as far as uGUI's event
system is concerned, so it is never told the pointer arrived — and say the same
thing with colour instead. `Button.transition = ColorTint` with `targetGraphic`
pointed at a background image of your own gives a hover that costs nothing per
frame and moves nothing. Note that uGUI then drives that image's colour on the
canvas renderer, so the image's own `color` stops mattering and every state has
to be set in the `ColorBlock`.

### Centre a column's contents; do not try to align them to its edge

If the rows are inset inside a margin and the heading strip is not, their column
fractions are fractions of different widths and every heading sits a few pixels
off the values under it. That much is arithmetic and can be fixed.

What cannot be fixed by arithmetic is where an *edge-aligned* label ends up
inside a `Text Button`. Its label sits somewhere down a hierarchy of the prefab's
own, so stretching "the label" insets it inside whatever container it happens to
be in rather than inside the button, and the heading lands some unpredictable
distance off its column — twenty pixels, in this mod, with no way to say why from
outside.

Centre both the heading and the values instead. A centred label only needs its
container to be centred within the button, which it is, so it is indifferent to
the whole question. It also happens to be what Besiege does with its own button
labels.

### A colour transition multiplies; it does not replace

`Button.transition = ColorTint` drives the state's colour onto the target
graphic's **canvas renderer**, and that is multiplied by the graphic's own
`color` at draw time. Point it at an image you created transparent — the obvious
thing to do for a highlight that starts off — and it stays invisible in every
state, silently. Create the image opaque white and let the transition supply the
colour.

### UI Factory has no colour picker, and Besiege's is out of reach

The nineteen prefabs are `Empty`, `Icon`, `Text`, `Text Button`, `Text Toggle`,
`Text Dropdown`, `Icon Button`, `Icon Toggle`, `Button Dropdown`, `Input Field`,
`Slider`, `Options`, `Scroll View`, `Blur`, `Panel`, `Mask`, `Window`, two
tooltips, plus `WorldCanvas` and `KeymapCanvas`. Besiege's own picker is the
block mapper's paint selector, behind `InternalModding`, and only opens for a
block. Four `Slider`s in a `Panel` is the honest answer.

Watch the name: **Besiege has its own `Slider` in the global namespace**, and it
is the one an unqualified `Slider` binds to inside this mod. Write
`UnityEngine.UI.Slider` in full.

### Whether a prefab's label is the prefab

`Text Button`'s label is a child, authored at a fixed width for the prefab's own
size, so it has to be stretched to whatever you resized the control to. `Text`'s
label **is** the prefab — and stretching that throws away wherever you just
placed it. Same call, opposite treatment, and the failure is silent: the status
line in this mod ended up anchored across the middle of the window, looking for
all the world like a placement bug. Check whether the `Text` you found is on the
root before touching its rect.

## Knowing when the game has a menu up

`StatMaster.inMenu` is a public static bool property, and `StatMaster.hudHidden`
a public static field for the player having hidden the interface. There is a
`StatMaster.inMenuChanged` Action beside it, but it is a plain static, so
subscribing means remembering to unsubscribe, and getting that wrong leaves a
destroyed object being called into; polling two statics per frame beats being
wrong about that.

Anything drawn over the build area should check them. Besiege's own block mapper
steps aside when a menu opens rather than floating over it, and a mod panel that
does not looks broken by comparison. Keep the player's own open/closed answer
separate from the suppression, or a menu opening and closing undoes a panel they
had deliberately hidden.

`StatMaster` is not in the stable `Modding` namespace, so guard the read and let
a failure mean "no menu": a panel that fails to hide is a great deal better than
one that never appears.

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
block out of the menu. Every block type has one and it is already the right
shape, which makes it the right thing to build a diff overlay out of.

**It is not inert.** A ghost carries `GhostTrigger` — and on some blocks
`GhostPinTrigger` — the behaviours that turn the preview red inside something and
call `IntersectWarning.WarningFromWorldPos`, which is the game's INTERSECTION
banner. They work off trigger colliders. Every ghost a diff overlay draws sits
exactly on a block of the machine, so an overlay of a dozen blocks raises a dozen
intersection warnings the instant it appears.

Instantiate it, `SetActive(false)` before anything on it gets a frame, then strip
every `MonoBehaviour`, `Collider` and `Rigidbody` out of the hierarchy and turn it
back on. Disable each behaviour as well as destroying it: `Destroy` takes effect
at the end of the frame, and one `Update` in between is one warning on screen. The alternative,
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
