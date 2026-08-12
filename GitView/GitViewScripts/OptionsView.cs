using System;
using UnityEngine;
using UnityEngine.UI;

namespace GitView
{
    /// <summary>
    /// The options window: the four diff colours, each on a colour slider with its
    /// own opacity underneath.
    ///
    /// One window for all four rather than a panel per column heading, which is what
    /// this was. A colour is chosen against the other three -- green against orange
    /// against red over a brown machine -- and choosing them one at a time through
    /// four little panels that each covered the list was choosing them blind.
    ///
    /// The colour slider is Besiege's: a knob dragged along a picture of the colours
    /// it can choose. The game's own is <c>Selectors.ColourSliderSelector</c>, which
    /// keeps that picture in a private <c>Texture</c> and lives on the block mapper,
    /// so it cannot be borrowed for anything but a block -- but the widget is a
    /// slider with a strip drawn behind it, and UI Factory supplies the slider.
    /// </summary>
    public class OptionsView
    {
        /// <summary>
        /// Half the width it started at. A colour slider is chosen from by eye and
        /// then typed into if it has to be exact, so the strip only has to be long
        /// enough to pick a hue off -- and a window this size can sit beside the
        /// list it recolours rather than across it.
        /// </summary>
        private const float Width = 240f;
        private const float Pad = 12f;

        // A row is a heading with the colour slider under it, then the opacity
        // slider under that, and a gap before the next colour.
        private const float HeadHeight = 20f;
        private const float StripHeight = 26f;
        private const float ThinHeight = 20f;
        private const float RowGap = 4f;
        private const float BlockGap = 14f;
        private const float LabelWidth = 46f;

        /// <summary>
        /// The typed value beside a slider, and the gap before it.
        ///
        /// Wide enough for "#FF4C00" at the size it is written, which is what it is
        /// for: a box that clips its own contents to "#FF4C" is worse than no box,
        /// since a colour read off it would be the wrong colour.
        /// </summary>
        private const float ValueWidth = 64f;
        private const float BoxGap = 6f;
        private const int LabelSize = 12;

        /// <summary>
        /// What a typed value is written at: smaller than a label, because seven
        /// characters have to fit in a box beside a slider and the label beside it is
        /// four.
        /// </summary>
        private const int ValueSize = 10;

        /// <summary>How big this window's title is written. See <see cref="SetTitle"/>.</summary>
        private const int TitleSize = 14;

        /// <summary>How wide the strip is drawn before it is stretched over a slider.</summary>
        private const int StripPixels = 512;

        private static readonly Color QuietInk = new Color(0.72f, 0.72f, 0.74f, 1f);


        /// <summary>
        /// The order the colours are listed in: everything the save left alone
        /// first, then what it added, changed and removed.
        ///
        /// The machine's own colour before the three that mark what happened to it,
        /// and then those three in the order a save does them. It is not the order
        /// the columns are in -- that one has the counts in it, and unchanged blocks
        /// are not counted -- so it is written out rather than derived.
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

        public OptionsView(Action<int> changed)
        {
            _changed = changed;
        }

        /// <summary>
        /// Whether the player has this window open -- which is not the same as
        /// whether it is on screen, since it steps aside while the game has a menu
        /// up, exactly as the history window does.
        /// </summary>
        public bool Visible
        {
            get { return _wanted; }
        }

        private bool _wanted;

        /// <summary>Opens the window if it is shut and shuts it if it is open.</summary>
        public void Toggle(Transform parent)
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
        /// either way, so a menu opening and closing does not open a window they had
        /// shut -- or leave shut one they had open.
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

            SetTitle();
            HookCloseButton();

            // The prefab is a window with a list in it. There is no list here, and
            // its scroll view would eat every click that landed on the form.
            ScrollRect scroll = _window.GetComponentInChildren<ScrollRect>(true);
            if (scroll != null)
            {
                scroll.gameObject.SetActive(false);
            }

            float y = TopBarHeight() + Pad;
            for (int i = 0; i < Order.Length; i++)
            {
                y = BuildRow(Order[i], y);
            }

            _rect.sizeDelta = new Vector2(Width, y - BlockGap + Pad);
            _window.SetActive(false);
            return true;
        }

