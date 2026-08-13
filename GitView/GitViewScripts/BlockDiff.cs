using System.Collections.Generic;

namespace GitView
{
    /// <summary>What one save did to the machine, relative to the save before it.</summary>
    public class DiffResult
    {
        /// <summary>Blocks that exist in the newer version and not in the older one.</summary>
        public readonly List<BlockRecord> Added = new List<BlockRecord>();

        /// <summary>
        /// Blocks in both, but moved, rotated, rescaled, reskinned or retuned. Held
        /// as they are in the *newer* version, which is where the overlay draws them.
        /// </summary>
        public readonly List<BlockRecord> Changed = new List<BlockRecord>();

        /// <summary>Blocks that were in the older version and are gone.</summary>
        public readonly List<BlockRecord> Removed = new List<BlockRecord>();

        /// <summary>
        /// Blocks that came through the save untouched. Held rather than counted
        /// because the overlay can draw them: not the answer to "what changed", but
        /// the thing the answer is attached to.
        /// </summary>
        public readonly List<BlockRecord> Unchanged = new List<BlockRecord>();

        public bool IsEmpty
        {
            get { return Added.Count == 0 && Changed.Count == 0 && Removed.Count == 0; }
        }
    }

    /// <summary>
    /// Works out which blocks a save added, changed and removed.
    ///
    /// Pairing by the guid alone is right most of the time and quietly wrong the
    /// rest: Besiege reissues a guid when a block is copied, mirrored or re-added by
    /// an undo, and trusting it then reports a deletion and an addition of the same
    /// block in the same place. So the pairing takes three passes, each looking only
    /// at what the one before could not place:
    ///
    ///   1. equal guids -- the common case, and the only pass that can tell a block
    ///      that moved from a block that was replaced;
    ///   2. equal in every other respect -- a reissued guid on an untouched block;
    ///   3. same type, nearest position within half a block -- a reissued guid on a
    ///      block that also moved.
    ///
    /// Whatever is still unpaired really was added or removed. Over six real
    /// machines that leaves about one unexplained block per folder, against nine per
    /// save for the guid alone.
    /// </summary>
    public static class BlockDiff
    {
        /// <summary>
        /// How far a block may have moved and still be recognised as itself once its
        /// guid has stopped matching. Pass 3 only: an ordinary moved block keeps its
        /// guid however far it travels. Past about half a block, "deleted, and
        /// another placed nearby" is the more honest reading anyway.
        /// </summary>
        public const float MatchRadius = 0.6f;

        public static DiffResult Compare(MachineSnapshot older, MachineSnapshot newer)
        {
            DiffResult result = new DiffResult();
            List<BlockRecord> before = older == null ? new List<BlockRecord>() : older.Blocks;
            List<BlockRecord> after = newer == null ? new List<BlockRecord>() : newer.Blocks;

            List<BlockRecord> unpairedBefore = new List<BlockRecord>(before);
            List<BlockRecord> unpairedAfter = new List<BlockRecord>();
            List<BlockRecord> pairedBefore = new List<BlockRecord>();
            List<BlockRecord> pairedAfter = new List<BlockRecord>();

            PairByIdentifier(before, after, unpairedBefore, unpairedAfter,
                             pairedBefore, pairedAfter);
            unpairedAfter = PairByLikeness(unpairedBefore, unpairedAfter,
                                           pairedBefore, pairedAfter);
            unpairedAfter = PairByProximity(unpairedBefore, unpairedAfter,
                                            pairedBefore, pairedAfter);

            for (int i = 0; i < pairedBefore.Count; i++)
            {
                if (pairedBefore[i].Matches(pairedAfter[i]))
                {
                    result.Unchanged.Add(pairedAfter[i]);
                }
                else
                {
                    result.Changed.Add(pairedAfter[i]);
                }
            }
            result.Added.AddRange(unpairedAfter);
            result.Removed.AddRange(unpairedBefore);
            OnceEach(result.Added, result.Changed);
            return result;
        }

        /// <summary>
        /// Makes sure no block is counted twice, added winning over changed.
        ///
        /// The passes above pair each block once, so on an ordinary machine this
        /// finds nothing. It is for the machine holding two blocks with the same
        /// identifier -- which Besiege can produce -- where one pairs up and the
        /// other does not, leaving the same block in both columns and drawn twice,
        /// once green and once orange. "There is something here that was not here
        /// before" is the larger fact.
        /// </summary>
        private static void OnceEach(List<BlockRecord> added, List<BlockRecord> changed)
        {
            if (added.Count == 0 || changed.Count == 0)
            {
                return;
            }

            Dictionary<string, bool> isNew = new Dictionary<string, bool>();
            for (int i = 0; i < added.Count; i++)
            {
                isNew[added[i].Id ?? string.Empty] = true;
            }
            for (int i = changed.Count - 1; i >= 0; i--)
            {
                if (isNew.ContainsKey(changed[i].Id ?? string.Empty))
                {
                    changed.RemoveAt(i);
                }
            }
        }

