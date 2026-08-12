using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GitView
{
    /// <summary>
    /// Works out where each build surface in a machine actually is.
    ///
    /// A build surface is not one block but nine: the surface itself, four edges and
    /// four corner nodes, each of them a block with a guid of its own. The surface
    /// names its edges, an edge names the two nodes it runs between, and a node is
    /// where a corner is. So the shape can only be worked out with the whole machine
    /// in hand -- which is why it is a pass of its own, after every block has been
    /// read, rather than something a block can answer about itself.
    ///
    /// Engine-free apart from Vector3, so the headless tests cover it: the walk round
    /// the edges is the part that can be wrong, and it is wrong in ways that only
    /// show up on a machine somebody built.
    /// </summary>
    public static class SurfaceShape
    {
        /// <summary>
        /// Fills in <see cref="BlockRecord.Corners"/> for every build surface in the
        /// snapshot.
        ///
        /// The corners go into the block's settings as well, so that dragging a
        /// corner counts as a change to the surface. Nothing else would notice it:
        /// the surface's own position and its list of edge guids are the same before
        /// and after, and the node that actually moved is a block with nothing to
        /// draw.
        /// </summary>
        public static void Link(MachineSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Blocks == null)
            {
                return;
            }

            // Built on demand: most machines have no build surface in them, and this
            // runs once per version of every machine in a folder.
            Dictionary<string, BlockRecord> byId = null;
            for (int i = 0; i < snapshot.Blocks.Count; i++)
            {
                if (snapshot.Blocks[i].EdgeIds == null)
                {
                    continue;
                }
                if (byId == null)
                {
                    byId = new Dictionary<string, BlockRecord>();
                    for (int b = 0; b < snapshot.Blocks.Count; b++)
                    {
                        byId[snapshot.Blocks[b].Id] = snapshot.Blocks[b];
                    }
                }
                Resolve(snapshot.Blocks[i], byId);
            }
        }

        /// <summary>
        /// Follows one surface's edges round its outline.
        ///
        /// Walked as a loop rather than taken in the order the edges are named: the
        /// corners have to come out in the order they go round the shape, which is
        /// what drawing it needs, and the file promises only that the four edges join
        /// up -- not that they are listed in that order.
        ///
        /// Anything that does not join up is left alone rather than guessed at. A
        /// surface that cannot be resolved draws the way it always did: a mark at the
        /// block's own position, which is one of its corners.
        /// </summary>
        private static void Resolve(BlockRecord surface,
                                    Dictionary<string, BlockRecord> byId)
        {
            string[] edges = surface.EdgeIds;
            if (edges.Length < 3)
            {
                return;
            }

            string[] from = new string[edges.Length];
            string[] to = new string[edges.Length];
            for (int i = 0; i < edges.Length; i++)
            {
                BlockRecord edge;
                if (!byId.TryGetValue(edges[i] ?? string.Empty, out edge) ||
                    edge.EdgeFrom.Length == 0 || edge.EdgeTo.Length == 0)
                {
                    // An edge that is not in this file, or one that does not name its
                    // ends.
                    return;
                }
                from[i] = edge.EdgeFrom;
                to[i] = edge.EdgeTo;
            }

            List<string> order = new List<string>();
            bool[] used = new bool[edges.Length];
            order.Add(from[0]);
            order.Add(to[0]);
            used[0] = true;
            for (int step = 1; step < edges.Length; step++)
            {
                string here = order[order.Count - 1];
                int next = -1;
                for (int i = 0; i < edges.Length && next < 0; i++)
                {
                    if (used[i])
                    {
                        continue;
                    }
                    if (from[i] == here) { next = i; order.Add(to[i]); }
                    else if (to[i] == here) { next = i; order.Add(from[i]); }
                }
                if (next < 0)
                {
                    // The edges do not form one loop, which is not a shape.
                    return;
                }
                used[next] = true;
            }
            // The walk comes back to where it started, and the closing corner is the
            // first one again rather than a corner of its own.
            if (order.Count > 1 && order[order.Count - 1] == order[0])
            {
                order.RemoveAt(order.Count - 1);
            }
            if (order.Count < 3)
            {
                return;
            }

            Vector3[] corners = new Vector3[order.Count];
            BlockRecord[] nodes = new BlockRecord[order.Count];
            for (int i = 0; i < order.Count; i++)
            {
                BlockRecord node;
                if (!byId.TryGetValue(order[i], out node))
                {
                    return;
                }
                nodes[i] = node;
                corners[i] = node.Position;
            }
            surface.Corners = corners;
            surface.Settings = surface.Settings + "|corners:" + Flatten(corners);

            // The pieces are spoken for: the surface now draws all of them, so they
            // are not to be drawn again on their own. Marked only once the shape has
            // been resolved -- a surface that could not be followed still needs its
            // corners drawn by whatever can draw them.
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i].PartOfSurface = true;
            }
            for (int i = 0; i < edges.Length; i++)
            {
                BlockRecord edge;
                if (byId.TryGetValue(edges[i], out edge))
                {
                    edge.PartOfSurface = true;
                }
            }
        }

        /// <summary>
        /// The corners as text, rounded the way every other position this mod
        /// compares is -- see <see cref="BlockRecord.Quantise"/>. A surface saved
        /// twice without being touched has to read the same both times.
        /// </summary>
        private static string Flatten(Vector3[] corners)
        {
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < corners.Length; i++)
            {
                text.Append(BlockRecord.Quantise(corners[i].x)).Append(',');
                text.Append(BlockRecord.Quantise(corners[i].y)).Append(',');
                text.Append(BlockRecord.Quantise(corners[i].z)).Append(';');
            }
            return text.ToString();
        }
    }
}
