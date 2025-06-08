using System.Drawing;

namespace MHWildsPathfindingBot.Core.Interfaces
{
    /// <summary>
    /// Interface for logging messages
    /// </summary>
    public interface ILogger
    {
        void LogMessage(string message, Color? color = null);
    }
}