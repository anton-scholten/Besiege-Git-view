using System;
using UnityEngine;
using UnityEngine.UI;

namespace GitView
{
    /// <summary>
    /// Thin wrapper over UI Factory 3 (https://gitlab.com/dagriefaa/ui-factory-3),
    /// Workshop item 2913469777.
    ///
    /// UIFactory ships Besiege's real UI as Unity prefabs -- the window frame, the
    /// buttons, the font, the hover and press animations. Instantiating those is
    /// the only way to look exactly like Besiege: the game's own panel materials
    /// are reachable only from inside a custom mapper selector, and
    /// <c>InternalModding</c> is on the mod loader's blacklist, so a hand-mixed
    /// panel is always an approximation.
    ///
    /// Every call is funnelled through here so that one file knows the package and
    /// prefab names, and so a missing UIFactory surfaces as a log line rather than
    /// an exception thrown halfway through building the window.
    /// </summary>
    public static class UIF
    {
        /// <summary>The name UIFactory registers its own prefabs under.</summary>
        public const string Package = "UIFactory3";

        // Prefab names, as registered by UIFactory's Mod.OnAllResourcesLoaded.
        public const string WindowPrefab = "Window";
        public const string PanelPrefab = "Panel";
        public const string TextPrefab = "Text";
        public const string ButtonPrefab = "Text Button";

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
        /// Fills the parent with a different inset on each side.
        ///
        /// Separate from <see cref="Stretch"/> because a column heading and the
        /// values under it have to be inset by exactly the same amounts on exactly
        /// the same sides, and those amounts are not symmetrical -- text is padded
        /// away from the column edge it is aligned to by more than the edge it is
        /// not.
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
        /// Sets the caption on a UIFactory prefab. Their text lives on a child, and
        /// carries a Translator that would overwrite whatever we assign the next
        /// time the language changes -- so that gets removed for text we own.
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
            // The sort arrows are two glyphs in one label, one lit and one dimmed,
            // which needs markup.
            label.supportRichText = true;
            return label;
        }

        /// <summary>
        /// Finds and styles the label on a spawned UIFactory prefab, leaving the
        /// prefab's own rect where the caller put it.
        ///
        /// The distinction matters and is easy to get wrong. On a `Text Button` the
        /// label is a child, authored at a fixed width for the prefab's own size,
        /// so it has to be stretched to whatever the control was resized to. On a
        /// `Text` the label *is* the prefab, and stretching it throws away the
        /// placement the caller just worked out -- which is how the status line
        /// ended up anchored across the middle of the window instead of under it.
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
        /// Moves the edge a control's hover animation grows from.
        ///
        /// UIFactory's buttons swell about their pivot when the pointer is over
        /// them, and they are pivoted in the middle. On a control as wide as a
        /// whole row that carries its text visibly sideways — a left-aligned label
        /// slides left, a right-aligned one slides right — which reads as the text
        /// jumping rather than as the row lighting up. Pinning the pivot to the
        /// edge the text is aligned to keeps that edge still, so the swell happens
        /// entirely on the other side and the words do not move.
        ///
        /// Moving a pivot moves the rect, so the insets are put back afterwards.
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

    }
}
