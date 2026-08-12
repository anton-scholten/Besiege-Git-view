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

        /// <summary>
        /// The version's number, which is the button that pins this version as the
        /// one to compare against. <see cref="Number"/> is its label.
        /// </summary>
        public Button Pin;

        /// <summary>
        /// The block of colour behind the number, transparent until the row is
        /// pinned. An image of ours rather than the button's own background -- see
        /// <see cref="HistoryView.BuildNumber"/>.
        /// </summary>
        public Image PinFill;

        /// <summary>
        /// The four bars that frame the row when it is the version on screen.
        /// </summary>
        public Image[] Edges;

        public Text Number;
        public RawImage Thumbnail;
        public Text Stamp;

        /// <summary>How many blocks the machine has at this version, all told.</summary>
        public Text Blocks;

        public Text Added;
        public Text Changed;
        public Text Removed;
        public Image Highlight;
        public Button Button;
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

        /// <summary>
        /// Wide enough for four count columns of the width the three had. The window
        /// grew by exactly one of them when the block count got a column of its own,
        /// so that nothing else had to give up room to it -- a timestamp column
        /// squeezed by a fifth would have started wrapping its own values.
        /// </summary>
        private const float WindowWidth = 850f;
        private const float WindowHeight = 560f;

        /// <summary>
        /// Where the window opens the first time, in canvas units from the middle
        /// of a 1920x1080 canvas: up in the top left, clear of the toolbar across
        /// the top and of the block palette along the bottom, leaving the machine
        /// the whole right-hand side of the screen to be looked at in.
        ///
        /// The margin is the same on both sides of the corner: the window's top
        /// edge sits at 389, which is 53 units under the toolbar, so its left edge
        /// sits 53 units in from the screen's, at -907. That puts the middle of an
        /// 850-wide window at -482. It used to be -547, which was the same
        /// arithmetic for a window 130 units narrower -- when the blocks column
        /// widened the window, the left margin was what paid for it, and the window
        /// ended up against the edge of the screen.
        /// </summary>
        private static readonly Vector2 WindowHome =
            new Vector2(-960f + 53f + WindowWidth * 0.5f, 109f);

        /// <summary>
        /// How far the window has to move before it is worth storing. Dragging it
        /// changes the position every frame, and a preference does not need to
        /// follow that; it needs to be right when the game closes.
        /// </summary>
        private const float MovedEnough = 0.5f;

        /// <summary>Seconds between attempts to put a lost overlay back.</summary>
        private const float RedrawInterval = 0.5f;
        private const float RowHeight = 66f;
        private const float HeaderHeight = 34f;
        private const float StatusHeight = 26f;

        /// <summary>How far the status line sits above the window's bottom edge.</summary>
        private const float StatusMargin = 10f;
        private const int RowFontSize = 15;
        private const int HeaderFontSize = 13;

        /// <summary>
        /// The counts are written at the size of the version number, which is the
        /// other thing in a row worth reading from across the screen. The timestamp
        /// stays smaller: it is the row's label rather than its answer, and at this
        /// size it would not fit its column anyway.
        /// </summary>
        private const int CountFontSize = NumberFontSize;

        // Column edges as fractions of the row's width, and the padding inside
        // one. One table, used by both the header and every row, so a heading and
        // the values under it cannot drift apart.
        //
        // The four count columns are one width: 130 units of the 814-unit row, which
        // is 0.16 each. That width is set by what has to be written above them, and
        // it was measured off a screenshot rather than guessed -- Besiege's font is
        // wide and letter-spaced, and nothing about it can be asked before the window
        // exists. "CHANGED ▲▼" is 76 units at the header size; the column gives up 23
        // of its own to the swatch and the gaps either side, so 130 leaves about 15
        // units of air on each side of the longest heading.
        //
        // The two on the left are in the same units: 109 for the number and the
        // picture, 185 for the name and the time, which is what a timestamp needs.
        private const float ThumbEnd = 0.134f;
        private const float ThumbInset = 5f;
        private const float StampEnd = 0.361f;
        private const float BlocksEnd = 0.521f;
        private const float AddedEnd = 0.680f;
        private const float ChangedEnd = 0.840f;
        private const float PadLeft = 9f;
        private const float PadRight = 13f;

        // The version's place in the history, left of its thumbnail, which is also
        // the button that pins it. In pixels rather than as a fraction of the row:
        // it holds two digits for most machines and three for a well-worn one, and
        // nothing else, so there is nothing for extra width to do.
        private const float NumberWidth = 44f;
        private const float NumberHeight = 42f;
        private const float NumberGap = 6f;
        private const int NumberFontSize = 20;

        // What turns a banded table into a stack of Besiege buttons: a margin
        // either side of a row and a gap between one row and the next.
        private const float RowMargin = 8f;
        private const float RowGap = 6f;
        private const float HeaderGap = 3f;

        /// <summary>
        /// The thumbnail's side. Square by construction rather than by a number
        /// that has to be kept in step with the row's height: Besiege writes them
        /// 512x512, and a rectangle of any other proportion visibly squashes one.
        /// </summary>
        private const float ThumbSize = RowHeight - RowGap - ThumbInset * 2f;

        /// <summary>
        /// Where the source column ends: the right edge of the thumbnail, which is
        /// the last thing in it. Its heading is measured against this, so the two
        /// cannot come apart.
        /// </summary>
        private const float SourceEnd = PadLeft + NumberWidth + NumberGap + ThumbSize;

        // The colour swatch that opens a column's picker, and the room it takes out
        // of that column's heading. Every unit here is a unit the heading does not
        // get, so it is as small as it can be and still be worth aiming at.
        private const float SwatchWidth = 18f;
        private const float SwatchGap = 2f;

        /// <summary>
        /// Where the time heading gives way to the name heading, as a fraction of
        /// the row. Near enough the middle of the column they share; the exact place
        /// is chosen so the two headings come out the same width once the swatch has
        /// taken its bite out of the left-hand one.
        /// </summary>
        private const float TimeEnd = 0.248f;

        // The frame that marks the row being shown: how thick its bars are and how
        // far inside the row's edge they sit. The inset matters for more than a
        // margin -- a row is a rounded rectangle, and a frame drawn hard against its
        // edge would have square corners sticking out past the rounded ones.
        private const float EdgeThickness = 2f;
        private const float EdgeInset = 3f;

        /// <summary>How far the pin's block of colour sits inside its button.</summary>
        private const float PinInset = 3f;

        private static readonly Color QuietInk = new Color(0.72f, 0.72f, 0.74f, 1f);
        private static readonly Color ClearTint = new Color(1f, 1f, 1f, 0f);

        // What a row does instead of swelling when the pointer is over it. See
        // UIF.NoSwell for why a row cannot swell.
        private static readonly Color HoverFill = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color PressFill = new Color(1f, 1f, 1f, 0.18f);

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
        private RectTransform _windowRect;

        /// <summary>Where the window was last known to be, so a drag can be noticed.</summary>
        private Vector2 _placed;

        /// <summary>Something worth storing has changed since the last flush.</summary>
        private bool _dirty;

        /// <summary>When the overlay may next be redrawn. See RedrawOverlay.</summary>
        private float _redrawAt;

        private ScrollRect _scroll;
        private RectTransform _content;
        private RectTransform _viewport;
        private Text _status;
        private readonly Text[] _headers = new Text[RowSort.ColumnCount];
        private readonly ColourPicker[] _pickers = new ColourPicker[DiffPalette.Categories];
        private readonly Image[] _swatches = new Image[DiffPalette.Categories];

        /// <summary>
        /// The button each swatch is drawn on, kept for the outside-click check: a
        /// click on one of these is the thing that opens and closes a picker, so it
        /// must not also count as a click somewhere else.
        /// </summary>
        private readonly RectTransform[] _swatchButtons =
            new RectTransform[DiffPalette.Categories];

        /// <summary>
        /// The version everything is compared against, or null for "the one
        /// before". The player sets it with a row's pin button.
        /// </summary>
        private VersionEntry _base;

        private int _sortColumn = RowSort.ByTime;
        private bool _ascending;
        private int _selected = -1;
        private bool _built;
        private bool _counting;

        /// <summary>
        /// Which counting pass is the current one. A pass reads a file a frame and
        /// takes about a second over a long history, which is plenty of time for the
        /// player to change what the counts are measured from; the older pass checks
        /// this and gives up rather than writing numbers nobody asked for any more.
        /// </summary>
        private int _countPass;

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
            // A pin belongs to the history it was set in; another machine's versions
            // are not something to compare this one against.
            _base = null;
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
            if (!visible)
            {
                ClosePickers();
                Store();
            }
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
        /// True while there is nothing for the window to be over: a menu is up, the
        /// HUD is hidden, or the player has left the build area altogether for the
        /// main menu, the level selector or a level that is still loading.
        ///
        /// The window and its canvas outlive scene loads -- they have to, or the
        /// history would be lost every time a level was opened -- so nothing stops
        /// them being drawn over the main menu unless it is checked for.
        /// <c>Machine.Active()</c> is the check that matters and the one that needs
        /// no list of scene names: it is the machine the window is describing, and
        /// outside a build area there is not one.
        ///
        /// <c>StatMaster</c> is not part of the stable Modding namespace and can
        /// change without notice, so a failure here means "not busy" -- a window
        /// that fails to hide is a great deal better than one that never appears.
        /// </summary>
        private static bool GameIsBusy()
        {
            if (Machine.Active() == null)
            {
                return true;
            }
            try
            {
                return StatMaster.inMenu || StatMaster.hudHidden
                    || StatMaster.isMainMenu || StatMaster.isLoadingLevels;
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
                NotePosition();
                RedrawOverlay();
                CloseOnOutsideClick();
            }
        }

        /// <summary>
        /// Puts an open picker away when the player clicks anywhere that is not it.
        ///
        /// Read off the mouse rather than caught by an invisible panel behind the
        /// picker, which is how this is usually done and cannot work here: the
        /// picker lives inside the window, so a catcher big enough to hear a click
        /// out in the world would have to be somewhere the picker could never be
        /// drawn on top of. A screen point is answerable wherever it lands.
        ///
        /// Clicks on a swatch are left alone, because a swatch is the thing that
        /// opens and closes a picker. Whether its own handler has run yet when this
        /// does depends on script execution order, and either way round the click
        /// would otherwise be counted twice -- closing the panel here and reopening
        /// it there, or the reverse.
        ///
        /// Left button only. The right one turns the camera, and a colour is often
        /// exactly the thing you want to turn the camera to look at.
        /// </summary>
        private void CloseOnOutsideClick()
        {
            if (!Input.GetMouseButtonDown(0))
            {
                return;
            }
            Vector2 point = Input.mousePosition;
            for (int i = 0; i < _pickers.Length; i++)
            {
                if (_pickers[i] != null && _pickers[i].Contains(point))
                {
                    return;
                }
            }
            for (int i = 0; i < _swatchButtons.Length; i++)
            {
                if (_swatchButtons[i] != null &&
                    RectTransformUtility.RectangleContainsScreenPoint(_swatchButtons[i],
                                                                      point, null))
                {
                    return;
                }
            }
            ClosePickers();
        }

        /// <summary>
        /// Puts the overlay back after a level change.
        ///
        /// The shells hang off the transform the machine's blocks are parented to,
        /// and loading a level destroys and rebuilds all of it -- so a diff being
        /// shown when the player opens another level is simply gone, while the
        /// window listing it is still there saying otherwise. Besiege carries the
        /// machine across, so the diff is still true of it; only the objects
        /// drawing it were lost.
        ///
        /// Retried on a timer rather than every frame because the first attempts
        /// after a level change will fail: the machine exists before its blocks do,
        /// and there is nothing to parent to until they arrive.
        /// </summary>
        private void RedrawOverlay()
        {
            if (!_ghosts.Lost || GameIsBusy())
            {
                return;
            }
            Machine machine = Machine.Active();
            if (machine == null || machine.IsLoadingMachine || Time.unscaledTime < _redrawAt)
            {
                return;
            }
            // Waited for rather than assumed: the machine object exists before its
            // blocks do, and the shells are parented to whatever the blocks are
            // parented to. Drawing early would hang them off the machine's own
            // transform, which is a different place.
            if (machine.BuildingBlocks == null || machine.BuildingBlocks.Count == 0)
            {
                return;
            }
            _redrawAt = Time.unscaledTime + RedrawInterval;
            _ghosts.Restore();
        }

        /// <summary>
        /// Notices the window being dragged.
        ///
        /// Polled rather than hooked, for the same reason as the menu check and one
        /// more: the drag is UI Factory's, on a component of its window prefab, and
        /// it reports nothing. Where the rect ended up is the only account of it
        /// there is.
        /// </summary>
        private void NotePosition()
        {
            if (_windowRect == null || !_window.activeSelf)
            {
                return;
            }
            Vector2 now = _windowRect.anchoredPosition;
            if ((now - _placed).sqrMagnitude < MovedEnough * MovedEnough)
            {
                return;
            }
            _placed = now;
            _dirty = true;
            Prefs.SetWindow(now);
        }

        /// <summary>
        /// Puts anything changed since the last call on disk. Called when the
        /// player is finished with something -- a picker closed, the window hidden,
        /// the game quit -- rather than as it changes, since a drag or a slider
        /// changes it many times a second and none of those are the answer.
        /// </summary>
        private void Store()
        {
            if (!_dirty)
            {
                return;
            }
            _dirty = false;
            Prefs.Flush();
        }

        private void OnApplicationQuit()
        {
            Store();
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
            Store();
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

            _windowRect = UIF.Rect(_window);
            // Anchored and pivoted in the middle by us rather than however the
            // prefab was authored, so that a stored position means one thing: an
            // offset in canvas units from the middle of the screen. Anything else
            // and a remembered position depends on a prefab we do not own.
            _windowRect.anchorMin = new Vector2(0.5f, 0.5f);
            _windowRect.anchorMax = new Vector2(0.5f, 0.5f);
            _windowRect.pivot = new Vector2(0.5f, 0.5f);
            _windowRect.sizeDelta = new Vector2(WindowWidth, WindowHeight);

            Canvas.ForceUpdateCanvases();
            _windowRect.anchoredPosition = Fit(Prefs.Window(WindowHome));
            _placed = _windowRect.anchoredPosition;

            HookCloseButton();
            if (!FindScrollView())
            {
                return;
            }
            BuildHeader();
            BuildStatusLine();
            _built = true;
        }

        /// <summary>
        /// Keeps a position on screen.
        ///
        /// Worth doing even for the default: the canvas matches on height, so a
        /// screen narrower than 16:9 has fewer canvas units across than the layout
        /// assumes. It matters more for a stored one -- the position is remembered
        /// across sessions, and nothing stops the player changing resolution or
        /// monitor between them, which would otherwise leave the window remembered
        /// somewhere off the edge with no way back to it.
        /// </summary>
        private Vector2 Fit(Vector2 at)
        {
            RectTransform canvas = _windowRect.parent as RectTransform;
            if (canvas == null || canvas.rect.width <= 0f)
            {
                return at;
            }
            float slackX = Mathf.Max(0f, (canvas.rect.width - WindowWidth) * 0.5f);
            float slackY = Mathf.Max(0f, (canvas.rect.height - WindowHeight) * 0.5f);
            return new Vector2(Mathf.Clamp(at.x, -slackX, slackX),
                               Mathf.Clamp(at.y, -slackY, slackY));
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

            // The one column of text, under two headings: what a row is called and
            // when it was saved are two different orders to want it in, and the row
            // writes both -- the name on the first line, the time on the second.
            _headers[RowSort.ByTime] = HeaderButton(rect, RowSort.ByTime, ThumbEnd,
                                                    TimeEnd);
            _headers[RowSort.ByName] = HeaderButton(rect, RowSort.ByName, TimeEnd,
                                                    StampEnd);
            _headers[RowSort.ByBlocks] = HeaderButton(rect, RowSort.ByBlocks, StampEnd,
                                                      BlocksEnd);
            _headers[RowSort.ByAdded] = HeaderButton(rect, RowSort.ByAdded, BlocksEnd,
                                                     AddedEnd);
            _headers[RowSort.ByChanged] = HeaderButton(rect, RowSort.ByChanged, AddedEnd,
                                                       ChangedEnd);
            _headers[RowSort.ByRemoved] = HeaderButton(rect, RowSort.ByRemoved, ChangedEnd, 1f);

            SourceHeading(rect);

            // Built after the headings so a swatch draws over the button it is
            // notched into rather than under it. The unchanged colour heads the
            // blocks column: it is the colour of a block that a save left alone, and
            // that column is how many blocks there are to leave alone.
            Swatch(rect, DiffPalette.Unchanged, StampEnd, HeaderGap);
            Swatch(rect, DiffPalette.Added, BlocksEnd, HeaderGap);
            Swatch(rect, DiffPalette.Changed, AddedEnd, HeaderGap);
            Swatch(rect, DiffPalette.Removed, ChangedEnd, HeaderGap);
        }

        /// <summary>
        /// The name of the leftmost column: the version's number, its picture, and
        /// the button that makes it the version everything is compared with.
        ///
        /// Across the whole column -- the numbers and the pictures beside them --
        /// rather than over the numbers alone, which is where it started and where
        /// it would not fit. "SOURCE ▲▼" is wider than a strip sized for two
        /// digits, and a heading centred on that strip has to hang off both ends of
        /// its own button to be centred at all; there is nothing to the left of it
        /// but the window's edge, so the room it needs can only come from the
        /// right. Taking the picture in too is what makes the button wide enough
        /// for the word to sit inside it with air around it, at every width the
        /// window can be dragged to, since both are measured in pixels.
        /// </summary>
        private void SourceHeading(RectTransform parent)
        {
            GameObject button = UIF.Spawn(UIF.ButtonPrefab, parent);
            if (button == null)
            {
                return;
            }

            // In pixels, like the number strip and the thumbnail it sits over,
            // rather than as a fraction of the row -- a fraction would drift off
            // them as the window's width changed and they did not.
            RectTransform rect = UIF.Rect(button);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(PadLeft, HeaderGap);
            rect.offsetMax = new Vector2(SourceEnd, -HeaderGap);

            _headers[RowSort.ByNumber] = Heading(button, RowSort.ByNumber);
        }

        /// <summary>
        /// The little block of colour at the left of a column, which opens that
        /// colour's picker.
        ///
        /// Beside the heading rather than in a settings panel of its own because
        /// the colour has no meaning away from the column: it is what "+4" is
        /// written in and what the blocks behind it are drawn in, and being able to
        /// see all four at once is most of what makes them choosable.
        ///
        /// The SAVED column carries the fourth, for the blocks a save left alone.
        /// It has no count to be the colour of -- there is no column of unchanged
        /// blocks and no use for one -- but it is the same kind of choice as the
        /// other three and belongs in the same row of them. Drawn at full opacity
        /// like the rest, so a colour turned off is still something to click.
        /// </summary>
        private void Swatch(RectTransform parent, int category, float columnStart,
                            float left)
        {
            GameObject button = UIF.Spawn(UIF.ButtonPrefab, parent);
            if (button == null)
            {
                return;
            }
            button.name = "Swatch " + DiffPalette.Name(category);

            RectTransform rect = UIF.Rect(button);
            // As tall as the headings beside it. It used to be inset by two units
            // more, which left the four of them looking like a different row of
            // controls from the buttons they belong to.
            rect.anchorMin = new Vector2(columnStart, 0f);
            rect.anchorMax = new Vector2(columnStart, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(left, HeaderGap);
            rect.offsetMax = new Vector2(left + SwatchWidth, -HeaderGap);

            Text spare = button.GetComponentInChildren<Text>(true);
            if (spare != null)
            {
                spare.text = string.Empty;
            }

            Image fill = UIBuild.AddImage(rect, "Fill", DiffPalette.Ink(category));
            UIF.Stretch(fill.rectTransform, 4f, 4f);
            fill.raycastTarget = false;
            _swatches[category] = fill;

            UIF.NoSwell(button);
            _swatchButtons[category] = rect;
            int captured = category;
            UIF.OnClick(button, delegate { TogglePicker(captured, rect); });
        }

        /// <summary>
        /// Opens one picker and shuts the others. Two open at once would overlap --
        /// they hang below headings a hundred pixels apart and are wider than that
        /// -- and there is nothing to compare between them anyway.
        /// </summary>
        private void TogglePicker(int category, RectTransform under)
        {
            bool wasOpen = _pickers[category] != null && _pickers[category].Visible;
            ClosePickers();
            if (wasOpen)
            {
                return;
            }

            if (_pickers[category] == null)
            {
                int captured = category;
                _pickers[category] = new ColourPicker(captured, delegate { Recolour(captured); });
            }
            _pickers[category].Open(_window.transform, under,
                                    _window.transform as RectTransform);
        }

        /// <summary>
        /// Shuts every picker and writes down anything they changed. Putting one
        /// away is the moment a colour is settled on, which is the moment worth
        /// reaching the disk for.
        /// </summary>
        private void ClosePickers()
        {
            bool any = false;
            for (int i = 0; i < _pickers.Length; i++)
            {
                if (_pickers[i] != null && _pickers[i].Visible)
                {
                    _pickers[i].Close();
                    any = true;
                }
            }
            if (any)
            {
                Store();
            }
        }

        /// <summary>
        /// Takes a colour the player has just changed through to everything drawn
        /// in it: the counts in the list, the swatch on the heading, and the shells
        /// over the machine.
        /// </summary>
        private void Recolour(int category)
        {
            if (_swatches[category] != null)
            {
                _swatches[category].color = DiffPalette.Ink(category);
            }
            Restyle();
            _ghosts.Refresh();
            // DiffPalette has already stored the colour; this is what says there is
            // something to flush when the picker is put away.
            _dirty = true;
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
            // buttons rather than as one bar chopped into pieces. Every column
            // gives up the width of a swatch on its left, and the saved column one
            // more for the clock; the values under them stay where they are, since
            // those belong to the column rather than to the heading.
            UIF.Span(UIF.Rect(button), from, to, Inset(column), HeaderGap);
            return Heading(button, column);
        }

        /// <summary>The parts of a heading that are the same wherever it is placed.</summary>
        private Text Heading(GameObject button, int column)
        {
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

        /// <summary>
        /// How far a heading is pushed off the left edge of its column by the
        /// controls that live there.
        /// </summary>
        private static float Inset(int column)
        {
            // The four columns of numbers each begin with the swatch that colours
            // them. The time and the name share a column between them and have no
            // colour of their own, so their headings start at the edge.
            bool swatched = column == RowSort.ByBlocks || column == RowSort.ByAdded ||
                            column == RowSort.ByChanged || column == RowSort.ByRemoved;
            return swatched ? HeaderGap + SwatchWidth + SwatchGap : HeaderGap;
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
            // A row is far too wide to swell under the pointer without throwing its
            // own text about; it lights up instead. Repaint wires that up, because
            // what "lit" means depends on whether the row is the chosen one.
            UIF.NoSwell(row.Root);

            // The prefab's own label is in the way of the columns, but destroying it
            // is not safe -- other UIFactory behaviours write to it -- so it is
            // emptied and pushed out of the way instead.
            Text ownLabel = row.Root.GetComponentInChildren<Text>(true);
            if (ownLabel != null)
            {
                ownLabel.text = string.Empty;
            }

            // Opaque white, and left that way. uGUI's colour transition multiplies
            // the state's colour by the graphic's own, so an image created
            // transparent stays invisible whatever it is told to become -- which is
            // exactly how the hover veil lost its first attempt at a colour.
            row.Highlight = UIBuild.AddImage(row.Rect, "Selected", Color.white);
            UIF.Stretch(row.Highlight.rectTransform, 0f, 0f);
            row.Highlight.raycastTarget = false;
            row.Highlight.transform.SetAsFirstSibling();

            // Above the highlight and below everything with words on it.
            BuildEdges(row);
            BuildNumber(row);

            // Sized in pixels rather than as a fraction of the row, so that it stays
            // square whatever the window is doing.
            row.Thumbnail = UIBuild.AddRawImage(row.Rect, "Thumb");
            RectTransform thumb = row.Thumbnail.rectTransform;
            float thumbLeft = PadLeft + NumberWidth + NumberGap;
            thumb.anchorMin = new Vector2(0f, 0f);
            thumb.anchorMax = new Vector2(0f, 1f);
            thumb.pivot = new Vector2(0f, 0.5f);
            thumb.offsetMin = new Vector2(thumbLeft, ThumbInset);
            thumb.offsetMax = new Vector2(thumbLeft + ThumbSize, -ThumbInset);
            row.Thumbnail.raycastTarget = false;

            // The counts are centred under centred headings -- see HeaderButton for
            // why the headings are centred rather than aligned to their column's
            // edge. The timestamps are the exception: they are all the same length
            // and read as a list rather than as a column of figures, so they line
            // up on the left, next to the thumbnails. Their heading stays centred
            // over the column like the rest.
            row.Stamp = Cell(row.Rect, "Stamp", ThumbEnd, StampEnd, TextAnchor.MiddleLeft,
                             RowFontSize);
            row.Blocks = Cell(row.Rect, "Blocks", StampEnd, BlocksEnd,
                              TextAnchor.MiddleCenter, CountFontSize);
            row.Added = Cell(row.Rect, "Added", BlocksEnd, AddedEnd,
                             TextAnchor.MiddleCenter, CountFontSize);
            row.Changed = Cell(row.Rect, "Changed", AddedEnd, ChangedEnd,
                               TextAnchor.MiddleCenter, CountFontSize);
            row.Removed = Cell(row.Rect, "Removed", ChangedEnd, 1f,
                               TextAnchor.MiddleCenter, CountFontSize);

            HistoryRow captured = row;
            row.Button = UIF.OnClick(row.Root, delegate { Choose(captured); });

            // Wired once, because it no longer depends on anything: the highlight is
            // the pointer's veil and nothing else, whether this row is the chosen
            // one or not. Before anything can see the white it was created with.
            UIF.HoverTint(row.Button, row.Highlight, ClearTint, HoverFill, PressFill);
            return row;
        }

        /// <summary>
        /// The version's number, which is also the button that pins it as the one
        /// every other version is compared against.
        ///
        /// The number and the pin were two things side by side and are now one,
        /// which is what they were always saying: a version is picked out of the
        /// history by its number, and pinning it is picking it out. It also gives
        /// the button something written on it, which a blank square never had.
        ///
        /// A button of UI Factory's rather than one drawn here, so it hovers,
        /// presses and rounds its corners like every other button in the game. A
        /// button inside a button works out on its own: uGUI hands a click to the
        /// innermost thing that handles it, so pinning a row does not also load it.
        /// </summary>
        private void BuildNumber(HistoryRow row)
        {
            GameObject number = UIF.Spawn(UIF.ButtonPrefab, row.Rect);
            if (number == null)
            {
                // Without the prefab the number still has to be readable; it just
                // cannot be pinned.
                row.Number = Pixels(row.Rect, "Number", PadLeft, PadLeft + NumberWidth,
                                    NumberFontSize, TextAnchor.MiddleCenter);
                return;
            }
            number.name = "Number";

            RectTransform rect = UIF.Rect(number);
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(NumberWidth, NumberHeight);
            rect.anchoredPosition = new Vector2(PadLeft, 0f);

            // Marked with an image of ours inside the button rather than by tinting
            // the button's own background, which was the first attempt and did
            // nothing visible at all. UIFactory's graphics can carry a
            // CustomMaterialHandler -- "forces the image to use a custom shader
            // material instead of the default one" -- and a shader that does not
            // multiply by the renderer's colour cannot be tinted, the same way a
            // slot button in the load screen cannot be repainted by setting its
            // texture. A plain uGUI Image on the default UI shader takes a colour,
            // which is what the column swatches have been doing all along.
            row.PinFill = UIBuild.AddImage(rect, "Fill", ClearTint);
            UIF.Stretch(row.PinFill.rectTransform, PinInset, PinInset);
            row.PinFill.raycastTarget = false;
            // In front of the button's own face, which is the root's graphic and so
            // always draws first, and behind the number, which is a later child.
            row.PinFill.transform.SetAsFirstSibling();

            // Borrowed if there is one: the button's rounded corners live in its
            // sprite, so the mark drawn with the same sprite is the same shape.
            // Without one it is a square inside a rounded button, which is what the
            // column swatches are and looks deliberate enough.
            Image face = number.GetComponent<Image>();
            if (face != null && face.sprite != null)
            {
                row.PinFill.sprite = face.sprite;
                row.PinFill.type = face.type;
            }

            row.Number = UIF.Caption(number, string.Empty, NumberFontSize,
                                     TextAnchor.MiddleCenter);
            if (row.Number != null)
            {
                // The prefab's label is authored for the prefab's own width, so it
                // has to be stretched to the size this was resized to.
                UIF.StretchInset(row.Number.rectTransform, 0f, 0f, 0f);
                row.Number.raycastTarget = false;
            }

            HistoryRow captured = row;
            row.Pin = UIF.OnClick(number, delegate { TogglePin(captured); });
        }

        /// <summary>
        /// The four bars that frame the row being shown.
        ///
        /// A frame rather than a filled row. Filling it red is what Besiege does to
        /// a chosen option on its own panels, and it works there because those are
        /// short buttons with one word on them. A row here is a thumbnail, a
        /// timestamp and three counts written in three colours of their own, and a
        /// strong red behind all of that leaves the one row that matters most the
        /// hardest one to read. An outline says the same thing and changes nothing
        /// inside it.
        ///
        /// Inset from the row's edge rather than drawn along it, because a row is a
        /// rounded rectangle: a frame flush with its edge would have square corners
        /// standing outside the rounded ones. Three units in, against a corner
        /// radius of about five, puts them back inside the shape.
        /// </summary>
        private static void BuildEdges(HistoryRow row)
        {
            float near = EdgeInset;
            float far = EdgeInset + EdgeThickness;
            row.Edges = new Image[4];
            row.Edges[0] = Edge(row.Rect, "EdgeTop",
                                new Vector2(0f, 1f), new Vector2(1f, 1f),
                                new Vector2(near, -far), new Vector2(-near, -near));
            row.Edges[1] = Edge(row.Rect, "EdgeBottom",
                                new Vector2(0f, 0f), new Vector2(1f, 0f),
                                new Vector2(near, near), new Vector2(-near, far));
            row.Edges[2] = Edge(row.Rect, "EdgeLeft",
                                new Vector2(0f, 0f), new Vector2(0f, 1f),
                                new Vector2(near, near), new Vector2(far, -near));
            row.Edges[3] = Edge(row.Rect, "EdgeRight",
                                new Vector2(1f, 0f), new Vector2(1f, 1f),
                                new Vector2(-far, near), new Vector2(-near, -near));
        }

        private static Image Edge(RectTransform row, string name, Vector2 anchorMin,
                                  Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            Image bar = UIBuild.AddImage(row, name, ClearTint);
            RectTransform rect = bar.rectTransform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            bar.raycastTarget = false;
            return bar;
        }

        /// <summary>
        /// Pins a version as the one to compare against, or unpins it.
        ///
        /// One or none, because it is one comparison: a diff has two sides, and the
        /// far side is either a version the player chose or the one before in time.
        /// Pinning a second version replaces the first rather than adding to it.
        /// </summary>
        private void TogglePin(HistoryRow row)
        {
            if (row == null || row.Entry == null)
            {
                return;
            }
            _base = _base == row.Entry ? null : row.Entry;
            Restyle();
            // The diff on screen was measured against something that is no longer
            // what this is compared with, so it is worked out again -- without
            // reloading the machine, which has not changed.
            if (_selected >= 0 && _selected < _versions.Count)
            {
                ShowDiff(_versions[_selected]);
            }
            // And so is every count in the list, which answers the same question a
            // row at a time.
            Recount();
        }

        /// <summary>
        /// Marks a pin, or puts it back the way the prefab had it.
        ///
        /// Red because that is what Besiege marks a chosen thing with, and the same
        /// red the chosen row is filled with -- one row is the version being looked
        /// at, one pin is what it is being looked at against.
        /// </summary>
        private static void MarkPin(HistoryRow row, bool pinned)
        {
            if (row.PinFill != null)
            {
                row.PinFill.color = pinned ? SelectedFill : ClearTint;
            }
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
        /// <summary>
        /// A label pinned to the left of a row in pixels rather than across a
        /// fraction of it. The number strip and the thumbnail beside it are fixed
        /// sizes -- a two-digit number does not want more room on a wider window,
        /// and a square thumbnail cannot have any.
        /// </summary>
        private static Text Pixels(RectTransform row, string name, float from, float to,
                                   int fontSize, TextAnchor alignment)
        {
            GameObject spawned = UIF.Spawn(UIF.TextPrefab, row);
            Text label = spawned == null
                ? UIBuild.AddText(row, name, fontSize, alignment)
                : UIF.Label(spawned, fontSize, alignment);
            if (label == null)
            {
                return null;
            }
            if (spawned != null)
            {
                spawned.name = name;
            }

            RectTransform rect = label.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(from, 0f);
            rect.offsetMax = new Vector2(to, 0f);
            label.raycastTarget = false;
            label.text = string.Empty;
            return label;
        }

        private static Text Cell(RectTransform row, string name, float from, float to,
                                 TextAnchor alignment, int fontSize)
        {
            GameObject spawned = UIF.Spawn(UIF.TextPrefab, row);
            if (spawned == null)
            {
                Text plain = UIBuild.AddText(row, name, fontSize, alignment);
                UIF.Span(plain.rectTransform, from, to, PadLeft, PadRight);
                return plain;
            }

            spawned.name = name;
            UIF.Span(UIF.Rect(spawned), from, to, PadLeft, PadRight);
            Text label = UIF.Label(spawned, fontSize, alignment);
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
            Repaint(row, entry, index == _selected, entry == _base);
        }

        /// <summary>
        /// Colours a row for what it is and whether it is the one being shown.
        ///
        /// Only the frame around it says which row is chosen. The text is the same
        /// on every row -- the counts in their own colours, the timestamp quiet --
        /// so that a column means one thing all the way down and the chosen row is
        /// not also the one row whose numbers are written differently from the rest.
        ///
        /// A manual save is marked by the word SAVED after its timestamp and by
        /// nothing else. It used to be written in white as well, which made it look
        /// selected.
        /// </summary>
        private void Repaint(HistoryRow row, VersionEntry entry, bool chosen,
                             bool pinned)
        {
            Tint(row, chosen);
            MarkPin(row, pinned);
            if (row.Number != null)
            {
                row.Number.text = entry.Number > 0 ? entry.Number.ToString() : string.Empty;
                // White on the red of a pinned button, where the quiet grey the rest
                // of the column is written in would be a grey number on red.
                row.Number.color = pinned ? Color.white : QuietInk;
            }
            row.Stamp.text = entry.Lines();
            row.Stamp.color = QuietInk;
            BindCounts(row, entry);
        }

        /// <summary>
        /// Frames the row that is the version on screen, and unframes the rest.
        ///
        /// The hover veil is not touched here: it is the button's own colour
        /// transition, wired once when the row was built, so being under the
        /// pointer and being the chosen row are two marks that cannot get in each
        /// other's way.
        /// </summary>
        private static void Tint(HistoryRow row, bool chosen)
        {
            if (row.Edges == null)
            {
                return;
            }
            Color frame = chosen ? SelectedFill : ClearTint;
            for (int i = 0; i < row.Edges.Length; i++)
            {
                if (row.Edges[i] != null)
                {
                    row.Edges[i].color = frame;
                }
            }
        }

        private void Restyle()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Entry != null)
                {
                    Repaint(_rows[i], _rows[i].Entry, i == _selected,
                            _rows[i].Entry == _base);
                }
            }
        }

        private void BindCounts(HistoryRow row, VersionEntry entry)
        {
            // How big the machine is, which is a fact about the version rather than
            // about any comparison -- so it is filled in the same way whatever the
            // rest of the row says, including on the row that has nothing to be
            // compared against.
            if (row.Blocks != null)
            {
                row.Blocks.text = entry.Counted ? entry.BlockCount.ToString() : "·";
                row.Blocks.color = entry.Counted
                    ? DiffPalette.Ink(DiffPalette.Unchanged) : QuietInk;
            }

            // The oldest version is only a special case while the counts are what
            // each save did. Against a pinned source it is a comparison like any
            // other, and so is the pinned version itself -- which comes out as three
            // dashes, being no different from itself.
            if (entry.IsFirst && _base == null)
            {
                // Nothing before it to be a change from, so nothing is written --
                // not even a dash, which would read as "nothing changed".
                row.Added.text = string.Empty;
                row.Changed.text = string.Empty;
                row.Removed.text = string.Empty;
                return;
            }
            if (!entry.Counted)
            {
                row.Added.text = "·";
                row.Changed.text = "·";
                row.Removed.text = "·";
                row.Added.color = QuietInk;
                row.Changed.color = QuietInk;
                row.Removed.color = QuietInk;
                return;
            }

            // A zero is written as a dash in the quiet ink rather than in the
            // column's own colour, so "nothing removed" cannot be misread at a
            // glance as a count with a minus sign in front of it.
            Fill(row.Added, entry.Added, "+", DiffPalette.Added);
            Fill(row.Changed, entry.Changed, "~", DiffPalette.Changed);
            Fill(row.Removed, entry.Removed, "-", DiffPalette.Removed);
        }

        private static void Fill(Text cell, int count, string sign, int category)
        {
            cell.text = count == 0 ? "–" : sign + count;
            cell.color = count == 0 ? QuietInk : DiffPalette.Ink(category);
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
            Say("Loading " + entry.Title() + "...");

            if (!VersionScan.LoadIntoWorld(entry.Path))
            {
                Say("Could not load " + entry.Title() + ".");
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

            ShowDiff(entry);
        }

        /// <summary>
        /// Works out what one version changed and draws it, without touching the
        /// machine in the build area.
        ///
        /// Separate from loading because the two sides of a diff can change without
        /// the machine changing: pinning a version re-answers the question about the
        /// version already on screen, and reloading it to say so would throw the
        /// build area away and put it back identical.
        /// </summary>
        private void ShowDiff(VersionEntry entry)
        {
            VersionEntry against = Baseline(entry);
            if (against == null)
            {
                _ghosts.Clear();
                Say(entry.Title() + " -- the oldest version, nothing to compare against.");
                return;
            }

            MachineSnapshot before = VersionScan.Read(against.Path);
            MachineSnapshot after = VersionScan.Read(entry.Path);
            if (before == null || after == null)
            {
                Say("Could not read one of the versions to compare.");
                return;
            }

            // Nothing here cares that these two versions are next to each other or
            // even which way round they are in time: it is two machines, and every
            // block in the newer one that the older one does not have is an
            // addition. A pinned base is that same comparison with one side held
            // still.
            DiffResult diff = BlockDiff.Compare(before, after);
            _ghosts.Show(diff);
            Say(Describe(diff) + "   vs " + against.Title() +
                (against == _base ? "  (pinned)" : string.Empty));
        }

        /// <summary>
        /// What a version is compared against: whatever the player pinned, or the
        /// version before it in time. A version pinned and then clicked is compared
        /// with itself, which is a real answer -- no change -- and needs no special
        /// case.
        /// </summary>
        private VersionEntry Baseline(VersionEntry entry)
        {
            return _base != null ? _base : Predecessor(entry);
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
            int pass = ++_countPass;

            List<VersionEntry> ordered = new List<VersionEntry>(_versions);
            RowSort.Apply(ordered, RowSort.ByTime, true);

            // Held for the whole pass when a version is pinned: every row is then a
            // comparison with the same machine, so it is read once instead of once
            // per row. Null when nothing is pinned, and then `previous` walks the
            // history a save at a time as it always did.
            VersionEntry source = _base;
            MachineSnapshot fixedBase = source == null ? null : VersionScan.Read(source.Path);
            MachineSnapshot previous = null;

            for (int i = 0; i < ordered.Count; i++)
            {
                VersionEntry entry = ordered[i];
                Say("Reading history... " + (i + 1) + " of " + ordered.Count);
                yield return null;
                // Checked after the wait rather than before it: a pin changed while
                // this was asleep makes every count it is about to write wrong, and
                // a newer pass is already on its way to writing the right ones.
                if (pass != _countPass)
                {
                    yield break;
                }

                MachineSnapshot current = VersionScan.Read(entry.Path);
                if (current == null)
                {
                    previous = null;
                    continue;
                }

                entry.BlockCount = current.Count;
                MachineSnapshot against = fixedBase != null ? fixedBase : previous;
                if (against != null)
                {
                    DiffResult diff = BlockDiff.Compare(against, current);
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
            Say(ordered.Count + " versions" +
                (source == null ? string.Empty : "   counted against " + source.Title()));
        }

        /// <summary>
        /// Counts the whole list again, because what the counts are measured from
        /// has changed. Rows go back to showing nothing until their new numbers
        /// arrive, which is honest -- the old ones were answers to a different
        /// question -- and takes about a second for a hundred versions.
        /// </summary>
        private void Recount()
        {
            for (int i = 0; i < _versions.Count; i++)
            {
                _versions[i].Counted = false;
                _versions[i].Added = 0;
                _versions[i].Changed = 0;
                _versions[i].Removed = 0;
            }
            RebuildRows();
            StartCoroutine(CountEverything());
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
