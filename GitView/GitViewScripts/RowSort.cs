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
            switch (column)
            {
                case ByAdded: return "ADDED";
                case ByChanged: return "CHANGED";
                case ByRemoved: return "REMOVED";
                case ByTime: return "TIME";
                case ByNumber: return "SOURCE";
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

        private static int Compare(VersionEntry left, VersionEntry right, int column)
        {
            switch (column)
            {
                case ByAdded: return left.Added.CompareTo(right.Added);
                case ByChanged: return left.Changed.CompareTo(right.Changed);
                case ByRemoved: return left.Removed.CompareTo(right.Removed);
                case ByName: return string.Compare(left.FileName, right.FileName,
                                                   StringComparison.OrdinalIgnoreCase);
                case ByNumber: return left.Number.CompareTo(right.Number);
                case ByBlocks: return left.BlockCount.CompareTo(right.BlockCount);
                default: return DateTime.Compare(left.Saved, right.Saved);
            }
        }
    }
}
