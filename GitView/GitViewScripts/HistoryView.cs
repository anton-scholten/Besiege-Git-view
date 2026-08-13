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
        /// Wide enough for what is written in it and no wider.
        ///
        /// It has been narrowed twice over: once when the colour swatches left the
        /// headings, which gave every count column back the 23 units it had notched
        /// out of itself, and once by measuring what is actually written in a column
        /// rather than leaving room for what might be. See the column table below.
        /// </summary>
        private const float WindowWidth = 679f;
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
        // Across a 643-unit row:
        //
        //   132  the pin, the number and the picture, which are fixed sizes in pixels
        //   155  the name and the time: 110 for a written-out timestamp, and enough
        //        for the two headings that share the column to sit side by side with
        //        air around each of them rather than butted together
        //    89  each of the four counts
        //
        // The count columns are set by what is written *above* them rather than in
        // them. Measured off a screenshot rather than guessed, since Besiege's font
        // is wide and letter-spaced and nothing about it can be asked before the
        // window exists: "CHANGED" is 49 units at the header size and its pair of
        // arrows another 24, so 89 leaves about 5 units of air on each side of the
        // longest heading -- enough for the swell under the pointer and not a unit
        // more.
        private const float ThumbEnd = 0.205f;
        private const float ThumbInset = 5f;
        private const float StampEnd = 0.446f;
        private const float BlocksEnd = 0.585f;
        private const float AddedEnd = 0.723f;
        private const float ChangedEnd = 0.862f;
        private const float PadLeft = 9f;
        private const float PadRight = 13f;

        // The version's place in the history, left of its thumbnail. In pixels
        // rather than as a fraction of the row: it holds two digits for most
        // machines and three for a well-worn one, and nothing else, so there is
        // nothing for extra width to do.
        //
        // Written on the row rather than on a button, because it is a fact about the
        // version and not a thing to press. What is pressed is the circle to the
        // left of it, which is the whole of what pinning looks like now: a small
        // empty ring that fills with red when this is the version everything is
        // being compared against. A number on a raised plate said "click me" about
        // something that is really the row's name, and the plate under it was doing
        // the work of a mark that is now its own control.
        private const float NumberWidth = 36f;
        private const float NumberGap = 6f;
        private const int NumberFontSize = 20;

        /// <summary>
        /// The pin circle: how far in from the row's edge it sits, how big it is, and
        /// the gap between it and the number.
        ///
        /// Further in than the row's own padding, because the number beside it is
        /// centred in a box wide enough for three digits: a two-digit number has six
        /// units of air on its left and a one-digit twelve, and the circle was that
        /// much further from the number it belongs to than it looked in the numbers.
        /// </summary>
        private const float PinLeft = 16f;
        private const float PinSize = 22f;
        private const float PinGap = 2f;

        /// <summary>How thick the empty ring is drawn, as a share of the circle.</summary>
        private const float PinRing = 0.07f;

        /// <summary>
        /// How much of its button the circle fills at rest, and under the pointer.
        ///
        /// The growing is the button's hover, done by swapping the picture for a
        /// bigger one rather than by scaling anything: UI Factory's own swell is a
        /// few per cent, which on a control this small is half a unit and reads as
        /// nothing at all.
        /// </summary>
        private const float PinRest = 0.80f;
        private const float PinOver = 1f;

        /// <summary>Where the number starts: past the pin.</summary>
        private const float NumberLeft = PinLeft + PinSize + PinGap;

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
        private const float SourceEnd = NumberLeft + NumberWidth + NumberGap + ThumbSize;

        // The colour swatch that opens a column's picker, and the room it takes out
        // of that column's heading. Every unit here is a unit the heading does not
        // get, so it is as small as it can be and still be worth aiming at.
        private const float SwatchWidth = 18f;
        private const float SwatchGap = 2f;

        /// <summary>
        /// Where the name heading gives way to the time heading, halfway across the
        /// column the two of them share.
        ///
        /// The name is on the left because that is where the name is: a row writes
        /// it on the first line and the time under it, both flush with the column's
        /// left edge, so the heading nearest the start of the text should be the one
        /// naming the text that starts there.
        /// </summary>
        private const float NameEnd = 0.326f;

        // The frame that marks the row being shown: how thick its bars are and how
        // far inside the row's edge they sit. The inset matters for more than a
        // margin -- a row is a rounded rectangle, and a frame drawn hard against its
        // edge would have square corners sticking out past the rounded ones.
        private const float EdgeThickness = 2f;
        private const float EdgeInset = 3f;

        /// <summary>
        /// What a canvas takes one unit to be, which is the number a repeating sprite
        /// has to be created at to repeat at the size it was drawn. Unity's default
        /// for <c>Canvas.referencePixelsPerUnit</c>, and ours is left at it.
        /// </summary>
        private const float CanvasPixelsPerUnit = 100f;


        private static readonly Color QuietInk = new Color(0.72f, 0.72f, 0.74f, 1f);
        private static readonly Color ClearTint = new Color(1f, 1f, 1f, 0f);

        /// <summary>
        /// The disc behind the pin circle, in place of the rounded square the button
        /// prefab draws. Sampled off the plate it replaces, so that the one round
        /// control in the list is the same shade as everything else on a row.
        /// </summary>
        private static readonly Color PlateFill =
            new Color(0.078f, 0.110f, 0.165f, 1f);

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

        /// <summary>The options window, built the first time the cog is pressed.</summary>
        private OptionsView _options;

        private static Sprite _gearFace;

        /// <summary>How big the cog is drawn before it is scaled to the button.</summary>
        private const int GearPixels = 64;

        /// <summary>
        /// The version everything is compared against, or null for "the one
        /// before". The player sets it with a row's pin button.
        /// </summary>
        private VersionEntry _base;

        /// <summary>
        /// True when the list is machines the player picked out, false when it is
        /// the versions of one machine. See <see cref="Open"/>.
        /// </summary>
        private bool _chosen;

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
        ///
        /// <paramref name="chosen"/> says which kind of list this is: machines the
        /// player picked out one at a time, or the versions of one machine. Only the
        /// first column's name turns on it -- what the number in it *means* is the
        /// one thing that differs between the two, and it is worth saying which.
        /// </summary>
        public void Open(string machineName, List<VersionEntry> versions, bool chosen)
        {
            Prepare(machineName, versions, chosen);
            UIF.WhenReady(delegate { BuildAndFill(); });
        }

        /// <summary>
        /// Takes the list, without deciding anything about what to show from it.
        /// Separate from <see cref="Open"/> so that a caller can pin and choose
        /// *before* the window is built: the counting pass reads the pin when it
        /// starts, so setting one afterwards would leave every count answering the
        /// question that was asked a moment earlier.
        /// </summary>
        private void Prepare(string machineName, List<VersionEntry> versions,
                             bool chosen)
        {
            _machineName = machineName;
            _versions = versions ?? new List<VersionEntry>();
            _chosen = chosen;
            _selected = -1;
            // A pin belongs to the history it was set in; another machine's versions
            // are not something to compare this one against.
            _base = null;
            _ghosts.Clear();

            // A list of machines you picked out opens in the order you picked them,
            // newest choice at the top, so the first one you chose is at the bottom.
            // That is the way the arrow then runs -- up from the source to the
            // machine being looked at -- and it is the order the marks in the load
            // screen were in. Sorting a hand-picked list by time was the old default
            // and means nothing: most of those machines have no time, and the ones
            // that do were saved in an order that has nothing to do with why they
            // were put together.
            if (_chosen)
            {
                _sortColumn = RowSort.ByNumber;
                _ascending = false;
            }
            RowSort.Apply(_versions, _sortColumn, _ascending);
        }

        /// <summary>
        /// Shows a list and opens it on its own first row, which is what the compare
        /// buttons in the load screen do.
        ///
        /// The top row against the one under it, and nothing pinned. That is the
        /// list's own default reading -- every row is the difference between it and
        /// the row below -- so the window opens showing the same thing the column of
        /// counts beside it is showing, and the arrow joins two rows that are next to
        /// each other. It opened pinned to the far end of the list before, which
        /// answered a bigger question but made the first row's counts mean something
        /// different from every other row's.
        /// </summary>
        public void OpenNewest(string machineName, List<VersionEntry> versions,
                               bool chosen)
        {
            Prepare(machineName, versions, chosen);

            // Chosen before the window exists, so the counting pass knows what it is
            // measuring from; loaded after it, since loading a machine is what the
            // window is there to show the result of.
            _selected = _versions.Count > 0 ? 0 : -1;

            VersionEntry target = _selected < 0 ? null : _versions[0];
            UIF.WhenReady(delegate
            {
                BuildAndFill();
                if (target != null)
                {
                    Select(target);
                }
            });
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
                if (_options != null)
                {
                    _options.Close();
                }
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
            if (_options != null)
            {
                // The colours belong to this window, so they go where it goes.
                _options.Allow(showing);
            }

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
        ///
        /// <c>StatMaster</c> is not part of the stable Modding namespace and can
        /// change without notice, so a failure here means "not busy" -- a window
        /// that fails to hide is a great deal better than one that never appears.
        ///
        /// "There is no machine" is not on the list, though it used to be. The level
        /// editor is a build area like any other and its load screen is the same load
        /// screen, but an editor with nothing placed in it has no active machine --
        /// so that test hid the window in the one place a player had just asked for
        /// it. What it was really guarding against is the main menu, and
        /// <c>isMainMenu</c> says that directly.
        /// </summary>
        private static bool GameIsBusy()
        {
            try
            {
                return StatMaster.inMenu || StatMaster.hudHidden
                    || StatMaster.isMainMenu || StatMaster.isLoadingLevels;
            }
            catch (Exception)
            {
                // Without StatMaster there is one test left, and it is the old one:
                // outside a build area there is no machine to be describing.
                return Machine.Active() == null;
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
                if (_options != null)
                {
                    _options.KeepInside();
                }
                NoteMachineCleared();
                RedrawOverlay();
            }
        }

        /// <summary>
        /// Drops the overlay when the player empties the machine.
        ///
        /// The shells hang off whatever the blocks are parented to, so clearing the
        /// machine leaves them where they were: a diff of a machine that is no longer
        /// there, in mid-air, with nothing underneath it. It is not the same thing as
        /// a level change, where the shells are destroyed along with everything else
        /// and <see cref="RedrawOverlay"/> puts them back -- here they survive and are
        /// simply wrong, so they go and do not come back until another version is
        /// clicked.
        /// </summary>
        private void NoteMachineCleared()
        {
            if (!_ghosts.Showing || GameIsBusy())
            {
                return;
            }
            Machine machine = Machine.Active();
            if (machine == null || machine.IsLoadingMachine)
            {
                return;
            }
            if (machine.BuildingBlocks != null && machine.BuildingBlocks.Count > 0)
            {
                return;
            }
            _ghosts.Clear();
            Say("The machine was cleared -- pick a version to draw one again.");
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
            Vector2 now = KeepOnScreen(_windowRect);
            if ((now - _placed).sqrMagnitude < MovedEnough * MovedEnough)
            {
                return;
            }
            _placed = now;
            _dirty = true;
            Prefs.SetWindow(now);
        }

        /// <summary>
        /// How much of a window has to stay on screen: enough of its title bar to
        /// get hold of, and enough across for that to be worth aiming at.
        /// </summary>
        private const float HeldWidth = 120f;
        private const float HeldHeight = 34f;

        /// <summary>
        /// Pulls a window back if it has been dragged off the screen, and answers
        /// where it ended up.
        ///
        /// A window is dragged by its title bar, so a window dragged out past the
        /// edge takes the only thing that can bring it back with it -- and this one
        /// remembers where it was put, so it is still out there the next time the
        /// game starts. Enough of the bar is kept on screen to grab.
        ///
        /// Public and static because the colours window has the same problem and no
        /// reason to solve it differently.
        /// </summary>
        public static Vector2 KeepOnScreen(RectTransform window)
        {
            RectTransform space = window == null
                ? null : window.parent as RectTransform;
            if (space == null)
            {
                return window == null ? Vector2.zero : window.anchoredPosition;
            }

            // Where the window's own box is, in the space it is placed in, and how
            // far it may go before there is nothing left to take hold of.
            Rect box = window.rect;
            Rect screen = space.rect;
            Vector2 at = window.anchoredPosition;
            // anchoredPosition is measured from the anchor; with the window anchored
            // anywhere but the middle of the screen the two differ by this much.
            Vector2 anchor = new Vector2(
                Mathf.Lerp(screen.xMin, screen.xMax, window.anchorMin.x),
                Mathf.Lerp(screen.yMin, screen.yMax, window.anchorMin.y));

            float left = anchor.x + at.x + box.xMin;
            float bottom = anchor.y + at.y + box.yMin;
            float wide = Mathf.Min(box.width, HeldWidth);
            float high = Mathf.Min(box.height, HeldHeight);

            float x = Mathf.Clamp(left, screen.xMin - box.width + wide,
                                  screen.xMax - wide);
            // Down is the direction that matters at the bottom: a window whose top
            // bar is below the screen cannot be reached at all, so the top is what is
            // kept rather than the bottom.
            float y = Mathf.Clamp(bottom, screen.yMin - box.height + high,
                                  screen.yMax - box.height);

            Vector2 moved = new Vector2(at.x + (x - left), at.y + (y - bottom));
            if ((moved - at).sqrMagnitude > 0.0001f)
            {
                window.anchoredPosition = moved;
            }
            return moved;
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
            // Again, now that there is something on screen to measure: the first
            // attempt inside RebuildRows runs while the window may still be hidden.
            AlignCounts();

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
                // A title is to be read, not pressed. Its box is the whole width of
                // the bar and it is drawn over the two controls in the corner, so
                // while it took the pointer the cog only answered on whatever part of
                // it the title did not reach.
                label.raycastTarget = false;
            }
        }

        /// <summary>
        /// The two controls at the right-hand end of the title bar: the cross that
        /// shuts the window, and the cog that opens the options.
        ///
        /// Both are squared off against the bar rather than left the size the prefab
        /// authored them -- see <see cref="UIBuild.SquareInBar"/> -- so that the one
        /// we add and the one UI Factory supplies are the same control at the same
        /// size, which is the only way a pair of buttons in a corner look deliberate.
        /// </summary>
        private void HookCloseButton()
        {
            Transform bar = _window.transform.FindChild("TopBar");
            if (bar == null)
            {
                return;
            }
            RectTransform barRect = bar as RectTransform;

            Transform close = bar.FindChild("CloseButton");
            if (close != null)
            {
                UIBuild.SquareInBar(close as RectTransform, barRect, 0);
                // The prefab's own handler may already hide the window; adding to it
                // is what makes sure the overlay goes with it either way.
                UIF.OnClick(close.gameObject, delegate { SetVisible(false); });
            }
            GearButton(barRect, close == null ? 0 : 1);
        }

        /// <summary>The cog beside the cross: everything that is a setting rather than a row.</summary>
        private void GearButton(RectTransform bar, int place)
        {
            UIBuild.BarButton(bar, place, "Options", GearSprite(), QuietInk,
                              ShowOptions);
        }

        /// <summary>
        /// The cog: the Clippy mod's settings mark, the one in the corner of its
        /// dialogue bubble, drawn to that icon's own radii.
        ///
        /// Drawn rather than asked for. UI Factory's bundle cannot be listed -- see
        /// the notes -- so asking the game for its own cog is a guess at a name, and
        /// this is a mark that already exists in a mod beside this one.
        /// </summary>
        private static Sprite GearSprite()
        {
            if (_gearFace == null)
            {
                _gearFace = Drawn(IconArt.Gear(GearPixels));
            }
            return _gearFace;
        }

        /// <summary>One of our own drawings, as a sprite.</summary>
        internal static Sprite Drawn(Texture2D texture)
        {
            texture.hideFlags = HideFlags.HideAndDontSave;
            Sprite made = Sprite.Create(texture,
                                        new Rect(0f, 0f, texture.width, texture.height),
                                        new Vector2(0.5f, 0.5f));
            made.hideFlags = HideFlags.HideAndDontSave;
            return made;
        }

        /// <summary>
        /// Opens the options window, or shuts it if it is already open.
        ///
        /// Parented to the canvas rather than to this window: it is a window in its
        /// own right, and one dragged inside another cannot be moved out of its way.
        /// </summary>
        private void ShowOptions()
        {
            if (_options == null)
            {
                _options = new OptionsView(Recolour, Rescale);
            }
            _options.Toggle(_window.transform.parent, _window.transform as RectTransform);
            if (!_options.Visible)
            {
                // Shutting it is when a colour is settled on, which is the moment
                // worth reaching the disk for.
                Store();
            }
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
            BuildArrow();
            return true;
        }

        // The arrow drawn from the machine a diff is measured *from* to the machine
        // it is measured *to*. Three bars and a head, in the scrolling content so
        // that it moves with the rows it joins.
        //
        // It runs down the outside of the list rather than across it: the rows are
        // full of numbers, and a line drawn over them would be crossing out the
        // answer it is pointing at. The strip to the left of the numbers is the only
        // part of a row with nothing in it.
        private const float ArrowSpine = 4f;
        private const float ArrowInk = 2.5f;
        private const float ArrowHead = 16f;
        private const float ArrowHeadHalf = 10f;

        /// <summary>
        /// How much of that the head at the end is drawn at. Smaller than the ones
        /// down the run: those are read at a glance from anywhere in the list, while
        /// this one is next to the circle it is pointing at and only has to reach it.
        /// </summary>
        private const float TipShare = 0.6f;

        /// <summary>
        /// How far into a row the arrow goes at each end: the near edge of the pin
        /// circle, and a couple of units over it.
        ///
        /// The edge rather than the middle, now that the head is twice the size it
        /// was: a head that reaches the centre of a 22-unit circle covers most of
        /// the circle, and the circle is the thing it is pointing out. Touching it is
        /// enough -- the line leaves the filled circle and arrives at the empty one,
        /// so the arrow and the circles read as one drawing.
        /// </summary>
        private const float ArrowReach = RowMargin + PinLeft + 3f;

        // The heads along the vertical run, and how much line each one wants to
        // itself. A long list puts the two ends of a comparison a screen apart, and
        // the line between them then says everything except which way round it is --
        // the one head, at the far end, may not even be on screen. These say it the
        // whole way down. Five at most, because past that it is a dashed line rather
        // than an arrow.
        private const int ArrowMarks = 5;
        private const float ArrowMarkSpace = 130f;

        private Image[] _arrow;
        private Image[] _arrowMarks;
        private Image _arrowHead;
        private static Sprite _headFace;

        private void BuildArrow()
        {
            _arrow = new Image[3];
            for (int i = 0; i < _arrow.Length; i++)
            {
                _arrow[i] = UIBuild.AddImage(_content, "Arrow", SelectedFill);
                _arrow[i].raycastTarget = false;
                _arrow[i].gameObject.SetActive(false);
            }
            _arrowMarks = new Image[ArrowMarks];
            for (int i = 0; i < _arrowMarks.Length; i++)
            {
                _arrowMarks[i] = UIBuild.AddImage(_content, "ArrowMark", SelectedFill);
                _arrowMarks[i].sprite = HeadSprite();
                _arrowMarks[i].raycastTarget = false;
                _arrowMarks[i].gameObject.SetActive(false);
            }
            _arrowHead = UIBuild.AddImage(_content, "ArrowHead", SelectedFill);
            _arrowHead.sprite = HeadSprite();
            _arrowHead.raycastTarget = false;
            _arrowHead.gameObject.SetActive(false);
        }

        /// <summary>The arrow's head, drawn once -- see <see cref="IconArt"/>.</summary>
        private static Sprite HeadSprite()
        {
            if (_headFace == null)
            {
                Texture2D drawn = IconArt.Head(GearPixels);
                drawn.hideFlags = HideFlags.HideAndDontSave;
                _headFace = Sprite.Create(drawn,
                                          new Rect(0f, 0f, drawn.width, drawn.height),
                                          new Vector2(0.5f, 0.5f));
                _headFace.hideFlags = HideFlags.HideAndDontSave;
            }
            return _headFace;
        }

        /// <summary>
        /// Points the arrow at the two machines the diff on screen is between: out
        /// of the number of the one it is measured from, down the outside of the
        /// list, and back in at the number of the one being looked at.
        ///
        /// Which two those are is not a question about rows. It is
        /// <see cref="Baseline"/> -- the pinned machine, or the one before this in
        /// time -- so the arrow says what the status line says, and follows the list
        /// as it is sorted rather than joining whichever rows happen to be adjacent.
        /// </summary>
        private void PointArrow()
        {
            if (_arrow == null)
            {
                return;
            }

            VersionEntry target = _selected >= 0 && _selected < _versions.Count
                ? _versions[_selected] : null;
            VersionEntry source = target == null ? null : Baseline(target);
            int from = source == null ? -1 : _versions.IndexOf(source);
            if (target == null || from < 0 || from == _selected)
            {
                ShowArrow(false);
                return;
            }

            float startY = RowMiddle(from);
            float endY = RowMiddle(_selected);
            float half = ArrowInk * 0.5f;

            ShowArrow(true);
            Bar(_arrow[0], ArrowSpine, startY - half, ArrowReach - ArrowSpine, ArrowInk);
            Bar(_arrow[1], ArrowSpine, Mathf.Min(startY, endY) - half, ArrowInk,
                Mathf.Abs(endY - startY) + ArrowInk);
            // Up to where the head begins, which is not where a full-sized head would
            // have begun: the head is drawn at a share of its own size, and a line
            // that stopped short of it left the point floating clear of the arrow it
            // belongs to.
            Bar(_arrow[2], ArrowSpine, endY - half,
                Mathf.Max(0f, ArrowReach - ArrowSpine - ArrowHead * TipShare),
                ArrowInk);
            Bar(_arrowHead, ArrowReach - ArrowHead * TipShare,
                endY - ArrowHeadHalf * TipShare, ArrowHead * TipShare,
                ArrowHeadHalf * TipShare * 2f);
            MarkSpine(startY, endY);
        }

        /// <summary>
        /// Puts a few heads down the vertical run, all pointing the way the line is
        /// going.
        ///
        /// Spaced out rather than counted: what matters is that a head is never far
        /// from wherever you are looking, and a list long enough to scroll can put
        /// both ends of the arrow off screen at once. Between one and
        /// <see cref="ArrowMarks"/> of them, each with about
        /// <see cref="ArrowMarkSpace"/> of line to itself.
        /// </summary>
        private void MarkSpine(float startY, float endY)
        {
            if (_arrowMarks == null)
            {
                return;
            }
            float span = Mathf.Abs(endY - startY);
            int wanted = Mathf.Clamp(Mathf.FloorToInt(span / ArrowMarkSpace), 1,
                                     _arrowMarks.Length);
            // Evenly along the run, and never on top of either end: at n marks the
            // first sits one nth of the way down and the last one nth from the
            // bottom, which is what dividing by n + 1 does.
            float step = span / (wanted + 1);
            float top = Mathf.Min(startY, endY);
            bool down = endY > startY;

            for (int i = 0; i < _arrowMarks.Length; i++)
            {
                bool used = i < wanted;
                if (_arrowMarks[i] == null)
                {
                    continue;
                }
                if (_arrowMarks[i].gameObject.activeSelf != used)
                {
                    _arrowMarks[i].gameObject.SetActive(used);
                }
                if (!used)
                {
                    continue;
                }
                float at = down ? top + step * (i + 1) : top + span - step * (i + 1);
                Mark(_arrowMarks[i], ArrowSpine + ArrowInk * 0.5f, at, down);
            }
        }

        /// <summary>
        /// One head on the vertical run, centred on the line and turned to face
        /// along it. The head is drawn pointing right, so the picture is the same one
        /// the arrow ends with, turned a quarter of the way round.
        /// </summary>
        private static void Mark(Image mark, float middleX, float middleY, bool down)
        {
            RectTransform rect = mark.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            // Long rather than square: turned on its side, what was the head's base
            // becomes its width, and the line runs four units from the edge of the
            // scrolling area -- anything broader would have its left side cut off by
            // the mask.
            rect.sizeDelta = new Vector2(ArrowHead, ArrowHeadHalf);
            rect.anchoredPosition = new Vector2(middleX, -middleY);
            rect.localEulerAngles = new Vector3(0f, 0f, down ? -90f : 90f);
            rect.SetAsLastSibling();
        }

        /// <summary>How far down the content a row's middle is.</summary>
        private static float RowMiddle(int index)
        {
            return index * RowHeight + (RowHeight - RowGap) * 0.5f;
        }

        private void ShowArrow(bool shown)
        {
            for (int i = 0; i < _arrow.Length; i++)
            {
                if (_arrow[i] != null && _arrow[i].gameObject.activeSelf != shown)
                {
                    _arrow[i].gameObject.SetActive(shown);
                }
            }
            if (_arrowHead != null && _arrowHead.gameObject.activeSelf != shown)
            {
                _arrowHead.gameObject.SetActive(shown);
            }
            if (shown || _arrowMarks == null)
            {
                // How many of the marks are wanted is MarkSpine's business; it is
                // called next. Hiding them all here is only for the case where there
                // is no arrow at all.
                return;
            }
            for (int i = 0; i < _arrowMarks.Length; i++)
            {
                if (_arrowMarks[i] != null && _arrowMarks[i].gameObject.activeSelf)
                {
                    _arrowMarks[i].gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// One piece of the arrow, in the content's own space: pixels from its
        /// top-left corner, y downwards. Put last, so the arrow draws over the rows
        /// it is joining rather than under whichever were built after it.
        /// </summary>
        private static void Bar(Image bar, float left, float top, float width,
                                float height)
        {
            if (bar == null)
            {
                return;
            }
            RectTransform rect = bar.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(left, -top);
            rect.SetAsLastSibling();
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
            // Vertically off the scrolling area, horizontally off the box the rows
            // are actually laid out in, less the margin they sit inside. Measured
            // rather than worked out from the viewport's width, which is what this
            // did: the two are not the same box, so every heading sat a little left
            // of its column and the numbers under them looked pushed to the right.
            UIBuild.MatchWidth(rect, _content, _window.transform as RectTransform,
                               RowMargin);

            // The one column of text, under two headings: what a row is called and
            // when it was saved are two different orders to want it in, and the row
            // writes both -- the name on the first line, the time on the second.
            _headers[RowSort.ByName] = HeaderButton(rect, RowSort.ByName, ThumbEnd,
                                                    NameEnd);
            _headers[RowSort.ByTime] = HeaderButton(rect, RowSort.ByTime, NameEnd,
                                                    StampEnd);
            _headers[RowSort.ByBlocks] = HeaderButton(rect, RowSort.ByBlocks, StampEnd,
                                                      BlocksEnd);
            _headers[RowSort.ByAdded] = HeaderButton(rect, RowSort.ByAdded, BlocksEnd,
                                                     AddedEnd);
            _headers[RowSort.ByChanged] = HeaderButton(rect, RowSort.ByChanged, AddedEnd,
                                                       ChangedEnd);
            _headers[RowSort.ByRemoved] = HeaderButton(rect, RowSort.ByRemoved, ChangedEnd, 1f);

            SourceHeading(rect);
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
        /// Takes a colour the player has just changed through to everything drawn
        /// in it: the counts in the list and the shells over the machine.
        ///
        /// The headings used to carry a block of colour each, which opened that
        /// colour's picker. Both are gone: the colours are chosen together in the
        /// options window, and a column already says which colour it is in -- its
        /// numbers are written in it.
        /// </summary>
        private void Recolour(int category)
        {
            Restyle();
            _ghosts.Refresh();
            // DiffPalette has already stored the colour; this is what says there is
            // something to flush when the picker is put away.
            _dirty = true;
        }

        /// <summary>
        /// Takes a new shell size through to the overlay. Nothing is drawn again for
        /// it: every shell hangs off a pivot at its own middle, and this is those
        /// pivots being scaled -- which is what makes it usable while the slider is
        /// being dragged.
        /// </summary>
        private void Rescale()
        {
            _ghosts.Rescale();
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
            Text label = UIF.Caption(button, RowSort.ColumnName(column, _chosen),
                                     HeaderFontSize,
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
            return HeaderGap;
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

        /// <summary>
        /// Works out which version the one on screen is being compared with, and
        /// keeps it: every row asks whether it is that one, and answering it per row
        /// would sort the whole list per row.
        /// </summary>
        private void NoteSource()
        {
            _source = _selected >= 0 && _selected < _versions.Count
                ? Baseline(_versions[_selected]) : null;
        }

        /// <summary>The version the diff on screen is measured from. See <see cref="NoteSource"/>.</summary>
        private VersionEntry _source;

        private void RebuildRows()
        {
            NoteSource();
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
            AlignCounts();
            RefreshThumbnails();
            // The rows have moved, so the two the arrow joins have moved with them.
            PointArrow();
        }

        // How far each column of numbers has to move to sit under the word above it,
        // and whether that has been measured yet.
        private readonly float[] _countShift = new float[RowSort.ColumnCount];
        private bool _aligned;

        /// <summary>The columns that hold numbers, in the order they are drawn.</summary>
        private static readonly int[] Counted =
        {
            RowSort.ByBlocks, RowSort.ByAdded, RowSort.ByChanged, RowSort.ByRemoved
        };

        /// <summary>
        /// Puts each column of numbers under the word above it.
        ///
        /// Measured, because two things that ought to line up by arithmetic do not.
        /// A heading is centred in its column of the header strip and the numbers are
        /// centred in the same column of a row, and the strip and the row are laid
        /// out inside different boxes -- so the two columns agree to within a few
        /// units rather than exactly. And a heading is not only its word: it carries
        /// the pair of sort arrows in the same label, which push the word left of the
        /// middle by half their own width. Both together left every number sitting
        /// visibly right of its heading.
        ///
        /// Once, since none of it moves afterwards: the window is a fixed width, and
        /// the arrows are the same two glyphs whichever column is sorted.
        /// </summary>
        private void AlignCounts()
        {
            RectTransform space = _window == null
                ? null : _window.transform as RectTransform;
            if (_aligned || space == null || _rows.Count == 0 ||
                !_window.activeInHierarchy)
            {
                // Nothing is measured off a window that is not on screen. Asked
                // again after every fill, so a first one that arrives before the
                // window does costs a frame rather than the alignment.
                return;
            }

            Canvas.ForceUpdateCanvases();
            _aligned = true;
            for (int i = 0; i < Counted.Length; i++)
            {
                int column = Counted[i];
                Text heading = _headers[column];
                Text cell = CountCell(_rows[0], column);
                if (heading == null || cell == null)
                {
                    continue;
                }
                Bounds word = RectTransformUtility.CalculateRelativeRectTransformBounds(
                    space, heading.rectTransform);
                Bounds under = RectTransformUtility.CalculateRelativeRectTransformBounds(
                    space, cell.rectTransform);
                _countShift[column] = word.center.x - under.center.x -
                                      ArrowsWidth(heading, column) * 0.5f;
            }

            for (int i = 0; i < _rows.Count; i++)
            {
                ShiftCounts(_rows[i]);
            }
        }

        /// <summary>
        /// How much of a heading is arrows: what it measures with them less what it
        /// measures without. Asked of the font that is drawing it, rather than
        /// assumed -- the glyphs are Besiege's and their width is its business.
        /// </summary>
        private float ArrowsWidth(Text heading, int column)
        {
            string marked = heading.text;
            float all = heading.preferredWidth;
            heading.text = RowSort.ColumnName(column, _chosen);
            float word = heading.preferredWidth;
            heading.text = marked;
            return Mathf.Max(0f, all - word);
        }

        private static Text CountCell(HistoryRow row, int column)
        {
            if (column == RowSort.ByBlocks) { return row.Blocks; }
            if (column == RowSort.ByAdded) { return row.Added; }
            if (column == RowSort.ByChanged) { return row.Changed; }
            if (column == RowSort.ByRemoved) { return row.Removed; }
            return null;
        }

        private void ShiftCounts(HistoryRow row)
        {
            for (int i = 0; i < Counted.Length; i++)
            {
                Nudge(CountCell(row, Counted[i]), _countShift[Counted[i]]);
            }
        }

        /// <summary>Slides a cell sideways without changing how wide it is.</summary>
        private static void Nudge(Text cell, float by)
        {
            if (cell == null || by == 0f)
            {
                return;
            }
            RectTransform rect = cell.rectTransform;
            rect.offsetMin = new Vector2(rect.offsetMin.x + by, rect.offsetMin.y);
            rect.offsetMax = new Vector2(rect.offsetMax.x + by, rect.offsetMax.y);
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
            float thumbLeft = NumberLeft + NumberWidth + NumberGap;
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
            // The counts are inset by what their headings are inset by, and by the
            // same amount on both sides, so a centred number lands under a centred
            // heading. Anything else -- the row's own left and right padding, say --
            // puts the two out by half the difference between them.
            row.Blocks = Count(row.Rect, "Blocks", StampEnd, BlocksEnd);
            row.Added = Count(row.Rect, "Added", BlocksEnd, AddedEnd);
            row.Changed = Count(row.Rect, "Changed", AddedEnd, ChangedEnd);
            row.Removed = Count(row.Rect, "Removed", ChangedEnd, 1f);
            // A row built after the columns were measured takes the same nudge: what
            // was measured is where the columns are, which has nothing to do with
            // which row is being built.
            if (_aligned)
            {
                ShiftCounts(row);
            }

            HistoryRow captured = row;
            row.Button = UIF.OnClick(row.Root, delegate { Choose(captured); });

            // Wired once, because it no longer depends on anything: the highlight is
            // the pointer's veil and nothing else, whether this row is the chosen
            // one or not. Before anything can see the white it was created with.
            UIF.HoverTint(row.Button, row.Highlight, ClearTint, HoverFill, PressFill);
            return row;
        }

        /// <summary>
        /// The version's number, written on the row, and the circle beside it that
        /// pins it as the one every other version is compared against.
        ///
        /// Two things again, and for a reason: a version's number is a fact about it
        /// -- where it comes in the history, what the load screen called it -- and
        /// pinning is something you do. They were one control, which meant the
        /// number wore a raised plate and looked like a button because the pin
        /// needed one, and the mark for "this is the source" was a whole square of
        /// red where a dot would do.
        /// </summary>
        private void BuildNumber(HistoryRow row)
        {
            BuildPin(row);
            row.Number = Pixels(row.Rect, "Number", NumberLeft,
                                NumberLeft + NumberWidth, NumberFontSize,
                                TextAnchor.MiddleCenter);
        }

        /// <summary>
        /// The circle that pins a version: empty until it is the one being compared
        /// against, filled red when it is, and the end the arrow is drawn from.
        ///
        /// A button of UI Factory's rather than one drawn here, so it hovers and
        /// presses like every other button in the game -- the circle is a picture
        /// inside it, because the button's own face is the prefab's to colour and a
        /// mark we set on it would be put back the moment the pointer left. A button
        /// inside a button works out on its own: uGUI hands a click to the innermost
        /// thing that handles it, so pinning a row does not also load it.
        /// </summary>
        private void BuildPin(HistoryRow row)
        {
            GameObject pin = UIF.Spawn(UIF.ButtonPrefab, row.Rect);
            if (pin == null)
            {
                // Without the prefab there is no pinning; the list still reads, and
                // every row is still compared with the one before it.
                return;
            }
            pin.name = "Pin";

            // The prefab's own face, before anything of ours is added under it and
            // could be mistaken for it.
            Image face = pin.GetComponent<Image>();
            if (face == null)
            {
                face = pin.GetComponentInChildren<Image>(true);
            }

            // The prefab arrives with "NEW TEXT" written across it, which on a
            // button this small lands on top of the number beside it. Emptied rather
            // than destroyed, as on the row itself: other UIFactory behaviours write
            // to that label, and one of them writing to a destroyed object is a
            // worse bug than a blank one.
            Text ownLabel = pin.GetComponentInChildren<Text>(true);
            if (ownLabel != null)
            {
                ownLabel.text = string.Empty;
            }

            RectTransform rect = UIF.Rect(pin);
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(PinSize, PinSize);
            rect.anchoredPosition = new Vector2(PinLeft, 0f);

            // A round control on a round plate. The prefab's face is a rounded
            // square, which is right for every other button in the game and wrong
            // under a circle -- so it is switched off and a disc of ours takes its
            // place. It is also what the button is clicked on: a Graphic that is not
            // enabled does not take the pointer either, and the ring over it is left
            // out of the raycast so that the whole plate is the target.
            if (face != null)
            {
                face.enabled = false;
            }
            Image plate = UIBuild.AddImage(rect, "Plate", PlateFill);
            plate.sprite = PinSprite(true, true);
            UIF.Stretch(plate.rectTransform, 0f, 0f);

            // The circle is an image of ours inside the button rather than the
            // button's own face recoloured, which was the first attempt at a mark
            // like this and did nothing visible at all. UIFactory's graphics can
            // carry a CustomMaterialHandler -- "forces the image to use a custom
            // shader material instead of the default one" -- and a shader that does
            // not multiply by the renderer's colour cannot be tinted. A plain uGUI
            // Image on the default UI shader takes a colour.
            row.PinFill = UIBuild.AddImage(rect, "Circle", QuietInk);
            row.PinFill.sprite = PinSprite(false, false);
            UIF.Stretch(row.PinFill.rectTransform, 0f, 0f);
            row.PinFill.raycastTarget = false;

            HistoryRow captured = row;
            row.Pin = UIF.OnClick(pin, delegate { TogglePin(captured); });
            SwellOnHover(row.Pin, row.PinFill, false);
        }

        /// <summary>
        /// Makes the circle grow while the pointer is on it.
        ///
        /// Done by handing the button a bigger picture for its hovered state rather
        /// than by scaling the control: UI Factory's buttons already swell a few per
        /// cent, which on a twenty-two unit circle is half a unit and invisible. The
        /// two pictures are the same circle drawn at two sizes inside the same box,
        /// so nothing moves and nothing has to be put back.
        /// </summary>
        private static void SwellOnHover(Button button, Image face, bool filled)
        {
            if (button == null || face == null)
            {
                return;
            }
            button.targetGraphic = face;
            button.transition = Selectable.Transition.SpriteSwap;

            SpriteState swap = new SpriteState();
            swap.highlightedSprite = PinSprite(filled, true);
            swap.pressedSprite = PinSprite(filled, true);
            swap.disabledSprite = PinSprite(filled, false);
            button.spriteState = swap;
        }

        private static readonly Sprite[] _pinFaces = new Sprite[4];

        /// <summary>
        /// The circle: empty or filled, at rest or under the pointer. Drawn at
        /// several times the size it is shown at, since a twenty-two unit ring drawn
        /// twenty-two pixels across is a square with its corners knocked off.
        /// </summary>
        private static Sprite PinSprite(bool filled, bool over)
        {
            int which = (filled ? 2 : 0) + (over ? 1 : 0);
            if (_pinFaces[which] == null)
            {
                _pinFaces[which] = Drawn(IconArt.Disc(PinPixels,
                                                      filled ? 0f : PinRing,
                                                      over ? PinOver : PinRest));
            }
            return _pinFaces[which];
        }

        private const int PinPixels = 64;

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
        /// Pins a version as the one to compare against, or lets go of it.
        ///
        /// One or none, because it is one comparison: a diff has two sides, and the
        /// far side is either a version the player chose or the one before in time.
        /// Pinning a second version replaces the first rather than adding to it.
        ///
        /// Letting go goes back to nothing pinned, which is every row against the row
        /// before it -- the reading of a history a minute at a time that the list is
        /// for. Whichever version that leaves the comparison being measured from is
        /// marked on its own circle, so "no pin" is still a visible state rather than
        /// an absence of one.
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
        private static void MarkPin(HistoryRow row, bool pinned, bool source)
        {
            if (row.PinFill != null)
            {
                row.PinFill.sprite = PinSprite(pinned, false);
                // Three states in two marks: filled red for the version somebody
                // pinned, an empty red ring for the one the diff happens to be
                // measured from -- usually the row above -- and a quiet grey ring for
                // everything else. The red ring is what the arrow comes out of, and
                // it used to be the same grey as every row that has nothing to do
                // with the comparison.
                row.PinFill.color = pinned || source ? SelectedFill : QuietInk;
                // The hovered picture has to change with it, or a filled circle would
                // turn back into a ring under the pointer.
                SwellOnHover(row.Pin, row.PinFill, pinned);
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

        /// <summary>
        /// One column of numbers, centred in the same box its heading is centred in.
        /// </summary>
        private static Text Count(RectTransform row, string name, float from, float to)
        {
            return Cell(row, name, from, to, TextAnchor.MiddleCenter, CountFontSize,
                        HeaderGap, HeaderGap);
        }

        private static Text Cell(RectTransform row, string name, float from, float to,
                                 TextAnchor alignment, int fontSize)
        {
            return Cell(row, name, from, to, alignment, fontSize, PadLeft, PadRight);
        }

        private static Text Cell(RectTransform row, string name, float from, float to,
                                 TextAnchor alignment, int fontSize, float left,
                                 float right)
        {
            GameObject spawned = UIF.Spawn(UIF.TextPrefab, row);
            if (spawned == null)
            {
                Text plain = UIBuild.AddText(row, name, fontSize, alignment);
                UIF.Span(plain.rectTransform, from, to, left, right);
                return plain;
            }

            spawned.name = name;
            UIF.Span(UIF.Rect(spawned), from, to, left, right);
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
        /// A manual save is marked by the word SAVED above its timestamp and by
        /// nothing else. It used to be written in white as well, which made it look
        /// selected.
        /// </summary>
        private void Repaint(HistoryRow row, VersionEntry entry, bool chosen,
                             bool pinned)
        {
            bool source = !chosen && entry == _source;
            Tint(row, chosen, source);
            MarkPin(row, pinned, source);
            if (row.Number != null)
            {
                row.Number.text = entry.Number > 0 ? entry.Number.ToString() : string.Empty;
                // Written white beside a filled circle and grey beside an empty one.
                // The circle is the mark and this is not a second one: it is the same
                // number in the same place, brought up to the weight of the thing
                // next to it.
                row.Number.color = pinned ? Color.white : QuietInk;
            }
            row.Stamp.text = entry.Lines();
            row.Stamp.color = QuietInk;
            BindCounts(row, entry);
        }

        /// <summary>
        /// Frames the row that is the version on screen, dashes the same frame round
        /// the one it is being compared with, and unframes the rest.
        ///
        /// The same frame in the same red for both, because they are the two ends of
        /// one comparison and the arrow between them says so. Solid for the one you
        /// chose and dashed for the one that follows from it -- which is the usual
        /// way of drawing a thing you set against a thing that was worked out.
        ///
        /// The hover veil is not touched here: it is the button's own colour
        /// transition, wired once when the row was built, so being under the
        /// pointer and being the chosen row are two marks that cannot get in each
        /// other's way.
        /// </summary>
        private static void Tint(HistoryRow row, bool chosen, bool source)
        {
            if (row.Edges == null)
            {
                return;
            }
            Color frame = chosen || source ? SelectedFill : ClearTint;
            for (int i = 0; i < row.Edges.Length; i++)
            {
                if (row.Edges[i] == null)
                {
                    continue;
                }
                row.Edges[i].color = frame;
                // The first two are the top and the bottom, the other two the sides.
                Sprite dashes = source && !chosen ? DashSprite(i >= 2) : null;
                row.Edges[i].sprite = dashes;
                row.Edges[i].type = dashes == null ? Image.Type.Simple
                                                   : Image.Type.Tiled;
            }
        }

        private static Sprite _dashAcross;
        private static Sprite _dashDown;

        /// <summary>
        /// The dashes, drawn once each way, at the size the pattern was drawn at
        /// however long the bar carrying it turns out to be.
        ///
        /// A hundred pixels to the unit, which is Unity's default and looks like the
        /// wrong number here. A tiled Image does not tile at the sprite's own size:
        /// it tiles at <c>sprite.pixelsPerUnit / canvas.referencePixelsPerUnit</c>,
        /// and a canvas's reference is 100. Created at one pixel per unit -- which
        /// reads as "draw it actual size" and is what this was -- every dash came out
        /// a hundred times too long, which is to say the frame was a solid line.
        /// </summary>
        private static Sprite DashSprite(bool down)
        {
            if (down && _dashDown != null)
            {
                return _dashDown;
            }
            if (!down && _dashAcross != null)
            {
                return _dashAcross;
            }

            Texture2D drawn = IconArt.Dashes(down, Mathf.RoundToInt(EdgeThickness));
            drawn.hideFlags = HideFlags.HideAndDontSave;
            Sprite made = Sprite.Create(drawn, new Rect(0f, 0f, drawn.width, drawn.height),
                                        new Vector2(0.5f, 0.5f), CanvasPixelsPerUnit);
            made.hideFlags = HideFlags.HideAndDontSave;
            if (down)
            {
                _dashDown = made;
            }
            else
            {
                _dashAcross = made;
            }
            return made;
        }

        private void Restyle()
        {
            NoteSource();
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Entry != null)
                {
                    Repaint(_rows[i], _rows[i].Entry, i == _selected,
                            _rows[i].Entry == _base);
                }
            }
            // Whatever changed here -- the row being looked at, or what it is being
            // compared with -- is the two ends of the arrow.
            PointArrow();
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
            // each save did: there is no save before it to be a change from. Against
            // a pinned source it is a comparison like any other, and so is the pinned
            // version itself -- which comes out as three dashes, being no different
            // from itself.
            if (_base == null && RowSort.Earlier(_versions, entry) == null)
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
                _headers[column].text = RowSort.ColumnName(column, _chosen) + "  " +
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
            if (chosen != null && _base == null)
            {
                // Nothing is re-counted -- what a row counts as added, changed and
                // removed is what that save did, whatever order the list is in -- but
                // the machine on screen is compared with the row under it, and that
                // is a different row now. So the colours over the machine, the arrow
                // and the status line are worked out again.
                ShowDiff(chosen);
            }
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
        /// What a version is compared against: whatever the player pinned, or the row
        /// underneath it. A version pinned and then clicked is compared with itself,
        /// which is a real answer -- no change -- and needs no special case.
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
        /// The row under this one, which with nothing pinned is what it is compared
        /// against -- see <see cref="RowSort.Below"/>. Null for the bottom row, which
        /// has nothing under it to be a change from.
        /// </summary>
        private VersionEntry Predecessor(VersionEntry entry)
        {
            return RowSort.Below(_versions, entry);
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

            // Oldest first, by number, which is the machine's own order and not the
            // one the list happens to be showing. What a row counts as added,
            // changed and removed is what that save did -- a fact about the version,
            // fixed once it has been read -- because a column of numbers that
            // changed every time the list was re-arranged could not be sorted by:
            // clicking ADDED would order the rows by figures that stopped being true
            // the moment the click landed.
            List<VersionEntry> ordered = new List<VersionEntry>(_versions);
            RowSort.Apply(ordered, RowSort.ByNumber, true);

            // Held for the whole pass when a version is pinned: every row is then a
            // comparison with the same machine, so it is read once instead of once
            // per row. Null when nothing is pinned, and then `previous` walks the
            // list a row at a time.
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
            // A list sorted by a column of counts was sorted by figures that had not
            // been read yet -- every row was a dot when the heading was clicked, or
            // half of them were. Now that they are all in, the order that was asked
            // for is applied to them.
            if (RowSort.IsCount(_sortColumn))
            {
                VersionEntry chosen = _selected >= 0 && _selected < _versions.Count
                    ? _versions[_selected] : null;
                RowSort.Apply(_versions, _sortColumn, _ascending);
                _selected = chosen == null ? -1 : _versions.IndexOf(chosen);
                RebuildRows();
            }
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
