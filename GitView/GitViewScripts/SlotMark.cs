using UnityEngine;

namespace GitView
{
    /// <summary>
    /// What this mod has done to one slot of the load screen, kept on the slot.
    ///
    /// A slot is reusable: turn the page and it is a different machine, keeping
    /// every child anything parented to it. So the mark has to say which machine it
    /// was for -- <see cref="Path"/> -- or page two comes up wearing page one's
    /// numbers, and that is why the choosing lives in <see cref="Selection"/>.
    /// Fields only; <c>BrowserWatch</c> acts on them.
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
        /// What the slot was drawing before, and the only reliable way back: the
        /// browser's own cache of the picture is not always filled in.
        /// </summary>
        public Texture Original;
    }
}
