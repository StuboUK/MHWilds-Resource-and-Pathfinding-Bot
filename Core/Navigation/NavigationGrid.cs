using System;
using System.Drawing;
using MHWildsPathfindingBot.Core.Interfaces;
using MHWildsPathfindingBot.Core.Models;
using MHWildsPathfindingBot.UI;

namespace MHWildsPathfindingBot.Navigation
{
    /// <summary>
    /// Manages the walkable and blacklisted grid data for navigation
    /// </summary>
    public class NavigationGrid
    {
        private readonly ILogger logger;
        private readonly IFileManager fileManager;

        // Grid data
        private bool[,] walkableGrid;
        private bool[,] blacklistGrid;

        // Grid parameters
        public int GridSizeX { get; private set; }
        public int GridSizeZ { get; private set; }
        public float OriginX { get; private set; }
        public float OriginZ { get; private set; }
        public float CellSize { get; }
        public int WalkabilityRadius { get; }

        // File access synchronization
        private readonly object gridFileLock = new object();

        // Optimization for position tracking
        private DateTime lastMarkTime = DateTime.MinValue;
        private Vector2 lastMarkedPosition = new Vector2(0, 0);
        private const float MinMarkDistance = 5.0f;
        private const int MinMarkInterval = 100;

        // Visualizer reference
        private PathVisualizer attachedVisualizer = null;

        public NavigationGrid(ILogger logger, IFileManager fileManager, int gridSizeX, int gridSizeZ,
                            float originX, float originZ, float cellSize, int walkabilityRadius)
        {
            this.logger = logger;
            this.fileManager = fileManager;

            GridSizeX = 5000; // Down from 8000
            GridSizeZ = 5000; // Down from 8000
            OriginX = originX;
            OriginZ = originZ;
            CellSize = cellSize;
            WalkabilityRadius = walkabilityRadius;

            walkableGrid = new bool[GridSizeX, GridSizeZ];
            blacklistGrid = new bool[GridSizeX, GridSizeZ];

            InitializeGrids();
            logger.LogMessage($"Grid initialized: {GridSizeX}x{GridSizeZ}, Origin: ({OriginX}, {OriginZ})");
        }

        /// <summary>
        /// Updates the visualizer with the current grid state
        /// </summary>
        public void UpdateVisualizer(PathVisualizer visualizer)
        {
            if (visualizer != null)
            {
                visualizer.SetGrids(walkableGrid, blacklistGrid, GridSizeX, GridSizeZ);
                visualizer.SetGridInfo(OriginX, OriginZ, CellSize);
                visualizer.ShowWalkableGrid = true;
                visualizer.Invalidate();
                attachedVisualizer = visualizer;
            }
        }

        /// <summary>
        /// Marks a cell as walkable at specific coordinates
        /// </summary>
        public void MarkWalkableAreaAtCoord(int x, int z)
        {
            if (IsValidCoordinate(x, z))
                walkableGrid[x, z] = true;
        }

        /// <summary>
        /// Clears a walkable area at specific coordinates
        /// </summary>
        public void ClearWalkableAreaAtCoord(int x, int z)
        {
            if (IsValidCoordinate(x, z))
                walkableGrid[x, z] = false;
        }

        /// <summary>
        /// Loads existing grid data or initializes new empty grids
        /// </summary>
        private void InitializeGrids()
        {
            bool walkableLoaded = LoadWalkableGrid();
            bool blacklistLoaded = LoadBlacklistGrid();

            if (!walkableLoaded)
                logger.LogMessage("Created new empty walkable grid.");

            if (!blacklistLoaded)
                logger.LogMessage("Created new empty blacklist grid.");
        }

        /// <summary>
        /// Converts world coordinates to grid coordinates
        /// </summary>
        public (int x, int z) WorldToGrid(Vector2 worldPos)
        {
            return (
                (int)((worldPos.X - OriginX) / CellSize),
                (int)((worldPos.Z - OriginZ) / CellSize)
            );
        }

        /// <summary>
        /// Converts grid coordinates to world coordinates
        /// </summary>
        public Vector2 GridToWorld(int gridX, int gridZ)
        {
            return new Vector2(
                OriginX + gridX * CellSize,
                OriginZ + gridZ * CellSize
            );
        }

        /// <summary>
        /// Checks if grid coordinates are within valid bounds
        /// </summary>
        public bool IsValidCoordinate(int x, int z)
        {
            return x >= 0 && x < GridSizeX && z >= 0 && z < GridSizeZ;
        }

