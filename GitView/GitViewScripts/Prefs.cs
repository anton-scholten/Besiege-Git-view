using System;
using UnityEngine;

namespace GitView
{
    /// <summary>
    /// The handful of things the mod remembers between sessions: where the windows
    /// were left, the four colours, and the shell size.
    ///
    /// Kept where Besiege's modding API says to keep it.
    /// <c>Modding.Configuration.GetData()</c> hands back an <c>XDataHolder</c>
    /// belonging to this mod alone; the loader reads it in with the mod and writes
    /// it out on a clean quit, so the round trip is the game's to manage. It lands
    /// in <c>Besiege_Data/Mods/Config/GitView_&lt;id&gt;.xml</c>, the same file the
    /// loader already keeps the <c>toggle-history</c> binding in. (PlayerPrefs would
    /// work too, but it is Unity's store, not Besiege's, and uninstalling the mod
    /// would leave its settings in the game's own options file.)
    ///
    /// Writes go into the holder immediately and cost nothing. <see cref="Flush"/>
    /// is what reaches the disk, so it is called when something is finished with
    /// rather than every frame a slider moves.
    /// </summary>
    public static class Prefs
    {
        // Written down rather than derived from anything: these are on disk, so
        // they are not free to change.
        private const string WindowXKey = "window-x";
        private const string WindowYKey = "window-y";

        /// <summary>Indexed by DiffPalette's category.</summary>
        private static readonly string[] ColourKeys =
        {
            "added", "changed", "removed", "unchanged"
        };

        /// <summary>
        /// Where a colour's opacity is kept, which is not with the colour:
        /// <c>XDataHolder</c>'s Color writes R, G and B and no alpha at all, so an
        /// opacity stored that way does not survive a restart. It goes beside it as
        /// a Single, the type every block setting in every .bsg uses.
        /// </summary>
        private const string OpacitySuffix = "-opacity";

        /// <summary>
        /// This mod's configuration, or null if the API refuses -- which it does by
        /// throwing. A mod that cannot store a preference should still run.
        /// </summary>
        private static XDataHolder Data()
        {
            try
            {
                return Modding.Configuration.GetData();
            }
            catch (Exception e)
            {
                Log.Warn("no configuration to store preferences in: " + e.Message);
                return null;
            }
        }

        // -------------------------------------------------------------- the window

        /// <summary>
        /// Where the window was left, or <paramref name="fallback"/> if it never was.
        /// Two Singles rather than a Vector3, for the same reason the opacity is one.
        /// </summary>
        public static Vector2 Window(Vector2 fallback)
        {
            return new Vector2(Number(WindowXKey, fallback.x),
                               Number(WindowYKey, fallback.y));
        }

        public static void SetWindow(Vector2 at)
        {
            Write(WindowXKey, at.x);
            Write(WindowYKey, at.y);
        }

        // --------------------------------------------------------------- the shells

        /// <summary>How much larger than its block a shell is drawn.</summary>
        public static float Shell(float fallback)
        {
            return Number(ShellKey, fallback);
        }

        public static void SetShell(float swell)
        {
            Write(ShellKey, swell);
        }

        private const string ShellKey = "shell-swell";

        // ------------------------------------------------------------- the colours

        public static Color Colour(int category, Color fallback)
        {
            XDataHolder data = Data();
            string key = KeyFor(category);
            if (data == null || key == null)
            {
                return fallback;
            }

            Color colour = fallback;
            if (data.HasKey(key))
            {
                try
                {
                    Color stored = data.ReadColor(key);
                    colour = new Color(stored.r, stored.g, stored.b, fallback.a);
                }
                catch (Exception)
                {
                    // Left at the fallback, opacity included.
                }
            }
            colour.a = Number(key + OpacitySuffix, colour.a);
            return colour;
        }

        public static void SetColour(int category, Color colour)
        {
            string key = KeyFor(category);
            if (key == null)
            {
                return;
            }
            Write(key, colour);
            Write(key + OpacitySuffix, colour.a);
        }

        /// <summary>One stored float, or the fallback if it is not there or unreadable.</summary>
        private static float Number(string key, float fallback)
        {
            XDataHolder data = Data();
            if (data == null || !data.HasKey(key))
            {
                return fallback;
            }
            try
            {
                return data.ReadFloat(key);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        /// <summary>Writes everything set so far to disk.</summary>
        public static void Flush()
        {
            try
            {
                Modding.Configuration.Save();
            }
            catch (Exception e)
            {
                Log.Warn("could not save preferences: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ writing

        /// <summary>
        /// Puts a value in the holder under its key. <c>XDataHolder.Write</c> picks
        /// the XData type off the object, so a Color goes in whole.
        /// </summary>
        private static void Write(string key, object value)
        {
            XDataHolder data = Data();
            if (data == null)
            {
                return;
            }
            try
            {
                data.Write(key, value);
            }
            catch (Exception)
            {
                // A preference that cannot be stored is not worth a log line every
                // time the window is dragged.
            }
        }

        private static string KeyFor(int category)
        {
            return category >= 0 && category < ColourKeys.Length
                ? ColourKeys[category] : null;
        }
    }
}
