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
        /// Moves a control's press animation to grow from its left edge instead of
        /// its middle.
        ///
        /// UIFactory's buttons scale about their pivot on hover. On a button as
        /// wide as a whole row that carries its left-aligned text visibly sideways,
        /// which reads as the text jumping rather than as the row lighting up.
        /// Moving the pivot moves the rect, so the insets are put back afterwards.
        /// </summary>
        public static void PivotAnimationLeft(GameObject control)
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
                rect.pivot = new Vector2(0f, 0.5f);
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
