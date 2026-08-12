using UnityEngine;

namespace GitView
{
    /// <summary>
    /// The four colours a diff is drawn in, and the one place they are kept.
    ///
    /// One colour per category rather than two, even though it is used for two
    /// different things -- the counts in the list and the shells over the machine.
    /// Two would be two things to keep in step and one more way for the window to
    /// disagree with the world it is describing. The alpha is what differs: a shell
    /// is translucent because several of them overlap on a dense machine, while
    /// text at less than full opacity is simply harder to read for no gain. So the
    /// player picks one colour with an opacity, the shell honours the opacity and
    /// the text ignores it.
    ///
    /// Remembered between sessions through <see cref="Prefs"/>, and read back the
    /// first time anything asks for a colour rather than at load: the mod is loaded
    /// when the player first enters a level, and there is no sense reading
    /// preferences for a window that may never be opened.
    ///
    /// Categories are ints rather than an enum because Besiege's in-game compiler
    /// segfaults on an enum declaration. See docs/MODDING-NOTES.md.
    /// </summary>
    public static class DiffPalette
    {
        public const int Added = 0;
        public const int Changed = 1;
        public const int Removed = 2;

        /// <summary>
        /// Everything the save left alone -- neither added, changed nor removed.
        /// The one category with no count of its own in the list, because it is not
        /// something a save did; it is what a save did not do.
        /// </summary>
        public const int Unchanged = 3;
        public const int Categories = 4;

        /// <summary>
        /// The colours as they start out: a plain green, yellow and red at the
        /// opacity a shell reads well at against the sky and against a machine, and
        /// a cyan at no opacity at all.
        ///
        /// Flat primaries rather than the muted palette Besiege writes its own
        /// interface in, because these are not interface: two thirds of their work
        /// is done as translucent shells over a brown machine in a blue-green
        /// landscape, where a soft colour disappears.
        ///
        /// Unchanged starts invisible on purpose. It is the one category that can
        /// be most of the machine, so having it on by default would bury the three
        /// that answer the question; it is there for the player who wants to see
        /// what a change is attached to. At zero opacity it costs nothing at all --
        /// see <see cref="Faded"/>.
        /// </summary>
        private static readonly Color[] Fallbacks =
        {
            new Color(0.000f, 1.000f, 0.000f, 0.30f),
            new Color(1.000f, 1.000f, 0.000f, 0.30f),
            new Color(0.996f, 0.000f, 0.000f, 0.30f),
            new Color(0.000f, 1.000f, 1.000f, 0.00f)
        };

        private static readonly Color[] Current = new Color[Categories];
        private static bool _loaded;

        private static readonly string[] Names =
        {
            "ADDED", "CHANGED", "REMOVED", "UNCHANGED"
        };

        /// <summary>The colour a category is drawn in, opacity included.</summary>
        public static Color Of(int category)
        {
            Load();
            return Valid(category) ? Current[category] : Color.white;
        }

        /// <summary>
        /// The same colour for text, which is always fully opaque -- see the class
        /// note. A count at 38% over a dark panel is not a count anybody can read.
        /// </summary>
        public static Color Ink(int category)
        {
            Color colour = Of(category);
            colour.a = 1f;
            return colour;
        }

        /// <summary>
        /// True when this category is turned off rather than merely faint. Asked by
        /// the overlay, which does not spawn shells it cannot show: unchanged blocks
        /// are most of a machine, and hundreds of invisible ones is a hitch every
        /// time a version is clicked in exchange for nothing on screen.
        /// </summary>
        public static bool Faded(int category)
        {
            return Of(category).a <= 0f;
        }

        public static void Set(int category, Color colour)
        {
            Load();
            if (!Valid(category))
            {
                return;
            }
            Current[category] = colour;
            // Stored as it is chosen rather than when the picker is put away: a
            // slider is dragged, and there is no moment afterwards that reliably
            // means "done". Reaching the disk is Prefs.Flush's job, and that is
            // called when the picker closes.
            Prefs.SetColour(category, colour);
        }

        /// <summary>
        /// Reads the stored colours once. Anything never chosen keeps its default,
        /// which is what <see cref="Prefs.Colour"/> falls back to key by key -- so a
        /// player who changed only the green keeps the new defaults for the other
        /// two rather than whatever they were when they last played.
        /// </summary>
        private static void Load()
        {
            if (_loaded)
            {
                return;
            }
            _loaded = true;
            for (int category = 0; category < Categories; category++)
            {
                Current[category] = Prefs.Colour(category, Fallbacks[category]);
            }
        }

        /// <summary>The colour this category started the session with.</summary>
        public static Color Default(int category)
        {
            return Valid(category) ? Fallbacks[category] : Color.white;
        }

        public static string Name(int category)
        {
            return Valid(category) ? Names[category] : string.Empty;
        }

        private static bool Valid(int category)
        {
            return category >= 0 && category < Categories;
        }
    }
}
