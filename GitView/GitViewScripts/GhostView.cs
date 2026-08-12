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
                drawn += Draw(blocks, category, Swell(category));
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

            GameObject tube;
            try
            {
                tube = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            }
            catch (Exception e)
            {
                Log.Warn("could not draw a dragged block's span: " + e.Message);
                return false;
            }
            // CreatePrimitive brings a collider, which is exactly what the placement
            // ghosts had to have taken off them.
            Sterilise(tube);

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
