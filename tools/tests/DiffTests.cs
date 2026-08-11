using System;
using System.Collections.Generic;
using GitView;
using UnityEngine;

/// <summary>
/// Headless tests for the half of the mod that has no Unity dependency: the block
/// diff, the version-name parsing and the sorting.
///
/// This is where the mod is right or wrong. A window that is a few pixels out is
/// obvious the first time it is opened; a diff that says a block was deleted and
/// replaced when it was only nudged is not obvious at all, and is the reading a
/// player would act on. They run in about a second under Besiege's own Mono.
/// </summary>
public static class DiffTests
{
    static int checks;
    static readonly List<string> failures = new List<string>();

    public static int Main(string[] args)
    {
        Identity();
        Additions();
        Removals();
        Changes();
        ReissuedIdentifiers();
        Settings();
        Stamps();
        Sorting();

        Console.WriteLine("Diff tests: " + checks + " checks.");
        if (failures.Count == 0)
        {
            Console.WriteLine("  all passed.");
            return 0;
        }
        Console.WriteLine("  " + failures.Count + " FAILED:");
        for (int i = 0; i < failures.Count; i++) { Console.WriteLine("    " + failures[i]); }
        return 1;
    }

    // -- the diff -------------------------------------------------------------------

    static void Identity()
    {
        MachineSnapshot machine = Machine(
            Block("a", 1, 0, 0, 0),
            Block("b", 1, 1, 0, 0),
            Block("c", 2, 2, 0, 0));

        DiffResult same = BlockDiff.Compare(machine, machine);
        Is("nothing added", 0, same.Added.Count);
        Is("nothing changed", 0, same.Changed.Count);
        Is("nothing removed", 0, same.Removed.Count);
        Is("all unchanged", 3, same.Unchanged);
        True("empty diff", same.IsEmpty);

        // A machine compared against nothing is entirely new, which is what the
        // oldest version in a folder looks like if anything ever asks.
        DiffResult fromNothing = BlockDiff.Compare(MachineSnapshot.Empty(), machine);
        Is("from nothing", 3, fromNothing.Added.Count);

        DiffResult toNothing = BlockDiff.Compare(machine, MachineSnapshot.Empty());
        Is("to nothing", 3, toNothing.Removed.Count);
    }

    static void Additions()
    {
        MachineSnapshot before = Machine(Block("a", 1, 0, 0, 0));
        MachineSnapshot after = Machine(
            Block("a", 1, 0, 0, 0),
            Block("b", 1, 1, 0, 0),
            Block("c", 1, 2, 0, 0));

        DiffResult diff = BlockDiff.Compare(before, after);
        Is("two added", 2, diff.Added.Count);
        Is("one kept", 1, diff.Unchanged);
        Is("none removed", 0, diff.Removed.Count);
        // Added blocks are reported as they are in the new version, because that is
        // where the overlay has to draw them.
        Is("added block is the new one", "b", diff.Added[0].Id);
    }

    static void Removals()
    {
        MachineSnapshot before = Machine(
            Block("a", 1, 0, 0, 0),
            Block("b", 1, 1, 0, 0));
        MachineSnapshot after = Machine(Block("a", 1, 0, 0, 0));

        DiffResult diff = BlockDiff.Compare(before, after);
        Is("one removed", 1, diff.Removed.Count);
        Is("removed block is the old one", "b", diff.Removed[0].Id);
        // Its old position is what the red ghost stands in.
        Is("removed position kept", 1f, diff.Removed[0].Position.x);
    }

