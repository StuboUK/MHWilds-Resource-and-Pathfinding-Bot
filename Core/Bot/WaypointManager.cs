using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Drawing;
using System.Threading;
using MHWildsPathfindingBot.Core.Interfaces;
using MHWildsPathfindingBot.Core.Models;

namespace MHWildsPathfindingBot.Bot
{
    /// <summary>
    /// Manages resource node waypoints for bot harvesting routes
    /// </summary>
    public class WaypointManager
    {
        private readonly ILogger logger;
        private readonly string waypointsFilePath = "waypoints.json";
        private List<Waypoint> waypoints = new List<Waypoint>();
        private int currentWaypointIndex = -1;

        public event EventHandler<WaypointEventArgs> WaypointsChanged;
        public event EventHandler<WaypointEventArgs> CurrentWaypointChanged;

        public WaypointManager(ILogger logger)
        {
            this.logger = logger;
            LoadWaypoints();
        }

        /// <summary>
        /// Gets all waypoints
        /// </summary>
        public List<Waypoint> GetWaypoints()
        {
            return new List<Waypoint>(waypoints);
        }

        public int GetWaypointCount()
        {
            return waypoints.Count;
        }

        /// <summary>
        /// Adds a new waypoint
        /// </summary>
        public void AddWaypoint(Vector2 position, string name = "")
        {
            if (string.IsNullOrEmpty(name))
            {
                name = $"Waypoint {waypoints.Count + 1}";
            }

            Waypoint waypoint = new Waypoint
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Position = position,
                IsResourceNode = true
            };

            waypoints.Add(waypoint);
            logger.LogMessage($"Added waypoint '{name}' at ({position.X:F1}, {position.Z:F1})", Color.LightGreen);
            SaveWaypoints();
            WaypointsChanged?.Invoke(this, new WaypointEventArgs { Waypoints = GetWaypoints() });
        }

        /// <summary>
        /// Removes a waypoint by ID
        /// </summary>
        public bool RemoveWaypoint(string id)
        {
            Waypoint waypointToRemove = waypoints.FirstOrDefault(w => w.Id == id);
            if (waypointToRemove != null)
            {
                waypoints.Remove(waypointToRemove);
                logger.LogMessage($"Removed waypoint '{waypointToRemove.Name}'", Color.LightYellow);
                SaveWaypoints();

                // If we removed the current waypoint, reset the index
                if (currentWaypointIndex >= waypoints.Count)
                {
                    currentWaypointIndex = waypoints.Count > 0 ? 0 : -1;
                    CurrentWaypointChanged?.Invoke(this, new WaypointEventArgs
                    {
                        Waypoints = GetWaypoints(),
                        CurrentWaypointIndex = currentWaypointIndex
                    });
                }

                WaypointsChanged?.Invoke(this, new WaypointEventArgs { Waypoints = GetWaypoints() });
                return true;
            }
            return false;
        }

