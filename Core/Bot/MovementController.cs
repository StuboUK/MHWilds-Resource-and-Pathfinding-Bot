using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using MHWildsPathfindingBot.Core.Interfaces;
using MHWildsPathfindingBot.Core.Models;
using MHWildsPathfindingBot.Core.Utils;

namespace MHWildsPathfindingBot.Bot
{
    /// <summary>
    /// Controls character movement using only the left analog stick
    /// with camera fixed to North orientation
    /// </summary>
    public class MovementController
    {
        private readonly ILogger logger;
        private Vector2 currentMovementVector = new Vector2(0, 0);
        private float currentSpeed = 1.0f; // Default to full speed
        private bool isFirstMovement = true;
        private DateTime botStartTime = DateTime.MinValue;

        // Player position tracking for debug logging
        private float playerX = 0;
        private float playerZ = 0;

        // Direction constants assuming camera faces NORTH
        // In this configuration:
        // - Forward/Up on stick = character moves North (negative Z)
        // - Right on stick = character moves East (positive X)
        // - Down on stick = character moves South (positive Z)
        // - Left on stick = character moves West (negative X)

        // Debug logging
        private string debugLogPath = "movement_debug.txt";
        private bool debugLoggingEnabled = false; // Disabled by default

        // Property to expose current speed
        public float CurrentSpeed => currentSpeed;

        // Constructor
        public MovementController(ILogger logger)
        {
            this.logger = logger;
            botStartTime = DateTime.Now;

            // Start debug logging if needed
            if (debugLoggingEnabled)
            {
                StartDebugLogging();
            }

            // Initialize controller
            if (InputSimulator.IsControllerConnected())
            {
                logger.LogMessage("XInput controller detected");
                InputSimulator.EnsureControllerInitialized();
            }
            else
            {
                logger.LogMessage("WARNING: No physical XInput controller detected. Virtual controller will still be used.", Color.Yellow);
                InputSimulator.EnsureControllerInitialized();
            }
        }

        // Reset movement state
        public void Reset()
        {
            currentMovementVector = new Vector2(0, 0);
            currentSpeed = 1.0f;
            isFirstMovement = true;
            botStartTime = DateTime.Now;
            InputSimulator.ResetControllerState();

            if (debugLoggingEnabled)
            {
                LogDebugInfo("Controller reset called");
            }
        }

        public bool HasStartedMoving()
        {
            return !isFirstMovement;
        }

        public void PressForward(bool press)
        {
            // Set forward movement on left stick
            InputSimulator.SetLeftThumbstick(0, press ? -1.0f : 0.0f);

            if (debugLoggingEnabled)
            {
                LogDebugInfo($"PressForward({press}) called");
            }
        }

        // Debug logging methods
        private void LogDebugInfo(string message)
        {
            if (!debugLoggingEnabled) return;

            try
            {
                using (StreamWriter writer = new StreamWriter(debugLogPath, true))
                {
                    writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
                }
            }
            catch (Exception ex)
            {
                logger.LogMessage($"Failed to write debug log: {ex.Message}", Color.Red);
            }
        }

        public void StartDebugLogging()
        {
            try
            {
                // Create/clear log file
                using (StreamWriter writer = new StreamWriter(debugLogPath, false))
                {
                    writer.WriteLine($"=== Movement Debug Log Started {DateTime.Now} ===");
                    writer.WriteLine("Format: [Timestamp] Message");
                }
                debugLoggingEnabled = true;
                logger.LogMessage("Debug logging started to " + debugLogPath);
            }
            catch (Exception ex)
            {
                logger.LogMessage($"Failed to start debug logging: {ex.Message}", Color.Red);
                debugLoggingEnabled = false;
            }
        }

        /// <summary>
        /// Main method for omnidirectional movement using left analog stick
        /// </summary>
        /// <param name="desiredDirection">Direction vector where character should move</param>
        /// <param name="currentPosition">Current position of the character</param>
        /// <param name="currentPath">Optional path data for visualization</param>
        public void MoveInDirection(Vector2 desiredDirection, Vector2 currentPosition, List<Vector2> currentPath)
        {
            // Store position for logging
            playerX = currentPosition.X;
            playerZ = currentPosition.Z;

            // Don't move immediately after starting
            if ((DateTime.Now - botStartTime).TotalSeconds < 0.5)
                return;

            // Handle normal movement logic
            if (desiredDirection.Magnitude() < 0.001f || currentPath == null || currentPath.Count <= 1)
            {
                StopMovement();
                return;
            }

            if (isFirstMovement)
            {
                isFirstMovement = false;
                if (debugLoggingEnabled)
                {
                    LogDebugInfo($"First movement initiated, direction: {desiredDirection}");
                }
            }

            // Convert world direction to analog stick input
            // Assuming camera fixed to NORTH:
            // +X (East) corresponds to right on stick (X = +1)
            // -X (West) corresponds to left on stick (X = -1)
            // -Z (North) corresponds to up on stick (Y = -1)
            // +Z (South) corresponds to down on stick (Y = +1)

            // Normalize input to ensure we don't exceed -1 to 1 range
            Vector2 normalizedDirection = desiredDirection.Normalized();

            // Map world directions to stick directions
            float leftStickX = normalizedDirection.X;
            float leftStickZ = normalizedDirection.Z;

            // Apply movement
            ApplyDirectionalMovement(leftStickX, leftStickZ);
        }

        /// <summary>
        /// Applies the actual directional movement based on stick coordinates
        /// </summary>
        private void ApplyDirectionalMovement(float leftStickX, float leftStickZ)
        {
            // Ensure input is within valid range
            leftStickX = Math.Clamp(leftStickX, -1.0f, 1.0f);
            leftStickZ = Math.Clamp(leftStickZ, -1.0f, 1.0f);

            // Apply to controller
            InputSimulator.SetLeftThumbstick(leftStickX, leftStickZ);

            if (debugLoggingEnabled)
            {
                LogDebugInfo($"POS=({playerX:F1},{playerZ:F1}) | LEFT STICK: X={leftStickX:F2}, Z={leftStickZ:F2}");
            }
        }

        public void StopMovement()
        {
            InputSimulator.ResetControllerState();

            if (debugLoggingEnabled)
            {
                LogDebugInfo("Movement stopped");
            }
        }

        /// <summary>
        /// Gets the controller for method access
        /// </summary>
        public MovementController GetMovementController()
        {
            return this;
        }
    }
}