    static void Changes()
    {
        BlockRecord moved = Block("b", 1, 1, 0, 0);
        moved.Position = new Vector3(1f, 3f, 0f);
        MachineSnapshot before = Machine(Block("a", 1, 0, 0, 0), Block("b", 1, 1, 0, 0));
        MachineSnapshot after = Machine(Block("a", 1, 0, 0, 0), moved);

        DiffResult diff = BlockDiff.Compare(before, after);
        Is("moved is a change", 1, diff.Changed.Count);
        Is("moved is not an addition", 0, diff.Added.Count);
        Is("moved is not a removal", 0, diff.Removed.Count);
        Is("change reported at the new place", 3f, diff.Changed[0].Position.y);

        // A block that moved far still kept its guid, so it is still a change and
        // not a delete plus an add. The match radius governs the reissued-guid
        // case only.
        BlockRecord far = Block("b", 1, 1, 0, 0);
        far.Position = new Vector3(40f, 0f, 0f);
        Is("a long move is still one block",
           1, BlockDiff.Compare(before, Machine(Block("a", 1, 0, 0, 0), far)).Changed.Count);

        // Settings, not geometry.
        BlockRecord retuned = Block("b", 1, 1, 0, 0);
        retuned.Settings = "speed=3";
        Is("a retuned block is a change",
           1, BlockDiff.Compare(before, Machine(Block("a", 1, 0, 0, 0), retuned)).Changed.Count);

        // Below the tolerance that re-serialising a float can introduce.
        BlockRecord jittered = Block("b", 1, 1, 0, 0);
        jittered.Position = new Vector3(1f + 0.0002f, 0f, 0f);
        Is("float noise is not a change",
           0, BlockDiff.Compare(before, Machine(Block("a", 1, 0, 0, 0), jittered)).Changed.Count);

        // The same rotation written the other way round the double cover.
        BlockRecord negated = Block("b", 1, 1, 0, 0);
        negated.Rotation = new Quaternion(-0f, -0f, -0f, -1f);
        Is("a negated quaternion is not a change",
           0, BlockDiff.Compare(before, Machine(Block("a", 1, 0, 0, 0), negated)).Changed.Count);
    }

    /// <summary>
    /// The case the three-pass matcher exists for. Besiege reissues a block's guid
    /// when it is copied, mirrored or restored by an undo, and the block is
    /// otherwise untouched -- so a diff that trusts the guid reports a deletion and
    /// an addition in the same place.
    /// </summary>
    static void ReissuedIdentifiers()
    {
        MachineSnapshot before = Machine(
            Block("a", 1, 0, 0, 0),
            Block("b", 1, 1, 0, 0));
        MachineSnapshot after = Machine(
            Block("a", 1, 0, 0, 0),
            Block("REISSUED", 1, 1, 0, 0));

        DiffResult diff = BlockDiff.Compare(before, after);
        Is("a reissued guid is not an addition", 0, diff.Added.Count);
        Is("a reissued guid is not a removal", 0, diff.Removed.Count);
        Is("a reissued guid is not a change", 0, diff.Changed.Count);
        Is("a reissued guid is unchanged", 2, diff.Unchanged);

        // Reissued and nudged: still the same block, now genuinely changed.
        BlockRecord nudged = Block("REISSUED", 1, 1, 0, 0);
        nudged.Position = new Vector3(1.2f, 0f, 0f);
        DiffResult nudge = BlockDiff.Compare(before, Machine(Block("a", 1, 0, 0, 0), nudged));
        Is("reissued and nudged is a change", 1, nudge.Changed.Count);
        Is("reissued and nudged is not an addition", 0, nudge.Added.Count);

        // Reissued and moved right across the machine: past the radius, and
        // "deleted, and another one placed over there" is the honest reading.
        BlockRecord relocated = Block("REISSUED", 1, 1, 0, 0);
        relocated.Position = new Vector3(9f, 0f, 0f);
        DiffResult far = BlockDiff.Compare(before, Machine(Block("a", 1, 0, 0, 0), relocated));
        Is("reissued and relocated is an addition", 1, far.Added.Count);
        Is("reissued and relocated is a removal", 1, far.Removed.Count);

        // A different block type in the same place is a replacement, not a move,
        // however close it sits.
        BlockRecord swapped = Block("REISSUED", 7, 1, 0, 0);
        DiffResult swap = BlockDiff.Compare(before, Machine(Block("a", 1, 0, 0, 0), swapped));
        Is("a swapped type is an addition", 1, swap.Added.Count);
        Is("a swapped type is a removal", 1, swap.Removed.Count);

        // Two identical blocks in different places, both reissued: each has to
        // pair with the one it is actually on top of, not with whichever came
        // first.
        MachineSnapshot pairBefore = Machine(Block("x", 1, 0, 0, 0), Block("y", 1, 5, 0, 0));
        MachineSnapshot pairAfter = Machine(Block("p", 1, 5, 0, 0), Block("q", 1, 0, 0, 0));
        DiffResult paired = BlockDiff.Compare(pairBefore, pairAfter);
        Is("identical blocks pair by place", 2, paired.Unchanged);
        Is("identical blocks add nothing", 0, paired.Added.Count);
    }

