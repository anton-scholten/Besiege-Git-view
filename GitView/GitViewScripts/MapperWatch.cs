using System;
using System.Collections.Generic;
using UnityEngine;

namespace GitView
{
    /// <summary>
    /// Makes Besiege's autosave notice that a block was retuned.
    ///
    /// Besiege only writes an autosave when it believes the machine has changed,
    /// and what it counts as a change is narrower than it sounds. The flag behind
    /// it is <c>MachineAutosaveController.MachineUpdatedSinceLastSave</c>, and the
    /// only things that set it are the seven places that raise
    /// <c>ReferenceMaster.onMachineModified</c>: placing a block, deleting one,
    /// finishing a drag, mirroring, and undoing. Remapping a key, moving a slider
    /// or flipping a toggle raises nothing, so the sixty-second timer comes round,
    /// finds the flag clear, and skips the save. The new setting is never written
    /// to a version at all.
    ///
    /// In ordinary play that is invisible: a tuning session nearly always nudges a
    /// block eventually, and the settings ride along with whatever save that
    /// triggers. It is very visible in this mod. Change a block's keys, wait for
    /// the autosave, open the history -- and there is no new version, and nothing
    /// is orange. The diff is right; the folder simply has nothing in it.
    ///
    /// So the block mapper is watched. Its settings are fingerprinted when it
    /// opens, compared when it closes, and if they differ the game is told the
    /// machine was modified in exactly the way it is told when a block is dragged.
    /// Nothing is saved here and no file is touched: the next autosave does the
    /// work, on its own schedule, and would have done it already if Besiege
    /// counted this as a change.
    /// </summary>
    public class MapperWatch : MonoBehaviour
    {
        /// <summary>
        /// The holder the mapper was opened on. Kept rather than asked for again on
        /// close, because <c>BlockMapper.Current</c> is cleared as part of closing
        /// and the two callbacks have to be comparing the same block.
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
        /// Subscribes to the mapper's own open and close callbacks.
        ///
        /// These are plain static delegates on <c>BlockMapper</c> rather than
        /// events, so they survive scene loads and have to be unsubscribed by hand.
        /// This component lives on the mod's DontDestroyOnLoad host, so in practice
        /// that is once, when the game shuts down.
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
        /// Everything the mapper can change about a block, as one comparable
        /// string.
        ///
        /// Built from the mapper's own <c>MapperType</c> list rather than from the
        /// block's saved data, because that list is what the widgets write to and
        /// it is up to date the instant a key is rebound -- there is no save to
        /// wait for. Each entry is serialised through the same
        /// <see cref="VersionScan.Describe"/> the diff uses, so a value that the
        /// diff would round away cannot look like a change here either. A slider
        /// dragged and put back is not a change.
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
        /// Tells the game the machine was modified.
        ///
        /// Raises Besiege's own <c>onMachineModified</c> rather than reaching into
        /// the autosave controller's flag directly. It is the same call the game
        /// makes when a block finishes being dragged, so everything else listening
        /// -- the centre of mass, the aerodynamics display, the block counter --
        /// gets the same news it would have got, instead of the autosave being told
        /// something the rest of the game has not heard.
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
