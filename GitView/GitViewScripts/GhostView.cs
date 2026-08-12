using System;
using System.Collections.Generic;
using UnityEngine;

namespace GitView
{
    /// <summary>
    /// Draws a diff over the machine in the build area: what the version added in
    /// green, what it moved or retuned in orange, and what it deleted in red, the
    /// deleted blocks standing where they used to be. What it left alone has a
    /// colour too, though that one starts turned off.
    ///
    /// Nothing here touches the machine. The green and orange marks are hollow
    /// shells spawned a few percent larger than the block they sit on, rather than
    /// a tint applied to the block's own renderers -- Besiege's blocks share their
    /// materials, so tinting one girder tints every girder, and putting the
    /// original back afterwards means being right about what it was. A shell is
    /// removed by deleting it.
    ///
    /// The shells are Besiege's own placement ghosts, the translucent preview it
    /// shows while you drag a block out of the menu. Every block type has one and
    /// it is already the right shape -- but it is not inert, and
    /// <see cref="Sterilise"/> is what makes it so.
    /// </summary>
    public class GhostView
    {
        /// <summary>How much larger than the real block a shell over it sits.</summary>
        private const float ShellSwell = 1.06f;

        /// <summary>
        /// How thick the tube drawn along a brace, a hose or a rope is. A little
        /// fatter than any of them, for the same reason the other shells are a
        /// little larger than their blocks: it has to be visible over the thing it
        /// is marking rather than inside it.
        /// </summary>
        private const float SpanWidth = 0.16f;

        /// <summary>
        /// Below this there is nothing to draw between the two ends. Braces are
        /// dragged, so a brace that was placed and never dragged has both ends in
        /// the same place, and a zero-length cylinder is a disc of noise.
        /// </summary>
        private const float ShortestSpan = 0.05f;

        private const string ContainerName = "GitView Diff Overlay";

        /// <summary>
        /// Shaders to draw a shell with, best first. Only shaders included in the
        /// player's build can be found by name, so this tries several and falls
        /// back to tinting the ghost's own material if none of them are there.
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
        /// One material per category, shared by every shell in it.
        ///
        /// Shared on purpose rather than one per colour: the player can drag a
        /// colour slider, and every shell of that category has to follow while the
        /// slider is moving. One material means that is a single assignment however
        /// many blocks are on screen, instead of hundreds of repaints a frame and a
        /// new material for every colour dragged through.
        /// </summary>
        private readonly Material[] _paint = new Material[DiffPalette.Categories];

        /// <summary>
        /// The shells of each category, kept only for the fallback path: where no
        /// translucent shader could be found the colour lives in a property block
        /// per renderer, and changing it means going round them again.
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
        /// The diff on show, kept so it can be drawn again.
        ///
        /// The shells are parented into the machine, and a level change destroys
        /// the machine -- so an overlay can disappear without anybody asking it to.
        /// This is what tells that apart from having been cleared, and what makes
        /// putting it back possible.
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

