using System;

namespace MHWildsPathfindingBot.Core.Interfaces
{
    public interface IFileManager
    {
        string ReadPositionFile(string filePath);

        bool WalkableGridExists();
        bool BlacklistGridExists();

        (bool[,] grid, bool success, float originX, float originZ, int sizeX, int sizeZ)
            LoadWalkableGrid(int gridSizeX, int gridSizeZ, float originX, float originZ, float cellSize);

        void SaveWalkableGrid(bool[,] grid, int gridSizeX, int gridSizeZ, float originX, float originZ, float cellSize);

        (bool[,] grid, bool success, float originX, float originZ, int sizeX, int sizeZ)
            LoadBlacklistGrid(int gridSizeX, int gridSizeZ, float originX, float originZ, float cellSize);

        void SaveBlacklistGrid(bool[,] grid, int gridSizeX, int gridSizeZ, float originX, float originZ, float cellSize);

        void SaveWaypoints(string json, string filePath);
        string LoadWaypoints(string filePath);
    }
}