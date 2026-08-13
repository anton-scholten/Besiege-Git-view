using System;
using System.Collections.Generic;
using UnityEngine;

namespace GitView
{
    /// <summary>
    /// Makes Besiege's autosave notice that a block was retuned.
    ///
    /// The autosave only fires when <c>MachineUpdatedSinceLastSave</c> is set, and
    /// the only things that set it are the seven places raising
    /// <c>ReferenceMaster.onMachineModified</c>: placing, deleting, dragging,
    /// mirroring, undoing. Remapping a key or moving a slider raises nothing, so the
    /// timer comes round, finds the flag clear and skips the save -- and the new
    /// setting never reaches a version at all.
    ///
    /// So the mapper is watched: its settings are fingerprinted when it opens,
    /// compared when it closes, and if they differ the game is told the machine was
    /// modified exactly as it is told when a block is dragged. Nothing is saved here;
    /// the next autosave does the work on its own schedule.
    /// </summary>
    public class MapperWatch : MonoBehaviour
    {
        /// <summary>
        /// The holder the mapper was opened on. Kept rather than asked for again on
        /// close: <c>BlockMapper.Current</c> is cleared as part of closing.
        /// </summary>
        private SaveableDataHolder _holder;
        private string _before = string.Empty;
        private bool _open;
        private bool _hooked;

        private void Start()
        {
            Hook();
        }

        private void OnDestroy()
        {
            Unhook();
        }

        /// <summary>
        /// Subscribes to the mapper's open and close callbacks. Plain static
        /// delegates rather than events, so they survive scene loads and have to be
        /// unsubscribed by hand -- once, when the game shuts down.
        /// </summary>
        private void Hook()
        {
            if (_hooked)
            {
                return;
            }
            try
            {
                BlockMapper.onMapperOpen += OnMapperOpen;
                BlockMapper.onMapperClose += OnMapperClose;
                _hooked = true;
            }
            catch (Exception e)
            {
                Log.Warn("could not watch the block mapper, so retuning a block may not " +
                         "reach an autosave: " + e.Message);
            }
        }

        private void Unhook()
        {
            if (!_hooked)
            {
                return;
            }
            try
            {
                BlockMapper.onMapperOpen -= OnMapperOpen;
                BlockMapper.onMapperClose -= OnMapperClose;
            }
            catch (Exception)
            {
                // Nothing useful to do about it while the game is being torn down.
            }
            _hooked = false;
        }

        private void OnMapperOpen()
        {
            _holder = null;
            _before = string.Empty;
            _open = false;

            try
            {
                BlockMapper mapper = BlockMapper.CurrentInstance;
                if (mapper == null)
                {
                    return;
                }
                _holder = mapper.Current;
                if (_holder == null)
                {
                    return;
                }
                _before = Fingerprint(_holder);
                _open = true;
            }
            catch (Exception e)
            {
                Log.Warn("could not read the block's settings as the mapper opened: " +
                         e.Message);
            }
        }

        private void OnMapperClose()
        {
            if (!_open)
            {
                return;
            }
            _open = false;

            SaveableDataHolder holder = _holder;
            _holder = null;
            if (holder == null)
            {
                return;
            }

            try
            {
                if (Fingerprint(holder) == _before)
                {
                    return;
                }
            }
            catch (Exception e)
            {
                Log.Warn("could not read the block's settings as the mapper closed: " +
                         e.Message);
                return;
            }

            MarkModified();
        }

        /// <summary>
        /// Everything the mapper can change about a block, as one comparable string.
        ///
        /// Off the mapper's own <c>MapperType</c> list rather than the block's saved
        /// data: that list is what the widgets write to, so it is current the instant
        /// a key is rebound. Serialised through the same
        /// <see cref="VersionScan.Describe"/> the diff uses, so a slider dragged and
        /// put back is not a change here either.
        /// </summary>
        private static string Fingerprint(SaveableDataHolder holder)
        {
            List<MapperType> types = holder.MapperTypes;
            if (types == null)
            {
                return string.Empty;
            }

            List<string> entries = new List<string>();
            for (int i = 0; i < types.Count; i++)
            {
                MapperType type = types[i];
                if (type == null)
                {
                    continue;
                }
                entries.Add(VersionScan.Describe(type.Serialize()));
            }
            return BlockRecord.FlattenSettings(entries);
        }

        /// <summary>
        /// Tells the game the machine was modified, by raising Besiege's own
        /// <c>onMachineModified</c> rather than setting the autosave flag directly --
        /// so the centre of mass, the block counter and everything else listening
        /// hear the same news the autosave does.
        /// </summary>
        private static void MarkModified()
        {
            try
            {
                Action<Machine> modified = ReferenceMaster.onMachineModified;
                if (modified != null)
                {
                    modified(Machine.Active());
                }
            }
            catch (Exception e)
            {
                Log.Warn("could not tell the game that retuning a block changed the " +
                         "machine: " + e.Message);
            }
        }
    }
}
