using UnityEngine;

namespace GitView
{
    /// <summary>
    /// Draws the compare button's face into a texture at runtime.
    ///
    /// Generated rather than shipped as a PNG: it cannot go missing from an
    /// install, it costs nothing at this size, and it sidesteps the case-sensitive
    /// resource loading that catches Workshop mods authored on Windows. UI Factory's
    /// sprite set is Besiege's own HUD sprites, so there is no ready-made icon to
    /// borrow for this.
    ///
    /// Drawn as line art rather than a silhouette, to sit beside the letter-like
    /// glyphs on Besiege's other slot buttons: two overlapping frames, the older
    /// version behind and the newer in front, which is what the button does.
    /// </summary>
    public static class IconArt
    {
        private const int Samples = 4;

        /// <summary>
        /// The comparison glyph, white on transparent, so that whatever tint the
        /// button's own material carries is what colours it.
        /// </summary>
        public static Texture2D Compare(int size)
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
                            if (Inside(u, v))
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

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        // The shape, in a unit square. Two frames of the same size, the back one up
        // and left, the front one down and right, with the front one's stroke
        // knocking a gap out of the back one so the overlap reads as depth rather
        // than as a smudge.
        private const float Side = 0.56f;
        private const float Stroke = 0.085f;
        private const float Shift = 0.16f;
        private const float Gap = 0.045f;

        private static bool Inside(float u, float v)
        {
            float backLeft = 0.5f - Side * 0.5f - Shift * 0.5f;
            float backBottom = 0.5f - Side * 0.5f + Shift * 0.5f;
            float frontLeft = backLeft + Shift;
            float frontBottom = backBottom - Shift;

            bool front = Frame(u, v, frontLeft, frontBottom, Stroke);
            if (front)
            {
                return true;
            }
            if (Solid(u, v, frontLeft - Gap, frontBottom - Gap, Side + Gap * 2f))
            {
                return false;
            }
            return Frame(u, v, backLeft, backBottom, Stroke);
        }

        private static bool Frame(float u, float v, float left, float bottom, float stroke)
        {
            if (!Solid(u, v, left, bottom, Side))
            {
                return false;
            }
            return !Solid(u, v, left + stroke, bottom + stroke, Side - stroke * 2f);
        }

        private static bool Solid(float u, float v, float left, float bottom, float side)
        {
            return u >= left && u <= left + side && v >= bottom && v <= bottom + side;
        }
    }
}