            // Which layer the machine's own blocks are drawn on. A GameObject built
            // from nothing -- the tube along a brace, the slab over a build surface
            // -- starts on the default layer, which the build area's camera need not
            // be drawing at all; the ghosts do not have the problem because they
            // arrive on whatever layer Besiege authored them for.
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
            int surfaces = 0;
            _surfaces = 0;
            for (int category = 0; category < DiffPalette.Categories; category++)
            {
                List<BlockRecord> blocks = Blocks(diff, category);
                drawn += Draw(blocks, category, Swell(category));
                asked += DiffPalette.Faded(category) ? 0 : blocks.Count;
                surfaces += Surfaces(blocks);
            }
            if (surfaces > 0 || _surfaces > 0)
            {
                Log.Info("the diff holds " + surfaces + " build surface(s), of which " +
                         _surfaces + " knew their own corners.");
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
        /// The layer a machine's blocks are drawn on, taken off the first thing under
        /// it that draws. Falls back to the root's own layer, which is right when the
        /// root is a block rather than an empty parent.
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

        /// <summary>
        /// How many of these blocks are build surfaces, resolved or not: a surface
        /// that could not be followed to its corners is the one thing here that looks
        /// like nothing at all, so it is worth being able to tell the two apart in
        /// the log.
        /// </summary>
        private static int Surfaces(List<BlockRecord> blocks)
        {
            int found = 0;
            for (int i = 0; i < blocks.Count; i++)
            {
                if (blocks[i] != null && blocks[i].EdgeIds != null)
                {
                    found++;
                }
            }
            return found;
        }

        /// <summary>The blocks of one category. The overlay's four are the diff's three and what it left alone.</summary>
        private static List<BlockRecord> Blocks(DiffResult diff, int category)
        {
            if (category == DiffPalette.Added) { return diff.Added; }
            if (category == DiffPalette.Changed) { return diff.Changed; }
            if (category == DiffPalette.Removed) { return diff.Removed; }
            return diff.Unchanged;
        }

        /// <summary>
        /// How much larger than the block a shell sits. A removed block has nothing
        /// left where it was, so its shell is the block; every other category wraps
        /// a block that is still there and has to be a little larger than it to be
        /// seen at all.
        /// </summary>
        private static float Swell(int category)
        {
            return category == DiffPalette.Removed ? 1f : ShellSwell;
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
            if (_container != null)
            {
                UnityEngine.Object.Destroy(_container);
                _container = null;
            }
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
        /// Picks the overlay up on a colour the player has just changed.
        ///
        /// Cheap enough to call from a slider that is being dragged, which is the
        /// point: the shells recolour under the pointer rather than after it. Where
        /// a shared material is doing the work that is one assignment; only the
        /// no-shader fallback has to walk the shells.
        /// </summary>
        public void Refresh()
        {
            for (int category = 0; category < DiffPalette.Categories; category++)
            {
                // A category dragged to nothing has no shells to recolour, and one
                // dragged off nothing has none yet. Crossing that line is the only
                // thing a colour change can do that a repaint cannot answer, and it
                // costs one category's shells rather than the whole overlay.
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
        /// Shows or hides the overlay. Idempotent, because the window asks every
        /// frame whether the game has a menu up and this is the answer it acts on.
        /// </summary>
        public void SetVisible(bool visible)
        {
            // Remembered as well as applied, so an overlay redrawn while a menu is
            // up does not come back on top of it.
            _hidden = !visible;
            if (_container != null && _container.activeSelf != visible)
            {
                _container.SetActive(visible);
            }
        }

        /// <summary>
        /// Throws one category's shells away and spawns them again at the colour it
        /// is now. Only needed when a category has been turned on or off; a colour
        /// that merely changed is a material assignment.
        /// </summary>
        private void Redraw(int category)
        {
            List<GameObject> shells = _shells[category];
            if (shells != null)
            {
                for (int i = 0; i < shells.Count; i++)
                {
                    if (shells[i] != null)
                    {
                        UnityEngine.Object.Destroy(shells[i]);
                    }
                }
                shells.Clear();
            }

            _faded[category] = DiffPalette.Faded(category);
            if (_container == null || _showing == null)
            {
                return;
            }
            Draw(Blocks(_showing, category), category, Swell(category));
        }

        private int Draw(List<BlockRecord> blocks, int category, float swell)
        {
            // Recorded whether anything is drawn or not: it is what Refresh compares
            // against, and a category that drew nothing because it is switched off
            // has to be told apart from one that drew nothing because it is empty.
            _faded[category] = DiffPalette.Faded(category);
            if (_faded[category])
            {
                return 0;
            }

            int drawn = 0;
            for (int i = 0; i < blocks.Count; i++)
            {
                if (Draw(blocks[i], category, swell))
                {
                    drawn++;
                }
            }
            return drawn;
        }

        private bool Draw(BlockRecord block, int category, float swell)
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

            // The blocks whose shape is not in their prefab: a build surface, whose
            // outline is four corner blocks and whose edges are curves; a brace, a
            // spring, a rope or a hose, which is stretched between two points and
            // whose ghost is one end of it. What the machine is drawing for those is
            // the only thing that is actually their shape, so it is copied -- see
            // <see cref="CopyOfLive"/> -- and the shapes built here are what is left
            // when there is nothing to copy, which is a block the version on screen
            // deleted.
            if (block.HasSurface || block.HasSpan)
            {
                GameObject copied = CopyOfLive(block.Id);
                if (copied != null)
                {
                    Adopt(copied, category);
                    if (block.HasSurface)
                    {
                        _surfaces++;
                    }
                    return true;
                }
            }
            if (block.HasSurface && DrawSurface(block, category))
            {
                return true;
            }

            GameObject ghost = Spawn(block);
            if (ghost != null)
            {
                ghost.transform.localPosition = block.Position;
                ghost.transform.localRotation = block.Rotation;
                ghost.transform.localScale = block.Scale * swell;
                Adopt(ghost, category);
                drawn = true;
            }

            // A brace, a fuel line or a winch is not where its ghost is; it is
            // strung between two points, and the ghost is only one end of it.
            if (block.HasSpan)
            {
                drawn = DrawSpan(block, category) || drawn;
            }
            return drawn;
        }

        /// <summary>
        /// Draws a build surface as the surface: a slab through its corners, a little
        /// larger than the one it marks.
        ///
        /// Nothing Besiege can be asked for. A surface is nine blocks -- the surface,
        /// four edges, four corner nodes -- and the ghost spawned for the surface
        /// block itself is a mark at its own position, which is one of the corners:
        /// a changed surface came out as one coloured corner with the rest of it
        /// still the colour of the machine. The corners and the edges have no
        /// placement ghost at all, being blocks nobody drags out of the menu.
        ///
        /// So the shape is built here, out of the corners the file names -- see
        /// <see cref="BlockRecord.Corners"/>. Marks on the corners were drawn as well
        /// for a while and are not any more: the whole surface says everything they
        /// said, and it says it over the thing that changed rather than at its edges.
        ///
        /// False if the shape could not be built, and then the caller falls back to
        /// the block's own ghost: a mark at one corner is a poor way to show a
        /// surface, and it is a great deal better than showing nothing.
        /// </summary>
        private bool DrawSurface(BlockRecord block, int category)
        {
            _surfaces++;
            GameObject sheet = Sheet(block.Corners, block.Thickness);
            if (sheet == null)
            {
                return false;
            }
            Adopt(sheet, category);
            return true;
        }

        /// <summary>
        /// A copy of what the machine is drawing for this block, or null if the block
        /// is not in the machine -- which is the case for anything the version on
        /// screen removed.
        ///
        /// For the blocks whose shape is not in their prefab. A build surface's
        /// outline is four other blocks and its edges are curves; a brace, a spring,
        /// a rope or a hose is stretched between two points its ghost knows nothing
        /// about. Both were being approximated -- a flat slab through the corners, a
        /// tube along the span -- and the machine standing in front of the player has
        /// the real thing on it, generated by the game from the same data.
        ///
        /// The meshes are copied, not the block. Instantiating a live block runs its
        /// Awake, and a <c>BuildSurface</c> waking up registers itself with the
        /// machine it finds itself in: the copy would be a real surface, in the real
        /// machine, in the next save. A GameObject with a MeshFilter and a
        /// MeshRenderer on it has no behaviour to run and cannot join anything.
        /// </summary>
        private GameObject CopyOfLive(string id)
        {
            GameObject real = LiveBlock(id);
            if (real == null || _container == null)
            {
                return null;
            }

            Renderer[] drawn = real.GetComponentsInChildren<Renderer>(false);
            Bounds box = new Bounds();
            bool any = false;
            for (int i = 0; i < drawn.Length; i++)
            {
                if (drawn[i] == null || !drawn[i].enabled)
                {
                    continue;
                }
                if (!any) { box = drawn[i].bounds; any = true; }
                else { box.Encapsulate(drawn[i].bounds); }
            }
            if (!any)
            {
                return null;
            }

            // The holder stands at the middle of what is being copied, so that
            // growing it a little -- the same trick every other shell plays, to be
            // outside the thing it marks rather than fighting it for the same pixels
            // -- grows it evenly instead of sliding it off the block.
            GameObject holder = new GameObject("Surface");
            holder.transform.SetParent(_container.transform, false);
            holder.transform.position = box.center;
            holder.transform.rotation = Quaternion.identity;

            int made = 0;
            for (int i = 0; i < drawn.Length; i++)
            {
                MeshFilter shape = drawn[i] == null || !drawn[i].enabled
                    ? null : drawn[i].GetComponent<MeshFilter>();
                if (shape == null || shape.sharedMesh == null)
                {
                    continue;
                }
                GameObject piece = new GameObject("Face", typeof(MeshFilter),
                                                  typeof(MeshRenderer));
                piece.GetComponent<MeshFilter>().sharedMesh = shape.sharedMesh;
                piece.transform.SetParent(holder.transform, false);
                piece.transform.position = drawn[i].transform.position;
                piece.transform.rotation = drawn[i].transform.rotation;
                piece.transform.localScale = Ratio(drawn[i].transform.lossyScale,
                                                   holder.transform.lossyScale);
                made++;
            }
            if (made == 0)
            {
                UnityEngine.Object.Destroy(holder);
                return null;
            }

            holder.transform.localScale = Vector3.one * SurfaceSwell;
            OnMachineLayer(holder);
            return holder;
        }

        /// <summary>One scale as a fraction of another, per axis.</summary>
        private static Vector3 Ratio(Vector3 want, Vector3 by)
        {
            return new Vector3(Mathf.Abs(by.x) < 0.0001f ? 1f : want.x / by.x,
                               Mathf.Abs(by.y) < 0.0001f ? 1f : want.y / by.y,
                               Mathf.Abs(by.z) < 0.0001f ? 1f : want.z / by.z);
        }

        /// <summary>
        /// The block in the machine with this identifier, or null. Looked up through
        /// a table built once per diff: a machine can hold a thousand blocks and a
        /// diff can hold a hundred surfaces.
        /// </summary>
        private GameObject LiveBlock(string id)
        {
            if (_live == null)
            {
                _live = new Dictionary<string, GameObject>();
                try
                {
                    Machine machine = Machine.Active();
                    List<BlockBehaviour> blocks = machine == null
                        ? null : machine.BuildingBlocks;
                    for (int i = 0; blocks != null && i < blocks.Count; i++)
                    {
                        if (blocks[i] != null)
                        {
                            _live[blocks[i].Guid.ToString()] = blocks[i].gameObject;
                        }
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
        /// The machine's blocks by identifier, for the length of one diff. Thrown
        /// away with the overlay, since the next one is drawn over a machine that has
        /// been loaded again.
        /// </summary>
        private Dictionary<string, GameObject> _live;

        /// <summary>
        /// How many build surfaces the last diff drew, for the log. Worth counting:
        /// a surface that resolved and one that did not look the same on screen
        /// unless you know which you are looking at.
        /// </summary>
        private int _surfaces;

        /// <summary>
        /// How far outside the real slab the mark round it is drawn: a little proud
        /// of each face, and a little wider than the outline.
        ///
        /// It has to be *outside*. A build surface is a slab with a thickness the
        /// player sets, so a flat sheet through its corners lies on the middle of it
        /// -- sealed inside the block, drawn every frame and visible from nowhere,
        /// which is exactly what the first attempt at this did. The other shells have
        /// the same problem and solve it the same way, by being a few per cent larger
        /// than the block they mark.
        /// </summary>
        private const float SurfaceSkin = 0.05f;
        private const float SurfaceSwell = 1.02f;

        /// <summary>
        /// A slab through the given corners, a little larger than the surface it
        /// marks: two faces and the rim between them.
        ///
        /// A mesh of ours rather than a primitive: the outline is a shape the player
        /// dragged into whatever form they liked, and no primitive is that shape.
        ///
        /// Three things here are deliberately the blunt version, because a surface
        /// that is not drawn is worse than one drawn a shade too generously:
        ///
        /// - The faces are fans from the *middle* rather than from the first corner,
        ///   so an outline pulled into a dart is still covered.
        /// - The plane is Newell's normal -- the whole outline rather than its first
        ///   three corners, which say nothing when they happen to be in a line.
        /// - Every triangle is wound both ways. Which way round the loop the walk
        ///   came out is the file's business, so "the front" is not knowable, and a
        ///   slab facing away from you is a slab you cannot see.
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
                // plane's normal for any polygon and does not care which three
                // corners you happen to look at first.
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

                GameObject sheet = new GameObject("Surface", typeof(MeshFilter),
                                                  typeof(MeshRenderer));
                sheet.GetComponent<MeshFilter>().sharedMesh = mesh;
                OnMachineLayer(sheet);
                Log.Info("drew a build surface of " + n + " corners, " +
                         mesh.bounds.size + " across.");
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
        /// Draws the part of a two-ended block that is neither of its ends: the
        /// brace itself, the length of hose, the rope.
        ///
        /// The two ends are stored in the block's own local space -- Besiege saves
        /// them through <c>transform.InverseTransformPoint</c> and loads them back
        /// through <c>TransformPoint</c> -- so putting them back means the block's
        /// rotation and its scale, in that order. The overlay's container sits on
        /// the same transform the blocks are parented to, which is the space the
        /// saved position and rotation are already in, so no more than that is
        /// needed.
        ///
        /// A cylinder of Unity's rather than another ghost: there is no prefab for
        /// "the middle of a brace" to instantiate, and the shape is a tube between
        /// two points whatever the block is.
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
        /// Rotation then scale, because that is the order a transform applies them
        /// and these came out of one.
        /// </summary>
        private static Vector3 Point(BlockRecord block, Vector3 local)
        {
            return block.Position + block.Rotation * Vector3.Scale(local, block.Scale);
        }

        /// <summary>Parents a finished shell into the overlay and paints it.</summary>
        private void Adopt(GameObject shell, int category)
        {
            shell.transform.SetParent(_container.transform, false);
            Paint(shell, category);
            shell.SetActive(true);

            if (_shells[category] == null)
            {
                _shells[category] = new List<GameObject>();
            }
            _shells[category].Add(shell);
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
        /// A ghost is not the inert model it looks like. It carries
        /// <c>GhostTrigger</c> and, on some blocks, <c>GhostPinTrigger</c>: the
        /// behaviours that turn the preview red while it is inside something, and
        /// that put the game's INTERSECTION warning on screen when it is. They work
        /// off trigger colliders, and every ghost this mod draws sits exactly on a
        /// block of the machine -- so a diff of a dozen blocks raises a dozen
        /// intersection warnings the moment it appears, which is what the player
        /// sees: a warning every single time they click a version.
        ///
        /// The behaviours go, and the colliders with them. Nothing here is meant to
        /// interact with anything; it is a coloured shape hanging in the air.
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
                // the frame, and one Update in between is one warning on screen.
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
        /// Colours every surface of a ghost.
        ///
        /// Where a translucent shader can be found, the ghost's own materials are
        /// replaced outright, which is the only way to be sure of the colour --
        /// Besiege's ghost material may not have a colour property to set at all.
        /// Where one cannot, a property block is the fallback: it costs nothing,
        /// touches no shared material, and works if the ghost shader does happen to
        /// take a tint.
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
        /// Sets a colour on a material both ways round. Which of the two properties
        /// a shader actually reads depends on which shader was found:
        /// <c>Particles/Alpha Blended</c> wants <c>_TintColor</c>, the rest want
        /// <c>_Color</c>, and setting one a shader does not have is free.
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
                    Log.Info("drawing the overlay with the '" + ShaderCandidates[i] +
                             "' shader.");
                    return _shader;
                }
            }

            Log.Warn("no transparent shader in this build; tinting Besiege's own ghost " +
                     "material instead, which may not take a colour.");
            return null;
        }

        /// <summary>
        /// The transform saved block coordinates are relative to: whatever the
        /// machine's own blocks are parented to. Taken off a block rather than
        /// named directly, because the field holding it on <c>Machine</c> is not
        /// public. An empty machine has no block to ask, and then the machine's own
        /// transform is the same thing.
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
