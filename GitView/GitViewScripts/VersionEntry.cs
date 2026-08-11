using System;

namespace GitView
{
    /// <summary>
    /// One row of the history: a saved .bsg, when it was written, and what it did
    /// to the machine relative to the version before it.
    ///
    /// The counts start out unknown -- working them out means reading and diffing
    /// every file in the folder, which happens in the background after the window
    /// is already on screen. <see cref="Counted"/> says whether they mean anything
    /// yet.
    /// </summary>
    public class VersionEntry
    {
        /// <summary>The file name without its extension, e.g. "aut 26.06.27 15-34-37".</summary>
        public string FileName = string.Empty;

        /// <summary>Full path to the .bsg, for loading it.</summary>
        public string Path = string.Empty;

        /// <summary>Full path to the PNG beside it, empty if there is none.</summary>
        public string ThumbnailPath = string.Empty;

        /// <summary>When the save was taken, out of its name where possible.</summary>
        public DateTime Saved = DateTime.MinValue;

        /// <summary>
        /// True for the snapshots Besiege takes when you save the machine yourself,
        /// false for the ones the timer takes. Both are versions and both belong in
        /// the list; the distinction is worth showing because a manual save is
        /// usually a point the player thought was worth keeping.
        /// </summary>
        public bool Manual;

        /// <summary>How many blocks the machine has at this version.</summary>
        public int BlockCount;

        public int Added;
        public int Changed;
        public int Removed;

        /// <summary>False until the background pass has diffed this version.</summary>
        public bool Counted;

        /// <summary>The oldest version has nothing before it to be compared against.</summary>
        public bool IsFirst;

        /// <summary>Besiege's own prefix for a timed autosave.</summary>
        public const string AutoPrefix = "aut";

        /// <summary>Besiege's own prefix for the copy it keeps when you save.</summary>
        public const string ManualPrefix = "ver";

        /// <summary>
        /// Reads the timestamp out of a name Besiege generated, which is
        /// "&lt;aut|ver&gt; yy.MM.dd HH-mm-ss".
        ///
        /// Hand-parsed rather than handed to DateTime.ParseExact because the format
        /// is the game's, not the player's locale's, and a machine whose culture
        /// reads "26.06.27" as something else entirely would otherwise sort its own
        /// history into nonsense.
        /// </summary>
        public static bool TryReadStamp(string fileName, out DateTime stamp, out bool manual)
        {
            stamp = DateTime.MinValue;
            manual = false;
            if (string.IsNullOrEmpty(fileName) || fileName.Length < 21)
            {
                return false;
            }

            string prefix = fileName.Substring(0, 3);
            if (prefix == ManualPrefix)
            {
                manual = true;
            }
            else if (prefix != AutoPrefix)
            {
                return false;
            }

            // "aut 26.06.27 15-34-37"
            //  012345678901234567890
            int year, month, day, hour, minute, second;
            if (fileName[3] != ' ' || fileName[6] != '.' || fileName[9] != '.'
                || fileName[12] != ' ' || fileName[15] != '-' || fileName[18] != '-')
            {
                return false;
            }
            if (!TwoDigits(fileName, 4, out year) || !TwoDigits(fileName, 7, out month)
                || !TwoDigits(fileName, 10, out day) || !TwoDigits(fileName, 13, out hour)
                || !TwoDigits(fileName, 16, out minute) || !TwoDigits(fileName, 19, out second))
            {
                return false;
            }

            try
            {
                stamp = new DateTime(2000 + year, month, day, hour, minute, second);
            }
            catch (Exception)
            {
                // A name that looks right but holds the 31st of February.
                return false;
            }
            return true;
        }

        private static bool TwoDigits(string text, int at, out int value)
        {
            value = 0;
            char high = text[at];
            char low = text[at + 1];
            if (high < '0' || high > '9' || low < '0' || low > '9')
            {
                return false;
            }
            value = (high - '0') * 10 + (low - '0');
            return true;
        }

        /// <summary>"2026-06-27 15:34:37", the one format that reads the same anywhere.</summary>
        public string Stamp()
        {
            if (Saved == DateTime.MinValue)
            {
                return FileName;
            }
            return Saved.Year.ToString("D4") + "-" + Saved.Month.ToString("D2") + "-" +
                   Saved.Day.ToString("D2") + "  " + Saved.Hour.ToString("D2") + ":" +
                   Saved.Minute.ToString("D2") + ":" + Saved.Second.ToString("D2");
        }
    }
}
