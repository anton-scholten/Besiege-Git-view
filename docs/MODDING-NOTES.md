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

### Every field on a slot is private

`FileBrowserSlot` names each of its buttons in a field — `cloudButton`,
`loadAsSelectionButton`, `deleteButton`, `versionsButton` — and the file name in
`fileNameTextMesh`. **None of them is reachable from a mod.** Only the two
properties are: `VirtualObject` and `Thumbnail`. So anything that wants to sit
beside one of those buttons has to find it another way: by name
(`GetComponentsInChildren<SimpleUIButton>` and match the GameObject's name), or
by geometry, with a fallback for when the name changes under you.

`LoadSaveButton` up in the browser's top bar is the same story: it keeps the
renderer its icon is drawn on in `buttonMeshRenderer`, private, so which of the
things a button draws is the picture has to be guessed. The size is a good guess
— a button is an icon on a plate and the plate is never the smaller of the two —
and repainting the smallest renderer instead of repainting one and turning the
rest off is what keeps the plate under a replaced icon.

`FileBrowserSlotThumbnail.ApplyTexture` is not public either. What *is* reachable
is the material behind it: `ThumbnailComponent` takes it from
`Renderer.material`, which instances one per renderer, so a slot's thumbnail
material is that slot's alone and setting `mainTexture` on it is exactly what
`ApplyTexture` does. The browser writes to the same material when it redraws the
slot, so it takes its picture back by itself — and `IVirtualObject.Thumbnail`,
the browser's own cache of that picture, is untouched by any of it and is
public both ways.

### `IVirtualObject.Date` is not an OLE automation date

It is a `double`, and the obvious reading of a `double` date in .NET is
`DateTime.FromOADate`. That reading is wrong, and it fails in the one way that is
hard to notice: `FromOADate` **throws** above the year 9999, so every real value
is out of range, every catch hands back `DateTime.MinValue`, and the date simply
disappears rather than coming out shifted.

What the number actually counts, from `VirtualFile..ctor`:

```
Date = StaticSettings.GetTimestamp(File.GetLastWriteTimeUtc(path))
     = (thatTime - StaticSettings.GetRefDateTime()).TotalSeconds
     = seconds since 2014-01-01T00:00:00Z
```

`GetRefDateTime` is a literal `new DateTime(2014, 1, 1, 0, 0, 0, DateTimeKind.Utc)`.
So the conversion back is `new DateTime(2014,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(date).ToLocalTime()`
— local, because the timestamps in Besiege's own file names are local wall clock,
and the two kinds of row have to be comparable. `VersionEntry.FromTimestamp`.

### …and on Linux it is always `DateTime.Now` anyway

Read the constructor again. That last-write-time branch is an `else`:

```
if (ObjectPath.IsRoot || ObjectPath.IsChildOf(FileSystemPath.Root))
    Date = GetTimestamp(DateTime.Now);          // <- this one
else if (IOHelper.FileOrFolderExists(...))
    Date = GetTimestamp(File.GetLastWriteTimeUtc(...));
```

`FileSystemPath.Root` is built in the static constructor as `new FileSystemPath("/")`,
and `IsChildOf` is `Root.IsParentOf(this)`, which is `this.Path.StartsWith("/")`.
**Every absolute path on Linux or macOS starts with "/"**, so every file in the
browser takes the first branch and is stamped with the moment the folder was
listed. On Windows the paths start `C:/` and the dates are real. The game's own
"sort by date" in the load screen is equally affected; this is Besiege's bug, not
the mod's.

There is no way round it from a mod: `System.IO.File` is blacklisted, `IOHelper`
exposes only existence checks, and `Modding.ModIO` — which is otherwise a complete
file API — has no timestamp call either. What Besiege *has* written down is the
time in the name of every autosave, so `VersionScan.LastSaved` takes a machine's
date from the newest file in its autosave folder, and rows with no autosave show
no time at all rather than a made-up one.

### Writing in Besiege's font without a Text or a TextMesh

A generated icon can carry the game's own lettering: `UIF.Font` (UI Factory's
`Besiege.UI.Make.Font`, the font the windows are written in) →
`RequestCharactersInTexture(text, points, style)` → `GetCharacterInfo` per
character → copy the glyph out of `font.material.mainTexture` into your own
pixels. The atlas is very often unreadable, so it comes back through the same
`RenderTexture` + `ReadPixels` route as a thumbnail; the coverage is in the
**alpha** channel whether the atlas is Alpha8 or ARGB32.

