using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using MHWildsPathfindingBot.Core.Interfaces;
using MHWildsPathfindingBot.Core.Models;
using System.Diagnostics;

namespace MHWildsPathfindingBot.Navigation.Pathfinding
{
    /// <summary>
    /// Enhanced pathfinding using A* algorithm with parallel search and improved heuristics
    /// </summary>
    public class Pathfinder
    {
        private readonly ILogger logger;
        private readonly NavigationGrid navigationGrid;
        private readonly PathOptimizer pathOptimizer;

        // Pathfinding settings for performance tuning
        private const int MaxIterations = 20000; // Increased maximum iterations for complex paths
        private const int ParallelSearchThreshold = 2000; // Distance threshold for parallel search
        private const int CacheMaxSize = 100; // Maximum size of path cache

        // Path caching
        private readonly Dictionary<(Vector2, Vector2), List<Vector2>> pathCache = new Dictionary<(Vector2, Vector2), List<Vector2>>();
        private readonly Queue<(Vector2, Vector2)> cacheOrder = new Queue<(Vector2, Vector2)>();

        // Heuristic weights
        private const float DiagonalWeight = 1.414f; // Weight for diagonal movement (sqrt(2))
        private const float HeuristicWeight = 1.1f; // Slight overestimation for faster convergence
        private const float BlacklistAvoidanceWeight = 10.0f; // Strong penalty for blacklisted areas

        public Pathfinder(ILogger logger, NavigationGrid navigationGrid)
        {
            this.logger = logger;
            this.navigationGrid = navigationGrid;
            this.pathOptimizer = new PathOptimizer(navigationGrid);
        }

        /// <summary>
        /// Find an optimized path between start and end positions
        /// </summary>
        public List<Vector2> FindPath(Vector2 start, Vector2 end)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            logger.LogMessage($"Calculating path to target ({end.X:F2}, {end.Z:F2})");

            // Check if path is in cache
            var cacheKey = (start, end);
            if (pathCache.ContainsKey(cacheKey))
            {
                logger.LogMessage("Using cached path");
                return new List<Vector2>(pathCache[cacheKey]);
            }

            // Find nearest walkable (and not blacklisted) positions
            Vector2 walkableStart = navigationGrid.FindNearestWalkable(start);
            Vector2 walkableEnd = navigationGrid.FindNearestWalkable(end);

            // Calculate direct distance to determine search strategy
            float directDistance = (walkableEnd - walkableStart).Magnitude();
            List<Vector2> path;

            // Use parallel search for long distances
            if (directDistance > ParallelSearchThreshold)
            {
                path = ParallelAStarSearch(walkableStart, walkableEnd);
            }
            else
            {
                path = AStarSearch(walkableStart, walkableEnd);
            }

            if (path != null && path.Count > 0)
            {
                logger.LogMessage($"Path found with {path.Count} waypoints");

                // Apply multi-stage optimization
                OptimizePath(path);

                // Cache the result if it's worth caching (more than two points)
                if (path.Count > 2 && !pathCache.ContainsKey(cacheKey))
                {
                    CachePath(cacheKey, path);
                }

                sw.Stop();
                logger.LogMessage($"Pathfinding completed in {sw.ElapsedMilliseconds}ms");
                return path;
            }
            else
            {
                logger.LogMessage("No path found! Creating emergency path.", Color.Red);
                return CreateEmergencyPath(start, end);
            }
        }

        /// <summary>
        /// Optimize the path with all available optimizers
        /// </summary>
        private void OptimizePath(List<Vector2> path)
        {
            int originalCount = path.Count;

            // Stage 1: Wall avoidance
            pathOptimizer.OptimizePathForWallAvoidance(path);

            // Stage 2: Path simplification
            pathOptimizer.SimplifyPath(path);
            logger.LogMessage($"Path simplified to {path.Count} waypoints");

            // Stage 3: Path smoothing for multi-point paths
            if (path.Count > 2)
            {
                List<Vector2> smoothedPath = pathOptimizer.SmoothPath(path);

                // Replace with smoothed path if it's valid
                if (smoothedPath.Count > 0)
                {
                    path.Clear();
                    path.AddRange(smoothedPath);
                }
            }

            logger.LogMessage($"Final path has {path.Count} waypoints (reduced from {originalCount})");

            if (path.Count > 1)
            {
                logger.LogMessage($"First waypoint: ({path[1].X:F2}, {path[1].Z:F2})");
            }
        }

