using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GitView
{
    /// <summary>
    /// Puts this mod's buttons on the load screen and opens the history window when
    /// one is pressed.
    ///
    /// The load screen is not uGUI: its slots are mesh objects in world space with
    /// <c>SimpleUIButton</c> colliders on them, so there is no prefab to instantiate
    /// and no layout group to add a child to. Every button here is therefore a copy
    /// of one of the screen's own, which brings the right mesh, material, collider
    /// and press behaviour with it, wearing a face drawn by <see cref="IconArt"/>.
    /// </summary>
    public class BrowserWatch : MonoBehaviour
    {
        /// <summary>Name given to the buttons this mod adds, so they are recognisable.</summary>
        public const string ButtonName = "GitViewCompareButton";

        /// <summary>The button that picks a machine out for a diff against another one.</summary>
        public const string SelectName = "GitViewSelectButton";

        /// <summary>The button that diffs everything picked out so far.</summary>
        public const string DiffAllName = "GitViewDiffAllButton";

        /// <summary>The big numbered mark a chosen machine's thumbnail wears.</summary>
        public const string BadgeName = "GitViewSelectedBadge";

        /// <summary>The line of text our own button shows while the pointer is on it.</summary>
        public const string TipName = "GitViewTip";

        private const float PollSeconds = 0.25f;
        private const int IconSize = 128;

        /// <summary>
        /// The mark over a chosen machine's thumbnail, drawn larger than a button's
        /// icon: it carries a number as well as a glyph and is read across the page.
        /// </summary>
        private const int BadgeSize = 192;

        /// <summary>
        /// How big the branch is drawn against the button it replaces. Matching that
        /// button exactly looks heavier than the game's own icons, which are drawn
        /// with white space around them inside their sprites.
        /// </summary>
        private const float IconScale = 0.75f;

        /// <summary>
        /// Shaders to draw a button's face with, best first. Only shaders in the
        /// player's build can be found by name, so several are tried;
        /// `Particles/Alpha Blended` is known to be in Besiege's.
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

        // Sprites and materials are cached per face, since there are now several:
        // the branch on the history button, the compare mark on the select button,
        // and one numbered mark per place in the choosing order.
        private static readonly Dictionary<string, Sprite> _sprites =
            new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Material> _materials =
            new Dictionary<string, Material>();
        private static readonly Dictionary<string, Texture2D> _faces =
            new Dictionary<string, Texture2D>();

        private readonly List<GameObject> _unpainted = new List<GameObject>();
        private readonly List<Texture2D> _wanted = new List<Texture2D>();
        private readonly List<int> _attempts = new List<int>();

        private HistoryView _history;
        private float _nextPoll;

        /// <summary>The one compare-them-all button, or null while the browser is shut.</summary>
        private GameObject _diffAll;

        /// <summary>That button's own <c>SimpleUIButton</c>. See <see cref="KeepPressable"/>.</summary>
        private SimpleUIButton _press;

        /// <summary>
        /// What the compare button can be hit on, taken before its tooltip was hung
        /// off it: the tooltip's own colliders are meant to stay switched off.
        /// </summary>
        private Collider[] _hits;

        /// <summary>
        /// The dark square behind the compare button: a copy of the one the load
        /// buttons sit on, and not a child of ours. See <see cref="CopyPlate"/>.
        /// </summary>
        private GameObject _plate;

        /// <summary>
        /// Besiege's tooltip on that button, where one could be copied, and null
        /// where a quad of ours is standing in for it.
        /// </summary>
        private Tooltip _tip;

        private bool _busy;

        public void Bind(HistoryView history)
        {
            _history = history;
        }

        private void Update()
        {
            // Every frame, unlike the rest of this: a tooltip that waited for the
            // sweep would lag the pointer.
            ShowTipOnHover();
            KeepPressable();

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
                if (slot == null)
                {
                    continue;
                }
                if (slot.transform.FindChild(ButtonName) == null && ShouldOfferHistory(slot))
                {
                    AddButton(slot);
                }
                if (CanChoose(slot) && slot.transform.FindChild(SelectName) == null)
                {
                    AddSelectButton(slot);
                }
                Reconcile(slot);
            }
            KeepDiffAllButton();
        }

        /// <summary>
        /// Whether this slot is a machine that can be chosen for a comparison: any
        /// machine file, including the versions inside an autosave folder. Not
        /// folders -- pressing the branch on one of those already means "every version
        /// of this machine".
        /// </summary>
        private static bool CanChoose(FileBrowserSlot slot)
        {
            IVirtualObject item = slot.VirtualObject;
            return item != null && !item.IsFolder;
        }

        // ------------------------------------------------------------------ deciding

        /// <summary>
        /// Whether this slot is a machine's history: one of the folders inside
        /// AutoSave. Only those -- a machine's own slot already carries Besiege's
        /// button for going to its autosaves and this mod's button for adding it to a
        /// comparison, and a third branch in the same corner was one too many.
        /// </summary>
        private static bool ShouldOfferHistory(FileBrowserSlot slot)
        {
            IVirtualObject item = slot.VirtualObject;
            if (item == null || !item.IsFolder)
            {
                return false;
            }
            VirtualFolder parent = item.Parent;
            return parent != null && parent.Name == VersionScan.AutoSaveFolder;
        }

        // ------------------------------------------------------------------ building

        private void AddButton(FileBrowserSlot slot)
        {
            SimpleUIButton template = PickTemplate(slot);
            if (template == null)
            {
                return;
            }
            SimpleUIButton button = CloneButton(slot, template, ButtonName,
                                                FreeSpot(slot, template), FaceBranch);
            if (button == null)
            {
                return;
            }
            FileBrowserSlot owner = slot;
            button.Click += new Click(delegate { OnPressed(owner); });
        }

        /// <summary>
        /// The button that picks this machine out to be compared with another one.
        /// Above the row along the bottom rather than in it: that row is what a slot
        /// does to itself, and this is about this machine and another.
        /// </summary>
        private void AddSelectButton(FileBrowserSlot slot)
        {
            SimpleUIButton template = PickTemplate(slot);
            if (template == null)
            {
                return;
            }
            SimpleUIButton button = CloneButton(slot, template, SelectName,
                                                AboveCorner(slot, template), FacePlus);
            if (button == null)
            {
                return;
            }
            FileBrowserSlot owner = slot;
            button.Click += new Click(delegate { OnChoosePressed(owner); });
        }

        /// <summary>
        /// Copies one of the slot's own buttons -- which brings the right mesh,
        /// material, collider and press behaviour -- and puts one of our faces on it.
        /// </summary>
        private SimpleUIButton CloneButton(FileBrowserSlot slot, SimpleUIButton template,
                                           string name, Vector3 spot, string face)
        {
            GameObject clone = Instantiate(template.gameObject) as GameObject;
            if (clone == null)
            {
                return null;
            }
            clone.name = name;
            clone.transform.SetParent(slot.transform, false);
            clone.transform.localRotation = template.transform.localRotation;
            clone.transform.localScale = template.transform.localScale;
            clone.transform.localPosition = spot;
            clone.SetActive(true);

            Remember(clone, IconTexture(face));

            SimpleUIButton button = clone.GetComponent<SimpleUIButton>();
            if (button == null)
            {
                Destroy(clone);
                return null;
            }
            // A clone carries no delegates, those not being serialised. Clearing them
            // anyway means a future Besiege that wires them up in Awake cannot leave
            // this button also deleting the machine.
            button.ResetDelegates();
            return button;
        }

        /// <summary>
        /// Straight above the leftmost icon in the slot's bottom row. Taken off the
        /// row rather than off one button found by name: the leftmost of whatever the
        /// slot is showing stays the corner however many icons a machine has.
        /// </summary>
        private Vector3 AboveCorner(FileBrowserSlot slot, SimpleUIButton template)
        {
            float step = Mathf.Max(Width(slot, template), 0.05f) * 1.25f;
            List<Vector3> places = ButtonPlaces(slot);

            if (places.Count > 0)
            {
                float lowest = float.MaxValue;
                for (int i = 0; i < places.Count; i++)
                {
                    lowest = Mathf.Min(lowest, places[i].y);
                }

                Vector3 corner = Vector3.zero;
                bool found = false;
                for (int i = 0; i < places.Count; i++)
                {
                    // The bottom row, which is everything within a button's height
                    // of the lowest one.
                    if (places[i].y - lowest > step || (found && places[i].x >= corner.x))
                    {
                        continue;
                    }
                    corner = places[i];
                    found = true;
                }
                if (found)
                {
                    return new Vector3(corner.x, corner.y + step, corner.z);
                }
            }

            Vector3 spare = FreeSpot(slot, template);
            return new Vector3(spare.x, spare.y + step * 2f, spare.z);
        }

        /// <summary>
        /// Which of the slot's own buttons to copy: an active one, which is definitely
        /// laid out and visible, and one with something to draw.
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
        /// Where to put the button: the gap in the row along the bottom of a slot.
        ///
        /// Worked out from where the slot's own buttons actually are rather than from
        /// numbers off a screenshot, since a hardcoded offset would not survive
        /// Besiege moving anything. A real slot has nine SimpleUIButtons with exactly
        /// one on show -- delete, in a bottom corner -- so the opposite corner is the
        /// place, and the rules below are for slots with more.
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
                // The usual case: one button on show, in a bottom corner. Its mirror
                // through the middle of the slot is certainly free, and is where a
                // second button would have gone if Besiege had one.
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
            return spot;
        }

        private static List<Vector3> ButtonPlaces(FileBrowserSlot slot)
        {
            List<Vector3> places = new List<Vector3>();
            SimpleUIButton[] buttons = slot.GetComponentsInChildren<SimpleUIButton>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                SimpleUIButton button = buttons[i];
                // Only the ones actually on screen: a slot carries hidden buttons for
                // uploading, cloud sync and confirming a delete, and avoiding places
                // nobody can see would use up the whole slot.
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
        /// Tries to put our glyph on every copy still wearing the icon it was cloned
        /// from, and drops the ones that are done or gone. Retried across frames
        /// rather than done at clone time: a slot button's face need not exist in the
        /// frame it is cloned, since whatever builds it runs in an Awake of its own.
        /// </summary>
        private void RepaintPending()
        {
            for (int i = _unpainted.Count - 1; i >= 0; i--)
            {
                if (_unpainted[i] == null || Paint(_unpainted[i], _wanted[i]))
                {
                    Forget(i);
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
                    Overlay(_unpainted[i], _wanted[i]);
                    Forget(i);
                }
            }
        }

        private void Forget(int i)
        {
            _unpainted.RemoveAt(i);
            _wanted.RemoveAt(i);
            _attempts.RemoveAt(i);
        }

        private void Remember(GameObject button, Texture2D face)
        {
            _unpainted.Add(button);
            _wanted.Add(face);
            _attempts.Add(0);
            // The first go is free: if the face is already there, nothing flickers.
            RepaintPending();
        }

        /// <summary>
        /// Repaints whatever the copied button actually draws with -- a sweep over
        /// every renderer and every kind of renderer, since what a slot button is made
        /// of cannot be checked from out here. Setting `material.mainTexture` and
        /// parenting a quad of our own both failed silently in game; assigning a whole
        /// material keeps Besiege's geometry and changes only what we chose.
        /// </summary>
        private bool Paint(GameObject button, Texture2D face)
        {
            Material material = IconMaterial(face);
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

            Renderer painted = null;
            for (int i = 0; i < renderers.Length && painted == null; i++)
            {
                if (PaintOne(renderers[i], material, face))
                {
                    painted = renderers[i];
                }
            }
            if (painted == null)
            {
                return false;
            }

            // Anything else it draws goes off: a button can be a glyph on a backing
            // plate, and leaving the glyph on draws the icon we are replacing over
            // the top of ours.
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != painted)
                {
                    renderers[i].enabled = false;
                }
            }
            return true;
        }

        private static bool PaintOne(Renderer renderer, Material material, Texture2D face)
        {
            if (renderer == null)
            {
                return false;
            }

            // A sprite ignores its material's texture and draws its own -- which is
            // what a slot button turns out to be.
            SpriteRenderer sprite = renderer as SpriteRenderer;
            if (sprite != null)
            {
                Sprite replacement = IconSprite(sprite.sprite, face);
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
        /// The last resort: our own quad, sized and placed from the button's collider,
        /// reached when nothing the button draws could be repainted. The collider is
        /// the one part of a SimpleUIButton that has to exist, so its bounds are a
        /// size and a place that can always be had. Wound both ways, since which side
        /// faces the camera cannot be known from here.
        /// </summary>
        private void Overlay(GameObject button, Texture2D face)
        {
            Material material = IconMaterial(face);
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
            // Wound both ways, so back-face culling cannot be what makes the icon
            // invisible.
            _quad.triangles = new int[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };
            _quad.RecalculateNormals();
            _quad.hideFlags = HideFlags.HideAndDontSave;
            return _quad;
        }

        /// <summary>
        /// Our glyph as a sprite the same size on screen as the one it replaces. A
        /// sprite is drawn <c>rect.height / pixelsPerUnit</c> units tall and
        /// <c>Sprite.Create</c> assumes 100 unless told otherwise, so matching the
        /// replaced sprite's world height is what makes the two the same size whatever
        /// either texture happens to be.
        /// </summary>
        private static Sprite IconSprite(Sprite replacing, Texture2D texture)
        {
            float density = 100f;
            if (replacing != null && replacing.rect.height > 0f && replacing.pixelsPerUnit > 0f)
            {
                float worldHeight = replacing.rect.height / replacing.pixelsPerUnit * IconScale;
                density = texture.height / Mathf.Max(worldHeight, 0.0001f);
            }

            // Cached per size rather than one shared sprite: different slots can
            // carry differently authored buttons.
            string key = texture.GetInstanceID() + "@" + Mathf.RoundToInt(density * 10f);
            Sprite made;
            if (_sprites.TryGetValue(key, out made) && made != null)
            {
                return made;
            }

            made = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                                 new Vector2(0.5f, 0.5f), density);
            made.hideFlags = HideFlags.HideAndDontSave;
            _sprites[key] = made;
            return made;
        }

        /// <summary>
        /// A copy of the button's quad with its texture coordinates spread over the
        /// whole of our icon. Besiege's UI meshes may carry coordinates into a shared
        /// atlas rather than 0..1, and a mesh cut out for a trash can would show that
        /// same corner of our texture. Remapping by position works whichever plane the
        /// quad lies in.
        ///
        /// Null if the mesh cannot be read, and the caller then keeps the original.
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
        /// One of the mod's faces, drawn once and kept: the branch, the branch with a
        /// plus, or the branch carrying a number.
        /// </summary>
        private static Texture2D IconTexture(string face)
        {
            Texture2D drawn;
            if (_faces.TryGetValue(face, out drawn) && drawn != null)
            {
                return drawn;
            }

            // The window's font, so the mark in the corner is the same lettering as
            // the number in the window it opens. Null until UI Factory has loaded,
            // which is why a face drawn without it is not kept.
            Font font = UIF.Font;
            bool lettered = true;
            if (face == FaceBranch)
            {
                drawn = IconArt.Branch(IconSize);
                lettered = false;
            }
            else if (face == FacePlated)
            {
                drawn = IconArt.Plated(IconSize);
                lettered = false;
            }
            else if (face == FacePlus)
            {
                drawn = IconArt.Plus(IconSize, font);
            }
            else
            {
                // "n3" -- the branch with a 3 in the corner.
                int number;
                if (!int.TryParse(face.Substring(1), out number))
                {
                    number = 0;
                }
                drawn = IconArt.Numbered(BadgeSize, number, font);
            }
            drawn.hideFlags = HideFlags.HideAndDontSave;
            if (!lettered || font != null)
            {
                _faces[face] = drawn;
            }
            return drawn;
        }

        private const string FaceBranch = "branch";
        private const string FacePlus = "plus";

        /// <summary>
        /// The branch on a dark square, for a button whose plate could not be copied.
        /// See <see cref="BuildDiffAllButton"/>.
        /// </summary>
        private const string FacePlated = "plated";

        /// <summary>The name of the face carrying a given number.</summary>
        private static string NumberedFace(int number)
        {
            return "n" + number;
        }

        private static Material IconMaterial(Texture2D face)
        {
            if (face == null)
            {
                return null;
            }
            Material made;
            if (_materials.TryGetValue(face.name + face.GetInstanceID(), out made) &&
                made != null)
            {
                return made;
            }

            for (int i = 0; i < IconShaders.Length; i++)
            {
                Shader shader = Shader.Find(IconShaders[i]);
                if (shader == null)
                {
                    continue;
                }
                made = new Material(shader);
                made.mainTexture = face;
                made.color = Color.white;
                // Particles/Alpha Blended doubles the texture against this; white
                // keeps the glyph at full strength and changes nothing elsewhere.
                if (made.HasProperty("_TintColor"))
                {
                    made.SetColor("_TintColor", Color.white);
                }
                made.hideFlags = HideFlags.HideAndDontSave;
                _materials[face.name + face.GetInstanceID()] = made;
                return made;
            }

            Log.Warn("no transparent shader in this build to draw the mod's buttons " +
                     "with; leaving them wearing the icons they were copied from.");
            return null;
        }


        // ------------------------------------------------------------------ choosing

        /// <summary>How much of the thumbnail the numbered mark covers.</summary>
        private const float BadgeShare = 0.45f;

        /// <summary>How small a chosen machine's picture is redrawn, and how far down.</summary>
        private const int DimSize = 24;
        private const float DimFactor = 0.45f;

        /// <summary>
        /// Brings one slot into line with what has been chosen. Every sweep, for every
        /// slot: the browser reuses its slots for whatever file is on the page now, so
        /// a mark is not a mark on a machine until something keeps checking that the
        /// slot still shows the machine it was put there for.
        /// </summary>
        private void Reconcile(FileBrowserSlot slot)
        {
            IVirtualObject item = slot.VirtualObject;
            string path = Selection.PathOf(item);
            int ordinal = Selection.Ordinal(path);

            SlotMark mark = slot.GetComponent<SlotMark>();
            if (mark == null)
            {
                if (ordinal == 0)
                {
                    return;
                }
                mark = slot.gameObject.AddComponent<SlotMark>();
            }

            if (mark.Path != path)
            {
                // The slot has been given to another machine.
                Drop(mark);
                mark.Path = path;
            }

            if (ordinal == 0)
            {
                Drop(mark);
            }
            else
            {
                Mark(slot, mark, ordinal);
            }
        }

        /// <summary>
        /// Puts the numbered mark on a slot and dims its picture, or renumbers it if
        /// unchoosing something else has moved it along.
        /// </summary>
        private void Mark(FileBrowserSlot slot, SlotMark mark, int ordinal)
        {
            if (mark.Ordinal != ordinal && mark.Badge != null)
            {
                Destroy(mark.Badge);
                mark.Badge = null;
            }
            mark.Ordinal = ordinal;

            if (mark.Badge == null)
            {
                mark.Badge = Badge(slot, ordinal);
            }
            // Asked every sweep rather than only when the mark is new: a thumbnail
            // arrives when its file has finished loading, which can be several sweeps
            // after the slot appeared, and whatever the browser has painted over ours
            // is dimmed again from here.
            Material material = PictureMaterial(slot);
            if (material != null && material.mainTexture != mark.Dimmed)
            {
                if (mark.Dimmed != null)
                {
                    Destroy(mark.Dimmed);
                    mark.Dimmed = null;
                    mark.Original = null;
                }
                Dim(slot, mark);
            }
        }

        /// <summary>
        /// Takes the mark off a slot and puts its picture back -- only if the slot is
        /// still showing the dimmed copy we made. A slot handed to another machine has
        /// been redrawn already, and putting the picture back then would put the
        /// previous machine's picture on the one now in the slot.
        /// </summary>
        private void Drop(SlotMark mark)
        {
            if (mark.Badge != null)
            {
                Destroy(mark.Badge);
                mark.Badge = null;
            }
            if (mark.Dimmed != null)
            {
                FileBrowserSlot slot = mark.GetComponent<FileBrowserSlot>();
                Material material = slot == null ? null : PictureMaterial(slot);
                if (material != null && material.mainTexture == mark.Dimmed)
                {
                    // What we replaced, or failing that the browser's own copy: only
                    // the slot's material was ever touched.
                    Texture original = mark.Original;
                    if (original == null && slot.VirtualObject != null)
                    {
                        original = slot.VirtualObject.Thumbnail;
                    }
                    if (original != null)
                    {
                        material.mainTexture = original;
                    }
                }
                Destroy(mark.Dimmed);
                mark.Dimmed = null;
                mark.Original = null;
            }
            mark.Ordinal = 0;
        }

        /// <summary>
        /// The big numbered mark, over the middle of the thumbnail and as far in front
        /// of it as the slot's own buttons are: which way "in front" is depends on how
        /// the browser faces the camera, and the buttons are the one thing here known
        /// to be visible.
        /// </summary>
        private GameObject Badge(FileBrowserSlot slot, int ordinal)
        {
            Material material = IconMaterial(IconTexture(NumberedFace(ordinal)));
            Renderer picture = Picture(slot);
            if (material == null || picture == null)
            {
                return null;
            }

            float scale = Mathf.Abs(slot.transform.lossyScale.x);
            if (scale < 0.0001f)
            {
                scale = 1f;
            }
            Vector3 size = picture.bounds.size / scale;
            float side = Mathf.Max(size.x, size.y) * BadgeShare;

            Vector3 at = slot.transform.InverseTransformPoint(picture.bounds.center);
            at.z = Front(slot);

            GameObject badge = new GameObject(BadgeName, typeof(MeshFilter),
                                              typeof(MeshRenderer));
            badge.transform.SetParent(slot.transform, false);
            badge.transform.localPosition = at;
            badge.transform.localRotation = Quaternion.identity;
            badge.transform.localScale = new Vector3(side, side, 1f);
            badge.layer = slot.gameObject.layer;
            badge.GetComponent<MeshFilter>().sharedMesh = DoubleSidedQuad();
            badge.GetComponent<MeshRenderer>().sharedMaterial = material;
            return badge;
        }

        /// <summary>The thumbnail's renderer, which is what says where the picture is and how big.</summary>
        private static Renderer Picture(FileBrowserSlot slot)
        {
            FileBrowserSlotThumbnail thumbnail = slot.Thumbnail;
            if (thumbnail == null)
            {
                return null;
            }
            Renderer[] renderers = thumbnail.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].enabled &&
                    renderers[i].bounds.size.x > 0.0001f)
                {
                    return renderers[i];
                }
            }
            return null;
        }

        /// <summary>
        /// How far in front of the slot to draw, off the button furthest forward: a
        /// transparent quad level with the picture behind it z-fights.
        /// </summary>
        private static float Front(FileBrowserSlot slot)
        {
            float front = 0f;
            SimpleUIButton[] buttons = slot.GetComponentsInChildren<SimpleUIButton>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null || !buttons[i].gameObject.activeInHierarchy)
                {
                    continue;
                }
                float z = buttons[i].transform.localPosition.z;
                if (Mathf.Abs(z) > Mathf.Abs(front))
                {
                    front = z;
                }
            }
            return front * 1.5f;
        }

        /// <summary>
        /// Replaces a chosen machine's picture with a small, dark copy of itself. The
        /// original is kept on the mark rather than left to the browser's own cache,
        /// which holds a thumbnail per machine -- put back only what the slot is
        /// holding and the machine comes up dim next time the screen opens.
        /// </summary>
        private void Dim(FileBrowserSlot slot, SlotMark mark)
        {
            Renderer picture = Picture(slot);
            if (picture == null || picture.sharedMaterial == null)
            {
                return;
            }
            Texture original = picture.sharedMaterial.mainTexture;
            if (original == null)
            {
                // Not loaded yet. The next sweep will find it.
                return;
            }

            Texture2D dimmed = IconArt.Dim(original, DimSize, DimFactor);
            if (dimmed == null)
            {
                return;
            }
            mark.Original = original;
            mark.Dimmed = dimmed;
            picture.sharedMaterial.mainTexture = dimmed;
        }

        /// <summary>
        /// What the picture is drawn with. The slot's <c>ApplyTexture</c> is not
        /// public, but the material behind it is per-slot -- <c>ThumbnailComponent</c>
        /// takes it from <c>Renderer.material</c>, which instances one -- so setting
        /// the texture from outside is the same thing.
        /// </summary>
        private static Material PictureMaterial(FileBrowserSlot slot)
        {
            Renderer picture = Picture(slot);
            return picture == null ? null : picture.sharedMaterial;
        }

        /// <summary>
        /// Takes every mark off every slot and puts every dimmed picture back, while
        /// there are still slots to put them back on.
        /// </summary>
        private void RestoreAll()
        {
            SlotMark[] marks = FindObjectsOfType<SlotMark>();
            if (marks == null)
            {
                return;
            }
            for (int i = 0; i < marks.Length; i++)
            {
                if (marks[i] != null)
                {
                    Drop(marks[i]);
                }
            }
        }

        // ------------------------------------------------------- comparing them all

        /// <summary>
        /// Keeps the one compare-them-all button, up beside the load buttons: it is
        /// not about a machine, so it belongs in the row that acts on the whole
        /// screen, and it is only on show once there are two machines to compare.
        /// </summary>
        private void KeepDiffAllButton()
        {
            if (_diffAll == null)
            {
                _diffAll = BuildDiffAllButton();
            }
            if (_diffAll == null)
            {
                return;
            }
            bool enough = Selection.Count > 1;
            if (_diffAll.activeSelf != enough)
            {
                if (!enough)
                {
                    // A button that goes away under the pointer is never told the
                    // pointer left, so its tooltip would still be shown next time --
                    // and Besiege's own would think it was already open and not open
                    // again.
                    if (_tip != null)
                    {
                        _tip.OnMouseExit();
                    }
                    Transform tip = _diffAll.transform.FindChild(TipName);
                    if (tip != null && _tip == null)
                    {
                        Reveal(tip.gameObject, false);
                    }
                }
                _diffAll.SetActive(enough);
            }
            // The plate belongs to the row rather than to the button, so it has to be
            // shown and hidden with it by hand.
            if (_plate != null && _plate.activeSelf != enough)
            {
                _plate.SetActive(enough);
            }
            if (enough)
            {
                Say(_diffAll, "COMPARE " + Selection.Count + " MACHINES");
            }
        }

        /// <summary>
        /// Copies one of the load buttons at the top of the screen -- the whole button
        /// as it stands, with what it draws and how it behaves left on it, which is
        /// the only way to be the same size, material and shape as its neighbours.
        /// Stripping it to the one component that makes it clickable cost it the plate
        /// under its icon and its animated tooltip, both of which then had to be
        /// imitated.
        ///
        /// Two things come off and only two: <c>LoadSaveButton</c>, which would
        /// repaint our icon with "load" or "save", and anything from
        /// <c>Localisation</c>, which would put the game's words back over ours.
        /// </summary>
        private GameObject BuildDiffAllButton()
        {
            // Whatever the last browser left behind went with it when the screen
            // closed.
            _press = null;
            _tip = null;
            _hits = null;
            _plate = null;
            _pressedOn = false;

            LoadSaveButton[] loads = FindObjectsOfType<LoadSaveButton>();
            if (loads == null || loads.Length == 0)
            {
                return null;
            }

            // The rightmost one actually on screen: a hidden load button is a
            // different size in a different place, and copying one would put ours
            // somewhere nobody can see.
            LoadSaveButton rightmost = null;
            for (int i = 0; i < loads.Length; i++)
            {
                if (loads[i] == null || !loads[i].gameObject.activeInHierarchy)
                {
                    continue;
                }
                if (rightmost == null ||
                    loads[i].transform.position.x > rightmost.transform.position.x)
                {
                    rightmost = loads[i];
                }
            }
            if (rightmost == null || rightmost.transform.parent == null)
            {
                return null;
            }

            // Worked out before the copy exists, so it cannot be mistaken for one of
            // the buttons the row is measured from.
            Vector3 spot = RightOfRow(rightmost.transform);

            GameObject clone = Instantiate(rightmost.gameObject) as GameObject;
            if (clone == null)
            {
                return null;
            }
            clone.name = DiffAllName;
            clone.transform.SetParent(rightmost.transform.parent, false);
            clone.transform.localRotation = rightmost.transform.localRotation;
            clone.transform.localScale = rightmost.transform.localScale;
            clone.transform.localPosition = spot;

            // The button's own colliders, taken before the tooltip is hung off it:
            // that brings colliders of its own, meant to stay off. See KeepPressable.
            _hits = clone.GetComponentsInChildren<Collider>(true);

            // Only the picture is repainted and nothing is turned off. The glyph goes
            // on bare and the plate is copied separately -- see CopyPlate -- because
            // the button swells under the pointer, and a plate painted into its icon
            // would swell with it where the game's plates hold still.
            Renderer face = IconRenderer(clone);
            Undress(clone);
            // Where no plate could be copied the glyph brings one of its own, so that
            // the button is not the one bare mark in a row of plated ones -- a worse
            // button than this, but a button.
            bool plated = CopyPlate(rightmost.gameObject, clone);
            Texture2D glyph = IconTexture(plated ? FaceBranch : FacePlated);
            if (face == null || !PaintOne(face, IconMaterial(glyph), glyph))
            {
                Remember(clone, glyph);
            }

            SimpleUIButton button = clone.GetComponent<SimpleUIButton>();
            if (button == null)
            {
                button = clone.GetComponentInChildren<SimpleUIButton>(true);
            }
            if (button == null)
            {
                Destroy(clone);
                return null;
            }
            // Cleared rather than replaced, in case a future Besiege wires them up in
            // Awake. Nothing of ours is added: the press is watched for in
            // KeepPressable, which works whether or not the button can notice it.
            button.ResetDelegates();
            _press = button;

            // Besiege's own tooltip where it can be had -- the plate that slides out
            // from under the button and fades in -- and a quad of ours where it
            // cannot.
            if (!CopyTip(rightmost.gameObject, clone))
            {
                AddTip(clone, button);
            }
            // The words belong to the button and this is a new one, so what it "last
            // said" has to be forgotten or the second visit shows a blank plate.
            _tipWords = string.Empty;
            clone.SetActive(false);
            return clone;
        }

        /// <summary>
        /// Copies the tooltip off the button this one was copied from: the plate, the
        /// arrow, the lettering and the slide it comes out with.
        ///
        /// The words live on a <c>tooltipParent</c> transform that is not inside the
        /// button, so a copy of the button points at the *original's* words in the
        /// original's place. Copying that object too and pointing our tooltip at the
        /// copy gives us one to write on. <c>Reset</c> is what makes it take: it
        /// re-finds every renderer and text under its new parent, works out which way
        /// the arrow points, and leaves the lot off until the pointer arrives.
        /// </summary>
        private bool CopyTip(GameObject original, GameObject clone)
        {
            Tooltip theirs = original.GetComponent<Tooltip>();
            Tooltip mine = clone.GetComponent<Tooltip>();
            if (theirs == null || mine == null || theirs.tooltipParent == null)
            {
                return false;
            }

            GameObject words =
                Instantiate(theirs.tooltipParent.gameObject) as GameObject;
            if (words == null)
            {
                return false;
            }
            words.name = TipName;

            // Placed in the world rather than in the button's own space: the same
            // distance below our button as theirs is below its own, at the same size
            // and facing the same way, whatever either has been scaled to.
            Transform at = words.transform;
            at.SetParent(clone.transform, false);
            at.localScale = Relative.Scale(theirs.tooltipParent.lossyScale,
                                           clone.transform.lossyScale);
            at.rotation = theirs.tooltipParent.rotation;
            at.position = clone.transform.position +
                          (theirs.tooltipParent.position - original.transform.position);

            Undress(words);
            mine.tooltipParent = at;
            try
            {
                mine.Reset();
            }
            catch (Exception e)
            {
                Log.Warn("could not set up the copied tooltip: " + e.Message);
                Destroy(words);
                return false;
            }
            _tip = mine;
            return true;
        }

        /// <summary>
        /// Copies the dark plate the button this one came from sits on, and puts it
        /// behind ours.
        ///
        /// The plate is not part of the button -- the square behind it belongs to the
        /// row, the same trick the tooltip plays with its words -- so a copied load
        /// button is a bare glyph about three fifths the height of its neighbours.
        /// Painting a plate into our own icon and growing the button to match came out
        /// small twice: the hover swell sets the scale from a size it remembers, so it
        /// puts back the size we grew from and grows the plate with it.
        ///
        /// A copy of the plate, parented where the plate is, has neither problem.
        /// </summary>
        private bool CopyPlate(GameObject original, GameObject clone)
        {
            Renderer plate = PlateRenderer(original);
            if (plate == null || plate.transform.parent == null)
            {
                return false;
            }

            GameObject copy = Instantiate(plate.gameObject) as GameObject;
            if (copy == null)
            {
                return false;
            }
            copy.name = PlateName;
            Undress(copy);
            // A plate is something to look at: with a collider on it, ours would be a
            // second thing in the row for the mouse to find.
            Collider[] boxes = copy.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < boxes.Length; i++)
            {
                if (boxes[i] != null)
                {
                    boxes[i].enabled = false;
                    Destroy(boxes[i]);
                }
            }

            Transform at = copy.transform;
            at.SetParent(plate.transform.parent, false);
            at.localRotation = plate.transform.localRotation;
            at.localScale = plate.transform.localScale;
            // The same step to the right that our button took from theirs.
            at.position = plate.transform.position +
                          (clone.transform.position - original.transform.position);

            _plate = copy;
            copy.SetActive(false);
            return true;
        }

        /// <summary>
        /// The plate behind a button: the thing drawn around it that its icon sits
        /// inside. Searched from the button's parent, since the plate need not be part
        /// of it, so it is found whether it is a sibling, a parent or a child.
        /// Anything much bigger than the icon is the panel the whole row is on.
        /// </summary>
        private static Renderer PlateRenderer(GameObject button)
        {
            Renderer icon = IconRenderer(button);
            Transform around = button.transform.parent;
            if (icon == null || around == null)
            {
                return null;
            }

            Bounds want = icon.bounds;
            Renderer best = null;
            Renderer[] near = around.GetComponentsInChildren<Renderer>(false);
            for (int i = 0; i < near.Length; i++)
            {
                Renderer renderer = near[i];
                if (renderer == null || renderer == icon || !renderer.enabled ||
                    renderer.GetComponent<TextMesh>() != null)
                {
                    continue;
                }
                // The one behind *this* button: the plate under the next button along
                // does not contain this icon.
                Bounds box = renderer.bounds;
                if (!box.Contains(want.center) || box.size.y < want.size.y ||
                    box.size.y > want.size.y * 3f)
                {
                    continue;
                }
                if (best == null || box.size.y < best.bounds.size.y)
                {
                    best = renderer;
                }
            }
            return best;
        }

        /// <summary>The name given to the copied plate, so it can be found again.</summary>
        public const string PlateName = "GitViewComparePlate";

        /// <summary>
        /// The renderer a button draws its icon on: the smallest thing it draws.
        /// <c>LoadSaveButton</c> names it in a private field like everything else in
        /// the browser, so size is what there is to go on -- and a button is an icon
        /// on a plate, so the icon is never the larger of the two.
        /// </summary>
        private static Renderer IconRenderer(GameObject clone)
        {
            Renderer[] renderers = clone.GetComponentsInChildren<Renderer>(true);
            Renderer best = null;
            float smallest = 0f;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                // Not the words on a button, on the day a button has words.
                if (renderer == null || renderer.GetComponent<TextMesh>() != null)
                {
                    continue;
                }
                Vector3 size = renderer.bounds.size;
                float area = Mathf.Max(size.x, 0.0001f) * Mathf.Max(size.y, 0.0001f);
                if (best == null || area < smallest)
                {
                    best = renderer;
                    smallest = area;
                }
            }
            return best;
        }

        /// <summary>
        /// The next place along a row of buttons: one step right of the rightmost one
        /// still part of the row. Walked rather than measured off the two load
        /// buttons, which are not all the row is made of -- "load as selection" is not
        /// a <c>LoadSaveButton</c> -- so the gap between those two is not the pitch the
        /// row is drawn on. Hopping button to button finds both.
        /// </summary>
        private static Vector3 RightOfRow(Transform anchor)
        {
            Transform parent = anchor.parent;
            float width = Mathf.Max(LocalWidth(anchor), 0.05f);
            Vector3 at = anchor.localPosition;
            float pitch = width * 1.25f;
            if (parent == null)
            {
                return new Vector3(at.x + pitch, at.y, at.z);
            }

            SimpleUIButton[] buttons = FindObjectsOfType<SimpleUIButton>();
            bool moved = true;
            while (moved)
            {
                moved = false;
                float nearest = 0f;
                Vector3 next = at;
                for (int i = 0; i < buttons.Length; i++)
                {
                    if (buttons[i] == null || !buttons[i].gameObject.activeInHierarchy)
                    {
                        continue;
                    }
                    // In the anchor's own space, so a button in a container of its
                    // own is still measured against the same ruler.
                    Vector3 spot = parent.InverseTransformPoint(
                        buttons[i].transform.position);
                    float gap = spot.x - at.x;
                    if (gap <= width * 0.25f || gap > width * 2.5f ||
                        Mathf.Abs(spot.y - at.y) > width * 0.5f)
                    {
                        continue;
                    }
                    if (!moved || gap < nearest)
                    {
                        nearest = gap;
                        next = spot;
                        moved = true;
                    }
                }
                if (moved)
                {
                    at = next;
                    pitch = nearest;
                }
            }
            return new Vector3(at.x + pitch, at.y, at.z);
        }

        /// <summary>
        /// How wide a button is, in the units of whatever it is parented to. Off the
        /// collider first, that being the one part which has to exist and is the
        /// button's own size: a renderer's bounds would take in its tooltip.
        /// </summary>
        private static float LocalWidth(Transform button)
        {
            Transform parent = button.parent;
            float scale = Mathf.Abs(parent == null ? button.lossyScale.x
                                                   : parent.lossyScale.x);
            if (scale < 0.0001f)
            {
                scale = 1f;
            }
            float wide = WorldWidth(button);
            return wide > 0f ? wide / scale : 1f;
        }

        /// <summary>How wide a button is in the world's own units.</summary>
        private static float WorldWidth(Transform button)
        {
            Collider box = button.GetComponent<Collider>();
            if (box != null && box.bounds.size.x > 0.0001f)
            {
                return box.bounds.size.x;
            }
            Renderer renderer = button.GetComponent<Renderer>();
            if (renderer != null && renderer.bounds.size.x > 0.0001f)
            {
                return renderer.bounds.size.x;
            }
            return 0f;
        }

        /// <summary>
        /// Takes off a copy the two things that would undo our own: <c>LoadSaveButton</c>,
        /// which holds the load and save icons and would put one on the renderer we
        /// are about to paint, and the <c>Localisation</c> behaviours, which exist to
        /// put the game's own words back over anything else.
        ///
        /// Everything else stays -- the plate, the way it lights up, the tooltip that
        /// slides out from under it are all things the button already does.
        /// </summary>
        private static void Undress(GameObject clone)
        {
            MonoBehaviour[] behaviours = clone.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || !Unwanted(behaviour))
                {
                    continue;
                }
                behaviour.enabled = false;
                Destroy(behaviour);
            }
        }

        /// <summary>
        /// Whether a copied behaviour is one of the kinds that would undo our own work.
        /// Named one at a time rather than matched on the type's name: that is
        /// <c>MemberInfo.get_Name</c>, which is <c>System.Reflection</c>, which the mod
        /// loader refuses to load an assembly for.
        /// </summary>
        private static bool Unwanted(MonoBehaviour behaviour)
        {
            return behaviour is LoadSaveButton
                || behaviour is Localisation.LocalisationChild
                || behaviour is Localisation.LocalisationChildClampingWidth;
        }

        /// <summary>
        /// Keeps the copied button answering the mouse, and does its clicking.
        ///
        /// The tooltip came up while the press did nothing, and both are driven by the
        /// same mouse messages on the same collider -- so the collider is fine and
        /// <c>SimpleUIButton</c> is not: a press is only *finished* in its
        /// <c>LateUpdate</c>, which Unity does not call on a disabled behaviour, and
        /// something switches that button off when the browser has nothing for it to
        /// act on.
        ///
        /// So it is switched back on every frame -- directly, since
        /// <c>ToggleButton</c> does nothing when the collider is not on the button's
        /// own object -- and the press is watched for here. This button is on screen
        /// only when there is something to compare: if it can be seen it can be
        /// pressed.
        /// </summary>
        private void KeepPressable()
        {
            if (_diffAll == null || !_diffAll.activeInHierarchy)
            {
                _pressedOn = false;
                return;
            }
            if (_press != null && !_press.enabled)
            {
                _press.enabled = true;
            }
            for (int i = 0; _hits != null && i < _hits.Length; i++)
            {
                if (_hits[i] != null && !_hits[i].enabled)
                {
                    _hits[i].enabled = true;
                }
            }

            // Pressed and released over the same button, which is what a click is.
            bool over = PointerOver(_diffAll);
            if (Input.GetMouseButtonDown(0))
            {
                _pressedOn = over;
            }
            else if (Input.GetMouseButtonUp(0))
            {
                bool clicked = _pressedOn && over;
                _pressedOn = false;
                if (clicked)
                {
                    OnDiffAllPressed();
                }
            }
        }

        /// <summary>Whether the press that is in progress started on our button.</summary>
        private bool _pressedOn;

        // How big the fallback tooltip is drawn and where it hangs, in button widths,
        // measured off Besiege's own. Sized by height rather than width, so the
        // lettering stays one size however much of it there is.
        private const float TipHeight = 0.86f;
        private const float TipDrop = 1.19f;

        /// <summary>The words the tooltip is showing, so it is only redrawn when they change.</summary>
        private string _tipWords = string.Empty;
        private Texture2D _tipFace;

        /// <summary>
        /// Hangs a tooltip under a button, hidden until the pointer is on it: a quad
        /// of ours with a picture of the words on it. The mesh, material and shader
        /// are the ones the mod's icons are already drawn with, that being the only
        /// combination seen to appear on this screen.
        /// </summary>
        private void AddTip(GameObject clone, SimpleUIButton press)
        {
            GameObject tip = new GameObject(TipName, typeof(MeshFilter),
                                            typeof(MeshRenderer));
            tip.transform.SetParent(clone.transform, false);
            tip.transform.localRotation = Quaternion.identity;
            tip.layer = clone.layer;
            tip.GetComponent<MeshFilter>().sharedMesh = DoubleSidedQuad();

            // Just under the button in the button's own units, and a little in front
            // of it, since the button sits in the panel the tooltip would otherwise
            // hang inside. Which way "in front" is depends on how the browser faces
            // the camera, so it is asked rather than assumed.
            float scale = Mathf.Abs(clone.transform.lossyScale.x);
            if (scale < 0.0001f)
            {
                scale = 1f;
            }
            float side = WorldWidth(clone.transform) / scale;
            if (side <= 0f)
            {
                side = 1f;
            }
            tip.transform.localPosition = new Vector3(0f, -side * TipDrop,
                                                      Forward(clone) * side * 0.1f);
            tip.transform.localScale = new Vector3(side * TipHeight * 3f,
                                                   side * TipHeight, 1f);
            tip.SetActive(false);
        }

        /// <summary>
        /// Which way along an object's own z the camera is: -1 or 1, and 0 if
        /// nothing can see it.
        /// </summary>
        private static float Forward(GameObject seen)
        {
            Camera camera = Watching(seen);
            if (camera == null)
            {
                return 0f;
            }
            float z = seen.transform.InverseTransformPoint(
                camera.transform.position).z;
            return z < 0f ? -1f : 1f;
        }

        /// <summary>A camera that draws this object's layer, or null.</summary>
        private static Camera Watching(GameObject seen)
        {
            Camera[] cameras = Camera.allCameras;
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null &&
                    (cameras[i].cullingMask & (1 << seen.layer)) != 0)
                {
                    return cameras[i];
                }
            }
            return null;
        }

        /// <summary>
        /// Shows the fallback tooltip while the pointer is on the button. Asked of the
        /// collider directly, every frame, rather than left to
        /// <c>SimpleUIButton</c>'s enter and exit events: those are Unity's mouse
        /// messages, and they stop arriving the moment anything disables the collider.
        /// <c>Collider.Raycast</c> asks one collider one question.
        /// </summary>
        private void ShowTipOnHover()
        {
            // Besiege's own tooltip shows itself, off the same mouse messages the
            // button hears.
            if (_tip != null || _diffAll == null || !_diffAll.activeInHierarchy)
            {
                return;
            }
            Transform tip = _diffAll.transform.FindChild(TipName);
            if (tip != null)
            {
                Reveal(tip.gameObject, PointerOver(_diffAll));
            }
        }

        /// <summary>
        /// Whether the pointer is on the compare button. Asked of the colliders the
        /// button was copied with, kept from before its tooltip was hung off it: a
        /// search for "a collider under this button" would find the tooltip's.
        /// </summary>
        private bool PointerOver(GameObject button)
        {
            Camera camera = Watching(button);
            if (camera == null)
            {
                return false;
            }
            Ray ray = camera.ScreenPointToRay(Input.mousePosition);

            if (_hits != null && _hits.Length > 0)
            {
                for (int i = 0; i < _hits.Length; i++)
                {
                    RaycastHit where;
                    if (_hits[i] != null && _hits[i].enabled &&
                        _hits[i].Raycast(ray, out where, 10000f))
                    {
                        return true;
                    }
                }
                return false;
            }

            Collider box = button.GetComponent<Collider>();
            if (box == null)
            {
                box = button.GetComponentInChildren<Collider>(true);
            }
            RaycastHit hit;
            return box != null && box.Raycast(ray, out hit, 10000f);
        }

        private static void Reveal(GameObject tip, bool shown)
        {
            if (tip != null && tip.activeSelf != shown)
            {
                tip.SetActive(shown);
            }
        }

        /// <summary>
        /// Puts words on a button's tooltip, redrawing the picture only when they
        /// change. The quad is reshaped to whatever the words came out as, so they are
        /// the same height however many there are.
        /// </summary>
        private void Say(GameObject button, string words)
        {
            if (button == null || _tipWords == words)
            {
                return;
            }
            Transform tip = button.transform.FindChild(TipName);
            if (tip == null)
            {
                return;
            }

            // Besiege's tooltip is text, not a picture of text: the words go on the
            // meshes the copy brought with it, and it resizes its own plate.
            if (_tip != null)
            {
                _tipWords = words;
                TextMesh[] lines = tip.GetComponentsInChildren<TextMesh>(true);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i] != null)
                    {
                        lines[i].text = words;
                    }
                }
                return;
            }

            Texture2D drawn = IconArt.Words(words, UIF.Font);
            if (drawn == null)
            {
                // Said once: a tooltip that never appears is otherwise
                // indistinguishable from one that appears somewhere nobody looks.
                if (!_saidNoWords)
                {
                    _saidNoWords = true;
                    Log.Warn("could not draw the compare button's tooltip" +
                             (UIF.Font == null ? "; UI Factory has no font yet." : "."));
                }
                return;
            }
            drawn.hideFlags = HideFlags.HideAndDontSave;
            _tipWords = words;

            MeshRenderer ink = tip.GetComponent<MeshRenderer>();
            if (ink != null)
            {
                ink.sharedMaterial = IconMaterial(drawn);
            }
            // The height is fixed -- that is the size of the lettering -- and the
            // width follows from however many words there are.
            Vector3 was = tip.localScale;
            tip.localScale = new Vector3(was.y * drawn.width / drawn.height, was.y,
                                         was.z);

            if (_tipFace != null)
            {
                Destroy(_tipFace);
            }
            _tipFace = drawn;
        }

        private bool _saidNoWords;

        // ------------------------------------------------------------------ pressing

        /// <summary>Picks a machine out for a comparison, or puts it back.</summary>
        private void OnChoosePressed(FileBrowserSlot slot)
        {
            if (slot == null)
            {
                return;
            }
            Selection.Toggle(slot.VirtualObject);
            // Next frame rather than in a quarter of a second, so the mark appears
            // under the press -- but still through the poll, rather than sweeping from
            // inside the browser's own click event.
            _nextPoll = 0f;
        }

        /// <summary>
        /// Opens the chosen machines in the history window, where they compare the
        /// same way the versions of one machine do.
        /// </summary>
        private void OnDiffAllPressed()
        {
            if (_busy || _history == null || Selection.Count < 2)
            {
                return;
            }
            List<VersionEntry> rows = Selection.AsRows();
            string title = Selection.Count + " MACHINES";
            // The pictures are put back before the browser closes rather than at the
            // next sweep: after it closes there are no slots left to put them on.
            RestoreAll();
            Selection.Clear();
            StartCoroutine(OpenHistory(title, rows, true));
        }


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

            StartCoroutine(OpenHistory(item.Name, versions, false));
        }

        /// <summary>
        /// Closes the load screen, then opens the newest version and the history
        /// beside it -- in that order and a frame apart, since the browser is mid-click
        /// when this runs.
        /// </summary>
        private IEnumerator OpenHistory(string machineName, List<VersionEntry> versions,
                                        bool chosen)
        {
            _busy = true;
            CloseBrowser();
            yield return null;

            _history.OpenNewest(machineName, versions, chosen);
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
