using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace GitView
{
    /// <summary>
    /// The options window: the shell size, then the four diff colours, each on a
    /// colour slider with its own opacity underneath.
    ///
    /// One window for all four rather than a panel per column heading: a colour is
    /// chosen against the other three -- green against orange against red over a
    /// brown machine -- and four little panels that each covered the list was
    /// choosing them blind.
    ///
    /// The colour slider is Besiege's, a knob dragged along a picture of the colours
    /// it can choose. The game's own <c>Selectors.ColourSliderSelector</c> keeps that
    /// picture private and lives on the block mapper, but the widget is a slider with
    /// a strip behind it and UI Factory supplies the slider.
    /// </summary>
    public class OptionsView
    {
        /// <summary>
        /// Narrower than the window it belongs to, and wide enough to write on. A
        /// colour slider is chosen from by eye and typed into when it has to be
        /// exact, so what sets the width is the title, then the words on the left and
        /// the values on the right at a size worth reading.
        /// </summary>
        private const float Width = 384f;
        private const float Pad = 12f;

        // A row is a heading with the colour slider under it, then the opacity
        // slider under that, and a gap before the next colour.
        private const float HeadHeight = 24f;
        private const float StripHeight = 26f;
        private const float ThinHeight = 20f;
        private const float RowGap = 4f;
        private const float BlockGap = 14f;
        private const float LabelWidth = 52f;

        /// <summary>
        /// The typed value beside a slider, and the gap before it: wide enough for
        /// "#FF4C00" at the size it is written. A box that clips to "#FF4C" is worse
        /// than no box, since a colour read off it is the wrong colour.
        /// </summary>
        private const float ValueWidth = 84f;
        private const float BoxGap = 6f;
        /// <summary>
        /// What the words here are written at -- the size the list writes a timestamp
        /// at. This is a form to read rather than a table to scan.
        /// </summary>
        private const int LabelSize = 16;

        /// <summary>
        /// What a typed value is written at: smaller than a label, because seven
        /// characters have to fit in a box beside a slider.
        /// </summary>
        private const int ValueSize = 14;

        /// <summary>How wide the strip is drawn before it is stretched over a slider.</summary>
        private const int StripPixels = 512;

        /// <summary>
        /// The order the colours are listed in: the machine's own colour first, then
        /// the three that mark what happened to it. Not the order the columns are in
        /// -- that one has the counts in it -- so it is written out.
        /// </summary>
        private static readonly int[] Order =
        {
            DiffPalette.Unchanged, DiffPalette.Added, DiffPalette.Changed,
            DiffPalette.Removed
        };

        private static Sprite _strip;

        private readonly Action<int> _changed;
        private GameObject _window;
        private RectTransform _rect;

        // Fully qualified throughout this file: Besiege has a Slider of its own in
        // the global namespace, and it is the one an unqualified name finds.
        private readonly UnityEngine.UI.Slider[] _colours =
            new UnityEngine.UI.Slider[DiffPalette.Categories];
        private readonly UnityEngine.UI.Slider[] _opacities =
            new UnityEngine.UI.Slider[DiffPalette.Categories];

        /// <summary>Each colour slider's knob, which is drawn in the colour it picks.</summary>
        private readonly Image[] _knobs = new Image[DiffPalette.Categories];

        // The typed values beside the two sliders, as the game puts a "#FF4C00" and
        // a "1.50x" beside its own. A slider is a hundred-odd pixels for a whole
        // colour wheel: fine for choosing by eye, no use at all for matching a
        // colour to one written down somewhere else.
        private readonly InputField[] _hexes = new InputField[DiffPalette.Categories];
        private readonly InputField[] _percents =
            new InputField[DiffPalette.Categories];

        /// <summary>
        /// True while a slider is being written to rather than dragged, so what that
        /// raises is not mistaken for the player changing something.
        /// </summary>
        private bool _binding;

        public OptionsView(Action<int> changed, Action resized)
        {
            _changed = changed;
            _resized = resized;
        }

        private readonly Action _resized;

        /// <summary>
        /// Whether the player has this window open, which is not whether it is on
        /// screen: it steps aside while the game has a menu up.
        /// </summary>
        public bool Visible
        {
            get { return _wanted; }
        }

        private bool _wanted;

        /// <summary>This window's own rect, or null before it is built.</summary>
        public RectTransform Rect
        {
            get { return _rect; }
        }

        /// <summary>
        /// Opens the window if it is shut and shuts it if it is open, beside the
        /// window it belongs to.
        /// </summary>
        public void Toggle(Transform parent, RectTransform beside)
        {
            if (_wanted)
            {
                Close();
                return;
            }
            if (!Build(parent))
            {
                return;
            }
            Bind();
            _wanted = true;
            _window.SetActive(true);
            _window.transform.SetAsLastSibling();
            // Placed on opening rather than once when it is built, so that it comes
            // up next to the list wherever the list has been dragged to since.
            PlaceBeside(beside);
        }

        /// <summary>
        /// Puts this window just off the right-hand edge of another, tops level --
        /// they are two halves of one thing, and both are the same prefab, so lining
        /// up their rects lines up what is drawn in them.
        ///
        /// Off the other window's four corners, and deliberately not off the box its
        /// contents occupy: a scrolling list's content is as tall as the list is long,
        /// so a hundred versions put "the top of the window" most of a screen too high.
        /// </summary>
        private void PlaceBeside(RectTransform other)
        {
            RectTransform space = _rect == null ? null : _rect.parent as RectTransform;
            if (other == null || space == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            Vector3[] corners = new Vector3[4];
            other.GetWorldCorners(corners);
            // 1 is the top-left corner and 2 the top-right, in world space, which on
            // a screen-space canvas is the space its children are laid out in.
            Vector3 topRight = space.InverseTransformPoint(corners[2]);

            _rect.anchorMin = new Vector2(0.5f, 0.5f);
            _rect.anchorMax = new Vector2(0.5f, 0.5f);
            _rect.pivot = new Vector2(0f, 1f);

            float x = topRight.x + WindowGap;
            float y = topRight.y;
            // Pulled back inside the screen if the list has been dragged far enough
            // right that there is no room beside it: over the list is better than off
            // the edge of the world.
            x = Mathf.Min(x, space.rect.xMax - _rect.sizeDelta.x);
            _rect.anchoredPosition = new Vector2(x, y) - space.rect.center;
        }

        /// <summary>How far this window sits from the one it belongs to.</summary>
        private const float WindowGap = 12f;

        /// <summary>
        /// Pulls this window back if it has been dragged off the screen -- the same
        /// problem and the same answer as <see cref="HistoryView.KeepOnScreen"/>.
        /// </summary>
        public void KeepInside()
        {
            if (_wanted && _window != null && _window.activeSelf)
            {
                HistoryView.KeepOnScreen(_rect);
            }
        }

        public void Close()
        {
            _wanted = false;
            if (_window != null)
            {
                _window.SetActive(false);
            }
        }

        /// <summary>
        /// Shows or hides the window for a reason that is not the player's: the game
        /// has put a menu up, or taken one down. What they asked for is remembered
        /// either way, so a menu cannot open a window they had shut.
        /// </summary>
        public void Allow(bool allowed)
        {
            bool showing = _wanted && allowed;
            if (_window != null && _window.activeSelf != showing)
            {
                _window.SetActive(showing);
            }
        }

        // ----------------------------------------------------------------- building

        private bool Build(Transform parent)
        {
            if (_window != null)
            {
                return true;
            }

            _window = UIF.Spawn(UIF.WindowPrefab, parent);
            if (_window == null)
            {
                Log.Warn("UI Factory could not supply a window, so the options cannot " +
                         "be shown.");
                return false;
            }
            _window.name = "GitView Options";
            _rect = UIF.Rect(_window);

            // Middle of the screen, and its own size: the window prefab is authored
            // for a list and this is a form.
            _rect.anchorMin = new Vector2(0.5f, 0.5f);
            _rect.anchorMax = new Vector2(0.5f, 0.5f);
            _rect.pivot = new Vector2(0.5f, 0.5f);
            _rect.anchoredPosition = Vector2.zero;

            BuildTitleBar();

            // The prefab is a window with a list in it. There is no list here, and
            // its scroll view would eat every click that landed on the form.
            ScrollRect scroll = _window.GetComponentInChildren<ScrollRect>(true);
            if (scroll != null)
            {
                scroll.gameObject.SetActive(false);
            }

            float y = TopBarHeight() + Pad;
            y = BuildShell(y);
            for (int i = 0; i < Order.Length; i++)
            {
                y = BuildRow(Order[i], y);
            }

            _rect.sizeDelta = new Vector2(Width, y - BlockGap + Pad);
            _window.SetActive(false);
            return true;
        }

        /// <summary>
        /// How much larger than its block each coloured shell is drawn -- at the top,
        /// because it is about all four colours rather than any one of them.
        ///
        /// A shell at exactly the block's size shares its surface, which is a fight
        /// the graphics card settles pixel by pixel. A little larger is a coat of
        /// paint; larger still is a marker you can pick out of a crowded machine from
        /// across the build area, and which of those a player wants is theirs to say.
        /// </summary>
        private float BuildShell(float y)
        {
            GameObject spawned = UIF.Spawn(UIF.SliderPrefab, _rect);
            if (spawned == null)
            {
                return y;
            }
            spawned.name = "Shell size";
            float high = Tall(spawned);

            Text label = Caption("SIZE", TextAnchor.MiddleLeft);
            if (label != null)
            {
                Place(label.rectTransform, Pad, y, LabelWidth, high);
                label.color = Color.white;
            }

            float left = Pad + LabelWidth;
            float right = Width - Pad - ValueWidth;
            Place(UIF.Rect(spawned), left, y, right - BoxGap - left, high);

            UnityEngine.UI.Slider slider = Slid(spawned);
            if (slider == null)
            {
                UnityEngine.Object.Destroy(spawned);
                return y + high + BlockGap;
            }
            slider.wholeNumbers = false;
            slider.minValue = DiffPalette.ShellSlideLeast;
            slider.maxValue = DiffPalette.ShellSlideMost;
            slider.value = Mathf.Clamp(DiffPalette.Shell, slider.minValue,
                                       slider.maxValue);

            _shellBox = Box(DiffPalette.Unchanged, right, y, high, 4, false);
            if (_shellBox != null)
            {
                // Not one of the colour boxes: a multiplier written as itself, taking
                // values from outside what the slider will slide to, so it needs its
                // own validation and handler.
                _shellBox.characterValidation = InputField.CharacterValidation.Decimal;
                _shellBox.onEndEdit.RemoveAllListeners();
                _shellBox.onEndEdit.AddListener(delegate(string typed)
                {
                    OnShellTyped(typed);
                });
            }
            _shell = slider;
            ShowShell(slider.value);

            slider.onValueChanged.AddListener(delegate(float moved)
            {
                OnShellMoved(moved);
            });
            return y + high + BlockGap;
        }

        private UnityEngine.UI.Slider _shell;
        private InputField _shellBox;

        private void OnShellMoved(float swell)
        {
            if (_binding)
            {
                return;
            }
            DiffPalette.SetShell(swell);
            ShowShell(swell);
            if (_resized != null)
            {
                _resized();
            }
        }

        /// <summary>
        /// Takes a typed size, as a multiple of the block's own. Anything unreadable
        /// puts the real one back rather than guessing.
        /// </summary>
        private void OnShellTyped(string typed)
        {
            if (_binding)
            {
                return;
            }
            float swell;
            if (!float.TryParse(typed == null ? string.Empty : typed.Trim(),
                                NumberStyles.Float, CultureInfo.InvariantCulture,
                                out swell))
            {
                ShowShell(DiffPalette.Shell);
                return;
            }
            DiffPalette.SetShell(swell);
            // The slider only slides through the middle of what may be typed, so a
            // number outside its range leaves it at whichever end it stopped at --
            // which is honest: the knob is as far that way as it goes.
            Slide(_shell, DiffPalette.Shell);
            ShowShell(DiffPalette.Shell);
            if (_resized != null)
            {
                _resized();
            }
        }

        private void ShowShell(float swell)
        {
            if (_shellBox == null)
            {
                return;
            }
            _binding = true;
            // Invariant culture: a size typed as "1.15" has to come back as "1.15"
            // wherever the player's machine thinks the decimal point goes.
            _shellBox.text = swell.ToString("0.00", CultureInfo.InvariantCulture);
            _binding = false;
        }

        /// <summary>
        /// One colour: its name, the strip it is chosen from, and how solid it is
        /// drawn. Returns where the next one starts. The name is centred over its own
        /// two sliders, with no swatch beside it -- a small square of the colour an
        /// inch from a knob wearing the same colour said nothing twice.
        /// </summary>
        private float BuildRow(int category, float y)
        {
            Text name = Caption(DiffPalette.Name(category), TextAnchor.MiddleCenter);
            if (name != null)
            {
                Place(name.rectTransform, Pad, y, Width - Pad * 2f, HeadHeight);
                name.color = Color.white;
            }
            y += HeadHeight + RowGap;
            y += Strip(category, y) + RowGap;
            y += Opacity(category, y) + BlockGap;
            return y;
        }

        /// <summary>
        /// How tall a slider wants to be: whatever UI Factory authored it as. The
        /// knob and the bar are drawn to each other's size, and neither is ours to
        /// re-proportion.
        /// </summary>
        private static float Tall(GameObject spawned)
        {
            float high = UIF.Rect(spawned).sizeDelta.y;
            return high >= 8f ? high : StripHeight;
        }

        /// <summary>
        /// How wide the knob is, which is how far the strip is inset for the knob to
        /// point at the colour under it. Falls back to the control's height.
        /// </summary>
        private static float Knob(UnityEngine.UI.Slider slider, float high)
        {
            RectTransform handle = slider == null ? null : slider.handleRect;
            float wide = handle == null
                ? 0f : Mathf.Max(handle.rect.width, handle.sizeDelta.x);
            return wide > 1f ? wide : high;
        }

        /// <summary>The picture the knob is drawn with, which is what wears the colour.</summary>
        private static Image KnobFace(UnityEngine.UI.Slider slider)
        {
            RectTransform handle = slider == null ? null : slider.handleRect;
            if (handle == null)
            {
                return null;
            }
            Image face = handle.GetComponent<Image>();
            return face != null ? face : handle.GetComponentInChildren<Image>(true);
        }

        /// <summary>Puts the chosen colour on the knob, at full strength.</summary>
        private void Wear(int category)
        {
            if (_knobs[category] != null)
            {
                // Ink rather than the colour itself: a knob faded to nothing cannot
                // say what is being faded.
                _knobs[category].color = DiffPalette.Ink(category);
            }
        }

        /// <summary>
        /// The colour slider: a Besiege slider with the strip of colours drawn behind
        /// its knob. The strip goes on an image of ours rather than the prefab's
        /// background -- a UI Factory graphic can carry a CustomMaterialHandler, and
        /// a shader that ignores what it is given cannot be given a picture. The fill
        /// goes off: a bar growing from the left says "this much", and this slider
        /// says "this one".
        /// </summary>
        private float Strip(int category, float y)
        {
            GameObject spawned = UIF.Spawn(UIF.SliderPrefab, _rect);
            if (spawned == null)
            {
                Log.Warn("UI Factory could not supply a slider, so the " +
                         DiffPalette.Name(category) + " colour cannot be changed.");
                return StripHeight;
            }
            spawned.name = DiffPalette.Name(category) + " colour";
            float high = Tall(spawned);
            float boxLeft = Width - Pad - ValueWidth;
            Place(UIF.Rect(spawned), Pad, y, boxLeft - BoxGap - Pad, high);
            _hexes[category] = Box(category, boxLeft, y, high, 7, true);

            // Fully qualified: Besiege has a Slider of its own in the global
            // namespace, and it is the one an unqualified name finds.
            UnityEngine.UI.Slider slider = Slid(spawned);
            if (slider == null)
            {
                UnityEngine.Object.Destroy(spawned);
                return high;
            }

            // Both of the prefab's bars go: Besiege's colour slider is a knob on a
            // picture of the colours and nothing else, and left in they read as a
            // sticker on an ordinary slider.
            //
            // Switched off a graphic at a time rather than by deactivating the objects
            // they are on: this prefab keeps the handle *inside* the bar, so turning
            // the bar off took the knob with it. A disabled Image draws nothing and
            // its children carry on. The bar is found before the fill is let go of,
            // since which is which is worked out by elimination.
            Image track = Background(slider);
            Hide(slider.fillRect, slider.handleRect);
            slider.fillRect = null;
            if (track != null)
            {
                track.enabled = false;
            }

            Image picture = UIBuild.AddImage(UIF.Rect(spawned), "Colours", Color.white);
            picture.sprite = StripSprite();
            picture.type = Image.Type.Simple;
            // The strip catches the pointer, because by now nothing else on the
            // slider does. A Slider is dragged through whatever graphic under it is
            // a raycast target, and turning off the two bars this prefab draws left
            // the whole control unclickable -- which is exactly what it looked like.
            picture.raycastTarget = true;
            // Inset by half a knob at each end: a knob's centre stops half a knob
            // short of both ends of its track, so a strip drawn edge to edge points at
            // the wrong colour near either end. Measured off the knob rather than
            // assumed to be the control's height, which it is not.
            UIF.Stretch(picture.rectTransform, Knob(slider, high) * 0.5f, high * 0.22f);
            // Behind the knob. The strip goes to the bottom of the pile rather than
            // the knob to the top: whether the knob is a child of the slider or of a
            // slide area inside it is the prefab's business, and raising "the knob's
            // parent" raises the whole slider when it turns out to be a direct child.
            picture.rectTransform.SetAsFirstSibling();

            slider.wholeNumbers = false;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = Along(DiffPalette.Of(category));

            // The knob wears the colour it is pointing at, as Besiege's own does:
            // the strip is drawn pale and hands back full strength, so the knob is the
            // only thing in the window showing the colour as it will be used.
            _knobs[category] = KnobFace(slider);
            Wear(category);

            int captured = category;
            slider.onValueChanged.AddListener(delegate(float moved)
            {
                OnColourMoved(captured, moved);
            });
            _colours[category] = slider;
            return high;
        }

        /// <summary>
        /// How solid the colour is drawn over the machine: one value between two
        /// ends, so an ordinary slider with the number beside it.
        /// </summary>
        private float Opacity(int category, float y)
        {
            GameObject spawned = UIF.Spawn(UIF.SliderPrefab, _rect);
            if (spawned == null)
            {
                return ThinHeight;
            }
            spawned.name = DiffPalette.Name(category) + " opacity";
            float high = Tall(spawned);

            // Shortened with the window: "OPACITY" does not fit beside a slider in
            // half the room, and the row under a colour can only be one thing.
            Text label = Caption("FADE", TextAnchor.MiddleLeft);
            if (label != null)
            {
                Place(label.rectTransform, Pad, y, LabelWidth, high);
            }

            float left = Pad + LabelWidth;
            float right = Width - Pad - ValueWidth;
            Place(UIF.Rect(spawned), left, y, right - BoxGap - left, high);

            UnityEngine.UI.Slider slider = Slid(spawned);
            if (slider == null)
            {
                UnityEngine.Object.Destroy(spawned);
                return high;
            }
            slider.wholeNumbers = false;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = DiffPalette.Of(category).a;

            _percents[category] = Box(category, right, y, high, 3, false);
            ShowPercent(category, slider.value);

            int captured = category;
            slider.onValueChanged.AddListener(delegate(float moved)
            {
                OnOpacityMoved(captured, moved);
            });
            _opacities[category] = slider;
            return high;
        }

        /// <summary>
        /// The typed value beside a slider: the colour as "#RRGGBB", or the opacity
        /// as a whole percent. UI Factory's Input Field rather than a uGUI one of our
        /// own, for <c>StopsHotkeysWhenInputFieldFocused</c> -- without it, typing
        /// "255" also fires whatever Besiege has bound to 2, 5 and 5.
        /// </summary>
        private InputField Box(int category, float x, float y, float high, int limit,
                               bool hex)
        {
            GameObject spawned = UIF.Spawn(UIF.InputPrefab, _rect);
            if (spawned == null)
            {
                Log.Warn("UI Factory could not supply a text box, so the " +
                         DiffPalette.Name(category) +
                         " colour can only be dragged.");
                return null;
            }
            spawned.name = DiffPalette.Name(category) + (hex ? " hex" : " percent");
            Place(UIF.Rect(spawned), x, y, ValueWidth, high);

            InputField box = spawned.GetComponent<InputField>();
            if (box == null)
            {
                box = spawned.GetComponentInChildren<InputField>(true);
            }
            if (box == null)
            {
                UnityEngine.Object.Destroy(spawned);
                return null;
            }

            box.characterValidation = hex
                ? InputField.CharacterValidation.None
                : InputField.CharacterValidation.Integer;
            box.lineType = InputField.LineType.SingleLine;
            box.characterLimit = limit;
            if (box.textComponent != null)
            {
                box.textComponent.alignment = TextAnchor.MiddleCenter;
                box.textComponent.fontSize = ValueSize;
                box.textComponent.resizeTextForBestFit = false;
            }

            int captured = category;
            bool asHex = hex;
            // onEndEdit rather than onValueChanged: committing every keystroke would
            // apply the "F" of an "#FF4C00" and drag the knob away underneath.
            box.onEndEdit.AddListener(delegate(string typed)
            {
                if (asHex)
                {
                    OnHexTyped(captured, typed);
                }
                else
                {
                    OnPercentTyped(captured, typed);
                }
            });
            return box;
        }

        /// <summary>
        /// The bar a slider's knob runs along: whatever it draws that is neither the
        /// fill nor the knob. By elimination, since the names inside the prefab are
        /// UI Factory's to change.
        /// </summary>
        private static Image Background(UnityEngine.UI.Slider slider)
        {
            Image[] images = slider.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Transform at = images[i].transform;
                if (Under(at, slider.fillRect) || Under(at, slider.handleRect) ||
                    images[i].name == "Colours")
                {
                    continue;
                }
                return images[i];
            }
            return null;
        }

        private static bool Under(Transform child, RectTransform parent)
        {
            return parent != null && (child == parent || child.IsChildOf(parent));
        }

        /// <summary>
        /// Stops something drawing without taking it out of the hierarchy, leaving
        /// whatever is under <paramref name="keep"/> alone. A disabled Graphic draws
        /// nothing and changes nothing else; deactivating its object would take its
        /// children with it.
        /// </summary>
        private static void Hide(RectTransform rect, RectTransform keep)
        {
            if (rect == null)
            {
                return;
            }
            Graphic[] drawn = rect.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < drawn.Length; i++)
            {
                if (drawn[i] != null && !Under(drawn[i].transform, keep))
                {
                    drawn[i].enabled = false;
                }
            }
        }

        private static UnityEngine.UI.Slider Slid(GameObject spawned)
        {
            UnityEngine.UI.Slider slider = spawned.GetComponent<UnityEngine.UI.Slider>();
            return slider != null
                ? slider
                : spawned.GetComponentInChildren<UnityEngine.UI.Slider>(true);
        }

        /// <summary>
        /// The title bar: its name, the cross that shuts the window, and the reload
        /// arrow that puts every colour back.
        ///
        /// The arrow is up here rather than a "RESET ALL" across the bottom of the
        /// form: it belongs to no one row and is not a fifth setting. At the left-hand
        /// end and not beside the cross, because two marks in one corner of a bar this
        /// narrow leave the title whatever room is left over.
        /// </summary>
        private void BuildTitleBar()
        {
            RectTransform bar = UIBuild.TitleBar(_window);
            if (bar == null)
            {
                return;
            }
            // At the size the prefab authors it, which is the size the history
            // window's own title is written at -- both are asking for the same thing,
            // so neither passes a number. It is what sets this window's width: the
            // title has to fit between the two marks in its bar.
            UIBuild.SetTitle(bar, "BLOCK COLORS");
            UIBuild.HookClose(bar, Close);
            UIBuild.BarButton(bar, 0, "Reset", ResetSprite(), UIBuild.QuietInk, Reset,
                              true);
        }

        /// <summary>
        /// The reset arrow: the Clippy mod's reload mark, which means the same thing
        /// in that mod's title bar -- put it back the way it was.
        /// </summary>
        private static Sprite ResetSprite()
        {
            if (_reset == null)
            {
                _reset = UIBuild.Drawn(IconArt.Reload(IconPixels));
            }
            return _reset;
        }

        private static Sprite _reset;

        /// <summary>How big a title-bar mark is drawn before it is scaled to its button.</summary>
        private const int IconPixels = 64;

        /// <summary>How much of the window the title bar takes off the top.</summary>
        private float TopBarHeight()
        {
            RectTransform bar = _window.transform.FindChild("TopBar") as RectTransform;
            return bar == null ? 30f : Mathf.Max(24f, bar.rect.height);
        }

        // ---------------------------------------------------------------- changing

        private void OnColourMoved(int category, float along)
        {
            if (_binding)
            {
                return;
            }
            Color colour = Picked(along);
            colour.a = DiffPalette.Of(category).a;
            Apply(category, colour);
            ShowHex(category, colour);
        }

        private void OnOpacityMoved(int category, float value)
        {
            if (_binding)
            {
                return;
            }
            Color colour = DiffPalette.Of(category);
            colour.a = value;
            Apply(category, colour);
            ShowPercent(category, value);
        }

        /// <summary>
        /// Takes a typed colour, as "#RRGGBB" or "RRGGBB". Anything unreadable puts
        /// the real one back: a box saying something that is not the colour would be
        /// worse than losing the edit.
        /// </summary>
        private void OnHexTyped(int category, string typed)
        {
            if (_binding)
            {
                return;
            }
            Color colour;
            if (!ReadHex(typed, out colour))
            {
                Bind();
                return;
            }
            colour.a = DiffPalette.Of(category).a;
            Apply(category, colour);
            // The knob goes to the nearest place on the strip, which for a colour
            // typed at full strength is exactly its own hue.
            Slide(_colours[category], Along(colour));
            ShowHex(category, colour);
        }

        private void OnPercentTyped(int category, string typed)
        {
            if (_binding)
            {
                return;
            }
            int percent;
            if (!int.TryParse(typed == null ? string.Empty : typed.Trim(), out percent))
            {
                Bind();
                return;
            }

            Color colour = DiffPalette.Of(category);
            colour.a = Mathf.Clamp01(percent / 100f);
            Apply(category, colour);
            Slide(_opacities[category], colour.a);
            // Written back, so "300" becomes 100 in front of the player.
            ShowPercent(category, colour.a);
        }

        /// <summary>Reads "#RRGGBB", with or without the hash.</summary>
        private static bool ReadHex(string typed, out Color colour)
        {
            colour = Color.white;
            string text = typed == null ? string.Empty : typed.Trim();
            if (text.StartsWith("#"))
            {
                text = text.Substring(1);
            }
            if (text.Length != 6)
            {
                return false;
            }

            int[] parts = new int[3];
            for (int i = 0; i < 3; i++)
            {
                if (!Nibble(text[i * 2], ref parts[i]) ||
                    !Nibble(text[i * 2 + 1], ref parts[i]))
                {
                    return false;
                }
            }
            colour = new Color(parts[0] / 255f, parts[1] / 255f, parts[2] / 255f, 1f);
            return true;
        }

        /// <summary>Shifts one hex digit into a byte being read, or fails.</summary>
        private static bool Nibble(char c, ref int value)
        {
            int digit;
            if (c >= '0' && c <= '9') { digit = c - '0'; }
            else if (c >= 'a' && c <= 'f') { digit = c - 'a' + 10; }
            else if (c >= 'A' && c <= 'F') { digit = c - 'A' + 10; }
            else { return false; }
            value = ((value << 4) | digit) & 0xFF;
            return true;
        }

        /// <summary>Moves a slider without that counting as a drag.</summary>
        private void Slide(UnityEngine.UI.Slider slider, float value)
        {
            if (slider == null)
            {
                return;
            }
            _binding = true;
            slider.value = value;
            _binding = false;
        }

        private void Apply(int category, Color colour)
        {
            DiffPalette.Set(category, colour);
            Wear(category);
            if (_changed != null)
            {
                _changed(category);
            }
        }

        private void Reset()
        {
            for (int category = 0; category < DiffPalette.Categories; category++)
            {
                DiffPalette.Set(category, DiffPalette.Default(category));
                if (_changed != null)
                {
                    _changed(category);
                }
            }
            DiffPalette.SetShell(DiffPalette.DefaultShell);
            if (_resized != null)
            {
                _resized();
            }
            Bind();
        }

        /// <summary>Puts the palette back onto every slider and swatch.</summary>
        private void Bind()
        {
            _binding = true;
            for (int category = 0; category < DiffPalette.Categories; category++)
            {
                Color colour = DiffPalette.Of(category);
                if (_colours[category] != null)
                {
                    _colours[category].value = Along(colour);
                }
                if (_opacities[category] != null)
                {
                    _opacities[category].value = colour.a;
                }
                ShowPercent(category, colour.a);
                ShowHex(category, colour);
                Wear(category);
            }
            if (_shell != null)
            {
                _shell.value = DiffPalette.Shell;
            }
            _binding = false;
            ShowShell(DiffPalette.Shell);
        }

        /// <summary>Writes a value into its box without that counting as an edit.</summary>
        private void ShowPercent(int category, float value)
        {
            if (_percents[category] == null)
            {
                return;
            }
            _binding = true;
            _percents[category].text = Mathf.RoundToInt(value * 100f).ToString();
            _binding = false;
        }

        private void ShowHex(int category, Color colour)
        {
            if (_hexes[category] == null)
            {
                return;
            }
            _binding = true;
            _hexes[category].text = "#" + Hex(colour.r) + Hex(colour.g) + Hex(colour.b);
            _binding = false;
        }

        private static string Hex(float channel)
        {
            const string digits = "0123456789ABCDEF";
            int value = Mathf.Clamp(Mathf.RoundToInt(channel * 255f), 0, 255);
            return digits[value >> 4].ToString() + digits[value & 0xF].ToString();
        }

        // ------------------------------------------------------------- the colours

        /// <summary>
        /// The colour a knob at this place along the strip picks: the hue under it, at
        /// full strength. The strip is drawn pale and answers in full colour, as the
        /// game's own does -- see <see cref="IconArt.Strip"/>.
        /// </summary>
        private static Color Picked(float along)
        {
            return IconArt.Hue(Mathf.Clamp01(along), 1f);
        }

        /// <summary>
        /// Where along the strip a colour sits: where the knob goes when the window
        /// opens. A grey has no hue and comes back at the left.
        /// </summary>
        private static float Along(Color colour)
        {
            return IconArt.HueOf(colour);
        }

        private static Sprite StripSprite()
        {
            if (_strip == null)
            {
                _strip = UIBuild.Drawn(IconArt.Strip(StripPixels, 8));
            }
            return _strip;
        }

        // ------------------------------------------------------------------ layout

        /// <summary>
        /// Anchors a child to the window's top-left corner and sizes it in pixels:
        /// everything here is laid out down a running y, and that is the only corner
        /// that stays still as the window grows.
        /// </summary>
        private static void Place(RectTransform rect, float x, float y,
                                  float width, float height)
        {
            if (rect == null)
            {
                return;
            }
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, -y);
        }

        private Text Caption(string text, TextAnchor alignment)
        {
            GameObject spawned = UIF.Spawn(UIF.TextPrefab, _rect);
            if (spawned == null)
            {
                return UIBuild.AddText(_rect, text, LabelSize, alignment);
            }
            Text label = UIF.Label(spawned, LabelSize, alignment);
            if (label != null)
            {
                label.text = text;
                label.color = UIBuild.QuietInk;
                label.raycastTarget = false;
            }
            return label;
        }
    }
}
