using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using MHWildsPathfindingBot.Core.Interfaces;
using MHWildsPathfindingBot.Core.Models;
using MHWildsPathfindingBot.Core.Utils;
using MHWildsPathfindingBot.Navigation;
using MHWildsPathfindingBot.Navigation.Pathfinding;
using MHWildsPathfindingBot.UI;

namespace MHWildsPathfindingBot.Bot
{
    public class Bot
    {
        // Windows API methods for window focus
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        // Core components
        private readonly ILogger logger;
        private readonly IFileManager fileManager;
        private readonly NavigationGrid navigationGrid;
        private readonly Pathfinder pathfinder;
        private readonly MovementController movementController;
        protected StuckDetector stuckDetector;
        private readonly PathVisualizer pathVisualizer;
        private readonly string playerPositionFilePath;

        // State tracking
        private bool isRunning = false;
        private bool targetReached = false;
        private bool paused = false;
        private bool simulationMode = false;
        private CancellationTokenSource botCts;
        private Task positionMonitorTask;
        private Task controlTask;
        private Task stuckDetectionTask;

        // Position tracking
        private float playerX = 0, playerY = 0, playerZ = 0;
        private float targetX, targetZ;
        private Vector2 lastRecalcPosition = new Vector2(0, 0);
        private DateTime lastPathfindingTime = DateTime.MinValue;
        private int pathRecalcAtSamePositionCounter = 0;

        // Path data
        private List<Vector2> currentPath = null;

        // Movement tracking
        private Vector2 lastProgressCheckPosition;
        private Vector2 lastDesiredDirection;
        private DateTime lastProgressCheckTime;
        private DateTime botStartTime;

        // Off-path detection
        private bool isOffPath = false;
        private Vector2 closestPathPoint = new Vector2(0, 0);
        private float offPathTimeout = 0;
        private const float MaxOffPathTime = 5.0f;

        // Events
        public event EventHandler<EventArgs> TargetReached;
        public event EventHandler<EventArgs> BotStopped;
        public event EventHandler<Vector2> PlayerPositionUpdated;

        public Bot(ILogger logger, IFileManager fileManager, NavigationGrid navigationGrid,
                  PathVisualizer pathVisualizer, string playerPositionFilePath)
        {
            this.logger = logger;
            this.fileManager = fileManager;
            this.navigationGrid = navigationGrid;
            this.pathVisualizer = pathVisualizer;
            this.playerPositionFilePath = playerPositionFilePath;

            this.pathfinder = new Pathfinder(logger, navigationGrid);
            this.movementController = new MovementController(logger);
            this.stuckDetector = new StuckDetector(logger);

            stuckDetector.StuckAreaDetected += OnStuckAreaDetected;
        }

        public MovementController GetMovementController() => movementController;

        public void SetSimulationMode(bool enabled)
        {
            simulationMode = enabled;
            logger.LogMessage($"Simulation mode {(enabled ? "enabled" : "disabled")}");
        }

        public bool Start(float targetX, float targetZ)
        {
            if (isRunning) return false;

            this.targetX = targetX;
            this.targetZ = targetZ;

            logger.LogMessage($"Starting to target: X={targetX}, Z={targetZ}");

            targetReached = false;
            paused = false;
            lastRecalcPosition = new Vector2(0, 0);
            pathRecalcAtSamePositionCounter = 0;
            movementController.Reset();
            botCts = new CancellationTokenSource();

            if (!GetInitialPosition())
            {
                logger.LogMessage("Could not read initial position.", Color.Red);
                return false;
            }

            // Immediately update visualizer with player position for centering
            if (pathVisualizer != null)
            {
                Vector2 playerPos = new Vector2(playerX, playerZ);
                pathVisualizer.UpdatePlayerPosition(playerPos);
                PlayerPositionUpdated?.Invoke(this, playerPos);
            }

            botStartTime = DateTime.Now;
            lastProgressCheckPosition = new Vector2(playerX, playerZ);
            lastProgressCheckTime = DateTime.Now;
            lastDesiredDirection = new Vector2(0, 0);
            RecalculatePath();

            isRunning = true;
            positionMonitorTask = Task.Run(() => MonitorPlayerPosition(), botCts.Token);
            controlTask = Task.Run(() => ControlLoop(), botCts.Token);

            if (!simulationMode)
            {
                stuckDetectionTask = Task.Run(() => stuckDetector.Run(
                    () => new Vector2(playerX, playerZ),
                    () => isRunning && !targetReached && !paused,
                    botCts.Token));
            }

            if (currentPath != null)
            {
                UpdateVisualizerPath();
            }

            return true;
        }