        /// <summary>
        /// Marks an area around the position as walkable
        /// </summary>
        public void MarkWalkableArea(Vector2 position)
        {
            float distanceFromLast = (position - lastMarkedPosition).Magnitude();
            TimeSpan timeSinceLast = DateTime.Now - lastMarkTime;

            if (distanceFromLast < MinMarkDistance && timeSinceLast.TotalMilliseconds < MinMarkInterval)
                return;

            (int gridX, int gridZ) = WorldToGrid(position);

            if (!IsValidCoordinate(gridX, gridZ))
            {
                // Add additional logging to help diagnose the issue
                logger.LogMessage($"Position outside grid bounds: {position} → ({gridX},{gridZ}) | Origin: ({OriginX},{OriginZ})");

                // Instead of returning, try to find a valid coordinate on the grid
                gridX = Math.Clamp(gridX, 0, GridSizeX - 1);
                gridZ = Math.Clamp(gridZ, 0, GridSizeZ - 1);

                // Log the clamped coordinates
                logger.LogMessage($"  -> Clamped to valid coordinates: ({gridX},{gridZ})");
            }

            // Increase walkability radius for better connectivity
            int walkRadius = Math.Max(2, WalkabilityRadius);  // Use a larger radius to create more connected paths

            for (int x = Math.Max(0, gridX - walkRadius); x <= Math.Min(GridSizeX - 1, gridX + walkRadius); x++)
            {
                for (int z = Math.Max(0, gridZ - walkRadius); z <= Math.Min(GridSizeZ - 1, gridZ + walkRadius); z++)
                {
                    int dx = x - gridX;
                    int dz = z - gridZ;

                    // Create a more connected walkable area with a softer falloff
                    float distance = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (distance <= walkRadius)
                        walkableGrid[x, z] = true;
                }
            }

            lastMarkedPosition = position;
            lastMarkTime = DateTime.Now;

            // Update visualizer more frequently for better feedback
            if (attachedVisualizer != null && DateTime.Now.Millisecond < 500)  // Increased from 200ms to 500ms
                UpdateVisualizer(attachedVisualizer);
        }

        /// <summary>
        /// Marks an area as blacklisted (to be avoided during pathfinding)
        /// </summary>
        public void BlacklistArea(Vector2 position, int radius, PathVisualizer visualizer = null)
        {
            radius = Math.Min(radius, 2);
            (int gridX, int gridZ) = WorldToGrid(position);

            if (!IsValidCoordinate(gridX, gridZ))
                return;

            for (int x = Math.Max(0, gridX - radius); x <= Math.Min(GridSizeX - 1, gridX + radius); x++)
            {
                for (int z = Math.Max(0, gridZ - radius); z <= Math.Min(GridSizeZ - 1, gridZ + radius); z++)
                {
                    float dx = x - gridX;
                    float dz = z - gridZ;
                    float distanceSquared = dx * dx + dz * dz;

                    if (distanceSquared <= radius * radius)
                    {
                        blacklistGrid[x, z] = true;

                        if (visualizer != null && distanceSquared <= (radius * 0.5f) * (radius * 0.5f))
                        {
                            Vector2 worldPos = GridToWorld(x, z);
                            visualizer.AddObstacle(worldPos, CellSize);
                        }
                    }
                }
            }

            SaveBlacklistGrid();

            if (visualizer != null)
                UpdateVisualizer(visualizer);
            else if (attachedVisualizer != null)
                UpdateVisualizer(attachedVisualizer);
        }

        /// <summary>
        /// Finds the nearest walkable (and not blacklisted) position to the given position
        /// </summary>
        public Vector2 FindNearestWalkable(Vector2 position)
        {
            (int gridX, int gridZ) = WorldToGrid(position);

            if (IsValidCoordinate(gridX, gridZ) && IsWalkable(gridX, gridZ) && !IsBlacklisted(gridX, gridZ))
                return position;

            int searchRadius = 1;
            int maxRadius = 20;

            while (searchRadius < maxRadius)
            {
                for (int dx = -searchRadius; dx <= searchRadius; dx++)
                {
                    for (int dz = -searchRadius; dz <= searchRadius; dz++)
                    {
                        if (Math.Abs(dx) == searchRadius || Math.Abs(dz) == searchRadius)
                        {
                            int checkX = gridX + dx;
                            int checkZ = gridZ + dz;

                            if (IsValidCoordinate(checkX, checkZ) &&
                                IsWalkable(checkX, checkZ) &&
                                !IsBlacklisted(checkX, checkZ))
                            {
                                return GridToWorld(checkX, checkZ);
                            }
                        }
                    }
                }
                searchRadius++;
            }

            return position;
        }