Two things to check, both of which mean falling back to something you drew
yourself: `GetCharacterInfo` returning false for a character the font has no
glyph for, and a glyph packed into the atlas rotated, which shows up as
`uvBottomLeft.x != uvTopLeft.x`.

Fit the glyphs' *ink* box (`minX`/`maxX`/`minY`/`maxY` around the pen positions),
not the line box. Line boxes carry the font's leading, so a "1" fitted by its
line box comes out visibly smaller than a "4" fitted the same way.

A `TextMesh` parented to the icon is the obvious alternative and is worse: it is a
second object to place, scale, hide and destroy in step with the first, and its
size in world units cannot be known until it has been rendered.

### A copied button must be stripped to its `SimpleUIButton`, and then it is small

Leave the other behaviours on a copied `LoadSaveButton` and it stops responding to
the mouse entirely — no click, no hover. Something on that button turns itself off
when the browser has nothing for it to act on, and `SimpleUIButton.ToggleButton`
disables **the collider** as well as the behaviour. Besiege's buttons are driven by
Unity's mouse messages (`ClickBehaviour.OnMouseOver` → `OnCursorOver` → `OnClicked`,
which also returns early on `!enabled`), so a button with no collider receives
nothing at all. With `LoadSaveButton` destroyed there is nothing left to switch it
back on.

Stripping it works and costs you the button: one of the behaviours you destroyed
is what enables the plate renderer under the icon, so the copy draws its icon and
nothing else — about three fifths the height of the buttons beside it, measured
on screen — and the tooltip goes with it. Both then have to be imitated.

**Keeping them is still the better trade** — it is what makes the tooltip below
possible — but keep your expectations low about what it buys:

- **The plate is not the button's.** Copy a load button and you get a bare glyph
  about three fifths the height of the buttons beside it, components or no
  components: the dark square behind it belongs to the row rather than to the
  control, and stays behind, exactly as the tooltip's words do.
  **Copy the plate too** — find it as whatever is drawn *around* the original's
  icon, searching from the button's parent since it may be a sibling, and
  `Instantiate` it into the row at the same offset your button took. Painting a
  plate into your own icon and scaling the button up instead fails twice over:
  the scale is put back by whatever animates the hover swell, which sets it from
  a size of its own remembering, and a plate inside the button swells with the
  button, where the game's plates hold still and only the icon on them grows.
- **The press stops working while the hover still does.** A press is finished in
  `SimpleUIButton.LateUpdate`, which Unity does not call on a disabled behaviour,
  while `Tooltip` is a behaviour of its own and carries on — so the symptom is a
  button that shows its tooltip and does nothing when clicked. Something switches
  the button off when the browser has nothing for it to act on. Setting `enabled`
  back to true every frame is the fix; `ToggleButton(true)` is *not*, because it
  returns without doing anything when the collider is not on the button's own
  object.
- Watching for the press yourself — down and up over your own collider — is worth
  the fifteen lines. It works whatever state the button has been put in.

Take off only what would fight you: `LoadSaveButton` itself, which holds the load
and save icons and would paint one of them over yours, and the `Localisation.*`
behaviours, which exist to put the game's own words back.

### A copied button shows the *original's* tooltip — but you can repoint it

`Tooltip` keeps what it shows in a `tooltipParent` Transform, and that object is
**not** a child of the button. Unity only redirects a copied reference when it
points inside the copy, so `Instantiate(button)` gives you a Tooltip still
pointing at the original's words: hovering the copy lights up the neighbour's
tooltip, in the neighbour's place, and rewriting every `TextMesh` under the copy
changes nothing because there are none.

