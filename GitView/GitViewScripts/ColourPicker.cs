using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace GitView
{
    /// <summary>
    /// A small panel for choosing one of the diff's four colours: red, green,
    /// blue, and an opacity that only the shells over the machine use.
    ///
    /// Built out of UI Factory's Panel and Slider rather than found ready-made --
    /// UIFactory ships nineteen prefabs and a colour picker is not among them, and
    /// Besiege's own is the block mapper's paint selector, which lives behind
    /// <c>InternalModding</c> and cannot be opened for anything but a block. Four
    /// sliders is not much of a picker, but it is made of the game's own widgets,
    /// which is the thing that matters here.
    ///
    /// Nothing is applied on a confirmation: the sliders drive the colour as they
    /// move, so the list and the machine both recolour under the pointer. That is
    /// the only way to choose a colour for something as particular as a translucent
    /// shell over a brown machine against a blue sky.
    /// </summary>
    public class ColourPicker
    {
        private const float Width = 320f;
        private const float Pad = 10f;
        private const float TitleHeight = 22f;
        private const float RowHeight = 28f;
        private const float RowGap = 2f;
        private const float LabelWidth = 68f;
        private const float SwatchSize = 20f;
        private const float ResetHeight = 24f;
        private const int LabelSize = 12;

        /// <summary>The typed value beside each slider, and the gap before it.</summary>
        private const float BoxWidth = 58f;
        private const float BoxGap = 6f;

        /// <summary>
        /// What a channel reads as in the box: 0-255 for the three colours, the
        /// numbers a colour is quoted in everywhere else, and 0-100 for the opacity
        /// because a percentage is what an opacity is.
        /// </summary>
        private static readonly float[] ChannelScales = { 255f, 255f, 255f, 100f };

        /// <summary>How far below the swatch that opened it the panel sits.</summary>
        private const float DropGap = 4f;

        private static readonly string[] ChannelNames = { "RED", "GREEN", "BLUE", "OPACITY %" };

        /// <summary>
        /// What the panel is filled with before anything is drawn on it.
        ///
        /// UI Factory's Panel is translucent, which is right for a panel with a
        /// window behind it and wrong for one hanging over a list: the rows showed
        /// through it, so the numbers in the column underneath ran into the labels
        /// on top of it and a selected row turned the whole picker red. Darker than
        /// the window it sits on, so it reads as being in front rather than as part
        /// of it, and just short of opaque so it is still Besiege's translucent
        /// interface rather than a solid box stuck on top of one.
        /// </summary>
        private static readonly Color Backing = new Color(0.043f, 0.055f, 0.075f, 0.97f);

        private readonly int _category;
        private readonly Action _changed;
        private GameObject _panel;
        private RectTransform _rect;
        private Image _preview;
        private readonly UnityEngine.UI.Slider[] _sliders = new UnityEngine.UI.Slider[4];
        private readonly UnityEngine.UI.InputField[] _boxes = new UnityEngine.UI.InputField[4];

        /// <summary>
        /// True while a slider or a box is being written to rather than used, so
        /// the callbacks that raises are not mistaken for the player changing
        /// something -- and, worse, fed back into the control that raised them.
        /// </summary>
        private bool _binding;

        public ColourPicker(int category, Action changed)
        {
            _category = category;
            _changed = changed;
        }

        public bool Visible
        {
            get { return _panel != null && _panel.activeSelf; }
        }

        /// <summary>
        /// Builds the panel, hidden, as a child of the window. Late-built rather
        /// than built with the header: most players never open one, and this way a
        /// UIFactory that cannot supply a Slider costs a log line at the moment it
        /// is asked rather than a broken header.
        /// </summary>
        private bool Build(Transform parent)
        {
            if (_panel != null)
            {
                return true;
            }

            _panel = UIF.Spawn(UIF.PanelPrefab, parent);
            if (_panel == null)
            {
                return false;
            }
            _panel.name = "GitView Colour " + DiffPalette.Name(_category);
            _rect = UIF.Rect(_panel);

            // First sibling, so it fills the panel behind everything -- including
            // whatever frame the prefab draws with children of its own, which stays
            // on top of it. Left as a raycast target on purpose: without something
            // solid under the pointer, a click on the panel's own background falls
            // through to the row behind it and loads a version.
            Image backing = UIBuild.AddImage(_rect, "Backing", Backing);
            UIF.Stretch(backing.rectTransform, 0f, 0f);
            backing.transform.SetAsFirstSibling();

            float y = Pad;

            _preview = UIBuild.AddImage(_rect, "Preview", DiffPalette.Ink(_category));
            Place(_preview.rectTransform, Pad, y + (TitleHeight - SwatchSize) * 0.5f,
                  SwatchSize, SwatchSize);

            Text title = Caption(DiffPalette.Name(_category), TextAnchor.MiddleLeft);
            if (title != null)
            {
                Place(title.rectTransform, Pad + SwatchSize + 8f, y,
                      Width - Pad * 2f - SwatchSize - 8f, TitleHeight);
                title.color = Color.white;
            }

            y += TitleHeight + RowGap * 2f;

            Color colour = DiffPalette.Of(_category);
            float[] values = { colour.r, colour.g, colour.b, colour.a };
            for (int channel = 0; channel < _sliders.Length; channel++)
            {
                if (!BuildChannel(channel, values[channel], y))
                {
                    Log.Warn("UI Factory could not supply a slider, so the " +
                             DiffPalette.Name(_category) + " colour cannot be changed.");
                    UnityEngine.Object.Destroy(_panel);
                    _panel = null;
                    return false;
                }
                y += RowHeight + RowGap;
            }

            y += RowGap;
            GameObject reset = UIF.Spawn(UIF.ButtonPrefab, _rect);
            if (reset != null)
            {
                Place(UIF.Rect(reset), Pad, y, Width - Pad * 2f, ResetHeight);
                Text label = UIF.Caption(reset, "RESET", LabelSize, TextAnchor.MiddleCenter);
                if (label != null)
                {
                    UIF.StretchInset(label.rectTransform, 0f, 0f, 0f);
                }
                // Left to swell like any other Besiege button: it is button-sized,
                // and its label is centred, so nothing moves that should not.
                UIF.OnClick(reset, Reset);
                y += ResetHeight;
            }

            _rect.sizeDelta = new Vector2(Width, y + Pad);
            _panel.SetActive(false);
            return true;
        }

        private bool BuildChannel(int channel, float value, float y)
        {
            Text label = Caption(ChannelNames[channel], TextAnchor.MiddleLeft);
            if (label != null)
            {
                Place(label.rectTransform, Pad, y, LabelWidth, RowHeight);
            }

            GameObject spawned = UIF.Spawn(UIF.SliderPrefab, _rect);
            if (spawned == null)
            {
                return false;
            }
            float sliderLeft = Pad + LabelWidth;
            float boxLeft = Width - Pad - BoxWidth;
            Place(UIF.Rect(spawned), sliderLeft, y, boxLeft - BoxGap - sliderLeft, RowHeight);

            // Fully qualified throughout: Besiege has a Slider of its own in the
            // global namespace, and it is the one an unqualified name finds.
            UnityEngine.UI.Slider slider = spawned.GetComponent<UnityEngine.UI.Slider>();
            if (slider == null)
            {
                slider = spawned.GetComponentInChildren<UnityEngine.UI.Slider>(true);
            }
            if (slider == null)
            {
                UnityEngine.Object.Destroy(spawned);
                return false;
            }

            slider.wholeNumbers = false;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = value;
            _sliders[channel] = slider;

            int captured = channel;
            slider.onValueChanged.AddListener(delegate(float moved)
            {
                OnChannelMoved(captured, moved);
            });

            BuildBox(channel, value, boxLeft, y);
            return true;
        }

        /// <summary>
        /// The typed value beside a slider.
        ///
        /// A slider alone cannot be told a number: it is a hundred-odd pixels for
        /// two hundred and fifty-six values, so a colour matched from somewhere
        /// else is a matter of dragging until it looks right. The box makes the
        /// same channel exact, and the two drive each other.
        ///
        /// Missing it is survivable -- the slider still works -- so an absent
        /// prefab costs a line in the log rather than the whole picker.
        /// </summary>
        private void BuildBox(int channel, float value, float x, float y)
        {
            GameObject spawned = UIF.Spawn(UIF.InputPrefab, _rect);
            if (spawned == null)
            {
                Log.Warn("UI Factory could not supply a text box; the " +
                         ChannelNames[channel] + " value can only be dragged.");
                return;
            }
            spawned.name = ChannelNames[channel] + " value";
            Place(UIF.Rect(spawned), x, y, BoxWidth, RowHeight);

            UnityEngine.UI.InputField box =
                spawned.GetComponent<UnityEngine.UI.InputField>();
            if (box == null)
            {
                box = spawned.GetComponentInChildren<UnityEngine.UI.InputField>(true);
            }
            if (box == null)
            {
                UnityEngine.Object.Destroy(spawned);
                return;
            }

            box.characterValidation = UnityEngine.UI.InputField.CharacterValidation.Decimal;
            box.lineType = UnityEngine.UI.InputField.LineType.SingleLine;
            box.characterLimit = 5;
            if (box.textComponent != null)
            {
                box.textComponent.alignment = TextAnchor.MiddleCenter;
            }
            box.text = Written(channel, value);
            _boxes[channel] = box;

            int captured = channel;
            // onEndEdit rather than onValueChanged: committing every keystroke
            // would apply the 2 of a "255" and drag the slider away underneath.
            box.onEndEdit.AddListener(delegate(string typed)
            {
                OnChannelTyped(captured, typed);
            });
        }

        /// <summary>
        /// Anchors a child to the panel's top-left corner and sizes it in pixels.
        /// Everything in here is laid out down a running y, so top-left is the only
        /// corner that stays still as the panel grows.
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
                label.raycastTarget = false;
            }
            return label;
        }

        private void OnChannelMoved(int channel, float value)
        {
            if (_binding)
            {
                return;
            }
            Apply(channel, value);
            Show(channel, value);
        }

        /// <summary>
        /// Takes a typed value. Anything unreadable puts the real one back rather
        /// than guessing: a box left saying something that is not what the colour
        /// is would be worse than losing the edit.
        /// </summary>
        private void OnChannelTyped(int channel, string typed)
        {
            if (_binding)
            {
                return;
            }

            float scaled;
            if (!float.TryParse(typed == null ? string.Empty : typed.Trim(),
                                NumberStyles.Float, CultureInfo.InvariantCulture,
                                out scaled))
            {
                Bind();
                return;
            }

            float value = Mathf.Clamp01(scaled / ChannelScales[channel]);
            Apply(channel, value);
            Slide(channel, value);
            // Written back rather than left as typed, so "300" becomes 255 and
            // "7.4" becomes 7 in front of the player instead of silently.
            Show(channel, value);
        }

        /// <summary>Puts one channel into the palette and everything drawn from it.</summary>
        private void Apply(int channel, float value)
        {
            Color colour = DiffPalette.Of(_category);
            if (channel == 0) { colour.r = value; }
            else if (channel == 1) { colour.g = value; }
            else if (channel == 2) { colour.b = value; }
            else { colour.a = value; }

            DiffPalette.Set(_category, colour);
            if (_preview != null)
            {
                // The swatch shows the colour at full opacity rather than as the
                // shells will be drawn: an opacity slider dragged to nothing would
                // otherwise fade the one thing telling you what you are dragging.
                _preview.color = DiffPalette.Ink(_category);
            }
            if (_changed != null)
            {
                _changed();
            }
        }

        /// <summary>One channel as it is written in its box: whole numbers, in scale.</summary>
        private static string Written(int channel, float value)
        {
            return Mathf.RoundToInt(value * ChannelScales[channel])
                       .ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Writes a channel into its box without that counting as an edit.</summary>
        private void Show(int channel, float value)
        {
            if (_boxes[channel] == null)
            {
                return;
            }
            _binding = true;
            _boxes[channel].text = Written(channel, value);
            _binding = false;
        }

        /// <summary>Moves a slider without that counting as a drag.</summary>
        private void Slide(int channel, float value)
        {
            if (_sliders[channel] == null)
            {
                return;
            }
            _binding = true;
            _sliders[channel].value = value;
            _binding = false;
        }

        private void Reset()
        {
            DiffPalette.Set(_category, DiffPalette.Default(_category));
            Bind();
            if (_changed != null)
            {
                _changed();
            }
        }

        /// <summary>Puts the palette's colour back onto the sliders, boxes and swatch.</summary>
        private void Bind()
        {
            Color colour = DiffPalette.Of(_category);
            float[] values = { colour.r, colour.g, colour.b, colour.a };

            _binding = true;
            for (int channel = 0; channel < _sliders.Length; channel++)
            {
                if (_sliders[channel] != null)
                {
                    _sliders[channel].value = values[channel];
                }
                if (_boxes[channel] != null)
                {
                    _boxes[channel].text = Written(channel, values[channel]);
                }
            }
            _binding = false;

            if (_preview != null)
            {
                _preview.color = DiffPalette.Ink(_category);
            }
        }

        /// <summary>
        /// Opens the panel under the control that asked for it, measured rather
        /// than derived — the same reason the column headings are measured. See
        /// <see cref="UIBuild.PlaceUnder"/>.
        /// </summary>
        public void Open(Transform parent, RectTransform under, RectTransform space)
        {
            if (!Build(parent))
            {
                return;
            }
            Bind();
            _panel.SetActive(true);
            // Above the rows and the header both, whatever order they were built in.
            _panel.transform.SetAsLastSibling();
            UIBuild.PlaceUnder(_rect, under, space, DropGap);
        }

        public void Close()
        {
            Commit();
            if (_panel != null)
            {
                _panel.SetActive(false);
            }
        }

        /// <summary>
        /// Takes whatever is in the boxes before the panel goes away.
        ///
        /// A picker now closes when the player clicks somewhere else, and a number
        /// typed but not entered is exactly what is on screen at that moment.
        /// Whether hiding the panel would raise <c>onEndEdit</c> on its own depends
        /// on what a disabled InputField does about the caret it had, which is not
        /// worth depending on. Re-applying a box that was not being edited costs
        /// nothing: it holds the value it would be set back to.
        /// </summary>
        private void Commit()
        {
            for (int channel = 0; channel < _boxes.Length; channel++)
            {
                if (_boxes[channel] != null)
                {
                    OnChannelTyped(channel, _boxes[channel].text);
                }
            }
        }

        /// <summary>
        /// True if a point on the screen is over the panel.
        ///
        /// Asked rather than handled: the panel can only be told about clicks that
        /// reach it, and what closes it is a click that does not -- anywhere at all,
        /// including out in the world past the window's edge, where no amount of
        /// invisible catcher parented into the window would ever be hit. The camera
        /// is null because the canvas is Screen Space - Overlay, where a screen
        /// point needs no unprojecting.
        /// </summary>
        public bool Contains(Vector2 screenPoint)
        {
            return Visible && _rect != null &&
                   RectTransformUtility.RectangleContainsScreenPoint(_rect, screenPoint,
                                                                     null);
        }
    }
}