    // -- settings ----------------------------------------------------------------------

    /// <summary>
    /// Settings are compared as text, so the rounding has to happen before the
    /// comparison. The case this exists for is real and was found in a live
    /// autosave folder: a piston's start-position came back as 5.96047E-08 one
    /// minute and 2.842171E-14 the next, with nobody having touched the machine.
    /// </summary>
    static void Settings()
    {
        Is("physics noise rounds to zero", BlockRecord.Quantise(5.96047E-08f),
                                           BlockRecord.Quantise(2.842171E-14f));
        Is("and reads as zero", "0", BlockRecord.Quantise(5.96047E-08f));
        Is("a sign flip in the noise is still zero", "0", BlockRecord.Quantise(-1E-12f));
        Is("whole numbers stay whole", "270", BlockRecord.Quantise(270f));
        Is("a slider value survives", "2.5", BlockRecord.Quantise(2.5f));
        Is("the fourth place survives", "0.0001", BlockRecord.Quantise(0.0001f));
        Is("the fifth place does not", "0", BlockRecord.Quantise(0.00004f));
        Is("negatives keep their sign", "-3.25", BlockRecord.Quantise(-3.25f));
        // Written the same whatever the player's locale does with decimal points.
        Contains("a decimal point, not a comma", BlockRecord.Quantise(1.5f), ".");

        // Two blocks differing only inside the noise floor are the same block.
        BlockRecord quiet = Block("a", 1, 0, 0, 0);
        quiet.Settings = BlockRecord.FlattenSettings(new List<string>(
            new string[] { "start|Vector3=" + BlockRecord.Quantise(5.96047E-08f) }));
        BlockRecord noisy = Block("a", 1, 0, 0, 0);
        noisy.Settings = BlockRecord.FlattenSettings(new List<string>(
            new string[] { "start|Vector3=" + BlockRecord.Quantise(2.842171E-14f) }));
        True("noise is not a change", quiet.Matches(noisy));

        // Order is not meaning: the same settings listed differently are equal.
        Is("settings are sorted",
           BlockRecord.FlattenSettings(new List<string>(new string[] { "a=1", "b=2" })),
           BlockRecord.FlattenSettings(new List<string>(new string[] { "b=2", "a=1" })));
        Is("no settings is empty", "",
           BlockRecord.FlattenSettings(new List<string>()));
    }

    // -- version names --------------------------------------------------------------

    static void Stamps()
    {
        DateTime stamp;
        bool manual;

        True("a timed autosave parses",
             VersionEntry.TryReadStamp("aut 26.06.27 15-34-37", out stamp, out manual));
        Is("year", 2026, stamp.Year);
        Is("month", 6, stamp.Month);
        Is("day", 27, stamp.Day);
        Is("hour", 15, stamp.Hour);
        Is("minute", 34, stamp.Minute);
        Is("second", 37, stamp.Second);
        Is("timed saves are not manual", false, manual);

        True("a save-time version parses",
             VersionEntry.TryReadStamp("ver 25.12.25 08-56-46", out stamp, out manual));
        Is("save-time versions are manual", true, manual);
        Is("century", 2025, stamp.Year);

        Is("an unknown prefix is refused", false,
           VersionEntry.TryReadStamp("bak 26.06.27 15-34-37", out stamp, out manual));
        Is("a hand-renamed file is refused", false,
           VersionEntry.TryReadStamp("before the wing", out stamp, out manual));
        Is("a short name is refused", false,
           VersionEntry.TryReadStamp("aut 26.06", out stamp, out manual));
        Is("letters where digits belong are refused", false,
           VersionEntry.TryReadStamp("aut ab.06.27 15-34-37", out stamp, out manual));
        Is("an impossible date is refused", false,
           VersionEntry.TryReadStamp("aut 26.02.31 15-34-37", out stamp, out manual));

        VersionEntry entry = new VersionEntry();
        entry.Saved = new DateTime(2026, 6, 27, 15, 34, 37);
        Is("the stamp reads the same in any locale", "2026-06-27  15:34:37", entry.Stamp());

        VersionEntry unparsed = new VersionEntry();
        unparsed.FileName = "before the wing";
        Is("an unparsed name shows itself", "before the wing", unparsed.Stamp());
    }

