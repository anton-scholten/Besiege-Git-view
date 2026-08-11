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
        public const int ColumnCount = 4;

        public static string ColumnName(int column)
        {
            switch (column)
            {
                case ByAdded: return "ADDED";
                case ByChanged: return "CHANGED";
                case ByRemoved: return "REMOVED";
                default: return "SAVED";
            }
        }

        /// <summary>
        /// Sorts in place. Ties break on time, newest first, so that ordering by a
        /// count -- where most rows are a 0 or a 1 -- still reads as a history
        /// rather than as whatever order the folder happened to come back in.
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
                if (key == ByTime)
                {
                    return 0;
                }
                return -DateTime.Compare(left.Saved, right.Saved);
            });
        }

        private static int Compare(VersionEntry left, VersionEntry right, int column)
        {
            switch (column)
            {
                case ByAdded: return left.Added.CompareTo(right.Added);
                case ByChanged: return left.Changed.CompareTo(right.Changed);
                case ByRemoved: return left.Removed.CompareTo(right.Removed);
                default: return DateTime.Compare(left.Saved, right.Saved);
            }
        }
    }
}
