using UnityEngine;

namespace GitView
{
    /// <summary>
    /// Measuring one transform against another.
    ///
    /// Both places this mod copies something -- a mesh off a block of the machine,
    /// a tooltip off one of Besiege's own buttons -- have to put the copy under a
    /// parent of a different size from the original's, and the same arithmetic
    /// answers both.
    /// </summary>
    public static class Relative
    {
        /// <summary>
        /// One scale as a fraction of another, per axis: what a child has to be set
        /// to under <paramref name="by"/> to come out the size <paramref name="want"/>
        /// is. An axis flattened to nothing has no answer, and 1 is the harmless one.
        /// </summary>
        public static Vector3 Scale(Vector3 want, Vector3 by)
        {
            return new Vector3(Mathf.Abs(by.x) < 0.0001f ? 1f : want.x / by.x,
                               Mathf.Abs(by.y) < 0.0001f ? 1f : want.y / by.y,
                               Mathf.Abs(by.z) < 0.0001f ? 1f : want.z / by.z);
        }
    }
}
