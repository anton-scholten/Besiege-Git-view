using System;
using System.Collections.Generic;
using UnityEngine;

namespace GitView
{
    /// <summary>
    /// Draws a diff over the machine in the build area: added in green, moved or
    /// retuned in orange, deleted in red and standing where it used to be. What the
    /// save left alone has a colour too, turned off by default.
    ///
    /// Nothing here touches the machine. Each mark is a hollow shell a few per cent
    /// larger than the block it sits on rather than a tint on the block's own
    /// renderers: Besiege's blocks share their materials, so tinting one girder
    /// tints every girder, and putting the original back means being right about
    /// what it was. A shell is removed by deleting it.
    ///
    /// The shells are Besiege's own placement ghosts, which are already the right
    /// shape for the block -- but they are not inert, and <see cref="Sterilise"/> is
    /// what makes them so.
    /// </summary>
    public class GhostView
    {
        /// <summary>
        /// How thick the tube along a brace, a hose or a rope is: a little fatter
        /// than any of them, so it is drawn over what it marks and not inside it.
        /// </summary>
        private const float SpanWidth = 0.16f;

        /// <summary>
        /// Below this there is nothing to draw between the two ends: a brace placed
        /// and never dragged has both in the same place.
        /// </summary>
        private const float ShortestSpan = 0.05f;

        private const string ContainerName = "GitView Diff Overlay";

        /// <summary>
        /// Shaders to draw a shell with, best first. Only shaders in the player's
        /// build can be found by name, so several are tried before falling back to
        /// tinting the ghost's own material.
        /// </summary>
        private static readonly string[] ShaderCandidates =
        {
            "Particles/Alpha Blended",
            "Transparent/Diffuse",
            "Unlit/Transparent",
            "Sprites/Default",
            "Particles/Additive"
        };

        private GameObject _container;

        /// <summary>
        /// One material per category, shared by every shell in it: a dragged colour
        /// slider is then one assignment however many blocks are on screen, instead
        /// of hundreds of repaints a frame.
        /// </summary>
        private readonly Material[] _paint = new Material[DiffPalette.Categories];

        /// <summary>
        /// The shells of each category. Needed for the fallback path, where no
        /// translucent shader was found and the colour lives in a property block per
        /// renderer, and for taking one category down on its own.
        /// </summary>
        private readonly List<GameObject>[] _shells = new List<GameObject>[DiffPalette.Categories];

        /// <summary>
        /// Whether each category was turned off when it was last drawn, so that a
        /// colour change can tell a recolour from a redraw. See <see cref="Refresh"/>.
        /// </summary>
        private readonly bool[] _faded = new bool[DiffPalette.Categories];

        private Shader _shader;
        private bool _shaderSearched;

        /// <summary>
        /// The diff on show, kept so it can be drawn again. The shells are parented
        /// into the machine and a level change destroys the machine, so an overlay
        /// can disappear without anybody asking it to; this tells that apart from
        /// having been cleared.
        /// </summary>
        private DiffResult _showing;

        private bool _hidden;

        /// <summary>True while a diff is being shown.</summary>
        public bool Showing
        {
            get { return _container != null; }
        }

        /// <summary>
        /// True when a diff should be on screen and its shells are not -- which
        /// means the machine they hung off was destroyed under them.
        /// </summary>
        public bool Lost
        {
            get { return _showing != null && _container == null; }
        }

        public void Show(DiffResult diff)
        {
            // Held across the wipe: Clear forgets what is being shown, and this
            // does not want forgetting.
            Wipe();
            _showing = diff;
            if (diff == null)
            {
                return;
            }

            Transform root = BlockRoot();
            if (root == null)
            {
                Log.Warn("no machine to draw the diff over.");
                return;
            }

            // Which layer the machine's blocks are drawn on. Anything built from
            // nothing -- the tube along a brace, the slab over a surface -- starts on
            // the default layer, which the build camera need not be drawing at all.
            // Ghosts arrive on whatever layer Besiege authored them for.
            _drawLayer = LayerOfBlocks(root);

            _container = new GameObject(ContainerName);
            _container.transform.SetParent(root, false);
            _container.transform.localPosition = Vector3.zero;
            _container.transform.localRotation = Quaternion.identity;
            _container.transform.localScale = Vector3.one;
            // A diff redrawn while the window is standing aside for a menu must
            // come back hidden, or it appears over the menu that hid it.
            _container.SetActive(!_hidden);

            int drawn = 0;
            int asked = 0;
            for (int category = 0; category < DiffPalette.Categories; category++)
            {
                List<BlockRecord> blocks = Blocks(diff, category);
                drawn += Draw(blocks, category);
                asked += DiffPalette.Faded(category) ? 0 : blocks.Count;
            }

            // Counted against what was asked for rather than against the diff: a
            // category the player has turned off draws nothing, and that is not a
            // failure worth a line in the log.
            if (drawn == 0 && asked > 0)
            {
                Log.Warn("the diff has " + asked + " blocks to draw but none could be " +
                         "drawn; the block types may not have placement ghosts.");
            }
        }

