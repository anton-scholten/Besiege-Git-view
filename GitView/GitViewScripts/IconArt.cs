using UnityEngine;

namespace GitView
{
    /// <summary>
    /// Draws the compare button's face into a texture at runtime.
    ///
    /// Generated rather than shipped as a PNG: it cannot go missing from an
    /// install, it costs nothing at this size, and it sidesteps the case-sensitive
    /// resource loading that catches Workshop mods authored on Windows. UI
    /// Factory's sprite set is Besiege's own HUD sprites, so there is no
    /// ready-made icon to borrow for this.
    ///
    /// The glyph is the usual branch mark: a trunk between two commits, and a
    /// branch leaving it for a third. It is what the button does, and it is
    /// recognisable at the size Besiege draws a slot button.
    /// </summary>
    public static class IconArt
    {
        private const int Samples = 4;

        // Which shape to draw. Ints rather than an enum, because Besiege's in-game
        // compiler segfaults on an enum declaration. There is only the one shape
        // now -- the mod says "history" with a branch everywhere it says anything,
        // on the machines, on the versions and on the button that compares them --
        // but the corner mark is what tells them apart, and that is drawn by the
        // same routine.
        public const int GlyphBranch = 0;

        /// <summary>
        /// The branch glyph, white on transparent, so that whatever tint it is
        /// drawn with is what colours it.
        /// </summary>
        public static Texture2D Branch(int size)
        {
            return Render(size, GlyphBranch, string.Empty, null);
        }

        // The two line-art marks on the window's title bar. Both are drawn to the
        // weight the Clippy mod draws its own with -- a tenth of the icon -- so that
        // a row of buttons across two of this author's mods reads as one set. The
        // reload arrow is that mod's, constant for constant: the ring it is struck
        // from, the arc left out for the head to sweep into, and the head itself.
        private const int ShapeGear = 0;
        private const int ShapeReload = 1;
        private const float Stroke10 = 0.10f;

        /// <summary>A cog: the button that opens the options.</summary>
        public static Texture2D Gear(int size)
        {
            return Trace(size, ShapeGear);
        }

        /// <summary>
        /// The reload arrow -- a ring with a bite out of it and an arrowhead --
        /// which is what "put it back the way it was" looks like in this game's
        /// mods.
        /// </summary>
        public static Texture2D Reload(int size)
        {
            return Trace(size, ShapeReload);
        }

        /// <summary>One of the line-art marks, white on transparent.</summary>
        private static Texture2D Trace(int size, int shape)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            Color32[] pixels = new Color32[size * size];
            float step = 1f / (size * Samples);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < Samples; sy++)
                    {
                        for (int sx = 0; sx < Samples; sx++)
                        {
                            float u = (x * Samples + sx + 0.5f) * step;
                            float v = (y * Samples + sy + 0.5f) * step;
                            if (shape == ShapeGear ? InsideGear(u, v) : InsideReload(u, v))
                            {
                                hits++;
                            }
                        }
                    }
                    pixels[y * size + x] = new Color32(255, 255, 255,
                                                       (byte)(255 * hits / (Samples * Samples)));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        // The cog, measured off the settings mark in the Clippy mod's dialogue
        // bubble: a solid body with eight square teeth, a ring of nothing inside it,
        // and a hub in the middle of that. Radii are that icon's own, read off a
        // screenshot a pixel at a time and divided by its half-width -- so this is
        // the same drawing rather than an impression of one.
        private const int Teeth = 8;
        private const float GearHub = 0.13f;
        private const float GearBore = 0.22f;
        private const float GearBody = 0.365f;
        private const float GearTip = 0.50f;
        private const float ToothHalfWidth = 0.227f;    // radians, 13 degrees

        private static bool InsideGear(float u, float v)
        {
            float dx = u - 0.5f;
            float dy = v - 0.5f;
            float radius = Mathf.Sqrt(dx * dx + dy * dy);
            if (radius <= GearHub)
            {
                return true;
            }
            if (radius < GearBore)
            {
                return false;
            }
            if (radius <= GearBody)
            {
                return true;
            }
            if (radius > GearTip)
            {
                return false;
            }
            float step = Mathf.PI * 2f / Teeth;
            float around = Mathf.Repeat(Mathf.Atan2(dy, dx), step) - step * 0.5f;
            return Mathf.Abs(around) <= ToothHalfWidth;
        }

        // The reload arrow, constant for constant out of the Clippy mod: one ring
        // with a bite taken out of it and an arrowhead sweeping into the gap. Drawn
        // a little low, because the head overshoots the top of the ring and the mark
        // should look centred rather than measure centred.
        private const float RingRadius = 0.375f;
        private const float RingCentreY = 0.465f;
        private const float GapFrom = 35f;
        private const float GapTo = 120f;
        private const float HeadWidth = 0.15f;
        private const float HeadLength = 0.24f;

