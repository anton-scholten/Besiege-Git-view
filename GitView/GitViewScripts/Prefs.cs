using System;
using UnityEngine;

namespace GitView
{
    /// <summary>
    /// The handful of things the mod remembers between sessions: where the window
    /// was left, and what colour each of the four categories is.
    ///
    /// Kept where Besiege's own modding API says to keep it.
    /// <c>Modding.Configuration.GetData()</c> hands back an <c>XDataHolder</c>
    /// belonging to this mod and nobody else -- it works out which mod is asking
    /// from the calling assembly, and throws if that assembly is not one the
    /// manifest lists. The loader reads it back in as the mod is loaded
    /// (<c>ModdingInitializer.LoadMod</c>) and writes every mod's holder out on a
    /// clean quit (<c>ModManager.OnApplicationQuit</c>), so the round trip is the
    /// game's to manage rather than ours.
    ///
    /// It lands in <c>Besiege_Data/Mods/Config/GitView_&lt;id&gt;.xml</c>, next to
    /// every other mod's, and this mod already has one: the loader keeps the
    /// <c>toggle-history</c> key binding in it. Sharing the file that already
    /// exists for this mod's settings is the whole point.
    ///
    /// (PlayerPrefs would also work and is not blacklisted, but it is Unity's
    /// store, not Besiege's -- it puts a mod's settings in the game's own options
    /// file, where nothing manages them and uninstalling the mod leaves them
    /// behind.)
    ///
    /// Writes go into the holder immediately and cost nothing.
    /// <see cref="Flush"/> is what reaches the disk, so it is called when something
    /// is finished with rather than on every frame a slider moves; the quit-time
    /// save means a missed call costs only what a crash would have cost anyway.
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
        /// Where a colour's opacity is kept, which is not with the colour.
        ///
        /// <c>XDataHolder</c>'s Color has three channels. Writing one produces
        ///   &lt;Color key="added"&gt;&lt;R&gt;0&lt;/R&gt;&lt;G&gt;1&lt;/G&gt;&lt;B&gt;0&lt;/B&gt;&lt;/Color&gt;
        /// with no alpha anywhere in it, so an opacity written that way is not
        /// stored and comes back as whatever <c>ReadColor</c> defaults to. That is
        /// the whole of the "the opacity does not survive a restart" bug: the other
        /// three channels always did.
        ///
        /// So the opacity goes beside it as a Single, which is the type every block
        /// setting in every .bsg is written with and the one thing about XDataHolder
        /// that needs no assuming.
        /// </summary>
        private const string OpacitySuffix = "-opacity";

        /// <summary>
        /// This mod's configuration, or null if the API refuses -- which it does by
        /// throwing, and only for an assembly the manifest does not list. Worth
        /// catching rather than trusting: a mod that cannot store a preference
        /// should still run.
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
        /// Where the window was left, or <paramref name="fallback"/> if it never
        /// was. Two Singles rather than one Vector3, for the same reason the opacity
        /// is one: a type whose round trip can be read straight out of any .bsg is
        /// worth more here than a tidier key.
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
        /// Puts a value in the holder under its key. <c>XDataHolder.Write</c> takes
        /// an object and picks the XData type off it, so a Color and a Vector3 both
        /// go in whole rather than as four floats each.
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
