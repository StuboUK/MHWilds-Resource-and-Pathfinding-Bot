using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using MHWildsPathfindingBot.Bot;
using MHWildsPathfindingBot.Core.Models;
using MHWildsPathfindingBot.Navigation;

namespace MHWildsPathfindingBot.UI
{
    public class PathVisualizer : Panel
    {
        private List<Vector2> path;
        private Vector2 playerPosition;
        private Vector2 targetPosition;
        private List<(Vector2 center, float radius)> obstacles = new List<(Vector2 center, float radius)>();

        // View settings
        private float zoomLevel = 0.7f; // Start with a more appropriate zoom level
        private PointF viewCenter;
        private bool dragging = false;
        private Point lastMousePos;

        // Painting settings
        private bool isPainting = false;
        private bool isErasing = false;
        private int paintRadius = 3;
        private NavigationGrid navigationGrid;

        // Grid coordinate system info
        private float gridOriginX = 0;
        private float gridOriginZ = 0;
        private float gridCellSize = 0.5f;

        // Display parameters - reduced sizes
        private const float MapScale = 0.5f;
        private float PathDotSize = 1.5f;
        private float PlayerDotSize = 3.0f;
        private float TargetDotSize = 4.0f;
        private float PathLineWidth = 0.8f;

        // Grid visualization - SET TO TRUE BY DEFAULT FOR VISIBILITY
        public bool ShowWalkableGrid { get; set; } = true;
        public bool ShowBlacklistGrid { get; set; } = true;
        public Color GridColor { get; set; } = Color.FromArgb(45, 45, 50);

        public Color WalkableColor { get; set; } = Color.FromArgb(120, 0, 150, 80);
        public Color BlacklistColor { get; set; } = Color.FromArgb(70, 170, 30, 30);
        private bool[,] walkableGrid = new bool[0, 0];
        private bool[,] blacklistGrid = new bool[0, 0];
        private int gridSizeX = 0, gridSizeZ = 0;

        // Waypoint visualization - smaller sizes
        private WaypointManager waypointManager;
        public bool ShowWaypoints { get; set; } = true;
        private float WaypointSize = 4.0f;
        private float ResourceNodeSize = 5.0f;

        // Performance settings
        private bool useGridCulling = true;
        private int maxGridCellsToRender = 5000;
        private DateTime lastFullRenderTime = DateTime.MinValue;

        private Timer refreshTimer;

        public PathVisualizer()
        {
            this.DoubleBuffered = true;
            playerPosition = new Vector2(0, 0);
            targetPosition = new Vector2(0, 0);
            viewCenter = new PointF(0, 0);
            zoomLevel = 0.4f; // Start with a wider zoom level to see more of the map
            ShowWalkableGrid = true; // Ensure grid is visible by default
            ShowBlacklistGrid = true;


            // Mouse event handlers
            this.MouseDown += (s, e) => {
                Console.WriteLine($"Mouse down at {e.Location}, button: {e.Button}");
                // Only start dragging with left click if it's not a double-click
                if (e.Button == MouseButtons.Left && e.Clicks == 1)
                {
                    dragging = true;
                    lastMousePos = e.Location;
                }
                // Right click for painting
                else if (e.Button == MouseButtons.Right && navigationGrid != null)
                {
                    Console.WriteLine("Starting to paint at: " + e.Location);
                    isPainting = true;
                    PaintAtMouse(e.Location, true);
                }
                // Middle button for erasing
                else if (e.Button == MouseButtons.Middle && navigationGrid != null)
                {
                    isErasing = true;
                    PaintAtMouse(e.Location, false);
                }
            };

            this.MouseMove += (s, e) => {
                if (dragging)
                {
                    float deltaX = e.X - lastMousePos.X;
                    float deltaY = e.Y - lastMousePos.Y;
                    float worldDeltaX = -deltaX / (MapScale * zoomLevel);
                    float worldDeltaY = -deltaY / (MapScale * zoomLevel);
                    viewCenter.X += worldDeltaX;
                    viewCenter.Y += worldDeltaY;
                    lastMousePos = e.Location;
                    this.Invalidate();
                }
                else if (isPainting && navigationGrid != null)
                {
                    PaintAtMouse(e.Location, true);
                }
                else if (isErasing && navigationGrid != null)
                {
                    PaintAtMouse(e.Location, false);
                }
            };

            this.MouseUp += (s, e) => {
                if (e.Button == MouseButtons.Left)
                {
                    dragging = false;
                }
                else if (e.Button == MouseButtons.Right)
                {
                    isPainting = false;
                    if (navigationGrid != null)
                        navigationGrid.SaveWalkableGrid();
                }
                else if (e.Button == MouseButtons.Middle)
                {
                    isErasing = false;
                    if (navigationGrid != null)
                        navigationGrid.SaveWalkableGrid();
                }
            };

            this.MouseWheel += (s, e) => {
                float oldZoom = zoomLevel;
                if (e.Delta > 0)
                    zoomLevel *= 1.1f;
                else
                    zoomLevel *= 0.9f;

                zoomLevel = Math.Max(0.1f, Math.Min(10.0f, zoomLevel));

                // Adjust view center to zoom toward mouse position
                if (zoomLevel != oldZoom)
                {
                    Point mousePos = e.Location;
                    float centerX = this.ClientSize.Width / 2;
                    float centerY = this.ClientSize.Height / 2;
                    float mouseOffsetX = (mousePos.X - centerX) / (MapScale * oldZoom);
                    float mouseOffsetY = (mousePos.Y - centerY) / (MapScale * oldZoom);
                    float newOffsetX = mouseOffsetX * (1 - zoomLevel / oldZoom);
                    float newOffsetY = mouseOffsetY * (1 - zoomLevel / oldZoom);
                    viewCenter.X += newOffsetX;
                    viewCenter.Y += newOffsetY;
                }

                this.Invalidate();
            };

            refreshTimer = new Timer();
            refreshTimer.Interval = 100;
            refreshTimer.Tick += (s, e) => this.Invalidate();
            refreshTimer.Start();
        }

