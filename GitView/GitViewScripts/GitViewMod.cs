using UnityEngine;

namespace GitView
{
    /// <summary>
    /// Mod entry point. The loader calls OnLoad once, on the main thread, when the
    /// mod is loaded -- which is when the player first enters a level, since Mod.xml
    /// does not use LoadInTitleScreen.
    ///
    /// Everything the mod does hangs off one object that outlives scene loads: the
    /// watcher that puts compare buttons in the load screen, and the window that
    /// shows a machine's history.
    /// </summary>
    public class GitViewMod : Modding.ModEntryPoint
    {
        private const string HostName = "GitView";
        private const string ToggleKey = "toggle-history";

        private static GameObject _host;
        private static HistoryView _history;
        private static Modding.ModKey _toggle;

        public override void OnLoad()
        {
            if (_host != null)
            {
                Log.Info("already running.");
                return;
            }

            _host = new GameObject(HostName);
            Object.DontDestroyOnLoad(_host);

            _history = _host.AddComponent<HistoryView>();
            BrowserWatch watch = _host.AddComponent<BrowserWatch>();
            watch.Bind(_history);
            _host.AddComponent<Hotkeys>();
            _host.AddComponent<MapperWatch>();

            try
            {
                _toggle = Modding.ModKeys.GetKey(ToggleKey);
            }
            catch (System.Exception e)
            {
                // A key declared in Mod.xml that the loader did not register. Worth
                // one line rather than taking the whole mod down with it.
                Log.Warn("could not bind the " + ToggleKey + " hotkey: " + e.Message);
            }

            Log.Info("loaded. The compare button on a machine in the load screen opens " +
                     "its history; Ctrl+Y hides and shows the window.");
        }

        /// <summary>The key that hides and shows the window, or null if unbound.</summary>
        public static Modding.ModKey ToggleWindow
        {
            get { return _toggle; }
        }

        public static HistoryView History
        {
            get { return _history; }
        }

        /// <summary>
        /// Nothing this mod does is stored in a machine or a level, so no save ever
        /// depends on it. Saying so lets the player open their creations normally
        /// when it is not installed.
        /// </summary>
        public override bool IsRequiredForMachine(Modding.Blocks.PlayerMachineInfo machine)
        {
            return false;
        }

        public override bool IsRequiredForLevel(Modding.Levels.Level level)
        {
            return false;
        }
    }

    /// <summary>Watches the one hotkey, which Besiege lets the player rebind.</summary>
    public class Hotkeys : MonoBehaviour
    {
        private void Update()
        {
            Modding.ModKey key = GitViewMod.ToggleWindow;
            if (key == null || !key.IsPressed)
            {
                return;
            }
            HistoryView history = GitViewMod.History;
            if (history != null)
            {
                history.Toggle();
            }
        }
    }
}
