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
        private const float WindowWidth = 720f;
        private const float WindowHeight = 560f;
        private const float RowHeight = 66f;
        private const float HeaderHeight = 34f;
        private const float StatusHeight = 26f;

        /// <summary>How far the status line sits above the window's bottom edge.</summary>
        private const float StatusMargin = 10f;
        private const int RowFontSize = 15;
        private const int HeaderFontSize = 13;

        // Column edges as fractions of the row's width, and the padding inside
        // one. One table, used by both the header and every row, so a heading and
        // the values under it cannot drift apart.
        private const float ThumbEnd = 0.105f;
        private const float ThumbInset = 5f;
        private const float StampEnd = 0.460f;
        private const float AddedEnd = 0.640f;
        private const float ChangedEnd = 0.820f;
        private const float PadLeft = 9f;
        private const float PadRight = 13f;

        // What turns a banded table into a stack of Besiege buttons: a margin
        // either side of a row and a gap between one row and the next.
        private const float RowMargin = 8f;
        private const float RowGap = 6f;
        private const float HeaderGap = 3f;

        private static readonly Color AddedInk = new Color(0.31f, 0.85f, 0.42f, 1f);
        private static readonly Color ChangedInk = new Color(1f, 0.66f, 0.16f, 1f);
        private static readonly Color RemovedInk = new Color(0.96f, 0.35f, 0.36f, 1f);
        private static readonly Color QuietInk = new Color(0.72f, 0.72f, 0.74f, 1f);
        private static readonly Color ClearTint = new Color(1f, 1f, 1f, 0f);

        /// <summary>
        /// The colour Besiege marks a chosen thing with -- the red on the block
        /// panel's selected option. Taken from UI Factory's copy of the game's
        /// palette rather than sampled off a screenshot, with the same value
        /// written out as a fallback in case that class ever moves.
        /// </summary>
        private static Color SelectedFill
        {
            get
            {
                try
                {
                    return Besiege.UI.Consts.C_BG_RED;
                }
                catch (Exception)
                {
                    return new Color(0.92f, 0.13f, 0.29f, 1f);
                }
            }
        }

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

        /// <summary>
        /// Whether the player has the window open -- which is not the same as
        /// whether it is on screen, since it steps aside while a menu is up.
        /// </summary>
        private bool _wanted;
        private bool _saidWaiting;

        public bool Visible
        {
            get { return _wanted; }
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
            SetVisible(!_wanted);
        }

        /// <summary>Opens or closes the window, as the player asked.</summary>
        public void SetVisible(bool visible)
        {
            _wanted = visible;
            Apply();
        }

        /// <summary>
        /// Shows the window when the player wants it and the game is not busy
        /// showing something of its own.
        ///
        /// This is how Besiege's own block mapper behaves: open a menu over the
        /// build area — escape, load, options — and the panel goes away rather than
        /// floating over the top of it, and comes back when the menu does. The
        /// overlay in the world goes with it, because a machine covered in red
        /// ghosts is not what you want to be looking at while picking a different
        /// machine to load.
        ///
        /// The player's own answer is kept separately in <c>_wanted</c>, so a menu
        /// opening and closing does not undo a window they had deliberately hidden.
        /// </summary>
        private void Apply()
        {
            bool showing = _wanted && !GameIsBusy();
            if (_window != null && _window.activeSelf != showing)
            {
                _window.SetActive(showing);
            }
            _ghosts.SetVisible(showing);

            if (_wanted && !showing && !_saidWaiting)
            {
                // Said once, because a window that is open and invisible is
                // otherwise indistinguishable from one that failed to build.
                _saidWaiting = true;
                Log.Info("the history window is waiting for the game's own menu to close.");
            }
        }

        /// <summary>
        /// True while the game has a menu up or the player has hidden the HUD.
        ///
        /// <c>StatMaster</c> is not part of the stable Modding namespace and can
        /// change without notice, so a failure here means "not busy" -- a window
        /// that fails to hide is a great deal better than one that never appears.
        /// </summary>
        private static bool GameIsBusy()
        {
            try
            {
                return StatMaster.inMenu || StatMaster.hudHidden;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void Update()
        {
            // Polled rather than driven off StatMaster.inMenuChanged: that is a
            // plain static Action, so subscribing to it means remembering to
            // unsubscribe, and getting that wrong leaves a destroyed window being
            // called into. Two static reads a frame is not worth the risk.
            if (_built)
            {
                Apply();
            }
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
            UIBuild.InsetBottom(_scroll.transform as RectTransform,
                                StatusHeight + StatusMargin);

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
        /// Placed by measuring the scrolling area rather than by anchoring to the
        /// window: what the rows are laid out across is the scroll view's viewport,
        /// which is inset inside the window by however much the prefab's frame and
        /// scrollbar take. A heading anchored to the window is that much wider than
        /// the rows beneath it, and every column drifts by a share of the
        /// difference — which is exactly what it looked like.
        /// </summary>
        private void BuildHeader()
        {
            RectTransform rect = UIBuild.CreateRect("Header", _window.transform);
            UIBuild.PlaceStrip(rect, _viewport, _window.transform as RectTransform,
                               HeaderHeight, true);
            // The rows sit inside their own margin, so their columns are fractions
            // of a narrower box than the viewport. The header has to lose the same
            // margin or every heading sits a few pixels left of its column.
            rect.sizeDelta = new Vector2(rect.sizeDelta.x - RowMargin * 2f, HeaderHeight);

            _headers[RowSort.ByTime] = HeaderButton(rect, RowSort.ByTime, ThumbEnd, StampEnd);
            _headers[RowSort.ByAdded] = HeaderButton(rect, RowSort.ByAdded, StampEnd, AddedEnd);
            _headers[RowSort.ByChanged] = HeaderButton(rect, RowSort.ByChanged, AddedEnd,
                                                       ChangedEnd);
            _headers[RowSort.ByRemoved] = HeaderButton(rect, RowSort.ByRemoved, ChangedEnd, 1f);
        }

        /// <summary>
        /// One clickable column heading, centred in its column above centred
        /// values.
        ///
        /// Centred rather than pushed to the column's edge, and not only because
        /// Besiege centres the labels on its own panel buttons. An edge-aligned
        /// label has to be inset from that edge by exactly what the values below it
        /// are inset by, and that inset cannot be applied reliably: UIFactory's
        /// button keeps its label somewhere down a hierarchy of its own, so
        /// stretching "the label" insets it inside whatever container it happens to
        /// sit in rather than inside the button. The heading came out twenty pixels
        /// off its column and no arithmetic here could say why. A centred label is
        /// indifferent to all of it -- it only needs its container to be centred in
        /// the button, which it is.
        /// </summary>
        private Text HeaderButton(RectTransform parent, int column, float from, float to)
        {
            GameObject button = UIF.Spawn(UIF.ButtonPrefab, parent);
            if (button == null)
            {
                return null;
            }
            // Gapped like the rows, so the headings read as the same family of
            // buttons rather than as one bar chopped into pieces.
            UIF.Span(UIF.Rect(button), from, to, HeaderGap, HeaderGap);

            Text label = UIF.Caption(button, RowSort.ColumnName(column), HeaderFontSize,
                                     TextAnchor.MiddleCenter);
            if (label != null)
            {
                label.color = QuietInk;
                UIF.StretchInset(label.rectTransform, 0f, 0f, 0f);
            }

            // Left pivoted in the middle, so the hover swell grows evenly either
            // side and a centred label stays where it is.
            UIF.PivotAnimation(button, 0.5f);

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
            // Anchored inside the window's own bottom edge rather than measured off
            // the scrolling area the way the header is. The viewport reaches the
            // bottom of the frame, so "just below the viewport" is just below the
            // window -- which is where this line ended up, floating on the scenery
            // under it.
            RectTransform rect = UIF.Rect(text);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(-(RowMargin + PadLeft) * 2f, StatusHeight);
            rect.anchoredPosition = new Vector2(0f, StatusMargin);
            // Note UIF.Label, not Caption-and-stretch: on the Text prefab the label
            // is the prefab, and stretching it would undo the placement above.
            _status = UIF.Label(text, 13, TextAnchor.MiddleCenter);
            if (_status != null)
            {
                _status.color = QuietInk;
                _status.text = string.Empty;
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

            // Inset and gapped rather than edge to edge, so the rows read as the
            // stack of separate buttons Besiege's own panels are made of instead of
            // as one banded table.
            row.Rect = UIF.Rect(row.Root);
            row.Rect.anchorMin = new Vector2(0f, 1f);
            row.Rect.anchorMax = new Vector2(1f, 1f);
            row.Rect.pivot = new Vector2(0.5f, 1f);
            row.Rect.sizeDelta = new Vector2(-RowMargin * 2f, RowHeight - RowGap);
            // Pivoted in the middle: everything in a row is centred in its column,
            // so a swell that grows evenly either side leaves it all where it is.
            UIF.PivotAnimation(row.Root, 0.5f);

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

            // Square, and sized in pixels rather than as a fraction of the row:
            // Besiege's autosave thumbnails are 512x512, and stretching one to fill
            // a column that is wider than the row is tall visibly squashes it.
            row.Thumbnail = UIBuild.AddRawImage(row.Rect, "Thumb");
            RectTransform thumb = row.Thumbnail.rectTransform;
            thumb.anchorMin = new Vector2(0f, 0f);
            thumb.anchorMax = new Vector2(0f, 1f);
            thumb.pivot = new Vector2(0f, 0.5f);
            thumb.offsetMin = new Vector2(PadLeft, ThumbInset);
            thumb.offsetMax = new Vector2(PadLeft + RowHeight - ThumbInset * 2f - 2f,
                                          -ThumbInset);
            row.Thumbnail.raycastTarget = false;

            // Every column below is inset exactly as its heading is, so the two
            // line up whatever the window is resized to.
            // Centred, under centred headings. See HeaderButton for why the columns
            // are aligned this way rather than to their edges.
            row.Stamp = Cell(row.Rect, "Stamp", ThumbEnd, StampEnd);
            row.Added = Cell(row.Rect, "Added", StampEnd, AddedEnd);
            row.Changed = Cell(row.Rect, "Changed", AddedEnd, ChangedEnd);
            row.Removed = Cell(row.Rect, "Removed", ChangedEnd, 1f);

            HistoryRow captured = row;
            UIF.OnClick(row.Root, delegate { Choose(captured); });
            return row;
        }

        /// <summary>
        /// One column's label, spawned from UI Factory's Text prefab rather than
        /// built out of a bare uGUI Text.
        ///
        /// The prefab brings Besiege's font and its letter spacing with it, which
        /// is most of what makes a panel look like the game's rather than like a
        /// mod's. Falls back to a plain label if UIFactory cannot supply one, so a
        /// row is never simply missing.
        /// </summary>
        private static Text Cell(RectTransform row, string name, float from, float to)
        {
            GameObject spawned = UIF.Spawn(UIF.TextPrefab, row);
            if (spawned == null)
            {
                Text plain = UIBuild.AddText(row, name, RowFontSize, TextAnchor.MiddleCenter);
                UIF.Span(plain.rectTransform, from, to, PadLeft, PadRight);
                return plain;
            }

            spawned.name = name;
            UIF.Span(UIF.Rect(spawned), from, to, PadLeft, PadRight);
            Text label = UIF.Label(spawned, RowFontSize, TextAnchor.MiddleCenter);
            if (label != null)
            {
                label.raycastTarget = false;
                label.text = string.Empty;
            }
            return label;
        }

        private void Bind(HistoryRow row, VersionEntry entry, int index)
        {
            if (row.Entry != entry)
            {
                ReleaseThumbnail(row);
            }
            row.Entry = entry;
            row.Rect.anchoredPosition = new Vector2(0f, -index * RowHeight);
            Repaint(row, entry, index == _selected);
        }

        /// <summary>
        /// Colours a row for what it is and whether it is the one being shown.
        ///
        /// A chosen row goes solid red with white text throughout, which is what
        /// Besiege does to the selected option on a block's panel. The count
        /// colours are given up on that one row deliberately: green on red is not
        /// worth reading, and the same three numbers are spelled out along the
        /// bottom of the window anyway.
        /// </summary>
        private static void Repaint(HistoryRow row, VersionEntry entry, bool chosen)
        {
            row.Highlight.color = chosen ? SelectedFill : ClearTint;
            row.Stamp.text = entry.Stamp() + (entry.Manual ? "    SAVED" : string.Empty);
            row.Stamp.color = chosen || entry.Manual ? Color.white : QuietInk;
            BindCounts(row, entry, chosen);
        }

        private void Restyle()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Entry != null)
                {
                    Repaint(_rows[i], _rows[i].Entry, i == _selected);
                }
            }
        }

        private static void BindCounts(HistoryRow row, VersionEntry entry, bool chosen)
        {
            Color quiet = chosen ? Color.white : QuietInk;
            if (entry.IsFirst)
            {
                // Nothing before it to be a change from. Its block count is the more
                // useful thing to show in the same space.
                row.Added.text = entry.Counted ? entry.BlockCount + " BLOCKS" : "";
                row.Added.color = quiet;
                row.Changed.text = string.Empty;
                row.Removed.text = string.Empty;
                return;
            }
            if (!entry.Counted)
            {
                row.Added.text = "·";
                row.Changed.text = "·";
                row.Removed.text = "·";
                row.Added.color = quiet;
                row.Changed.color = quiet;
                row.Removed.color = quiet;
                return;
            }

            // A zero is written as a dash in the quiet ink rather than in the
            // column's own colour, so "nothing removed" cannot be misread at a
            // glance as a count with a minus sign in front of it.
            Fill(row.Added, entry.Added, "+", chosen ? Color.white : AddedInk, quiet);
            Fill(row.Changed, entry.Changed, "~", chosen ? Color.white : ChangedInk, quiet);
            Fill(row.Removed, entry.Removed, "-", chosen ? Color.white : RemovedInk, quiet);
        }

        private static void Fill(Text cell, int count, string sign, Color ink, Color quiet)
        {
            cell.text = count == 0 ? "–" : sign + count;
            cell.color = count == 0 ? quiet : ink;
        }

        /// <summary>
        /// Marks every heading with both arrows, the one that is in force lit and
        /// the other dimmed.
        ///
        /// Showing a single arrow on the sorted column only says which column is
        /// sorted; it does not say what clicking the others would do, or that
        /// clicking this one again would reverse it. A pair on every column says
        /// all three at once.
        /// </summary>
        private void UpdateHeaderMarks()
        {
            for (int column = 0; column < _headers.Length; column++)
            {
                if (_headers[column] == null)
                {
                    continue;
                }
                bool sorted = column == _sortColumn;
                _headers[column].text = RowSort.ColumnName(column) + "  " +
                                        Arrow("▲", sorted && _ascending) +
                                        Arrow("▼", sorted && !_ascending);
                _headers[column].color = sorted ? Color.white : QuietInk;
            }
        }

        /// <summary>
        /// One arrow, lit or dimmed. The colour has to be markup rather than the
        /// label's own: both arrows live in the same label as the heading, and only
        /// one of them is ever in force.
        /// </summary>
        private static string Arrow(string glyph, bool lit)
        {
            return (lit ? "<color=#FFFFFFFF>" : "<color=#FFFFFF33>") + glyph + "</color>";
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
            Restyle();
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
                        BindCounts(_rows[r], entry, r == _selected);
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
