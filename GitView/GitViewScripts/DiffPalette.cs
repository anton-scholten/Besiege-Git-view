using UnityEngine;

namespace GitView
{
    /// <summary>
    /// The four colours a diff is drawn in, and the one place they are kept.
    ///
    /// One colour per category for both the things it is used for -- the counts in
    /// the list and the shells over the machine -- so the window cannot disagree
    /// with the world it describes. Only the alpha differs: shells overlap on a
    /// dense machine and want to be translucent, text at less than full opacity is
    /// just harder to read. So the shell honours the opacity and the text ignores it.
    ///
    /// Read back from <see cref="Prefs"/> the first time anything asks, rather than
    /// at load: the window may never be opened. Categories are ints rather than an
    /// enum because Besiege's compiler segfaults on one -- see docs/MODDING-NOTES.md.
    /// </summary>
    public static class DiffPalette
    {
        public const int Added = 0;
        public const int Changed = 1;
        public const int Removed = 2;

        /// <summary>
        /// Everything the save left alone. The one category with no count of its
        /// own, because it is what a save did not do.
        /// </summary>
        public const int Unchanged = 3;
        public const int Categories = 4;

        /// <summary>
        /// Flat primaries rather than Besiege's muted interface palette, because
        /// most of their work is done as translucent shells over a brown machine in
        /// a blue-green landscape, where a soft colour disappears.
        ///
        /// Unchanged starts invisible on purpose: it can be most of the machine, so
        /// on by default it would bury the three that answer the question. At zero
        /// opacity it costs nothing -- see <see cref="Faded"/>.
        /// </summary>
        private static readonly Color[] Fallbacks =
        {
            new Color(0.000f, 1.000f, 0.000f, 0.20f),
            new Color(1.000f, 1.000f, 0.000f, 0.20f),
            new Color(0.996f, 0.000f, 0.000f, 0.20f),
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
        /// The same colour for text, always fully opaque: a count at 38% over a dark
        /// panel is not a count anybody can read.
        /// </summary>
        public static Color Ink(int category)
        {
            Color colour = Of(category);
            colour.a = 1f;
            return colour;
        }

        /// <summary>
        /// True when this category is off rather than merely faint. The overlay does
        /// not spawn shells it cannot show: hundreds of invisible unchanged blocks
        /// is a hitch on every click in exchange for nothing on screen.
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
            // Stored as it is chosen: a slider is dragged, and there is no later
            // moment that reliably means "done". Reaching the disk is Prefs.Flush,
            // called when the picker closes.
            Prefs.SetColour(category, colour);
        }

        /// <summary>
        /// Reads the stored colours once, key by key, so a player who changed only
        /// the green keeps the current defaults for the other three.
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

        // How much larger than the block a shell is drawn. At 1 it shares the
        // block's surface and fights it for pixels; much past a tenth larger it
        // starts hiding the block's neighbours.
        //
        // Two ranges, because they are two questions: the slider covers what is
        // worth dragging through on an ordinary machine, and what may be typed is
        // wider -- half size to see a shell buried inside a block, double to find
        // one at all.
        public const float ShellLeast = 0.5f;
        public const float ShellMost = 2f;
        public const float ShellSlideLeast = 0.9f;
        public const float ShellSlideMost = 1.3f;
        private const float ShellFallback = 1.03f;
        private static float _shell = -1f;

        /// <summary>
        /// How much larger than its block each shell is drawn, applied about the
        /// shell's own middle so the block ends up inside its mark.
        /// </summary>
        public static float Shell
        {
            get
            {
                if (_shell < 0f)
                {
                    _shell = Mathf.Clamp(Prefs.Shell(ShellFallback), ShellLeast,
                                         ShellMost);
                }
                return _shell;
            }
        }

        public static void SetShell(float swell)
        {
            _shell = Mathf.Clamp(swell, ShellLeast, ShellMost);
            Prefs.SetShell(_shell);
        }

        /// <summary>The size a shell started the session at.</summary>
        public static float DefaultShell
        {
            get { return ShellFallback; }
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
