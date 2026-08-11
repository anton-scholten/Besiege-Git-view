using System;
using System.Collections.Generic;
using UnityEngine;

namespace GitView
{
    /// <summary>
    /// Draws a diff over the machine in the build area: what the version added in
    /// green, what it moved or retuned in orange, and what it deleted in red, the
    /// deleted blocks standing where they used to be.
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
        /// <summary>How much larger than the real block an added/changed shell sits.</summary>
        private const float ShellSwell = 1.06f;

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

        private Shader _shader;
        private bool _shaderSearched;

        /// <summary>True while a diff is being shown.</summary>
        public bool Showing
        {
            get { return _container != null; }
        }

        public void Show(DiffResult diff)
        {
            Clear();
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

            int drawn = 0;
            drawn += Draw(diff.Removed, DiffPalette.Removed, 1f);
            drawn += Draw(diff.Added, DiffPalette.Added, ShellSwell);
            drawn += Draw(diff.Changed, DiffPalette.Changed, ShellSwell);

            if (drawn == 0 && !diff.IsEmpty)
            {
                Log.Warn("the diff has " + (diff.Added.Count + diff.Changed.Count +
                         diff.Removed.Count) + " blocks in it but none could be drawn; " +
                         "the block types may not have placement ghosts.");
            }
        }

        public void Clear()
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
            if (_container != null && _container.activeSelf != visible)
            {
                _container.SetActive(visible);
            }
        }

        private int Draw(List<BlockRecord> blocks, int category, float swell)
        {
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
            GameObject ghost = Spawn(block);
            if (ghost == null)
            {
                return false;
            }

            ghost.transform.SetParent(_container.transform, false);
            ghost.transform.localPosition = block.Position;
            ghost.transform.localRotation = block.Rotation;
            ghost.transform.localScale = block.Scale * swell;
            Paint(ghost, category);
            ghost.SetActive(true);

            if (_shells[category] == null)
            {
                _shells[category] = new List<GameObject>();
            }
            _shells[category].Add(ghost);
            return true;
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
