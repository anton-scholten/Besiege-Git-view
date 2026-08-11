using UnityEngine;

namespace GitView
{
    /// <summary>
    /// The three colours a diff is drawn in, and the one place they are kept.
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
    /// Held in memory for the session. Nothing here is written to disk: a mod's
    /// only sanctioned route to a file is through the game's own save data, and a
    /// preference about colours is not worth attaching to anybody's machine.
    ///
    /// Categories are ints rather than an enum because Besiege's in-game compiler
    /// segfaults on an enum declaration. See docs/MODDING-NOTES.md.
    /// </summary>
    public static class DiffPalette
    {
        public const int Added = 0;
        public const int Changed = 1;
        public const int Removed = 2;
        public const int Categories = 3;

        /// <summary>
        /// The colours as they start out: Besiege's own green, amber and red, at
        /// the opacity a shell reads well at against the sky and against a machine.
        /// </summary>
        private static readonly Color[] Fallbacks =
        {
            new Color(0.28f, 0.88f, 0.42f, 0.42f),
            new Color(1.00f, 0.64f, 0.13f, 0.45f),
            new Color(0.95f, 0.27f, 0.30f, 0.38f)
        };

        private static readonly Color[] Current =
        {
            new Color(0.28f, 0.88f, 0.42f, 0.42f),
            new Color(1.00f, 0.64f, 0.13f, 0.45f),
            new Color(0.95f, 0.27f, 0.30f, 0.38f)
        };

        private static readonly string[] Names = { "ADDED", "CHANGED", "REMOVED" };

        /// <summary>The colour a category is drawn in, opacity included.</summary>
        public static Color Of(int category)
        {
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

        public static void Set(int category, Color colour)
        {
            if (Valid(category))
            {
                Current[category] = colour;
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
