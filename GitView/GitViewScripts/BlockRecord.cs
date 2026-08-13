using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace GitView
{
    /// <summary>
    /// One block, reduced to the things a diff can be about: what it is, where it
    /// is, how it is set up. A plain object rather than the game's
    /// <c>BlockInfo</c>, so the diff stays engine-free and testable headless.
    /// </summary>
    public class BlockRecord
    {
        /// <summary>
        /// The guid Besiege writes into the .bsg: per block, not per block type, so
        /// it is the natural key for "the same block, one save later". Not quite
        /// stable -- see <see cref="BlockDiff"/> for when it changes underneath a
        /// block that did not.
        /// </summary>
        public string Id = string.Empty;

        /// <summary>The block type, i.e. which block out of the palette this is.</summary>
        public int Kind;

        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale = Vector3.one;
        public bool Flipped;

        /// <summary>The skin applied to the block, empty for the default one.</summary>
        public string SkinName = string.Empty;

        /// <summary>
        /// Slider values, toggles and key bindings, flattened into one string so
        /// they compare without knowing what a given block keeps in there.
        /// </summary>
        public string Settings = string.Empty;

        /// <summary>
        /// True for the blocks that are not in one place: a brace, a fuel line, a
        /// winch, dragged between two points.
        /// </summary>
        public bool HasSpan;

        /// <summary>
        /// The two ends, in the block's own local space, as Besiege writes them and
        /// reads them back through <c>transform.TransformPoint</c> -- so they are
        /// positions only once <see cref="Scale"/> is applied. Not compared by
        /// <see cref="Matches"/>: they are settings, so <see cref="Settings"/>
        /// already carries them. They are kept apart because the overlay has to draw
        /// them and a string is no use for that.
        /// </summary>
        public Vector3 SpanStart;
        public Vector3 SpanEnd;

        /// <summary>
        /// The corners of a build surface, in the order they go round it, in the
        /// same space as <see cref="Position"/>.
        ///
        /// A surface is nine blocks -- itself, four edges, four corner nodes -- each
        /// with its own guid: the surface names its edges, an edge names its two
        /// nodes, and a node is where a corner is. So where a surface *is* can only
        /// be answered by following those references, which <see cref="SurfaceShape"/>
        /// does once every block has been read. Null on everything but the surface.
        /// </summary>
        public Vector3[] Corners;

        /// <summary>True when this is a build surface whose corners were resolved.</summary>
        public bool HasSurface
        {
            get { return Corners != null && Corners.Length >= 3; }
        }

        /// <summary>
        /// How thick the player made the slab. The mark over it has to be thicker
        /// than this or it is drawn inside the block and cannot be seen. Besiege's
        /// own default where the block does not say.
        /// </summary>
        public float Thickness = 0.08f;

        // What a surface and its edges named before they were resolved, kept here
        // because linking happens only once every block has been read.
        public string[] EdgeIds;
        public string EdgeFrom = string.Empty;
        public string EdgeTo = string.Empty;

        /// <summary>
        /// True for the edges and corner nodes of a resolved surface. Counted like
        /// any other block, but not drawn: the surface is drawn as the surface, and
        /// a node's own ghost is the little ball you drag it by.
        /// </summary>
        public bool PartOfSurface;

        /// <summary>
        /// How far two positions may differ and still be the same place. Besiege
        /// writes coordinates as decimal text and reads them back through a fast
        /// float parser, so an untouched block comes back a hair off; a thousandth
        /// of a block is well under what a player can place and well over that noise.
        /// </summary>
        public const float PositionEpsilon = 0.001f;

        /// <summary>Radians-equivalent tolerance for the rotation quaternion.</summary>
        public const float RotationEpsilon = 0.0005f;

        /// <summary>True if the two records describe a block nobody has touched.</summary>
        public bool Matches(BlockRecord other)
        {
            return other != null
                && Kind == other.Kind
                && Flipped == other.Flipped
                && SamePlace(other)
                && Near(Rotation, other.Rotation)
                && Near(Scale, other.Scale)
                && SkinName == other.SkinName
                && Settings == other.Settings;
        }

        /// <summary>True if the two records sit in the same place, to tolerance.</summary>
        public bool SamePlace(BlockRecord other)
        {
            return other != null && Near(Position, other.Position);
        }

        /// <summary>Distance between two records' positions, for nearest-match.</summary>
        public float DistanceTo(BlockRecord other)
        {
            return other == null ? float.MaxValue : (Position - other.Position).magnitude;
        }

        private static bool Near(Vector3 a, Vector3 b)
        {
            return Math.Abs(a.x - b.x) <= PositionEpsilon
                && Math.Abs(a.y - b.y) <= PositionEpsilon
                && Math.Abs(a.z - b.z) <= PositionEpsilon;
        }

        private static bool Near(Quaternion a, Quaternion b)
        {
            // q and -q are the same rotation, so compare the absolute dot product
            // rather than the components: a block flipped through the double cover
            // between two saves has not moved.
            float dot = a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
            return 1f - Math.Abs(dot) <= RotationEpsilon;
        }

        /// <summary>
        /// A key equal exactly when two blocks are interchangeable, for pairing up
        /// blocks whose guid changed but which are otherwise identical. The floats
        /// are quantised, so two values within epsilon can land either side of a
        /// bucket edge -- that costs an exact match and falls through to the
        /// nearest-position pass rather than being wrong.
        /// </summary>
        public string IdentityKey()
        {
            StringBuilder key = new StringBuilder();
            key.Append(Kind).Append('|');
            Round(key, Position.x); Round(key, Position.y); Round(key, Position.z);
            Round(key, Rotation.x); Round(key, Rotation.y);
            Round(key, Rotation.z); Round(key, Rotation.w);
            Round(key, Scale.x); Round(key, Scale.y); Round(key, Scale.z);
            key.Append(Flipped ? "f|" : "-|");
            key.Append(SkinName).Append('|').Append(Settings);
            return key.ToString();
        }

        private static void Round(StringBuilder into, float value)
        {
            into.Append(Mathf.RoundToInt(value * 1000f)).Append(',');
        }

        /// <summary>
        /// Flattens a set of key/value settings into the <see cref="Settings"/>
        /// string. Sorted, because neither the game nor the file promises an order
        /// and a reordering is not a change.
        /// </summary>
        public static string FlattenSettings(List<string> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return string.Empty;
            }
            List<string> sorted = new List<string>(entries);
            sorted.Sort(StringComparer.Ordinal);
            return string.Join(";", sorted.ToArray());
        }

        /// <summary>
        /// Four decimal places, as the scale a value is rounded on before it goes
        /// into <see cref="Settings"/>. Settings are compared as text, so the
        /// tolerance has to be applied first: some hold live physics values that
        /// come back 5.96047E-08 one save and 2.842171E-14 the next, both of them
        /// zero to anyone but a float. Four places is finer than any slider moves
        /// and coarser than anything physics wobbles by.
        /// </summary>
        private const float SettingsScale = 10000f;

        /// <summary>A number as it goes into a settings string: rounded, and the same in any locale.</summary>
        public static string Quantise(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return value.ToString(CultureInfo.InvariantCulture);
            }
            double rounded = Math.Round(value * SettingsScale) / SettingsScale;
            // Negative zero and positive zero are the same number and must produce
            // the same text, or a sign flip in the noise reads as a change.
            if (rounded == 0d)
            {
                rounded = 0d;
            }
            return rounded.ToString("0.####", CultureInfo.InvariantCulture);
        }

        public static string Quantise(float value)
        {
            return Quantise((double)value);
        }
    }
}