        /// <summary>
        /// Sets grid coordinate system information to ensure consistent coordinate conversion
        /// </summary>
        public void SetGridInfo(float originX, float originZ, float cellSize)
        {
            // Store grid coordinate system info
            this.gridOriginX = originX;
            this.gridOriginZ = originZ;
            this.gridCellSize = cellSize;

            Console.WriteLine($"Visualizer coordinate system: Origin=({originX}, {originZ}), CellSize={cellSize}");
        }

        public void SetElementSizes(float pathDotSize, float playerDotSize, float targetDotSize, float pathLineWidth)
        {
            PathDotSize = pathDotSize;
            PlayerDotSize = playerDotSize;
            TargetDotSize = targetDotSize;
            PathLineWidth = pathLineWidth;
            this.Invalidate();
        }

        public void SetNavigationGrid(NavigationGrid grid)
        {
            navigationGrid = grid;
            Console.WriteLine($"NavigationGrid set: {grid != null}");
        }


        public void SetWaypointManager(WaypointManager manager)
        {
            waypointManager = manager;
            this.Invalidate();
        }

        public void SetGrids(bool[,] walkable, bool[,] blacklist, int sizeX, int sizeZ)
        {
            // Check if grids are valid
            if (walkable == null || blacklist == null || sizeX <= 0 || sizeZ <= 0)
                return;

            walkableGrid = walkable;
            blacklistGrid = blacklist;
            gridSizeX = sizeX;
            gridSizeZ = sizeZ;

            Console.WriteLine($"PathVisualizer.SetGrids: size={sizeX}x{sizeZ}, walkable array size={walkable.GetLength(0)}x{walkable.GetLength(1)}");

            int walkableCount = 0;
            for (int x = 0; x < walkable.GetLength(0); x++)
                for (int z = 0; z < walkable.GetLength(1); z++)
                    if (walkable[x, z])
                        walkableCount++;

            Console.WriteLine($"PathVisualizer.SetGrids: Total walkable points: {walkableCount}");

            // Make sure grid visibility flags are set
            ShowWalkableGrid = true;
            ShowBlacklistGrid = true;

            // Force a refresh
            this.Invalidate();
        }

        public void UpdatePath(List<Vector2> newPath, Vector2 playerPos, Vector2 targetPos)
        {
            path = new List<Vector2>(newPath);
            playerPosition = playerPos;
            targetPosition = targetPos;

            if (viewCenter.X == 0 && viewCenter.Y == 0)
                viewCenter = new PointF(playerPos.X, playerPos.Z);

            this.Invalidate();
        }

