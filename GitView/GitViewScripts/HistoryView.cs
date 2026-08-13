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

        /// <summary>The circle left of the number, which pins this version.</summary>
        public Button Pin;

        /// <summary>
        /// The circle drawn inside that button: a ring, or a disc when pinned. An
        /// image of ours rather than the button's own face -- see
        /// <see cref="HistoryView.BuildPin"/>.
        /// </summary>
        public Image PinFill;

        /// <summary>The four bars that frame the row when it is the version on screen.</summary>
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
    /// when it was taken, and what it did to the machine. Built out of UI Factory's
    /// prefabs so it is Besiege's window rather than a drawing of one, on a canvas of
    /// its own above the game's HUD.
    /// </summary>
    public class HistoryView : MonoBehaviour
    {
        private const int CanvasOrder = 29000;

        /// <summary>
        /// Wide enough for what is written in it and no wider -- measured from what a
        /// column actually holds rather than what it might. See the column table.
        /// </summary>
        private const float WindowWidth = 679f;
        private const float WindowHeight = 560f;

        /// <summary>
        /// Where the window opens the first time, in canvas units from the middle of a
        /// 1920x1080 canvas: the top left, clear of the toolbar and the block palette,
        /// leaving the machine the right-hand side of the screen. The same 53-unit
        /// margin under the toolbar and in from the edge, which is why it is written
        /// as arithmetic on the width rather than as a number.
        /// </summary>
        private static readonly Vector2 WindowHome =
            new Vector2(-960f + 53f + WindowWidth * 0.5f, 109f);

        /// <summary>
        /// How far the window has to move before it is worth storing: a drag changes
        /// the position every frame, and a preference only has to be right at the end.
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
        /// The counts are written at the size of the version number, the other thing
        /// in a row worth reading from across the screen. The timestamp stays smaller:
        /// it is the row's label rather than its answer.
        /// </summary>
        private const int CountFontSize = NumberFontSize;

        // Column edges as fractions of the row's width, and the padding inside one.
        // One table for both the header and every row, so a heading and the values
        // under it cannot drift apart. Across a 643-unit row:
        //
        //   132  the pin, the number and the picture, which are fixed pixel sizes
        //   155  the name and the time: 110 for a written-out timestamp, and room for
        //        the two headings that share the column to sit side by side
        //    89  each of the four counts
        //
        // The count columns are set by what is written *above* them, measured off a
        // screenshot: "CHANGED" is 49 units at the header size and its pair of arrows
        // another 24, so 89 leaves about 5 units of air each side -- enough for the
        // swell under the pointer and not a unit more.
        private const float ThumbEnd = 0.205f;
        private const float ThumbInset = 5f;
        private const float StampEnd = 0.446f;
        private const float BlocksEnd = 0.585f;
        private const float AddedEnd = 0.723f;
        private const float ChangedEnd = 0.862f;
        private const float PadLeft = 9f;
        private const float PadRight = 13f;

        // The version's place in the history, left of its thumbnail. In pixels rather
        // than as a fraction of the row: it holds two or three digits and nothing
        // else, so there is nothing for extra width to do.
        //
        // Written on the row rather than on a button, being a fact about the version
        // and not a thing to press. What is pressed is the circle to its left.
        private const float NumberWidth = 36f;
        private const float NumberGap = 6f;
        private const int NumberFontSize = 20;

        /// <summary>
        /// The pin circle: how far in from the row's edge it sits, how big it is, and
        /// the gap to the number. Further in than the row's own padding, because the
        /// number is centred in a box wide enough for three digits and so sits six to
        /// twelve units further off than it looks.
        /// </summary>
        private const float PinLeft = 16f;
        private const float PinSize = 22f;
        private const float PinGap = 2f;

        /// <summary>How thick the empty ring is drawn, as a share of the circle.</summary>
        private const float PinRing = 0.07f;

        /// <summary>
        /// How much of its button the circle fills at rest, and under the pointer.
        /// The growing is done by swapping the picture rather than scaling anything:
        /// UI Factory's own swell is a few per cent, which on a 22-unit circle is
        /// half a unit and reads as nothing.
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
        /// The thumbnail's side, square by construction rather than by a number kept
        /// in step with the row's height: Besiege writes them 512x512.
        /// </summary>
        private const float ThumbSize = RowHeight - RowGap - ThumbInset * 2f;

        /// <summary>
        /// Where the first column ends: the right edge of the thumbnail. Its heading
        /// is measured against this, so the two cannot come apart.
        /// </summary>
        private const float SourceEnd = NumberLeft + NumberWidth + NumberGap + ThumbSize;

        /// <summary>
        /// Where the name heading gives way to the time heading, halfway across the
        /// column they share. The name is on the left because that is where the name
        /// is: a row writes it on the first line and the time under it.
        /// </summary>
        private const float NameEnd = 0.326f;

        // The frame that marks the row being shown: how thick its bars are and how
        // far inside the row's edge they sit. A row is a rounded rectangle, so a
        // frame drawn hard against its edge would poke square corners out past the
        // rounded ones.
        private const float EdgeThickness = 2f;
        private const float EdgeInset = 3f;

        /// <summary>
        /// What a canvas takes one unit to be: the number a repeating sprite has to be
        /// created at to repeat at the size it was drawn. Unity's default for
        /// <c>Canvas.referencePixelsPerUnit</c>, and ours is left at it.
        /// </summary>
        private const float CanvasPixelsPerUnit = 100f;

        private static readonly Color ClearTint = new Color(1f, 1f, 1f, 0f);

        /// <summary>
        /// The disc behind the pin circle, in place of the rounded square the prefab
        /// draws. Sampled off the plate it replaces, so the one round control in the
        /// list is the same shade as everything else.
        /// </summary>
        private static readonly Color PlateFill =
            new Color(0.078f, 0.110f, 0.165f, 1f);

        // What a row does instead of swelling when the pointer is over it. See
        // UIF.NoSwell for why a row cannot swell.
        private static readonly Color HoverFill = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color PressFill = new Color(1f, 1f, 1f, 0.18f);

        /// <summary>
        /// The red Besiege marks a chosen thing with, out of UI Factory's copy of the
        /// game's palette, with the same value written out in case that class moves.
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

        /// <summary>Keeps the game from being clicked through either window.</summary>
        private readonly ClickShield _shield = new ClickShield();

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
        /// takes about a second over a long history -- time enough for the player to
        /// change what the counts are measured from, and the older pass then gives up
        /// rather than writing numbers nobody asked for.
        /// </summary>
        private int _countPass;

        /// <summary>
        /// Whether the player has the window open, which is not whether it is on
        /// screen: it steps aside while a menu is up.
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
        /// <paramref name="chosen"/> says which kind of list this is: machines picked
        /// out one at a time, or the versions of one machine. Only the first column's
        /// name turns on it, that being where the difference shows.
        /// </summary>
        public void Open(string machineName, List<VersionEntry> versions, bool chosen)
        {
            Prepare(machineName, versions, chosen);
            UIF.WhenReady(delegate { BuildAndFill(); });
        }

        /// <summary>
        /// Takes the list, without deciding what to show from it. Separate from
        /// <see cref="Open"/> so a caller can pin and choose *before* the window is
        /// built: the counting pass reads the pin when it starts.
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

            // A hand-picked list opens in the order you picked them, newest choice at
            // the top, which is the order the marks in the load screen were in. Sorting
            // one by time means nothing: most of those machines have no time, and the
            // ones that do were saved in an order unrelated to why they were compared.
            if (_chosen)
            {
                _sortColumn = RowSort.ByNumber;
                _ascending = false;
            }
            RowSort.Apply(_versions, _sortColumn, _ascending);
        }

        /// <summary>
        /// Shows a list and opens it on its own first row, which is what the compare
        /// buttons in the load screen do: the top row against the one under it, with
        /// nothing pinned. That is the list's own default reading, so the window opens
        /// showing what the counts beside it are showing.
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
        /// Shows the window when the player wants it and the game is not busy showing
        /// something of its own -- which is how Besiege's block mapper behaves. The
        /// overlay goes with it: a machine covered in red ghosts is not what you want
        /// to look at while picking a different machine to load. The player's own
        /// answer is kept in <c>_wanted</c>, so a menu cannot undo it.
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
                // Said once: a window that is open and invisible is otherwise
                // indistinguishable from one that failed to build.
                _saidWaiting = true;
                Log.Info("the history window is waiting for the game's own menu to close.");
            }
        }

        /// <summary>
        /// True while there is nothing for the window to be over: a menu is up, the
        /// HUD is hidden, or the player has left the build area for the main menu, the
        /// level selector or a level still loading. The window and its canvas outlive
        /// scene loads -- they have to, or the history would be lost whenever a level
        /// was opened -- so nothing else stops them being drawn over the main menu.
        ///
        /// <c>StatMaster</c> is not part of the stable Modding namespace, so a failure
        /// here means "not busy": a window that fails to hide beats one that never
        /// appears. "There is no machine" is deliberately not on the list -- an empty
        /// level editor has none, and that is exactly where a player asked for this.
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
            // Polled rather than driven off StatMaster.inMenuChanged, which is a
            // plain static Action: subscribing means remembering to unsubscribe, and
            // getting that wrong leaves a destroyed window being called into.
            if (_built)
            {
                // First, so that anything below throwing cannot leave the game's
                // own cameras deaf to the mouse.
                _shield.Follow();
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
        /// Drops the overlay when the player empties the machine. The shells hang off
        /// whatever the blocks are parented to, so clearing leaves them in mid-air
        /// with nothing underneath. Not the same as a level change, where they are
        /// destroyed with everything else and <see cref="RedrawOverlay"/> puts them
        /// back; here they survive and are simply wrong.
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
        /// Puts the overlay back after a level change, which destroys and rebuilds
        /// everything the shells were parented to. Besiege carries the machine across,
        /// so the diff is still true of it; only the objects drawing it were lost.
        ///
        /// Retried on a timer rather than every frame, because the first attempts fail
        /// -- the machine exists before its blocks do.
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
            // Waited for rather than assumed: drawing before the blocks arrive would
            // hang the shells off the machine's own transform, which is a different
            // place from whatever the blocks are parented to.
            if (machine.BuildingBlocks == null || machine.BuildingBlocks.Count == 0)
            {
                return;
            }
            _redrawAt = Time.unscaledTime + RedrawInterval;
            _ghosts.Restore();
        }

        /// <summary>
        /// Notices the window being dragged. Polled rather than hooked: the drag is UI
        /// Factory's and reports nothing, so where the rect ended up is the only
        /// account of it there is.
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
        /// How much of a window has to stay on screen: enough of the title bar to take
        /// hold of, and enough across to be worth aiming at.
        /// </summary>
        private const float HeldWidth = 120f;
        private const float HeldHeight = 34f;

        /// <summary>
        /// Pulls a window back if it has been dragged off the screen, and answers
        /// where it ended up. A window is dragged by its title bar, so one dragged out
        /// past the edge takes the only thing that could bring it back -- and the
        /// position is remembered, so it would still be out there next time. Public
        /// and static because the colours window has the same problem.
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
        /// Puts anything changed since the last call on disk. Called when the player
        /// is finished with something -- a picker closed, the window hidden, the game
        /// quit -- since a drag changes things many times a second.
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

        private void OnDisable()
        {
            // Before OnDestroy, and also for a disabling that is not one: either
            // way there is no longer anything on screen to be shielding.
            _shield.Release();
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
            // Anchored and pivoted in the middle by us rather than however the prefab
            // was authored, so a stored position means one thing: canvas units from
            // the middle of the screen.
            _windowRect.anchorMin = new Vector2(0.5f, 0.5f);
            _windowRect.anchorMax = new Vector2(0.5f, 0.5f);
            _windowRect.pivot = new Vector2(0.5f, 0.5f);
            _windowRect.sizeDelta = new Vector2(WindowWidth, WindowHeight);

            Canvas.ForceUpdateCanvases();
            _windowRect.anchoredPosition = Fit(Prefs.Window(WindowHome));
            _placed = _windowRect.anchoredPosition;
            _shield.Guard(_windowRect);

            HookCloseButton();
            if (!FindScrollView())
            {
                return;
            }
            BuildHeader();
            BuildStatusLine();
            BuildBottomGrip();
            _built = true;
        }

        /// <summary>
        /// Keeps a position on screen. Worth doing even for the default, since the
        /// canvas matches on height and a screen narrower than 16:9 has fewer units
        /// across than the layout assumes -- and more so for a stored one, which was
        /// written on whatever monitor the player had last time.
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
            UIBuild.SetTitle(UIBuild.TitleBar(_window),
                             string.IsNullOrEmpty(title) ? "HISTORY" : title.ToUpper());
        }

        /// <summary>
        /// The two controls at the right-hand end of the title bar: the cross that
        /// shuts the window, and the cog that opens the options.
        /// </summary>
        private void HookCloseButton()
        {
            RectTransform bar = UIBuild.TitleBar(_window);
            if (bar == null)
            {
                return;
            }
            int place = UIBuild.HookClose(bar, delegate { SetVisible(false); });
            // The cog beside the cross: everything that is a setting rather than a row.
            UIBuild.BarButton(bar, place, "Options", GearSprite(), UIBuild.QuietInk,
                              ShowOptions);
        }

        /// <summary>
        /// The cog: the Clippy mod's settings mark, drawn to that icon's own radii.
        /// Drawn rather than asked for -- UI Factory's bundle cannot be listed, so
        /// asking the game for its own cog is a guess at a name.
        /// </summary>
        private static Sprite GearSprite()
        {
            if (_gearFace == null)
            {
                _gearFace = UIBuild.Drawn(IconArt.Gear(GearPixels));
            }
            return _gearFace;
        }

        /// <summary>
        /// Opens the options window, or shuts it if it is open. Parented to the canvas
        /// rather than to this window: one window dragged inside another cannot be
        /// moved out of its way.
        /// </summary>
        private void ShowOptions()
        {
            if (_options == null)
            {
                _options = new OptionsView(Recolour, Rescale);
            }
            _options.Toggle(_window.transform.parent, _window.transform as RectTransform);
            _shield.Guard(_options.Rect);
            if (!_options.Visible)
            {
                // Shutting it is when a colour is settled on, which is the moment
                // worth reaching the disk for.
                Store();
            }
        }

        /// <summary>
        /// Takes over the scroll view the Window prefab ships with, and makes room
        /// above and below it for the headers and the status line.
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

        // The arrow from the machine a diff is measured *from* to the machine it is
        // measured *to*. Three bars and a head, in the scrolling content so it moves
        // with the rows it joins. Down the outside of the list rather than across it:
        // a line drawn over the rows would be crossing out the answer it points at.
        private const float ArrowSpine = 4f;
        private const float ArrowInk = 2.5f;
        private const float ArrowHead = 16f;
        private const float ArrowHeadHalf = 10f;

        /// <summary>
        /// How much of that size the head at the end is drawn at. Smaller than the
        /// ones down the run: it sits beside the circle it points at and only has to
        /// reach it.
        /// </summary>
        private const float TipShare = 0.6f;

        /// <summary>
        /// How far into a row the arrow reaches: the near edge of the pin circle and a
        /// couple of units over it. The edge rather than the middle, since a head that
        /// reached the centre would cover the circle it is pointing out.
        /// </summary>
        private const float ArrowReach = RowMargin + PinLeft + 3f;

        // The heads along the vertical run, and how much line each one wants to
        // itself. A long list puts the two ends of a comparison a screen apart, with
        // the head at the far end off screen -- so these say which way it runs the
        // whole way down. Five at most, past which it is a dashed line, not an arrow.
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
                _headFace = UIBuild.Drawn(IconArt.Head(GearPixels));
            }
            return _headFace;
        }

        /// <summary>
        /// Points the arrow at the two machines the diff on screen is between: out of
        /// the circle of the one it is measured from, down the outside of the list,
        /// back in at the circle of the one being looked at. Which two those are is
        /// <see cref="Baseline"/>, so the arrow always says what the status line says.
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
            // have begun -- the head is drawn at a share of its own size, and a line
            // stopping short of it leaves the point floating.
            Bar(_arrow[2], ArrowSpine, endY - half,
                Mathf.Max(0f, ArrowReach - ArrowSpine - ArrowHead * TipShare),
                ArrowInk);
            Bar(_arrowHead, ArrowReach - ArrowHead * TipShare,
                endY - ArrowHeadHalf * TipShare, ArrowHead * TipShare,
                ArrowHeadHalf * TipShare * 2f);
            MarkSpine(startY, endY);
        }

        /// <summary>
        /// Puts a few heads down the vertical run, pointing the way the line goes.
        /// Spaced rather than counted, so a head is never far from wherever you are
        /// looking: between one and <see cref="ArrowMarks"/>, each with about
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
            // Evenly along the run and never on top of either end, which is what
            // dividing by n + 1 does.
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
        /// One head on the vertical run, centred on the line and turned to face along
        /// it: the same picture the arrow ends with, a quarter turn round.
        /// </summary>
        private static void Mark(Image mark, float middleX, float middleY, bool down)
        {
            RectTransform rect = mark.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            // Long rather than square: turned on its side the head's base becomes its
            // width, and the line runs four units from the edge of the scrolling area,
            // so anything broader is cut off by the mask.
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
                // How many marks are wanted is MarkSpine's business, and it is called
                // next; this is only for the case where there is no arrow at all.
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
        /// top-left corner, y downwards. Put last, so it draws over the rows it joins.
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
        /// The column headings, in the strip the scroll view has just given up. Placed
        /// by measuring rather than anchoring to the window: the rows are laid out
        /// across the viewport, which is inset by however much the prefab's frame and
        /// scrollbar take, so an anchored heading drifts off its column.
        /// </summary>
        private void BuildHeader()
        {
            RectTransform rect = UIBuild.CreateRect("Header", _window.transform);
            UIBuild.PlaceStrip(rect, _viewport, _window.transform as RectTransform,
                               HeaderHeight, true);
            // Vertically off the scrolling area, horizontally off the box the rows
            // are actually laid out in, less the margin they sit inside: the viewport
            // and the content are not the same box.
            UIBuild.MatchWidth(rect, _content, _window.transform as RectTransform,
                               RowMargin);

            // One column of text under two headings: what a row is called and when it
            // was saved are two different orders to want it in, and the row writes
            // both -- the name on the first line, the time on the second.
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
        /// The heading over the leftmost column: the pin, the number and the picture.
        /// Across the whole column rather than over the numbers alone -- "VERSION ▲▼"
        /// is wider than a strip sized for three digits, and the only room it can
        /// borrow is the picture's.
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
        /// Takes a colour the player has just changed through to everything drawn in
        /// it: the counts in the list and the shells over the machine.
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
        /// Takes a new shell size through to the overlay. Nothing is drawn again --
        /// every shell hangs off a pivot at its own middle, and this scales those --
        /// which is what makes it usable while the slider is being dragged.
        /// </summary>
        private void Rescale()
        {
            _ghosts.Rescale();
            _dirty = true;
        }

        /// <summary>
        /// One clickable column heading, centred in its column above centred values.
        ///
        /// Centred rather than pushed to the column's edge: an edge-aligned label has
        /// to be inset by exactly what the values below it are inset by, and that
        /// cannot be applied reliably -- UIFactory keeps its label down a hierarchy of
        /// its own, so stretching "the label" insets it inside whatever container it
        /// sits in. A centred label only needs its container centred in the button.
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
                label.color = UIBuild.QuietInk;
                UIF.StretchInset(label.rectTransform, 0f, 0f, 0f);
            }

            // Pivoted in the middle, so the hover swell grows evenly either side and
            // a centred label stays where it is.
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
            // Anchored inside the window's bottom edge rather than measured off the
            // scrolling area the way the header is: the viewport reaches the bottom of
            // the frame, so "just below the viewport" is outside the window.
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
                _status.color = UIBuild.QuietInk;
                _status.text = string.Empty;
            }
        }

        /// <summary>
        /// Makes the strip under the list a second place to take hold of the
        /// window.
        ///
        /// UI Factory puts its Drag on the title bar and nowhere else, and that is
        /// 50 units at the top of a window most of a screen tall. The band the
        /// status line is written across is the only other part of the frame with
        /// nothing in it to press, so it is the one part that can be grabbed
        /// without pressing something -- and it is exactly the room
        /// <see cref="FindScrollView"/> took off the bottom of the list.
        /// </summary>
        private void BuildBottomGrip()
        {
            // Invisible, but a raycast target all the same, which is the whole of
            // what Drag asks of the thing it is put on.
            Image grip = UIBuild.AddImage(_window.transform, "Grip", ClearTint);
            grip.raycastTarget = true;

            RectTransform rect = grip.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(0f, StatusHeight + StatusMargin);
            rect.anchoredPosition = Vector2.zero;
            // Last, so the status line it is drawn over is behind it rather than
            // in front of it taking the pointer first.
            rect.SetAsLastSibling();

            try
            {
                Besiege.UI.Bridge.Drag drag =
                    grip.gameObject.AddComponent<Besiege.UI.Bridge.Drag>();
                // Named before Drag's own Start runs, which would otherwise take
                // the strip itself as the thing to move.
                drag.Target = _windowRect;
                drag.UseSnap = false;
            }
            catch (Exception)
            {
                // A UI Factory without that behaviour leaves the title bar, which
                // is how this window has always been moved.
            }
        }

        // --------------------------------------------------------------------- rows

        /// <summary>
        /// Works out which version the one on screen is compared with, and keeps it:
        /// every row asks whether it is that one, and answering per row would search
        /// the whole list per row.
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
        /// Measured, because two things that ought to line up by arithmetic do not:
        /// the header strip and a row are laid out inside different boxes, so their
        /// columns agree to within a few units rather than exactly, and a heading
        /// carries the pair of sort arrows in the same label -- which pushes the word
        /// left of the middle by half their width.
        ///
        /// Once, since none of it moves afterwards.
        /// </summary>
        private void AlignCounts()
        {
            RectTransform space = _window == null
                ? null : _window.transform as RectTransform;
            if (_aligned || space == null || _rows.Count == 0 ||
                !_window.activeInHierarchy)
            {
                // Nothing is measured off a window that is not on screen. Asked again
                // after every fill, so a first one that arrives too early costs a
                // frame rather than the alignment.
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
        /// measures without, asked of the font that is drawing it.
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
            // stack of separate buttons Besiege's own panels are made of.
            row.Rect = UIF.Rect(row.Root);
            row.Rect.anchorMin = new Vector2(0f, 1f);
            row.Rect.anchorMax = new Vector2(1f, 1f);
            row.Rect.pivot = new Vector2(0.5f, 1f);
            row.Rect.sizeDelta = new Vector2(-RowMargin * 2f, RowHeight - RowGap);
            // A row is far too wide to swell under the pointer without throwing its
            // own text about; it lights up instead.
            UIF.NoSwell(row.Root);

            // The prefab's own label is in the way of the columns. Emptied rather
            // than destroyed: other UIFactory behaviours write to it.
            Text ownLabel = row.Root.GetComponentInChildren<Text>(true);
            if (ownLabel != null)
            {
                ownLabel.text = string.Empty;
            }

            // Opaque white, and left that way: uGUI's colour transition multiplies
            // the state's colour by the graphic's own, so an image created transparent
            // stays invisible whatever it is told to become.
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

            // The counts are centred under centred headings -- see HeaderButton. The
            // timestamps are the exception: all the same length and read as a list
            // rather than a column of figures, so they line up on the left.
            row.Stamp = Cell(row.Rect, "Stamp", ThumbEnd, StampEnd, TextAnchor.MiddleLeft,
                             RowFontSize, PadLeft, PadRight);
            // Inset by what their headings are inset by, the same on both sides, so a
            // centred number lands under a centred heading.
            row.Blocks = Count(row.Rect, "Blocks", StampEnd, BlocksEnd);
            row.Added = Count(row.Rect, "Added", BlocksEnd, AddedEnd);
            row.Changed = Count(row.Rect, "Changed", AddedEnd, ChangedEnd);
            row.Removed = Count(row.Rect, "Removed", ChangedEnd, 1f);
            // A row built after the columns were measured takes the same nudge.
            if (_aligned)
            {
                ShiftCounts(row);
            }

            HistoryRow captured = row;
            row.Button = UIF.OnClick(row.Root, delegate { Choose(captured); });

            // Wired once: the highlight is the pointer's veil and nothing else,
            // whether this row is the chosen one or not.
            UIF.HoverTint(row.Button, row.Highlight, ClearTint, HoverFill, PressFill);
            return row;
        }

        /// <summary>
        /// The version's number, written on the row, and the circle beside it that
        /// pins it. Two things, because a version's number is a fact about it and
        /// pinning is something you do.
        /// </summary>
        private void BuildNumber(HistoryRow row)
        {
            BuildPin(row);
            row.Number = Pixels(row.Rect, "Number", NumberLeft,
                                NumberLeft + NumberWidth, NumberFontSize,
                                TextAnchor.MiddleCenter);
        }

        /// <summary>
        /// The circle that pins a version: an empty ring until it is the one being
        /// compared against, filled red when it is, and the end the arrow leaves from.
        ///
        /// A button of UI Factory's, so it hovers and presses like every other button
        /// in the game, with the circle a picture inside it -- the button's own face
        /// is the prefab's to colour, and a mark set on it would be put back the
        /// moment the pointer left. A button inside a button works out on its own:
        /// uGUI hands a click to the innermost thing that handles it.
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

            // The prefab arrives with "NEW TEXT" written across it, which on a button
            // this small lands on the number beside it. Emptied rather than destroyed,
            // as on the row itself.
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

            // A round control on a round plate: the prefab's face is a rounded square,
            // so it is switched off and a disc of ours takes its place. That disc is
            // also what the button is clicked on -- a disabled Graphic does not take
            // the pointer -- and the ring over it is left out of the raycast.
            if (face != null)
            {
                face.enabled = false;
            }
            Image plate = UIBuild.AddImage(rect, "Plate", PlateFill);
            plate.sprite = PinSprite(true, true);
            UIF.Stretch(plate.rectTransform, 0f, 0f);

            // An image of ours rather than the button's own face recoloured, which did
            // nothing visible: UIFactory's graphics can carry a CustomMaterialHandler,
            // and a shader that does not multiply by the renderer's colour cannot be
            // tinted. A plain uGUI Image on the default UI shader takes a colour.
            row.PinFill = UIBuild.AddImage(rect, "Circle", UIBuild.QuietInk);
            row.PinFill.sprite = PinSprite(false, false);
            UIF.Stretch(row.PinFill.rectTransform, 0f, 0f);
            row.PinFill.raycastTarget = false;

            HistoryRow captured = row;
            row.Pin = UIF.OnClick(pin, delegate { TogglePin(captured); });
            SwellOnHover(row.Pin, row.PinFill, false);
        }

        /// <summary>
        /// Makes the circle grow while the pointer is on it, by handing the button a
        /// bigger picture for its hovered state rather than scaling the control. The
        /// two pictures are the same circle at two sizes inside the same box, so
        /// nothing moves and nothing has to be put back.
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
        /// The circle: empty or filled, at rest or under the pointer. Drawn several
        /// times the size it is shown at, since a 22-unit ring drawn 22 pixels across
        /// is a square with its corners knocked off.
        /// </summary>
        private static Sprite PinSprite(bool filled, bool over)
        {
            int which = (filled ? 2 : 0) + (over ? 1 : 0);
            if (_pinFaces[which] == null)
            {
                _pinFaces[which] = UIBuild.Drawn(
                    IconArt.Disc(PinPixels, filled ? 0f : PinRing,
                                 over ? PinOver : PinRest));
            }
            return _pinFaces[which];
        }

        private const int PinPixels = 64;

        /// <summary>
        /// The four bars that frame the row being shown.
        ///
        /// A frame rather than the filled red Besiege puts on a chosen option: that
        /// works on a short button with one word on it, and a row here is a thumbnail,
        /// a timestamp and three counts in three colours of their own, which a strong
        /// red behind everything would make the hardest row to read.
        ///
        /// Inset from the row's edge because a row is a rounded rectangle: three units
        /// in, against a corner radius of about five.
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
        /// Pins a version as the one to compare against, or lets go of it. One or
        /// none, because a diff has two sides: pinning a second version replaces the
        /// first. Letting go goes back to every row against the row under it, and
        /// whichever version that leaves the comparison measured from is marked on its
        /// own circle -- so "no pin" is a visible state rather than an absence of one.
        /// </summary>
        private void TogglePin(HistoryRow row)
        {
            if (row == null || row.Entry == null)
            {
                return;
            }
            _base = _base == row.Entry ? null : row.Entry;
            Restyle();
            // The diff on screen was measured against something else, so it is worked
            // out again -- without reloading the machine, which has not changed.
            if (_selected >= 0 && _selected < _versions.Count)
            {
                ShowDiff(_versions[_selected]);
            }
            // And so is every count in the list, which answers the same question a
            // row at a time.
            Recount();
        }

        /// <summary>
        /// Marks a pin. Three states in two pictures: filled red for the version
        /// somebody pinned, an empty red ring for the one the diff happens to be
        /// measured from, and a quiet grey ring for everything else.
        /// </summary>
        private static void MarkPin(HistoryRow row, bool pinned, bool source)
        {
            if (row.PinFill != null)
            {
                row.PinFill.sprite = PinSprite(pinned, false);
                row.PinFill.color = pinned || source ? SelectedFill : UIBuild.QuietInk;
                // The hovered picture has to change with it, or a filled circle would
                // turn back into a ring under the pointer.
                SwellOnHover(row.Pin, row.PinFill, pinned);
            }
        }

        /// <summary>
        /// A label placed in pixels rather than across a fraction of the row: a
        /// two-digit number does not want more room on a wider window.
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

        /// <summary>
        /// One column of a row: a label from UI Factory's Text prefab, which brings
        /// Besiege's font and its letter spacing with it, spanning the given share of
        /// the row's width. Falls back to a plain label if UI Factory cannot supply
        /// one, so a row is never simply missing.
        /// </summary>
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
        /// Colours a row for what it is and whether it is the one being shown. Only
        /// the frame says which row is chosen: the text is the same on every row, so
        /// a column means one thing all the way down.
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
                // White beside a filled circle and grey beside an empty one: the same
                // number in the same place, at the weight of the thing next to it.
                row.Number.color = pinned ? Color.white : UIBuild.QuietInk;
            }
            row.Stamp.text = entry.Lines();
            row.Stamp.color = UIBuild.QuietInk;
            BindCounts(row, entry);
        }

        /// <summary>
        /// Frames the row that is the version on screen, dashes the same frame round
        /// the one it is compared with, and unframes the rest. The same red for both,
        /// they being two ends of one comparison: solid for the one you chose, dashed
        /// for the one that follows from it. The hover veil is the button's own colour
        /// transition and is not touched here.
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
        /// however long the bar carrying it is.
        ///
        /// A hundred pixels to the unit, which looks like the wrong number and is not:
        /// a tiled Image tiles at
        /// <c>sprite.pixelsPerUnit / canvas.referencePixelsPerUnit</c>, and a canvas's
        /// reference is 100. Created at 1 -- which reads as "actual size" -- every
        /// dash comes out a hundred times too long, and the frame is a solid line.
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
            // How big the machine is: a fact about the version rather than about any
            // comparison, so it is filled in even on the row with nothing to compare
            // against.
            if (row.Blocks != null)
            {
                row.Blocks.text = entry.Counted ? entry.BlockCount.ToString() : "·";
                row.Blocks.color = entry.Counted
                    ? DiffPalette.Ink(DiffPalette.Unchanged) : UIBuild.QuietInk;
            }

            // The oldest version is a special case only while the counts are what
            // each save did, there being no save before it. Against a pinned source it
            // is a comparison like any other.
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
                row.Added.color = UIBuild.QuietInk;
                row.Changed.color = UIBuild.QuietInk;
                row.Removed.color = UIBuild.QuietInk;
                return;
            }

            // A zero is a dash in the quiet ink rather than the column's own colour,
            // so "nothing removed" cannot be misread as a count with a minus sign.
            Fill(row.Added, entry.Added, "+", DiffPalette.Added);
            Fill(row.Changed, entry.Changed, "~", DiffPalette.Changed);
            Fill(row.Removed, entry.Removed, "-", DiffPalette.Removed);
        }

        private static void Fill(Text cell, int count, string sign, int category)
        {
            cell.text = count == 0 ? "–" : sign + count;
            cell.color = count == 0 ? UIBuild.QuietInk : DiffPalette.Ink(category);
        }

        /// <summary>
        /// Marks every heading with both arrows, the one in force lit and the other
        /// dimmed. One arrow on the sorted column says which column is sorted; a pair
        /// on every column also says what clicking the others would do.
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
                _headers[column].color = sorted ? Color.white : UIBuild.QuietInk;
            }
        }

        /// <summary>
        /// One arrow, lit or dimmed. Markup rather than the label's own colour: both
        /// arrows live in the same label as the heading.
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
                // Nothing is re-counted -- a row's counts are what that save did,
                // whatever order the list is in -- but the machine on screen is
                // compared with the row under it, and that is a different row now.
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
        /// Loads the chosen version and draws what it changed. The overlay has to wait
        /// for the load: <c>LoadMachineInfo</c> destroys and rebuilds every block, and
        /// the shells hang off the transform those blocks live under.
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
        /// machine. Separate from loading because the two sides of a diff can change
        /// without the machine changing: pinning re-answers the question about the
        /// version already on screen.
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

            // Nothing here cares that the two versions are next to each other, or
            // which way round they are in time: it is two machines, and a pinned base
            // is the same comparison with one side held still.
            DiffResult diff = BlockDiff.Compare(before, after);
            _ghosts.Show(diff);
            Say(Describe(diff) + "   vs " + against.Title() +
                (against == _base ? "  (pinned)" : string.Empty));
        }

        /// <summary>
        /// What a version is compared against: whatever the player pinned, or the row
        /// underneath it in the order the list is being shown in -- see
        /// <see cref="RowSort.Below"/>. A version pinned and then clicked is compared
        /// with itself, which is a real answer -- no change -- and needs no special
        /// case; the bottom row has nothing under it and comes back null.
        /// </summary>
        private VersionEntry Baseline(VersionEntry entry)
        {
            return _base != null ? _base : RowSort.Below(_versions, entry);
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

        // ----------------------------------------------------------------- counting

        /// <summary>
        /// Fills in every row's counts, oldest first, a file a frame: a folder can
        /// hold a hundred versions of a five-hundred-block machine, and doing them all
        /// at once is a visible freeze. Only two snapshots are held at a time.
        /// </summary>
        private IEnumerator CountEverything()
        {
            _counting = true;
            int pass = ++_countPass;

            // Oldest first, by number, which is the machine's own order and not the
            // one the list is showing: a row's counts are what that save did, fixed
            // once read, because a column that changed with the arrangement could not
            // be sorted by.
            List<VersionEntry> ordered = new List<VersionEntry>(_versions);
            RowSort.Apply(ordered, RowSort.ByNumber, true);

            // Held for the whole pass when a version is pinned: every row is then a
            // comparison with the same machine, read once instead of once per row.
            // Null when nothing is pinned, and `previous` walks the list instead.
            VersionEntry source = _base;
            MachineSnapshot fixedBase = source == null ? null : VersionScan.Read(source.Path);
            MachineSnapshot previous = null;

            for (int i = 0; i < ordered.Count; i++)
            {
                VersionEntry entry = ordered[i];
                Say("Reading history... " + (i + 1) + " of " + ordered.Count);
                yield return null;
                // Checked after the wait: a pin changed while this was asleep makes
                // every count it is about to write wrong, and a newer pass is already
                // writing the right ones.
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
            // been read yet. Now they are all in, the order asked for is applied.
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
        /// Counts the whole list again, because what the counts are measured from has
        /// changed. Rows go back to a dot until their new numbers arrive, the old ones
        /// being answers to a different question.
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
        /// Loads the thumbnails of the rows on screen and drops the rest. Besiege
        /// writes a 512x512 PNG per autosave, a megabyte once it is a texture, so a
        /// hundred-version folder would be a hundred megabytes held at once.
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
                // One fresh texture per call, loaded by us: ours to destroy.
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