        /// <summary>
        /// Checks if a grid position is walkable
        /// </summary>
        public bool IsWalkable(int x, int z)
        {
            return IsValidCoordinate(x, z) && walkableGrid[x, z];
        }

        /// <summary>
        /// Checks if a grid position is blacklisted
        /// </summary>
        public bool IsBlacklisted(int x, int z)
        {
            return IsValidCoordinate(x, z) && blacklistGrid[x, z];
        }

        /// <summary>
        /// Clears all navigation data (both walkable and blacklisted areas)
        /// </summary>
        public void ClearAllData(PathVisualizer visualizer = null)
        {
            logger.LogMessage("Clearing all navigation data...");

            GC.Collect();
            GC.WaitForPendingFinalizers();

            for (int x = 0; x < GridSizeX; x++)
                for (int z = 0; z < GridSizeZ; z++)
                {
                    walkableGrid[x, z] = false;
                    blacklistGrid[x, z] = false;
                }

            SaveWalkableGrid();
            SaveBlacklistGrid();

            if (visualizer != null)
            {
                UpdateVisualizer(visualizer);
                visualizer.ClearObstacles();
            }
            else if (attachedVisualizer != null)
            {
                UpdateVisualizer(attachedVisualizer);
                attachedVisualizer.ClearObstacles();
            }

            logger.LogMessage("Navigation data cleared. Ready to rebuild navmesh.");
        }