        public void UpdatePlayerPosition(Vector2 playerPos)
        {
            playerPosition = playerPos;

            // Always center view on player to follow them
            viewCenter = new PointF(playerPos.X, playerPos.Z);

            // Force a redraw
            this.Invalidate();
        }


        public void AddObstacle(Vector2 position, float radius)
        {
            obstacles.Add((position, radius));
            this.Invalidate();
        }

        public void ClearObstacles()
        {
            obstacles.Clear();
            this.Invalidate();
        }

        private void PaintAtMouse(Point screenPos, bool isWalkable)
        {
            if (navigationGrid == null)
            {
                Console.WriteLine("Cannot paint: navigationGrid is null");
                return;
            }

            // Convert screen position to world position
            Vector2 worldPos = ScreenToWorld(screenPos);
            Console.WriteLine($"Paint at world: {worldPos}");

            // Convert world position to grid coordinates
            (int gridX, int gridZ) = navigationGrid.WorldToGrid(worldPos);
            Console.WriteLine($"Paint at grid: ({gridX},{gridZ})");

            // Ensure valid coordinates
            if (gridX < 0 || gridX >= navigationGrid.GridSizeX ||
                gridZ < 0 || gridZ >= navigationGrid.GridSizeZ)
            {
                Console.WriteLine("Grid coordinates out of bounds");
                return;
            }

            // Paint a circle around this point
            int painted = 0;
            for (int x = gridX - paintRadius; x <= gridX + paintRadius; x++)
            {
                for (int z = gridZ - paintRadius; z <= gridZ + paintRadius; z++)
                {
                    if (x >= 0 && x < navigationGrid.GridSizeX && z >= 0 && z < navigationGrid.GridSizeZ)
                    {
                        float dx = x - gridX;
                        float dz = z - gridZ;
                        if (dx * dx + dz * dz <= paintRadius * paintRadius)
                        {
                            if (isWalkable)
                                navigationGrid.MarkWalkableAreaAtCoord(x, z);
                            else
                                navigationGrid.ClearWalkableAreaAtCoord(x, z);
                            painted++;
                        }
                    }
                }
            }

            // Force grid update after painting
            walkableGrid = navigationGrid.GetWalkableGrid();
            blacklistGrid = navigationGrid.GetBlacklistGrid();
            Console.WriteLine($"Painted {painted} cells");

            // Force redraw
            this.Invalidate();
        }

        public Vector2 ScreenToWorld(Point screenPos)
        {
            float effectiveScale = MapScale * zoomLevel;
            int centerX = this.ClientSize.Width / 2;
            int centerY = this.ClientSize.Height / 2;

            // Use the current view center for screen conversion
            return new Vector2(
                viewCenter.X + (screenPos.X - centerX) / effectiveScale,
                viewCenter.Y + (screenPos.Y - centerY) / effectiveScale
            );
        }

        private PointF WorldToScreen(Vector2 worldPos)
        {
            float effectiveScale = MapScale * zoomLevel;
            int centerX = this.ClientSize.Width / 2;
            int centerY = this.ClientSize.Height / 2;

            // Use the current view center for screen conversion
            return new PointF(
                centerX + (worldPos.X - viewCenter.X) * effectiveScale,
                centerY + (worldPos.Z - viewCenter.Y) * effectiveScale
            );
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode == Keys.Add || e.KeyCode == Keys.Oemplus)
            {
                paintRadius = Math.Min(paintRadius + 1, 10);
                UpdateStatusBar();
            }
            else if (e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus)
            {
                paintRadius = Math.Max(paintRadius - 1, 1);
                UpdateStatusBar();
            }
        }