        /// <summary>
        /// One colour: its name, the strip it is chosen from, and how solid it is
        /// drawn. Returns where the next one starts.
        ///
        /// The name is centred over its own two sliders and has no swatch beside it.
        /// A swatch is a small square of the colour, sitting an inch from a strip
        /// with a knob on it that is the same answer at ten times the size: it said
        /// nothing the row was not already saying, and it was the only thing keeping
        /// the heading off centre.
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
        /// How tall a slider wants to be: whatever UI Factory authored its prefab
        /// as. Squashing it into a height of ours is what makes a Besiege widget stop
        /// looking like one -- the knob and the bar are drawn to each other's size,
        /// and neither of them is ours to re-proportion.
        /// </summary>
        private static float Tall(GameObject spawned)
        {
            float high = UIF.Rect(spawned).sizeDelta.y;
            return high >= 8f ? high : StripHeight;
        }

        /// <summary>
        /// How wide the knob is, which is how far the strip has to be inset for the
        /// knob to point at the colour under it. Falls back to the control's height,
        /// which is what a round knob on a bar usually is.
        /// </summary>
        private static float Knob(UnityEngine.UI.Slider slider, float high)
        {
            RectTransform handle = slider == null ? null : slider.handleRect;
            float wide = handle == null
                ? 0f : Mathf.Max(handle.rect.width, handle.sizeDelta.x);
            return wide > 1f ? wide : high;
        }

        /// <summary>
        /// The colour slider: a Besiege slider with the strip of colours drawn
        /// behind its knob.
        ///
        /// The strip goes on an image of our own rather than on the prefab's own
        /// background, for the reason every other picture in this mod does -- a UI
        /// Factory graphic can carry a CustomMaterialHandler, and a shader that does
        /// not sample what it is given cannot be given a picture. The fill goes off
        /// altogether: a bar that grows from the left is how a slider says "this
        /// much", and this slider says "this one".
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

            // Both of the prefab's own bars go. Besiege's colour slider is a knob on
            // a picture of the colours -- see ColourSliderSelector -- and neither a
            // dark track nor a bar filling in behind the knob is part of that. Left
            // in, the strip read as a sticker on an ordinary slider rather than as
            // the slider's own face.
            if (slider.fillRect != null)
            {
                slider.fillRect.gameObject.SetActive(false);
                slider.fillRect = null;
            }
            Image track = Background(slider);
            if (track != null)
            {
                track.gameObject.SetActive(false);
            }

            Image picture = UIBuild.AddImage(UIF.Rect(spawned), "Colours", Color.white);
            picture.sprite = StripSprite();
            picture.type = Image.Type.Simple;
            // The strip catches the pointer, because by now nothing else on the
            // slider does. A Slider is dragged through whatever graphic under it is
            // a raycast target, and turning off the two bars this prefab draws left
            // the whole control unclickable -- which is exactly what it looked like.
            picture.raycastTarget = true;
            // Inset by half a knob at each end. A slider's knob does not travel the
            // whole width of its track -- its centre stops half a knob short at both
            // ends -- so a strip drawn edge to edge would have the knob pointing at
            // the wrong colour by that much wherever it was near an end. Measured off
            // the knob rather than assumed to be the slider's height, which it is not:
            // this prefab's knob is about half as wide as the control is tall.
            UIF.Stretch(picture.rectTransform, Knob(slider, high) * 0.5f, high * 0.22f);
            // Behind the knob, which has to be on top of it and had been under it.
            // Putting the strip at the bottom of the pile rather than raising the
            // knob is the version that cannot be wrong: whether the knob is a child
            // of the slider or of a slide area inside it is the prefab's business,
            // and raising "the knob's parent" raised the whole slider when the knob
            // turned out to be a direct child -- which left the strip drawn over it
            // and the slider looking like it had no knob at all.
            picture.rectTransform.SetAsFirstSibling();

            slider.wholeNumbers = false;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = Along(DiffPalette.Of(category));

