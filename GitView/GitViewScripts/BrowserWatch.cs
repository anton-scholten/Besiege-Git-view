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
        /// The mark over a chosen machine's thumbnail is drawn larger than a
        /// button's icon, because it carries a number as well as a glyph and is
        /// read from across the page.
        /// </summary>
        private const int BadgeSize = 192;

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
        private bool _reportedFaces;
        private float _nextPoll;
        private bool _reportedLayout;
        private bool _reportedNames;

        /// <summary>The one compare-them-all button, or null while the browser is shut.</summary>
        private GameObject _diffAll;
        private bool _busy;

        public void Bind(HistoryView history)
        {
            _history = history;
        }

        private void Update()
        {
            // Every frame, unlike the rest of this: a tooltip that waited a quarter
            // of a second for the sweep would be a tooltip that lags the pointer.
            ShowTipOnHover();

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
        /// Whether this slot is a machine that can be chosen for a comparison.
        ///
        /// Any machine file, wherever it is -- including the versions inside an
        /// autosave folder, which is the whole point of being able to choose two
        /// things by hand. Not folders: pressing the compare button on one of those
        /// already means "every version of this machine", and choosing a folder
        /// would have to mean something different from choosing a file.
        /// </summary>
        private static bool CanChoose(FileBrowserSlot slot)
        {
            IVirtualObject item = slot.VirtualObject;
            return item != null && !item.IsFolder;
        }

        // ------------------------------------------------------------------ deciding

        /// <summary>
        /// Whether this slot is a machine's history: one of the folders inside
        /// AutoSave, each of which is every version of one machine.
        ///
        /// Only those. A machine's own slot out in SavedMachines used to carry this
        /// button as well, and it was one branch too many: the slot already has
        /// Besiege's own button for going to that machine's autosaves, and this mod
        /// already puts a branch on it -- the one that adds it to a comparison --
        /// so a second, differently-meaning branch two icons along was three ways of
        /// saying "versions" in one corner. Follow the game's own button into the
        /// folder and the history button is on the folder, where it says exactly
        /// what it does.
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
        ///
        /// Above the row along the bottom rather than in it: that row is where a
        /// slot keeps the things it does to itself -- load, delete, versions -- and
        /// this is about this machine and another one.
        /// </summary>
        private void AddSelectButton(FileBrowserSlot slot)
        {
            SimpleUIButton template = PickTemplate(slot);
            if (template == null)
            {
                return;
            }
            ReportButtons(slot);
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
        /// Copies one of the slot's own buttons, which brings the right mesh,
        /// material, collider and press behaviour with it, and puts one of our
        /// faces on it.
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
            // A clone carries the prefab's serialised state but not its delegates,
            // since those are not serialised. Clearing them anyway costs nothing and
            // means a future Besiege that does wire them up cannot leave this button
            // also deleting the machine.
            button.ResetDelegates();
            return button;
        }

        /// <summary>
        /// Straight above the leftmost icon in the slot's bottom row -- the corner
        /// of the picture, over the steam mark.
        ///
        /// Taken off the row rather than off one button found by name, which is what
        /// this did: the cloud is the *second* icon along, so the button sat over
        /// the middle of the row with the corner beside it empty. The leftmost of
        /// whatever the slot is showing is a place that stays the corner however
        /// many icons a machine happens to have.
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
        /// One of the slot's own buttons, by name.
        ///
        /// By name because <c>FileBrowserSlot</c> keeps every one of them in a
        /// private field -- `cloudButton`, `loadAsSelectionButton` and the rest are
        /// all unreachable from a mod, as is the text mesh holding the file name.
        /// A name is a weaker handle than a field and it is the handle there is; a
        /// miss costs the fallback placement rather than the button.
        /// </summary>
        /// <summary>
        /// Writes down what a slot's buttons are called, once.
        ///
        /// The two new buttons are placed off buttons found by name, and the names
        /// are Besiege's rather than anything documented. If either lands somewhere
        /// strange, this line says whether the name was found at all or whether the
        /// fallback placement is what is on screen.
        /// </summary>
        private void ReportButtons(FileBrowserSlot slot)
        {
            if (_reportedNames)
            {
                return;
            }
            _reportedNames = true;

            StringBuilder names = new StringBuilder();
            SimpleUIButton[] buttons = slot.GetComponentsInChildren<SimpleUIButton>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null || buttons[i].gameObject == slot.gameObject)
                {
                    continue;
                }
                names.Append(names.Length == 0 ? "" : ", ").Append(buttons[i].name);
                if (!buttons[i].gameObject.activeInHierarchy)
                {
                    names.Append(" (hidden)");
                }
            }
            Log.Info("slot buttons: " + (names.Length == 0 ? "none" : names.ToString()) +
                     "; matched cloud=" + (Named(slot, "cloud") != null) +
                     " selection=" + (Named(slot, "selection") != null) + ".");
        }

        private static SimpleUIButton Named(FileBrowserSlot slot, string part)
        {
            SimpleUIButton[] buttons = slot.GetComponentsInChildren<SimpleUIButton>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null || buttons[i].gameObject == slot.gameObject)
                {
                    continue;
                }
                if (buttons[i].name.ToLower().Contains(part))
                {
                    return buttons[i];
                }
            }
            return null;
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
            ReportFaces(renderers);

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

        private static bool PaintOne(Renderer renderer, Material material, Texture2D face)
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
            Log.Info("drew an icon on a quad of our own, " + side.ToString("0.###") +
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
        private static Sprite IconSprite(Sprite replacing, Texture2D texture)
        {
            float density = 100f;
            if (replacing != null && replacing.rect.height > 0f && replacing.pixelsPerUnit > 0f)
            {
                float worldHeight = replacing.rect.height / replacing.pixelsPerUnit * IconScale;
                density = texture.height / Mathf.Max(worldHeight, 0.0001f);
            }

            // Cached per size rather than one shared sprite: different slots can
            // carry differently authored buttons, and a sprite is cheap but not
            // free.
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
            Log.Info("icon sized at " + density.ToString("0.#") + " pixels per unit" +
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

        /// <summary>
        /// One of the mod's faces, drawn once and kept: the branch, the compare
        /// mark, or the compare mark carrying a number.
        /// </summary>
        private static Texture2D IconTexture(string face)
        {
            Texture2D drawn;
            if (_faces.TryGetValue(face, out drawn) && drawn != null)
            {
                return drawn;
            }

            // The window's font, so the mark in the corner of an icon is the same
            // lettering as the number in the window it opens. Null until UI Factory
            // has loaded, which is why a face drawn without it is not kept: the next
            // machine to be chosen should get the real thing.
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

        /// <summary>The branch on a dark square, for the row of buttons at the top.</summary>
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
                // Particles/Alpha Blended doubles the texture against this, so a
                // plain white keeps the glyph at full strength there and changes
                // nothing under the shaders that ignore it.
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
        /// Brings one slot into line with what has been chosen.
        ///
        /// Every sweep, for every slot, rather than only when something is pressed:
        /// the browser reuses its slots for whatever file is on the page now, so a
        /// mark put on a slot is not a mark on a machine until something keeps
        /// checking that the slot is still showing the machine it was put there for.
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
        /// Puts the numbered mark on a slot and dims its picture, or moves the mark
        /// to a new number if unchoosing something else has renumbered it.
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
            // Asked again every sweep rather than only when the mark is new: a
            // thumbnail arrives when its file has finished loading, which can be
            // several sweeps after the slot appeared and is always after the page
            // is turned back to. Whatever the browser has just painted over the top
            // of ours is dimmed again from here.
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
        /// Takes the mark off a slot and puts its picture back.
        ///
        /// Only if the slot is still showing the dimmed copy we made, which is the
        /// one question worth asking here: a slot handed to another machine has
        /// been redrawn by the browser already, and putting the picture back then
        /// would put the *previous* machine's picture on the one now in the slot.
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
                    // What we replaced, or failing that the browser's own copy,
                    // which was never touched -- only the slot's material was.
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
        /// The big numbered mark, over the middle of the thumbnail.
        ///
        /// In front of it by as much as the slot's own buttons are in front of the
        /// slot: which way "in front" is depends on how the browser is turned to
        /// face the camera, and the buttons are the one thing here that is known to
        /// be visible.
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
        /// How far in front of the slot to draw, taken from the button that is
        /// furthest forward. A transparent quad level with the picture behind it
        /// z-fights; one in front of everything the slot draws does not.
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
        /// Replaces a chosen machine's picture with a small, dark copy of itself.
        ///
        /// The original goes into the selection rather than into the slot, because
        /// the browser caches a thumbnail on its own object for the machine: put
        /// back only what the slot is holding and the dimmed copy survives in that
        /// cache, and the machine comes up dim the next time the load screen opens.
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
        /// What the picture is drawn with. The slot's own <c>ApplyTexture</c> is not
        /// public, but the material behind it is per-slot -- <c>ThumbnailComponent</c>
        /// takes it from <c>Renderer.material</c>, which instances one -- so setting
        /// the texture on it is the same thing done from outside, and the game
        /// setting its own texture later still works.
        /// </summary>
        private static Material PictureMaterial(FileBrowserSlot slot)
        {
            Renderer picture = Picture(slot);
            return picture == null ? null : picture.sharedMaterial;
        }

        /// <summary>
        /// Takes every mark off every slot on screen and puts every dimmed picture
        /// back. Used when the choosing is over, while there is still something to
        /// put them back on.
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
        /// Keeps the one compare-them-all button, up beside the load buttons.
        ///
        /// One button rather than one per machine: it is not about a machine. It
        /// belongs where the other things that act on the whole screen are, which
        /// is the row at the top, and it is only on show once there are two
        /// machines to compare -- one machine is not a diff.
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
                    // pointer left, so its tooltip would be waiting, still shown,
                    // the next time there is something to compare.
                    Transform tip = _diffAll.transform.FindChild(TipName);
                    if (tip != null)
                    {
                        Reveal(tip.gameObject, false);
                    }
                }
                _diffAll.SetActive(enough);
            }
            if (enough)
            {
                Say(_diffAll, "COMPARE " + Selection.Count + " MACHINES");
            }
        }

        /// <summary>
        /// Copies one of the load buttons at the top of the screen.
        ///
        /// Copied from a neighbour, as everything else here is: it is the only way
        /// to be the same size, the same material and the same shape as the buttons
        /// it stands beside. <c>LoadSaveButton</c> has no Awake and no Start, so a
        /// copy of one does nothing on its own; the component goes anyway, along
        /// with the localisation that would put "load machine" back over our own
        /// text at the next language change.
        /// </summary>
        private GameObject BuildDiffAllButton()
        {
            LoadSaveButton[] loads = FindObjectsOfType<LoadSaveButton>();
            if (loads == null || loads.Length == 0)
            {
                return null;
            }

            // The rightmost one that is actually on screen. A hidden load button is
            // a different size and in a different place, and copying one puts ours
            // somewhere nobody can see and at a scale nothing else is drawn at.
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

            // Worked out before the copy exists, so the copy cannot be mistaken for
            // one of the buttons the row is measured from.
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

            // Only the picture is repainted, and nothing is turned off: the plate
            // under it is the button's own, drawn by a renderer of its own, and the
            // glyph is the smallest thing the button draws. The plate is drawn into
            // our texture as well, which costs nothing where the button's own is
            // there -- black on black, and behind ours -- and leaves the icon
            // looking deliberate where it is not.
            Renderer face = IconRenderer(clone);
            Strip(clone);
            Texture2D plated = IconTexture(FacePlated);
            if (face == null || !PaintOne(face, IconMaterial(plated), plated))
            {
                Remember(clone, plated);
            }
            else
            {
                MatchSize(clone, rightmost.gameObject, face);
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
            button.ResetDelegates();
            button.Click += new Click(delegate { OnDiffAllPressed(); });
            AddTip(clone, button);
            // The words belong to the button, and this is a new one -- the browser
            // takes the last with it when it closes. Without this the tooltip on the
            // second visit is a blank plate, because what it should say is what it
            // already said.
            _tipWords = string.Empty;
            clone.SetActive(false);
            Log.Info("added the compare-them-all button at " + spot + ".");
            return clone;
        }

        /// <summary>
        /// Grows a copied button until what it draws is the height of what the
        /// button it was copied from draws.
        ///
        /// The copy draws its icon and nothing else -- the plate is another
        /// renderer, and whatever switches that on cannot be kept (see
        /// <see cref="Strip"/>). An icon quad is about three fifths of the plate it
        /// sits on, measured on screen, so a copy that draws only its icon comes out
        /// three fifths the size of the buttons beside it. Since our own plate fills
        /// the icon, growing the whole button until the icon is plate-sized puts it
        /// back in the row. The collider grows with it, so what can be clicked is
        /// still what can be seen.
        /// </summary>
        private static void MatchSize(GameObject clone, GameObject original,
                                      Renderer face)
        {
            float want = DrawnHeight(original);
            float have = face.bounds.size.y;
            if (want <= 0.0001f || have <= 0.0001f)
            {
                return;
            }
            float ratio = want / have;
            if (ratio < 1.01f)
            {
                return;
            }
            clone.transform.localScale = clone.transform.localScale * ratio;
            Log.Info("compare-them-all button grown by " + ratio.ToString("0.##") +
                     " to match the load buttons.");
        }

        /// <summary>How tall everything a button draws is, together, in world units.</summary>
        private static float DrawnHeight(GameObject button)
        {
            Renderer[] renderers = button.GetComponentsInChildren<Renderer>(false);
            float top = float.MinValue;
            float bottom = float.MaxValue;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled ||
                    renderer.GetComponent<TextMesh>() != null)
                {
                    continue;
                }
                top = Mathf.Max(top, renderer.bounds.max.y);
                bottom = Mathf.Min(bottom, renderer.bounds.min.y);
            }
            return top > bottom ? top - bottom : 0f;
        }

        /// <summary>
        /// The renderer a button draws its icon on, guessed as the smallest thing
        /// it draws.
        ///
        /// <c>LoadSaveButton</c> names it in `buttonMeshRenderer` and that field is
        /// private like every other one in the browser, so the size is what there is
        /// to go on -- and it is a good deal better than a guess: a button is an
        /// icon on a plate, and the plate is what the icon is drawn on top of, so it
        /// is never the smaller of the two. Repainting the smallest leaves the plate
        /// where it is, which is the whole point.
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
        /// The next place along a row of buttons: one more step to the right of the
        /// rightmost one that is still part of the row the given button is in.
        ///
        /// Walked rather than measured off the two load buttons, which is what this
        /// did and what put ours a button and a half away from its neighbour. The
        /// buttons up there are not all of a type -- "load as selection" is not a
        /// <c>LoadSaveButton</c> at all -- so the gap between the two that are is
        /// not the gap the row is actually drawn on. Hopping from button to button
        /// while the next one is close enough to be beside this one finds the end of
        /// the row and the pitch it was laid out on, whatever the buttons happen to
        /// be made of.
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
        /// How wide a button is, in the units of whatever it is parented to.
        ///
        /// Off the collider first: it is what makes a button clickable, so it is
        /// the one part that has to be there and is the button's own size. A
        /// renderer's bounds would take in anything else hanging off it -- a
        /// tooltip, for one, which is wider than the button by a good deal.
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
        /// Takes everything off a copied button but the <c>SimpleUIButton</c> that
        /// makes it a button.
        ///
        /// Keeping the rest was tried, to see whether one of them was what draws the
        /// plate under the icon. It is not worth the price: the copy stopped
        /// responding to the mouse altogether. Something on that button turns itself
        /// off when the browser says it has nothing to act on --
        /// <c>SimpleUIButton.ToggleButton</c> disables the *collider* as well as the
        /// behaviour, and a button with no collider gets no mouse messages at all,
        /// which is every click and every hover. With <c>LoadSaveButton</c> gone
        /// there is nothing left to turn it back on.
        ///
        /// So: everything goes, and the plate is drawn into our own icon instead.
        ///
        /// <c>Tooltip</c> would have to go in any case. It keeps what it shows in a
        /// private <c>tooltipParent</c>, and Unity only redirects a copied reference
        /// when it points inside the copy. That one points outside, so the copy's
        /// tooltip is the *original's* -- hovering ours lit up the load button's
        /// words, in the load button's place, with nothing on our own object to
        /// rewrite, and the field cannot be repointed without reflection.
        /// <see cref="AddTip"/> puts up one of ours instead.
        /// </summary>
        private static void Strip(GameObject clone)
        {
            MonoBehaviour[] behaviours = clone.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour is SimpleUIButton)
                {
                    continue;
                }
                behaviour.enabled = false;
                Destroy(behaviour);
            }
        }

        // How big the tooltip is drawn and where it hangs, in button widths, off
        // Besiege's own: a plate a little over half the button's height with a
        // quarter of that again for the arrow above it, and the arrow's point a
        // quarter of a button below the button. Sized by its height rather than its
        // width, so the lettering stays one size however much of it there is.
        private const float TipHeight = 0.86f;
        private const float TipDrop = 1.19f;

        /// <summary>The words the tooltip is showing, so it is only redrawn when they change.</summary>
        private string _tipWords = string.Empty;
        private Texture2D _tipFace;

        /// <summary>
        /// Hangs a tooltip under a button, hidden until the pointer is on it.
        ///
        /// A quad of ours with a picture of the words on it -- see
        /// <see cref="IconArt.Words"/> for why the words are drawn rather than
        /// written. The mesh, the material and the shader are the ones the mod's
        /// icons are already drawn with, which is the only combination of those
        /// three that has been seen to appear on this screen.
        ///
        /// Shown off <c>SimpleUIButton</c>'s own enter and exit events, which is how
        /// the game's own tooltips are driven.
        /// </summary>
        private void AddTip(GameObject clone, SimpleUIButton press)
        {
            GameObject tip = new GameObject(TipName, typeof(MeshFilter),
                                            typeof(MeshRenderer));
            tip.transform.SetParent(clone.transform, false);
            tip.transform.localRotation = Quaternion.identity;
            tip.layer = clone.layer;
            tip.GetComponent<MeshFilter>().sharedMesh = DoubleSidedQuad();

            // Just under the button, in the button's own units: far enough down to
            // be clear of it, near enough to belong to it. And a little in front of
            // it, because the button sits in the panel the tooltip would otherwise
            // hang inside -- which way "in front" is depends on how the browser is
            // turned to face the camera, so it is asked rather than assumed.
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
        /// Shows the tooltip while the pointer is on the button.
        ///
        /// Asked of the collider directly, every frame, rather than left to
        /// <c>SimpleUIButton</c>'s enter and exit events. Those are Unity's mouse
        /// messages, they are how the game's own buttons work, and they are also the
        /// thing that stops arriving the moment anything disables the collider --
        /// which is how the first attempt at this came to show nothing at all.
        /// <c>Collider.Raycast</c> asks one collider one question and cannot be
        /// blocked by another.
        /// </summary>
        private void ShowTipOnHover()
        {
            if (_diffAll == null || !_diffAll.activeInHierarchy)
            {
                return;
            }
            Transform tip = _diffAll.transform.FindChild(TipName);
            if (tip != null)
            {
                Reveal(tip.gameObject, PointerOver(_diffAll));
            }
        }

        private static bool PointerOver(GameObject button)
        {
            Collider box = button.GetComponent<Collider>();
            if (box == null)
            {
                box = button.GetComponentInChildren<Collider>(true);
            }
            Camera camera = box == null ? null : Watching(button);
            if (camera == null)
            {
                return false;
            }

            RaycastHit hit;
            return box.Raycast(camera.ScreenPointToRay(Input.mousePosition), out hit,
                               10000f);
        }

        private static void Reveal(GameObject tip, bool shown)
        {
            if (tip != null && tip.activeSelf != shown)
            {
                tip.SetActive(shown);
            }
        }

        /// <summary>
        /// Puts words on a button's tooltip, redrawing the picture of them when they
        /// change -- which is only when the number of chosen machines changes.
        ///
        /// The quad is reshaped to whatever the words came out as, so that they are
        /// the same height however many of them there are and the plate around them
        /// keeps its margin.
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
            // The height is what is fixed -- that is the size of the lettering -- and
            // the width follows from however many words there are.
            Vector3 was = tip.localScale;
            tip.localScale = new Vector3(was.y * drawn.width / drawn.height, was.y,
                                         was.z);

            if (_tipFace != null)
            {
                Destroy(_tipFace);
            }
            _tipFace = drawn;

            if (!_saidTip)
            {
                _saidTip = true;
                Log.Info("tooltip \"" + words + "\" drawn " + drawn.width + "x" +
                         drawn.height + ", quad at " + tip.localPosition + " size " +
                         tip.localScale + " (button " +
                         button.transform.localScale + ").");
            }
        }

        private bool _saidTip;
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
            // under the press. Through the poll rather than by sweeping from inside
            // a click, which would put the whole sweep -- including building
            // buttons -- inside the browser's own event.
            _nextPoll = 0f;
        }

        /// <summary>
        /// Opens the chosen machines in the history window, where they can be
        /// compared with each other the same way the versions of one machine are.
        /// </summary>
        private void OnDiffAllPressed()
        {
            if (_busy || _history == null || Selection.Count < 2)
            {
                return;
            }
            List<VersionEntry> rows = Selection.AsRows();
            string title = Selection.Count + " MACHINES";
            // The choosing is finished with, and the window about to open is what
            // carries it from here. The pictures are put back before the browser
            // closes rather than at the next sweep, because after it closes there
            // are no slots left to put anything back on.
            RestoreAll();
            Selection.Clear();
            StartCoroutine(OpenHistory(title, rows));
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