`tooltipParent` is **public**, though, along with `timeToProc`, `useFadeOut`,
`lerpPosDirection`, `Reset()` and `OnMouseExit()`. So: `Instantiate` the
original's `tooltipParent` object, hang it off your button at the same offset it
has from its own, point your `Tooltip` at the copy and call `Reset()`. That
re-finds the renderers and texts under the parent it has now been given, works
out which way the arrow points, and leaves everything switched off until the
pointer arrives — and you get `LerpPosIn` and the fades, which is the animation
you would otherwise be imitating. `TooltipOn` calls `ResizeBackground` every time
it opens, so writing new text into the copied `TextMesh`es is all that changing
the words takes.

Two traps. `TooltipOn`/`TooltipOff` are private, and `TooltipOff` is the only
thing that clears the `on` flag — a tooltip hidden by deactivating its button
still thinks it is open and will not open again, so call the public
`OnMouseExit()` before hiding the button. And `OnDisable` only turns the
renderers off, which is not the same thing.

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

### Where a mod keeps data between restarts

**`Modding.Configuration`.** This is the sanctioned route and it is easy to miss,
because `System.IO` being blacklisted makes it look as though there is none.

```csharp
XDataHolder data = Modding.Configuration.GetData();   // this mod's, and only this mod's
data.Write("window", new Vector3(x, y, 0f));
Modding.Configuration.Save();
```

`GetData()` works out which mod is asking from `Assembly.GetCallingAssembly()`
and throws `InvalidOperationException` for an assembly the manifest does not
list, so call it from your own code and not through a helper in somebody else's
assembly. It lands in `Besiege_Data/Mods/Config/<Name>_<Id>.xml`, which for most
mods already exists — the loader keeps `modkeys` there, so any mod declaring a
`<Key>` has a file before it writes a line of its own.

The round trip is the game's: `ModdingInitializer.LoadMod` calls
`Configuration.Load` as the mod loads, and `ModManager.OnApplicationQuit` calls
`Configuration.SaveAll`. Writing into the holder is enough for a clean quit;
call `Save()` yourself at moments where losing the value to a crash would annoy.

`XDataHolder.Write(string, object)` picks the XData type off the value, and knows
`bool`, `int`, `float`, `string`, their arrays, `Vector3` and `Color`. There is
no `Vector2`.

**`Color` has three channels.** Writing one produces

```xml
<Color key="added"><R>0</R><G>1</G><B>0</B></Color>
```

with no alpha anywhere in it, so an opacity stored that way is silently gone by
the next launch and comes back as whatever `ReadColor` defaults to. Keep the
alpha beside it as a `Single`. Worth reading the file rather than trusting the
round trip: `Besiege_Data/Mods/Config/<Name>_<Id>.xml` is plain XML, and it
answered this in one look after a bug report that no amount of staring at the
code would have.

There is also **`Modding.ModIO`** for arbitrary files — `OpenText`, `CreateText`,
`ReadAllText`, `SerializeXml` and so on, each taking a `data` flag that decides
whether the path is relative to the mod's own folder or to Besiege's data folder.
That is the way round the `System.IO` blacklist when a config holder is not
enough; note that it is also how the loader intends downloads to happen, since
`UnityEngine.WWW` is blacklisted too.

`UnityEngine.PlayerPrefs` is not blacklisted and does work, but it is Unity's
store rather than Besiege's: a mod's settings end up in the game's own options
file, unmanaged, and outlive uninstalling the mod.

### Own the window's anchors before remembering where it is

A prefab's rect can be anchored and pivoted any way its author liked, and
`anchoredPosition` means something different for each of them. Set
`anchorMin`/`anchorMax`/`pivot` to the middle yourself and a stored position
means one thing — an offset in canvas units from the centre of the screen —
rather than something only true of the version of the prefab you tested against.

Clamp it when you read it back. The canvas matches on height, so canvas width
varies with aspect ratio, and nothing stops the player changing resolution
between sessions; a position remembered off the edge of a narrower screen is a
window with no way back to it.

