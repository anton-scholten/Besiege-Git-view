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

        /// <summary>
        /// True when Besiege wrote this name and it was read: the kind and the time
        /// both mean something. False for a file somebody renamed, whose date came
        /// from the filesystem and whose kind is not known at all.
        /// </summary>
        public bool Named;

        /// <summary>
        /// Where this version comes in the machine's history: 1 for the oldest, and
        /// the count of versions for the newest.
        ///
        /// Fixed to the timeline rather than to the row it happens to be on, so
        /// sorting by a count does not renumber anything. That is the point of it:
        /// with the list ordered by, say, how much each save removed, the numbers
        /// are the only thing left saying which version came before which.
        /// </summary>
        public int Number;

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

        /// <summary>
        /// Besiege's own epoch: its virtual filesystem carries a date as the
        /// seconds since the first of January 2014, UTC.
        ///
        /// Out of <c>StaticSettings.GetTimestamp</c>, which is what every
        /// <c>VirtualFile</c> is given its date by -- the file's last write time in
        /// UTC, less that instant. It is emphatically not an OLE automation date,
        /// which is what this used to be read as: those count days since 1899 and
        /// stop at the year 9999, so a real machine's date -- some four hundred
        /// million, in seconds -- was out of range, threw, and left every chosen
        /// machine with no timestamp at all.
        ///
        /// Reading it correctly is still not enough to get a date out of the load
        /// screen, mind. <c>VirtualFile</c>'s constructor only asks the filesystem
        /// for a write time when the path is *not* a child of
        /// <c>FileSystemPath.Root</c>, and that root is "/" -- so on Linux every
        /// absolute path is a child of it and every file in the browser is stamped
        /// with <c>DateTime.Now</c> instead. That is why the machines chosen by hand
        /// take their time from their newest autosave, whose name Besiege wrote the
        /// real time into, and show none at all when there is no autosave to ask.
        /// </summary>
        private static readonly DateTime Epoch =
            new DateTime(2014, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// A date the browser carries, as a local time -- which is the clock the
        /// file names are written in, so the two kinds of row read alike.
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
        ///
        /// The file name would do and is what this used to show, but it is Besiege's
        /// shorthand rather than anybody's writing -- "ver 26.06.27 15-52-19" needs
        /// knowing that "ver" is what the game calls the copy it keeps when you save
        /// and "aut" is the one the timer takes. Spelled out, and in the same
        /// timestamp the list is written in.
        ///
        /// A file whose name was not Besiege's keeps that name, because guessing
        /// which kind it is would be inventing the one thing this says.
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
        /// How the row reads: the name on the first line and the time under it,
        /// which is the order the two headings over that column are in.
        ///
        /// A version out of an autosave folder is called "aut 26.06.27 15-34-37" --
        /// a timestamp and nothing else -- so its name line holds what the name says
        /// beyond the time, which is whether the player saved it themselves or the
        /// timer took it. That leaves the timestamp on the line under the word TIME
        /// rather than on the line under NAME, in step with every other row, instead
        /// of one kind of row writing its time where another writes its name.
        ///
        /// A machine chosen by hand is called whatever the player called it, and that
        /// is the first thing worth knowing about the row -- with the time under it,
        /// because two machines with different names still have an order.
        /// </summary>
        public string Lines()
        {
            if (Named)
            {
                return (Manual ? "SAVED" : string.Empty) + "\n" + Stamp();
            }
            // A machine nobody can put a time to is written as what it is: a name.
            // Better an empty time column than a made-up one, and the load screen
            // cannot always say when a machine was saved -- see
            // <see cref="FromTimestamp"/>.
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