        /// <summary>
        /// Updates a waypoint's properties
        /// </summary>
        public bool UpdateWaypoint(string id, string name, Vector2 position, bool isResourceNode)
        {
            Waypoint waypoint = waypoints.FirstOrDefault(w => w.Id == id);
            if (waypoint != null)
            {
                waypoint.Name = name;
                waypoint.Position = position;
                waypoint.IsResourceNode = isResourceNode;
                logger.LogMessage($"Updated waypoint '{name}'", Color.LightGreen);
                SaveWaypoints();
                WaypointsChanged?.Invoke(this, new WaypointEventArgs { Waypoints = GetWaypoints() });
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the next waypoint for the bot to navigate to
        /// </summary>
        public Waypoint GetNextWaypoint()
        {
            if (waypoints.Count == 0)
                return null;

            // Move to the next waypoint
            currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Count;

            CurrentWaypointChanged?.Invoke(this, new WaypointEventArgs
            {
                Waypoints = GetWaypoints(),
                CurrentWaypointIndex = currentWaypointIndex
            });

            return waypoints[currentWaypointIndex];
        }

        /// <summary>
        /// Gets the current waypoint
        /// </summary>
        public Waypoint GetCurrentWaypoint()
        {
            if (currentWaypointIndex >= 0 && currentWaypointIndex < waypoints.Count)
                return waypoints[currentWaypointIndex];
            return null;
        }

        /// <summary>
        /// Loads waypoints from file
        /// </summary>
        private void LoadWaypoints()
        {
            try
            {
                if (File.Exists(waypointsFilePath))
                {
                    string json = File.ReadAllText(waypointsFilePath);
                    waypoints = JsonSerializer.Deserialize<List<Waypoint>>(json);
                    logger.LogMessage($"Loaded {waypoints.Count} waypoints", Color.LightGreen);

                    if (waypoints.Count > 0)
                    {
                        currentWaypointIndex = 0;
                    }
                }
                else
                {
                    logger.LogMessage("No existing waypoints file found. Creating new waypoints list.", Color.Yellow);
                    waypoints = new List<Waypoint>();
                }
            }
            catch (Exception ex)
            {
                logger.LogMessage($"Error loading waypoints: {ex.Message}", Color.Red);
                waypoints = new List<Waypoint>();
            }
        }

        /// <summary>
        /// Saves waypoints to file
        /// </summary>
        public void SaveWaypoints()
        {
            try
            {
                string json = JsonSerializer.Serialize(waypoints, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(waypointsFilePath, json);
                logger.LogMessage($"Saved {waypoints.Count} waypoints", Color.LightGreen);
            }
            catch (Exception ex)
            {
                logger.LogMessage($"Error saving waypoints: {ex.Message}", Color.Red);
            }
        }

        /// <summary>
        /// Set the current waypoint index
        /// </summary>
        public void SetCurrentWaypointIndex(int index)
        {
            if (index >= -1 && index < waypoints.Count)
            {
                currentWaypointIndex = index;
                CurrentWaypointChanged?.Invoke(this, new WaypointEventArgs
                {
                    Waypoints = GetWaypoints(),
                    CurrentWaypointIndex = currentWaypointIndex
                });
            }
        }

        /// <summary>
        /// Get the current waypoint index
        /// </summary>
        public int GetCurrentWaypointIndex()
        {
            return currentWaypointIndex;
        }

        /// <summary>
        /// Optimizes the waypoint route using a simple nearest-neighbor approach
        /// </summary>
        public void OptimizeRoute()
        {
            if (waypoints.Count <= 2)
                return;

            logger.LogMessage("Optimizing waypoint route...", Color.LightBlue);

            List<Waypoint> optimizedRoute = new List<Waypoint>();
            List<Waypoint> remainingWaypoints = new List<Waypoint>(waypoints);

            // Start with the first waypoint
            Waypoint currentWaypoint = remainingWaypoints[0];
            optimizedRoute.Add(currentWaypoint);
            remainingWaypoints.Remove(currentWaypoint);

            // Find the nearest neighbor for each waypoint
            while (remainingWaypoints.Count > 0)
            {
                Waypoint nearest = null;
                float minDistance = float.MaxValue;

                foreach (var waypoint in remainingWaypoints)
                {
                    // Convert positions to Vector2 before calculating distance
                    Vector2 currentPos = currentWaypoint.Position;
                    Vector2 waypointPos = waypoint.Position;

                    float distance = (waypointPos - currentPos).Magnitude();
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        nearest = waypoint;
                    }
                }

                currentWaypoint = nearest;
                optimizedRoute.Add(currentWaypoint);
                remainingWaypoints.Remove(currentWaypoint);
            }

            waypoints = optimizedRoute;
            currentWaypointIndex = 0;

            SaveWaypoints();
            logger.LogMessage("Route optimization complete", Color.LightGreen);
            WaypointsChanged?.Invoke(this, new WaypointEventArgs { Waypoints = GetWaypoints() });
        }

        /// <summary>
        /// Moves a waypoint up in the list
        /// </summary>
        public bool MoveWaypointUp(string id)
        {
            int index = waypoints.FindIndex(w => w.Id == id);
            if (index <= 0)
                return false;

            Waypoint waypoint = waypoints[index];
            waypoints.RemoveAt(index);
            waypoints.Insert(index - 1, waypoint);

            SaveWaypoints();
            WaypointsChanged?.Invoke(this, new WaypointEventArgs { Waypoints = GetWaypoints() });
            return true;
        }

        /// <summary>
        /// Moves a waypoint down in the list
        /// </summary>
        public bool MoveWaypointDown(string id)
        {
            int index = waypoints.FindIndex(w => w.Id == id);
            if (index < 0 || index >= waypoints.Count - 1)
                return false;

            Waypoint waypoint = waypoints[index];
            waypoints.RemoveAt(index);
            waypoints.Insert(index + 1, waypoint);

            SaveWaypoints();
            WaypointsChanged?.Invoke(this, new WaypointEventArgs { Waypoints = GetWaypoints() });
            return true;
        }

        /// <summary>
        /// Exports waypoints to a specified JSON file
        /// </summary>
        public void ExportWaypointsToFile(string filePath)
        {
            try
            {
                string json = JsonSerializer.Serialize(waypoints, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
                logger.LogMessage($"Exported {waypoints.Count} waypoints to {Path.GetFileName(filePath)}", Color.LightGreen);
            }
            catch (Exception ex)
            {
                logger.LogMessage($"Error exporting waypoints: {ex.Message}", Color.Red);
                throw;
            }
        }

        /// <summary>
        /// Imports waypoints from a specified JSON file
        /// </summary>
        public void ImportWaypointsFromFile(string filePath)
        {
            try
            {
                string json = File.ReadAllText(filePath);
                List<Waypoint> importedWaypoints = JsonSerializer.Deserialize<List<Waypoint>>(json);

                if (importedWaypoints == null || importedWaypoints.Count == 0)
                {
                    logger.LogMessage("No waypoints found in the import file.", Color.Yellow);
                    return;
                }

                // Option to append or replace existing waypoints
                bool hasExistingWaypoints = waypoints.Count > 0;
                bool appendWaypoints = true; // Set to false to replace all waypoints

                if (hasExistingWaypoints && !appendWaypoints)
                {
                    // Replace existing waypoints
                    waypoints.Clear();
                }

                // Add imported waypoints
                foreach (var waypoint in importedWaypoints)
                {
                    // Ensure each waypoint has a unique ID
                    if (string.IsNullOrEmpty(waypoint.Id))
                    {
                        waypoint.Id = Guid.NewGuid().ToString();
                    }
                    waypoints.Add(waypoint);
                }

                logger.LogMessage($"Imported {importedWaypoints.Count} waypoints from {Path.GetFileName(filePath)}", Color.LightGreen);

                // Save the updated waypoints
                SaveWaypoints();

                // Notify that waypoints have changed
                WaypointsChanged?.Invoke(this, new WaypointEventArgs { Waypoints = GetWaypoints() });

                // Reset the current waypoint index
                SetCurrentWaypointIndex(waypoints.Count > 0 ? 0 : -1);
            }
            catch (Exception ex)
            {
                logger.LogMessage($"Error importing waypoints: {ex.Message}", Color.Red);
                throw;
            }
        }

        /// <summary>
        /// Creates example waypoints for testing
        /// </summary>
        public void CreateExampleWaypoints()
        {
            // Clear existing waypoints
            waypoints.Clear();

            // Add some example waypoints in a circular pattern
            float centerX = -10.0f;
            float centerZ = 20.0f;
            float radius = 50.0f;
            int count = 6;

            for (int i = 0; i < count; i++)
            {
                float angle = i * (360f / count) * (float)Math.PI / 180f;
                float x = centerX + radius * (float)Math.Cos(angle);
                float z = centerZ + radius * (float)Math.Sin(angle);

                Waypoint waypoint = new Waypoint
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = $"Waypoint {i + 1}",
                    Position = new Vector2(x, z),
                    IsResourceNode = i % 2 == 0 // Alternate between resource nodes and regular waypoints
                };

                waypoints.Add(waypoint);
            }

            // Save and notify
            SaveWaypoints();
            logger.LogMessage($"Created {waypoints.Count} example waypoints", Color.LightGreen);
            WaypointsChanged?.Invoke(this, new WaypointEventArgs { Waypoints = GetWaypoints() });
            SetCurrentWaypointIndex(0);
        }
    }

    /// <summary>
    /// Represents a waypoint in the harvesting route
    /// </summary>
    public class Waypoint
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public Vector2Position Position { get; set; }
        public bool IsResourceNode { get; set; } = true;

        // This class is used for serialization
        public class Vector2Position
        {
            public float X { get; set; }
            public float Z { get; set; }

            public static implicit operator Vector2(Vector2Position position)
            {
                return new Vector2(position.X, position.Z);
            }

            public static implicit operator Vector2Position(Vector2 vector)
            {
                return new Vector2Position { X = vector.X, Z = vector.Z };
            }
        }
    }

    /// <summary>
    /// Event arguments for waypoint events
    /// </summary>
    public class WaypointEventArgs : EventArgs
    {
        public List<Waypoint> Waypoints { get; set; }
        public int CurrentWaypointIndex { get; set; } = -1;
    }
}