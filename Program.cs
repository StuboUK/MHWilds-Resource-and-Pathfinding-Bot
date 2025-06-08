using System;
using System.Windows.Forms;
using MHWildsPathfindingBot.UI;

namespace MHWildsPathfindingBot
{
    /// <summary>
    /// Program entry point
    /// </summary>
    static class Program
    {
        [STAThread]
        static void Main()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // Check if ViGEm is installed
                CheckDependencies();

                // Run the application
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while starting the application:\n\n{ex.Message}\n\n{ex.StackTrace}",
                                "Startup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void CheckDependencies()
        {
            try
            {
                // Create a temporary ViGEm client to check if the driver is installed
                var client = new Nefarius.ViGEm.Client.ViGEmClient();
                client.Dispose();
            }
            catch (Exception)
            {
                DialogResult result = MessageBox.Show(
                    "ViGEmBus driver is not installed. This driver is required for controller emulation.\n\n" +
                    "Would you like to download it now?",
                    "Missing Dependencies",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.Yes)
                {
                    System.Diagnostics.Process.Start("https://github.com/ViGEm/ViGEmBus/releases");
                }
            }
        }
    }
}