using System;
using System.Drawing;
using System.Threading;
using MHWildsPathfindingBot.Core.Interfaces;
using MHWildsPathfindingBot.Core.Models;
using MHWildsPathfindingBot.Core.Utils;

namespace MHWildsPathfindingBot.Bot
{
    public class StuckDetector
    {
        private readonly ILogger logger;
        private int noProgressCounter = 0;
        private Vector2 lastPosition = new Vector2(0, 0);
        private Vector2 lastStuckPosition = new Vector2(0, 0);
        private DateTime lastObstacleUpdate = DateTime.MinValue;
        private bool hasStartedMoving = false;
        private int consecutiveStuckDetections = 0;

        // Added flag to disable blacklisting
        private bool blacklistingEnabled = false;

        public int NoProgressCount => noProgressCounter;
        public event EventHandler<StuckAreaEventArgs> StuckAreaDetected;

        public StuckDetector(ILogger logger)
        {
            this.logger = logger;
        }

        public void ResetNoProgressCounter() => noProgressCounter = 0;
        public void IncrementNoProgressCounter() => noProgressCounter++;

        /// <summary>
        /// Enable or disable blacklisting functionality
        /// </summary>
        public void SetBlacklistingEnabled(bool enabled)
        {
            blacklistingEnabled = false;
            logger.LogMessage($"Area blacklisting {(enabled ? "enabled" : "disabled")}");
        }

        public void Run(Func<Vector2> getPositionFunc, Func<bool> isRunningFunc, CancellationToken token)
        {
            int stuckCounter = 0;
            DateTime botStartTime = DateTime.Now;

            while (!token.IsCancellationRequested && isRunningFunc())
            {
                Vector2 currentPosition = getPositionFunc();

                // Skip initial detection
                if ((DateTime.Now - botStartTime).TotalSeconds < 5.0)
                {
                    lastPosition = currentPosition;
                    Thread.Sleep(1000);
                    continue;
                }

                float movement = (currentPosition - lastPosition).Magnitude();

                // Track first movement
                if (!hasStartedMoving && movement > 0.5f)
                    hasStartedMoving = true;

                float stuckThreshold = hasStartedMoving ? 0.1f : 0.05f;

                if (movement < stuckThreshold) // Almost no movement
                {
                    stuckCounter++;

                    if (stuckCounter > 5) // Stuck for 5+ seconds
                    {
                        // Only handle blacklisting if enabled
                        if (blacklistingEnabled)
                        {
                            // Check if stuck in same spot
                            float distanceFromLastStuck = (currentPosition - lastStuckPosition).Magnitude();

                            // Handle repeated stuckness in same area
                            if (distanceFromLastStuck < 1.0f &&
                                (DateTime.Now - lastObstacleUpdate).TotalSeconds > 10)
                            {
                                consecutiveStuckDetections++;

                                if (consecutiveStuckDetections >= 3)
                                {
                                    // Notify about obstacle
                                    StuckAreaDetected?.Invoke(this, new StuckAreaEventArgs(currentPosition, new Vector2(0, 0)));
                                    lastObstacleUpdate = DateTime.Now;
                                    consecutiveStuckDetections = 0;
                                }
                            }
                            else if (distanceFromLastStuck >= 1.0f)
                            {
                                consecutiveStuckDetections = 1;
                            }

                            lastStuckPosition = currentPosition;
                        }

                        // Try evasive maneuvers for extended stuckness
                        if (stuckCounter > 15)
                        {
                            logger.LogMessage("Bot seems stuck - attempting evasive maneuvers", Color.Yellow);

                            // Reset controller
                            InputSimulator.ResetControllerState();
                            Thread.Sleep(200);

                            // Backward movement
                            InputSimulator.SetLeftThumbstick(0, 0.8f);
                            Thread.Sleep(1000);

                            // Side movement - alternate directions
                            InputSimulator.SetLeftThumbstick(stuckCounter % 2 == 0 ? -0.8f : 0.8f, 0);
                            Thread.Sleep(800);

                            // Diagonal movement
                            InputSimulator.SetLeftThumbstick(0.7f, 0.7f);
                            Thread.Sleep(800);

                            InputSimulator.ResetControllerState();
                            stuckCounter = 0;
                        }
                    }
                }
                else
                {
                    stuckCounter = 0;
                }

                lastPosition = currentPosition;
                Thread.Sleep(1000);
            }
        }
    }
}