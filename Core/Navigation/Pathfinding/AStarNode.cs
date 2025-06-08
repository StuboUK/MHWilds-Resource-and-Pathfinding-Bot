using System;
using MHWildsPathfindingBot.Core.Models;

namespace MHWildsPathfindingBot.Navigation.Pathfinding
{
    /// <summary>
    /// Node class for A* algorithm
    /// </summary>
    public class AStarNode : IComparable<AStarNode>
    {
        public Vector2 Position { get; }
        public AStarNode Parent { get; set; }
        public float GCost { get; set; }
        public float HCost { get; set; }
        public float FCost => GCost + HCost;
        public float WallPenalty { get; set; } = 0; // Higher values for nodes near walls
        public float BlacklistPenalty { get; set; } = 0; // Penalty for proximity to blacklisted areas

        public AStarNode(Vector2 position, AStarNode parent = null)
        {
            Position = position;
            Parent = parent;
            GCost = HCost = 0;
        }

        public int CompareTo(AStarNode other)
        {
            if (other == null) return 1;

            float myCost = FCost + WallPenalty + BlacklistPenalty;
            float otherCost = other.FCost + other.WallPenalty + other.BlacklistPenalty;

            int fCompare = myCost.CompareTo(otherCost);
            return fCompare == 0 ? HCost.CompareTo(other.HCost) : fCompare;
        }
    }
}