        private static bool InsideReload(float u, float v)
        {
            float dx = u - 0.5f;
            float dy = v - RingCentreY;
            if (Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - RingRadius) <= Stroke10 * 0.5f)
            {
                float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                if (angle < 0f)
                {
                    angle += 360f;
                }
                if (angle <= GapFrom || angle >= GapTo)
                {
                    return true;
                }
            }

            // A triangle standing on the end of the arc, pointing the way the arc
            // was going: base across the stroke, tip one head-length along it.
            float end = GapTo * Mathf.Deg2Rad;
            float px = 0.5f + RingRadius * Mathf.Cos(end);
            float py = RingCentreY + RingRadius * Mathf.Sin(end);
            float tx = Mathf.Sin(end);
            float ty = -Mathf.Cos(end);
            float along = (u - px) * tx + (v - py) * ty;
            float across = Mathf.Abs(-(u - px) * ty + (v - py) * tx);
            return along >= 0f && along <= HeadLength &&
                   across <= HeadWidth * (1f - along / HeadLength);
        }

        /// <summary>
        /// A row of colours, side by side: the strip a colour slider runs along.
        ///
        /// Besiege's own colour slider is a knob dragged along a picture of the
        /// colours it can choose -- <c>ColourSliderSelector</c> holds one as a
        /// <c>Texture</c> and works out which pixel of it a colour is. That texture
        /// belongs to the block mapper and cannot be borrowed, so this draws the
        /// same thing: one band per colour, hard-edged, so that what the knob is
        /// pointing at is unambiguous.
        /// </summary>
        public static Texture2D Strip(int width, int height)
        {
            if (width < 2)
            {
                return null;
            }

            Texture2D texture = new Texture2D(width, Mathf.Max(1, height),
                                              TextureFormat.ARGB32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            Color32[] pixels = new Color32[width * texture.height];
            for (int x = 0; x < width; x++)
            {
                Color32 ink = Hue((float)x / (width - 1), StripSaturation);
                for (int y = 0; y < texture.height; y++)
                {
                    pixels[y * width + x] = ink;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        /// <summary>
        /// How washed out the strip is drawn.
        ///
        /// Besiege's own colour slider is a pale ramp -- sampled off the rocket's
        /// explosion colour, its saturation runs about 0.62 across the whole bar --
        /// while the colour it hands back is the full-strength one: the rocket's
        /// knob sits at the left of a pink-red band and the value beside it reads
        /// #FF4C00. So the picture is pastel and the answer is not, and this is the
        /// picture.
        /// </summary>
        private const float StripSaturation = 0.62f;

        /// <summary>
        /// One hue, at the saturation asked for and full brightness. Worked out here
        /// rather than through <c>Color.HSVToRGB</c>: this mod is compiled by the
        /// game's own compiler against the game's own Unity, and six lines of
        /// arithmetic cannot be missing from it.
        /// </summary>
        public static Color Hue(float hue, float saturation)
        {
            float sector = Mathf.Repeat(hue, 1f) * 6f;
            float rise = sector - Mathf.Floor(sector);
            float low = 1f - saturation;
            float up = low + saturation * rise;
            float down = 1f - saturation * rise;
            switch ((int)sector)
            {
                case 0: return new Color(1f, up, low, 1f);
                case 1: return new Color(down, 1f, low, 1f);
                case 2: return new Color(low, 1f, up, 1f);
                case 3: return new Color(low, down, 1f, 1f);
                case 4: return new Color(up, low, 1f, 1f);
                default: return new Color(1f, low, down, 1f);
            }
        }

        /// <summary>
        /// Which hue a colour is, as a place along the strip. Grey has no hue and
        /// comes back as 0, which is where the knob then sits.
        /// </summary>
        public static float HueOf(Color colour)
        {
            float high = Mathf.Max(colour.r, Mathf.Max(colour.g, colour.b));
            float low = Mathf.Min(colour.r, Mathf.Min(colour.g, colour.b));
            float span = high - low;
            if (span <= 0.0001f)
            {
                return 0f;
            }
            float hue;
            if (high == colour.r)
            {
                hue = (colour.g - colour.b) / span / 6f;
            }
            else if (high == colour.g)
            {
                hue = (2f + (colour.b - colour.r) / span) / 6f;
            }
            else
            {
                hue = (4f + (colour.r - colour.g) / span) / 6f;
            }
            return Mathf.Repeat(hue, 1f);
        }

        // A dash and the gap after it, in pixels, which are units once the sprite is
        // made at one pixel per unit. Long enough to read as a dashed line at the
        // width of a two-unit bar, short enough that a row's edge fits several.
        private const int DashLit = 7;
        private const int DashGap = 5;

        /// <summary>
        /// The dashes the frame round the source row is drawn with, running along
        /// the bar or down it.
        ///
        /// Two textures rather than one rotated: a tiled Image repeats along its own
        /// width, so the dashes in a vertical bar have to be drawn down a vertical
        /// texture.
        /// </summary>
        public static Texture2D Dashes(bool down, int thickness)
        {
            int along = DashLit + DashGap;
            int width = down ? thickness : along;
            int height = down ? along : thickness;

            Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Point;

            Color32[] pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool lit = (down ? y : x) < DashLit;
                    pixels[y * width + x] = new Color32(255, 255, 255,
                                                        lit ? (byte)255 : (byte)0);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        /// <summary>
        /// A triangle pointing right: the head of the arrow the history window draws
        /// from one machine to the other.
        /// </summary>
        public static Texture2D Head(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            Color32[] pixels = new Color32[size * size];
            float step = 1f / (size * Samples);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < Samples; sy++)
                    {
                        for (int sx = 0; sx < Samples; sx++)
                        {
                            float u = (x * Samples + sx + 0.5f) * step;
                            float v = (y * Samples + sy + 0.5f) * step;
                            // Base up the left edge, tip at the middle of the right.
                            if (Mathf.Abs(v - 0.5f) <= (1f - u) * 0.5f)
                            {
                                hits++;
                            }
                        }
                    }
                    pixels[y * size + x] = new Color32(255, 255, 255,
                                                       (byte)(255 * hits / (Samples * Samples)));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        /// <summary>
        /// The branch with a plus in its corner: the button that adds this machine
        /// to a comparison. The same glyph as the history button next to it,
        /// because it is the same mod and the same idea -- with the one mark that
        /// says this one adds something.
        /// </summary>
        public static Texture2D Plus(int size, Font font)
        {
            return Render(size, GlyphBranch, "+", font);
        }

        /// <summary>
        /// The branch with a number in its bottom-right corner: what the plus
        /// becomes once a machine has been added, saying where it comes in the
        /// order.
        /// </summary>
        public static Texture2D Numbered(int size, int number, Font font)
        {
            return Render(size, GlyphBranch, number.ToString(), font);
        }

        /// <summary>
        /// What Besiege's own top-bar buttons are drawn on: a nearly black rounded
        /// square. Sampled off one of the load buttons rather than guessed.
        /// </summary>
        private static readonly Color32 PlateInk = new Color32(2, 7, 13, 255);

        /// <summary>
        /// Barely rounded. Besiege's own button plates are square-cornered -- read
        /// off a screenshot, a pixel at a time -- and this was visibly the odd one
        /// out in the row at a fifth of its width.
        /// </summary>
        private const float PlateRadius = 0.04f;

        /// <summary>
        /// What a tooltip is made of, sampled off the load screen's own: a lighter
        /// plate than a button's, and the game's teal for the words.
        /// </summary>
        private static readonly Color32 TipPlate = new Color32(28, 29, 33, 255);
        private static readonly Color32 TipInk = new Color32(96, 220, 192, 255);

        /// <summary>How much of the plate the glyph on it takes up.</summary>
        private const float PlateGlyph = 0.62f;

        /// <summary>
        /// The branch on a plate, for the row of buttons at the top of the load
        /// screen.
        ///
        /// The plate has to be drawn rather than left to the button, because a load
        /// button's picture and the dark square under it are one texture on one
        /// quad: replacing the picture takes the square with it, and the button
        /// comes out as the one bare glyph in a row of plated ones. Everything
        /// through the icon is drawn opaque, so what is behind the plate cannot show
        /// through the corners either.
        /// </summary>
        public static Texture2D Plated(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            Color32[] pixels = new Color32[size * size];
            float step = 1f / (size * Samples);
            float half = PlateGlyph * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int onPlate = 0;
                    int onGlyph = 0;
                    for (int sy = 0; sy < Samples; sy++)
                    {
                        for (int sx = 0; sx < Samples; sx++)
                        {
                            float u = (x * Samples + sx + 0.5f) * step;
                            float v = (y * Samples + sy + 0.5f) * step;
                            if (InsidePlate(u, v))
                            {
                                onPlate++;
                            }
                            if (InsideBranch((u - 0.5f) / PlateGlyph + 0.5f,
                                             (v - 0.5f) / PlateGlyph + 0.5f) &&
                                Mathf.Abs(u - 0.5f) < half && Mathf.Abs(v - 0.5f) < half)
                            {
                                onGlyph++;
                            }
                        }
                    }

                    int all = Samples * Samples;
                    float ink = (float)onGlyph / all;
                    pixels[y * size + x] = new Color32(
                        (byte)Mathf.Lerp(PlateInk.r, 255f, ink),
                        (byte)Mathf.Lerp(PlateInk.g, 255f, ink),
                        (byte)Mathf.Lerp(PlateInk.b, 255f, ink),
                        (byte)(255 * Mathf.Max(onPlate, onGlyph) / all));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        /// <summary>A rounded square filling the icon.</summary>
        private static bool InsidePlate(float u, float v)
        {
            float dx = Mathf.Abs(u - 0.5f);
            float dy = Mathf.Abs(v - 0.5f);
            float straight = 0.5f - PlateRadius;
            if (dx > 0.5f || dy > 0.5f)
            {
                return false;
            }
            if (dx <= straight || dy <= straight)
            {
                return true;
            }
            float cornerX = dx - straight;
            float cornerY = dy - straight;
            return cornerX * cornerX + cornerY * cornerY <= PlateRadius * PlateRadius;
        }

        // ------------------------------------------------------------- the tooltip

        // How much air is left around the words and how big the arrow on top of the
        // plate is, both as a share of the height of the lettering. Measured off
        // Besiege's own tooltip rather than chosen: it is a wide, shallow plate with
        // a good deal more room at the ends than above and below.
        private const float TipPadX = 1.4f;
        private const float TipPadY = 0.85f;
        private const float TipArrow = 0.5f;

        /// <summary>How tall the words are drawn before the quad scales them.</summary>
        private const int TipPoints = 48;

        /// <summary>
        /// A line of words on a plate, as a picture: what the compare-them-all
        /// button says while the pointer is on it.
        ///
        /// Drawn rather than written into a <c>TextMesh</c> for the reason the whole
        /// of this file exists -- a texture on a quad of our own is the one way of
        /// putting something on the load screen that has been seen to work. The
        /// tooltip Besiege would have lent us belongs to the button we copied and
        /// cannot be pointed anywhere else; a TextMesh copied off the screen brings
        /// a material, a property block and a scale that are all somebody else's.
        ///
        /// Null if the font cannot draw it, and the button then has no tooltip,
        /// which is a good deal better than a blank plate.
        /// </summary>
        public static Texture2D Words(string text, Font font)
        {
            if (font == null || string.IsNullOrEmpty(text))
            {
                return null;
            }

            CharacterInfo[] glyphs;
            float[] pen;
            if (!LayOut(text, font, TipPoints, out glyphs, out pen))
            {
                return null;
            }
            Atlas atlas = ReadAtlas(font);
            if (atlas == null)
            {
                return null;
            }

            float left, right, bottom, top;
            Extent(glyphs, pen, out left, out right, out bottom, out top);
            float high = top - bottom;
            if (right - left < 1f || high < 1f)
            {
                return null;
            }

            int padX = Mathf.RoundToInt(high * TipPadX);
            int padY = Mathf.RoundToInt(high * TipPadY);
            int width = Mathf.RoundToInt(right - left) + padX * 2;
            int plate = Mathf.RoundToInt(high) + padY * 2;
            int arrow = Mathf.RoundToInt(plate * TipArrow);
            int height = plate + arrow;

            // The plate, and above it the little triangle that points at the button
            // the tooltip belongs to -- which is what makes it Besiege's tooltip
            // rather than a caption that happens to be underneath something.
            Color32[] pixels = new Color32[width * height];
            Color32 clear = new Color32(TipPlate.r, TipPlate.g, TipPlate.b, 0);
            for (int y = 0; y < height; y++)
            {
                float point = arrow <= 0 ? 1f : (float)(y - plate) / arrow;
                float half = point <= 0f ? 0f : (1f - point) * arrow;
                for (int x = 0; x < width; x++)
                {
                    bool ink = y < plate ||
                               Mathf.Abs(x + 0.5f - width * 0.5f) <= half;
                    pixels[y * width + x] = ink ? TipPlate : clear;
                }
            }

            for (int i = 0; i < text.Length; i++)
            {
                Draw(pixels, width, height, atlas, glyphs[i],
                     padX + pen[i] + glyphs[i].minX - left,
                     padY + glyphs[i].minY - bottom, 1f, TipInk);
            }

            Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static Texture2D Render(int size, int glyph, string corner, Font font)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            Color32[] pixels = new Color32[size * size];
            float step = 1f / (size * Samples);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < Samples; sy++)
                    {
                        for (int sx = 0; sx < Samples; sx++)
                        {
                            float u = (x * Samples + sx + 0.5f) * step;
                            float v = (y * Samples + sy + 0.5f) * step;
                            if (Inside(u, v, glyph))
                            {
                                hits++;
                            }
                        }
                    }
                    byte alpha = (byte)(255 * hits / (Samples * Samples));
                    // SetPixels32 fills with y running up, which is the order this
                    // loop produces, so the shape is not drawn upside down.
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            if (!string.IsNullOrEmpty(corner) && !StampFont(pixels, size, corner, font))
            {
                Stamp(pixels, size, corner);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        // The shape, in a unit square with y running up.
        //
        //   (0.30,0.86) o        o (0.72,0.86)    two commits at the top
        //               |       /
        //               |      |                  the branch, a quarter turn
        //               |     /
        //               |----'                    leaving the trunk at 0.44
        //               |
        //   (0.30,0.14) o                         where the trunk starts
        //
        // The arc's centre is the *top of the trunk*, not the fork: a quarter
        // circle from there leaves the trunk horizontally and arrives at the far
        // commit vertically, so both joins are tangent and neither shows. Centring
        // it on the fork instead — the first thing I tried — leaves the branch
        // climbing almost parallel to the trunk for half its length, and the two
        // read as one thick smudge.
        private const float TrunkX = 0.30f;
        private const float BranchX = 0.72f;
        private const float BottomY = 0.14f;
        private const float TopY = 0.86f;
        private const float NodeRadius = 0.125f;
        private const float Stroke = 0.075f;

        private static bool Inside(float u, float v, int glyph)
        {
            return InsideBranch(u, v);
        }

        private static bool InsideBranch(float u, float v)
        {
            // The trunk, from commit to commit.
            if (NearSegment(u, v, TrunkX, BottomY, TrunkX, TopY, Stroke))
            {
                return true;
            }

            // The branch: a quarter turn out of the trunk and up to its own commit.
            if (NearArc(u, v, TrunkX, TopY, BranchX - TrunkX, Stroke))
            {
                return true;
            }

            return Disc(u, v, TrunkX, BottomY, NodeRadius)
                || Disc(u, v, TrunkX, TopY, NodeRadius)
                || Disc(u, v, BranchX, TopY, NodeRadius);
        }

        // ------------------------------------------------------------- the number

        /// <summary>
        /// The digits, three across and five down, a row per nibble from the top.
        /// A font of five hundred glyphs is not worth loading, rendering and
        /// keeping alive to write "2" in the corner of an icon.
        /// </summary>
        /// <summary>The plus, in the same three-by-five grid as the digits.</summary>
        private const int PlusBits = 0x05D0; // 000 010 111 010 000

        private static readonly int[] DigitBits =
        {
            0x7B6F, // 0  111 101 101 101 111
            0x2493, // 1  010 010 010 010 011  -- with a foot, so it is not a bar
            0x73E7, // 2  111 001 111 100 111
            0x73CF, // 3  111 001 111 001 111
            0x5BC9, // 4  101 101 111 001 001
            0x79CF, // 5  111 100 111 001 111
            0x79EF, // 6  111 100 111 101 111
            0x7249, // 7  111 001 001 001 001
            0x7BEF, // 8  111 101 111 101 111
            0x7BCF  // 9  111 101 111 001 111
        };

        private const int DigitColumns = 3;
        private const int DigitRows = 5;

        // Where the number sits, as a fraction of the icon: hard into the
        // bottom-right corner, tall enough to read at a slot button's size.
        //
        // That corner and not the top one, which is where this started and where
        // the branch already is: the trunk climbs the left, the arc leaves it for
        // a commit at the top right, and a number written over that has the glyph
        // running through it. The bottom right is the one quarter of the square
        // the branch never reaches -- the arc's lowest point is at 0.44 and the
        // trunk stops at x = 0.30 -- so the two marks are simply beside each other.
        private const float NumberRight = 0.97f;
        private const float NumberBottom = 0.03f;
        private const float NumberHeight = 0.40f;
        private const float NumberGap = 0.05f;

        /// <summary>
        /// Writes a number into the corner of an icon that has already been drawn.
        ///
        /// It overwrites rather than draws over: every pixel of the number's own box
        /// is set, to the digit or to nothing at all. A number in the same white as
        /// the glyph, laid on top of the glyph, is not a number anybody can read --
        /// and clearing the box first is what makes the corner its own space.
        /// </summary>
        private static void Stamp(Color32[] pixels, int size, string text)
        {
            float cell = NumberHeight / DigitRows;
            float width = cell * DigitColumns;
            float total = text.Length * width + (text.Length - 1) * NumberGap * cell;
            float left = NumberRight - total;
            float bottom = NumberBottom;
            float top = NumberBottom + NumberHeight;

            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) / size;
                if (v < bottom - cell * 0.5f || v > top + cell * 0.5f)
                {
                    continue;
                }
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    if (u < left - cell * 0.5f || u > NumberRight + cell * 0.5f)
                    {
                        continue;
                    }
                    pixels[y * size + x] = new Color32(255, 255, 255,
                        Lit(text, u, v, left, bottom, cell, width) ? (byte)255 : (byte)0);
                }
            }
        }

        private static bool Lit(string text, float u, float v, float left, float bottom,
                                float cell, float width)
        {
            for (int i = 0; i < text.Length; i++)
            {
                float x0 = left + i * (width + NumberGap * cell);
                if (u < x0 || u >= x0 + width)
                {
                    continue;
                }
                int shape = Shape(text[i]);
                if (shape == 0)
                {
                    return false;
                }
                int column = (int)((u - x0) / cell);
                // Rows are written top first; v runs up.
                int row = DigitRows - 1 - (int)((v - bottom) / cell);
                if (column < 0 || column >= DigitColumns || row < 0 || row >= DigitRows)
                {
                    return false;
                }
                int bit = (DigitRows - 1 - row) * DigitColumns + (DigitColumns - 1 - column);
                return (shape & (1 << bit)) != 0;
            }
            return false;
        }

        // ------------------------------------------------------- the number, in the
        //                                                          game's own font

        /// <summary>
        /// Writes the corner mark in Besiege's UI font -- the font the history
        /// window is written in -- by copying the glyphs straight out of the font's
        /// atlas.
        ///
        /// The three-by-five bitmap below it is still there and is still what draws
        /// this if anything goes wrong, but a number beside a machine's picture is
        /// read as text and looked pixelled next to everything else on the screen.
        /// A <c>TextMesh</c> parented to the mark would have been the obvious way
        /// and is worse: it is a second object to place, scale, hide and destroy in
        /// step with the first, and its size in world units cannot be known without
        /// rendering it. Copied into the picture, the number is part of the picture.
        ///
        /// Returns false if the font cannot supply what is needed -- no font, no
        /// atlas, a character it does not have, or a glyph packed sideways -- and
        /// the caller then draws the bitmap.
        /// </summary>
        private static bool StampFont(Color32[] pixels, int size, string text, Font font)
        {
            if (font == null || text.Length == 0)
            {
                return false;
            }

            int points = Mathf.Max(8, Mathf.RoundToInt(size * NumberHeight));
            CharacterInfo[] glyphs;
            float[] pen;
            if (!LayOut(text, font, points, out glyphs, out pen))
            {
                return false;
            }

            float left, right, bottom, top;
            Extent(glyphs, pen, out left, out right, out bottom, out top);
            if (right - left < 1f || top - bottom < 1f)
            {
                return false;
            }

            Atlas atlas = ReadAtlas(font);
            if (atlas == null)
            {
                return false;
            }

            float scale = NumberHeight * size / (top - bottom);
            float toRight = NumberRight * size;
            float toBottom = NumberBottom * size;
            float toLeft = toRight - (right - left) * scale;
            float toTop = toBottom + NumberHeight * size;

            Clear(pixels, size, toLeft, toBottom, toRight, toTop);
            for (int i = 0; i < text.Length; i++)
            {
                Draw(pixels, size, size, atlas, glyphs[i],
                     toLeft + (pen[i] + glyphs[i].minX - left) * scale,
                     toBottom + (glyphs[i].minY - bottom) * scale, scale,
                     new Color32(255, 255, 255, 255));
            }
            return true;
        }

        /// <summary>
        /// Asks the font for every character of a line and where each one sits along
        /// it. False if the font has no glyph for one of them, or packs it into its
        /// atlas turned on its side.
        /// </summary>
        private static bool LayOut(string text, Font font, int points,
                                   out CharacterInfo[] glyphs, out float[] pen)
        {
            glyphs = new CharacterInfo[text.Length];
            pen = new float[text.Length];
            try
            {
                font.RequestCharactersInTexture(text, points, FontStyle.Bold);
                float x = 0f;
                for (int i = 0; i < text.Length; i++)
                {
                    if (!font.GetCharacterInfo(text[i], out glyphs[i], points,
                                               FontStyle.Bold) || Sideways(glyphs[i]))
                    {
                        return false;
                    }
                    pen[i] = x;
                    x += glyphs[i].advance;
                }
            }
            catch (System.Exception e)
            {
                Log.Warn("could not lay text out in Besiege's font: " + e.Message);
                return false;
            }
            return true;
        }

        /// <summary>
        /// The box the ink of a laid-out line actually fills -- not the line box.
        /// Line boxes carry the font's leading, so a "1" fitted by its line box comes
        /// out visibly smaller than a "4" fitted the same way.
        /// </summary>
        private static void Extent(CharacterInfo[] glyphs, float[] pen, out float left,
                                   out float right, out float bottom, out float top)
        {
            left = pen[0] + glyphs[0].minX;
            right = pen[0] + glyphs[0].maxX;
            bottom = glyphs[0].minY;
            top = glyphs[0].maxY;
            for (int i = 1; i < glyphs.Length; i++)
            {
                left = Mathf.Min(left, pen[i] + glyphs[i].minX);
                right = Mathf.Max(right, pen[i] + glyphs[i].maxX);
                bottom = Mathf.Min(bottom, glyphs[i].minY);
                top = Mathf.Max(top, glyphs[i].maxY);
            }
        }

        /// <summary>
        /// Whether a glyph is packed into the atlas turned on its side, which the
        /// font is free to do and this is not going to unpick.
        /// </summary>
        private static bool Sideways(CharacterInfo glyph)
        {
            return Mathf.Abs(glyph.uvBottomLeft.x - glyph.uvTopLeft.x) > 0.0001f;
        }

        /// <summary>Blanks the box the number is about to be written in.</summary>
        private static void Clear(Color32[] pixels, int size, float left, float bottom,
                                  float right, float top)
        {
            float margin = size * 0.03f;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(left - margin));
            int x1 = Mathf.Min(size - 1, Mathf.CeilToInt(right + margin));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(bottom - margin));
            int y1 = Mathf.Min(size - 1, Mathf.CeilToInt(top + margin));
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    pixels[y * size + x] = new Color32(255, 255, 255, 0);
                }
            }
        }

        /// <summary>
        /// Copies one glyph out of the atlas and into the icon, sampled smoothly
        /// because the two are never the same size.
        /// </summary>
        private static void Draw(Color32[] pixels, int width, int height, Atlas atlas,
                                 CharacterInfo glyph, float atX, float atY, float scale,
                                 Color32 ink)
        {
            float wide = (glyph.maxX - glyph.minX) * scale;
            float high = (glyph.maxY - glyph.minY) * scale;
            if (wide < 0.5f || high < 0.5f)
            {
                return;
            }

            // Where the glyph is in the atlas, in the atlas's own pixels.
            float u0 = glyph.uvBottomLeft.x * atlas.Width;
            float v0 = glyph.uvBottomLeft.y * atlas.Height;
            float u1 = glyph.uvTopRight.x * atlas.Width;
            float v1 = glyph.uvTopRight.y * atlas.Height;

            int x0 = Mathf.Max(0, Mathf.FloorToInt(atX));
            int x1 = Mathf.Min(width - 1, Mathf.CeilToInt(atX + wide));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(atY));
            int y1 = Mathf.Min(height - 1, Mathf.CeilToInt(atY + high));

            for (int y = y0; y <= y1; y++)
            {
                float fy = (y + 0.5f - atY) / high;
                if (fy < 0f || fy > 1f)
                {
                    continue;
                }
                for (int x = x0; x <= x1; x++)
                {
                    float fx = (x + 0.5f - atX) / wide;
                    if (fx < 0f || fx > 1f)
                    {
                        continue;
                    }
                    byte covered = atlas.Sample(Mathf.Lerp(u0, u1, fx),
                                                Mathf.Lerp(v0, v1, fy));
                    if (covered == 0)
                    {
                        continue;
                    }
                    // Laid over whatever is there rather than written on top of it:
                    // on an icon that is nothing, and on a tooltip it is the plate,
                    // which the letters have to be readable against.
                    Color32 was = pixels[y * width + x];
                    float share = covered / 255f;
                    pixels[y * width + x] = new Color32(
                        (byte)Mathf.Lerp(was.r, ink.r, share),
                        (byte)Mathf.Lerp(was.g, ink.g, share),
                        (byte)Mathf.Lerp(was.b, ink.b, share),
                        was.a > covered ? was.a : covered);
                }
            }
        }

        /// <summary>
        /// A readable copy of a font's atlas.
        ///
        /// Through a RenderTexture for the same reason as <see cref="Dim"/>: the
        /// atlas belongs to the font, is very often not readable, and asking is not
        /// something that can be got right from out here.
        /// </summary>
        private class Atlas
        {
            public int Width;
            public int Height;
            public Color32[] Pixels;

            /// <summary>The coverage at a point, between the four pixels around it.</summary>
            public byte Sample(float x, float y)
            {
                float fx = Mathf.Clamp(x - 0.5f, 0f, Width - 1f);
                float fy = Mathf.Clamp(y - 0.5f, 0f, Height - 1f);
                int x0 = (int)fx;
                int y0 = (int)fy;
                int x1 = Mathf.Min(x0 + 1, Width - 1);
                int y1 = Mathf.Min(y0 + 1, Height - 1);
                float tx = fx - x0;
                float ty = fy - y0;

                float bottom = Mathf.Lerp(Pixels[y0 * Width + x0].a,
                                          Pixels[y0 * Width + x1].a, tx);
                float top = Mathf.Lerp(Pixels[y1 * Width + x0].a,
                                       Pixels[y1 * Width + x1].a, tx);
                return (byte)Mathf.Clamp(Mathf.Lerp(bottom, top, ty), 0f, 255f);
            }
        }

        private static Atlas ReadAtlas(Font font)
        {
            Texture source = font.material == null ? null : font.material.mainTexture;
            if (source == null || source.width <= 0 || source.height <= 0)
            {
                return null;
            }

            RenderTexture copy = null;
            RenderTexture was = RenderTexture.active;
            Texture2D readable = null;
            try
            {
                copy = RenderTexture.GetTemporary(source.width, source.height, 0);
                Graphics.Blit(source, copy);
                RenderTexture.active = copy;

                readable = new Texture2D(source.width, source.height,
                                         TextureFormat.ARGB32, false);
                readable.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0,
                                    false);
                readable.Apply(false, false);

                Atlas atlas = new Atlas();
                atlas.Width = source.width;
                atlas.Height = source.height;
                atlas.Pixels = readable.GetPixels32();
                return atlas;
            }
            catch (System.Exception e)
            {
                Log.Warn("could not read the font's atlas (" + e.Message +
                         "); drawing the number as a bitmap.");
                return null;
            }
            finally
            {
                RenderTexture.active = was;
                if (copy != null)
                {
                    RenderTexture.ReleaseTemporary(copy);
                }
                if (readable != null)
                {
                    Object.Destroy(readable);
                }
            }
        }

        /// <summary>The pattern for one character, or 0 for one we cannot draw.</summary>
        private static int Shape(char c)
        {
            if (c == '+')
            {
                return PlusBits;
            }
            return c >= '0' && c <= '9' ? DigitBits[c - '0'] : 0;
        }

        // ---------------------------------------------------------- the thumbnail

        /// <summary>
        /// A machine's thumbnail as it looks once the machine has been chosen:
        /// darker, and blurred by being small.
        ///
        /// The blur is the cheapest one there is and the only one available -- a
        /// blur shader is not something a mod can count on being in the player's
        /// build. Drawing the picture into a very small texture averages it down,
        /// and the bilinear filter stretching it back over the slot is what turns
        /// that into a blur rather than a mosaic.
        ///
        /// Through a RenderTexture rather than <c>GetPixels</c> because the source
        /// belongs to the game and may well have been loaded unreadable, which
        /// nothing out here can ask about without being wrong sooner or later.
        /// </summary>
        public static Texture2D Dim(Texture source, int size, float darken)
        {
            if (source == null)
            {
                return null;
            }

            RenderTexture small = null;
            RenderTexture was = RenderTexture.active;
            try
            {
                small = RenderTexture.GetTemporary(size, size, 0);
                small.filterMode = FilterMode.Bilinear;
                Graphics.Blit(source, small);
                RenderTexture.active = small;

                Texture2D copy = new Texture2D(size, size, TextureFormat.ARGB32, false);
                copy.ReadPixels(new Rect(0f, 0f, size, size), 0, 0, false);

                Color32[] pixels = copy.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = new Color32((byte)(pixels[i].r * darken),
                                            (byte)(pixels[i].g * darken),
                                            (byte)(pixels[i].b * darken),
                                            pixels[i].a);
                }
                copy.SetPixels32(pixels);
                copy.Apply(false, false);
                copy.wrapMode = TextureWrapMode.Clamp;
                copy.filterMode = FilterMode.Bilinear;
                copy.hideFlags = HideFlags.HideAndDontSave;
                return copy;
            }
            catch (System.Exception e)
            {
                Log.Warn("could not dim a thumbnail: " + e.Message);
                return null;
            }
            finally
            {
                RenderTexture.active = was;
                if (small != null)
                {
                    RenderTexture.ReleaseTemporary(small);
                }
            }
        }

        private static bool Disc(float u, float v, float cx, float cy, float radius)
        {
            float dx = u - cx;
            float dy = v - cy;
            return dx * dx + dy * dy <= radius * radius;
        }

        private static bool NearSegment(float u, float v, float ax, float ay,
                                        float bx, float by, float stroke)
        {
            float dx = bx - ax;
            float dy = by - ay;
            float lengthSquared = dx * dx + dy * dy;
            float t = lengthSquared <= 0f
                ? 0f
                : Mathf.Clamp01(((u - ax) * dx + (v - ay) * dy) / lengthSquared);
            float px = ax + dx * t;
            float py = ay + dy * t;
            return Disc(u, v, px, py, stroke * 0.5f);
        }

        /// <summary>
        /// The lower-right quarter of a circle: the only part of the arc that is
        /// wanted, from where it leaves the trunk to where it reaches the commit.
        /// </summary>
        private static bool NearArc(float u, float v, float cx, float cy,
                                    float radius, float stroke)
        {
            if (u < cx || v > cy)
            {
                return false;
            }
            float distance = Mathf.Sqrt((u - cx) * (u - cx) + (v - cy) * (v - cy));
            return Mathf.Abs(distance - radius) <= stroke * 0.5f;
        }
    }
}