        /// <summary>
        /// Cache a path and manage cache size
        /// </summary>
        private void CachePath((Vector2, Vector2) key, List<Vector2> path)
        {
            // Add to cache
            pathCache[key] = new List<Vector2>(path);
            cacheOrder.Enqueue(key);

            // Manage cache size
            if (cacheOrder.Count > CacheMaxSize)
            {
                var oldestKey = cacheOrder.Dequeue();
                pathCache.Remove(oldestKey);
            }
        }

        /// <summary>
        /// Create a fallback direct path when no normal path can be found
        /// </summary>
        public List<Vector2> CreateEmergencyPath(Vector2 start, Vector2 end)
        {
            Vector2 direction = (end - start).Normalized();
            float totalDistance = (end - start).Magnitude();

            // Enhanced emergency path with dynamic point distribution
            List<Vector2> directPath = new List<Vector2>();
            directPath.Add(start);

            // Add more intermediate points for longer distances
            int pointCount = Math.Min(10, Math.Max(5, (int)(totalDistance / 15)));

            for (int i = 1; i < pointCount; i++)
            {
                float distance = totalDistance * i / pointCount;
                Vector2 point = new Vector2(
                    start.X + direction.X * distance,
                    start.Z + direction.Z * distance
                );

                // Try to find walkable points near the direct line
                (int gridX, int gridZ) = navigationGrid.WorldToGrid(point);
                if (navigationGrid.IsValidCoordinate(gridX, gridZ))
                {
                    // Check surrounding area for walkable points
                    bool foundWalkable = false;
                    for (int radius = 0; radius < 5 && !foundWalkable; radius++)
                    {
                        for (int dx = -radius; dx <= radius && !foundWalkable; dx++)
                        {
                            for (int dz = -radius; dz <= radius && !foundWalkable; dz++)
                            {
                                int checkX = gridX + dx;
                                int checkZ = gridZ + dz;

                                if (navigationGrid.IsValidCoordinate(checkX, checkZ) &&
                                    navigationGrid.IsWalkable(checkX, checkZ) &&
                                    !navigationGrid.IsBlacklisted(checkX, checkZ))
                                {
                                    Vector2 walkablePoint = navigationGrid.GridToWorld(checkX, checkZ);
                                    directPath.Add(walkablePoint);
                                    foundWalkable = true;
                                    break;
                                }
                            }
                        }
                    }

                    // If no walkable point found, use original point
                    if (!foundWalkable)
                    {
                        directPath.Add(point);
                    }
                }
                else
                {
                    directPath.Add(point);
                }
            }

            directPath.Add(end);

            logger.LogMessage("Created emergency direct path with " + directPath.Count + " waypoints");
            return directPath;
        }

        /// <summary>
        /// Standard A* pathfinding algorithm
        /// </summary>
        private List<Vector2> AStarSearch(Vector2 start, Vector2 end)
        {
            AStarNode startNode = new AStarNode(start);
            AStarNode endNode = new AStarNode(end);
            SortedSet<AStarNode> openSet = new SortedSet<AStarNode>();
            HashSet<Vector2> closedSet = new HashSet<Vector2>();
            Dictionary<Vector2, AStarNode> allNodes = new Dictionary<Vector2, AStarNode>();

            openSet.Add(startNode);
            allNodes[start] = startNode;

            int iterations = 0;

            while (openSet.Count > 0 && iterations < MaxIterations)
            {
                iterations++;
                AStarNode currentNode = openSet.Min;
                openSet.Remove(currentNode);
                closedSet.Add(currentNode.Position);

                // Check if we've reached the destination
                if ((currentNode.Position - end).Magnitude() < navigationGrid.CellSize)
                {
                    logger.LogMessage($"Path found in {iterations} iterations");
                    return RetracePath(startNode, currentNode);
                }

                // Process all neighbors
                foreach (AStarNode neighbor in GetNeighbors(currentNode))
                {
                    if (closedSet.Contains(neighbor.Position))
                        continue;

                    // Calculate cost of this path
                    float newCost = currentNode.GCost + (currentNode.Position - neighbor.Position).Magnitude();

                    // Diagonal movement costs more
                    if (Math.Abs(currentNode.Position.X - neighbor.Position.X) > 0.01f &&
                        Math.Abs(currentNode.Position.Z - neighbor.Position.Z) > 0.01f)
                    {
                        newCost *= DiagonalWeight;
                    }

                    // Check if this node exists in the open set
                    bool inOpenSet = allNodes.TryGetValue(neighbor.Position, out AStarNode existingNode) && openSet.Contains(existingNode);

                    if (!inOpenSet || newCost < existingNode.GCost)
                    {
                        // Create or update the node
                        if (!inOpenSet)
                        {
                            neighbor.GCost = newCost;
                            neighbor.HCost = CalculateHeuristic(neighbor.Position, end);
                            neighbor.Parent = currentNode;

                            // Calculate penalties for hazards
                            CalculateProximityPenalties(neighbor);

                            openSet.Add(neighbor);
                            allNodes[neighbor.Position] = neighbor;
                        }
                        else
                        {
                            openSet.Remove(existingNode);
                            existingNode.GCost = newCost;
                            existingNode.Parent = currentNode;
                            openSet.Add(existingNode);
                        }
                    }
                }
            }

            if (iterations >= MaxIterations)
            {
                logger.LogMessage("Pathfinding exceeded maximum iterations", Color.Yellow);
            }

            return null;
        }

