using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GitView
{
    /// <summary>The widgets of one row, kept so the row can be rebound when the sort changes.</summary>
    public class HistoryRow
    {
        public GameObject Root;
        public RectTransform Rect;
        public RawImage Thumbnail;
        public Text Stamp;
        public Text Added;
        public Text Changed;
        public Text Removed;
        public Image Highlight;
        public VersionEntry Entry;
        public bool ThumbnailLoaded;
    }

    /// <summary>
    /// The history window: every saved version of one machine, with its thumbnail,
    /// when it was taken, and what it did to the machine.
    ///
    /// Built out of UI Factory's prefabs so it is Besiege's window rather than a
    /// drawing of one, on its own canvas because Besiege's HUD is a Screen Space
    /// Overlay canvas and Unity draws those over everything IMGUI produces.
    /// </summary>
    public class HistoryView : MonoBehaviour
    {
        private const int CanvasOrder = 29000;
        private const float WindowWidth = 700f;
        private const float WindowHeight = 560f;
        private const float RowHeight = 66f;
        private const float HeaderHeight = 34f;
        private const int RowFontSize = 15;
        private const int HeaderFontSize = 14;

        // Column edges as fractions of the row's width. One table, used by both the
        // header and every row, so the two can never drift apart.
        private const float ThumbEnd = 0.145f;
        private const float StampEnd = 0.565f;
        private const float AddedEnd = 0.710f;
        private const float ChangedEnd = 0.855f;

        private static readonly Color AddedInk = new Color(0.31f, 0.85f, 0.42f, 1f);
        private static readonly Color ChangedInk = new Color(1f, 0.66f, 0.16f, 1f);
        private static readonly Color RemovedInk = new Color(0.96f, 0.35f, 0.36f, 1f);
        private static readonly Color QuietInk = new Color(0.72f, 0.72f, 0.74f, 1f);
        private static readonly Color SelectedTint = new Color(1f, 1f, 1f, 0.16f);
        private static readonly Color ClearTint = new Color(1f, 1f, 1f, 0f);

        private readonly List<HistoryRow> _rows = new List<HistoryRow>();
        private readonly GhostView _ghosts = new GhostView();

        private List<VersionEntry> _versions = new List<VersionEntry>();
        private string _machineName = string.Empty;
        private GameObject _window;
        private ScrollRect _scroll;
        private RectTransform _content;
        private RectTransform _viewport;
        private Text _status;
        private readonly Text[] _headers = new Text[RowSort.ColumnCount];

        private int _sortColumn = RowSort.ByTime;
        private bool _ascending;
        private int _selected = -1;
        private bool _built;
        private bool _counting;

        /// <summary>Whether the window is on screen.</summary>
        public bool Visible
        {
            get { return _window != null && _window.activeSelf; }
        }

        public bool HasHistory
        {
            get { return _versions.Count > 0; }
        }

        // ------------------------------------------------------------------ opening

        /// <summary>
        /// Shows the history of one machine. Safe to call again with a different
        /// machine; the previous one's rows and overlay are dropped.
        /// </summary>
        public void Open(string machineName, List<VersionEntry> versions)
        {
            _machineName = machineName;
            _versions = versions ?? new List<VersionEntry>();
            _selected = -1;
            _ghosts.Clear();

            RowSort.Apply(_versions, _sortColumn, _ascending);
            UIF.WhenReady(delegate { BuildAndFill(); });
        }

        /// <summary>
        /// Shows a machine's history and loads its newest version, which is what
        /// the compare button in the load screen does.
        /// </summary>
        public void OpenNewest(string machineName, List<VersionEntry> versions)
        {
            Open(machineName, versions);

            VersionEntry newest = null;
            for (int i = 0; i < _versions.Count; i++)
            {
                if (newest == null || _versions[i].Saved > newest.Saved)
                {
                    newest = _versions[i];
                }
            }
            if (newest != null)
            {
                Select(newest);
            }
        }

        public void Toggle()
        {
            if (_window == null)
            {
                Log.Console("no machine history open. Use the compare button in the " +
                            "load screen.");
                return;
            }
            SetVisible(!_window.activeSelf);
        }

        public void SetVisible(bool visible)
        {
            if (_window != null)
            {
                _window.SetActive(visible);
            }
            _ghosts.SetVisible(visible);
        }

        private void BuildAndFill()
        {
            if (!_built)
            {
                Build();
            }
            if (!_built)
            {
                return;
            }

            SetTitle(_machineName);
            RebuildRows();
            SetVisible(true);

            if (!_counting)
            {
                StartCoroutine(CountEverything());
            }
        }

        private void OnDestroy()
        {
            _ghosts.Clear();
            DropThumbnails();
        }

        // ----------------------------------------------------------------- building

        private void Build()
        {
            if (!UIF.Ready)
            {
                Log.Warn("UI Factory 3 is not available, so the history window cannot be " +
                         "drawn. Subscribe to Workshop item 2913469777 and enable it.");
                return;
            }

            Canvas canvas = UIBuild.CreateCanvas(gameObject, CanvasOrder);
            _window = UIF.Spawn(UIF.WindowPrefab, canvas.transform);
            if (_window == null)
            {
                return;
            }

            RectTransform windowRect = UIF.Rect(_window);
            windowRect.sizeDelta = new Vector2(WindowWidth, WindowHeight);
            windowRect.anchoredPosition = new Vector2(0f, 0f);

            HookCloseButton();
            if (!FindScrollView())
            {
                return;
            }
            BuildHeader();
            BuildStatusLine();
            _built = true;
        }

        private void SetTitle(string title)
        {
            Transform bar = _window.transform.FindChild("TopBar");
            if (bar == null)
            {
                return;
            }
            Text label = bar.GetComponentInChildren<Text>(true);
            UIF.Style(label, 0, TextAnchor.MiddleCenter);
            if (label != null)
            {
                label.text = string.IsNullOrEmpty(title) ? "HISTORY" : title.ToUpper();
            }
        }

        private void HookCloseButton()
        {
            Transform bar = _window.transform.FindChild("TopBar");
            Transform close = bar == null ? null : bar.FindChild("CloseButton");
            if (close == null)
            {
                return;
            }
            // The prefab's own handler may already hide the window; adding to it is
            // what makes sure the overlay goes with it either way.
            UIF.OnClick(close.gameObject, delegate { SetVisible(false); });
        }

        /// <summary>
        /// Takes over the scroll view the Window prefab ships with, and makes room
        /// above it for the column headers.
        /// </summary>
        private bool FindScrollView()
        {
            _scroll = _window.GetComponentInChildren<ScrollRect>(true);
            if (_scroll == null || _scroll.content == null)
            {
                Log.Warn("UI Factory's Window prefab has no scroll view; cannot list the " +
                         "versions.");
                return false;
            }

            _scroll.horizontal = false;
            _scroll.vertical = true;
            _content = _scroll.content;
            _viewport = _scroll.viewport != null
                ? _scroll.viewport
                : _scroll.transform as RectTransform;

            UIBuild.InsetTop(_scroll.transform as RectTransform, HeaderHeight);

            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.offsetMin = new Vector2(0f, _content.offsetMin.y);
            _content.offsetMax = new Vector2(0f, _content.offsetMax.y);

            _scroll.onValueChanged.AddListener(delegate { RefreshThumbnails(); });
            return true;
        }

        /// <summary>
        /// The column headings, in the strip the scroll view has just given up.
        ///
        /// Placed against the window's own top edge, below the title bar, rather
        /// than against the scroll view: the prefab's scroll view is stretched
        /// inside the window, and a stretched rect's anchoredPosition and sizeDelta
        /// are offsets from its anchors rather than a place and a size, so copying
        /// them puts the header somewhere unrelated.
        /// </summary>
        private void BuildHeader()
        {
            RectTransform rect = UIBuild.CreateRect("Header", _window.transform);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, HeaderHeight);
            rect.anchoredPosition = new Vector2(0f, -TopBarHeight());

            _headers[RowSort.ByTime] = HeaderButton(rect, RowSort.ByTime, ThumbEnd, StampEnd,
                                                    TextAnchor.MiddleLeft);
            _headers[RowSort.ByAdded] = HeaderButton(rect, RowSort.ByAdded, StampEnd, AddedEnd,
                                                     TextAnchor.MiddleRight);
            _headers[RowSort.ByChanged] = HeaderButton(rect, RowSort.ByChanged, AddedEnd,
                                                       ChangedEnd, TextAnchor.MiddleRight);
            _headers[RowSort.ByRemoved] = HeaderButton(rect, RowSort.ByRemoved, ChangedEnd, 1f,
                                                       TextAnchor.MiddleRight);
        }

        /// <summary>
        /// How tall the window's title bar is, measured rather than assumed --
        /// UI Factory authors it at 50, but that is its number to change.
        /// </summary>
        private float TopBarHeight()
        {
            Transform bar = _window.transform.FindChild("TopBar");
            RectTransform rect = bar == null ? null : bar as RectTransform;
            return rect == null ? 50f : rect.rect.height;
        }

        private Text HeaderButton(RectTransform parent, int column, float from, float to,
                                  TextAnchor alignment)
        {
            GameObject button = UIF.Spawn(UIF.ButtonPrefab, parent);
            if (button == null)
            {
                return null;
            }
            UIF.Span(UIF.Rect(button), from, to, 6f, 6f);
            Text label = UIF.Caption(button, RowSort.ColumnName(column), HeaderFontSize,
                                     alignment);
            if (label != null)
            {
                label.color = QuietInk;
                UIF.Stretch(label.rectTransform, 6f, 0f);
            }
            int captured = column;
            UIF.OnClick(button, delegate { SortBy(captured); });
            return label;
        }

        private void BuildStatusLine()
        {
            GameObject text = UIF.Spawn(UIF.TextPrefab, _window.transform);
            if (text == null)
            {
                return;
            }
            RectTransform rect = UIF.Rect(text);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(0f, 26f);
            rect.anchoredPosition = new Vector2(0f, 4f);
            _status = UIF.Caption(text, "", 13, TextAnchor.MiddleCenter);
            if (_status != null)
            {
                _status.color = QuietInk;
                UIF.Stretch(_status.rectTransform, 12f, 0f);
            }
        }

        // --------------------------------------------------------------------- rows

        private void RebuildRows()
        {
            while (_rows.Count < _versions.Count)
            {
                _rows.Add(BuildRow());
            }
            for (int i = 0; i < _rows.Count; i++)
            {
                HistoryRow row = _rows[i];
                if (row.Root == null)
                {
                    continue;
                }
                bool used = i < _versions.Count;
                row.Root.SetActive(used);
                if (!used)
                {
                    ReleaseThumbnail(row);
                    row.Entry = null;
                    continue;
                }
                Bind(row, _versions[i], i);
            }

            _content.sizeDelta = new Vector2(_content.sizeDelta.x,
                                             Mathf.Max(0f, _versions.Count * RowHeight));
            UpdateHeaderMarks();
            RefreshThumbnails();
        }

        private HistoryRow BuildRow()
        {
            HistoryRow row = new HistoryRow();
            row.Root = UIF.Spawn(UIF.ButtonPrefab, _content);
            if (row.Root == null)
            {
                return row;
            }

            row.Rect = UIF.Rect(row.Root);
            row.Rect.anchorMin = new Vector2(0f, 1f);
            row.Rect.anchorMax = new Vector2(1f, 1f);
            row.Rect.pivot = new Vector2(0.5f, 1f);
            row.Rect.sizeDelta = new Vector2(0f, RowHeight - 2f);
            UIF.PivotAnimationLeft(row.Root);

            // The prefab's own label is in the way of the columns, but destroying it
            // is not safe -- other UIFactory behaviours write to it -- so it is
            // emptied and pushed out of the way instead.
            Text ownLabel = row.Root.GetComponentInChildren<Text>(true);
            if (ownLabel != null)
            {
                ownLabel.text = string.Empty;
            }

            row.Highlight = UIBuild.AddImage(row.Rect, "Selected", ClearTint);
            UIF.Stretch(row.Highlight.rectTransform, 0f, 0f);
            row.Highlight.raycastTarget = false;
            row.Highlight.transform.SetAsFirstSibling();

            row.Thumbnail = UIBuild.AddRawImage(row.Rect, "Thumb");
            UIF.Span(row.Thumbnail.rectTransform, 0f, ThumbEnd, 8f, 6f);
            row.Thumbnail.raycastTarget = false;

            row.Stamp = UIBuild.AddText(row.Rect, "Stamp", RowFontSize, TextAnchor.MiddleLeft);
            UIF.Span(row.Stamp.rectTransform, ThumbEnd, StampEnd, 8f, 6f);

            row.Added = UIBuild.AddText(row.Rect, "Added", RowFontSize, TextAnchor.MiddleRight);
            UIF.Span(row.Added.rectTransform, StampEnd, AddedEnd, 6f, 12f);
            row.Added.color = AddedInk;

            row.Changed = UIBuild.AddText(row.Rect, "Changed", RowFontSize,
                                          TextAnchor.MiddleRight);
            UIF.Span(row.Changed.rectTransform, AddedEnd, ChangedEnd, 6f, 12f);
            row.Changed.color = ChangedInk;

            row.Removed = UIBuild.AddText(row.Rect, "Removed", RowFontSize,
                                          TextAnchor.MiddleRight);
            UIF.Span(row.Removed.rectTransform, ChangedEnd, 1f, 6f, 12f);
            row.Removed.color = RemovedInk;

            HistoryRow captured = row;
            UIF.OnClick(row.Root, delegate { Choose(captured); });
            return row;
        }

        private void Bind(HistoryRow row, VersionEntry entry, int index)
        {
            if (row.Entry != entry)
            {
                ReleaseThumbnail(row);
            }
            row.Entry = entry;
            row.Rect.anchoredPosition = new Vector2(0f, -index * RowHeight);
            row.Stamp.text = entry.Stamp() + (entry.Manual ? "    SAVED" : string.Empty);
            row.Stamp.color = entry.Manual ? Color.white : QuietInk;
            row.Highlight.color = index == _selected ? SelectedTint : ClearTint;
            BindCounts(row, entry);
        }

        private static void BindCounts(HistoryRow row, VersionEntry entry)
        {
            if (entry.IsFirst)
            {
                // Nothing before it to be a change from. Its block count is the more
                // useful thing to show in the same space.
                row.Added.text = entry.Counted ? entry.BlockCount + " BLOCKS" : "";
                row.Changed.text = string.Empty;
                row.Removed.text = string.Empty;
                return;
            }
            if (!entry.Counted)
            {
                row.Added.text = "·";
                row.Changed.text = "·";
                row.Removed.text = "·";
                return;
            }
            row.Added.text = entry.Added == 0 ? "-" : "+" + entry.Added;
            row.Changed.text = entry.Changed == 0 ? "-" : "~" + entry.Changed;
            row.Removed.text = entry.Removed == 0 ? "-" : "-" + entry.Removed;
        }

        private void UpdateHeaderMarks()
        {
            for (int column = 0; column < _headers.Length; column++)
            {
                if (_headers[column] == null)
                {
                    continue;
                }
                string mark = column != _sortColumn ? string.Empty
                                                    : (_ascending ? "  ▲" : "  ▼");
                _headers[column].text = RowSort.ColumnName(column) + mark;
                _headers[column].color = column == _sortColumn ? Color.white : QuietInk;
            }
        }

        private void SortBy(int column)
        {
            if (column == _sortColumn)
            {
                _ascending = !_ascending;
            }
            else
            {
                _sortColumn = column;
                // A count is most interesting at its largest, a time at its latest,
                // so both start descending.
                _ascending = false;
            }

            VersionEntry chosen = _selected >= 0 && _selected < _versions.Count
                ? _versions[_selected] : null;
            RowSort.Apply(_versions, _sortColumn, _ascending);
            _selected = chosen == null ? -1 : _versions.IndexOf(chosen);
            RebuildRows();
            _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, 0f);
        }

        // ---------------------------------------------------------------- selecting

        /// <summary>Loads a version and draws what it changed, marking its row.</summary>
        public void Select(VersionEntry entry)
        {
            if (entry == null)
            {
                return;
            }
            _selected = _versions.IndexOf(entry);
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Highlight != null)
                {
                    _rows[i].Highlight.color = i == _selected ? SelectedTint : ClearTint;
                }
            }
            StartCoroutine(LoadAndShow(entry));
        }

        private void Choose(HistoryRow row)
        {
            if (row != null)
            {
                Select(row.Entry);
            }
        }

        /// <summary>
        /// Loads the chosen version and draws what it changed.
        ///
        /// The overlay has to wait for the load: <c>LoadMachineInfo</c> destroys and
        /// rebuilds every block, and the ghosts hang off the transform those blocks
        /// live under, so drawing them first would leave them parented to something
        /// about to be thrown away.
        /// </summary>
        private IEnumerator LoadAndShow(VersionEntry entry)
        {
            _ghosts.Clear();
            Say("Loading " + entry.FileName + "...");

            if (!VersionScan.LoadIntoWorld(entry.Path))
            {
                Say("Could not load " + entry.FileName + ".");
                yield break;
            }

            Machine machine = Machine.Active();
            int guard = 0;
            while (machine != null && machine.IsLoadingMachine && guard < 600)
            {
                guard++;
                yield return null;
            }
            yield return null;

            VersionEntry previous = Predecessor(entry);
            if (previous == null)
            {
                Say(entry.FileName + " -- the oldest version, nothing to compare against.");
                yield break;
            }

            MachineSnapshot before = VersionScan.Read(previous.Path);
            MachineSnapshot after = VersionScan.Read(entry.Path);
            if (before == null || after == null)
            {
                Say("Could not read one of the versions to compare.");
                yield break;
            }

            DiffResult diff = BlockDiff.Compare(before, after);
            _ghosts.Show(diff);
            Say(Describe(diff) + "   vs " + previous.FileName);
        }

        private static string Describe(DiffResult diff)
        {
            if (diff.IsEmpty)
            {
                return "No change";
            }
            return "+" + diff.Added.Count + " added    ~" + diff.Changed.Count +
                   " changed    -" + diff.Removed.Count + " removed";
        }

        /// <summary>
        /// The version immediately before this one in time -- which is not the row
        /// above it, once the list has been sorted by a count.
        /// </summary>
        private VersionEntry Predecessor(VersionEntry entry)
        {
            VersionEntry best = null;
            for (int i = 0; i < _versions.Count; i++)
            {
                VersionEntry other = _versions[i];
                if (other == entry || other.Saved >= entry.Saved)
                {
                    continue;
                }
                if (best == null || other.Saved > best.Saved)
                {
                    best = other;
                }
            }
            return best;
        }

        // ----------------------------------------------------------------- counting

        /// <summary>
        /// Fills in every row's counts, oldest first.
        ///
        /// Spread over frames on purpose. A folder can hold a hundred versions of a
        /// five-hundred-block machine, and reading and diffing all of them in one
        /// go is a visible freeze; done this way the window is usable immediately
        /// and the numbers arrive behind it. Only two snapshots are ever held at
        /// once, so the memory does not grow with the length of the history.
        /// </summary>
        private IEnumerator CountEverything()
        {
            _counting = true;

            List<VersionEntry> ordered = new List<VersionEntry>(_versions);
            RowSort.Apply(ordered, RowSort.ByTime, true);

            MachineSnapshot previous = null;
            for (int i = 0; i < ordered.Count; i++)
            {
                VersionEntry entry = ordered[i];
                Say("Reading history... " + (i + 1) + " of " + ordered.Count);
                yield return null;

                MachineSnapshot current = VersionScan.Read(entry.Path);
                if (current == null)
                {
                    previous = null;
                    continue;
                }

                entry.BlockCount = current.Count;
                if (previous != null)
                {
                    DiffResult diff = BlockDiff.Compare(previous, current);
                    entry.Added = diff.Added.Count;
                    entry.Changed = diff.Changed.Count;
                    entry.Removed = diff.Removed.Count;
                }
                entry.Counted = true;
                previous = current;

                for (int r = 0; r < _rows.Count; r++)
                {
                    if (_rows[r].Entry == entry)
                    {
                        BindCounts(_rows[r], entry);
                    }
                }
            }

            _counting = false;
            Say(ordered.Count + " versions");
        }

        private void Say(string message)
        {
            if (_status != null)
            {
                _status.text = message;
            }
        }

        // --------------------------------------------------------------- thumbnails

        /// <summary>
        /// Loads the thumbnails of the rows on screen and drops the rest.
        ///
        /// Besiege writes a 512x512 PNG per autosave, which is a megabyte once it
        /// is a texture, so a hundred-version folder is a hundred megabytes if they
        /// are all held at once. Only what is visible -- a dozen at the very most --
        /// is ever loaded.
        /// </summary>
        private void RefreshThumbnails()
        {
            if (_viewport == null || _content == null)
            {
                return;
            }

            float top = _content.anchoredPosition.y;
            float bottom = top + _viewport.rect.height;
            float margin = RowHeight;

            for (int i = 0; i < _rows.Count; i++)
            {
                HistoryRow row = _rows[i];
                if (row.Entry == null || row.Rect == null)
                {
                    continue;
                }
                float rowTop = -row.Rect.anchoredPosition.y;
                float rowBottom = rowTop + RowHeight;
                bool visible = rowBottom >= top - margin && rowTop <= bottom + margin;

                if (visible && !row.ThumbnailLoaded)
                {
                    Texture texture = VersionScan.LoadThumbnail(row.Entry.ThumbnailPath);
                    row.Thumbnail.texture = texture;
                    row.Thumbnail.color = texture == null ? ClearTint : Color.white;
                    row.ThumbnailLoaded = true;
                }
                else if (!visible && row.ThumbnailLoaded)
                {
                    ReleaseThumbnail(row);
                }
            }
        }

        private static void ReleaseThumbnail(HistoryRow row)
        {
            if (row.Thumbnail == null)
            {
                return;
            }
            Texture texture = row.Thumbnail.texture;
            row.Thumbnail.texture = null;
            // Transparent rather than white: a RawImage with no texture draws an
            // opaque white rectangle, which reads as a broken row.
            row.Thumbnail.color = ClearTint;
            row.ThumbnailLoaded = false;
            if (texture != null)
            {
                // Loaded by us, from a file, one fresh texture per call -- so this
                // is ours to destroy and nothing else is holding it.
                Destroy(texture);
            }
        }

        private void DropThumbnails()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                ReleaseThumbnail(_rows[i]);
            }
        }
    }
}
