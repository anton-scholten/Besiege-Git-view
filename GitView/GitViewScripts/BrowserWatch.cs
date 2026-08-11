using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GitView
{
    /// <summary>
    /// Puts a compare button on the machines in the load screen that have a saved
    /// history, and opens the history window when it is pressed.
    ///
    /// The load screen is not uGUI. Its slots are mesh objects in world space with
    /// <c>SimpleUIButton</c> colliders on them, so there is no prefab of Besiege's
    /// to instantiate and no layout group to add a child to. The button is
    /// therefore a copy of one of the slot's own buttons, which brings the right
    /// mesh, material, collider and press behaviour with it, wearing a face drawn
    /// by <see cref="IconArt"/>.
    ///
    /// Besiege already puts a "versions" button on the same slots, but all it does
    /// is navigate the browser into the autosave folder, which leaves you looking
    /// at a hundred files called "aut 26.06.27 15-34-37". This is the button that
    /// answers what actually changed.
    /// </summary>
    public class BrowserWatch : MonoBehaviour
    {
        /// <summary>Name given to the buttons this mod adds, so they are recognisable.</summary>
        public const string ButtonName = "GitViewCompareButton";

        private const float PollSeconds = 0.25f;
        private const float IndexSeconds = 5f;
        private const int IconSize = 128;

        /// <summary>
        /// How big the branch glyph is drawn against the button it replaces.
        ///
        /// Matching that button exactly comes out looking heavier than the game's
        /// own icons: a trash can is drawn with white space around it inside its
        /// sprite, and the branch fills nearly all of its own. Three quarters is
        /// what puts the two at the same weight beside each other.
        /// </summary>
        private const float IconScale = 0.75f;

        /// <summary>
        /// Shaders to draw the button's face with, best first. Only shaders
        /// included in the player's build can be found by name, so this tries
        /// several. `Particles/Alpha Blended` is known to be in Besiege's.
        /// </summary>
        private static readonly string[] IconShaders =
        {
            "Unlit/Transparent",
            "Sprites/Default",
            "Particles/Alpha Blended",
            "Transparent/Diffuse"
        };

        /// <summary>
        /// How many sweeps to wait for a copied button's face to appear before
        /// drawing the glyph on a surface of our own instead.
        /// </summary>
        private const int PaintAttempts = 8;

        private static Mesh _quad;
        private static readonly Dictionary<int, Sprite> _sprites = new Dictionary<int, Sprite>();

        private readonly List<GameObject> _unpainted = new List<GameObject>();
        private readonly List<int> _attempts = new List<int>();

        private static Texture2D _icon;

        private HistoryView _history;
        private Material _material;
        private bool _reportedFaces;
        private float _nextPoll;
        private float _indexedAt = -1000f;
        private HashSet<string> _withHistory = new HashSet<string>();
        private bool _reportedLayout;
        private bool _busy;

        public void Bind(HistoryView history)
        {
            _history = history;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextPoll)
            {
                return;
            }
            _nextPoll = Time.unscaledTime + PollSeconds;

            try
            {
                Sweep();
            }
            catch (Exception e)
            {
                Log.Warn("could not add the compare buttons: " + e.Message);
                // Back off rather than repeating the failure every quarter second.
                _nextPoll = Time.unscaledTime + 5f;
            }
        }

        private void Sweep()
        {
            RepaintPending();

            FileBrowserSlot[] slots = FindObjectsOfType<FileBrowserSlot>();
            if (slots == null || slots.Length == 0)
            {
                return;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                FileBrowserSlot slot = slots[i];
                if (slot == null || slot.transform.FindChild(ButtonName) != null)
                {
                    continue;
                }
                if (!ShouldOfferHistory(slot))
                {
                    continue;
                }
                AddButton(slot);
            }
        }

        // ------------------------------------------------------------------ deciding

        /// <summary>
        /// Whether this slot is a machine with a history behind it.
        ///
        /// Answered from one listing of the AutoSave folder rather than by walking
        /// the virtual filesystem per slot per poll: the folder names in there are
        /// exactly the machines that have versions, and there are at most a few
        /// dozen of them.
        /// </summary>
        private bool ShouldOfferHistory(FileBrowserSlot slot)
        {
            IVirtualObject item = slot.VirtualObject;
            if (item == null)
            {
                return false;
            }

            // A folder inside AutoSave is a machine's history in its own right.
            if (item.IsFolder)
            {
                VirtualFolder parent = item.Parent;
                return parent != null && parent.Name == VersionScan.AutoSaveFolder;
            }

            RefreshIndex(item);
            return _withHistory.Contains(item.Name);
        }

        private void RefreshIndex(IVirtualObject anchor)
        {
            if (Time.unscaledTime - _indexedAt < IndexSeconds)
            {
                return;
            }
            _indexedAt = Time.unscaledTime;

            HashSet<string> found = new HashSet<string>();
            try
            {
                VirtualFolder root = anchor.Parent;
                while (root != null && root.Parent != null)
                {
                    root = root.Parent;
                }
                if (root != null)
                {
                    foreach (IVirtualObject child in root.GetObjects())
                    {
                        if (child == null || !child.IsFolder ||
                            child.Name != VersionScan.AutoSaveFolder)
                        {
                            continue;
                        }
                        child.Open();
                        VirtualFolder autoSave = child as VirtualFolder;
                        if (autoSave == null)
                        {
                            continue;
                        }
                        foreach (IVirtualObject machine in autoSave.GetObjects())
                        {
                            if (machine != null && machine.IsFolder)
                            {
                                found.Add(machine.Name);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn("could not index the autosave folder: " + e.Message);
                return;
            }
            _withHistory = found;
        }

        // ------------------------------------------------------------------ building

        private void AddButton(FileBrowserSlot slot)
        {
            SimpleUIButton template = PickTemplate(slot);
            if (template == null)
            {
                return;
            }

            GameObject clone = Instantiate(template.gameObject) as GameObject;
            if (clone == null)
            {
                return;
            }
            clone.name = ButtonName;
            clone.transform.SetParent(slot.transform, false);
            clone.transform.localRotation = template.transform.localRotation;
            clone.transform.localScale = template.transform.localScale;
            clone.transform.localPosition = FreeSpot(slot, template);
            clone.SetActive(true);

            Remember(clone);

            SimpleUIButton button = clone.GetComponent<SimpleUIButton>();
            if (button == null)
            {
                Destroy(clone);
                return;
            }
            // A clone carries the prefab's serialised state but not its delegates,
            // since those are not serialised. Clearing them anyway costs nothing and
            // means a future Besiege that does wire them up cannot leave this button
            // also deleting the machine.
            button.ResetDelegates();

            FileBrowserSlot owner = slot;
            button.Click += new Click(delegate { OnPressed(owner); });
        }

        /// <summary>
        /// Which of the slot's own buttons to copy. An active one is preferred --
        /// it is definitely laid out and definitely visible -- and one with
        /// something to draw is preferred over one without.
        /// </summary>
        private static SimpleUIButton PickTemplate(FileBrowserSlot slot)
        {
            SimpleUIButton[] buttons = slot.GetComponentsInChildren<SimpleUIButton>(true);
            SimpleUIButton best = null;
            int bestScore = -1;

            for (int i = 0; i < buttons.Length; i++)
            {
                SimpleUIButton button = buttons[i];
                if (button == null || button.gameObject == slot.gameObject ||
                    button.name == ButtonName)
                {
                    continue;
                }
                int score = 0;
                if (button.gameObject.activeInHierarchy) { score += 2; }
                if (button.GetComponent<MeshRenderer>() != null) { score += 1; }
                if (score > bestScore)
                {
                    best = button;
                    bestScore = score;
                }
            }
            return best;
        }

        /// <summary>
        /// Where to put the button: the gap in the middle of the row of buttons
        /// along the bottom of a slot.
        ///
        /// Measured on a real slot: nine SimpleUIButtons, of which exactly one is
        /// on show -- the delete button, in a bottom corner at x = 1.2. So the
        /// opposite corner is the place, and the general rules below are for the
        /// slots that have more than one.
        ///
        /// All of it is worked out from where the slot's own buttons actually are
        /// rather than from numbers measured off a screenshot, because a hardcoded
        /// offset would not survive Besiege moving anything.
        ///
        /// An earlier version stepped one pitch left of the leftmost button, where
        /// pitch was the smallest gap between any two -- counting the hidden ones,
        /// two of which are 0.018 apart, so the new button landed on top of one.
        /// </summary>
        private Vector3 FreeSpot(FileBrowserSlot slot, SimpleUIButton template)
        {
            List<Vector3> places = ButtonPlaces(slot);
            float step = Mathf.Max(Width(slot, template), 0.05f) * 1.25f;
            Vector3 spot = new Vector3(template.transform.localPosition.x - step,
                                       template.transform.localPosition.y,
                                       template.transform.localPosition.z);

            if (places.Count == 1)
            {
                // The usual case, as it turns out: a slot has one button on show
                // and it is in a bottom corner. The opposite corner is its mirror
                // through the middle of the slot, which is both certainly free and
                // where a second button would have gone if Besiege had one.
                spot = new Vector3(-places[0].x, places[0].y, places[0].z);
            }
            else if (places.Count >= 2)
            {
                // The bottom row: everything within a button's height of the lowest.
                float lowest = float.MaxValue;
                for (int i = 0; i < places.Count; i++)
                {
                    lowest = Mathf.Min(lowest, places[i].y);
                }

                float left = float.MaxValue;
                float right = float.MinValue;
                float z = spot.z;
                for (int i = 0; i < places.Count; i++)
                {
                    if (places[i].y - lowest > step)
                    {
                        continue;
                    }
                    left = Mathf.Min(left, places[i].x);
                    right = Mathf.Max(right, places[i].x);
                    z = places[i].z;
                }
                if (right > left)
                {
                    spot = new Vector3((left + right) * 0.5f, lowest, z);
                }
            }

            // If the middle turns out to be taken on some kind of slot, move up a
            // row rather than sitting on top of whatever is there.
            for (int attempt = 0; attempt < 3 && !IsClear(places, spot, step * 0.8f); attempt++)
            {
                spot = new Vector3(spot.x, spot.y + step, spot.z);
            }

            ReportLayout(places, spot, step);
            return spot;
        }

        private static List<Vector3> ButtonPlaces(FileBrowserSlot slot)
        {
            List<Vector3> places = new List<Vector3>();
            SimpleUIButton[] buttons = slot.GetComponentsInChildren<SimpleUIButton>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                SimpleUIButton button = buttons[i];
                // Only the ones that are actually on screen: a slot carries hidden
                // buttons for uploading, cloud sync and confirming a delete, and
                // avoiding places nobody can see would use up the whole slot.
                if (button != null && button.gameObject != slot.gameObject &&
                    button.name != ButtonName && button.gameObject.activeInHierarchy)
                {
                    places.Add(button.transform.localPosition);
                }
            }
            return places;
        }

        private static bool IsClear(List<Vector3> places, Vector3 spot, float clearance)
        {
            for (int i = 0; i < places.Count; i++)
            {
                if (Mathf.Abs(places[i].x - spot.x) < clearance &&
                    Mathf.Abs(places[i].y - spot.y) < clearance)
                {
                    return false;
                }
            }
            return true;
        }

        private void ReportLayout(List<Vector3> places, Vector3 spot, float step)
        {
            if (_reportedLayout)
            {
                return;
            }
            _reportedLayout = true;

            StringBuilder found = new StringBuilder();
            for (int i = 0; i < places.Count; i++)
            {
                found.Append(i == 0 ? "" : " ").Append(places[i]);
            }
            Log.Info("slot buttons at " + found + "; compare button at " + spot +
                     " (step " + step.ToString("0.###") + ").");
        }

        /// <summary>A button's width in the slot's own units, not the world's.</summary>
        private static float Width(FileBrowserSlot slot, SimpleUIButton button)
        {
            float scale = Mathf.Abs(slot.transform.lossyScale.x);
            if (scale < 0.0001f)
            {
                scale = 1f;
            }

            Renderer renderer = button.GetComponentInChildren<Renderer>(true);
            if (renderer != null && renderer.bounds.size.x > 0.0001f)
            {
                return renderer.bounds.size.x / scale;
            }
            Collider collider = button.GetComponent<Collider>();
            if (collider != null && collider.bounds.size.x > 0.0001f)
            {
                return collider.bounds.size.x / scale;
            }
            return 0.5f;
        }

        /// <summary>
        /// Tries to put our glyph on every copy that is still wearing the icon it
        /// was cloned from, and drops the ones that are done or gone.
        ///
        /// Retried across frames rather than done once at clone time, because a
        /// slot button's face need not exist in the frame it is cloned: whatever
        /// builds it runs in an Awake or a Start of its own. Asking too early finds
        /// a button with no mesh anywhere on it, which is what the log said was
        /// happening.
        /// </summary>
        private void RepaintPending()
        {
            for (int i = _unpainted.Count - 1; i >= 0; i--)
            {
                if (_unpainted[i] == null || Paint(_unpainted[i]))
                {
                    _unpainted.RemoveAt(i);
                }
                else
                {
                    _attempts[i]++;
                    if (_attempts[i] < PaintAttempts)
                    {
                        continue;
                    }
                    // Out of patience: draw the glyph on a surface of our own
                    // rather than leave the button wearing a delete icon.
                    Overlay(_unpainted[i]);
                    _unpainted.RemoveAt(i);
                }
            }
        }

        private void Remember(GameObject button)
        {
            _unpainted.Add(button);
            _attempts.Add(0);
            // The first go is free: if the face is already there this is done in
            // the frame the button appears, and nothing flickers.
            RepaintPending();
        }

        /// <summary>
        /// Repaints whatever the copied button actually draws with.
        ///
        /// A sweep over every renderer and every kind of renderer, because what a
        /// slot button is made of cannot be checked from out here. Two ways of
        /// doing this have failed silently in game: setting
        /// `material.mainTexture` (Besiege's shader need not sample it), and
        /// parenting a quad of our own to the face (invisible). Assigning a whole
        /// material to the button's own renderer keeps Besiege's geometry — right
        /// plane, right winding, right size — and changes only what we chose.
        /// </summary>
        private bool Paint(GameObject button)
        {
            Material material = IconMaterial();
            if (material == null)
            {
                // No shader to draw with. Nothing will change on a later frame
                // either, so stop asking.
                return true;
            }

            Renderer[] renderers = button.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return false;
            }
            ReportFaces(renderers);

            Renderer painted = null;
            for (int i = 0; i < renderers.Length && painted == null; i++)
            {
                if (PaintOne(renderers[i], material))
                {
                    painted = renderers[i];
                }
            }
            if (painted == null)
            {
                return false;
            }

            // Anything else it draws goes off. A button can be a glyph on a backing
            // plate, and leaving the glyph on shows the icon we are replacing
            // straight over the top of ours.
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != painted)
                {
                    renderers[i].enabled = false;
                }
            }
            return true;
        }

        private static bool PaintOne(Renderer renderer, Material material)
        {
            if (renderer == null)
            {
                return false;
            }

            // A sprite ignores its material's texture and draws its own. This is
            // what a slot button turns out to be.
            SpriteRenderer sprite = renderer as SpriteRenderer;
            if (sprite != null)
            {
                Sprite replacement = IconSprite(sprite.sprite);
                if (replacement == null)
                {
                    return false;
                }
                sprite.sprite = replacement;
                sprite.color = Color.white;
                return true;
            }

            SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
            if (skinned != null)
            {
                if (skinned.sharedMesh == null)
                {
                    return false;
                }
                skinned.sharedMaterials = new Material[] { material };
                return true;
            }

            MeshFilter shape = renderer.GetComponent<MeshFilter>();
            if (shape == null || shape.sharedMesh == null)
            {
                return false;
            }
            Mesh unwrapped = Unwrapped(shape.sharedMesh);
            if (unwrapped != null)
            {
                shape.sharedMesh = unwrapped;
            }
            renderer.sharedMaterials = new Material[] { material };
            return true;
        }

        /// <summary>
        /// The last resort: our own quad, sized and placed from the button's
        /// collider.
        ///
        /// Only reached when nothing the button draws could be repainted after
        /// several frames of trying. The collider is the one part of a
        /// SimpleUIButton that has to exist — it is what makes it clickable — so
        /// its bounds are a size and a place that can always be had. The quad is
        /// wound both ways, because which side of it faces the camera is exactly
        /// the sort of thing that cannot be known from here.
        /// </summary>
        private void Overlay(GameObject button)
        {
            Material material = IconMaterial();
            Collider box = button.GetComponentInChildren<Collider>(true);
            if (material == null || box == null || button.transform.FindChild("Icon") != null)
            {
                return;
            }

            float scale = Mathf.Abs(button.transform.lossyScale.x);
            if (scale < 0.0001f)
            {
                scale = 1f;
            }
            Vector3 size = box.bounds.size;
            float side = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) / scale * 0.8f;

            GameObject icon = new GameObject("Icon", typeof(MeshFilter), typeof(MeshRenderer));
            icon.transform.SetParent(button.transform, false);
            icon.transform.localPosition = Vector3.zero;
            icon.transform.localRotation = Quaternion.identity;
            icon.transform.localScale = new Vector3(side, side, 1f);
            icon.layer = button.layer;
            icon.GetComponent<MeshFilter>().sharedMesh = DoubleSidedQuad();
            icon.GetComponent<MeshRenderer>().sharedMaterial = material;
            Log.Info("drew the branch icon on a quad of our own, " + side.ToString("0.###") +
                     " across.");
        }

        private static Mesh DoubleSidedQuad()
        {
            if (_quad != null)
            {
                return _quad;
            }
            _quad = new Mesh();
            _quad.name = "GitViewIconQuad";
            _quad.vertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f)
            };
            _quad.uv = new Vector2[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f)
            };
            // The same two triangles wound both ways, so back-face culling cannot
            // be what makes the icon invisible.
            _quad.triangles = new int[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };
            _quad.RecalculateNormals();
            _quad.hideFlags = HideFlags.HideAndDontSave;
            return _quad;
        }

        /// <summary>
        /// Our glyph as a sprite the same size on screen as the one it replaces.
        ///
        /// A sprite is drawn <c>rect.height / pixelsPerUnit</c> units tall, and
        /// <c>Sprite.Create</c> assumes 100 pixels per unit unless told otherwise.
        /// Our texture is not the size of Besiege's, so taking that default drew
        /// the branch at about half the trash can it sat next to. Matching the
        /// replaced sprite's world height instead makes the two the same whatever
        /// either texture happens to be.
        /// </summary>
        private static Sprite IconSprite(Sprite replacing)
        {
            Texture2D texture = IconTexture();
            float density = 100f;
            if (replacing != null && replacing.rect.height > 0f && replacing.pixelsPerUnit > 0f)
            {
                float worldHeight = replacing.rect.height / replacing.pixelsPerUnit * IconScale;
                density = texture.height / Mathf.Max(worldHeight, 0.0001f);
            }

            // Cached per size rather than one shared sprite: different slots can
            // carry differently authored buttons, and a sprite is cheap but not
            // free.
            int key = Mathf.RoundToInt(density * 10f);
            Sprite made;
            if (_sprites.TryGetValue(key, out made) && made != null)
            {
                return made;
            }

            made = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                                 new Vector2(0.5f, 0.5f), density);
            made.hideFlags = HideFlags.HideAndDontSave;
            _sprites[key] = made;
            Log.Info("branch icon sized at " + density.ToString("0.#") + " pixels per unit" +
                     (replacing == null ? " (nothing to match)" : " to match " + replacing.name));
            return made;
        }

        /// <summary>
        /// A copy of the button's quad with its texture coordinates spread over the
        /// whole of our icon.
        ///
        /// Besiege's UI meshes may carry coordinates into a shared atlas rather
        /// than 0..1, and a mesh cut out for a trash can would show that same
        /// corner of our texture. Remapping by position works whichever plane the
        /// quad lies in: the two axes it actually has a size in are the two the
        /// picture goes on.
        ///
        /// Returns null if the mesh cannot be read -- Unity refuses on meshes
        /// imported without read/write -- and the caller then keeps the original,
        /// which is right far more often than it is wrong.
        /// </summary>
        private static Mesh Unwrapped(Mesh original)
        {
            try
            {
                Vector3[] points = original.vertices;
                if (points == null || points.Length == 0)
                {
                    return null;
                }

                Bounds box = original.bounds;
                int across = WidestAxis(box.size, -1);
                int up = WidestAxis(box.size, across);
                float width = Mathf.Max(Component(box.size, across), 0.0001f);
                float height = Mathf.Max(Component(box.size, up), 0.0001f);

                Vector2[] coordinates = new Vector2[points.Length];
                for (int i = 0; i < points.Length; i++)
                {
                    coordinates[i] = new Vector2(
                        (Component(points[i], across) - Component(box.min, across)) / width,
                        (Component(points[i], up) - Component(box.min, up)) / height);
                }

                Mesh copy = UnityEngine.Object.Instantiate(original) as Mesh;
                copy.name = "GitViewIconQuad";
                copy.uv = coordinates;
                copy.hideFlags = HideFlags.HideAndDontSave;
                return copy;
            }
            catch (Exception e)
            {
                Log.Warn("could not remap the button's texture coordinates (" +
                         e.Message + "); using the ones it came with.");
                return null;
            }
        }

        /// <summary>The axis a box is largest along, ignoring one already taken.</summary>
        private static int WidestAxis(Vector3 size, int skip)
        {
            int widest = -1;
            for (int axis = 0; axis < 3; axis++)
            {
                if (axis == skip)
                {
                    continue;
                }
                if (widest < 0 || Component(size, axis) > Component(size, widest))
                {
                    widest = axis;
                }
            }
            return widest;
        }

        private static float Component(Vector3 vector, int axis)
        {
            return axis == 0 ? vector.x : (axis == 1 ? vector.y : vector.z);
        }

        /// <summary>
        /// Says once what the copied button is actually made of, and by what kind
        /// of renderer.
        ///
        /// Everything about this button has had to be inferred from a running game
        /// through a log file — three attempts at the icon, three silent failures —
        /// so the inventory is worth the one line it costs. The kinds are tested
        /// with `is` rather than asked for by name: Type.Name is
        /// System.Reflection.MemberInfo.get_Name, and one of those is enough for
        /// the mod loader to refuse the whole assembly.
        /// </summary>
        private void ReportFaces(Renderer[] renderers)
        {
            if (_reportedFaces)
            {
                return;
            }
            _reportedFaces = true;

            StringBuilder inventory = new StringBuilder();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                inventory.Append(i == 0 ? "" : ", ").Append(renderer.name).Append(' ');
                if (renderer is SpriteRenderer) { inventory.Append("sprite"); }
                else if (renderer is SkinnedMeshRenderer) { inventory.Append("skinned"); }
                else if (renderer is MeshRenderer) { inventory.Append("mesh"); }
                else { inventory.Append("other"); }

                MeshFilter shape = renderer.GetComponent<MeshFilter>();
                if (shape == null)
                {
                    inventory.Append(" no-filter");
                }
                else if (shape.sharedMesh == null)
                {
                    inventory.Append(" no-mesh");
                }
                else
                {
                    inventory.Append(' ').Append(shape.sharedMesh.bounds.size)
                             .Append(shape.sharedMesh.isReadable ? "" : " unreadable");
                }
            }
            Log.Info("copied button draws: " +
                     (renderers.Length == 0 ? "nothing" : inventory.ToString()));
        }

        private static Texture2D IconTexture()
        {
            if (_icon == null)
            {
                _icon = IconArt.Branch(IconSize);
                _icon.hideFlags = HideFlags.HideAndDontSave;
            }
            return _icon;
        }

        private Material IconMaterial()
        {
            if (_material != null)
            {
                return _material;
            }
            for (int i = 0; i < IconShaders.Length; i++)
            {
                Shader shader = Shader.Find(IconShaders[i]);
                if (shader == null)
                {
                    continue;
                }
                _material = new Material(shader);
                _material.mainTexture = IconTexture();
                _material.color = Color.white;
                // Particles/Alpha Blended doubles the texture against this, so a
                // plain white keeps the glyph at full strength there and changes
                // nothing under the shaders that ignore it.
                if (_material.HasProperty("_TintColor"))
                {
                    _material.SetColor("_TintColor", Color.white);
                }
                _material.hideFlags = HideFlags.HideAndDontSave;
                Log.Info("drawing the compare button with the '" + IconShaders[i] +
                         "' shader.");
                return _material;
            }

            Log.Warn("no transparent shader in this build to draw the compare button " +
                     "with; leaving it wearing the icon it was copied from.");
            return null;
        }


        // ------------------------------------------------------------------ pressing

        private void OnPressed(FileBrowserSlot slot)
        {
            if (_busy || slot == null || _history == null)
            {
                return;
            }
            IVirtualObject item = slot.VirtualObject;
            if (item == null)
            {
                return;
            }

            VirtualFolder folder = VersionScan.FolderFor(item);
            List<VersionEntry> versions = VersionScan.Versions(folder);
            if (versions.Count == 0)
            {
                Log.Console("no saved versions for " + item.Name + ".");
                return;
            }

            StartCoroutine(OpenHistory(item.Name, versions));
        }

        /// <summary>
        /// Closes the load screen, then opens the newest version and the history
        /// beside it.
        ///
        /// In that order and a frame apart: the browser is mid-click when this
        /// runs, and loading a machine out from under it puts a load and a close
        /// through the same frame.
        /// </summary>
        private IEnumerator OpenHistory(string machineName, List<VersionEntry> versions)
        {
            _busy = true;
            CloseBrowser();
            yield return null;

            _history.OpenNewest(machineName, versions);
            _busy = false;
        }

        private static void CloseBrowser()
        {
            try
            {
                FileBrowserView view = FindObjectOfType<FileBrowserView>();
                if (view != null && view.IsOpen)
                {
                    view.Close();
                }
            }
            catch (Exception e)
            {
                Log.Warn("could not close the load screen: " + e.Message);
            }
        }
    }
}
