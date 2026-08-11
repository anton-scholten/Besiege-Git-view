using System;
using System.Collections;
using System.Collections.Generic;
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
        private const int IconSize = 64;

        /// <summary>
        /// How far along the row of a slot's own buttons ours is placed, as a
        /// multiple of the gap between the existing ones.
        /// </summary>
        private const float StepFactor = 1f;

        private HistoryView _history;
        private Texture2D _icon;
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

            Face(clone);

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
        /// Where to put the button, worked out from where the slot's existing ones
        /// are rather than from numbers measured off a screenshot.
        ///
        /// Besiege lays a slot's buttons out in a line. Reading their spacing and
        /// stepping one place past the end of it puts ours in the row with them
        /// whatever the slot looks like, and whatever a future Besiege moves around
        /// -- which a hardcoded offset would not survive.
        /// </summary>
        private Vector3 FreeSpot(FileBrowserSlot slot, SimpleUIButton template)
        {
            List<Vector3> places = new List<Vector3>();
            SimpleUIButton[] buttons = slot.GetComponentsInChildren<SimpleUIButton>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null && buttons[i].gameObject != slot.gameObject &&
                    buttons[i].name != ButtonName)
                {
                    places.Add(buttons[i].transform.localPosition);
                }
            }

            Vector3 anchor = template.transform.localPosition;
            float step = Width(template) * 1.25f;

            if (places.Count >= 2)
            {
                // The smallest gap between two of them along x is their pitch; the
                // larger gaps are between separate groups.
                float pitch = float.MaxValue;
                float lowest = float.MaxValue;
                for (int i = 0; i < places.Count; i++)
                {
                    if (places[i].x < lowest)
                    {
                        lowest = places[i].x;
                        anchor = places[i];
                    }
                    for (int j = i + 1; j < places.Count; j++)
                    {
                        float gap = Mathf.Abs(places[i].x - places[j].x);
                        if (gap > 0.0001f && gap < pitch)
                        {
                            pitch = gap;
                        }
                    }
                }
                if (pitch < float.MaxValue)
                {
                    step = pitch;
                }
            }

            Vector3 spot = new Vector3(anchor.x - step * StepFactor, anchor.y, anchor.z);
            if (!_reportedLayout)
            {
                _reportedLayout = true;
                Log.Info("slot has " + places.Count + " buttons; placing the compare " +
                         "button at " + spot + " (step " + step.ToString("0.###") + ").");
            }
            return spot;
        }

        private static float Width(SimpleUIButton button)
        {
            Renderer renderer = button.GetComponent<Renderer>();
            if (renderer != null && renderer.bounds.size.x > 0.0001f)
            {
                return renderer.bounds.size.x;
            }
            Collider collider = button.GetComponent<Collider>();
            if (collider != null && collider.bounds.size.x > 0.0001f)
            {
                return collider.bounds.size.x;
            }
            return 0.5f;
        }

        /// <summary>
        /// Puts our own glyph on the copied button.
        ///
        /// Assigns through <c>Renderer.material</c>, which instantiates the material
        /// rather than editing it in place: the original is shared with the button
        /// this was copied from, and with every other slot's copy of it, so writing
        /// to it would put this icon on the game's own delete buttons.
        /// </summary>
        private void Face(GameObject button)
        {
            if (_icon == null)
            {
                _icon = IconArt.Compare(IconSize);
                _icon.hideFlags = HideFlags.HideAndDontSave;
            }

            Renderer[] renderers = button.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material material = renderers[i].material;
                if (material != null)
                {
                    material.mainTexture = _icon;
                }
            }
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
