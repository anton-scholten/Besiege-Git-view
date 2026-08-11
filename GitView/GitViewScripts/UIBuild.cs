using System;
using UnityEngine;
using UnityEngine.UI;

namespace GitView
{
    /// <summary>
    /// The few pieces of uGUI this mod assembles by hand rather than taking from UI
    /// Factory: the canvas everything sits on, and the labels and images inside a
    /// row, which have no prefab of their own.
    /// </summary>
    public static class UIBuild
    {
        /// <summary>
        /// UI Factory authors its prefabs against 1920x1080 and matches on height.
        /// Anything else renders Besiege's own widgets at the wrong size beside the
        /// game's.
        /// </summary>
        public static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

        private static Font _fallback;

        /// <summary>
        /// Besiege's UI font where UI Factory can supply it, Unity's stock one
        /// otherwise.
        ///
        /// Worth checking rather than trusting: Besiege writes its interface
        /// entirely in capitals, so a font baked for it may carry no lowercase
        /// glyphs at all, and handing that font a timestamp would draw nothing --
        /// a blank row that reads like a layout bug rather than a font problem.
        /// </summary>
        public static Font DefaultFont
        {
            get
            {
                Font font = UIF.Font;
                if (font != null && (font.dynamic || font.HasCharacter('0')))
                {
                    return font;
                }
                return FallbackFont;
            }
        }

        public static Font FallbackFont
        {
            get
            {
                if (_fallback == null)
                {
                    try
                    {
                        _fallback = Resources.GetBuiltinResource<Font>("Arial.ttf");
                    }
                    catch (Exception)
                    {
                        _fallback = null;
                    }
                    if (_fallback == null)
                    {
                        _fallback = Font.CreateDynamicFontFromOSFont("Arial", 16);
                    }
                }
                return _fallback;
            }
        }

        /// <summary>
        /// A Screen Space - Overlay canvas above Besiege's own UI.
        ///
        /// Kept below 30000, which is the sorting order UnityEngine.UI.Dropdown
        /// hardcodes for a popup list's canvas; a canvas that ties with it wins and
        /// leaves the list invisible and unclickable. Besiege's own UI never sets a
        /// sorting order from code, so there is nothing between here and there.
        /// </summary>
        public static Canvas CreateCanvas(GameObject host, int sortingOrder)
        {
            Canvas canvas = host.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            canvas.pixelPerfect = false;

            CanvasScaler scaler = host.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            host.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        /// <summary>
        /// Takes a strip off the top of a rect, whether it is stretched to its
        /// parent or sized in pixels.
        ///
        /// The two need opposite treatment and look identical from the outside. A
        /// stretched rect's offsets are distances from its anchors, so lowering the
        /// top offset shortens it and nothing else needs doing. A fixed one has a
        /// height and a position about its pivot, so shortening it alone would take
        /// the strip out of both ends; holding the bottom edge still means sliding
        /// it down by the pivot's share of what it lost.
        /// </summary>
        public static void InsetTop(RectTransform rect, float amount)
        {
            if (rect == null || amount == 0f)
            {
                return;
            }
            if (rect.anchorMin.y != rect.anchorMax.y)
            {
                rect.offsetMax = new Vector2(rect.offsetMax.x, rect.offsetMax.y - amount);
                return;
            }
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, rect.sizeDelta.y - amount);
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x,
                                                rect.anchoredPosition.y - amount * rect.pivot.y);
        }

        /// <summary>Takes a strip off the bottom of a rect. See <see cref="InsetTop"/>.</summary>
        public static void InsetBottom(RectTransform rect, float amount)
        {
            if (rect == null || amount == 0f)
            {
                return;
            }
            if (rect.anchorMin.y != rect.anchorMax.y)
            {
                rect.offsetMin = new Vector2(rect.offsetMin.x, rect.offsetMin.y + amount);
                return;
            }
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, rect.sizeDelta.y - amount);
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x,
                                                rect.anchoredPosition.y + amount * (1f - rect.pivot.y));
        }

        /// <summary>
        /// Puts a rect immediately above or below another one, matching its width.
        ///
        /// Measured rather than derived. The obvious approach — anchor the strip to
        /// the same edge of the window and step by a known height — assumes the
        /// window's own rect is the box you can see, and for a prefab it need not
        /// be: the frame can be a child, inset inside a larger root, and the
        /// scrolling area inset again inside that for its scrollbar. Anything
        /// placed by arithmetic then lands somewhere unrelated, and anything sized
        /// by arithmetic ends up a scrollbar's width out of step with the rows it
        /// is a heading for.
        ///
        /// <c>CalculateRelativeRectTransformBounds</c> asks where the box actually
        /// is, in the coordinates of the thing we are parenting to, which is true
        /// whatever the prefab's internal layout turns out to be.
        /// </summary>
        public static void PlaceStrip(RectTransform strip, RectTransform against,
                                      RectTransform space, float height, bool above)
        {
            if (strip == null || against == null || space == null)
            {
                return;
            }

            // Rects are only correct once a layout pass has run over them, and the
            // caller has just resized things.
            Canvas.ForceUpdateCanvases();

            Bounds box = RectTransformUtility.CalculateRelativeRectTransformBounds(space, against);
            strip.anchorMin = new Vector2(0.5f, 0.5f);
            strip.anchorMax = new Vector2(0.5f, 0.5f);
            strip.pivot = new Vector2(0.5f, 0.5f);
            strip.sizeDelta = new Vector2(box.size.x, height);

            float edge = above ? box.center.y + box.extents.y + height * 0.5f
                               : box.center.y - box.extents.y - height * 0.5f;
            // The bounds are in the parent's local space, whose origin is its
            // pivot; anchoredPosition is measured from the anchor, which is the
            // centre of the parent's rect. Those coincide only when the parent is
            // pivoted in its middle, which is not ours to assume.
            strip.anchoredPosition = new Vector2(box.center.x, edge) - space.rect.center;
        }

        public static Text AddText(Transform parent, string name, int fontSize,
                                   TextAnchor alignment)
        {
            Text label = CreateRect(name, parent).gameObject.AddComponent<Text>();
            label.font = DefaultFont;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = Color.white;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        public static Image AddImage(Transform parent, string name, Color colour)
        {
            Image image = CreateRect(name, parent).gameObject.AddComponent<Image>();
            // An Image with no sprite draws an opaque white quad, so the colour is
            // set before anything can see it.
            image.color = colour;
            return image;
        }

        public static RawImage AddRawImage(Transform parent, string name)
        {
            RawImage image = CreateRect(name, parent).gameObject.AddComponent<RawImage>();
            image.color = new Color(1f, 1f, 1f, 0f);
            image.texture = null;
            return image;
        }
    }
}
