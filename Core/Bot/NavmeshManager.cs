using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using MHWildsPathfindingBot.Core.Interfaces;
using MHWildsPathfindingBot.Core.Models;
using MHWildsPathfindingBot.Navigation;
using MHWildsPathfindingBot.UI;

namespace MHWildsPathfindingBot.Bot
{
    /// <summary>
    /// Handles navmesh generation mode operations
    /// </summary>
    public class NavmeshManager
    {
        private readonly ILogger logger;
        private readonly IFileManager fileManager;
        private readonly NavigationGrid navigationGrid;
        private readonly PathVisualizer pathVisualizer;
        private readonly string playerPositionFilePath;

        private bool isRunning = false;
        private bool isPaused = false;
        private CancellationTokenSource cts;
        private Task navmeshTask;

        // Performance tracking
        private int pointsMarked = 0;
        private int lastPointCount = 0;
        private Stopwatch performanceTimer = new Stopwatch();
        private Vector2 lastPlayerPosition = new Vector2(0, 0);

        // Optimization settings
        private const int OptimizationInterval = 120000; // 2 minutes in milliseconds
        private const int VisualizationUpdateInterval = 300; // 300ms for visualization updates
        private const float MinPositionChangeForUpdate = 1.0f; // Minimum movement before updating grid

        // Events
        public event EventHandler<int> PointCountUpdated;

        public NavmeshManager(
            ILogger logger,
            IFileManager fileManager,
            NavigationGrid navigationGrid,
            PathVisualizer pathVisualizer,
            string playerPositionFilePath)
        {
            this.logger = logger;
            this.fileManager = fileManager;
            this.navigationGrid = navigationGrid;
            this.pathVisualizer = pathVisualizer;
            this.playerPositionFilePath = playerPositionFilePath;
        }

        /// <summary>
        /// Start navmesh generation mode
        /// </summary>
        public void Start()
        {
            if (isRunning)
                return;

            logger.LogMessage("Entering Navmesh Mode...");
            logger.LogMessage("Move your character to update the walkable grid.");
            logger.LogMessage($"Using cell size: {Globals.CellSize}");

            // Ensure grid visualization is enabled and properly initialized
            if (pathVisualizer != null)
            {
                pathVisualizer.ShowWalkableGrid = true;

                // Update the visualizer with current grid info
                pathVisualizer.SetGrids(
                    navigationGrid.GetWalkableGrid(),
                    navigationGrid.GetBlacklistGrid(),
                    navigationGrid.GridSizeX,
                    navigationGrid.GridSizeZ
                );

                // Set coordinate system info
                pathVisualizer.SetGridInfo(
                    navigationGrid.OriginX,
                    navigationGrid.OriginZ,
                    navigationGrid.CellSize
                );

                // Force a repaint
                pathVisualizer.Invalidate();

                // Attach visualizer to grid for updates
                navigationGrid.UpdateVisualizer(pathVisualizer);
            }

            isRunning = true;
            isPaused = false;
            performanceTimer.Reset();
            performanceTimer.Start();
            pointsMarked = 0;

            lastPointCount = navigationGrid.CountWalkableNodes();

            cts = new CancellationTokenSource();
            navmeshTask = Task.Run(() => RunNavmeshGeneration(), cts.Token);
        }

        /// <summary>
        /// Stop navmesh generation mode
        /// </summary>
        public void Stop()
        {
            if (!isRunning)
                return;

            logger.LogMessage("Exiting Navmesh Mode...");

            isRunning = false;

            if (cts != null)
            {
                cts.Cancel();
                try
                {
                    navmeshTask.Wait(1000); // Wait 1 second for task to complete
                }
                catch { /* Ignore any exceptions during cancellation */ }

                cts.Dispose();
                cts = null;
            }

            // Optimize and save when stopping
            OptimizeGrid();
            navigationGrid.SaveWalkableGrid();

            performanceTimer.Stop();
            int totalPoints = navigationGrid.CountWalkableNodes();
            logger.LogMessage($"Navmesh generation complete. Total walkable points: {totalPoints}");
            logger.LogMessage($"Points per second: {pointsMarked / (performanceTimer.ElapsedMilliseconds / 1000.0):F1}");
        }

        /// <summary>
        /// Pause or resume navmesh generation
        /// </summary>
        public void TogglePause()
        {
            isPaused = !isPaused;
            logger.LogMessage(isPaused ? "Navmesh generation paused" : "Navmesh generation resumed");
        }

        /// <summary>
        /// Clear all navmesh data
        /// </summary>
        public void ClearData()
        {
            navigationGrid.ClearAllData(pathVisualizer);
        }

