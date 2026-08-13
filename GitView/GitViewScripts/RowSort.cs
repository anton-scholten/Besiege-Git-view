using System;
using System.Collections.Generic;

namespace GitView
{
    /// <summary>
    /// Ordering for the history list. Engine-free, so the headless tests cover it.
    /// Int constants rather than an enum: Besiege's own compiler, which builds this
    /// mod, segfaults on an enum declaration.
    /// </summary>
    public static class RowSort
    {
        public const int ByTime = 0;
        public const int ByAdded = 1;
        public const int ByChanged = 2;
        public const int ByRemoved = 3;

        /// <summary>
        /// Alphabetical. Only worth having since the list can hold machines chosen
        /// by hand; one machine's versions are all the same word and a time.
        /// </summary>
        public const int ByName = 4;

        /// <summary>
        /// By the number in the first column: a version's place in the history, or
        /// the order hand-picked machines were chosen in -- which is why it is a key
        /// of its own rather than the same thing as time.
        /// </summary>
        public const int ByNumber = 5;

        /// <summary>
        /// How many blocks the machine has at this version: the one number in the
        /// row about the machine rather than about a comparison.
        /// </summary>
        public const int ByBlocks = 6;
        public const int ColumnCount = 7;

        /// <summary>
        /// Whether a column holds numbers that have to be read out of the files
        /// first. Those are the ones a player can sort by before there is anything
        /// to sort -- the counts arrive over a few seconds -- so the order is
        /// applied again when the reading finishes.
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
        /// What a column is called. Only the first turns on <paramref name="chosen"/>:
        /// its number is a place in one machine's history, or the order the player
        /// picked machines in. Two different things, so two names.
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
        /// Sorts in place. Ties break on time, newest first, so ordering by a count
        /// -- where most rows are 0 or 1 -- still reads as a history; any tie left
        /// breaks on the number, which no two rows share. That last rule is what
        /// makes it an order at all, since <c>List.Sort</c> is not stable and rows
        /// it is told are equal come back shuffled on every click.
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
        /// The version before this one in the machine's own history -- the largest
        /// number below it, or null if this is the oldest.
        ///
        /// Nothing to do with how the list is arranged: this is what a row's counts
        /// are measured against, and they have to be a fact about the version or the
        /// columns holding them cannot be sorted. By number rather than time, which
        /// are the same thing for one machine's versions and are not for hand-picked
        /// machines, most of which the load screen cannot date at all.
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
        /// The row under this one in the order the list is shown in, or null at the
        /// bottom. What the machine on screen is compared with when nothing is
        /// pinned, so the arrow always joins two adjacent rows and any two versions
        /// can be diffed by putting them next to each other. Not what the counts are
        /// measured against -- those are <see cref="Earlier"/>.
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
                    // Two rows nothing can date go in name order instead: "the order
                    // they happened to be in" is not an order anybody asked for. See
                    // VersionEntry.FromTimestamp for why a machine may have no date.
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
