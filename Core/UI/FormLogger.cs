using System;
using System.Drawing;
using System.Windows.Forms;
using MHWildsPathfindingBot.Core.Interfaces;

namespace MHWildsPathfindingBot.UI
{
    /// <summary>
    /// Implements the logger interface for UI logging
    /// </summary>
    public class FormLogger : ILogger
    {
        private readonly RichTextBox logBox;

        public FormLogger(RichTextBox logBox)
        {
            this.logBox = logBox;
        }

        public void LogMessage(string message, Color? color = null)
        {
            if (logBox.InvokeRequired)
            {
                logBox.Invoke(new Action<string, Color?>((msg, clr) => LogMessage(msg, clr)), message, color);
                return;
            }

            Color textColor = color ?? Color.LightGreen;
            logBox.SelectionStart = logBox.TextLength;
            logBox.SelectionLength = 0;
            logBox.SelectionColor = textColor;
            logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
            logBox.ScrollToCaret();
        }
    }
}