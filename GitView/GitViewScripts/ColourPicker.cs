using System;
using UnityEngine;
using UnityEngine.UI;

namespace GitView
{
    /// <summary>
    /// A small panel for choosing one of the diff's three colours: red, green,
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
        private const float Width = 258f;
        private const float Pad = 10f;
        private const float TitleHeight = 22f;
        private const float RowHeight = 28f;
        private const float RowGap = 2f;
        private const float LabelWidth = 74f;
        private const float SwatchSize = 20f;
        private const float ResetHeight = 24f;
        private const int LabelSize = 12;

        /// <summary>How far below the swatch that opened it the panel sits.</summary>
        private const float DropGap = 4f;

        private static readonly string[] ChannelNames = { "RED", "GREEN", "BLUE", "OPACITY" };

        private readonly int _category;
        private readonly Action _changed;
        private GameObject _panel;
        private RectTransform _rect;
        private Image _preview;
        private readonly UnityEngine.UI.Slider[] _sliders = new UnityEngine.UI.Slider[4];

        /// <summary>
        /// True while a slider is being written to rather than dragged, so the
        /// callbacks it raises are not mistaken for the player moving it.
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
            Place(UIF.Rect(spawned), Pad + LabelWidth, y,
                  Width - Pad * 2f - LabelWidth, RowHeight);

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
            return true;
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

            Color colour = DiffPalette.Of(_category);
            if (channel == 0) { colour.r = value; }
            else if (channel == 1) { colour.g = value; }
            else if (channel == 2) { colour.b = value; }
            else { colour.a = value; }

            DiffPalette.Set(_category, colour);
            if (_preview != null)
            {
                // The swatch shows the colour as the text will read, not as the
                // shells will: an opacity slider dragged to nothing would otherwise
                // fade the one thing telling you what you are dragging.
                _preview.color = DiffPalette.Ink(_category);
            }
            if (_changed != null)
            {
                _changed();
            }
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

        /// <summary>Puts the palette's colour back onto the sliders and the swatch.</summary>
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
            if (_panel != null)
            {
                _panel.SetActive(false);
            }
        }
    }
}
