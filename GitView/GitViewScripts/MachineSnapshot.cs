using System.Collections.Generic;

namespace GitView
{
    /// <summary>
    /// One saved version of a machine, as the diff sees it: a name and a bag of
    /// blocks. Engine-free on purpose -- <c>VersionScan</c> is what turns one of
    /// the game's <c>MachineInfo</c> objects into this.
    /// </summary>
    public class MachineSnapshot
    {
        public string Title = string.Empty;
        public readonly List<BlockRecord> Blocks = new List<BlockRecord>();

        public int Count
        {
            get { return Blocks.Count; }
        }

        public static MachineSnapshot Empty()
        {
            return new MachineSnapshot();
        }
    }
}
