using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace GitView
{
    /// <summary>
    /// One block, reduced to the things a diff can be about: what it is, where it
    /// is, and how it is set up.
    ///
    /// Deliberately a plain object rather than the game's <c>BlockInfo</c>. The
    /// diff is the part of this mod that is actually right or wrong, so it is kept
    /// free of anything that needs a running game and is covered by the headless
    /// tests. <see cref="MachineSnapshot"/> is what converts the game's model into
    /// these.
    /// </summary>
    public class BlockRecord
    {
        /// <summary>
        /// The block's own identity, as Besiege writes it into the .bsg.
        ///
        /// It is per-block, not per-block-type -- two identical girders have
        /// different guids -- which makes it the natural key for "the same block,
        /// one save later". It is not quite stable, though: see
        /// <see cref="BlockDiff"/> for what happens when it changes underneath a
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
        /// The block's settings -- slider values, toggles, key bindings -- flattened
        /// into one string so they can be compared without knowing what any
        /// particular block keeps in there.
        /// </summary>
        public string Settings = string.Empty;

        /// <summary>
        /// How far two positions may differ and still count as the same place.
        ///
        /// Besiege writes coordinates as decimal text and reads them back through a
        /// fast float parser, so a block that was never touched can come back a
        /// hair off. A thousandth of a block is far below anything a player can
        /// place by hand and far above that noise.
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
        /// A key that is equal exactly when two blocks are interchangeable. Used to
        /// pair up blocks whose guid changed but which are otherwise identical, so
        /// it has to quantise the floats that <see cref="Matches"/> compares with a
        /// tolerance -- two values within epsilon can still land either side of a
        /// bucket edge, which costs an exact match and falls through to the
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
        /// Decimal places kept when a setting's value goes into
        /// <see cref="Settings"/>.
        ///
        /// Settings are compared as text, so they need the same tolerance the
        /// transform gets, applied before the comparison rather than during it.
        /// Some of Besiege's block settings hold live physics values -- a piston's
        /// start and end positions, say -- and those come back a little different
        /// every save even when nobody has touched the block: 5.96047E-08 one
        /// minute and 2.842171E-14 the next, both of them zero to anyone but a
        /// float. Comparing the text as written reports that block as changed once
        /// a minute forever.
        ///
        /// Four places is finer than any slider in the game moves and coarser than
        /// anything physics wobbles by.
        /// </summary>
        public const int SettingsDecimals = 4;

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