### A colour transition multiplies; it does not replace

`Button.transition = ColorTint` drives the state's colour onto the target
graphic's **canvas renderer**, and that is multiplied by the graphic's own
`color` at draw time. Point it at an image you created transparent — the obvious
thing to do for a highlight that starts off — and it stays invisible in every
state, silently. Create the image opaque white and let the transition supply the
colour.

### You cannot colour a UI Factory graphic; put one of your own in front of it

`Button.colors` on a UIFactory prefab, or setting `color` on the image it draws
itself with, can do nothing at all and say nothing about it. `Besiege.UI.Bridge`
ships a `CustomMaterialHandler` — *"forces the image to use a custom shader
material instead of the default one"* — and a shader written to draw Besiege's
rounded panel need not multiply by the renderer's colour, which is the only thing
a tint sets. It is the same failure as repainting a load-screen slot button by
setting its texture, one canvas up.

What works is a plain uGUI `Image` of your own, parented inside the control and
stretched over it: default UI shader, takes a colour, one assignment. Borrow the
prefab image's `sprite` and `type` and it keeps the rounded corners too. This mod
marks a pinned row and draws its column swatches that way.

### UI Factory has no colour picker, and Besiege's is out of reach

The nineteen prefabs are `Empty`, `Icon`, `Text`, `Text Button`, `Text Toggle`,
`Text Dropdown`, `Icon Button`, `Icon Toggle`, `Button Dropdown`, `Input Field`,
`Slider`, `Options`, `Scroll View`, `Blur`, `Panel`, `Mask`, `Window`, two
tooltips, plus `WorldCanvas` and `KeymapCanvas`. Besiege's own picker is the
block mapper's paint selector, behind `InternalModding`, and only opens for a
block.

What that selector *is*, though, is worth copying: `Selectors.ColourSliderSelector`
is a knob dragged along a `Texture colourPicker` — a strip of the colours it can
choose — with `ColorToPixelPos` / `ClosestColorPos` mapping between the two. The
texture is private and mapper-only, but the widget is a slider with a picture
behind it, and UI Factory supplies the slider. `OptionsView` draws its own strip
and puts it on a `Slider` with **both** of the prefab's bars turned off — the fill
*and* the track. A fill bar means "this much"; a colour slider means "this one",
and a track under the strip makes the strip read as a sticker.

Two things worth knowing about the game's own, read off a screenshot of the rocket
block's settings:

- **The strip is pale and the answer is not.** Sampled across the bar, its
  saturation runs about 0.62 the whole way, while the value beside it reads
  `#FF4C00` — full strength. So draw the ramp washed out and hand back
  `Hue(t, 1f)`.
- **It is a smooth ramp of every hue**, not a row of swatches, and there is no
  black or white on it.

Inset the strip by half the knob's width at each end: a knob's centre stops half a
knob short of both ends of its track, so a strip drawn edge to edge points at the
wrong colour near the ends.

And **leave something on the slider that is a raycast target**. A `Slider` is
dragged through whatever graphic under the pointer catches the ray; turn off the
prefab's fill *and* its track and there is nothing left, so the control goes
completely dead — it looks like a picture of a slider. The strip that replaced
them has to take the job: `raycastTarget = true`.

### What is in UI Factory's sprite bundle cannot be listed

`Make.Sprites` is keyed `"package::name"` and is **not public**, and `Make.Sprite`
only answers for a name you already know — warning into the log when you are
wrong. So a mod cannot ask what artwork the bundle holds; it can only try names
and read `Player.log` for the misses. Worth doing anyway for anything Besiege
draws itself: its own cog beats a drawn one, and the cost of being wrong is a
log line and a fallback.

Watch the name: **Besiege has its own `Slider` in the global namespace**, and it
is the one an unqualified `Slider` binds to inside this mod. Write
`UnityEngine.UI.Slider` in full.

### A text box that does not also drive the game

Use UIFactory's `Input Field` prefab rather than a uGUI `InputField` of your own:
it carries `StopsHotkeysWhenInputFieldFocused`, without which typing `255` also
fires whatever Besiege has bound to 2, 5 and 5.

