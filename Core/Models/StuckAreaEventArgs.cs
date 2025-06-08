using System;

namespace MHWildsPathfindingBot.Core.Models
{
    /// <summary>
    /// Event args for stuck area detection
    /// </summary>
    public class StuckAreaEventArgs : EventArgs
    {
        public Vector2 Position { get; }
        public Vector2 Direction { get; }

        public StuckAreaEventArgs(Vector2 position, Vector2 direction)
        {
            Position = position;
            Direction = direction;
        }
    }
}