        /// <summary>The layer the machine's blocks are drawn on. See <see cref="Show"/>.</summary>
        private int _drawLayer;

        /// <summary>
        /// The layer a machine's blocks are drawn on, off the first thing under it
        /// that draws. Falls back to the root's own layer.
        /// </summary>
        private static int LayerOfBlocks(Transform root)
        {
            Renderer drawn = root.GetComponentInChildren<Renderer>(true);
            return drawn != null ? drawn.gameObject.layer : root.gameObject.layer;
        }

        /// <summary>Puts an object of ours on the layer the machine is drawn on.</summary>
        private void OnMachineLayer(GameObject made)
        {
            Transform[] parts = made.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i].gameObject.layer = _drawLayer;
            }
        }

        /// <summary>The blocks of one category. The overlay's four are the diff's three and what it left alone.</summary>
        private static List<BlockRecord> Blocks(DiffResult diff, int category)
        {
            if (category == DiffPalette.Added) { return diff.Added; }
            if (category == DiffPalette.Changed) { return diff.Changed; }
            if (category == DiffPalette.Removed) { return diff.Removed; }
            return diff.Unchanged;
        }

        /// <summary>Takes the overlay down and forgets it.</summary>
        public void Clear()
        {
            _showing = null;
            Wipe();
        }

        /// <summary>Takes the shells down, remembering what they were.</summary>
        private void Wipe()
        {
            // The machine may be loaded again between one diff and the next, and a
            // table of its blocks does not survive that.
            _live = null;
            _kinds = null;
            for (int i = 0; _made != null && i < _made.Count; i++)
            {
                if (_made[i] != null)
                {
                    UnityEngine.Object.Destroy(_made[i]);
                }
            }
            _made = null;
            if (_container != null)
            {
                UnityEngine.Object.Destroy(_container);
                _container = null;
            }
            // The copies hang off the machine's own blocks rather than off the
            // container, so nothing takes them down but this.
            for (int i = 0; i < _loose.Count; i++)
            {
                if (_loose[i] != null)
                {
                    UnityEngine.Object.Destroy(_loose[i]);
                }
            }
            _loose.Clear();
            _pivots.Clear();
            for (int i = 0; i < _shells.Length; i++)
            {
                if (_shells[i] != null)
                {
                    _shells[i].Clear();
                }
            }
        }

        /// <summary>
        /// Draws the current diff again over whatever machine is there now. Used
        /// after a level change, which destroys the machine the shells were
        /// parented to. See <see cref="Lost"/>.
        /// </summary>
        public void Restore()
        {
            if (_showing != null)
            {
                Show(_showing);
            }
        }

        /// <summary>
        /// Picks the overlay up on a colour the player has just changed. Cheap enough
        /// to call from a slider being dragged, which is the point: one assignment
        /// where a shared material is doing the work, and only the no-shader fallback
        /// has to walk the shells.
        /// </summary>
        public void Refresh()
        {
            for (int category = 0; category < DiffPalette.Categories; category++)
            {
                // A category dragged to nothing has no shells to recolour, and one
                // dragged off nothing has none yet. Crossing that line is the only
                // thing a repaint cannot answer.
                if (_faded[category] != DiffPalette.Faded(category))
                {
                    Redraw(category);
                    continue;
                }

                Color colour = DiffPalette.Of(category);
                if (_paint[category] != null)
                {
                    Tint(_paint[category], colour);
                    continue;
                }
                List<GameObject> shells = _shells[category];
                if (shells == null)
                {
                    continue;
                }
                for (int i = 0; i < shells.Count; i++)
                {
                    if (shells[i] != null)
                    {
                        Paint(shells[i], category);
                    }
                }
            }
        }

        /// <summary>
        /// Shows or hides the overlay. Idempotent: the window asks every frame
        /// whether the game has a menu up, and this is what it acts on.
        /// </summary>
        public void SetVisible(bool visible)
        {
            // Remembered as well as applied, so an overlay redrawn while a menu is up
            // does not come back on top of it.
            _hidden = !visible;
            if (_container != null && _container.activeSelf != visible)
            {
                _container.SetActive(visible);
            }
            // And the ones hanging off the machine's own blocks, which the container
            // does not speak for.
            for (int i = 0; i < _loose.Count; i++)
            {
                if (_loose[i] != null && _loose[i].activeSelf != visible)
                {
                    _loose[i].SetActive(visible);
                }
            }
        }

        /// <summary>
        /// Throws one category's shells away and spawns them again. Only needed when
        /// a category has been turned on or off; a colour that merely changed is a
        /// material assignment.
        /// </summary>
        private void Redraw(int category)
        {
            List<GameObject> shells = _shells[category];
            if (shells != null)
            {
                for (int i = 0; i < shells.Count; i++)
                {
                    if (shells[i] == null)
                    {
                        continue;
                    }
                    // A shell of this category may be one of the loose ones, and its
                    // pivot may be the shell itself: both lists have to let go before
                    // it is destroyed, or the next rescale walks a corpse.
                    _loose.Remove(shells[i]);
                    _pivots.Remove(shells[i].transform);
                    if (shells[i].transform.parent != null)
                    {
                        _pivots.Remove(shells[i].transform.parent);
                    }
                    UnityEngine.Object.Destroy(shells[i]);
                }
                shells.Clear();
            }

            _faded[category] = DiffPalette.Faded(category);
            if (_container == null || _showing == null)
            {
                return;
            }
            Draw(Blocks(_showing, category), category);
        }

        private int Draw(List<BlockRecord> blocks, int category)
        {
            // Recorded whether anything is drawn or not: Refresh compares against it,
            // and a category that drew nothing because it is off has to be told apart
            // from one that drew nothing because it is empty.
            _faded[category] = DiffPalette.Faded(category);
            if (_faded[category])
            {
                return 0;
            }

            int drawn = 0;
            for (int i = 0; i < blocks.Count; i++)
            {
                if (Draw(blocks[i], category))
                {
                    drawn++;
                }
            }
            return drawn;
        }

        private bool Draw(BlockRecord block, int category)
        {
            bool drawn = false;

            // The edges and corner nodes a surface is made of are drawn by the
            // surface, so they are not drawn again here. A node's own ghost is the
            // little ball you drag the corner by, and a handful of those scattered
            // over a changed surface said nothing the surface was not saying.
            if (block.PartOfSurface)
            {
                return false;
            }

            // A build surface is the one block a ghost cannot answer for: its own
            // ghost is a mark at one corner, and where the other three are is a
            // question about eight other blocks. So that one is copied off the machine
            // -- see CopyOfLive -- and everything else is drawn from its placement
            // ghost. Copying every block was tried and came out worse on wheels and
            // cannons; what is left is the case where the ghost has nothing to draw.
            if (block.HasSurface)
            {
                if (category != DiffPalette.Removed && CopyOfLive(block.Id, category))
                {
                    return true;
                }
                if (DrawSurface(block, category))
                {
                    return true;
                }
            }

            GameObject ghost = Spawn(block);
            if (ghost != null)
            {
                // What the prefab was built at, before anything of ours touches it.
                Vector3 authored = ghost.transform.localScale;
                // A ghost that draws nothing is worse than no ghost: it counts as
                // drawn, and the player is left looking at a block the list says
                // changed with nothing on it. The drag panel's is one of those.
                if (!Drawable(ghost))
                {
                    UnityEngine.Object.Destroy(ghost);
                }
                else
                {
                    ghost.transform.localPosition = block.Position;
                    ghost.transform.localRotation = block.Rotation;
                    // Multiplied by the size the ghost was authored at rather than
                    // replacing it: a ghost prefab need not be built at full size -- a
                    // cannon's is not -- and what is wanted is the prefab's size *and*
                    // whatever the player scaled the block to. No swelling here
                    // either; every shell is grown by its own pivot. See OnPivot.
                    ghost.transform.localScale =
                        Vector3.Scale(authored, block.Scale);
                    Adopt(ghost, category);
                    drawn = true;
                }
            }
            if (!drawn)
            {
                // Nothing to spawn and nothing to draw. The block itself is the best
                // thing to copy where it is still in the machine -- it is this block,
                // in this place, at this size -- and another block of the same type
                // is the answer for one the version deleted.
                drawn = (category != DiffPalette.Removed &&
                         CopyOfLive(block.Id, category)) ||
                        StandIn(block, category);
            }

            // A brace, a fuel line or a winch is not where its ghost is; it is
            // strung between two points, and the ghost is only one end of it.
            if (block.HasSpan)
            {
                drawn = DrawSpan(block, category) || drawn;
            }
            return drawn || DrawBox(block, category);
        }

        /// <summary>
        /// Whether an object has any geometry worth drawing: a mesh with vertices in
        /// it, anywhere in the hierarchy. Asked of inactive objects, since a ghost is
        /// spawned switched off.
        /// </summary>
        private static bool Drawable(GameObject of)
        {
            MeshFilter[] shapes = of.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < shapes.Length; i++)
            {
                if (shapes[i] != null && shapes[i].sharedMesh != null &&
                    shapes[i].sharedMesh.vertexCount > 0)
                {
                    return true;
                }
            }
            SkinnedMeshRenderer[] bent =
                of.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < bent.Length; i++)
            {
                if (bent[i] != null && bent[i].sharedMesh != null &&
                    bent[i].sharedMesh.vertexCount > 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Draws a block by borrowing the look of another block of the same type
        /// standing in the machine.
        ///
        /// For the types with no ghost worth spawning, where the block itself is gone
        /// too: a machine that had two drag panels deleted usually has a third still
        /// on it, and one drag panel looks like another. Placed where the record says,
        /// at the record's own size, so what is borrowed is the shape and nothing else.
        /// </summary>
        private bool StandIn(BlockRecord block, int category)
        {
            GameObject donor = SameKind(block.Kind);
            if (donor == null || _container == null)
            {
                return false;
            }

            GameObject holder = new GameObject("Borrowed");
            holder.transform.SetParent(_container.transform, false);
            holder.transform.localPosition = block.Position;
            holder.transform.localRotation = block.Rotation;
            holder.transform.localScale = block.Scale;

            Transform root = donor.transform;
            Renderer[] drawn = donor.GetComponentsInChildren<Renderer>(false);
            int made = 0;
            for (int i = 0; i < drawn.Length; i++)
            {
                Mesh shape = MeshOf(drawn[i]);
                if (shape == null)
                {
                    continue;
                }
                GameObject piece = new GameObject("Face", typeof(MeshFilter),
                                                  typeof(MeshRenderer));
                piece.GetComponent<MeshFilter>().sharedMesh = shape;
                piece.transform.SetParent(holder.transform, false);
                // Where that piece sits on the block it was borrowed from, which is
                // where it sits on this one: the two are the same kind of block.
                piece.transform.localPosition =
                    root.InverseTransformPoint(drawn[i].transform.position);
                piece.transform.localRotation =
                    Quaternion.Inverse(root.rotation) * drawn[i].transform.rotation;
                piece.transform.localScale = Relative.Scale(
                    drawn[i].transform.lossyScale, root.lossyScale);
                made++;
            }
            if (made == 0)
            {
                UnityEngine.Object.Destroy(holder);
                return false;
            }

            OnMachineLayer(holder);
            Adopt(holder, category);
            return true;
        }

        /// <summary>
        /// Any block of this type standing in the machine, or null. Indexed with the
        /// rest of the machine's blocks, once per diff.
        /// </summary>
        private GameObject SameKind(int kind)
        {
            LiveBlock(string.Empty);
            GameObject found;
            return _kinds != null && _kinds.TryGetValue(kind, out found) ? found : null;
        }

        /// <summary>
        /// A plain box where the block is, for a block nothing else could draw. The
        /// last resort, and worth having: a block in the diff with no mark on the
        /// machine is the mod saying "two blocks changed" and pointing at nothing,
        /// which is worse than a mark of the wrong shape.
        /// </summary>
        private bool DrawBox(BlockRecord block, int category)
        {
            GameObject box = Primitive(PrimitiveType.Cube);
            if (box == null)
            {
                return false;
            }
            box.name = "Where";
            box.transform.localPosition = block.Position;
            box.transform.localRotation = block.Rotation;
            box.transform.localScale = Vector3.Scale(block.Scale,
                                                     Vector3.one * BoxSide);
            Adopt(box, category);
            return true;
        }

        /// <summary>
        /// How big the last-resort box is: a Besiege block is one unit, and rather
        /// less than that says "something here" without burying its neighbours.
        /// </summary>
        private const float BoxSide = 0.7f;

        /// <summary>
        /// Draws a deleted build surface as a slab through the corners the file names
        /// -- see <see cref="BlockRecord.Corners"/> -- since there is nothing left in
        /// the machine to copy. Nothing Besiege can be asked for: the ghost for a
        /// surface is a mark at its own position, which is one of its corners, and
        /// the corners and edges have no ghost at all.
        ///
        /// False if the shape could not be built, and the caller then falls back to
        /// that ghost: one coloured corner is a poor way to show a surface and a good
        /// deal better than nothing.
        /// </summary>
        private bool DrawSurface(BlockRecord block, int category)
        {
            GameObject sheet = Sheet(block.Corners, block.Thickness);
            if (sheet == null)
            {
                return false;
            }
            Adopt(sheet, category);
            return true;
        }

        /// <summary>
        /// A copy of what the machine is drawing for this block, or false if the
        /// block is not in the machine -- which is the case for anything the version
        /// on screen removed.
        ///
        /// For the blocks whose shape is not in their prefab: a build surface, whose
        /// outline is four other blocks and whose edges are curves, and the few types
        /// with no usable ghost. The machine in front of the player has the real
        /// thing on it, generated by the game from the same data.
        ///
        /// The meshes are copied, not the block: instantiating a live block runs its
        /// Awake, and a <c>BuildSurface</c> waking up registers itself with the
        /// machine it finds itself in -- the copy would be a real surface in the real
        /// machine in the next save. A MeshFilter and a MeshRenderer cannot join
        /// anything.
        /// </summary>
        private bool CopyOfLive(string id, int category)
        {
            GameObject real = LiveBlock(id);
            if (real == null || _container == null)
            {
                return false;
            }

            Renderer[] drawn = real.GetComponentsInChildren<Renderer>(false);
            int made = 0;
            for (int i = 0; i < drawn.Length; i++)
            {
                Mesh shape = MeshOf(drawn[i]);
                if (shape == null)
                {
                    continue;
                }

                // Hung off the very transform that is drawing it, which is the only
                // way to be exactly where it is: a position-rotation-scale triple
                // cannot describe a rotated child of an unevenly scaled parent, which
                // Besiege's blocks are full of, and baking the matrix into the
                // vertices is not an option either -- block meshes are not readable.
                //
                // The pivot sits at the middle of the mesh so the shell grows about
                // the block rather than off one end, and the piece is offset back by
                // the same amount so the mesh lands where it was.
                Vector3 middle = shape.bounds.center;
                GameObject pivot = new GameObject("Shell");
                pivot.transform.SetParent(drawn[i].transform, false);
                pivot.transform.localPosition = middle;
                pivot.transform.localRotation = Quaternion.identity;
                pivot.transform.localScale = Vector3.one * DiffPalette.Shell;

                GameObject piece = new GameObject("Face", typeof(MeshFilter),
                                                  typeof(MeshRenderer));
                piece.GetComponent<MeshFilter>().sharedMesh = shape;
                piece.transform.SetParent(pivot.transform, false);
                piece.transform.localPosition = -middle;
                piece.transform.localRotation = Quaternion.identity;
                piece.transform.localScale = Vector3.one;

                OnMachineLayer(pivot);
                Paint(piece, category);
                Keep(pivot, category);
                _pivots.Add(pivot.transform);
                made++;
            }
            return made > 0;
        }

        /// <summary>
        /// Holds on to a shell that is not under the overlay's container, so it is
        /// hidden, recoloured and destroyed with the rest.
        /// </summary>
        private void Keep(GameObject shell, int category)
        {
            shell.SetActive(!_hidden);
            _loose.Add(shell);
            Note(shell, category);
        }

        /// <summary>
        /// Files a shell under its category, which is how a colour change finds the
        /// ones it has to repaint.
        /// </summary>
        private void Note(GameObject shell, int category)
        {
            if (_shells[category] == null)
            {
                _shells[category] = new List<GameObject>();
            }
            _shells[category].Add(shell);
        }

        /// <summary>
        /// The mesh a renderer is drawing, in the space of its own transform.
        ///
        /// Two kinds answer. An ordinary renderer keeps its mesh in a MeshFilter
        /// beside it, to be shared as it stands. A *skinned* one has no MeshFilter at
        /// all -- its shape is worked out every frame from bones, which is what a
        /// hose, a rope and a spring are -- so its mesh has to be baked at the pose it
        /// is in now. Anything else has no mesh to take.
        /// </summary>
        private Mesh MeshOf(Renderer renderer)
        {
            if (renderer == null || !renderer.enabled ||
                renderer.GetComponent<TextMesh>() != null)
            {
                return null;
            }

            SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
            if (skinned != null)
            {
                if (skinned.sharedMesh == null)
                {
                    return null;
                }
                try
                {
                    Mesh baked = new Mesh();
                    skinned.BakeMesh(baked);
                    // Ours to destroy, unlike a shared mesh: baking makes a new one
                    // per copy, and nothing else will ever let go of it.
                    Remember(baked);
                    // A bake can come back empty -- no bones yet, nothing posed --
                    // and an empty mesh is a shell that counts as drawn and cannot be
                    // seen.
                    if (baked.vertexCount == 0)
                    {
                        return null;
                    }
                    // And it can come back in a space that is not the renderer's,
                    // depending on where the mesh's root bone is -- a fuel hose's is
                    // not its renderer, and the copy came out as a spike of black
                    // threads reaching towards the machine's origin. Nothing can be
                    // asked, so it is measured instead.
                    if (!SameSize(baked, skinned))
                    {
                        Log.Warn("a bent mesh came back the wrong size and was not " +
                                 "copied; that block is drawn the plain way instead.");
                        return null;
                    }
                    return baked;
                }
                catch (Exception e)
                {
                    Log.Warn("could not take a copy of a bent mesh: " + e.Message);
                    return null;
                }
            }

            MeshFilter shape = renderer.GetComponent<MeshFilter>();
            Mesh mesh = shape == null ? null : shape.sharedMesh;
            // Vertex *count* can be asked of a mesh that cannot be read, and a mesh
            // with none draws nothing: better to fall through to the placement ghost
            // than to hang an invisible shell off the block and call it marked.
            return mesh != null && mesh.vertexCount > 0 ? mesh : null;
        }

        /// <summary>
        /// Whether a baked mesh is about the size of what the renderer is drawing --
        /// the one check available on a bake landing in the wrong space. The
        /// renderer's bounds are world units and known good; a copy out by more than
        /// a factor of three is not a copy.
        /// </summary>
        private static bool SameSize(Mesh baked, Renderer against)
        {
            float want = against.bounds.size.magnitude;
            Vector3 scaled = Vector3.Scale(baked.bounds.size,
                                           against.transform.lossyScale);
            float got = scaled.magnitude;
            if (want <= 0.0001f || got <= 0.0001f)
            {
                return false;
            }
            return got > want * 0.34f && got < want * 3f;
        }

        /// <summary>
        /// Keeps a mesh that was made rather than borrowed, so it can be destroyed
        /// with the overlay: a Mesh built at runtime is not collected with the object
        /// carrying it.
        /// </summary>
        private void Remember(Mesh made)
        {
            if (_made == null)
            {
                _made = new List<Mesh>();
            }
            _made.Add(made);
        }

        private List<Mesh> _made;

        /// <summary>
        /// The block in the machine with this identifier, or null. Through a table
        /// built once per diff: a machine can hold a thousand blocks.
        /// </summary>
        private GameObject LiveBlock(string id)
        {
            if (_live == null)
            {
                _live = new Dictionary<string, GameObject>();
                _kinds = new Dictionary<int, GameObject>();
                try
                {
                    Machine machine = Machine.Active();
                    List<BlockBehaviour> blocks = machine == null
                        ? null : machine.BuildingBlocks;
                    for (int i = 0; blocks != null && i < blocks.Count; i++)
                    {
                        if (blocks[i] == null)
                        {
                            continue;
                        }
                        _live[blocks[i].Guid.ToString()] = blocks[i].gameObject;
                        // And one of each type, for the blocks that have to borrow
                        // somebody else's look. See StandIn.
                        _kinds[blocks[i].BlockID] = blocks[i].gameObject;
                    }
                }
                catch (Exception e)
                {
                    Log.Warn("could not list the machine's blocks: " + e.Message);
                }
            }

            GameObject found;
            return _live.TryGetValue(id ?? string.Empty, out found) ? found : null;
        }

        /// <summary>
        /// The machine's blocks by identifier, for the length of one diff: the next
        /// one is drawn over a machine that has been loaded again.
        /// </summary>
        private Dictionary<string, GameObject> _live;

        /// <summary>One block of each type in the machine, for <see cref="StandIn"/>.</summary>
        private Dictionary<int, GameObject> _kinds;

        /// <summary>
        /// How far outside the real slab the mark round it is drawn: a little proud of
        /// each face, a little wider than the outline. It has to be outside -- a sheet
        /// through the corners lies on the middle of a slab with a thickness, sealed
        /// inside the block and visible from nowhere.
        /// </summary>
        private const float SurfaceSkin = 0.05f;
        private const float SurfaceSwell = 1.02f;

        /// <summary>
        /// A slab through the given corners, a little larger than the surface it
        /// marks: two faces and the rim between them. A mesh of ours, since the
        /// outline is whatever shape the player dragged it into.
        ///
        /// Three things are deliberately the blunt version, a surface not drawn being
        /// worse than one drawn a shade too generously: the faces are fans from the
        /// *middle*, so an outline pulled into a dart is still covered; the plane is
        /// Newell's normal, which the whole outline votes on rather than three corners
        /// that may be in a line; and every triangle is wound both ways, since which
        /// way round the walk came out is the file's business.
        /// </summary>
        private GameObject Sheet(Vector3[] corners, float thickness)
        {
            int n = corners.Length;
            if (n < 3)
            {
                return null;
            }
            try
            {
                Vector3 middle = Vector3.zero;
                for (int i = 0; i < n; i++)
                {
                    middle += corners[i];
                }
                middle /= n;

                // Newell's method: the sum of the outline's own turns, which is the
                // plane's normal for any polygon.
                Vector3 up = Vector3.zero;
                for (int i = 0; i < n; i++)
                {
                    Vector3 here = corners[i];
                    Vector3 next = corners[(i + 1) % n];
                    up.x += (here.y - next.y) * (here.z + next.z);
                    up.y += (here.z - next.z) * (here.x + next.x);
                    up.z += (here.x - next.x) * (here.y + next.y);
                }
                if (up.sqrMagnitude < 1e-10f)
                {
                    // Every corner in one line: there is no plane to stand off from.
                    up = Vector3.up;
                }
                up = up.normalized * (thickness * 0.5f + SurfaceSkin);

                // The middle of each face, then its corners pushed out from that
                // middle so the mark is a shade wider than what it marks.
                Vector3[] points = new Vector3[(n + 1) * 2];
                points[0] = middle + up;
                points[n + 1] = middle - up;
                for (int i = 0; i < n; i++)
                {
                    Vector3 out2 = middle + (corners[i] - middle) * SurfaceSwell;
                    points[1 + i] = out2 + up;
                    points[n + 2 + i] = out2 - up;
                }

                List<int> triangles = new List<int>();
                for (int i = 0; i < n; i++)
                {
                    int next = (i + 1) % n;
                    int topHere = 1 + i, topNext = 1 + next;
                    int lowHere = n + 2 + i, lowNext = n + 2 + next;
                    Face(triangles, 0, topHere, topNext);
                    Face(triangles, n + 1, lowNext, lowHere);
                    Face(triangles, topHere, lowHere, topNext);
                    Face(triangles, topNext, lowHere, lowNext);
                }

                Mesh mesh = new Mesh();
                mesh.name = "GitView Surface";
                mesh.vertices = points;
                mesh.triangles = triangles.ToArray();
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                Remember(mesh);

                GameObject sheet = new GameObject("Surface", typeof(MeshFilter),
                                                  typeof(MeshRenderer));
                sheet.GetComponent<MeshFilter>().sharedMesh = mesh;
                OnMachineLayer(sheet);
                return sheet;
            }
            catch (Exception e)
            {
                Log.Warn("could not draw a build surface: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// One triangle of the slab, added facing both ways. See <see cref="Sheet"/>
        /// for why both.
        /// </summary>
        private static void Face(List<int> into, int a, int b, int c)
        {
            into.Add(a); into.Add(b); into.Add(c);
            into.Add(c); into.Add(b); into.Add(a);
        }

        /// <summary>
        /// One of Unity's shapes, with everything that makes it a physical object
        /// taken off it. See <see cref="Sterilise"/>.
        /// </summary>
        private GameObject Primitive(PrimitiveType shape)
        {
            try
            {
                GameObject made = GameObject.CreatePrimitive(shape);
                Sterilise(made);
                // Built from nothing, so it starts on the default layer -- which the
                // build area's camera need not be drawing.
                OnMachineLayer(made);
                return made;
            }
            catch (Exception e)
            {
                Log.Warn("could not draw an overlay shape: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Draws the part of a two-ended block that is neither of its ends: the brace
        /// itself, the length of hose, the rope.
        ///
        /// The ends are stored in the block's own local space -- Besiege saves them
        /// through <c>InverseTransformPoint</c> -- so putting them back means the
        /// block's rotation and then its scale. The overlay's container sits on the
        /// transform the blocks are parented to, which is the space the saved position
        /// and rotation are already in. A Unity cylinder rather than a ghost: there is
        /// no prefab for "the middle of a brace".
        /// </summary>
        private bool DrawSpan(BlockRecord block, int category)
        {
            Vector3 from = Point(block, block.SpanStart);
            Vector3 to = Point(block, block.SpanEnd);
            Vector3 along = to - from;
            float length = along.magnitude;
            if (length < ShortestSpan)
            {
                // Both ends in the same place: a brace that was placed and not
                // dragged anywhere. Its ghost is the whole of it.
                return false;
            }

            // CreatePrimitive brings a collider, which is exactly what the placement
            // ghosts had to have taken off them -- Primitive takes it off again.
            GameObject tube = Primitive(PrimitiveType.Cylinder);
            if (tube == null)
            {
                return false;
            }

            tube.transform.localPosition = (from + to) * 0.5f;
            tube.transform.localRotation = Quaternion.FromToRotation(Vector3.up, along);
            // Unity's cylinder is two units tall and one across, so half the length
            // goes in as the height and the width is the diameter.
            tube.transform.localScale = new Vector3(SpanWidth, length * 0.5f, SpanWidth);
            Adopt(tube, category);
            return true;
        }

        /// <summary>
        /// One of a block's local points, in the space the overlay is drawn in.
        /// Rotation then scale, the order a transform applies them.
        /// </summary>
        private static Vector3 Point(BlockRecord block, Vector3 local)
        {
            return block.Position + block.Rotation * Vector3.Scale(local, block.Scale);
        }

        /// <summary>Parents a finished shell into the overlay and paints it.</summary>
        private void Adopt(GameObject shell, int category)
        {
            shell.transform.SetParent(_container.transform, false);
            OnPivot(shell, _container.transform);
            Paint(shell, category);
            shell.SetActive(true);
            Note(shell, category);
        }

        /// <summary>
        /// Hangs a finished shell off a pivot at the middle of what it draws, and
        /// grows it there by however much the player asked for. The middle rather than
        /// the block's own origin, which is wherever its model was built around -- the
        /// base of a wheel, one end of a cannon -- and growing about that slides the
        /// shell off the block instead of wrapping it.
        /// </summary>
        private void OnPivot(GameObject shell, Transform under)
        {
            Bounds box;
            if (!Extent(shell, out box))
            {
                return;
            }
            GameObject pivot = new GameObject("Shell");
            pivot.transform.SetParent(under, false);
            pivot.transform.position = box.center;
            pivot.transform.rotation = Quaternion.identity;
            pivot.transform.localScale = Vector3.one;
            // Reparented while the pivot is still full size, so that what it keeps
            // is where the shell actually is; the growing comes after.
            shell.transform.SetParent(pivot.transform, true);
            pivot.transform.localScale = Vector3.one * DiffPalette.Shell;
            _pivots.Add(pivot.transform);
        }

        /// <summary>The box everything an object draws sits inside, in world space.</summary>
        private static bool Extent(GameObject of, out Bounds box)
        {
            box = new Bounds();
            Renderer[] drawn = of.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            for (int i = 0; i < drawn.Length; i++)
            {
                if (drawn[i] == null)
                {
                    continue;
                }
                if (!any) { box = drawn[i].bounds; any = true; }
                else { box.Encapsulate(drawn[i].bounds); }
            }
            return any;
        }

        /// <summary>
        /// Every shell's pivot, so the size can be changed while the diff is on screen
        /// instead of drawing it all again.
        /// </summary>
        private readonly List<Transform> _pivots = new List<Transform>();

        /// <summary>
        /// The shells that are not under the overlay's container: the copies, which
        /// hang off the very block they mark and so have to be hidden and destroyed
        /// by hand.
        /// </summary>
        private readonly List<GameObject> _loose = new List<GameObject>();

        /// <summary>
        /// Grows or shrinks every shell on screen. Called while the player drags the
        /// size slider, so it touches nothing but the pivots.
        /// </summary>
        public void Rescale()
        {
            float swell = DiffPalette.Shell;
            for (int i = 0; i < _pivots.Count; i++)
            {
                if (_pivots[i] != null)
                {
                    _pivots[i].localScale = Vector3.one * swell;
                }
            }
        }

        private GameObject Spawn(BlockRecord block)
        {
            try
            {
                BlockPrefab prefab;
                if (!PrefabMaster.GetPrefab((BlockType)block.Kind, out prefab)
                    || prefab == null || prefab.ghost == null)
                {
                    return null;
                }

                GameObject ghost = UnityEngine.Object.Instantiate(prefab.ghost) as GameObject;
                if (ghost != null)
                {
                    // Off before anything on it gets a frame to run in. Draw turns
                    // it back on once it has been placed and painted.
                    ghost.SetActive(false);
                    Sterilise(ghost);
                }
                return ghost;
            }
            catch (Exception e)
            {
                Log.Warn("could not spawn a ghost for block type " + block.Kind + ": " +
                         e.Message);
                return null;
            }
        }

        /// <summary>
        /// Strips a placement ghost down to the part of it that draws.
        ///
        /// A ghost is not the inert model it looks like: it carries
        /// <c>GhostTrigger</c> and sometimes <c>GhostPinTrigger</c>, which turn the
        /// preview red inside something and put the game's INTERSECTION warning on
        /// screen. Every ghost this mod draws sits exactly on a block of the machine,
        /// so a diff of a dozen blocks would raise a dozen warnings the moment it
        /// appeared. The behaviours go, and the colliders with them.
        /// </summary>
        private static void Sterilise(GameObject ghost)
        {
            MonoBehaviour[] behaviours = ghost.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] == null)
                {
                    continue;
                }
                // Disabled as well as destroyed: Destroy takes effect at the end of
                // the frame, and one Update in between is a warning on screen.
                behaviours[i].enabled = false;
                UnityEngine.Object.Destroy(behaviours[i]);
            }

            Collider[] colliders = ghost.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    colliders[i].enabled = false;
                    UnityEngine.Object.Destroy(colliders[i]);
                }
            }

            Rigidbody[] bodies = ghost.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i] != null)
                {
                    UnityEngine.Object.Destroy(bodies[i]);
                }
            }
        }

        /// <summary>
        /// Colours every surface of a ghost. Where a translucent shader can be found
        /// the ghost's materials are replaced outright, which is the only way to be
        /// sure of the colour; where one cannot, a property block is the fallback --
        /// it costs nothing and works if the ghost's own shader takes a tint.
        /// </summary>
        private void Paint(GameObject ghost, int category)
        {
            Color colour = DiffPalette.Of(category);
            Material replacement = MaterialFor(category);
            Renderer[] renderers = ghost.GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                renderer.receiveShadows = false;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                if (replacement != null)
                {
                    Material[] slots = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                    for (int slot = 0; slot < slots.Length; slot++)
                    {
                        slots[slot] = replacement;
                    }
                    renderer.sharedMaterials = slots;
                    continue;
                }

                MaterialPropertyBlock tint = new MaterialPropertyBlock();
                tint.SetColor("_Color", colour);
                tint.SetColor("_TintColor", colour);
                renderer.SetPropertyBlock(tint);
            }
        }

        private Material MaterialFor(int category)
        {
            if (_paint[category] != null)
            {
                return _paint[category];
            }

            Shader shader = TransparentShader();
            if (shader == null)
            {
                return null;
            }

            Material material = new Material(shader);
            // Kept alive across scene loads for as long as the mod runs; there are
            // three of them and they are rebuilt if anything does collect them.
            material.hideFlags = HideFlags.HideAndDontSave;
            Tint(material, DiffPalette.Of(category));
            _paint[category] = material;
            return material;
        }

        /// <summary>
        /// Sets a colour on a material both ways round: <c>Particles/Alpha Blended</c>
        /// wants <c>_TintColor</c>, the rest want <c>_Color</c>, and setting one a
        /// shader does not have is free.
        /// </summary>
        private static void Tint(Material material, Color colour)
        {
            material.color = colour;
            if (material.HasProperty("_TintColor"))
            {
                material.SetColor("_TintColor", colour);
            }
        }

        private Shader TransparentShader()
        {
            if (_shaderSearched)
            {
                return _shader;
            }
            _shaderSearched = true;

            for (int i = 0; i < ShaderCandidates.Length; i++)
            {
                Shader found = Shader.Find(ShaderCandidates[i]);
                if (found != null)
                {
                    _shader = found;
                    return _shader;
                }
            }

            Log.Warn("no transparent shader in this build; tinting Besiege's own ghost " +
                     "material instead, which may not take a colour.");
            return null;
        }

        /// <summary>
        /// The transform saved block coordinates are relative to: whatever the
        /// machine's blocks are parented to. Taken off a block, the field holding it
        /// on <c>Machine</c> not being public; an empty machine has no block to ask,
        /// and then the machine's own transform is the same thing.
        /// </summary>
        private static Transform BlockRoot()
        {
            Machine machine = Machine.Active();
            if (machine == null)
            {
                return null;
            }
            try
            {
                List<BlockBehaviour> blocks = machine.BuildingBlocks;
                if (blocks != null)
                {
                    for (int i = 0; i < blocks.Count; i++)
                    {
                        if (blocks[i] != null && blocks[i].transform.parent != null)
                        {
                            return blocks[i].transform.parent;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn("could not find the machine's block root: " + e.Message);
            }
            return machine.transform;
        }
    }
}
