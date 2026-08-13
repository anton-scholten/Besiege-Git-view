using System;

namespace GitView
{
    /// <summary>
    /// One row of the history: a saved .bsg, when it was written, and what it did to
    /// the machine. The counts start out unknown -- reading and diffing every file
    /// happens in the background once the window is up -- and <see cref="Counted"/>
    /// says whether they mean anything yet.
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
        /// True for the snapshots Besiege takes when you save, false for the timer's.
        /// Worth showing: a manual save is a point somebody thought worth keeping.
        /// </summary>
        public bool Manual;

        /// <summary>How many blocks the machine has at this version.</summary>
        public int BlockCount;

        public int Added;
        public int Changed;
        public int Removed;

        /// <summary>False until the background pass has diffed this version.</summary>
        public bool Counted;

        /// <summary>
        /// True when Besiege wrote this name and it was read, so the kind and the
        /// time both mean something. False for a file somebody renamed.
        /// </summary>
        public bool Named;

        /// <summary>
        /// Where this version comes in the history: 1 for the oldest. Fixed to the
        /// timeline rather than to the row, so with the list ordered by a count the
        /// numbers are the only thing left saying which version came first.
        /// </summary>
        public int Number;

        /// <summary>Besiege's own prefix for a timed autosave.</summary>
        public const string AutoPrefix = "aut";

        /// <summary>Besiege's own prefix for the copy it keeps when you save.</summary>
        public const string ManualPrefix = "ver";

        /// <summary>
        /// Reads the timestamp out of a name Besiege generated, which is
        /// "&lt;aut|ver&gt; yy.MM.dd HH-mm-ss". Hand-parsed rather than left to
        /// DateTime.ParseExact, because the format is the game's and not the
        /// player's locale's.
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

        /// <summary>
        /// Besiege's own epoch: <c>StaticSettings.GetTimestamp</c> dates every
        /// <c>VirtualFile</c> in seconds since the first of January 2014, UTC. Not an
        /// OLE automation date, which is what this used to be read as and which
        /// overflows on a real machine's date.
        ///
        /// Reading it right is still not enough to date a file in the load screen:
        /// <c>VirtualFile</c> only asks the filesystem for a write time when the path
        /// is not a child of <c>FileSystemPath.Root</c>, which is "/" -- so on Linux
        /// everything is stamped <c>DateTime.Now</c>. Hence hand-picked machines take
        /// their time from their newest autosave, or show none.
        /// </summary>
        private static readonly DateTime Epoch =
            new DateTime(2014, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// A date the browser carries, as a local time -- the clock the file names
        /// are written in, so the two kinds of row read alike.
        /// </summary>
        public static DateTime FromTimestamp(double seconds)
        {
            try
            {
                return Epoch.AddSeconds(seconds).ToLocalTime();
            }
            catch (Exception)
            {
                // A date from a collection that counts something else entirely.
                return DateTime.MinValue;
            }
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

        /// <summary>
        /// How a version is named in a sentence: "autosave 2026-06-27  15:34:37".
        /// Spelled out rather than shown as "ver 26.06.27 15-52-19", which needs
        /// knowing the game's shorthand. A file whose name was not Besiege's keeps
        /// that name, since guessing its kind would invent the one thing this says.
        /// </summary>
        public string Title()
        {
            if (!Named)
            {
                return FileName;
            }
            return (Manual ? "manual save " : "autosave ") + Stamp();
        }

        /// <summary>
        /// How the row reads: the name on the first line and the time under it, in
        /// the order the two headings over that column are in.
        ///
        /// An autosave is called "aut 26.06.27 15-34-37" -- a timestamp and nothing
        /// else -- so its name line holds what the name says beyond the time, which
        /// is whether the player saved it or the timer did. The timestamp then stays
        /// under TIME, in step with every other row.
        /// </summary>
        public string Lines()
        {
            if (Named)
            {
                return (Manual ? "SAVED" : string.Empty) + "\n" + Stamp();
            }
            // A machine nobody can date is written as what it is: a name. Better an
            // empty time column than a made-up one -- see FromTimestamp.
            return Saved == DateTime.MinValue ? FileName : FileName + "\n" + Stamp();
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
