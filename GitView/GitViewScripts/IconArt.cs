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

        /// <summary>
        /// The branch glyph, white on transparent, so that whatever tint it is
        /// drawn with is what colours it.
        /// </summary>
        public static Texture2D Branch(int size)
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

        private static bool Inside(float u, float v)
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
