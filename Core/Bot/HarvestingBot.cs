using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using MHWildsPathfindingBot.Core.Interfaces;
using MHWildsPathfindingBot.Core.Models;
using MHWildsPathfindingBot.Core.Utils;
using MHWildsPathfindingBot.Navigation;
using MHWildsPathfindingBot.UI;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace MHWildsPathfindingBot.Bot
{
    /// <summary>
    /// Extends the Bot class with resource harvesting capabilities
    /// </summary>
    public class HarvestingBot : Bot
    {
        private readonly WaypointManager waypointManager;
        private readonly ILogger logger;

        // Harvesting constants
        private const float HarvestingRadius = 2.0f; // How close to be to harvest
        private const int HarvestingDuration = 600; // Duration to hold B in milliseconds
        private const int WaitAfterHarvest = 3000; // Wait time after harvesting

        // Harvesting state
        private bool isHarvesting = false;
        private bool isWaitingAfterHarvest = false;
        private DateTime harvestingStartTime;
        private DateTime harvestingEndTime;

        // Auto-harvesting
        private bool autoHarvestEnabled = false;

        // Events
        public event EventHandler<EventArgs> HarvestingStarted;
        public event EventHandler<EventArgs> HarvestingCompleted;

        public HarvestingBot(
            ILogger logger,
            IFileManager fileManager,
            NavigationGrid navigationGrid,
            PathVisualizer pathVisualizer,
            string playerPositionFilePath)
            : base(logger, fileManager, navigationGrid, pathVisualizer, playerPositionFilePath)
        {
            this.logger = logger;
            waypointManager = new WaypointManager(logger);

            // Subscribe to target reached event
            this.TargetReached += OnTargetReached;

            // Disable blacklisting in the StuckDetector
            this.stuckDetector.SetBlacklistingEnabled(false);
        }

        /// <summary>
        /// Get the waypoint manager
        /// </summary>
        public WaypointManager GetWaypointManager()
        {
            return waypointManager;
        }

        /// <summary>
        /// Get the logger for debugging purposes
        /// </summary>
        public ILogger GetLogger()
        {
            return logger;
        }

        /// <summary>
        /// Enables or disables auto-harvesting mode
        /// </summary>
        public void SetAutoHarvesting(bool enabled)
        {
            autoHarvestEnabled = enabled;
            logger.LogMessage($"Auto-harvesting {(enabled ? "enabled" : "disabled")}");

            if (enabled && !IsRunning())
            {
                // Start the auto-harvesting process
                StartAutoHarvesting();
            }
        }

        /// <summary>
        /// Starts the auto-harvesting process
        /// </summary>
        private void StartAutoHarvesting()
        {
            if (waypointManager.GetWaypoints().Count == 0)
            {
                logger.LogMessage("No waypoints to harvest. Please add waypoints first.", Color.Yellow);
                return;
            }

            // Start with the first/next waypoint
            Waypoint nextWaypoint = waypointManager.GetNextWaypoint();
            if (nextWaypoint != null)
            {
                logger.LogMessage($"Auto-harvesting: Moving to {nextWaypoint.Name}", Color.LightGreen);
                FocusGameWindow();
                Thread.Sleep(300);
                Start(nextWaypoint.Position.X, nextWaypoint.Position.Z);
            }
        }

        /// <summary>
        /// Handles the target reached event
        /// </summary>
        private void OnTargetReached(object sender, EventArgs e)
        {
            // Debug the state when target is reached
            logger.LogMessage($"Target reached event fired. Auto-harvest: {autoHarvestEnabled}");

            if (!autoHarvestEnabled)
            {
                logger.LogMessage("Auto-harvest is disabled, not proceeding with harvesting");
                return;
            }

            // Get the current waypoint
            Waypoint currentWaypoint = waypointManager.GetCurrentWaypoint();
            if (currentWaypoint == null)
            {
                logger.LogMessage("No current waypoint found");
                return;
            }

            // Perform harvesting if this is a resource node
            if (currentWaypoint.IsResourceNode)
            {
                logger.LogMessage($"Harvesting at {currentWaypoint.Name}...");
                Task.Run(() => PerformHarvesting());
            }
            else
            {
                // If not a resource node, immediately move to the next waypoint
                logger.LogMessage("Not a resource node, continuing to next waypoint");
                ContinueToNextWaypoint();
            }
        }

        /// <summary>
        /// Performs the harvesting action (presses B)
        /// </summary>
        private async Task PerformHarvesting()
        {
            logger.LogMessage("Beginning harvesting action...");
            isHarvesting = true;
            HarvestingStarted?.Invoke(this, EventArgs.Empty);

            // Press B button to harvest
            try
            {
                // Stop movement first
                GetMovementController().StopMovement();

                // Wait 1 second after stopping before pressing B (as requested)
                logger.LogMessage("Stopped movement, waiting 1 second before harvesting...");
                await Task.Delay(1500);

                harvestingStartTime = DateTime.Now;

                // Press and hold B button
                logger.LogMessage("Pressing B button to harvest");
                InputSimulator.SendControllerButton(Xbox360Button.B, true);

                // Wait for the harvesting duration
                await Task.Delay(HarvestingDuration);

                // Release B button
                logger.LogMessage("Releasing B button");
                InputSimulator.SendControllerButton(Xbox360Button.B, false);

                harvestingEndTime = DateTime.Now;
                isHarvesting = false;
                isWaitingAfterHarvest = true;

                // Wait a shorter time (500ms) after harvesting before moving (as requested)
                logger.LogMessage("Waiting 500ms after harvest before moving");
                await Task.Delay(300);

                isWaitingAfterHarvest = false;
                HarvestingCompleted?.Invoke(this, EventArgs.Empty);

                // Continue to the next waypoint if auto-harvesting is enabled
                logger.LogMessage($"Harvesting complete, auto-harvest is: {autoHarvestEnabled}");
                if (autoHarvestEnabled)
                {
                    ContinueToNextWaypoint();
                }
            }
            catch (Exception ex)
            {
                logger.LogMessage($"Error during harvesting: {ex.Message}", Color.Red);
                isHarvesting = false;
                isWaitingAfterHarvest = false;
            }
        }

        /// <summary>
        /// Continues to the next waypoint in the route
        /// </summary>
        private void ContinueToNextWaypoint()
        {
            logger.LogMessage("Continuing to next waypoint...");
            Waypoint nextWaypoint = waypointManager.GetNextWaypoint();
            if (nextWaypoint != null)
            {
                logger.LogMessage($"Next waypoint is: {nextWaypoint.Name}", Color.LightGreen);

                // Short delay before moving to the next waypoint
                Thread.Sleep(500);

                // Use ContinueToTarget instead of Start for ongoing navigation
                ContinueToTarget(nextWaypoint.Position.X, nextWaypoint.Position.Z);

                // Force a path recalculation
                ForceRecalculatePath();
            }
            else
            {
                logger.LogMessage("No more waypoints in the route.", Color.Yellow);
            }
        }

        /// <summary>
        /// Manually trigger harvesting at the current position
        /// </summary>
        public void TriggerHarvesting()
        {
            if (isHarvesting || isWaitingAfterHarvest)
                return;

            logger.LogMessage("Manually triggered harvesting at current position");
            Task.Run(() => PerformHarvesting());
        }

        /// <summary>
        /// Returns true if the bot is currently harvesting
        /// </summary>
        public bool IsHarvesting()
        {
            return isHarvesting || isWaitingAfterHarvest;
        }

        /// <summary>
        /// Returns true if auto-harvesting is enabled
        /// </summary>
        public bool IsAutoHarvestEnabled()
        {
            return autoHarvestEnabled;
        }

        /// <summary>
        /// Stops the bot and disables auto-harvesting
        /// </summary>
        public new void Stop()
        {
            logger.LogMessage("Stopping harvesting bot");
            autoHarvestEnabled = false;
            isHarvesting = false;
            isWaitingAfterHarvest = false;
            InputSimulator.SendControllerButton(Xbox360Button.B, false); // Ensure B is released
            base.Stop();
        }
    }
}