        public void Stop()
        {
            if (!isRunning) return;

            isRunning = false;

            if (botCts != null)
            {
                botCts.Cancel();
                try
                {
                    if (positionMonitorTask != null && controlTask != null)
                    {
                        var tasksToWait = stuckDetectionTask != null
                            ? new[] { positionMonitorTask, controlTask, stuckDetectionTask }
                            : new[] { positionMonitorTask, controlTask };

                        Task.WaitAll(tasksToWait, 2000);
                    }
                }
                catch (AggregateException) { }
                botCts.Dispose();
                botCts = null;
            }

            movementController.StopMovement();
            InputSimulator.ResetControllerState();

            navigationGrid.SaveWalkableGrid();
            navigationGrid.SaveBlacklistGrid();

            targetReached = false;
            paused = false;
            currentPath = null;
            pathRecalcAtSamePositionCounter = 0;

            BotStopped?.Invoke(this, EventArgs.Empty);
        }

        public void SetPaused(bool paused)
        {
            this.paused = paused;
            if (paused)
            {
                movementController.StopMovement();
            }
        }

        public void ForceRecalculatePath()
        {
            RecalculatePath();
        }

        public Vector2 GetPlayerPosition() => new Vector2(playerX, playerZ);

        public Vector2 GetTargetPosition() => new Vector2(targetX, targetZ);

        public bool IsRunning() => isRunning && !targetReached && !paused;

        public void StartNavigation(float targetX, float targetZ)
        {
            FocusGameWindow();
            Thread.Sleep(300);
            Start(targetX, targetZ);
        }

        private bool GetInitialPosition()
        {
            DateTime startTime = DateTime.Now;
            TimeSpan timeout = TimeSpan.FromSeconds(15);

            while ((DateTime.Now - startTime) < timeout)
            {
                try
                {
                    string fileContents = fileManager.ReadPositionFile(playerPositionFilePath);
                    if (!string.IsNullOrWhiteSpace(fileContents))
                    {
                        string[] values = fileContents.Split(',');
                        if (values.Length >= 3 &&
                            float.TryParse(values[0], out float tempX) &&
                            float.TryParse(values[1], out float tempY) &&
                            float.TryParse(values[2], out float tempZ))
                        {
                            playerX = tempX; playerY = tempY; playerZ = tempZ;

                            if (!simulationMode)
                            {
                                movementController.PressForward(true);
                                Thread.Sleep(100);
                                movementController.PressForward(false);
                            }
                            return true;
                        }
                    }
                }
                catch { }
                Thread.Sleep(500);
            }
            return false;
        }

        private void MonitorPlayerPosition()
        {
            while (isRunning && !botCts.IsCancellationRequested)
            {
                try
                {
                    string fileContents = fileManager.ReadPositionFile(playerPositionFilePath);
                    string[] values = fileContents.Split(',');

                    if (values.Length >= 3 &&
                        float.TryParse(values[0], out float tempX) &&
                        float.TryParse(values[1], out float tempY) &&
                        float.TryParse(values[2], out float tempZ))
                    {
                        playerX = tempX; playerY = tempY; playerZ = tempZ;
                        Vector2 playerPos = new Vector2(playerX, playerZ);

                        // Update player position in visualizer (now will center view)
                        PlayerPositionUpdated?.Invoke(this, playerPos);

                        // Mark walkable area with larger radius for better visibility
                        navigationGrid.MarkWalkableArea(playerPos);

                        // Save grid more frequently for better visibility during operation
                        if (DateTime.Now.Second % 10 == 0) // Every 10 seconds instead of 30
                        {
                            navigationGrid.SaveWalkableGrid();
                            navigationGrid.SaveBlacklistGrid();
                        }

                        if (!simulationMode)
                        {
                            CheckMovementProgress();
                        }

                        float distanceToTarget = (float)Math.Sqrt(Math.Pow(playerX - targetX, 2) + Math.Pow(playerZ - targetZ, 2));

                        if (distanceToTarget < Globals.DistanceThreshold && !targetReached)
                        {
                            targetReached = true;
                            movementController.StopMovement();
                            TargetReached?.Invoke(this, EventArgs.Empty);
                        }
                    }
                }
                catch { }

                Thread.Sleep(Globals.UpdateRate);
            }
        }

