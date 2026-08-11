using UnityEngine;

namespace GitView
{
    /// <summary>
    /// Prefixed logging. Messages show up in Player.log, and in the in-game console
    /// once you enable them there with: show_logs true
    /// </summary>
    public static class Log
    {
        private const string Prefix = "[GitView] ";

        public static void Info(string message)
        {
            Debug.Log(Prefix + message);
        }

        public static void Warn(string message)
        {
            Debug.LogWarning(Prefix + message);
        }

        public static void Error(string message)
        {
            Debug.LogError(Prefix + message);
        }

        /// <summary>Writes straight to the in-game console, for user-facing notices.</summary>
        public static void Console(string message)
        {
            try
            {
                Modding.ModConsole.Log(Prefix + message);
            }
            catch
            {
                Debug.Log(Prefix + message);
            }
        }
    }
}
