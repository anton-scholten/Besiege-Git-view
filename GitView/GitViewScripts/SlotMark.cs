using UnityEngine;

namespace GitView
{
    /// <summary>
    /// What this mod has done to one slot of the load screen, kept on the slot
    /// itself.
    ///
    /// A slot is a reusable thing: the browser turns a page and the same slot is
    /// suddenly a different machine, keeping every child anything has parented to
    /// it. So the mark it is wearing has to be able to say which machine it was
    /// for, or the second page of a folder comes up wearing the first page's
    /// numbers. <see cref="Path"/> is that check, and it is why the choosing itself
    /// lives in <see cref="Selection"/> and not here.
    ///
    /// Fields only. Everything that acts on these is in <c>BrowserWatch</c>, which
    /// is the one thing that knows when the browser has changed under it.
    /// </summary>
    public class SlotMark : MonoBehaviour
    {
        /// <summary>The machine this mark is about.</summary>
        public string Path = string.Empty;

        /// <summary>Where it comes in the choosing order, or 0 if it is not chosen.</summary>
        public int Ordinal;

        /// <summary>The numbered mark drawn over the thumbnail.</summary>
        public GameObject Badge;

        /// <summary>The dimmed picture we put on the slot, ours to destroy.</summary>
        public Texture2D Dimmed;

        /// <summary>
        /// What the slot was drawing before that, and the only reliable way back to
        /// it: by the time a machine is unchosen it is out of the selection, and the
        /// browser's own cache of the picture is not always filled in.
        /// </summary>
        public Texture Original;
    }
}