Commit on `onEndEdit`, not `onValueChanged` — the latter applies the `2` of a
`255` while it is still being typed, which is very visible when a slider is
following along. Write the parsed value back into the box afterwards, so `300`
becoming 255 happens in front of the player rather than silently, and put the
real value back when the text will not parse at all. A box and a slider driving
each other need one "I am writing to this" flag between them, or each will hear
the other's callback as the player having moved it.

### A button inside a button, and a heading that fits its own button

A `Text Button` dropped inside another one does the right thing without being
told: uGUI walks up from whatever the pointer hit until it finds something that
handles the click, and stops at the first — so the inner button fires and the
row it sits in does not. Hover is the exception and is also what you want, since
`OnPointerEnter` goes to everything in the chain and the row still lights up
under a pointer that is over the small button.

Reusing the prefab is also the only cheap way to get Besiege's rounded corners:
they live in the button's own background sprite, so *mark* a button by tinting
that graphic (`Button.targetGraphic`) rather than by putting a rectangle in front
of it, which will have corners the button does not. Keep the prefab's `colors`
and `transition` before overwriting them — they are the only description of the
ordinary state there is, and putting them back is how the mark comes off.

Sizing a heading is arithmetic nobody can do at build time: Besiege's font is
wide and letter-spaced, and none of it is measurable before the window exists.
`Text` is created with `horizontalOverflow = Overflow`, so a label too wide for
its button silently hangs out of both ends rather than wrapping or clipping.
Measure a screenshot — in this mod `CHANGED ▲▼` is 82 units of a 690-unit row at
font size 13 — and give the column enough width for it plus whatever the column
gives up to a swatch.

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

### And when the player has left the build area entirely

`inMenu` is about a menu over the game, not about which game there is. A panel on
a `DontDestroyOnLoad` canvas — and anything that must survive a level change has
to be on one — will happily draw itself over the main menu and the level
selector unless something stops it.

`StatMaster.isMainMenu` is a public static bool, set from `GameVersionText.Awake`
and cleared from `AddPiece.Awake`/`OnSceneLoad`, and `StatMaster.isLoadingLevels`
covers the gap in between. The check worth leaning on, though, needs no list of
scene names at all: **`Machine.Active() == null`**. It is
`MachineObjectTracker.activeMachine`, and outside a build area there is no
machine — which for anything describing the machine is the same question anyway.

### Anything parented into the machine is destroyed with it

A level change rebuilds the machine, so objects hung off a block's parent —
overlays, markers, anything positioned in machine space — are gone, silently,
while the interface that put them there is still on screen saying otherwise.
Besiege carries the machine itself across levels, so the right answer is usually
to draw them again rather than to give up: keep what was drawn, notice that the
container is null when it should not be, and redraw.

Wait for `Machine.Active().BuildingBlocks` to be non-empty first, not just for
`IsLoadingMachine` to go false. The machine object exists before its blocks do,
and a parent taken in that window is the machine's own transform, which is a
different place — so everything lands somewhere plausible and wrong.

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

### Some blocks are several blocks

Two shapes in the palette are not one block in the file:

- **Braces, fuel hoses and winch ropes** are one block that writes
  `start-position` and `end-position` — two `Vector3`s in the block's own local
  space, which come back through `TransformPoint`, so rotation *and* scale apply.
  Nothing else writes those two keys, so recognising them by the data rather than
  by a list of block types picks up modded blocks that drag the same way.
- **A build surface is nine blocks.** The surface (id 73) writes
  `edges`: a `String` of four guids separated by `|`. Each edge (id 72) writes
  `start` and `end`: the guids of two corner nodes. Each node (id 71) is an
  ordinary block whose `Position` is where that corner is, and it may be shared
  with the surface next door — one real machine had 44 surfaces, 137 edges and
  109 nodes.