        /// <summary>
        /// Parallel A* search using bidirectional approach
        /// </summary>
        private List<Vector2> ParallelAStarSearch(Vector2 start, Vector2 end)
        {
            logger.LogMessage("Using parallel search for long distance");

            // Setup forward search
            AStarNode startNode = new AStarNode(start);
            SortedSet<AStarNode> forwardOpenSet = new SortedSet<AStarNode>();
            HashSet<Vector2> forwardClosedSet = new HashSet<Vector2>();
            Dictionary<Vector2, AStarNode> forwardAllNodes = new Dictionary<Vector2, AStarNode>();

            // Setup backward search
            AStarNode endNode = new AStarNode(end);
            SortedSet<AStarNode> backwardOpenSet = new SortedSet<AStarNode>();
            HashSet<Vector2> backwardClosedSet = new HashSet<Vector2>();
            Dictionary<Vector2, AStarNode> backwardAllNodes = new Dictionary<Vector2, AStarNode>();

            // Initialize both searches
            forwardOpenSet.Add(startNode);
            forwardAllNodes[start] = startNode;

            backwardOpenSet.Add(endNode);
            backwardAllNodes[end] = endNode;

            int iterations = 0;
            AStarNode meetingNode = null;
            float bestMeetingCost = float.MaxValue;

            // Run bidirectional search
            while (forwardOpenSet.Count > 0 && backwardOpenSet.Count > 0 && iterations < MaxIterations)
            {
                iterations++;

                // Forward search step
                if (forwardOpenSet.Count > 0)
                {
                    AStarNode currentNode = forwardOpenSet.Min;
                    forwardOpenSet.Remove(currentNode);
                    forwardClosedSet.Add(currentNode.Position);

                    // Check if this node has been reached by the backward search
                    if (backwardClosedSet.Contains(currentNode.Position))
                    {
                        // Potential meeting point
                        AStarNode backwardNode = backwardAllNodes[currentNode.Position];
                        float meetingCost = currentNode.GCost + backwardNode.GCost;

                        if (meetingCost < bestMeetingCost)
                        {
                            bestMeetingCost = meetingCost;
                            meetingNode = currentNode;
                        }
                    }

                    // Process forward neighbors
                    foreach (AStarNode neighbor in GetNeighbors(currentNode))
                    {
                        if (forwardClosedSet.Contains(neighbor.Position))
                            continue;

                        float newCost = currentNode.GCost + (currentNode.Position - neighbor.Position).Magnitude();

                        // Diagonal movement costs more
                        if (Math.Abs(currentNode.Position.X - neighbor.Position.X) > 0.01f &&
                            Math.Abs(currentNode.Position.Z - neighbor.Position.Z) > 0.01f)
                        {
                            newCost *= DiagonalWeight;
                        }

                        bool inOpenSet = forwardAllNodes.TryGetValue(neighbor.Position, out AStarNode existingNode) &&
                                        forwardOpenSet.Contains(existingNode);

                        if (!inOpenSet || newCost < existingNode.GCost)
                        {
                            if (!inOpenSet)
                            {
                                neighbor.GCost = newCost;
                                neighbor.HCost = CalculateHeuristic(neighbor.Position, end);
                                neighbor.Parent = currentNode;

                                CalculateProximityPenalties(neighbor);

                                forwardOpenSet.Add(neighbor);
                                forwardAllNodes[neighbor.Position] = neighbor;
                            }
                            else
                            {
                                forwardOpenSet.Remove(existingNode);
                                existingNode.GCost = newCost;
                                existingNode.Parent = currentNode;
                                forwardOpenSet.Add(existingNode);
                            }
                        }
                    }
                }

                // Backward search step
                if (backwardOpenSet.Count > 0)
                {
                    AStarNode currentNode = backwardOpenSet.Min;
                    backwardOpenSet.Remove(currentNode);
                    backwardClosedSet.Add(currentNode.Position);

                    // Check if this node has been reached by the forward search
                    if (forwardClosedSet.Contains(currentNode.Position))
                    {
                        // Potential meeting point
                        AStarNode forwardNode = forwardAllNodes[currentNode.Position];
                        float meetingCost = forwardNode.GCost + currentNode.GCost;

                        if (meetingCost < bestMeetingCost)
                        {
                            bestMeetingCost = meetingCost;
                            meetingNode = forwardNode;
                        }
                    }

                    // Process backward neighbors
                    foreach (AStarNode neighbor in GetNeighbors(currentNode))
                    {
                        if (backwardClosedSet.Contains(neighbor.Position))
                            continue;

                        float newCost = currentNode.GCost + (currentNode.Position - neighbor.Position).Magnitude();

                        // Diagonal movement costs more
                        if (Math.Abs(currentNode.Position.X - neighbor.Position.X) > 0.01f &&
                            Math.Abs(currentNode.Position.Z - neighbor.Position.Z) > 0.01f)
                        {
                            newCost *= DiagonalWeight;
                        }

                        bool inOpenSet = backwardAllNodes.TryGetValue(neighbor.Position, out AStarNode existingNode) &&
                                        backwardOpenSet.Contains(existingNode);

                        if (!inOpenSet || newCost < existingNode.GCost)
                        {
                            if (!inOpenSet)
                            {
                                neighbor.GCost = newCost;
                                neighbor.HCost = CalculateHeuristic(neighbor.Position, start);
                                neighbor.Parent = currentNode;

                                CalculateProximityPenalties(neighbor);

                                backwardOpenSet.Add(neighbor);
                                backwardAllNodes[neighbor.Position] = neighbor;
                            }
                            else
                            {
                                backwardOpenSet.Remove(existingNode);
                                existingNode.GCost = newCost;
                                existingNode.Parent = currentNode;
                                backwardOpenSet.Add(existingNode);
                            }
                        }
                    }
                }

                // Check if we have a good meeting point
                if (meetingNode != null &&
                    (forwardOpenSet.Count == 0 || forwardOpenSet.Min.FCost >= bestMeetingCost) &&
                    (backwardOpenSet.Count == 0 || backwardOpenSet.Min.FCost >= bestMeetingCost))
                {
                    // Found optimal bidirectional path
                    break;
                }
            }

            if (meetingNode != null)
            {
                logger.LogMessage($"Bidirectional path found in {iterations} iterations");

                // Construct forward half
                List<Vector2> forwardPath = RetracePath(startNode, meetingNode);

                // Construct backward half
                List<Vector2> backwardPath = RetracePath(endNode, backwardAllNodes[meetingNode.Position]);
                backwardPath.Reverse(); // Reverse since we need start to end

                // Remove duplicate meeting point
                backwardPath.RemoveAt(0);

                // Combine paths
                forwardPath.AddRange(backwardPath);
                return forwardPath;
            }

            // Fallback to standard A* if bidirectional failed
            logger.LogMessage("Parallel search failed, fallback to standard A*");
            return AStarSearch(start, end);
        }

