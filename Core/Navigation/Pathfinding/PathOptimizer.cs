using System;
using System.Collections.Generic;
using MHWildsPathfindingBot.Core.Models;
using MHWildsPathfindingBot.Navigation;

namespace MHWildsPathfindingBot.Navigation.Pathfinding
{
    /// <summary>
    /// Path optimizer for smoothing and improving paths
    /// </summary>
    public class PathOptimizer
    {
        private readonly NavigationGrid navigationGrid;

        public PathOptimizer(NavigationGrid navigationGrid)
        {
            this.navigationGrid = navigationGrid;
        }

        /// <summary>
        /// Adjust path waypoints to keep away from walls and obstacles
        /// </summary>
        public void OptimizePathForWallAvoidance(List<Vector2> path)
        {
            if (path.Count <= 2)
                return;

            // Analyze the path and adjust waypoints to move away from walls
            for (int i = 1; i < path.Count - 1; i++)
            {
                Vector2 point = path[i];
                (int gridX, int gridZ) = navigationGrid.WorldToGrid(point);

                // Check for nearby unwalkable cells or blacklisted areas
                bool hasNearbyWall = false;
                Vector2 awayFromWallDirection = new Vector2(0, 0);

                int checkRadius = (int)(Globals.WallClearance / navigationGrid.CellSize);
                for (int dx = -checkRadius; dx <= checkRadius; dx++)
                {
                    for (int dz = -checkRadius; dz <= checkRadius; dz++)
                    {
                        // Skip checking the point itself
                        if (dx == 0 && dz == 0) continue;

                        int neighborX = gridX + dx;
                        int neighborZ = gridZ + dz;

                        // If we found an unwalkable cell or blacklisted area
                        if (navigationGrid.IsValidCoordinate(neighborX, neighborZ) &&
                            (!navigationGrid.IsWalkable(neighborX, neighborZ) || navigationGrid.IsBlacklisted(neighborX, neighborZ)))
                        {
                            hasNearbyWall = true;

                            // Calculate vector away from the wall
                            float weight = 1.0f / (dx * dx + dz * dz); // Weight by inverse square distance
                            Vector2 awayVector = new Vector2(-dx, -dz).Normalized() * weight;
                            awayFromWallDirection.X += awayVector.X;
                            awayFromWallDirection.Z += awayVector.Z;
                        }
                    }
                }

                // If we have a wall nearby, adjust the waypoint to move away from it
                if (hasNearbyWall && awayFromWallDirection.Magnitude() > 0.001f)
                {
                    // Normalize the away direction
                    awayFromWallDirection = awayFromWallDirection.Normalized();

                    // Calculate new position (moved away from wall)
                    Vector2 newPos = new Vector2(
                        point.X + awayFromWallDirection.X * Globals.WallClearance,
                        point.Z + awayFromWallDirection.Z * Globals.WallClearance
                    );

                    // Check if the new position is walkable and not blacklisted
                    (int newGridX, int newGridZ) = navigationGrid.WorldToGrid(newPos);
                    if (navigationGrid.IsValidCoordinate(newGridX, newGridZ) &&
                        navigationGrid.IsWalkable(newGridX, newGridZ) &&
                        !navigationGrid.IsBlacklisted(newGridX, newGridZ))
                    {
                        // Update the path point
                        path[i] = newPos;
                    }
                }
            }
        }

        /// <summary>
        /// Simplify the path by removing unnecessary waypoints
        /// </summary>
        public void SimplifyPath(List<Vector2> path)
        {
            if (path.Count <= 3)
                return;

            // More aggressive simplification approach that removes more unnecessary points
            List<Vector2> simplified = new List<Vector2> { path[0] };
            float minAngleForKeeping = 40.0f; // Higher angle threshold (was 20.0f)
            float minDistanceForKeeping = 5.0f; // Only keep points that are at least this far apart

            Vector2 lastKeptPoint = path[0];

            for (int i = 1; i < path.Count - 1; i++)
            {
                Vector2 prev = path[i - 1];
                Vector2 current = path[i];
                Vector2 next = path[i + 1];

                // Calculate direction vectors
                Vector2 dirFromPrev = new Vector2(current.X - prev.X, current.Z - prev.Z).Normalized();
                Vector2 dirToNext = new Vector2(next.X - current.X, next.Z - current.Z).Normalized();

                // Calculate angle between vectors (in degrees)
                float dotProduct = Vector2.DotProduct(dirFromPrev, dirToNext);
                dotProduct = Math.Max(-1.0f, Math.Min(1.0f, dotProduct)); // Clamp to avoid precision issues
                float angleDegrees = (float)(Math.Acos(dotProduct) * 180.0 / Math.PI);

                // Distance from last kept point
                float distanceFromLast = (current - lastKeptPoint).Magnitude();

                // Keep point if significant angle change or sufficient distance from last point
                bool keepDueToAngle = angleDegrees > minAngleForKeeping;
                bool keepDueToDistance = distanceFromLast > minDistanceForKeeping;

                if (keepDueToAngle || keepDueToDistance)
                {
                    simplified.Add(current);
                    lastKeptPoint = current;
                }
            }

            // Always keep the final point
            if (!simplified.Contains(path[path.Count - 1]))
                simplified.Add(path[path.Count - 1]);

            // Replace original path with simplified path
            path.Clear();
            path.AddRange(simplified);
        }

