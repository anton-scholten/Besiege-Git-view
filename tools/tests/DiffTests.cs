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
        KeyBindings();
        Stamps();
        Sorting();
        Surfaces();

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
        Is("all unchanged", 3, same.Unchanged.Count);
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
        Is("one kept", 1, diff.Unchanged.Count);
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
        Is("a reissued guid is unchanged", 2, diff.Unchanged.Count);

        // An unchanged block is kept as the newer version has it, the same way a
        // changed one is: the overlay can draw them, and only the newer version
        // says where the block is now. Taking the older record would leave the
        // guid it no longer has.
        bool fromNewer = false;
        for (int i = 0; i < diff.Unchanged.Count; i++)
        {
            fromNewer = fromNewer || diff.Unchanged[i].Id == "REISSUED";
        }
        True("an unchanged block comes from the newer version", fromNewer);

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

        // A block cannot be both new and altered. Besiege reissues identifiers, so a
        // machine can hold two blocks carrying the same one: one of the two pairs up
        // and the other does not, and without this the same identifier stands in both
        // columns and is drawn twice on the machine, green and orange at once.
        MachineSnapshot twiceBefore = Machine(Block("t", 1, 0, 0, 0));
        BlockRecord moved = Block("t", 1, 0, 0, 0);
        moved.Position = new Vector3(0.2f, 0f, 0f);
        MachineSnapshot twiceAfter = Machine(moved, Block("t", 1, 8, 0, 0));
        DiffResult twice = BlockDiff.Compare(twiceBefore, twiceAfter);
        Is("a block that is both new and altered is counted as new", 1,
           twice.Added.Count);
        Is("and not as a change as well", 0, twice.Changed.Count);

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
        Is("identical blocks pair by place", 2, paired.Unchanged.Count);
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

    // -- key bindings ---------------------------------------------------------------

    /// <summary>
    /// A block whose keys were remapped and nothing else. Besiege keeps key
    /// bindings in the same per-block data as sliders and toggles -- a StringArray
    /// per action, "bmt-left" and the rest -- so they are compared like any other
    /// setting and a remapped block is a changed block, not a deleted one and a new
    /// one. Worth pinning down: it is the one kind of change that leaves the
    /// machine looking identical, so nothing else about the block gives it away.
    /// </summary>
    static void KeyBindings()
    {
        BlockRecord before = Block("hinge", 28, 1, 0, 0);
        before.Settings = Keys("LeftArrow", "RightArrow");
        BlockRecord after = Block("hinge", 28, 1, 0, 0);
        after.Settings = Keys("Keypad4", "Keypad6");

        False("a remapped key is a change", before.Matches(after));

        DiffResult diff = BlockDiff.Compare(Machine(before), Machine(after));
        Is("nothing added", 0, diff.Added.Count);
        Is("nothing removed", 0, diff.Removed.Count);
        Is("the block is changed", 1, diff.Changed.Count);
        True("and it is the newer one, which is what the overlay draws",
             diff.Changed.Count == 1 && diff.Changed[0] == after);

        // Besiege reissues a guid when a block is copied or restored by an undo, so
        // the same remapping has to survive the block arriving with a new identity:
        // pass 2 cannot pair them, since their settings differ, which leaves pass 3
        // to recognise the same block type in the same place.
        BlockRecord reissued = Block("hinge-copy", 28, 1, 0, 0);
        reissued.Settings = after.Settings;
        DiffResult moved = BlockDiff.Compare(Machine(before), Machine(reissued));
        Is("a reissued guid is still one changed block", 1, moved.Changed.Count);
        Is("not an addition", 0, moved.Added.Count);
        Is("not a removal", 0, moved.Removed.Count);

        // Binding a key to a block that had none is a change too: the whole entry
        // appears rather than one value being replaced.
        BlockRecord unbound = Block("hinge", 28, 1, 0, 0);
        False("binding a key for the first time is a change", unbound.Matches(after));

        // And the reverse of all of it: the same keys written in a different order
        // are the same keys.
        BlockRecord same = Block("hinge", 28, 1, 0, 0);
        same.Settings = BlockRecord.FlattenSettings(new List<string>(new string[] {
            "bmt-right|StringArray=RightArrow",
            "bmt-rotation-speed|Single=" + BlockRecord.Quantise(1f),
            "bmt-left|StringArray=LeftArrow" }));
        True("the order settings are listed in is not a change", before.Matches(same));
    }

    static string Keys(string left, string right)
    {
        return BlockRecord.FlattenSettings(new List<string>(new string[] {
            "bmt-left|StringArray=" + left,
            "bmt-right|StringArray=" + right,
            "bmt-rotation-speed|Single=" + BlockRecord.Quantise(1f) }));
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

        BrowserDates();
    }

    // The date the load screen carries for a machine, which is what a row gets
    // when its name is the player's rather than Besiege's. It counts seconds from
    // the start of 2014 -- see VersionEntry.FromTimestamp -- and reading it as
    // anything else is not a wrong time but no time at all, since every real value
    // is then out of range.
    static void BrowserDates()
    {
        DateTime epoch = new DateTime(2014, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Is("the browser's dates start at 2014", epoch,
           VersionEntry.FromTimestamp(0.0).ToUniversalTime());
        Is("and count seconds", epoch.AddDays(365),
           VersionEntry.FromTimestamp(365.0 * 24.0 * 60.0 * 60.0).ToUniversalTime());

        double seconds = (new DateTime(2026, 8, 11, 10, 30, 0, DateTimeKind.Utc) -
                          epoch).TotalSeconds;
        DateTime read = VersionEntry.FromTimestamp(seconds);
        True("a real machine's date survives being read", read != DateTime.MinValue);
        Is("and is the year it was saved", 2026, read.ToUniversalTime().Year);

        VersionEntry chosen = new VersionEntry();
        chosen.FileName = "Tow-truck";
        chosen.Saved = read;
        True("so a chosen machine's row has its name on the first line",
             chosen.Lines().StartsWith("Tow-truck\n"));
        // The bug this is here for: with no readable date, Stamp falls back to the
        // file name, and both lines of the row read "Tow-truck".
        False("and a time rather than the name again on the second",
              chosen.Lines().EndsWith("Tow-truck"));
        Contains("which is a clock time", chosen.Lines(), ":");
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

        // By name, which is what the saved column sorts by now that a row can be a
        // machine with a name of its own rather than a version called after a time.
        // "first", "second", "third" happen to be neither alphabetical nor in time
        // order, so this cannot pass by accident.
        RowSort.Apply(rows, RowSort.ByName, true);
        Is("A to Z", "first", rows[0].FileName);
        Is("A to Z, second row", "second", rows[1].FileName);
        Is("A to Z, third row", "third", rows[2].FileName);

        RowSort.Apply(rows, RowSort.ByName, false);
        Is("Z to A", "third", rows[0].FileName);

        // By the number in the source column: a version's place in its history, or
        // the order machines chosen by hand were chosen in. The rows were built in
        // time order, so numbering them backwards is the only way to tell this
        // apart from sorting by time.
        rows[0].Number = 3;
        rows[1].Number = 2;
        rows[2].Number = 1;
        RowSort.Apply(rows, RowSort.ByNumber, true);
        Is("source order, not time order", 1, rows[0].Number);
        Is("source order, last row", 3, rows[2].Number);

        Is("column names", "REMOVED", RowSort.ColumnName(RowSort.ByRemoved));
        // The first column's heading is the one thing that says which kind of list
        // is on screen: a machine's own history, or machines picked out by hand.
        Is("the first column names the history", "VERSION",
           RowSort.ColumnName(RowSort.ByNumber));
        Is("and names the picking order", "SELECTION",
           RowSort.ColumnName(RowSort.ByNumber, true));
        Is("the other columns do not care", "ADDED",
           RowSort.ColumnName(RowSort.ByAdded, true));
        Is("the clock's column has a name too", "TIME", RowSort.ColumnName(RowSort.ByTime));
        Is("the name column has a name", "NAME", RowSort.ColumnName(RowSort.ByName));
        Is("the blocks column has a name", "BLOCKS", RowSort.ColumnName(RowSort.ByBlocks));
        Is("unknown columns fall back to it", "NAME", RowSort.ColumnName(99));

        // By how big the machine is, which is the one number in a row that is not a
        // comparison. Set against the time order so it cannot pass by accident.
        rows[0].BlockCount = 40;
        rows[1].BlockCount = 900;
        rows[2].BlockCount = 200;
        RowSort.Apply(rows, RowSort.ByBlocks, false);
        Is("biggest first", 900, rows[0].BlockCount);
        Is("smallest last", 40, rows[2].BlockCount);
        RowSort.Apply(rows, RowSort.ByBlocks, true);
        Is("smallest first", 40, rows[0].BlockCount);

        SortingIsAnOrder();
        WhatCameBefore();

        // The two-line row: whatever the row is called goes on the first line and its
        // time on the second, so that both line up under the two headings over that
        // column. A name Besiege wrote is a timestamp and nothing else, so its first
        // line holds only what the name says beyond the time -- which is whether the
        // player saved it or the timer did.
        VersionEntry auto = Row("aut 26.06.27 15-34-37", 4, 0, 0, 0);
        auto.Named = true;
        DateTime taken;
        bool byHand;
        VersionEntry.TryReadStamp(auto.FileName, out taken, out byHand);
        auto.Saved = taken;
        True("a version writes its time on the time line",
             auto.Lines().StartsWith("\n"));
        Contains("and nothing above it", auto.Lines(), "2026-06-27");
        VersionEntry saved = Row("ver 26.06.27 15-34-37", 6, 0, 0, 0);
        saved.Named = true;
        saved.Manual = true;
        True("a save the player took says so on the name line",
             saved.Lines().StartsWith("SAVED\n"));
        VersionEntry machine = Row("Tow-truck", 5, 0, 0, 0);
        True("a machine's name goes above its time", machine.Lines().StartsWith("Tow-truck\n"));

        // And a machine nothing can put a time to is one line: its name. The load
        // screen cannot always say when a machine was saved, and a row that repeats
        // the name on both lines says nothing twice.
        VersionEntry undated = new VersionEntry();
        undated.FileName = "Tow-truck";
        Is("an undated machine is just its name", "Tow-truck", undated.Lines());
    }

    // Every sort has to be a total order, because List.Sort is not stable: rows it
    // is told are equal come back in an arbitrary arrangement, and one that changes
    // between calls. A list of machines saved in the same second -- or one whose
    // times could not be read at all, which is what a browser date read as an OLE
    // automation date produced -- would then shuffle itself every time a heading
    // was clicked, and nothing on a row would look like it belonged to it.
    static void SortingIsAnOrder()
    {
        List<VersionEntry> tied = new List<VersionEntry>();
        for (int i = 0; i < 12; i++)
        {
            VersionEntry row = Row("machine " + i, 1, 0, 0, 0);
            row.Number = i + 1;
            tied.Add(row);
        }

        RowSort.Apply(tied, RowSort.ByTime, false);
        string first = tied[0].FileName + tied[1].FileName + tied[11].FileName;
        RowSort.Apply(tied, RowSort.ByName, true);
        RowSort.Apply(tied, RowSort.ByTime, false);
        Is("rows with the same time keep their order", first,
           tied[0].FileName + tied[1].FileName + tied[11].FileName);
        Is("which is the order of their numbers", "machine 0", tied[0].FileName);

        RowSort.Apply(tied, RowSort.ByAdded, false);
        Is("and so do rows with the same count", "machine 0", tied[0].FileName);
    }

    // What a row is compared against when nothing is pinned: the row under it, in
    // whatever order the list is being shown. So it follows the sort rather than
    // holding still against it -- the counts in the list are the differences between
    // each row and the one below, which is what the arrangement in front of the
    // player says they are.
    static void WhatCameBefore()
    {
        List<VersionEntry> undated = new List<VersionEntry>();
        for (int i = 0; i < 4; i++)
        {
            VersionEntry row = new VersionEntry();
            row.FileName = "machine " + i;
            row.Number = i + 1;
            undated.Add(row);          // Saved left at DateTime.MinValue, as picked
        }                              // machines with no autosaves come out

        Is("a row is compared with the one under it", "machine 1",
           RowSort.Below(undated, undated[0]).FileName);
        Is("and the bottom row has nothing under it", null,
           RowSort.Below(undated, undated[undated.Count - 1]));

        // Which is a fact about the arrangement, so re-arranging changes it. Sorting
        // by time puts undated rows in name order.
        undated[0].FileName = "zulu";
        undated[3].FileName = "alpha";
        RowSort.Apply(undated, RowSort.ByTime, true);
        Is("undated rows sort by name", "alpha", undated[0].FileName);
        Is("still by name, reversed", "zulu",
           Reversed(undated, RowSort.ByTime)[0].FileName);
        Is("and the pairing follows the new order", undated[1].FileName,
           RowSort.Below(undated, undated[0]).FileName);
        Is("wherever a row has ended up", undated[3].FileName,
           RowSort.Below(undated, undated[2]).FileName);

        // A history read newest-first -- the order the window opens in -- compares
        // each save with the save before it, which is what the row under it is.
        List<VersionEntry> history = new List<VersionEntry>();
        history.Add(Numbered(Row("newest", 9, 0, 0, 0), 3));
        history.Add(Numbered(Row("middle", 5, 0, 0, 0), 2));
        history.Add(Numbered(Row("oldest", 1, 0, 0, 0), 1));
        Is("the save before the newest is the middle one", "middle",
           RowSort.Below(history, history[0]).FileName);
        Is("and the oldest, at the bottom, has nothing before it", null,
           RowSort.Below(history, history[2]));

        WhatTheCountsAreOf(history);
    }

    // What the counts in a row are measured against, which is not the row under it:
    // it is the version before it in the machine's own history, whatever order the
    // list is in. They have to be a fact about the version rather than about the
    // arrangement, or the columns holding them cannot be sorted -- clicking ADDED
    // would put the rows in an order that stopped being true the moment it landed.
    static void WhatTheCountsAreOf(List<VersionEntry> history)
    {
        Is("a version's counts are against the version before it", "middle",
           RowSort.Earlier(history, history[0]).FileName);
        Is("and the oldest has nothing before it", null,
           RowSort.Earlier(history, history[2]));

        // The point of the exercise: this is the same answer however the list has
        // been arranged, including upside down.
        RowSort.Apply(history, RowSort.ByNumber, true);
        Is("which holds with the list the other way up", "middle",
           RowSort.Earlier(history, history[Last(history, "newest")]).FileName);
        RowSort.Apply(history, RowSort.ByName, false);
        Is("and in any other order", "middle",
           RowSort.Earlier(history, history[Last(history, "newest")]).FileName);

        True("count columns are known to need reading first",
             RowSort.IsCount(RowSort.ByAdded) && RowSort.IsCount(RowSort.ByBlocks));
        False("and the columns that are already known are not",
              RowSort.IsCount(RowSort.ByName) || RowSort.IsCount(RowSort.ByNumber) ||
              RowSort.IsCount(RowSort.ByTime));
    }

    static int Last(List<VersionEntry> rows, string name)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].FileName == name) { return i; }
        }
        return -1;
    }

    static VersionEntry Numbered(VersionEntry entry, int number)
    {
        entry.Number = number;
        return entry;
    }

    static List<VersionEntry> Reversed(List<VersionEntry> rows, int column)
    {
        List<VersionEntry> copy = new List<VersionEntry>(rows);
        RowSort.Apply(copy, column, false);
        return copy;
    }

    // -- fixtures ---------------------------------------------------------------------

    // -- build surfaces ---------------------------------------------------------------

    // A build surface is nine blocks: the surface, four edges and four corner nodes,
    // each naming the next by guid. Where it *is* is therefore a question about the
    // whole machine, and until it was answered a changed surface was marked by one
    // dot at one corner -- the surface block's own position -- with nothing drawn
    // between them.
    static void Surfaces()
    {
        MachineSnapshot machine = Surface(true);
        BlockRecord surface = machine.Blocks[0];
        True("a surface knows its own shape", surface.HasSurface);
        Is("with one corner per node", 4, surface.Corners.Length);

        // The corners have to come out in the order they go round the outline, which
        // is not the order the edges are named in: a fan of triangles through them in
        // any other order is a bow tie. Which corner it starts at does not matter --
        // that every step is along a side and never across the diagonal does.
        True("the corners come out in the order they go round",
             RoundTheOutline(surface.Corners));
        True("and every corner is there once",
             Spread(surface.Corners) == 4f + 3f + 4f + 3f);

        // Dragging a corner moves a node, not the surface: the surface's own
        // position, rotation and list of edges are all exactly what they were. The
        // corners go into its settings so that the diff notices anyway.
        MachineSnapshot moved = Surface(true);
        moved.Blocks[6].Position = new Vector3(9f, 0f, 0f);   // one of the nodes
        SurfaceShape.Link(moved);
        False("a dragged corner is a change to the surface",
              surface.Matches(moved.Blocks[0]));
        Is("and the surface itself has not moved", surface.Position,
           moved.Blocks[0].Position);

        // A machine saved twice without being touched must read the same both times,
        // or every surface in it would be reported as changed every save.
        Is("an untouched surface reads the same twice", surface.Settings,
           Surface(true).Blocks[0].Settings);

        // Half a surface -- an edge belonging to another machine, a file cut short --
        // is left alone rather than guessed at.
        MachineSnapshot broken = Surface(false);
        False("a surface whose edges do not join up claims no shape",
              broken.Blocks[0].HasSurface);

        // The pieces are drawn by the surface, so they are not drawn again on their
        // own: a corner node's ghost is the little ball you drag it by.
        True("a resolved surface speaks for its edges and corners",
             AllOf(machine, 1, machine.Blocks.Count, true));
        False("and one that could not be resolved speaks for nothing",
              AllOf(broken, 1, broken.Blocks.Count, true));

        // Three corners is a surface too, and the same walk closes it.
        MachineSnapshot triangle = Surface(true, 3);
        True("a three-cornered surface resolves", triangle.Blocks[0].HasSurface);
        Is("with three corners", 3, triangle.Blocks[0].Corners.Length);

        // Standing on its side, which is the case the outline's own plane has to be
        // worked out for rather than guessed as "flat".
        MachineSnapshot upright = Surface(true);
        for (int i = 5; i < upright.Blocks.Count; i++)
        {
            Vector3 was = upright.Blocks[i].Position;
            upright.Blocks[i].Position = new Vector3(was.x, was.z, 0f);
        }
        SurfaceShape.Link(upright);
        True("a surface standing on its side resolves like any other",
             upright.Blocks[0].HasSurface);
    }

    /// Whether the corners of the 4x3 test rectangle are in order round it: every
    /// step from one to the next is a side, never the diagonal.
    static bool RoundTheOutline(Vector3[] corners)
    {
        for (int i = 0; i < corners.Length; i++)
        {
            float step = (corners[(i + 1) % corners.Length] - corners[i]).magnitude;
            if (step > 4.001f || step < 2.999f) { return false; }
        }
        return true;
    }

    /// How far it is round the outline, corner to corner.
    static float Spread(Vector3[] corners)
    {
        float all = 0f;
        for (int i = 0; i < corners.Length; i++)
        {
            all += (corners[(i + 1) % corners.Length] - corners[i]).magnitude;
        }
        return all;
    }

    /// Whether every block from `from` to `to` is (or is not) part of a surface.
    static bool AllOf(MachineSnapshot machine, int from, int to, bool part)
    {
        for (int i = from; i < to; i++)
        {
            if (machine.Blocks[i].PartOfSurface != part) { return false; }
        }
        return true;
    }

    static MachineSnapshot Surface(bool whole)
    {
        return Surface(whole, 4);
    }

    /// Builds one surface out of its pieces -- the surface, an edge per side and a
    /// corner node per corner -- with the edges named out of order on purpose. Pass
    /// false to break the loop.
    static MachineSnapshot Surface(bool whole, int sides)
    {
        BlockRecord surface = Block("surface", 73, 0f, 0f, 0f);

        Vector3[] where = new Vector3[]
        {
            new Vector3(0f, 0f, 0f), new Vector3(4f, 0f, 0f),
            new Vector3(4f, 0f, 3f), new Vector3(0f, 0f, 3f)
        };
        BlockRecord[] nodes = new BlockRecord[sides];
        BlockRecord[] edges = new BlockRecord[sides];
        string[] named = new string[sides];
        for (int i = 0; i < sides; i++)
        {
            nodes[i] = Block("n" + i, 71, where[i].x, where[i].y, where[i].z);
            edges[i] = Block("e" + i, 72, 0f, 0f, 0f);
            edges[i].EdgeFrom = "n" + i;
            edges[i].EdgeTo = "n" + ((i + 1) % sides);
            // Named back to front: the file does not promise an order, and the walk
            // is what puts them in one.
            named[i] = "e" + (sides - 1 - i);
        }
        surface.EdgeIds = named;
        if (!whole)
        {
            edges[sides - 2].EdgeFrom = "somewhere-else";
            edges[sides - 2].EdgeTo = "somewhere-else-again";
        }

        MachineSnapshot machine = new MachineSnapshot();
        machine.Blocks.Add(surface);
        for (int i = 0; i < sides; i++) { machine.Blocks.Add(edges[i]); }
        for (int i = 0; i < sides; i++) { machine.Blocks.Add(nodes[i]); }
        SurfaceShape.Link(machine);
        return machine;
    }

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

    static void False(string what, bool condition)
    {
        checks++;
        if (condition) { failures.Add(what); }
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
