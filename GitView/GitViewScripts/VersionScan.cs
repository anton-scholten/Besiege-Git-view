using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace GitView
{
    /// <summary>
    /// Everything that reaches into Besiege's autosave folder: finding a machine's
    /// versions, reading one into a <see cref="MachineSnapshot"/>, and putting one
    /// back into the build area.
    ///
    /// It never touches the filesystem itself. Listing goes through the same
    /// virtual folders the load screen browses, and a .bsg is parsed by the game's
    /// own <c>XmlLoader</c> -- which is not politeness but necessity: the mod
    /// loader refuses any assembly that references System.IO's file classes or
    /// System.Xml at all, so a mod cannot open its own files, let alone parse them.
    /// </summary>
    public static class VersionScan
    {
        /// <summary>The folder Besiege keeps machine backups in, under SavedMachines.</summary>
        public const string AutoSaveFolder = "AutoSave";

        private const string MachineSuffix = ".bsg";

        // ---------------------------------------------------------------- listing

        /// <summary>
        /// The version folder for a slot in the load screen, or null if that
        /// machine has no history.
        ///
        /// The route to it is the game's own, from
        /// <c>FileBrowserView.OnPageViewSlotVersions</c>: walk up to the root of
        /// whatever collection the slot came from, find the AutoSave folder in it,
        /// and find the folder named after the machine. Deliberately not built out
        /// of <c>StaticSettings.MachineAutosavePath</c> and a string join -- the
        /// browser can be showing local files, a Steam collection or mod.io, and
        /// only the object it handed us knows which.
        /// </summary>
        public static VirtualFolder FolderFor(IVirtualObject machineObject)
        {
            if (machineObject == null)
            {
                return null;
            }

            try
            {
                // The slot may already be one of the machine folders inside AutoSave.
                if (machineObject.IsFolder && IsInsideAutoSave(machineObject))
                {
                    return Opened(machineObject as VirtualFolder);
                }

                VirtualFolder root = machineObject.Parent;
                if (root == null)
                {
                    return null;
                }
                while (root.Parent != null)
                {
                    root = root.Parent;
                }

                VirtualFolder autoSave = Opened(FindFolder(root, AutoSaveFolder));
                if (autoSave == null)
                {
                    return null;
                }
                return Opened(FindFolder(autoSave, machineObject.Name));
            }
            catch (Exception e)
            {
                Log.Warn("could not reach the autosave folder: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// When a machine was last saved, taken from the newest version in its
        /// autosave folder, or <c>DateTime.MinValue</c> if it has none.
        ///
        /// The long way round to a file's date, and the only one there is: the
        /// browser's own <c>Date</c> is <c>DateTime.Now</c> for everything on this
        /// platform (see <see cref="VersionEntry.FromTimestamp"/>), a mod may not
        /// touch <c>System.IO.File</c>, and nothing Besiege exposes will answer the
        /// question either. What it *has* written down is the time in the name of
        /// every autosave, so the newest of those is when the machine was last
        /// saved -- give or take the minutes since the last autosave ran.
        /// </summary>
        public static DateTime LastSaved(IVirtualObject machineObject)
        {
            List<VersionEntry> versions = Versions(FolderFor(machineObject));
            DateTime newest = DateTime.MinValue;
            for (int i = 0; i < versions.Count; i++)
            {
                if (versions[i].Saved > newest)
                {
                    newest = versions[i].Saved;
                }
            }
            return newest;
        }

        /// <summary>True if this machine has any saved history to show.</summary>
        public static bool HasHistory(IVirtualObject machineObject)
        {
            VirtualFolder folder = FolderFor(machineObject);
            if (folder == null)
            {
                return false;
            }
            foreach (IVirtualObject child in folder.GetObjects())
            {
                if (child != null && !child.IsFolder)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The versions in a machine's history, oldest first.
        ///
        /// Both kinds are included. Besiege writes an "aut" file on a timer and a
        /// "ver" file when the player saves over the machine, and they are the same
        /// thing to a diff -- a snapshot with a time on it. Sorting them into one
        /// timeline is the only way the counts mean anything, since a manual save
        /// lands between two timed ones.
        /// </summary>
        public static List<VersionEntry> Versions(VirtualFolder folder)
        {
            List<VersionEntry> versions = new List<VersionEntry>();
            if (folder == null)
            {
                return versions;
            }

            try
            {
                foreach (IVirtualObject child in folder.GetObjects())
                {
                    VersionEntry entry = Describe(child);
                    if (entry != null)
                    {
                        versions.Add(entry);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn("could not list the versions: " + e.Message);
                return versions;
            }

            // Numbered here, while the list is in time order and before anything
            // else has had a chance to sort it. A version's number is a fact about
            // the history, not about where it happens to sit in the window.
            RowSort.Apply(versions, RowSort.ByTime, true);
            for (int i = 0; i < versions.Count; i++)
            {
                versions[i].Number = i + 1;
            }
            return versions;
        }

        private static VersionEntry Describe(IVirtualObject file)
        {
            if (file == null || file.IsFolder)
            {
                return null;
            }

            string path = file.ObjectPath.Path;
            if (string.IsNullOrEmpty(path) || !EndsWithSuffix(path, MachineSuffix))
            {
                return null;
            }

            VersionEntry entry = new VersionEntry();
            entry.Path = path;
            entry.FileName = StripSuffix(file.Name);
            try
            {
                entry.ThumbnailPath = file.ThumbnailPath.Path;
            }
            catch (Exception)
            {
                entry.ThumbnailPath = string.Empty;
            }

            DateTime stamp;
            bool manual;
            if (VersionEntry.TryReadStamp(entry.FileName, out stamp, out manual))
            {
                entry.Saved = stamp;
                entry.Manual = manual;
                entry.Named = true;
            }
            else
            {
                // A file somebody renamed, or one from a Besiege that named them
                // differently. The collection's own date still sorts it correctly.
                entry.Saved = FromCollectionDate(file);
            }
            return entry;
        }

        /// <summary>
        /// The date the virtual filesystem carries, as a fallback for a name that
        /// cannot be parsed. See <see cref="VersionEntry.FromTimestamp"/> for what
        /// that number actually counts.
        /// </summary>
        private static DateTime FromCollectionDate(IVirtualObject file)
        {
            try
            {
                return VersionEntry.FromTimestamp(file.Date);
            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
        }

        // ---------------------------------------------------------------- reading

        /// <summary>
        /// Reads a .bsg into the model the diff works on. Returns null if the file
        /// cannot be parsed, which the caller shows as an uncounted row rather than
        /// letting one bad file empty the whole list.
        ///
        /// Loaded as a "dummy": the parse is the same, but the game skips the work
        /// that only matters for a machine about to exist, and reading seventy of
        /// these in a row should not drag skin packs in behind it.
        /// </summary>
        public static MachineSnapshot Read(string path)
        {
            MachineInfo info;
            try
            {
                info = XmlLoader.LoadFromFullPath(path, true, string.Empty);
            }
            catch (Exception e)
            {
                Log.Warn("could not read " + path + ": " + e.Message);
                return null;
            }
            if (info == null)
            {
                return null;
            }

            MachineSnapshot snapshot = new MachineSnapshot();
            snapshot.Title = info.Name;
            if (info.Blocks == null)
            {
                return snapshot;
            }

            for (int i = 0; i < info.Blocks.Count; i++)
            {
                BlockRecord record = Convert(info.Blocks[i]);
                if (record != null)
                {
                    snapshot.Blocks.Add(record);
                }
            }
            SurfaceShape.Link(snapshot);
            return snapshot;
        }

        private static BlockRecord Convert(BlockInfo block)
        {
            if (block == null)
            {
                return null;
            }

            BlockRecord record = new BlockRecord();
            record.Id = block.Guid.ToString();
            record.Kind = (int)block.ID;
            record.Position = block.Position;
            record.Rotation = block.Rotation;
            record.Scale = block.Scale;
            record.Flipped = block.Flipped;
            record.SkinName = SkinOf(block);
            record.Settings = SettingsOf(block);
            ReadSpan(block, record);
            ReadLinks(block, record);
            return record;
        }

        /// <summary>The key Besiege writes a dragged block's first end under.</summary>
        private const string SpanStartKey = "start-position";
        private const string SpanEndKey = "end-position";

        /// <summary>
        /// Picks up the two ends of a block that has two: a brace, a fuel line, a
        /// winch's rope.
        ///
        /// Recognised by the data rather than by a list of block types, because the
        /// data is what says it. Besiege's own <c>GenericDraggedBlock</c> and
        /// <c>FuelLineBehaviour</c> both write these two keys and nothing else does,
        /// and a modded block that drags the same way writes them too and gets drawn
        /// properly for free.
        /// </summary>
        private static void ReadSpan(BlockInfo block, BlockRecord record)
        {
            XDataHolder data = block.BlockData;
            if (data == null || !data.HasData)
            {
                return;
            }
            try
            {
                if (!data.HasKey(SpanStartKey) || !data.HasKey(SpanEndKey))
                {
                    return;
                }
                record.SpanStart = data.ReadVector3(SpanStartKey);
                record.SpanEnd = data.ReadVector3(SpanEndKey);
                record.HasSpan = true;
            }
            catch (Exception e)
            {
                Log.Warn("could not read a dragged block's ends: " + e.Message);
            }
        }

        // What a build surface writes: the guids of its four edges, and what an edge
        // writes: the guids of the two corner nodes it runs between. Recognised by
        // the keys rather than by block type, as the two ends of a brace are.
        private const string EdgesKey = "edges";
        private const string EdgeStartKey = "start";
        private const string EdgeEndKey = "end";
        private const string ThicknessKey = "bmt-thickness";

        /// <summary>
        /// Picks up what a block says about other blocks: a build surface's edges,
        /// and an edge's two corner nodes.
        ///
        /// Read here and followed later, because a guid means nothing until the
        /// block it names has been read too. See <see cref="SurfaceShape.Link"/>.
        /// </summary>
        private static void ReadLinks(BlockInfo block, BlockRecord record)
        {
            XDataHolder data = block.BlockData;
            if (data == null || !data.HasData)
            {
                return;
            }
            try
            {
                if (data.HasKey(EdgesKey))
                {
                    string edges = data.ReadString(EdgesKey);
                    if (!string.IsNullOrEmpty(edges))
                    {
                        record.EdgeIds = edges.Split('|');
                    }
                    if (data.HasKey(ThicknessKey))
                    {
                        // How thick the player made the slab, which is how thick the
                        // mark drawn over it has to be to be outside it.
                        record.Thickness = Mathf.Abs(data.ReadFloat(ThicknessKey));
                    }
                }
                // Not "start-position" and "end-position", which are the two ends of
                // a brace: these two hold guids, and only the pieces of a surface
                // write them.
                if (data.HasKey(EdgeStartKey) && data.HasKey(EdgeEndKey))
                {
                    record.EdgeFrom = data.ReadString(EdgeStartKey) ?? string.Empty;
                    record.EdgeTo = data.ReadString(EdgeEndKey) ?? string.Empty;
                }
            }
            catch (Exception e)
            {
                Log.Warn("could not read what a block is joined to: " + e.Message);
            }
        }

        private static string SkinOf(BlockInfo block)
        {
            try
            {
                BlockSkinLoader.SkinPack.Skin skin = block.Skin;
                if (skin == null || skin.isDefault)
                {
                    return string.Empty;
                }
                return skin.path;
            }
            catch (Exception)
            {
                // Whether a skin is resolved at all depends on how the file was
                // loaded. It is resolved the same way for every version in the
                // folder, so an empty answer here costs a re-skin showing up as a
                // change -- never a false one.
                return string.Empty;
            }
        }

        /// <summary>
        /// Flattens a block's settings into one comparable string.
        ///
        /// Read through <c>XData.RawValue</c> rather than <c>XData.Encode</c>, even
        /// though the encoded bytes would be exact and a great deal less code.
        /// Exact is the problem: a piston's start and end positions are settings,
        /// they are written out of live physics, and they come back a hair
        /// different every save. Comparing the bytes calls that a change; going
        /// through the values means <see cref="BlockRecord.Quantise"/> can round the
        /// noise off first.
        ///
        /// The block's own <c>XDataHolder.Encode</c> is not used either, for a
        /// different reason: it carries session flags like "was this loaded from a
        /// file", which would make every block in every version read as changed.
        /// </summary>
        private static string SettingsOf(BlockInfo block)
        {
            XDataHolder data = block.BlockData;
            if (data == null || !data.HasData)
            {
                return string.Empty;
            }

            List<string> entries = new List<string>();
            try
            {
                foreach (XData item in data.ReadAll())
                {
                    if (item != null)
                    {
                        entries.Add(Describe(item));
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn("could not read a block's settings: " + e.Message);
                return string.Empty;
            }
            return BlockRecord.FlattenSettings(entries);
        }

        /// <summary>
        /// One setting -- its key, its type and its value -- as the one line of
        /// text the diff compares.
        ///
        /// Public because the same rules have to apply to a setting read out of a
        /// .bsg and to one read live off a block's mapper: <see cref="MapperWatch"/>
        /// decides whether the player actually changed anything, and it would be
        /// worse than useless if it disagreed with the diff about what a change is.
        /// </summary>
        public static string Describe(XData item)
        {
            if (item == null)
            {
                return string.Empty;
            }
            return item.Key + "|" + item.Type + "=" + Value(item.RawValue);
        }

        /// <summary>
        /// A setting's value as text, with every number quantised.
        ///
        /// The types are checked one by one rather than asked what they are:
        /// <c>Type.Name</c> is <c>System.Reflection.MemberInfo.get_Name</c>, and one
        /// reference to that is enough for the mod loader to refuse the whole
        /// assembly. An `is` test compiles to isinst, which is not reflection.
        /// </summary>
        private static string Value(object raw)
        {
            if (raw == null)
            {
                return string.Empty;
            }
            if (raw is float) { return BlockRecord.Quantise((float)raw); }
            if (raw is double) { return BlockRecord.Quantise((double)raw); }
            if (raw is Vector3)
            {
                Vector3 vector = (Vector3)raw;
                return BlockRecord.Quantise(vector.x) + "," + BlockRecord.Quantise(vector.y) +
                       "," + BlockRecord.Quantise(vector.z);
            }
            if (raw is Color)
            {
                Color colour = (Color)raw;
                return BlockRecord.Quantise(colour.r) + "," + BlockRecord.Quantise(colour.g) +
                       "," + BlockRecord.Quantise(colour.b) + "," +
                       BlockRecord.Quantise(colour.a);
            }

            Array array = raw as Array;
            if (array != null)
            {
                StringBuilder joined = new StringBuilder();
                for (int i = 0; i < array.Length; i++)
                {
                    if (i > 0) { joined.Append(','); }
                    joined.Append(Value(array.GetValue(i)));
                }
                return joined.ToString();
            }

            return System.Convert.ToString(raw, CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------- loading

        /// <summary>
        /// Replaces the machine in the build area with a saved version.
        ///
        /// Goes through <c>Machine.LoadMachineInfo</c>, the same call the load
        /// screen makes, so joints, clusters and physics are worked out by Besiege
        /// exactly as they are for any other machine and an interrupted load cannot
        /// leave half a machine behind.
        /// </summary>
        public static bool LoadIntoWorld(string path)
        {
            Machine machine = Machine.Active();
            if (machine == null)
            {
                Log.Warn("there is no machine to load into.");
                return false;
            }

            try
            {
                MachineInfo info = XmlLoader.LoadFromFullPath(path, false, string.Empty);
                if (info == null)
                {
                    return false;
                }
                machine.LoadMachineInfo(info, true);
                return true;
            }
            catch (Exception e)
            {
                Log.Error("could not load " + path + ": " + e.Message);
                return false;
            }
        }

        /// <summary>A version's thumbnail, or null if it has none.</summary>
        public static Texture LoadThumbnail(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            try
            {
                return Besiege.AssetImporter.LoadTexture(path, false, false);
            }
            catch (Exception e)
            {
                Log.Warn("could not load thumbnail " + path + ": " + e.Message);
                return null;
            }
        }

        // ---------------------------------------------------------------- helpers

        private static bool IsInsideAutoSave(IVirtualObject virtualObject)
        {
            VirtualFolder parent = virtualObject.Parent;
            return parent != null && parent.Name == AutoSaveFolder;
        }

        private static VirtualFolder FindFolder(VirtualFolder within, string name)
        {
            if (within == null || string.IsNullOrEmpty(name))
            {
                return null;
            }
            foreach (IVirtualObject child in within.GetObjects())
            {
                if (child != null && child.IsFolder && child.Name == name)
                {
                    return child as VirtualFolder;
                }
            }
            return null;
        }

        /// <summary>
        /// A virtual folder does not list its contents until it is opened, so every
        /// step down the path needs this. Opening one is a re-read of the directory,
        /// not a navigation -- the browser stays where the player left it.
        /// </summary>
        private static VirtualFolder Opened(VirtualFolder folder)
        {
            if (folder != null)
            {
                folder.Open();
            }
            return folder;
        }

        private static bool EndsWithSuffix(string path, string suffix)
        {
            return path.Length > suffix.Length &&
                   string.Compare(path, path.Length - suffix.Length, suffix, 0,
                                  suffix.Length, StringComparison.OrdinalIgnoreCase) == 0;
        }

        private static string StripSuffix(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }
            return EndsWithSuffix(name, MachineSuffix)
                ? name.Substring(0, name.Length - MachineSuffix.Length)
                : name;
        }
    }
}