        /// <summary>
        /// Calculate heuristic value for A* algorithm
        /// </summary>
        private float CalculateHeuristic(Vector2 position, Vector2 target)
        {
            // Use weighted Manhattan + Euclidean combined heuristic
            float dx = Math.Abs(position.X - target.X);
            float dz = Math.Abs(position.Z - target.Z);
            float manhattan = dx + dz;
            float euclidean = (float)Math.Sqrt(dx * dx + dz * dz);

            // Weighted combination of heuristics
            return HeuristicWeight * (0.5f * manhattan + 0.5f * euclidean);
        }

        /// <summary>
        /// Calculate proximity penalties for nodes near walls or blacklisted areas
        /// </summary>
        private void CalculateProximityPenalties(AStarNode node)
        {
            // Apply penalties for nodes close to unwalkable cells (walls) or blacklisted areas
            (int gridX, int gridZ) = navigationGrid.WorldToGrid(node.Position);
            float wallPenalty = 0;
            float blacklistPenalty = 0;

            // Check surrounding cells
            int checkRadius = (int)(Globals.WallClearance / navigationGrid.CellSize);
            for (int dx = -checkRadius; dx <= checkRadius; dx++)
            {
                for (int dz = -checkRadius; dz <= checkRadius; dz++)
                {
                    if (dx == 0 && dz == 0) continue; // Skip the node itself

                    int neighborX = gridX + dx;
                    int neighborZ = gridZ + dz;

                    if (navigationGrid.IsValidCoordinate(neighborX, neighborZ))
                    {
                        // Calculate distance to this cell
                        float distance = (float)Math.Sqrt(dx * dx + dz * dz) * navigationGrid.CellSize;

                        // Add wall penalty
                        if (!navigationGrid.IsWalkable(neighborX, neighborZ) && distance < Globals.WallClearance)
                        {
                            // Exponentially increase penalty as we get closer to the wall
                            wallPenalty += (float)Math.Pow(Globals.WallClearance - distance, 2) * 0.5f;
                        }

                        // Add blacklist penalty (much higher to strongly avoid these areas)
                        if (navigationGrid.IsBlacklisted(neighborX, neighborZ))
                        {
                            // Severe penalty for blacklisted areas, especially if close
                            blacklistPenalty += (Globals.WallClearance * 2.0f - Math.Min(distance, Globals.WallClearance * 2.0f))
                                              * BlacklistAvoidanceWeight;
                        }
                    }
                }
            }

            node.WallPenalty = wallPenalty;
            node.BlacklistPenalty = blacklistPenalty;
        }

