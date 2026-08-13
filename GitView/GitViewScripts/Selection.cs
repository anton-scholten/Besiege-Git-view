using System.Collections.Generic;

namespace GitView
{
    /// <summary>One machine the player has picked out of the load screen to compare.</summary>
    public class ChosenMachine
    {
        public string Path = string.Empty;
        public string Name = string.Empty;
        public string ThumbnailPath = string.Empty;

        /// <summary>
        /// When the machine was last saved, for putting the chosen machines in
        /// order. <c>DateTime.MinValue</c> when nothing can say -- see
        /// <see cref="VersionScan.LastSaved"/>.
        /// </summary>
        public System.DateTime Saved;
    }

    /// <summary>
    /// The machines picked out of the load screen for a diff, in the order they were
    /// picked -- which is what the numbers on the marks say.
    ///
    /// Static, and deliberately outliving the browser: the load screen rebuilds its
    /// slots when the page turns, so anything kept on a slot is gone the moment the
    /// player goes looking for the second machine. A machine is identified by its
    /// path for the same reason.
    /// </summary>
    public static class Selection
    {
        private static readonly List<ChosenMachine> Chosen = new List<ChosenMachine>();

        public static int Count
        {
            get { return Chosen.Count; }
        }

        /// <summary>Where a machine comes in the order, counting from 1, or 0 if it is not chosen.</summary>
        public static int Ordinal(string path)
        {
            for (int i = 0; i < Chosen.Count; i++)
            {
                if (Chosen[i].Path == path)
                {
                    return i + 1;
                }
            }
            return 0;
        }

        /// <summary>
        /// Picks a machine, or unpicks it if it was already picked. Unpicking
        /// renumbers everything after it, so the marks always read 1, 2, 3 with
        /// nothing missing out of the middle.
        /// </summary>
        public static bool Toggle(IVirtualObject item)
        {
            if (item == null || item.IsFolder)
            {
                return false;
            }
            string path = PathOf(item);
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            int at = Ordinal(path);
            if (at > 0)
            {
                Chosen.RemoveAt(at - 1);
                return false;
            }

            ChosenMachine chosen = new ChosenMachine();
            chosen.Path = path;
            chosen.Name = item.Name;
            // Asked once, here, rather than when the window is built: it is a
            // directory listing, and this is a button press.
            chosen.Saved = VersionScan.LastSaved(item);
            try
            {
                chosen.ThumbnailPath = item.ThumbnailPath.Path;
            }
            catch (System.Exception)
            {
                chosen.ThumbnailPath = string.Empty;
            }
            Chosen.Add(chosen);
            return true;
        }

        /// <summary>
        /// Forgets everything chosen. Putting the dimmed pictures back is
        /// <c>BrowserWatch.Reconcile</c>'s business, at its next sweep.
        /// </summary>
        public static void Clear()
        {
            Chosen.Clear();
        }

        public static string PathOf(IVirtualObject item)
        {
            if (item == null)
            {
                return string.Empty;
            }
            try
            {
                return item.ObjectPath.Path;
            }
            catch (System.Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// The chosen machines as the history window's rows. A row is a file, a time
        /// and a picture either way; the two things that differ are said here -- the
        /// number is the order they were chosen in, and the name is the machine's own
        /// rather than an "autosave" of anything.
        /// </summary>
        public static List<VersionEntry> AsRows()
        {
            List<VersionEntry> rows = new List<VersionEntry>();
            for (int i = 0; i < Chosen.Count; i++)
            {
                ChosenMachine chosen = Chosen[i];
                VersionEntry entry = new VersionEntry();
                entry.Path = chosen.Path;
                entry.FileName = chosen.Name;
                entry.ThumbnailPath = chosen.ThumbnailPath;
                entry.Number = i + 1;
                entry.Saved = chosen.Saved;
                rows.Add(entry);
            }

            return rows;
        }
    }
}