        public void ContinueToTarget(float targetX, float targetZ)
        {
            if (!isRunning) return;

            this.targetX = targetX;
            this.targetZ = targetZ;

            logger.LogMessage($"Continuing to new target: X={targetX}, Z={targetZ}");

            // Reset target-specific state but keep running state
            targetReached = false;
            RecalculatePath();
        }
        private void ControlLoop()
        {
            while (isRunning && !botCts.IsCancellationRequested)
            {
                if (paused || targetReached)
                {
                    movementController.StopMovement();
                    Thread.Sleep(100);
                    continue;
                }

                if ((DateTime.Now - lastPathfindingTime).TotalMilliseconds > Globals.PathfindingInterval)
                    RecalculatePath();

                if (currentPath != null && currentPath.Count > 1)
                {
                    Vector2 targetDirection = FollowPath();

                    if (!simulationMode)
                    {
                        movementController.MoveInDirection(targetDirection, new Vector2(playerX, playerZ), currentPath);
                    }
                    Thread.Sleep(50);
                }
                else
                    Thread.Sleep(50);
            }
        }

        private Vector2 FollowPath()
        {
            if (currentPath == null || currentPath.Count <= 1)
                return new Vector2(0, 0);

            Vector2 currentPosition = new Vector2(playerX, playerZ);
            bool onPath = IsOnPath(currentPosition, out Vector2 closest, out int closestIdx);

            if (!onPath)
            {
                if (!isOffPath)
                {
                    isOffPath = true;
                    closestPathPoint = closest;
                    offPathTimeout = 0;
                }

                offPathTimeout += 0.1f;
                if (offPathTimeout > MaxOffPathTime)
                {
                    ForceRecalculatePath();
                    isOffPath = false;
                    return new Vector2(0, 0);
                }

                Vector2 returnVector = (closestPathPoint - currentPosition).Normalized();
                lastDesiredDirection = returnVector;
                return returnVector;
            }
            else
            {
                if (isOffPath)
                {
                    isOffPath = false;
                }

                float lookaheadDistance = 10.0f;
                int targetWaypointIndex = closestIdx > 0 ? closestIdx : 1;
                float distanceAlongPath = 0;

                for (int i = targetWaypointIndex; i < currentPath.Count; i++)
                {
                    if (i > targetWaypointIndex)
                        distanceAlongPath += (currentPath[i] - currentPath[i - 1]).Magnitude();

                    if (distanceAlongPath > lookaheadDistance)
                        break;

                    bool hasLineOfSight = CheckLineOfSight(currentPosition, currentPath[i]);
                    if (hasLineOfSight)
                        targetWaypointIndex = i;
                }

                Vector2 targetWaypoint = currentPath[targetWaypointIndex];
                Vector2 toTarget = targetWaypoint - currentPosition;
                float distanceToWaypoint = toTarget.Magnitude();
                Vector2 desiredDirection = toTarget.Normalized();
                lastDesiredDirection = desiredDirection;

                float waypointReachDistance = 3.0f;
                if (distanceToWaypoint < waypointReachDistance && currentPath.Count > 2)
                {
                    if (targetWaypointIndex == 1)
                    {
                        currentPath.RemoveAt(0);
                        UpdateVisualizerPath();
                    }
                }

                return desiredDirection;
            }
        }

        private bool IsOnPath(Vector2 position, out Vector2 closestPoint, out int closestIndex)
        {
            closestPoint = new Vector2(0, 0);
            closestIndex = 0;

            if (currentPath == null || currentPath.Count < 2)
                return false;

            float minDistance = float.MaxValue;

            for (int i = 0; i < currentPath.Count - 1; i++)
            {
                Vector2 a = currentPath[i];
                Vector2 b = currentPath[i + 1];

                Vector2 closestOnSegment = GetClosestPointOnSegment(position, a, b);
                float distance = (position - closestOnSegment).Magnitude();

                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestPoint = closestOnSegment;
                    closestIndex = i;
                }
            }

            float onPathThreshold = 5.0f;
            return minDistance <= onPathThreshold;
        }

        private Vector2 GetClosestPointOnSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ap = p - a;
            Vector2 ab = b - a;

            float magnitudeAB = ab.Magnitude();
            if (magnitudeAB < 0.00001f)
                return a;

