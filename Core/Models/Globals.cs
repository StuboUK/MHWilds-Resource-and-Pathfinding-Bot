namespace MHWildsPathfindingBot.Core.Models
{
    /// <summary>
    /// Global constants accessible to all classes
    /// </summary>
    public static class Globals
    {
        public const float CellSize = 1f;

        public const float OriginX = -3276.6f;  // Keep this the same
        public const float OriginZ = -1683.9f;    // Change this to accommodate high positive Z values
        public const int WalkabilityRadius = 1; // Increase from 4 to create more connected walkable areas

        public const float WallClearance = 3.0f; // Distance to keep from walls/obstacles

        // File paths
        public static string WalkableGridFilePath = "walkable_grid.bin";
        public static string BlacklistGridFilePath = "blacklist_grid.bin";

        // Movement and detection constants
        public const int UpdateRate = 30; // milliseconds
        public const int PathfindingInterval = 350; // milliseconds
        public const float DistanceThreshold = 2.0f; // How close to target to consider reached
        public const int NavmeshSaveInterval = 5000; // milliseconds

        // Movement progress tracking constants
        public const int ProgressCheckInterval = 6000; // ms
        public const float MinProgressDistance = 0.3f;
        public const int MaxNoProgressCount = 5;

        // Input key constants
        public const ushort VK_W = 0x57;
        public const ushort VK_A = 0x41;
        public const ushort VK_S = 0x53;
        public const ushort VK_D = 0x44;
    }
}