using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace GitView
{
    /// <summary>
    /// Thin wrapper over UI Factory 3 (https://gitlab.com/dagriefaa/ui-factory-3),
    /// Workshop item 2913469777.
    ///
    /// UIFactory ships Besiege's real UI as Unity prefabs -- window frame, buttons,
    /// font, hover and press animations. Instantiating those is the only way to look
    /// exactly like Besiege: the game's own panel materials are reachable only from
    /// inside a custom mapper selector, and <c>InternalModding</c> is blacklisted.
    ///
    /// Every call comes through here, so one file knows the prefab names and a
    /// missing UIFactory is a log line rather than an exception thrown halfway
    /// through building the window.
    /// </summary>
    public static class UIF
    {
        /// <summary>The name UIFactory registers its own prefabs under.</summary>
        public const string Package = "UIFactory3";

        // Prefab names, as registered by UIFactory's Mod.OnAllResourcesLoaded.
        public const string WindowPrefab = "Window";
        public const string TextPrefab = "Text";
        public const string ButtonPrefab = "Text Button";
        public const string SliderPrefab = "Slider";

        /// <summary>
        /// A button whose face is a picture rather than a word, on a child called
        /// "Icon". What the close cross itself is made from, so a mark beside it is
        /// the same size and shape without being told to be.
        /// </summary>
        public const string IconButtonPrefab = "Icon Button";

        /// <summary>
        /// Besiege's text box. It carries <c>StopsHotkeysWhenInputFieldFocused</c>,
        /// so typing a number does not also fire the game's shortcuts -- which is the
        /// reason to use it over a uGUI InputField of one's own.
        /// </summary>
        public const string InputPrefab = "Input Field";

        /// <summary>
        /// Instantiates one of UIFactory's prefabs. Returns null (and says why) if
        /// UIFactory is not ready, rather than throwing into the caller.
        /// </summary>
        public static GameObject Spawn(string prefab, Transform parent)
        {
            try
            {
                return Besiege.UI.Make.Prefab(Package, prefab, parent);
            }
            catch (Exception e)
            {
                Log.Warn("UIFactory could not supply prefab '" + prefab + "': " + e.Message);
                return null;
            }
        }

        /// <summary>Besiege's own UI font, as UIFactory resolved it from the game.</summary>
        public static Font Font
        {
            get
            {
                try
                {
                    return Besiege.UI.Make.Font;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// True once UIFactory has loaded its asset bundle. Building UI before this
        /// throws inside UIFactory, so the window waits for it.
        /// </summary>
        public static bool Ready
        {
            get
            {
                try
                {
                    return Besiege.UI.Make.Instance != null && Modding.ModResource.AllResourcesLoaded;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>Runs <paramref name="action"/> as soon as UIFactory's resources exist.</summary>
        public static void WhenReady(Action action)
        {
            if (action == null)
            {
                return;
            }
            try
            {
                Besiege.UI.Make.OnReady(Package, action);
            }
            catch (Exception e)
            {
                Log.Warn("UIFactory readiness callback failed (" + e.Message + "); building now.");
                action();
            }
        }

        // -- small helpers ------------------------------------------------------------

        public static RectTransform Rect(GameObject go)
        {
            return go == null ? null : go.transform as RectTransform;
        }

        /// <summary>Makes a child fill its parent, inset by the given padding.</summary>
        public static void Stretch(RectTransform rect, float horizontal, float vertical)
        {
            if (rect == null)
            {
                return;
            }
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(horizontal, vertical);
            rect.offsetMax = new Vector2(-horizontal, -vertical);
        }

        /// <summary>Anchors a control across a horizontal fraction of its row.</summary>
        public static void Span(RectTransform rect, float minX, float maxX,
                                float insetLeft, float insetRight)
        {
            if (rect == null)
            {
                return;
            }
            rect.anchorMin = new Vector2(minX, 0f);
            rect.anchorMax = new Vector2(maxX, 1f);
            rect.offsetMin = new Vector2(insetLeft, 0f);
            rect.offsetMax = new Vector2(-insetRight, 0f);
        }

        /// <summary>
        /// Fills the parent with a different inset on each side. Separate from
        /// <see cref="Stretch"/> because a heading and the values under it need the
        /// same insets, and those are not symmetrical: text is padded away from the
        /// edge it is aligned to by more than from the other.
        /// </summary>
        public static void StretchInset(RectTransform rect, float left, float right,
                                        float vertical)
        {
            if (rect == null)
            {
                return;
            }
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, vertical);
            rect.offsetMax = new Vector2(-right, -vertical);
        }

        /// <summary>
        /// Sets the caption on a UIFactory prefab. Their text lives on a child and
        /// carries a Translator, which would overwrite ours at the next language
        /// change -- so <see cref="Style"/> takes it off.
        /// </summary>
        public static Text Caption(GameObject go, string text, int fontSize, TextAnchor alignment)
        {
            if (go == null)
            {
                return null;
            }
            Text label = Style(go.GetComponentInChildren<Text>(true), fontSize, alignment);
            if (label != null)
            {
                label.text = text;
            }
            return label;
        }

        /// <summary>Sizes and aligns a label we own, and takes its Translator off.</summary>
        public static Text Style(Text label, int fontSize, TextAnchor alignment)
        {
            if (label == null)
            {
                return null;
            }

            try
            {
                Besiege.UI.Behaviours.Translator translator =
                    label.GetComponent<Besiege.UI.Behaviours.Translator>();
                if (translator != null)
                {
                    UnityEngine.Object.Destroy(translator);
                }
            }
            catch (Exception)
            {
                // A UIFactory without that behaviour still gives usable text.
            }

            if (fontSize > 0)
            {
                label.fontSize = fontSize;
                label.resizeTextForBestFit = false;
            }
            label.alignment = alignment;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            // A row can be two lines now -- a machine's name over its timestamp --
            // and a prefab authored to truncate would show only the first.
            label.verticalOverflow = VerticalWrapMode.Overflow;
            // The sort arrows are two glyphs in one label, one lit and one dimmed,
            // which needs markup.
            label.supportRichText = true;
            return label;
        }

        /// <summary>
        /// Finds and styles the label on a spawned prefab, leaving the prefab's own
        /// rect where the caller put it. On a `Text Button` the label is a child
        /// authored at a fixed width, so it has to be stretched to whatever the
        /// control was resized to; on a `Text` the label *is* the prefab, and
        /// stretching it would throw away the caller's placement.
        /// </summary>
        public static Text Label(GameObject go, int fontSize, TextAnchor alignment)
        {
            if (go == null)
            {
                return null;
            }
            Text label = go.GetComponent<Text>();
            if (label == null)
            {
                label = go.GetComponentInChildren<Text>(true);
            }
            Style(label, fontSize, alignment);

            RectTransform own = go.transform as RectTransform;
            if (label != null && label.rectTransform != own)
            {
                StretchInset(label.rectTransform, 0f, 0f, 0f);
            }
            return label;
        }

        /// <summary>Wires a UIFactory button prefab to a plain callback.</summary>
        public static Button OnClick(GameObject go, Action handler)
        {
            if (go == null)
            {
                return null;
            }
            Button button = go.GetComponent<Button>();
            if (button == null || handler == null)
            {
                return button;
            }
            Action captured = handler;
            button.onClick.AddListener(delegate { captured(); });
            return button;
        }

        /// <summary>
        /// Moves the edge a control's hover animation grows from. UIFactory's buttons
        /// swell about their pivot, which is the middle: on a wide control that
        /// carries the text visibly sideways. Pinning the pivot to the edge the text
        /// is aligned to keeps that edge still. Moving a pivot moves the rect, so the
        /// insets are put back afterwards.
        /// </summary>
        public static void PivotAnimation(GameObject control, float pivotX)
        {
            if (control == null)
            {
                return;
            }
            RectTransform rect = control.transform as RectTransform;
            if (rect == null)
            {
                return;
            }

            try
            {
                Besiege.UI.Bridge.ScaleAnimation scale =
                    control.GetComponent<Besiege.UI.Bridge.ScaleAnimation>();
                if (scale == null || scale.Target != rect)
                {
                    return;
                }
                Vector2 min = rect.offsetMin;
                Vector2 max = rect.offsetMax;
                rect.pivot = new Vector2(pivotX, 0.5f);
                rect.offsetMin = min;
                rect.offsetMax = max;
            }
            catch (Exception)
            {
                // Not worth a log line; the animation simply stays as authored.
            }
        }

        /// <summary>
        /// Takes a control's hover swell away, for controls too wide to swell.
        ///
        /// A hovered button grows 15%, which is right for a button the size of a
        /// button; on a seven-hundred-pixel row it carries the text at both ends tens
        /// of pixels sideways, and no pivot fixes that because a pivot can only hold
        /// one end still. So rows light up instead, as Besiege's own lists do.
        ///
        /// Switched off rather than turned down, the two scales being private to
        /// UIFactory: a disabled behaviour is never told the pointer arrived.
        /// </summary>
        public static void NoSwell(GameObject control)
        {
            if (control == null)
            {
                return;
            }
            try
            {
                Besiege.UI.Bridge.ScaleAnimation scale =
                    control.GetComponent<Besiege.UI.Bridge.ScaleAnimation>();
                if (scale != null)
                {
                    scale.enabled = false;
                }
            }
            catch (Exception)
            {
                // An older UIFactory without that behaviour simply never swelled.
            }
        }

        /// <summary>
        /// Makes a button tint one of its own images as the pointer arrives and
        /// presses. uGUI drives the tint onto the graphic's canvas renderer, so the
        /// image's own colour stops mattering -- hence every state is passed in.
        /// </summary>
        public static void HoverTint(Button button, Graphic target, Color normal,
                                     Color hovered, Color pressed)
        {
            if (button == null || target == null)
            {
                return;
            }
            button.targetGraphic = target;
            button.transition = Selectable.Transition.ColorTint;

            ColorBlock colours = button.colors;
            colours.normalColor = normal;
            colours.highlightedColor = hovered;
            colours.pressedColor = pressed;
            colours.disabledColor = normal;
            colours.colorMultiplier = 1f;
            colours.fadeDuration = 0.08f;
            button.colors = colours;
        }
    }
}