        /// <summary>
        /// Loads the walkable grid from file
        /// </summary>
        private bool LoadWalkableGrid()
        {
            lock (gridFileLock)
            {
                try
                {
                    if (!fileManager.WalkableGridExists())
                        return false;

                    var (grid, success, loadedOriginX, loadedOriginZ, loadedSizeX, loadedSizeZ) =
                        fileManager.LoadWalkableGrid(GridSizeX, GridSizeZ, OriginX, OriginZ, CellSize);

                    if (success)
                    {
                        if (loadedSizeX == GridSizeX && loadedSizeZ == GridSizeZ)
                        {
                            walkableGrid = grid;
                            OriginX = loadedOriginX;
                            OriginZ = loadedOriginZ;

                            logger.LogMessage($"Loaded walkable grid: {GridSizeX}x{GridSizeZ}, Origin: ({OriginX},{OriginZ})");
                            logger.LogMessage($"Walkable nodes: {CountWalkableNodes()}");

                            return true;
                        }
                        else
                        {
                            logger.LogMessage($"Grid dimensions mismatch: {loadedSizeX}x{loadedSizeZ} vs {GridSizeX}x{GridSizeZ}", Color.Yellow);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogMessage($"Error loading walkable grid: {ex.Message}", Color.Red);
                }
            }

            return false;
        }

        /// <summary>
        /// Loads the blacklist grid from file
        /// </summary>
        private bool LoadBlacklistGrid()
        {
            lock (gridFileLock)
            {
                try
                {
                    if (!fileManager.BlacklistGridExists())
                        return false;

                    var (grid, success, loadedOriginX, loadedOriginZ, loadedSizeX, loadedSizeZ) =
                        fileManager.LoadBlacklistGrid(GridSizeX, GridSizeZ, OriginX, OriginZ, CellSize);

                    if (success)
                    {
                        if (loadedSizeX == GridSizeX && loadedSizeZ == GridSizeZ)
                        {
                            blacklistGrid = grid;
                            logger.LogMessage("Loaded existing blacklist grid.");
                            logger.LogMessage($"Blacklisted nodes: {CountBlacklistedNodes()}");
                            return true;
                        }
                        else
                        {
                            logger.LogMessage($"Blacklist dimensions mismatch: {loadedSizeX}x{loadedSizeZ} vs {GridSizeX}x{GridSizeZ}", Color.Yellow);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogMessage($"Error loading blacklist grid: {ex.Message}", Color.Red);
                }
            }

            return false;
        }

        /// <summary>
        /// Saves the walkable grid to file
        /// </summary>
        public void SaveWalkableGrid()
        {
            lock (gridFileLock)
            {
                fileManager.SaveWalkableGrid(walkableGrid, GridSizeX, GridSizeZ, OriginX, OriginZ, CellSize);
                logger.LogMessage($"Walkable grid saved: {GridSizeX}x{GridSizeZ}, Origin: ({OriginX},{OriginZ})");
            }
        }

        /// <summary>
        /// Saves the blacklist grid to file
        /// </summary>
        public void SaveBlacklistGrid()
        {
            lock (gridFileLock)
            {
                fileManager.SaveBlacklistGrid(blacklistGrid, GridSizeX, GridSizeZ, OriginX, OriginZ, CellSize);
                logger.LogMessage("Blacklist grid saved.");
            }
        }

        /// <summary>
        /// Counts the number of walkable nodes in the grid
        /// </summary>
        public int CountWalkableNodes()
        {
            int count = 0;
            for (int x = 0; x < GridSizeX; x++)
                for (int z = 0; z < GridSizeZ; z++)
                    if (walkableGrid[x, z])
                        count++;
            return count;
        }

        /// <summary>
        /// Counts the number of blacklisted nodes in the grid
        /// </summary>
        public int CountBlacklistedNodes()
        {
            int count = 0;
            for (int x = 0; x < GridSizeX; x++)
                for (int z = 0; z < GridSizeZ; z++)
                    if (blacklistGrid[x, z])
                        count++;
            return count;
        }

        /// <summary>
        /// Gets a reference to the walkable grid for visualization purposes
        /// </summary>
        public bool[,] GetWalkableGrid() => walkableGrid;

        /// <summary>
        /// Gets a reference to the blacklist grid for visualization purposes
        /// </summary>
        public bool[,] GetBlacklistGrid() => blacklistGrid;

        /// <summary>
        /// Ensures a path exists between two points by creating a direct walkable line
        /// </summary>
        public void EnsurePathExists(Vector2 start, Vector2 end)
        {
            (int startX, int startZ) = WorldToGrid(start);
            (int endX, int endZ) = WorldToGrid(end);

            int dx = endX - startX;
            int dz = endZ - startZ;
            int steps = Math.Max(Math.Abs(dx), Math.Abs(dz));

            for (int i = 0; i <= steps; i++)
            {
                int x = startX + (int)(dx * (i / (float)steps));
                int z = startZ + (int)(dz * (i / (float)steps));

                if (IsValidCoordinate(x, z))
                {
                    for (int nx = -1; nx <= 1; nx++)
                    {
                        for (int nz = -1; nz <= 1; nz++)
                        {
                            if (IsValidCoordinate(x + nx, z + nz))
                            {
                                walkableGrid[x + nx, z + nz] = true;
                                blacklistGrid[x + nx, z + nz] = false;
                            }
                        }
                    }
                }
            }

            logger.LogMessage("Created direct walkable path between points");
            SaveWalkableGrid();

            if (attachedVisualizer != null)
                UpdateVisualizer(attachedVisualizer);
        }

        /// <summary>
        /// Optimizes the existing grid by removing redundant points
        /// </summary>
        public void OptimizeGrid()
        {
            logger.LogMessage("Optimizing walkable grid...");
            int pointsBefore = CountWalkableNodes();

            if (pointsBefore < 20)
            {
                logger.LogMessage($"Too few points to optimize ({pointsBefore}).");
                return;
            }

            bool[,] tempGrid = new bool[GridSizeX, GridSizeZ];
            for (int x = 0; x < GridSizeX; x++)
                for (int z = 0; z < GridSizeZ; z++)
                    tempGrid[x, z] = walkableGrid[x, z];

            int pointsRemoved = 0;

            for (int x = 2; x < GridSizeX - 2; x++)
            {
                for (int z = 2; z < GridSizeZ - 2; z++)
                {
                    if (!walkableGrid[x, z])
                        continue;

                    int walkableNeighbors = 0;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (dx == 0 && dz == 0) continue;

                            if (IsValidCoordinate(x + dx, z + dz) && walkableGrid[x + dx, z + dz])
                                walkableNeighbors++;
                        }
                    }

                    if (walkableNeighbors >= 5 && (x % 8 == 0 && z % 8 == 0))
                    {
                        tempGrid[x, z] = false;
                        pointsRemoved++;
                    }
                }
            }

            if (pointsRemoved < pointsBefore / 2)
            {
                walkableGrid = tempGrid;
                logger.LogMessage($"Applied optimization, removed {pointsRemoved} points");
            }
            else
            {
                logger.LogMessage($"Skipped optimization - would remove too many points ({pointsRemoved} of {pointsBefore})");
            }

            int pointsAfter = CountWalkableNodes();
            logger.LogMessage($"Grid optimization complete. Points: {pointsBefore} → {pointsAfter}");

            if (attachedVisualizer != null)
                UpdateVisualizer(attachedVisualizer);
        }
    }
}