            float t = Vector2.DotProduct(ap, ab) / (magnitudeAB * magnitudeAB);
            t = Math.Max(0, Math.Min(1, t));

            return new Vector2(a.X + ab.X * t, a.Z + ab.Z * t);
        }

        private bool CheckLineOfSight(Vector2 from, Vector2 to)
        {
            Vector2 direction = (to - from).Normalized();
            float distance = (to - from).Magnitude();
            int checkPoints = Math.Min((int)(distance / 3.0f), 5);

            for (int i = 1; i <= checkPoints; i++)
            {
                float checkDistance = distance * (i / (float)(checkPoints + 1));
                Vector2 checkPoint = new Vector2(
                    from.X + direction.X * checkDistance,
                    from.Z + direction.Z * checkDistance
                );

                (int gridX, int gridZ) = navigationGrid.WorldToGrid(checkPoint);

                if (!navigationGrid.IsValidCoordinate(gridX, gridZ) ||
                    !navigationGrid.IsWalkable(gridX, gridZ) ||
                    navigationGrid.IsBlacklisted(gridX, gridZ))
                {
                    return false;
                }
            }
            return true;
        }

        private void CheckMovementProgress()
        {
            if (lastDesiredDirection.Magnitude() < 0.001f || !movementController.HasStartedMoving())
                return;

            if ((DateTime.Now - lastProgressCheckTime).TotalMilliseconds >= Globals.ProgressCheckInterval)
            {
                Vector2 currentPosition = new Vector2(playerX, playerZ);
                Vector2 movementVector = currentPosition - lastProgressCheckPosition;
                float distance = movementVector.Magnitude();

                float directionDot = 0;
                if (distance > 0.001f)
                    directionDot = Vector2.DotProduct(movementVector.Normalized(), lastDesiredDirection);

                if (distance < Globals.MinProgressDistance || directionDot < 0.5f)
                {
                    stuckDetector.IncrementNoProgressCounter();
                }
                else
                {
                    stuckDetector.ResetNoProgressCounter();
                }

                lastProgressCheckPosition = currentPosition;
                lastProgressCheckTime = DateTime.Now;
            }
        }

        private void OnStuckAreaDetected(object sender, StuckAreaEventArgs e)
        {
            int blacklistRadius = 1;
            Vector2 aheadPosition = e.Position;

            (int gridX, int gridZ) = navigationGrid.WorldToGrid(aheadPosition);
            if (navigationGrid.IsValidCoordinate(gridX, gridZ) && navigationGrid.IsWalkable(gridX, gridZ))
            {
                navigationGrid.BlacklistArea(aheadPosition, blacklistRadius, pathVisualizer);
                lastPathfindingTime = DateTime.MinValue;
            }
        }

        private void RecalculatePath()
        {
            Vector2 currentPos = new Vector2(playerX, playerZ);
            float distanceFromLastRecalc = (currentPos - lastRecalcPosition).Magnitude();

            if (distanceFromLastRecalc < 5)
            {
                pathRecalcAtSamePositionCounter++;
                if (pathRecalcAtSamePositionCounter > 10)
                {
                    int blockedRadius = Math.Min(pathRecalcAtSamePositionCounter / 4, 2);
                    navigationGrid.BlacklistArea(currentPos, blockedRadius, pathVisualizer);

                    if (pathRecalcAtSamePositionCounter > 20)
                    {
                        currentPath = pathfinder.CreateEmergencyPath(currentPos, new Vector2(targetX, targetZ));
                        UpdateVisualizerPath();
                        lastPathfindingTime = DateTime.Now;
                        return;
                    }
                }
            }
            else
            {
                pathRecalcAtSamePositionCounter = 0;
            }

            lastRecalcPosition = currentPos;
            lastPathfindingTime = DateTime.Now;
            currentPath = pathfinder.FindPath(currentPos, new Vector2(targetX, targetZ));
            UpdateVisualizerPath();
        }

        private void UpdateVisualizerPath()
        {
            if (pathVisualizer != null && currentPath != null)
            {
                pathVisualizer.UpdatePath(currentPath, new Vector2(playerX, playerZ), new Vector2(targetX, targetZ));
            }
        }

        protected bool FocusGameWindow()
        {
            try
            {
                IntPtr gameWindow = FindWindow(null, "Monster Hunter Wilds");
                if (gameWindow != IntPtr.Zero)
                {
                    return SetForegroundWindow(gameWindow);
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}