        /// <summary>Pass 1: blocks that kept their guid.</summary>
        private static void PairByIdentifier(List<BlockRecord> before, List<BlockRecord> after,
                                             List<BlockRecord> unpairedBefore,
                                             List<BlockRecord> unpairedAfter,
                                             List<BlockRecord> pairedBefore,
                                             List<BlockRecord> pairedAfter)
        {
            Dictionary<string, List<BlockRecord>> byId = Bucket(before);
            for (int i = 0; i < after.Count; i++)
            {
                BlockRecord candidate = after[i];
                BlockRecord match = TakeFrom(byId, candidate.Id);
                if (match == null)
                {
                    unpairedAfter.Add(candidate);
                    continue;
                }
                unpairedBefore.Remove(match);
                pairedBefore.Add(match);
                pairedAfter.Add(candidate);
            }
        }

        /// <summary>Pass 2: a reissued guid on a block that is otherwise identical.</summary>
        private static List<BlockRecord> PairByLikeness(List<BlockRecord> unpairedBefore,
                                                        List<BlockRecord> unpairedAfter,
                                                        List<BlockRecord> pairedBefore,
                                                        List<BlockRecord> pairedAfter)
        {
            if (unpairedBefore.Count == 0 || unpairedAfter.Count == 0)
            {
                return unpairedAfter;
            }

            Dictionary<string, List<BlockRecord>> byLikeness =
                new Dictionary<string, List<BlockRecord>>();
            for (int i = 0; i < unpairedBefore.Count; i++)
            {
                Add(byLikeness, unpairedBefore[i].IdentityKey(), unpairedBefore[i]);
            }

            List<BlockRecord> stillUnpaired = new List<BlockRecord>();
            for (int i = 0; i < unpairedAfter.Count; i++)
            {
                BlockRecord candidate = unpairedAfter[i];
                BlockRecord match = TakeFrom(byLikeness, candidate.IdentityKey());
                if (match == null)
                {
                    stillUnpaired.Add(candidate);
                    continue;
                }
                unpairedBefore.Remove(match);
                pairedBefore.Add(match);
                pairedAfter.Add(candidate);
            }
            return stillUnpaired;
        }

        /// <summary>Pass 3: a reissued guid on a block that also moved.</summary>
        private static List<BlockRecord> PairByProximity(List<BlockRecord> unpairedBefore,
                                                         List<BlockRecord> unpairedAfter,
                                                         List<BlockRecord> pairedBefore,
                                                         List<BlockRecord> pairedAfter)
        {
            if (unpairedBefore.Count == 0 || unpairedAfter.Count == 0)
            {
                return unpairedAfter;
            }

            List<BlockRecord> stillUnpaired = new List<BlockRecord>();
            for (int i = 0; i < unpairedAfter.Count; i++)
            {
                BlockRecord candidate = unpairedAfter[i];
                BlockRecord best = null;
                float bestDistance = MatchRadius;

                for (int j = 0; j < unpairedBefore.Count; j++)
                {
                    BlockRecord other = unpairedBefore[j];
                    if (other.Kind != candidate.Kind)
                    {
                        continue;
                    }
                    float distance = other.DistanceTo(candidate);
                    if (distance < bestDistance)
                    {
                        best = other;
                        bestDistance = distance;
                    }
                }

                if (best == null)
                {
                    stillUnpaired.Add(candidate);
                    continue;
                }
                unpairedBefore.Remove(best);
                pairedBefore.Add(best);
                pairedAfter.Add(candidate);
            }
            return stillUnpaired;
        }

        private static Dictionary<string, List<BlockRecord>> Bucket(List<BlockRecord> records)
        {
            Dictionary<string, List<BlockRecord>> buckets =
                new Dictionary<string, List<BlockRecord>>();
            for (int i = 0; i < records.Count; i++)
            {
                Add(buckets, records[i].Id, records[i]);
            }
            return buckets;
        }

        private static void Add(Dictionary<string, List<BlockRecord>> buckets,
                                string key, BlockRecord record)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }
            List<BlockRecord> bucket;
            if (!buckets.TryGetValue(key, out bucket))
            {
                bucket = new List<BlockRecord>();
                buckets[key] = bucket;
            }
            bucket.Add(record);
        }

        private static BlockRecord TakeFrom(Dictionary<string, List<BlockRecord>> buckets,
                                            string key)
        {
            List<BlockRecord> bucket;
            if (string.IsNullOrEmpty(key) || !buckets.TryGetValue(key, out bucket)
                || bucket.Count == 0)
            {
                return null;
            }
            BlockRecord taken = bucket[0];
            bucket.RemoveAt(0);
            return taken;
        }
    }
}