The consequences for a diff are worth spelling out. The surface's own `Position`
is one of its corners, so a diff that only marks the block marks one corner of
it. The nodes and edges have **no placement ghost** — nobody drags a corner out
of the menu — so nothing is drawn for them either, and dragging a corner changes
*only* the node: the surface's own position, rotation and `edges` list are
identical before and after. A surface whose shape was pulled about therefore
reads as unchanged unless the corner positions are folded into whatever
fingerprint the diff compares.

Resolving the shape needs the whole machine in hand — a guid means nothing until
the block it names has been read — so it is a pass after parsing, not something a
block can answer about itself. Walk the four edges as a loop rather than taking
the nodes in the order they are named: the file does not promise an order, and a
fan of triangles through four corners in the wrong order is a bow tie.

The corner node **does** have a ghost, and it is the little ball you drag the
corner by — so a diff that draws every changed block draws a scatter of balls
around a surface as well as the surface. Whatever draws the surface should speak
for its edges and nodes too, and skip them.

**Its edges are curves, and the curve is not in the file.** `BuildEdgeBlock` saves
`start` and `end` and nothing else; it rebuilds the shape at load time out of the
two nodes and *its own transform*. `UpdateEdge` puts five points into a path —
`start + q·a`, `start`, the edge block's own position, `end`, `end + q̄·b` — and
`Interp(t)` runs a Catmull-Rom along it, so the edge block's position is the
control point and a straight edge is one sitting at the midpoint.
`BuildSurface.GetPointOnSurface(u, v)` is then a Coons patch: the bilinear of the
corner nodes, plus the edge interpolations, minus the bilinear again.

Replicating that is a lot of arithmetic to get subtly wrong, and there is a
shortcut for three of the four cases: **the machine on screen has already
generated the mesh**. Copy the block's `MeshFilter.sharedMesh` onto a plain
GameObject rather than reimplementing the patch — exact curves, exact thickness,
no maths. Only a *removed* surface has nothing to copy.

That shortcut looks like it should be taken for **every** block, and it was, and
it was taken back out. In theory a copy hung off the block's own renderer is exact
where a ghost is only the block as it comes out of the menu; in practice, on real
machines, wheels and cannons came out worse than the ghosts they replaced. Keep it
for the build surface, which has no usable ghost at all, and let the ghosts do the
rest.

**A ghost prefab is not necessarily built at full size.** Writing the block's own
scale onto a spawned ghost throws away whatever the prefab was authored at, and a
cannon's is not 1 — so the mark came out half again too big. Multiply the two:
the prefab's scale for the shape, the block's for what the player scaled it to.

For the block types whose ghost has nothing on it — Besiege has a few — copy the
block itself out of the machine where it is still there, and fall back to another
block of the same type for one the version deleted, placed at the record's own
transform. `BlockBehaviour.BlockID` is the type, so one pass over
`Machine.BuildingBlocks` gives you both a guid index and a type index.

Take the mesh off a `SkinnedMeshRenderer` with `BakeMesh`, and remember that it
has no `MeshFilter` at all: a fuel hose, a rope and a spring are skinned, and
skipping them leaves the block marked at whatever solid fitting is on the end of
it and nowhere else. A baked mesh is made rather than borrowed, so destroy it
with the overlay — a runtime Mesh is not collected when the object holding it
goes.

**Check what a bake gives you back.** It can be empty — no pose yet, no bones —
and an empty mesh is a shell that is present, painted, counted as drawn and
invisible. It can also land in a space that is not the renderer's, depending on
where the mesh's root bone is: a fuel hose baked that way came out as a spike of
black threads reaching towards the machine's origin. There is no way to ask which
space it chose, so measure it: `renderer.bounds` is world units and known good,
and a bake whose bounds are out by more than a factor of three is not a copy of
that renderer. Fall back to the plain drawing when it fails.

The same suspicion applies to a **placement ghost**: some block types have a ghost
prefab with no geometry on it at all, and spawning one gives you a shell that
counts as drawn and shows nothing. Check for a mesh with vertices before adopting
it, and keep a last resort — a plain box where the block is — for a type that has
neither. A block the list calls changed and the machine does not mark is the worst
answer available.

