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
    /// shows while you drag a block out of the menu. Every block type has one, it
    /// is already the right shape, and it is already visual-only: no colliders, no
    /// physics, nothing the game will mistake for part of the machine.
    /// </summary>
    public class GhostView
    {
        /// <summary>How much larger than the real block an added/changed shell sits.</summary>
        private const float ShellSwell = 1.06f;

        // Alpha is low: several of these overlap on a dense machine, and uGUI's
        // rule holds in 3D too -- two translucent surfaces in front of each other
        // composite darker than either.
        private static readonly Color AddedColour = new Color(0.24f, 0.90f, 0.36f, 0.42f);
        private static readonly Color ChangedColour = new Color(1.00f, 0.62f, 0.09f, 0.45f);
        private static readonly Color RemovedColour = new Color(0.95f, 0.20f, 0.22f, 0.38f);

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
        private readonly Dictionary<int, Material> _materials = new Dictionary<int, Material>();
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
            drawn += Draw(diff.Removed, RemovedColour, 1f);
            drawn += Draw(diff.Added, AddedColour, ShellSwell);
            drawn += Draw(diff.Changed, ChangedColour, ShellSwell);

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

        private int Draw(List<BlockRecord> blocks, Color colour, float swell)
        {
            int drawn = 0;
            for (int i = 0; i < blocks.Count; i++)
            {
                if (Draw(blocks[i], colour, swell))
                {
                    drawn++;
                }
            }
            return drawn;
        }

        private bool Draw(BlockRecord block, Color colour, float swell)
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
            Paint(ghost, colour);
            ghost.SetActive(true);
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
                return UnityEngine.Object.Instantiate(prefab.ghost) as GameObject;
            }
            catch (Exception e)
            {
                Log.Warn("could not spawn a ghost for block type " + block.Kind + ": " +
                         e.Message);
                return null;
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
        private void Paint(GameObject ghost, Color colour)
        {
            Material replacement = MaterialFor(colour);
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

        private Material MaterialFor(Color colour)
        {
            Shader shader = TransparentShader();
            if (shader == null)
            {
                return null;
            }

            int key = ColourKey(colour);
            Material material;
            if (_materials.TryGetValue(key, out material) && material != null)
            {
                return material;
            }

            material = new Material(shader);
            material.color = colour;
            if (material.HasProperty("_TintColor"))
            {
                material.SetColor("_TintColor", colour);
            }
            // Kept alive across scene loads for as long as the mod runs; there are
            // three of them and they are rebuilt if anything does collect them.
            material.hideFlags = HideFlags.HideAndDontSave;
            _materials[key] = material;
            return material;
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

        private static int ColourKey(Color colour)
        {
            return Mathf.RoundToInt(colour.r * 255f) << 24
                 | Mathf.RoundToInt(colour.g * 255f) << 16
                 | Mathf.RoundToInt(colour.b * 255f) << 8
                 | Mathf.RoundToInt(colour.a * 255f);
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
