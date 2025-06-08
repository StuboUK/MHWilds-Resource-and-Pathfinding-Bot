using System;
using System.IO;
using System.Threading;
using System.Drawing;
using MHWildsPathfindingBot.Core.Interfaces;
using MHWildsPathfindingBot.Core.Models;

namespace MHWildsPathfindingBot.Core.Utils
{
    public class FileManager : IFileManager
    {
        private readonly ILogger logger;
        private const int FileReadRetries = 3;
        private const int FileReadRetryDelay = 100; // ms

        public FileManager(ILogger logger)
        {
            this.logger = logger;
        }

        /// <summary>
        /// Attempts to read the position file with retries
        /// </summary>
        public string ReadPositionFile(string filePath)
        {
            for (int attempt = 0; attempt < FileReadRetries; attempt++)
            {
                try
                {
                    return File.ReadAllText(filePath);
                }
                catch (IOException)
                {
                    // File might be in use, retry after delay
                    Thread.Sleep(FileReadRetryDelay);
                }
                catch (Exception ex)
                {
                    // Log other exceptions
                    if (attempt == FileReadRetries - 1)
                    {
                        logger.LogMessage($"Error reading position file after {FileReadRetries} attempts: {ex.Message}", Color.Red);
                    }
                    return string.Empty;
                }
            }
            return string.Empty;
        }

        /// <summary>
        /// Checks if walkable grid file exists
        /// </summary>
        public bool WalkableGridExists()
        {
            return File.Exists("walkable_grid.dat") && File.Exists("walkable_grid.meta");
        }

        /// <summary>
        /// Checks if blacklist grid file exists
        /// </summary>
        public bool BlacklistGridExists()
        {
            return File.Exists("blacklist_grid.dat") && File.Exists("blacklist_grid.meta");
        }

        /// <summary>
        /// Loads walkable grid from file
        /// </summary>
        public (bool[,] grid, bool success, float originX, float originZ, int sizeX, int sizeZ)
            LoadWalkableGrid(int gridSizeX, int gridSizeZ, float originX, float originZ, float cellSize)
        {
            try
            {
                if (!WalkableGridExists())
                    return (new bool[gridSizeX, gridSizeZ], false, originX, originZ, gridSizeX, gridSizeZ);

                // Read metadata first
                string[] metaLines = File.ReadAllLines("walkable_grid.meta");
                float loadedOriginX = originX;
                float loadedOriginZ = originZ;
                int loadedSizeX = gridSizeX;
                int loadedSizeZ = gridSizeZ;
                float loadedCellSize = cellSize;

                foreach (string line in metaLines)
                {
                    string[] parts = line.Split('=');
                    if (parts.Length != 2) continue;

                    string key = parts[0].Trim();
                    string value = parts[1].Trim();

                    switch (key)
                    {
                        case "OriginX":
                            float.TryParse(value, out loadedOriginX);
                            break;
                        case "OriginZ":
                            float.TryParse(value, out loadedOriginZ);
                            break;
                        case "GridSizeX":
                            int.TryParse(value, out loadedSizeX);
                            break;
                        case "GridSizeZ":
                            int.TryParse(value, out loadedSizeZ);
                            break;
                        case "CellSize":
                            float.TryParse(value, out loadedCellSize);
                            break;
                    }
                }

                // Check for cell size mismatch
                if (Math.Abs(loadedCellSize - cellSize) > 0.001f)
                {
                    logger.LogMessage($"Cell size mismatch: Loaded={loadedCellSize}, Current={cellSize}", Color.Yellow);
                    // We'll continue anyway, but this could cause issues
                }

                // Now read the grid data
                bool[,] grid = new bool[loadedSizeX, loadedSizeZ];

                using (FileStream fs = new FileStream("walkable_grid.dat", FileMode.Open))
                using (BinaryReader reader = new BinaryReader(fs))
                {
                    for (int x = 0; x < loadedSizeX; x++)
                    {
                        for (int z = 0; z < loadedSizeZ; z++)
                        {
                            if (reader.BaseStream.Position < reader.BaseStream.Length)
                            {
                                grid[x, z] = reader.ReadBoolean();
                            }
                        }
                    }
                }

                logger.LogMessage($"Loaded walkable grid: {loadedSizeX}x{loadedSizeZ}, Origin: ({loadedOriginX},{loadedOriginZ})");
                return (grid, true, loadedOriginX, loadedOriginZ, loadedSizeX, loadedSizeZ);
            }
            catch (Exception ex)
            {
                logger.LogMessage($"Error loading walkable grid: {ex.Message}", Color.Red);
                return (new bool[gridSizeX, gridSizeZ], false, originX, originZ, gridSizeX, gridSizeZ);
            }
        }