**Hang the copy off the transform that is drawing the original.** Copying a
renderer's position, rotation and `lossyScale` onto an object parented somewhere
else is exact only while nothing above it is scaled unevenly, and Besiege's blocks
are: a wheel stretched to 1.5 × 1 × 0.7 with its rim on a rotated child cannot be
described by any position-rotation-scale triple, so the copy comes out the wrong
shape and the wrong size — which looks exactly like the mod ignoring the resize.
A child of the renderer's own transform, at identity, is exact by construction.

**Do not bake the matrix into the vertices instead.** Block meshes are not
readable: `mesh.vertices` on one prints "Not allowed to access vertices on mesh
'SM_fuel_tank_small…'" and hands back an empty array, so the copy is silently
empty and the console fills up. `mesh.bounds` *is* readable — it is metadata, not
mesh data — which is enough to put a pivot at the middle of a mesh you cannot
otherwise inspect.

Copies parented into the machine are not under the mod's own container, so they
have to be hidden, recoloured and destroyed by hand.

Copy the meshes, **not the block**. `Instantiate` on a live block runs its Awake,
and a `BuildSurface` waking up registers itself with the machine it finds itself
in — the copy becomes a real surface in the real machine, and the next save has
it. A GameObject carrying a MeshFilter and a MeshRenderer has no behaviour to run.

Drawing a surface from scratch, for the case where there is nothing to copy: a
flat sheet through the corners is *inside* the block — a surface is a slab as
thick as its `bmt-thickness` — so it has to stand off both faces. Take the plane from the whole outline (Newell's method) rather
than from its first three corners, which say nothing when those happen to be in a
line; fan the faces from the middle rather than from a corner, so a dart-shaped
outline is still covered; and wind every triangle both ways, because which way
round the loop came out is the file's business and a slab facing away from you is
a slab you cannot see.

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

**Anything you build yourself has to be put on the machine's layer.** A ghost
arrives on whatever layer Besiege authored it for; a `new GameObject` or a
`GameObject.CreatePrimitive` starts on the default layer, which the build area's
camera need not be drawing at all — so a shape of your own can be present, placed,
painted and invisible, with nothing in the log. Take the layer off the first
renderer under the machine's block root and set it on everything you make.

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

### A block that is in two places: braces, fuel lines, winches

A dragged block's ghost is one end of it. The rest — the brace itself, the length
of hose, the rope — is strung between two points that live in the block's data,
under `start-position` and `end-position`, with `start-rotation` and
`end-rotation` beside them. `GenericDraggedBlock` and `FuelLineBehaviour` both
write them; recognise them by the keys rather than by a list of block types, and
a modded block that drags the same way is handled too.

**They are in the block's own local space**, scale included:

```
OnSave    transform.InverseTransformPoint(startPoint.position)
OnLoad    transform.TransformPoint(data.ReadVector3("start-position"))
```

so putting one back is `Position + Rotation * Vector3.Scale(local, Scale)`, and
`Machine.SpawnBlock` assigns `transform.localScale = blockInfo.Scale` directly,
which is what makes the file's `Scale` the right one to use. Checked against 1900
endpoints in six real machines: they land in tight clusters at 0.71 (a block's
corner), 0.00 (dead on a block's centre) and 1.00 from the nearest other block —
distances that would smear if the space or the scale were wrong.

Nothing in the prefab library is "the middle of a brace", so draw it with a
`GameObject.CreatePrimitive(PrimitiveType.Cylinder)`: two units tall and one
across, so the scale is `(width, length / 2, width)` and the rotation is
`Quaternion.FromToRotation(Vector3.up, end - start)`. It arrives with a collider,
which wants the same stripping a ghost does.

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
   The same crash, with the same unhelpful stack, is also what you get for
   **an identifier that does not exist** — a constant you forgot to declare, or
   a field that turned out to be private. A SIGSEGV from this compiler means
   "there is an error somewhere in these files", not necessarily "there is an
   enum in these files". Bisect: comment out the newest block and build again.
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
