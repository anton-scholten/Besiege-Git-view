using System;
using System.Collections.Generic;

namespace GitView
{
    /// <summary>
    /// Ordering for the history list. Engine-free, so the headless tests cover it.
    ///
    /// Written as int constants rather than an enum: Besiege's in-game C# compiler
    /// segfaults on an enum declaration, and this mod is built with that same
    /// compiler.
    /// </summary>
    public static class RowSort
    {
        public const int ByTime = 0;
        public const int ByAdded = 1;
        public const int ByChanged = 2;
        public const int ByRemoved = 3;

        /// <summary>
        /// Alphabetical, by what the row is called. Only worth having since the
        /// list can hold machines chosen by hand rather than the versions of one
        /// machine, whose names are all the same word and a time.
        /// </summary>
        public const int ByName = 4;

        /// <summary>
        /// By the number in the source column: a version's place in the history, or
        /// -- for machines chosen by hand -- the order they were chosen in. Which
        /// is why it is a key of its own rather than the same thing as time.
        /// </summary>
        public const int ByNumber = 5;

        /// <summary>
        /// How many blocks the machine has at this version, all told -- which is the
        /// one number in the row that is about the machine rather than about a
        /// comparison, and the one every other number is a change to.
        /// </summary>
        public const int ByBlocks = 6;
        public const int ColumnCount = 7;

        /// <summary>
        /// Whether a column holds numbers that have to be read out of the files
        /// before they mean anything.
        ///
        /// Worth asking, because those are the columns a player can sort by before
        /// there is anything to sort: the list is on screen in a fraction of a second
        /// and the counts arrive over the next few, so a heading clicked early orders
        /// the rows by a column of dots. The order is applied again when the reading
        /// finishes.
        /// </summary>
        public static bool IsCount(int column)
        {
            return column == ByAdded || column == ByChanged || column == ByRemoved ||
                   column == ByBlocks;
        }

        public static string ColumnName(int column)
        {
            return ColumnName(column, false);
        }

        /// <summary>
        /// What a column is called. Only the first one turns on
        /// <paramref name="chosen"/>: its number is a place in one machine's history
        /// when the list is that, and the order the player picked them in when the
        /// list is machines they chose. Two different things, so two names.
        /// </summary>
        public static string ColumnName(int column, bool chosen)
        {
            switch (column)
            {
                case ByAdded: return "ADDED";
                case ByChanged: return "CHANGED";
                case ByRemoved: return "REMOVED";
                case ByTime: return "TIME";
                case ByNumber: return chosen ? "SELECTION" : "VERSION";
                case ByBlocks: return "BLOCKS";
                default: return "NAME";
            }
        }

        /// <summary>
        /// Sorts in place. Ties break on time, newest first, so that ordering by a
        /// count -- where most rows are a 0 or a 1 -- still reads as a history
        /// rather than as whatever order the folder happened to come back in.
        ///
        /// And any tie left after that breaks on the number, which no two rows
        /// share. That last rule is what makes the order an order at all:
        /// <c>List.Sort</c> is not a stable sort, so rows it is told are equal come
        /// back in an arbitrary and changing arrangement -- two machines saved in
        /// the same second, or a whole list whose times could not be read, would
        /// shuffle themselves every time the player clicked a heading.
        /// </summary>
        public static void Apply(List<VersionEntry> rows, int column, bool ascending)
        {
            if (rows == null || rows.Count < 2)
            {
                return;
            }

            int direction = ascending ? 1 : -1;
            int key = column;
            rows.Sort(delegate(VersionEntry left, VersionEntry right)
            {
                int order = direction * Compare(left, right, key);
                if (order != 0)
                {
                    return order;
                }
                if (key != ByTime)
                {
                    order = -DateTime.Compare(left.Saved, right.Saved);
                    if (order != 0)
                    {
                        return order;
                    }
                }
                return left.Number.CompareTo(right.Number);
            });
        }

        /// <summary>
        /// The version before this one in the machine's own history: the largest
        /// number below this one, or null if this is the oldest there is.
        ///
        /// Nothing to do with how the list is arranged. This is what the counts in a
        /// row are measured against, and they have to be a fact about the version --
        /// what that save did -- or the columns holding them cannot be sorted. A
        /// count that changed with the arrangement would mean sorting by "added" put
        /// the rows in an order that was true a moment ago and is not true of the
        /// numbers now beside them.
        ///
        /// By number rather than by time, which is the same thing for one machine's
        /// versions and is not for machines picked out by hand: the load screen
        /// cannot date most of those, so any number of them share a time of "none at
        /// all", while the number is always the order they were picked in.
        /// </summary>
        public static VersionEntry Earlier(List<VersionEntry> rows, VersionEntry entry)
        {
            if (rows == null || entry == null)
            {
                return null;
            }
            VersionEntry best = null;
            for (int i = 0; i < rows.Count; i++)
            {
                VersionEntry row = rows[i];
                if (row == entry || row.Number >= entry.Number)
                {
                    continue;
                }
                if (best == null || row.Number > best.Number)
                {
                    best = row;
                }
            }
            return best;
        }

        /// <summary>
        /// The row under this one, in the order the list is being shown in, or null
        /// if it is the bottom row.
        ///
        /// What the machine on screen is compared with when nothing is pinned: the
        /// row you can see under it, whichever heading the list is sorted by. That
        /// keeps the arrow a picture of the comparison -- it joins two rows that are
        /// next to each other -- and it lets a diff be taken between any two versions
        /// by putting them next to each other.
        ///
        /// Not what the counts in the columns are measured against; those are
        /// <see cref="Earlier"/>, and the difference between the two is the
        /// difference between what you are looking at and what the table says.
        /// </summary>
        public static VersionEntry Below(List<VersionEntry> rows, VersionEntry entry)
        {
            if (rows == null || entry == null)
            {
                return null;
            }
            for (int i = 0; i + 1 < rows.Count; i++)
            {
                if (rows[i] == entry)
                {
                    return rows[i + 1];
                }
            }
            return null;
        }

        private static int Alphabetical(VersionEntry left, VersionEntry right)
        {
            return string.Compare(left.FileName, right.FileName,
                                  StringComparison.OrdinalIgnoreCase);
        }

        private static int Compare(VersionEntry left, VersionEntry right, int column)
        {
            switch (column)
            {
                case ByAdded: return left.Added.CompareTo(right.Added);
                case ByChanged: return left.Changed.CompareTo(right.Changed);
                case ByRemoved: return left.Removed.CompareTo(right.Removed);
                case ByName: return Alphabetical(left, right);
                case ByNumber: return left.Number.CompareTo(right.Number);
                case ByBlocks: return left.BlockCount.CompareTo(right.BlockCount);
                default:
                    // Two rows nothing can put a time to are put in name order
                    // instead. Sorting by time is asking for the list in an order
                    // that means something, and "the order they happened to be in"
                    // is not one -- see VersionEntry.FromTimestamp for why a machine
                    // picked out of the load screen may have no date at all.
                    if (left.Saved == DateTime.MinValue &&
                        right.Saved == DateTime.MinValue)
                    {
                        return Alphabetical(left, right);
                    }
                    return DateTime.Compare(left.Saved, right.Saved);
            }
        }
    }
}
