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

        /// <summary>
        /// Puts a strip's left and right edges exactly where another object's are,
        /// less an inset on each side, without touching how tall it is or where it
        /// sits vertically.
        ///
        /// For a header over a list: the strip is placed against the scrolling area,
        /// but the rows are laid out inside the *content* -- and the two need not be
        /// the same width or start at the same x, since a scroll view may inset its
        /// content for a scrollbar or a mask. Aligning the header to the box the
        /// rows are actually in is the only way to be sure a column heading is over
        /// its column; anything else is arithmetic about a prefab we do not own.
        /// </summary>
        public static void MatchWidth(RectTransform strip, RectTransform rows,
                                      RectTransform space, float inset)
        {
            if (strip == null || rows == null || space == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            Bounds box = RectTransformUtility.CalculateRelativeRectTransformBounds(space, rows);

            strip.sizeDelta = new Vector2(box.size.x - inset * 2f, strip.sizeDelta.y);
            strip.anchoredPosition = new Vector2(box.center.x - space.rect.center.x,
                                                 strip.anchoredPosition.y);
        }

        /// <summary>
        /// Hangs a panel off the bottom-left corner of a control, in the space of
        /// whatever it is parented to.
        ///
        /// Measured for the same reason <see cref="PlaceStrip"/> is: the control
        /// being dropped from is a UIFactory prefab whose visible box need not be
        /// its rect, and it sits inside a header inside a window, none of which are
        /// pivoted or anchored the same way.
        /// </summary>
        public static void PlaceUnder(RectTransform panel, RectTransform under,
                                      RectTransform space, float gap)
        {
            if (panel == null || under == null || space == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            Bounds box = RectTransformUtility.CalculateRelativeRectTransformBounds(space, under);

            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0f, 1f);
            // As in PlaceStrip: the bounds are relative to the parent's pivot and
            // anchoredPosition is relative to the anchor, which is the centre of the
            // parent's rect. They coincide only for a centre-pivoted parent.
            Vector2 at = new Vector2(box.center.x - box.extents.x,
                                     box.center.y - box.extents.y - gap) - space.rect.center;

            // Pulled back inside if hanging it off that corner would put it past
            // the parent's edge, which the rightmost of a row of controls always
            // would. Left-aligned under its control where there is room, and flush
            // with the edge where there is not.
            // sizeDelta rather than rect.width: the anchors were changed a line ago
            // and no layout pass has run since, but with the anchors together
            // sizeDelta is the width by definition.
            float half = space.rect.width * 0.5f;
            at.x = Mathf.Min(at.x, half - panel.sizeDelta.x);
            at.x = Mathf.Max(at.x, -half);
            panel.anchoredPosition = at;
        }

        /// <summary>
        /// Puts a control in a window's title bar, at the right-hand end: square,
        /// as tall as the bar allows, and <paramref name="place"/> places along from
        /// the corner.
        ///
        /// Square is the point of it. Besiege's own title-bar controls are square --
        /// the close cross, the pin on the block panel -- and the prefab's is
        /// authored to whatever width its own window wanted, which is not this one.
        /// A cross stretched into a rectangle is the sort of thing that reads as a
        /// mod before anything else about the window does.
        /// </summary>
        public static void SquareInBar(RectTransform control, RectTransform bar,
                                       int place)
        {
            SquareInBar(control, bar, place, false);
        }

        /// <summary>
        /// The same, at whichever end of the bar is asked for.
        ///
        /// The left end is for a control that is not about shutting the window --
        /// the reset arrows on the colours -- because a title is centred in its bar
        /// and a pile of marks at one end pushes it off centre or sits on top of it.
        /// One at each end leaves the middle to the title.
        /// </summary>
        public static void SquareInBar(RectTransform control, RectTransform bar,
                                       int place, bool leftEnd)
        {
            if (control == null || bar == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            float side = Mathf.Max(14f, bar.rect.height - BarMargin * 2f);
            float edge = leftEnd ? 0f : 1f;
            float along = BarMargin + place * (side + BarGap);

            control.anchorMin = new Vector2(edge, 0.5f);
            control.anchorMax = new Vector2(edge, 0.5f);
            control.pivot = new Vector2(edge, 0.5f);
            control.sizeDelta = new Vector2(side, side);
            control.anchoredPosition = new Vector2(leftEnd ? along : -along, 0f);

            // A square box is not a square picture. A mark authored taller than it is
            // wide keeps those proportions inside a square rect if it is allowed to,
            // and Besiege's cross comes out a tall thin X; told to fill the square it
            // has been given, it is as wide as it is high. Harmless on a background,
            // which is nine-sliced and ignores it either way.
            Image[] faces = control.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < faces.Length; i++)
            {
                if (faces[i] != null)
                {
                    faces[i].preserveAspect = false;
                }
            }
        }

        /// <summary>How far a title bar's controls sit inside it, and from each other.</summary>
        private const float BarMargin = 5f;
        private const float BarGap = 4f;

        /// <summary>
        /// How much of a title-bar button one of our own marks is drawn across.
        ///
        /// Less than all of it. A mark drawn to the edge of its button is a mark as
        /// big as the bar is tall, which is bigger than anything Besiege puts in a
        /// title bar -- the cog beside the cross looked like a cog on top of the
        /// window rather than a control in it. The button keeps its size, since that
        /// is what is being clicked; only the picture inside it comes in.
        /// </summary>
        private const float IconShare = 0.55f;

        /// <summary>
        /// A picture button for a window's title bar: UI Factory's Icon Button, which
        /// is what the close cross itself is, wearing a mark of ours.
        ///
        /// The sprite goes on the child called "Icon" where the prefab has one. The
        /// fallback is whatever Image the button does have, because the name is UI
        /// Factory's to change and a button wearing the wrong picture is a smaller
        /// failure than a button with none.
        /// </summary>
        public static GameObject BarButton(RectTransform bar, int place, string name,
                                           Sprite face, Color tint, Action clicked)
        {
            return BarButton(bar, place, name, face, tint, clicked, false);
        }

        /// <summary>The same, at whichever end of the bar is asked for.</summary>
        public static GameObject BarButton(RectTransform bar, int place, string name,
                                           Sprite face, Color tint, Action clicked,
                                           bool leftEnd)
        {
            GameObject button = UIF.Spawn(UIF.IconButtonPrefab, bar);
            if (button == null)
            {
                return null;
            }
            button.name = name;
            RectTransform rect = UIF.Rect(button);
            SquareInBar(rect, bar, place, leftEnd);
            // Last in the bar, so that nothing drawn after it -- the title, whatever
            // the prefab has -- is over it and taking the pointer first.
            rect.SetAsLastSibling();

            Transform child = button.transform.FindChild("Icon");
            Image image = child == null ? null : child.GetComponent<Image>();
            if (image == null)
            {
                image = button.GetComponentInChildren<Image>(true);
            }
            if (image != null)
            {
                image.sprite = face;
                image.color = tint;
                // Ours are drawn square and edge to edge, so how big the mark is
                // drawn is exactly how big this rect is -- see IconShare.
                image.preserveAspect = false;
                Inside(image.rectTransform, rect.sizeDelta.x * IconShare);
            }

            // What is actually clicked: the whole button, rather than whichever part
            // of it the prefab happens to draw something on. A mark inset inside its
            // button is a mark whose own edges miss, which is a button that works
            // when aimed at and not when aimed near -- and the corner of a title bar
            // is aimed at in a hurry.
            Image reach = AddImage(rect, "Reach", new Color(1f, 1f, 1f, 0f));
            reach.raycastTarget = true;
            UIF.Stretch(reach.rectTransform, 0f, 0f);
            reach.transform.SetAsFirstSibling();

            UIF.OnClick(button, clicked);
            return button;
        }

        /// <summary>Centres a square of the given side inside its parent.</summary>
        private static void Inside(RectTransform rect, float side)
        {
            if (rect == null)
            {
                return;
            }
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(side, side);
            rect.anchoredPosition = Vector2.zero;
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