        private void UpdateStatusBar()
        {
            Form parentForm = this.FindForm();
            if (parentForm != null && parentForm.Controls.Count > 0)
            {
                foreach (Control c in parentForm.Controls)
                {
                    if (c is StatusStrip)
                    {
                        ((StatusStrip)c).Items[0].Text = $"Paint Radius: {paintRadius}";
                        break;
                    }
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.Clear(Color.FromArgb(25, 25, 28));
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float effectiveScale = MapScale * zoomLevel;
            int centerX = this.ClientSize.Width / 2;
            int centerY = this.ClientSize.Height / 2;

            // Draw grids if enabled and available
            if ((ShowWalkableGrid || ShowBlacklistGrid) && gridSizeX > 0 && gridSizeZ > 0)
            {
                // Convert screen boundaries to world coordinates
                float halfWidth = this.ClientSize.Width / (2 * effectiveScale);
                float halfHeight = this.ClientSize.Height / (2 * effectiveScale);

                Vector2 topLeft = new Vector2(viewCenter.X - halfWidth, viewCenter.Y - halfHeight);
                Vector2 bottomRight = new Vector2(viewCenter.X + halfWidth, viewCenter.Y + halfHeight);

                // Convert world coordinates to grid coordinates
                int startGridX, startGridZ, endGridX, endGridZ;

                if (navigationGrid != null)
                {
                    (startGridX, startGridZ) = navigationGrid.WorldToGrid(topLeft);
                    (endGridX, endGridZ) = navigationGrid.WorldToGrid(bottomRight);
                }
                else
                {
                    // Fallback if navigationGrid is not available
                    startGridX = (int)((topLeft.X - gridOriginX) / gridCellSize);
                    startGridZ = (int)((topLeft.Z - gridOriginZ) / gridCellSize);
                    endGridX = (int)((bottomRight.X - gridOriginX) / gridCellSize);
                    endGridZ = (int)((bottomRight.Z - gridOriginZ) / gridCellSize);
                }

                // Ensure coordinates are within bounds
                startGridX = Math.Max(0, startGridX);
                startGridZ = Math.Max(0, startGridZ);
                endGridX = Math.Min(gridSizeX - 1, endGridX);
                endGridZ = Math.Min(gridSizeZ - 1, endGridZ);

                // Calculate step size based on zoom level - skip cells when zoomed out
                int stepSize = Math.Max(1, (int)(0.8f / zoomLevel));
                int maxCellsToRender = 20000; // Limit total cells rendered
                int cellsRendered = 0;

                if (zoomLevel > 0.2f) // Only render grid cells when zoomed in enough
                {
                    using (SolidBrush walkableBrush = new SolidBrush(WalkableColor),
                                      blacklistBrush = new SolidBrush(BlacklistColor))
                    {
                        for (int x = startGridX; x <= endGridX; x += stepSize)
                        {
                            if (cellsRendered >= maxCellsToRender) break;

                            for (int z = startGridZ; z <= endGridZ; z += stepSize)
                            {
                                // Break if we hit the maximum cells to draw
                                if (cellsRendered >= maxCellsToRender)
                                    break;

                                // Check array bounds
                                if (x >= walkableGrid.GetLength(0) || z >= walkableGrid.GetLength(1))
                                    continue;

                                // Convert grid coordinates to world coordinates
                                Vector2 cellWorldPos;
                                if (navigationGrid != null)
                                {
                                    cellWorldPos = navigationGrid.GridToWorld(x, z);
                                }
                                else
                                {
                                    cellWorldPos = new Vector2(
                                        gridOriginX + x * gridCellSize,
                                        gridOriginZ + z * gridCellSize
                                    );
                                }

                                PointF screenPos = WorldToScreen(cellWorldPos);
                                float cellSize = gridCellSize * effectiveScale;

                                // Skip cells that would be too small to be visible
                                if (cellSize < 1.0f)
                                    continue;

                                // Draw walkable grid with increased opacity
                                if (ShowWalkableGrid && walkableGrid[x, z])
                                {
                                    float cellDisplaySize = cellSize * 0.8f;
                                    g.FillRectangle(walkableBrush,
                                        screenPos.X - cellDisplaySize / 2,
                                        screenPos.Y - cellDisplaySize / 2,
                                        cellDisplaySize,
                                        cellDisplaySize);
                                    cellsRendered++;
                                }

                                // Draw blacklist grid
                                if (ShowBlacklistGrid && blacklistGrid[x, z])
                                {
                                    g.FillRectangle(blacklistBrush, screenPos.X - cellSize / 2, screenPos.Y - cellSize / 2, cellSize, cellSize);
                                    cellsRendered++;
                                }
                            }
                        }
                    }
                }

                // Draw info about rendering
                using (Font infoFont = new Font("Segoe UI", 7f))
                using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(200, 200, 100)))
                using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
                {
                    string renderInfo = $"Rendering: {cellsRendered} cells, step:{stepSize}";
                    SizeF textSize = g.MeasureString(renderInfo, infoFont);
                    g.FillRectangle(bgBrush, 5, this.Height - 65, textSize.Width + 6, 16);
                    g.DrawString(renderInfo, infoFont, textBrush, 8, this.Height - 65);
                }
            }

            // Draw grid lines
            using (Pen gridPen = new Pen(GridColor, 1))
            {
                float gridSize = 10.0f;
                if (zoomLevel > 2.0f) gridSize = 5.0f;
                if (zoomLevel > 5.0f) gridSize = 1.0f;

                float halfWidth = this.ClientSize.Width / (2 * effectiveScale);
                float halfHeight = this.ClientSize.Height / (2 * effectiveScale);
                float minX = viewCenter.X - halfWidth;
                float maxX = viewCenter.X + halfWidth;
                float minY = viewCenter.Y - halfHeight;
                float maxY = viewCenter.Y + halfHeight;

                minX = (float)Math.Floor(minX / gridSize) * gridSize;
                maxX = (float)Math.Ceiling(maxX / gridSize) * gridSize;
                minY = (float)Math.Floor(minY / gridSize) * gridSize;
                maxY = (float)Math.Ceiling(maxY / gridSize) * gridSize;

                // Draw vertical grid lines
                for (float x = minX; x <= maxX; x += gridSize)
                {
                    PointF p1 = WorldToScreen(new Vector2(x, minY));
                    PointF p2 = WorldToScreen(new Vector2(x, maxY));
                    g.DrawLine(gridPen, p1, p2);
                }

                // Draw horizontal grid lines
                for (float y = minY; y <= maxY; y += gridSize)
                {
                    PointF p1 = WorldToScreen(new Vector2(minX, y));
                    PointF p2 = WorldToScreen(new Vector2(maxX, y));
                    g.DrawLine(gridPen, p1, p2);
                }

                // Draw origin with slightly thicker lines
                using (Pen originPen = new Pen(Color.FromArgb(80, 80, 90), 1.5f))
                {
                    PointF p1 = WorldToScreen(new Vector2(0, minY));
                    PointF p2 = WorldToScreen(new Vector2(0, maxY));
                    g.DrawLine(originPen, p1, p2);

                    p1 = WorldToScreen(new Vector2(minX, 0));
                    p2 = WorldToScreen(new Vector2(maxX, 0));
                    g.DrawLine(originPen, p1, p2);
                }
            }

            // Draw obstacles
            using (Brush obstacleBrush = new SolidBrush(Color.FromArgb(100, 255, 30, 30)))
            {
                foreach (var obstacle in obstacles)
                {
                    PointF center = WorldToScreen(obstacle.center);
                    float radius = obstacle.radius * effectiveScale;
                    g.FillEllipse(obstacleBrush, center.X - radius, center.Y - radius, radius * 2, radius * 2);
                }
            }

            // Draw the path if available
            if (path != null && path.Count > 0)
            {
                // Draw path lines
                if (path.Count > 1)
                {
                    using (Pen pathPen = new Pen(Color.FromArgb(180, 64, 158, 255), PathLineWidth * zoomLevel))
                    {
                        pathPen.StartCap = LineCap.Round;
                        pathPen.EndCap = LineCap.Round;

                        for (int i = 0; i < path.Count - 1; i++)
                        {
                            PointF p1 = WorldToScreen(path[i]);
                            PointF p2 = WorldToScreen(path[i + 1]);
                            g.DrawLine(pathPen, p1, p2);
                        }
                    }
                }

                // Draw path points
                using (Brush pathBrush = new SolidBrush(Color.FromArgb(180, 64, 158, 255)))
                {
                    foreach (var point in path)
                    {
                        PointF p = WorldToScreen(point);
                        float size = PathDotSize * zoomLevel;
                        g.FillEllipse(pathBrush, p.X - size / 2, p.Y - size / 2, size, size);
                    }
                }
            }

            // Draw waypoints
            if (ShowWaypoints && waypointManager != null)
            {
                DrawWaypoints(g);
            }

            // Draw player position
            PointF playerPos = WorldToScreen(playerPosition);
            float playerSize = PlayerDotSize * zoomLevel;

            // Draw player with smaller glow
            using (GraphicsPath circlePath = new GraphicsPath())
            {
                circlePath.AddEllipse(playerPos.X - playerSize, playerPos.Y - playerSize, playerSize * 2, playerSize * 2);

                using (PathGradientBrush glowBrush = new PathGradientBrush(circlePath))
                {
                    glowBrush.CenterColor = Color.FromArgb(100, 0, 200, 0);
                    glowBrush.SurroundColors = new Color[] { Color.FromArgb(0, 0, 200, 0) };
                    g.FillEllipse(glowBrush, playerPos.X - playerSize * 1.2f, playerPos.Y - playerSize * 1.2f, playerSize * 2.4f, playerSize * 2.4f);
                }
            }

            // Draw player dot
            using (Brush playerBrush = new SolidBrush(Color.LimeGreen))
            {
                g.FillEllipse(playerBrush, playerPos.X - playerSize / 2, playerPos.Y - playerSize / 2, playerSize, playerSize);
            }

            // Draw target position if it's not at origin
            if (targetPosition.X != 0 || targetPosition.Z != 0)
            {
                PointF targetPos = WorldToScreen(targetPosition);
                float targetSize = TargetDotSize * zoomLevel;

                // Draw target with smaller glow
                using (GraphicsPath targetPath = new GraphicsPath())
                {
                    targetPath.AddEllipse(targetPos.X - targetSize, targetPos.Y - targetSize, targetSize * 2, targetSize * 2);

                    using (PathGradientBrush glowBrush = new PathGradientBrush(targetPath))
                    {
                        glowBrush.CenterColor = Color.FromArgb(100, 200, 50, 50);
                        glowBrush.SurroundColors = new Color[] { Color.FromArgb(0, 200, 50, 50) };
                        g.FillEllipse(glowBrush, targetPos.X - targetSize * 1.2f, targetPos.Y - targetSize * 1.2f, targetSize * 2.4f, targetSize * 2.4f);
                    }
                }

                // Draw target dot
                using (Brush targetBrush = new SolidBrush(Color.Red))
                {
                    g.FillEllipse(targetBrush, targetPos.X - targetSize / 2, targetPos.Y - targetSize / 2, targetSize, targetSize);
                }
            }

            // Draw position info - smaller, semi-transparent panel
            using (Font infoFont = new Font("Segoe UI", 8f))
            using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(200, 200, 200)))
            using (SolidBrush panelBrush = new SolidBrush(Color.FromArgb(100, 20, 20, 25)))
            {
                // More compact player/target position display at top left
                string playerInfo = $"Player: {playerPosition}";
                string targetInfo = $"Target: {targetPosition}";
                SizeF playerSize1 = g.MeasureString(playerInfo, infoFont);
                SizeF targetSize1 = g.MeasureString(targetInfo, infoFont);
                float width = Math.Max(playerSize1.Width, targetSize1.Width) + 10;

                g.FillRectangle(panelBrush, 5, 5, width, 36);
                g.DrawString(playerInfo, infoFont, textBrush, 8, 6);
                g.DrawString(targetInfo, infoFont, textBrush, 8, 20);

                // Zoom indicator in bottom left corner
                g.FillRectangle(panelBrush, 5, this.ClientSize.Height - 22, 65, 18);
                g.DrawString($"Zoom: {zoomLevel:F1}x", infoFont, textBrush, 8, this.ClientSize.Height - 21);
            }

            // Add minimal paint info if navigation grid is available
            if (navigationGrid != null)
            {
                using (Font infoFont = new Font("Segoe UI", 8f))
                using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(190, 190, 190)))
                using (SolidBrush panelBrush = new SolidBrush(Color.FromArgb(100, 20, 20, 25)))
                {
                    string paintInfo = $"[RMB]: Paint | [MMB]: Erase (r:{paintRadius})";
                    SizeF textSize = g.MeasureString(paintInfo, infoFont);
                    g.FillRectangle(panelBrush, 5, this.ClientSize.Height - 45, textSize.Width + 6, 18);
                    g.DrawString(paintInfo, infoFont, textBrush, 8, this.ClientSize.Height - 44);
                }
            }

            // Add grid info for debugging
            using (Font infoFont = new Font("Segoe UI", 7f))
            using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(150, 150, 150)))
            using (SolidBrush panelBrush = new SolidBrush(Color.FromArgb(100, 20, 20, 25)))
            {
                string gridInfo = $"Grid: {gridSizeX}x{gridSizeZ} Origin: ({gridOriginX:F0},{gridOriginZ:F0}) CellSize:{gridCellSize}";
                SizeF textSize = g.MeasureString(gridInfo, infoFont);
                g.FillRectangle(panelBrush, 5, this.ClientSize.Height - 85, textSize.Width + 6, 15);
                g.DrawString(gridInfo, infoFont, textBrush, 8, this.ClientSize.Height - 84);
            }
        }

        private void DrawWaypoints(Graphics g)
        {
            if (!ShowWaypoints || waypointManager == null)
                return;

            float effectiveScale = MapScale * zoomLevel;
            int centerX = this.ClientSize.Width / 2;
            int centerY = this.ClientSize.Height / 2;

            var waypoints = waypointManager.GetWaypoints();

            // Draw route path connecting waypoints - thinner line
            if (waypoints.Count > 1)
            {
                using (Pen routePen = new Pen(Color.FromArgb(120, 255, 165, 0), 0.8f * zoomLevel))
                {
                    routePen.DashStyle = DashStyle.Dash;

                    for (int i = 0; i < waypoints.Count - 1; i++)
                    {
                        PointF p1 = WorldToScreen(waypoints[i].Position);
                        PointF p2 = WorldToScreen(waypoints[i + 1].Position);
                        g.DrawLine(routePen, p1, p2);
                    }

                    // Complete the loop for circular routes
                    if (waypoints.Count > 2)
                    {
                        PointF pFirst = WorldToScreen(waypoints[0].Position);
                        PointF pLast = WorldToScreen(waypoints[waypoints.Count - 1].Position);
                        g.DrawLine(routePen, pLast, pFirst);
                    }
                }
            }

            // Draw waypoints
            using (Font waypointFont = new Font("Arial", 6 * zoomLevel))
            {
                int currentIndex = waypointManager.GetCurrentWaypointIndex();

                for (int i = 0; i < waypoints.Count; i++)
                {
                    var waypoint = waypoints[i];
                    PointF screenPos = WorldToScreen(waypoint.Position);

                    // Choose colors based on waypoint type and if it's the current waypoint
                    Color waypointColor = waypoint.IsResourceNode ? Color.Gold : Color.LightBlue;
                    float size = waypoint.IsResourceNode ? ResourceNodeSize * zoomLevel : WaypointSize * zoomLevel;

                    // Highlight current waypoint
                    if (i == currentIndex)
                    {
                        // Draw modest glow for current waypoint
                        using (GraphicsPath circlePath = new GraphicsPath())
                        {
                            circlePath.AddEllipse(screenPos.X - size * 1.2f, screenPos.Y - size * 1.2f,
                                                size * 2.4f, size * 2.4f);

                            using (PathGradientBrush glowBrush = new PathGradientBrush(circlePath))
                            {
                                glowBrush.CenterColor = Color.FromArgb(80, 255, 255, 100);
                                glowBrush.SurroundColors = new Color[] { Color.FromArgb(0, 255, 255, 100) };
                                g.FillEllipse(glowBrush, screenPos.X - size * 1.5f,
                                            screenPos.Y - size * 1.5f, size * 3f, size * 3f);
                            }
                        }

                        waypointColor = Color.White;
                        size *= 1.1f; // Less enlargement
                    }

                    // Draw waypoint marker
                    using (Brush waypointBrush = new SolidBrush(waypointColor))
                    {
                        if (waypoint.IsResourceNode)
                        {
                            // Draw resource node as diamond
                            PointF[] diamond = {
                                new PointF(screenPos.X, screenPos.Y - size),
                                new PointF(screenPos.X + size, screenPos.Y),
                                new PointF(screenPos.X, screenPos.Y + size),
                                new PointF(screenPos.X - size, screenPos.Y)
                            };
                            g.FillPolygon(waypointBrush, diamond);
                        }
                        else
                        {
                            // Draw regular waypoint as circle
                            g.FillEllipse(waypointBrush, screenPos.X - size / 2,
                                         screenPos.Y - size / 2, size, size);
                        }
                    }

                    // Draw waypoint index
                    using (Brush indexBrush = new SolidBrush(Color.White))
                    using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    {
                        g.DrawString((i + 1).ToString(), waypointFont, indexBrush, screenPos, sf);
                    }

                    // Draw waypoint name if zoomed in enough, with better positioning
                    if (zoomLevel > 0.5f)
                    {
                        using (Brush nameBrush = new SolidBrush(Color.White))
                        using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        {
                            // Position name below waypoint with more space to prevent overlap
                            g.DrawString(waypoint.Name, waypointFont, nameBrush,
                                        new PointF(screenPos.X, screenPos.Y + size * 2.5f), sf);
                        }
                    }
                }
            }
        }
    }
}