        /// <summary>
        /// Saves walkable grid to file
        /// </summary>
        public void SaveWalkableGrid(bool[,] grid, int gridSizeX, int gridSizeZ, float originX, float originZ, float cellSize)
        {
            try
            {
                // Save the grid data first
                using (FileStream fs = new FileStream("walkable_grid.dat", FileMode.Create))
                using (BinaryWriter writer = new BinaryWriter(fs))
                {
                    for (int x = 0; x < gridSizeX; x++)
                    {
                        for (int z = 0; z < gridSizeZ; z++)
                        {
                            writer.Write(grid[x, z]);
                        }
                    }
                }

                // Save the metadata
                using (StreamWriter writer = new StreamWriter("walkable_grid.meta"))
                {
                    writer.WriteLine($"OriginX={originX}");
                    writer.WriteLine($"OriginZ={originZ}");
                    writer.WriteLine($"GridSizeX={gridSizeX}");
                    writer.WriteLine($"GridSizeZ={gridSizeZ}");
                    writer.WriteLine($"CellSize={cellSize}");
                    writer.WriteLine($"Timestamp={DateTime.Now}");
                }

                logger.LogMessage($"Saved walkable grid: {gridSizeX}x{gridSizeZ}");
            }
            catch (Exception ex)
            {
                logger.LogMessage($"Error saving walkable grid: {ex.Message}", Color.Red);
            }
        }

        /// <summary>
        /// Loads blacklist grid from file
        /// </summary>
        public (bool[,] grid, bool success, float originX, float originZ, int sizeX, int sizeZ)
            LoadBlacklistGrid(int gridSizeX, int gridSizeZ, float originX, float originZ, float cellSize)
        {
            try
            {
                if (!BlacklistGridExists())
                    return (new bool[gridSizeX, gridSizeZ], false, originX, originZ, gridSizeX, gridSizeZ);

                // Read metadata
                string[] metaLines = File.ReadAllLines("blacklist_grid.meta");
                float loadedOriginX = originX;
                float loadedOriginZ = originZ;
                int loadedSizeX = gridSizeX;
                int loadedSizeZ = gridSizeZ;

                foreach (string line in metaLines)
                {
                    string[] parts = line.Split('=');
                    if (parts.Length != 2) continue;

                    string key = parts[0].Trim();
                    string value = parts[1].Trim();

                    if (key == "OriginX") float.TryParse(value, out loadedOriginX);
                    else if (key == "OriginZ") float.TryParse(value, out loadedOriginZ);
                    else if (key == "GridSizeX") int.TryParse(value, out loadedSizeX);
                    else if (key == "GridSizeZ") int.TryParse(value, out loadedSizeZ);
                }

                // Read grid data
                bool[,] grid = new bool[loadedSizeX, loadedSizeZ];

                using (FileStream fs = new FileStream("blacklist_grid.dat", FileMode.Open))
                using (BinaryReader reader = new BinaryReader(fs))
                {
                    for (int x = 0; x < loadedSizeX; x++)
                    {
                        for (int z = 0; z < loadedSizeZ; z++)
                        {
                            if (reader.BaseStream.Position < reader.BaseStream.Length)
                            {
                                grid[x, z] = reader.ReadBoolean();
                            }
                        }
                    }
                }

                return (grid, true, loadedOriginX, loadedOriginZ, loadedSizeX, loadedSizeZ);
            }
            catch (Exception ex)
            {
                logger.LogMessage($"Error loading blacklist grid: {ex.Message}", Color.Red);
                return (new bool[gridSizeX, gridSizeZ], false, originX, originZ, gridSizeX, gridSizeZ);
            }
        }

        /// <summary>
        /// Saves blacklist grid to file
        /// </summary>
        public void SaveBlacklistGrid(bool[,] grid, int gridSizeX, int gridSizeZ, float originX, float originZ, float cellSize)
        {
            try
            {
                // Save grid data
                using (FileStream fs = new FileStream("blacklist_grid.dat", FileMode.Create))
                using (BinaryWriter writer = new BinaryWriter(fs))
                {
                    for (int x = 0; x < gridSizeX; x++)
                    {
                        for (int z = 0; z < gridSizeZ; z++)
                        {
                            writer.Write(grid[x, z]);
                        }
                    }
                }

                // Save metadata
                using (StreamWriter writer = new StreamWriter("blacklist_grid.meta"))
                {
                    writer.WriteLine($"OriginX={originX}");
                    writer.WriteLine($"OriginZ={originZ}");
                    writer.WriteLine($"GridSizeX={gridSizeX}");
                    writer.WriteLine($"GridSizeZ={gridSizeZ}");
                    writer.WriteLine($"CellSize={cellSize}");
                    writer.WriteLine($"Timestamp={DateTime.Now}");
                }
            }
            catch (Exception ex)
            {
                logger.LogMessage($"Error saving blacklist grid: {ex.Message}", Color.Red);
            }
        }

        /// <summary>
        /// Saves waypoints to JSON file
        /// </summary>
        public void SaveWaypoints(string json, string filePath)
        {
            try
            {
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                logger.LogMessage($"Error saving waypoints: {ex.Message}", Color.Red);
            }
        }

        /// <summary>
        /// Loads waypoints from JSON file
        /// </summary>
        public string LoadWaypoints(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    return File.ReadAllText(filePath);
                }
            }
            catch (Exception ex)
            {
                logger.LogMessage($"Error loading waypoints: {ex.Message}", Color.Red);
            }
            return string.Empty;
        }
    }
}