        /// <summary>
        /// Manually trigger grid optimization
        /// </summary>
        public void OptimizeGrid()
        {
            logger.LogMessage("Optimizing grid...");

            // Skip any resizing operations - just update visualization
            if (pathVisualizer != null)
            {
                pathVisualizer.SetGrids(
                    navigationGrid.GetWalkableGrid(),
                    navigationGrid.GetBlacklistGrid(),
                    navigationGrid.GridSizeX,
                    navigationGrid.GridSizeZ
                );
                pathVisualizer.ShowWalkableGrid = true;
                pathVisualizer.Invalidate();
            }

            int totalPoints = navigationGrid.CountWalkableNodes();
            PointCountUpdated?.Invoke(this, totalPoints);
            logger.LogMessage($"Grid optimization complete. Total walkable points: {totalPoints}");
        }

        /// <summary>
        /// Run the navmesh generation loop
        /// </summary>
        private void RunNavmeshGeneration()
        {
            DateTime lastSaveTime = DateTime.MinValue;
            DateTime lastOptimizationTime = DateTime.MinValue;
            DateTime lastVisualizationUpdate = DateTime.MinValue;
            DateTime lastStatusUpdate = DateTime.MinValue;

            float playerX = 0, playerY = 0, playerZ = 0;
            int updateCount = 0;

            while (isRunning && !cts.IsCancellationRequested)
            {
                if (isPaused)
                {
                    Thread.Sleep(100);
                    continue;
                }

                try
                {
                    string fileContents = fileManager.ReadPositionFile(playerPositionFilePath);
                    string[] values = fileContents.Split(',');

                    if (values.Length == 3 &&
                        float.TryParse(values[0], out playerX) &&
                        float.TryParse(values[1], out playerY) &&
                        float.TryParse(values[2], out playerZ))
                    {
                        Vector2 position = new Vector2(playerX, playerZ);
                        float distanceMoved = (position - lastPlayerPosition).Magnitude();

                        // Only mark grid if we've moved enough to justify an update
                        if (distanceMoved >= MinPositionChangeForUpdate)
                        {
                            navigationGrid.MarkWalkableArea(position);
                            lastPlayerPosition = position;
                            pointsMarked++;
                            updateCount++;
                        }

                        // Update visualization less frequently for better performance
                        if ((DateTime.Now - lastVisualizationUpdate).TotalMilliseconds > VisualizationUpdateInterval)
                        {
                            pathVisualizer.UpdatePlayerPosition(position);

                            if (updateCount > 0)
                            {
                                // Update with latest grid data
                                pathVisualizer.SetGrids(
                                    navigationGrid.GetWalkableGrid(),
                                    navigationGrid.GetBlacklistGrid(),
                                    navigationGrid.GridSizeX,
                                    navigationGrid.GridSizeZ
                                );

                                // Ensure grid is visible
                                pathVisualizer.ShowWalkableGrid = true;

                                // Force a repaint
                                pathVisualizer.Invalidate();

                                updateCount = 0;
                            }

                            lastVisualizationUpdate = DateTime.Now;
                        }

                        // Periodically save grid
                        if ((DateTime.Now - lastSaveTime).TotalMilliseconds > Globals.NavmeshSaveInterval)
                        {
                            navigationGrid.SaveWalkableGrid();
                            lastSaveTime = DateTime.Now;

                            // Reduce memory usage
                            if (GC.GetTotalMemory(false) > 100_000_000) // 100MB
                            {
                                GC.Collect();
                            }
                        }

                        // Periodically optimize grid
                        if ((DateTime.Now - lastOptimizationTime).TotalMilliseconds > OptimizationInterval)
                        {
                            OptimizeGrid();
                            lastOptimizationTime = DateTime.Now;
                        }


                        // Update status periodically
                        if ((DateTime.Now - lastStatusUpdate).TotalSeconds > 10)
                        {
                            int currentPointCount = navigationGrid.CountWalkableNodes();
                            if (currentPointCount != lastPointCount)
                            {
                                PointCountUpdated?.Invoke(this, currentPointCount);
                                lastPointCount = currentPointCount;
                            }
                            lastStatusUpdate = DateTime.Now;
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogMessage($"Error in Navmesh: {ex.Message}", Color.Red);
                    // Sleep longer on error to avoid spamming
                    Thread.Sleep(500);
                }

                // Dynamic sleep based on movement
                Thread.Sleep(50);
            }
        }


        /// <summary>
        /// Get current statistics for navmesh generation
        /// </summary>
        public (int totalPoints, int pointsPerSecond) GetStatistics()
        {
            int totalPoints = navigationGrid.CountWalkableNodes();
            double seconds = performanceTimer.ElapsedMilliseconds / 1000.0;
            int pointsPerSecond = seconds > 0 ? (int)(pointsMarked / seconds) : 0;

            return (totalPoints, pointsPerSecond);
        }
    }
}