            int captured = category;
            slider.onValueChanged.AddListener(delegate(float moved)
            {
                OnColourMoved(captured, moved);
            });
            _colours[category] = slider;
            return high;
        }

        /// <summary>
        /// How solid the colour is drawn over the machine, on an ordinary slider
        /// with the number beside it. Nothing is picked from a strip here: it is one
        /// value between two ends, which is what a slider is for.
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
        /// as a whole percent.
        ///
        /// UI Factory's Input Field rather than a uGUI one of our own, because it
        /// carries <c>StopsHotkeysWhenInputFieldFocused</c> -- without it, typing
        /// "255" also fires whatever Besiege has bound to 2, 5 and 5. Missing it is
        /// survivable: the slider still works, so an absent prefab costs a line in
        /// the log rather than the row.
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
        /// fill nor the knob. Found by elimination rather than by name, since the
        /// names inside the prefab are UI Factory's to change.
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

        private static UnityEngine.UI.Slider Slid(GameObject spawned)
        {
            UnityEngine.UI.Slider slider = spawned.GetComponent<UnityEngine.UI.Slider>();
            return slider != null
                ? slider
                : spawned.GetComponentInChildren<UnityEngine.UI.Slider>(true);
        }

        private void SetTitle()
        {
            Transform bar = _window.transform.FindChild("TopBar");
            Text label = bar == null ? null : bar.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                // Written smaller than the history window's title, because it is a
                // longer title on a window a third the width: at the size the prefab
                // authors it, "BLOCK COLORS" is wider than the room between the two
                // marks in the bar, and a centred title that runs under them is not
                // centred in any useful sense.
                UIF.Style(label, TitleSize, TextAnchor.MiddleCenter);
                label.text = "BLOCK COLORS";
            }
        }

        /// <summary>
        /// The title bar's two controls: the cross that shuts the window, and the
        /// reload arrow that puts every colour back the way it started.
        ///
        /// Up here rather than a "RESET ALL" button across the bottom of the form,
        /// because it does not belong to any one row and it is not a fifth setting.
        /// It is the same mark as the reload button on this author's other mod, where
        /// it means the same thing.
        ///
        /// At the left-hand end, though, and not beside the cross: two marks in one
        /// corner of a bar this narrow left the title to whatever room was left over,
        /// and "BLOCK COLORS" is not a short title. One at each end gives it the
        /// middle back.
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
                UIF.OnClick(close.gameObject, Close);
            }
            UIBuild.BarButton(barRect, 0, "Reset", ResetSprite(), QuietInk, Reset,
                              true);
        }

        /// <summary>
        /// The reset arrow: the Clippy mod's reload mark, which sits in that mod's
        /// title bar meaning the same thing -- put it back the way it was.
        /// </summary>
        private static Sprite ResetSprite()
        {
            if (_reset == null)
            {
                _reset = HistoryView.Drawn(IconArt.Reload(IconPixels));
            }
            return _reset;
        }

        private static Sprite _reset;

        /// <summary>How big a title-bar mark is drawn before it is scaled to its button.</summary>
        private const int IconPixels = 64;

        /// <summary>
        /// How much of the top of the window its title bar takes, so the first row
        /// starts under it rather than behind it.
        /// </summary>
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
        /// the real one back rather than guessing -- a box left saying something
        /// that is not what the colour is would be worse than losing the edit.
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
            // Written back rather than left as typed, so "300" becomes 100 in front
            // of the player instead of silently.
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
            }
            _binding = false;
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
        /// The colour a knob at this place along the strip picks: the hue under it,
        /// at full strength.
        ///
        /// The strip is drawn pale and answers in full colour, which is what the
        /// game's own slider does -- see <see cref="IconArt.Strip"/>. Any hue rather
        /// than one of a list of stops: Besiege's is a smooth ramp and so is this.
        /// </summary>
        private static Color Picked(float along)
        {
            return IconArt.Hue(Mathf.Clamp01(along), 1f);
        }

        /// <summary>
        /// Where along the strip a colour sits, which is where the knob goes when
        /// the window is opened on a colour chosen before -- or written into the
        /// preferences file by hand. A grey has no hue and comes back at the left.
        /// </summary>
        private static float Along(Color colour)
        {
            return IconArt.HueOf(colour);
        }

        private static Sprite StripSprite()
        {
            if (_strip == null)
            {
                Texture2D drawn = IconArt.Strip(StripPixels, 8);
                drawn.hideFlags = HideFlags.HideAndDontSave;
                _strip = Sprite.Create(drawn, new Rect(0f, 0f, drawn.width, drawn.height),
                                       new Vector2(0.5f, 0.5f));
                _strip.hideFlags = HideFlags.HideAndDontSave;
            }
            return _strip;
        }

        // ------------------------------------------------------------------ layout

        /// <summary>
        /// Anchors a child to the window's top-left corner and sizes it in pixels.
        /// Everything here is laid out down a running y, so top-left is the only
        /// corner that stays still as the window grows.
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
                label.color = QuietInk;
                label.raycastTarget = false;
            }
            return label;
        }
    }
}