        /// <summary>
        /// Get neighboring nodes for A* algorithm
        /// </summary>
        private List<AStarNode> GetNeighbors(AStarNode node)
        {
            List<AStarNode> neighbors = new List<AStarNode>();
            (int nodeGridX, int nodeGridZ) = navigationGrid.WorldToGrid(node.Position);

            // Enhanced neighbor search with 8 directions
            int[] dx = { 0, 1, 1, 1, 0, -1, -1, -1 }; // Ordered clockwise from N
            int[] dz = { -1, -1, 0, 1, 1, 1, 0, -1 };

            for (int i = 0; i < 8; i++)
            {
                int checkX = nodeGridX + dx[i];
                int checkZ = nodeGridZ + dz[i];

                if (!navigationGrid.IsValidCoordinate(checkX, checkZ) ||
                    !navigationGrid.IsWalkable(checkX, checkZ) ||
                    navigationGrid.IsBlacklisted(checkX, checkZ))
                {
                    continue;
                }

                // For diagonal movement, ensure both cardinal directions are walkable
                if (i % 2 == 1) // Diagonals are at odd indices in the dx/dz arrays
                {
                    int cardinal1X = nodeGridX + dx[(i - 1) % 8];
                    int cardinal1Z = nodeGridZ + dz[(i - 1) % 8];
                    int cardinal2X = nodeGridX + dx[(i + 1) % 8];
                    int cardinal2Z = nodeGridZ + dz[(i + 1) % 8];

                    bool cardinal1Walkable = navigationGrid.IsValidCoordinate(cardinal1X, cardinal1Z) &&
                                           navigationGrid.IsWalkable(cardinal1X, cardinal1Z) &&
                                           !navigationGrid.IsBlacklisted(cardinal1X, cardinal1Z);

                    bool cardinal2Walkable = navigationGrid.IsValidCoordinate(cardinal2X, cardinal2Z) &&
                                           navigationGrid.IsWalkable(cardinal2X, cardinal2Z) &&
                                           !navigationGrid.IsBlacklisted(cardinal2X, cardinal2Z);

                    // Only allow diagonal if both cardinal directions are walkable
                    if (!cardinal1Walkable || !cardinal2Walkable)
                        continue;
                }

                Vector2 neighborPos = navigationGrid.GridToWorld(checkX, checkZ);
                neighbors.Add(new AStarNode(neighborPos));
            }

            return neighbors;
        }

        /// <summary>
        /// Retrace the path from end to start to return the full path
        /// </summary>
        private List<Vector2> RetracePath(AStarNode startNode, AStarNode endNode)
        {
            List<Vector2> path = new List<Vector2>();
            AStarNode currentNode = endNode;

            while (currentNode != null && !currentNode.Position.Equals(startNode.Position))
            {
                path.Add(currentNode.Position);
                currentNode = currentNode.Parent;
            }

            path.Add(startNode.Position);
            path.Reverse();
            return path;
        }

        /// <summary>
        /// Clears the path cache
        /// </summary>
        public void ClearCache()
        {
            pathCache.Clear();
            cacheOrder.Clear();
            logger.LogMessage("Path cache cleared");
        }
    }
}