        /// <summary>
        /// Smooth path using spline interpolation
        /// </summary>
        public List<Vector2> SmoothPath(List<Vector2> originalPath)
        {
            if (originalPath.Count <= 2)
                return new List<Vector2>(originalPath);

            List<Vector2> smoothedPath = new List<Vector2>();
            smoothedPath.Add(originalPath[0]); // Keep start point

            // Use a more aggressive corner-cutting approach for smoother paths
            if (originalPath.Count > 3)
            {
                // First pass: corner cutting
                List<Vector2> midpoints = new List<Vector2>();
                midpoints.Add(originalPath[0]);

                for (int i = 0; i < originalPath.Count - 1; i++)
                {
                    Vector2 current = originalPath[i];
                    Vector2 next = originalPath[i + 1];

                    // Add a point 70% of the way from current to next
                    Vector2 seventyPercentPoint = new Vector2(
                        current.X + (next.X - current.X) * 0.7f,
                        current.Z + (next.Z - current.Z) * 0.7f
                    );

                    // Make sure the point is walkable
                    (int gridX, int gridZ) = navigationGrid.WorldToGrid(seventyPercentPoint);
                    if (navigationGrid.IsValidCoordinate(gridX, gridZ) &&
                        navigationGrid.IsWalkable(gridX, gridZ) &&
                        !navigationGrid.IsBlacklisted(gridX, gridZ))
                    {
                        midpoints.Add(seventyPercentPoint);
                    }
                    else
                    {
                        midpoints.Add(next);
                    }
                }

                midpoints.Add(originalPath[originalPath.Count - 1]);

                // Second pass: spline smoothing with more points on longer segments
                for (int i = 0; i < midpoints.Count - 1; i++)
                {
                    Vector2 p0 = i > 0 ? midpoints[i - 1] : midpoints[i];
                    Vector2 p1 = midpoints[i];
                    Vector2 p2 = midpoints[i + 1];
                    Vector2 p3 = (i + 2 < midpoints.Count) ? midpoints[i + 2] : p2;

                    // Only add intermediate points for longer segments
                    float segmentLength = (p2 - p1).Magnitude();
                    int steps = Math.Min((int)(segmentLength / 2.0f), 4); // More steps for smoother curves

                    if (segmentLength > 3.0f && steps > 0)
                    {
                        for (int step = 0; step <= steps; step++)
                        {
                            float t = step / (float)(steps + 1);

                            // Catmull-Rom spline calculation
                            float t2 = t * t;
                            float t3 = t2 * t;

                            Vector2 point = new Vector2(
                                0.5f * ((2 * p1.X) + (-p0.X + p2.X) * t +
                                (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 +
                                (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3),

                                0.5f * ((2 * p1.Z) + (-p0.Z + p2.Z) * t +
                                (2 * p0.Z - 5 * p1.Z + 4 * p2.Z - p3.Z) * t2 +
                                (-p0.Z + 3 * p1.Z - 3 * p2.Z + p3.Z) * t3)
                            );

                            // Verify point is walkable and not blacklisted
                            (int gridX, int gridZ) = navigationGrid.WorldToGrid(point);
                            if (navigationGrid.IsValidCoordinate(gridX, gridZ) &&
                                navigationGrid.IsWalkable(gridX, gridZ) &&
                                !navigationGrid.IsBlacklisted(gridX, gridZ))
                            {
                                smoothedPath.Add(point);
                            }
                        }
                    }

                    // Add the endpoint if it's not the last iteration
                    if (i < midpoints.Count - 2)
                        smoothedPath.Add(p2);
                }
            }
            else
            {
                // For very short paths, just use the original points
                for (int i = 1; i < originalPath.Count - 1; i++)
                    smoothedPath.Add(originalPath[i]);
            }

            // Always add the final destination point
            smoothedPath.Add(originalPath[originalPath.Count - 1]);

            return smoothedPath;
        }
    }
}