    // -- sorting ---------------------------------------------------------------------

    static void Sorting()
    {
        List<VersionEntry> rows = new List<VersionEntry>();
        rows.Add(Row("first", 1, 5, 0, 2));
        rows.Add(Row("second", 2, 1, 9, 0));
        rows.Add(Row("third", 3, 5, 1, 1));

        RowSort.Apply(rows, RowSort.ByTime, true);
        Is("oldest first", "first", rows[0].FileName);
        Is("newest last", "third", rows[2].FileName);

        RowSort.Apply(rows, RowSort.ByTime, false);
        Is("newest first", "third", rows[0].FileName);

        RowSort.Apply(rows, RowSort.ByChanged, false);
        Is("most changed first", "second", rows[0].FileName);

        RowSort.Apply(rows, RowSort.ByRemoved, true);
        Is("fewest removed first", "second", rows[0].FileName);

        // Both of these added 5. The tie breaks on time, newest first, so the
        // order is a history rather than whatever the folder handed back.
        RowSort.Apply(rows, RowSort.ByAdded, false);
        Is("tie breaks on time", "third", rows[0].FileName);
        Is("tie breaks on time, second row", "first", rows[1].FileName);
        Is("the odd one out sorts last", "second", rows[2].FileName);

        Is("column names", "REMOVED", RowSort.ColumnName(RowSort.ByRemoved));
        Is("unknown columns fall back to time", "SAVED", RowSort.ColumnName(99));
    }

    // -- fixtures ---------------------------------------------------------------------

    static BlockRecord Block(string id, int kind, float x, float y, float z)
    {
        BlockRecord record = new BlockRecord();
        record.Id = id;
        record.Kind = kind;
        record.Position = new Vector3(x, y, z);
        record.Rotation = new Quaternion(0f, 0f, 0f, 1f);
        record.Scale = Vector3.one;
        return record;
    }

    static MachineSnapshot Machine(params BlockRecord[] blocks)
    {
        MachineSnapshot machine = new MachineSnapshot();
        machine.Blocks.AddRange(blocks);
        return machine;
    }

    static VersionEntry Row(string name, int day, int added, int changed, int removed)
    {
        VersionEntry entry = new VersionEntry();
        entry.FileName = name;
        entry.Saved = new DateTime(2026, 6, day);
        entry.Added = added;
        entry.Changed = changed;
        entry.Removed = removed;
        entry.Counted = true;
        return entry;
    }

    static void True(string what, bool condition)
    {
        checks++;
        if (!condition) { failures.Add(what); }
    }

    static void Is(string what, object expected, object actual)
    {
        checks++;
        string a = expected == null ? "null" : expected.ToString();
        string b = actual == null ? "null" : actual.ToString();
        if (a != b) { failures.Add(what + ": expected <" + a + ">, got <" + b + ">"); }
    }

    static void Contains(string what, string haystack, string needle)
    {
        checks++;
        if (haystack == null || haystack.IndexOf(needle, StringComparison.Ordinal) < 0)
        {
            failures.Add(what + ": <" + needle + "> not found in <" +
                         (haystack == null ? "null" : haystack) + ">");
        }
    }
}
