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
        /// The row before this one in the list's own order, or null if it is the
        /// first there is.
        ///
        /// By the number, which is the one order that means something in both kinds
        /// of list: a version's place in its machine's history, and a chosen
        /// machine's place in the order the player picked them. Deliberately not by
        /// time. Machines picked out of the load screen often have no readable date
        /// at all, so any number of them share a time -- comparing on time found
        /// nothing before *any* of them, every row claimed to be the oldest, and no
        /// diff was drawn. The counts are worked out walking this same order, so
        /// this is the row whose numbers are already on screen beside it.
        /// </summary>
        public static VersionEntry Previous(List<VersionEntry> rows, VersionEntry entry)
        {
            if (rows == null || entry == null)
            {
                return null;
            }
            List<VersionEntry> ordered = new List<VersionEntry>(rows);
            Apply(ordered, ByNumber, true);
            for (int i = 1; i < ordered.Count; i++)
            {
                if (ordered[i] == entry)
                {
                    return ordered[i - 1];
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
