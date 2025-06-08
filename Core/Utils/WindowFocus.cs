using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading;

namespace MHWildsPathfindingBot.Core.Utils
{
    public static class WindowFocus
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        /// <summary>
        /// Focuses the Monster Hunter Wilds window
        /// </summary>
        /// <returns>True if window was found and focused</returns>
        public static bool FocusGameWindow()
        {
            // Try by window title (multiple possible titles)
            string[] possibleTitles = {
                "Monster Hunter Wilds",
                "MONSTER HUNTER WILDS",
                "MHWilds"
            };

            foreach (string title in possibleTitles)
            {
                IntPtr hWnd = FindWindow(null, title);
                if (hWnd != IntPtr.Zero)
                {
                    return SetForegroundWindow(hWnd);
                }
            }

            // Alternative approach - try to find by process name
            try
            {
                Process[] processes = Process.GetProcessesByName("MonsterHunterWilds");
                if (processes.Length > 0)
                {
                    return SetForegroundWindow(processes[0].MainWindowHandle);
                }

                // Try alternative process names
                processes = Process.GetProcessesByName("MHWilds");
                if (processes.Length > 0)
                {
                    return SetForegroundWindow(processes[0].MainWindowHandle);
                }
            }
            catch (Exception)
            {
                // Ignore errors in process access
            }

            return false;
        }
    }
}
