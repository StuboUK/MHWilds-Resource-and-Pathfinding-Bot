using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MHWildsPathfindingBot.Bot;
using MHWildsPathfindingBot.Core.Interfaces;
using MHWildsPathfindingBot.Core.Models;
using MHWildsPathfindingBot.Core.Utils;
using MHWildsPathfindingBot.Navigation;

namespace MHWildsPathfindingBot.UI
{
    public class MainForm : Form
    {
        #region UI Components
        // Main layout components
        private Panel mainPanel;
        private Panel visualizerPanel;
        private Panel controlsContainer;
        private Panel logPanel;
        private RichTextBox logBox;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusLabel;
        private MenuStrip menuStrip;

        // Control panels
        private Panel navControlPanel;
        private Panel waypointControlPanel;
        private Panel navmeshControlPanel;
        private Panel displayControlPanel;

        // Navigation controls
        private ModernButton startButton;
        private ModernButton stopButton;
        private ModernButton recalculateButton;
        private CheckBox simulationModeCheckbox;
        private NumericUpDown targetXInput;
        private NumericUpDown targetZInput;
        private TextBox filePathTextBox;
        private ModernButton browseButton;
        private OpenFileDialog fileDialog;

        // Navmesh controls
        private ModernButton startNavmeshButton;
        private ModernButton stopNavmeshButton;
        private ModernButton clearNavmeshButton;
        private ModernButton optimizeGridButton;

        // Display controls
        private CheckBox showGridCheckbox;
        private CheckBox showBlacklistCheckbox;
        private CheckBox showWaypointsCheckbox;

        // Waypoint panel
        private WaypointPanel waypointPanel;
        #endregion

        #region Core Components
        private FormLogger logger;
        private FileManager fileManager;
        private NavigationGrid navigationGrid;
        private PathVisualizer pathVisualizer;
        private NavmeshManager navmeshManager;
        private HarvestingBot harvestingBot;
        #endregion

        #region Position Monitoring
        private System.Threading.CancellationTokenSource monitorCts;
        private System.Threading.Tasks.Task positionMonitorTask;
        private bool isMonitoringPosition = false;
        private string playerPositionFilePath = @"S:\Games\SteamLibrary\steamapps\common\MonsterHunterWilds\reframework\data\reframework\player_position.txt";
        #endregion

        #region UI Colors
        public static readonly Color BackgroundColor = Color.FromArgb(28, 28, 30);
        public static readonly Color PanelColor = Color.FromArgb(40, 40, 44);
        public static readonly Color AccentColor = Color.FromArgb(70, 130, 180);
        public static readonly Color TextColor = Color.FromArgb(180, 180, 180);
        public static readonly Color SuccessColor = Color.FromArgb(80, 170, 120);
        public static readonly Color DangerColor = Color.FromArgb(170, 80, 80);
        public static readonly Color WarningColor = Color.FromArgb(230, 190, 80);
        public static readonly Color GroupColor = Color.FromArgb(45, 45, 50);
        #endregion

        public MainForm()
        {
            InitializeComponents();
            InitializeMenuItems();

            logger = new FormLogger(logBox);
            fileManager = new FileManager(logger);

            // Show the form immediately
            this.Show();
            Application.DoEvents();

            // Display loading message
            logger.LogMessage("Initializing grid system... This may take a moment.");

            // Run heavy initialization in background
            Task.Run(() => {
                navigationGrid = new NavigationGrid(
                    logger, fileManager, 2000, 2000,
                    Globals.OriginX, Globals.OriginZ, Globals.CellSize, Globals.WalkabilityRadius
                );

                this.Invoke((MethodInvoker)delegate {
                    InitializePathVisualizer();
                    InitializeWaypointSystem();
                    InitializeWaypointVisualization();

                    navmeshManager = new NavmeshManager(logger, fileManager, navigationGrid,
                        pathVisualizer, playerPositionFilePath);
                    navmeshManager.PointCountUpdated += (sender, count) => {
                        statusLabel.Text = $"Navmesh Points: {count}";
                    };

                    ApplyStyles();
                    CheckInitialState();
                    StartPositionMonitoring();

                    logger.LogMessage("Initialization complete!");
                });
            });
        }

        #region Initialization Methods
        private void InitializeComponents()
        {
            // Form settings
            this.Text = "MH Wilds Harvesting Bot by StuboUK";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormClosing += MainForm_FormClosing;
            this.Font = new Font("Segoe UI", 8.5F);
            this.ForeColor = TextColor;
            this.BackColor = BackgroundColor;
            this.MinimumSize = new Size(1200, 700);
            this.MaximizeBox = true;
            this.Size = new Size(1400, 800);

            // Main layout
            InitializeMainLayout();

            // Create file dialog
            fileDialog = new OpenFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                Title = "Select player position file"
            };

            // Create control panels in the correct order (bottom to top)
            InitializeDisplayPanel();
            InitializeNavmeshPanel();
            InitializeNavigationPanel();

            // Add panels to the main container
            mainPanel.Controls.Add(visualizerPanel);
            mainPanel.Controls.Add(controlsContainer);
            mainPanel.Controls.Add(logPanel);
            this.Controls.Add(statusStrip);

            // Panel ordering
            logPanel.BringToFront();
            controlsContainer.BringToFront();
            statusStrip.BringToFront();
        }

        private void InitializeMainLayout()
        {
            // Main panel
            mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = BackgroundColor,
                Padding = new Padding(0)
            };
            this.Controls.Add(mainPanel);

            // Visualizer panel (main map area)
            visualizerPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = BackgroundColor,
                Padding = new Padding(0)
            };

            // Controls container (right side)
            controlsContainer = new Panel
            {
                Dock = DockStyle.Right,
                Width = 320,
                BackColor = PanelColor,
                Padding = new Padding(10)
            };

            // Status strip
            statusStrip = new StatusStrip
            {
                BackColor = PanelColor,
                ForeColor = TextColor,
                SizingGrip = false
            };
            statusLabel = new ToolStripStatusLabel
            {
                Text = "Ready"
            };
            statusStrip.Items.Add(statusLabel);

            // Log panel (bottom)
            logPanel = new Panel
            {
                Height = 120,
                Dock = DockStyle.Bottom,
                BackColor = PanelColor,
                Padding = new Padding(5)
            };

            Label logLabel = new Label
            {
                Text = "LOG",
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                ForeColor = AccentColor,
                Size = new Size(40, 18),
                Location = new Point(5, 5),
                TextAlign = ContentAlignment.MiddleLeft
            };
            logPanel.Controls.Add(logLabel);

            logBox = new RichTextBox
            {
                ReadOnly = true,
                BackColor = Color.FromArgb(32, 32, 35),
                ForeColor = Color.FromArgb(140, 220, 140),
                Font = new Font("Consolas", 8.5F),
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Bottom,
                Height = 90
            };
            logPanel.Controls.Add(logBox);

            // Path visualizer
            pathVisualizer = new PathVisualizer
            {
                Dock = DockStyle.Fill,
                BackColor = BackgroundColor
            };
            pathVisualizer.SetElementSizes(2.5f, 4.0f, 6.0f, 1.5f);
            pathVisualizer.GridColor = Color.FromArgb(45, 45, 50);
            pathVisualizer.MouseDoubleClick += PathVisualizer_MouseDoubleClick;
            visualizerPanel.Controls.Add(pathVisualizer);
        }

        private void InitializeNavigationPanel()
        {
            // Navigation panel
            navControlPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 145,
                BackColor = GroupColor,
                Padding = new Padding(10),
                Margin = new Padding(0, 0, 0, 10)
            };

            // Title
            Label navTitle = new Label
            {
                Text = "NAVIGATION",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = AccentColor,
                Dock = DockStyle.Top,
                Height = 20
            };
            navControlPanel.Controls.Add(navTitle);

            // Position file
            Panel positionFilePanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 26,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 5, 0, 0)
            };

            Label positionFileLabel = new Label
            {
                Text = "Position file:",
                Size = new Size(75, 22),
                Location = new Point(0, 2),
                TextAlign = ContentAlignment.MiddleLeft
            };
            positionFilePanel.Controls.Add(positionFileLabel);

            filePathTextBox = new TextBox
            {
                ReadOnly = true,
                Text = Path.GetFileName(playerPositionFilePath),
                Size = new Size(160, 22),
                Location = new Point(positionFileLabel.Right, 2),
                BackColor = BackgroundColor,
                ForeColor = TextColor,
                BorderStyle = BorderStyle.FixedSingle
            };
            positionFilePanel.Controls.Add(filePathTextBox);

            browseButton = new ModernButton
            {
                Text = "...",
                Size = new Size(30, 22),
                Location = new Point(filePathTextBox.Right + 2, 2),
                BackColor = AccentColor
            };
            browseButton.Click += BrowseButton_Click;
            positionFilePanel.Controls.Add(browseButton);

            navControlPanel.Controls.Add(positionFilePanel);

            // Target coordinates panel
            Panel targetPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 54,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 5, 0, 0)
            };

            // Target X
            Label targetXLabel = new Label
            {
                Text = "Target X:",
                Size = new Size(60, 22),
                Location = new Point(0, 2),
                TextAlign = ContentAlignment.MiddleLeft
            };
            targetPanel.Controls.Add(targetXLabel);

            targetXInput = new NumericUpDown
            {
                Minimum = -10000,
                Maximum = 10000,
                DecimalPlaces = 1,
                Value = -784,
                Size = new Size(90, 22),
                Location = new Point(targetXLabel.Right, 2),
                BackColor = BackgroundColor,
                ForeColor = TextColor,
                BorderStyle = BorderStyle.FixedSingle
            };
            targetPanel.Controls.Add(targetXInput);

            // Target Z
            Label targetZLabel = new Label
            {
                Text = "Target Z:",
                Size = new Size(60, 22),
                Location = new Point(0, 30),
                TextAlign = ContentAlignment.MiddleLeft
            };
            targetPanel.Controls.Add(targetZLabel);

            targetZInput = new NumericUpDown
            {
                Minimum = -10000,
                Maximum = 10000,
                DecimalPlaces = 1,
                Value = 690,
                Size = new Size(90, 22),
                Location = new Point(targetZLabel.Right, 30),
                BackColor = BackgroundColor,
                ForeColor = TextColor,
                BorderStyle = BorderStyle.FixedSingle
            };
            targetPanel.Controls.Add(targetZInput);

            navControlPanel.Controls.Add(targetPanel);

            // Simulation mode
            simulationModeCheckbox = new CheckBox
            {
                Text = "Simulation Mode (Plan Path Only)",
                Size = new Size(280, 22),
                Location = new Point(0, targetPanel.Bottom + 4),
                Checked = false,
                ForeColor = TextColor,
                BackColor = Color.Transparent
            };
            simulationModeCheckbox.CheckedChanged += (s, e) => {
                if (harvestingBot != null)
                    harvestingBot.SetSimulationMode(simulationModeCheckbox.Checked);
            };
            navControlPanel.Controls.Add(simulationModeCheckbox);

            // Navigation buttons panel
            Panel navButtonsPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 30,
                BackColor = Color.Transparent
            };

            // Start button
            startButton = new ModernButton
            {
                Text = "START",
                Width = 80,
                Height = 26,
                Location = new Point(0, 2),
                BackColor = SuccessColor,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8, FontStyle.Bold)
            };
            startButton.Click += StartButton_Click;
            navButtonsPanel.Controls.Add(startButton);

            // Stop button
            stopButton = new ModernButton
            {
                Text = "STOP",
                Width = 70,
                Height = 26,
                Location = new Point(startButton.Right + 5, 2),
                BackColor = DangerColor,
                ForeColor = Color.White,
                Enabled = false,
                Font = new Font("Segoe UI", 8, FontStyle.Bold)
            };
            stopButton.Click += StopButton_Click;
            navButtonsPanel.Controls.Add(stopButton);

            // Recalculate button
            recalculateButton = new ModernButton
            {
                Text = "RECALC",
                Width = 70,
                Height = 26,
                Location = new Point(stopButton.Right + 5, 2),
                BackColor = AccentColor,
                Enabled = false
            };
            recalculateButton.Click += RecalculateButton_Click;
            navButtonsPanel.Controls.Add(recalculateButton);

            navControlPanel.Controls.Add(navButtonsPanel);
        }

        private void InitializeNavmeshPanel()
        {
            navmeshControlPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 110,
                BackColor = GroupColor,
                Padding = new Padding(10),
                Margin = new Padding(0, 0, 0, 10)
            };

            // Title
            Label navmeshTitle = new Label
            {
                Text = "NAVMESH GENERATION",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = AccentColor,
                Dock = DockStyle.Top,
                Height = 20
            };
            navmeshControlPanel.Controls.Add(navmeshTitle);

            // Help text
            Label navmeshHelp = new Label
            {
                Text = "Generate walkable grid by moving in-game.",
                Dock = DockStyle.Top,
                Height = 18,
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.LightGray
            };
            navmeshControlPanel.Controls.Add(navmeshHelp);

            // Navmesh buttons panel
            Panel navmeshButtonsPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                BackColor = Color.Transparent
            };

            // Start navmesh button
            startNavmeshButton = new ModernButton
            {
                Text = "START",
                Width = 70,
                Height = 26,
                Location = new Point(0, 2),
                BackColor = SuccessColor,
                ForeColor = Color.White
            };
            startNavmeshButton.Click += StartNavmeshButton_Click;
            navmeshButtonsPanel.Controls.Add(startNavmeshButton);

            // Stop navmesh button
            stopNavmeshButton = new ModernButton
            {
                Text = "STOP",
                Width = 70,
                Height = 26,
                Location = new Point(startNavmeshButton.Right + 5, 2),
                BackColor = DangerColor,
                ForeColor = Color.White
            };
            stopNavmeshButton.Click += StopNavmeshButton_Click;
            navmeshButtonsPanel.Controls.Add(stopNavmeshButton);

            // Clear navmesh button
            clearNavmeshButton = new ModernButton
            {
                Text = "CLEAR",
                Width = 70,
                Height = 26,
                Location = new Point(0, 32),
                BackColor = WarningColor,
                ForeColor = Color.Black
            };
            clearNavmeshButton.Click += ClearNavmeshButton_Click;
            navmeshButtonsPanel.Controls.Add(clearNavmeshButton);

            // Optimize grid button
            optimizeGridButton = new ModernButton
            {
                Text = "OPTIMIZE",
                Width = 80,
                Height = 26,
                Location = new Point(clearNavmeshButton.Right + 5, 32),
                BackColor = AccentColor
            };
            optimizeGridButton.Click += (s, e) => navmeshManager.OptimizeGrid();
            navmeshButtonsPanel.Controls.Add(optimizeGridButton);

            navmeshControlPanel.Controls.Add(navmeshButtonsPanel);
        }

        private void InitializeDisplayPanel()
        {
            displayControlPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 110,
                BackColor = GroupColor,
                Padding = new Padding(10),
                Margin = new Padding(0, 0, 0, 10)
            };

            // Title
            Label displayTitle = new Label
            {
                Text = "DISPLAY OPTIONS",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = AccentColor,
                Dock = DockStyle.Top,
                Height = 20
            };
            displayControlPanel.Controls.Add(displayTitle);

            // Show walkable grid checkbox
            showGridCheckbox = new CheckBox
            {
                Text = "Show Walkable Grid",
                Size = new Size(280, 22),
                Location = new Point(0, 25),
                Checked = true,
                ForeColor = TextColor,
                BackColor = Color.Transparent
            };
            showGridCheckbox.CheckedChanged += (s, e) => {
                pathVisualizer.ShowWalkableGrid = showGridCheckbox.Checked;
                pathVisualizer.Invalidate();
            };
            displayControlPanel.Controls.Add(showGridCheckbox);

            // Show blacklist grid checkbox
            showBlacklistCheckbox = new CheckBox
            {
                Text = "Show Blacklisted Areas",
                Size = new Size(280, 22),
                Location = new Point(0, 50),
                Checked = true,
                ForeColor = TextColor,
                BackColor = Color.Transparent
            };
            showBlacklistCheckbox.CheckedChanged += (s, e) => {
                pathVisualizer.ShowBlacklistGrid = showBlacklistCheckbox.Checked;
                pathVisualizer.Invalidate();
            };
            displayControlPanel.Controls.Add(showBlacklistCheckbox);

            // Show waypoints checkbox
            showWaypointsCheckbox = new CheckBox
            {
                Text = "Show Waypoints",
                Size = new Size(280, 22),
                Location = new Point(0, 75),
                Checked = true,
                ForeColor = TextColor,
                BackColor = Color.Transparent
            };
            showWaypointsCheckbox.CheckedChanged += (s, e) => {
                pathVisualizer.ShowWaypoints = showWaypointsCheckbox.Checked;
                pathVisualizer.Invalidate();
            };
            displayControlPanel.Controls.Add(showWaypointsCheckbox);

            // Help text
            Label mouseHelpLabel = new Label
            {
                Text = "Mouse: Drag to Pan, Wheel to Zoom\nDouble-click on Map to Add Waypoint",
                Size = new Size(280, 34),
                Location = new Point(0, showWaypointsCheckbox.Bottom + 5),
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.LightGray
            };
            displayControlPanel.Controls.Add(mouseHelpLabel);
        }

        private void InitializeWaypointSystem()
        {
            // Create the HarvestingBot
            harvestingBot = new HarvestingBot(logger, fileManager, navigationGrid, pathVisualizer, playerPositionFilePath);

            // Subscribe to bot events
            harvestingBot.PlayerPositionUpdated += (sender, pos) => pathVisualizer.UpdatePlayerPosition(pos);
            harvestingBot.TargetReached += (sender, e) => {
                statusLabel.Text = "Target Reached!";
                statusLabel.ForeColor = SuccessColor;
                logger.LogMessage("Target reached!");
            };
            harvestingBot.BotStopped += (sender, e) => {
                startButton.Enabled = true;
                stopButton.Enabled = false;
                recalculateButton.Enabled = false;
            };
            harvestingBot.HarvestingStarted += (sender, e) => {
                logger.LogMessage("Harvesting started...", Color.LightGreen);
            };
            harvestingBot.HarvestingCompleted += (sender, e) => {
                logger.LogMessage("Harvesting completed.", Color.LightGreen);
            };

            // Create waypoint panel
            waypointPanel = new WaypointPanel(harvestingBot);
            waypointPanel.Dock = DockStyle.Top;
            waypointPanel.Height = 320;
            waypointPanel.BackColor = GroupColor;
            waypointPanel.Margin = new Padding(0, 0, 0, 10);

            controlsContainer.Controls.Add(waypointPanel);
            controlsContainer.Controls.Add(displayControlPanel);
            controlsContainer.Controls.Add(navmeshControlPanel);
            controlsContainer.Controls.Add(navControlPanel);
        }

        private void InitializeWaypointVisualization()
        {
            // Set waypoint manager in the visualizer
            pathVisualizer.SetWaypointManager(harvestingBot.GetWaypointManager());
        }

        private void InitializeMenuItems()
        {
            menuStrip = new MenuStrip
            {
                BackColor = Color.FromArgb(35, 35, 38),
                ForeColor = TextColor,
                Renderer = new CustomMenuRenderer(),
                Padding = new Padding(6, 3, 6, 3)
            };

            ToolStripMenuItem fileMenu = new ToolStripMenuItem("File") { Padding = new Padding(4, 0, 4, 0) };
            fileMenu.DropDownItems.Add("Import Waypoints...", null, (s, e) => ImportWaypoints());
            fileMenu.DropDownItems.Add("Export Waypoints...", null, (s, e) => ExportWaypoints());
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add("Exit", null, (s, e) => this.Close());

            ToolStripMenuItem toolsMenu = new ToolStripMenuItem("Tools") { Padding = new Padding(4, 0, 4, 0) };
            toolsMenu.DropDownItems.Add("Create Example Waypoints", null, (s, e) => {
                harvestingBot.GetWaypointManager().CreateExampleWaypoints();
            });
            toolsMenu.DropDownItems.Add("Optimize Waypoint Route", null, (s, e) => {
                harvestingBot.GetWaypointManager().OptimizeRoute();
            });

            ToolStripMenuItem helpMenu = new ToolStripMenuItem("Help") { Padding = new Padding(4, 0, 4, 0) };
            helpMenu.DropDownItems.Add("About", null, (s, e) => {
                MessageBox.Show(
                    "MH Wilds Pathfinding Bot\nVersion 1.1\n\n" +
                    "A tool for automated pathfinding and resource harvesting in Monster Hunter Wilds.\n\n" +
                    "Created by StuboUK",
                    "About",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            });

            menuStrip.Items.Add(fileMenu);
            menuStrip.Items.Add(toolsMenu);
            menuStrip.Items.Add(helpMenu);

            this.Controls.Add(menuStrip);
            this.MainMenuStrip = menuStrip;
        }
        #endregion

        #region Helper Methods
        private void InitializePathVisualizer()
        {
            if (navigationGrid == null || pathVisualizer == null)
                return;

            // Explicitly set NavigationGrid reference
            pathVisualizer.SetNavigationGrid(navigationGrid);

            pathVisualizer.SetGrids(
                navigationGrid.GetWalkableGrid(),
                navigationGrid.GetBlacklistGrid(),
                navigationGrid.GridSizeX,
                navigationGrid.GridSizeZ
            );

            pathVisualizer.SetGridInfo(
                navigationGrid.OriginX,
                navigationGrid.OriginZ,
                navigationGrid.CellSize
            );

            pathVisualizer.ShowWalkableGrid = true;
            pathVisualizer.Invalidate();
        }

        private void ApplyStyles()
        {
            // Apply styling to form controls
            StyleControlRecursive(this);

            // Additional tweaks for aesthetic consistency
            foreach (Control control in controlsContainer.Controls)
            {
                if (control is Panel panel)
                {
                    panel.BackColor = GroupColor;
                    panel.BorderStyle = BorderStyle.None;
                }
            }
        }

        private void StyleControlRecursive(Control control)
        {
            // Set foreground color for all controls
            control.ForeColor = TextColor;

            // Panel styling
            if (control is Panel pnl)
            {
                pnl.BorderStyle = BorderStyle.None;
            }
            // TextBox styling
            else if (control is TextBox txt)
            {
                txt.BorderStyle = BorderStyle.FixedSingle;
                txt.BackColor = BackgroundColor;
                txt.ForeColor = TextColor;
            }
            // RichTextBox styling
            else if (control is RichTextBox rtb)
            {
                rtb.BorderStyle = BorderStyle.None;
                if (rtb == logBox)
                {
                    rtb.BackColor = Color.FromArgb(32, 32, 35);
                    rtb.ForeColor = Color.FromArgb(140, 220, 140);
                }
                else
                {
                    rtb.BackColor = BackgroundColor;
                    rtb.ForeColor = TextColor;
                }
            }
            // NumericUpDown styling
            else if (control is NumericUpDown nud)
            {
                nud.BackColor = BackgroundColor;
                nud.ForeColor = TextColor;
                nud.BorderStyle = BorderStyle.FixedSingle;
            }
            // Label styling
            else if (control is Label lbl)
            {
                lbl.BackColor = Color.Transparent;
            }
            // Checkbox styling
            else if (control is CheckBox chk)
            {
                chk.BackColor = Color.Transparent;
                chk.ForeColor = TextColor;
                chk.FlatStyle = FlatStyle.Standard;
            }

            // Apply to child controls
            foreach (Control child in control.Controls)
            {
                StyleControlRecursive(child);
            }
        }

        private void CheckInitialState()
        {
            InitializePathVisualizer();
        }
        #endregion

        #region Event Handlers
        private void BrowseButton_Click(object sender, EventArgs e)
        {
            if (fileDialog.ShowDialog() == DialogResult.OK)
            {
                playerPositionFilePath = fileDialog.FileName;
                filePathTextBox.Text = Path.GetFileName(playerPositionFilePath);

                harvestingBot.Stop();
                StopPositionMonitoring();
                StartPositionMonitoring();
            }
        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            float targetX = (float)targetXInput.Value;
            float targetZ = (float)targetZInput.Value;

            startButton.Enabled = false;
            stopButton.Enabled = true;
            recalculateButton.Enabled = true;

            harvestingBot.SetSimulationMode(simulationModeCheckbox.Checked);

            if (harvestingBot.Start(targetX, targetZ))
            {
                statusLabel.Text = simulationModeCheckbox.Checked ? "Simulation Mode Active" : "Bot Running";
                statusLabel.ForeColor = simulationModeCheckbox.Checked ? WarningColor : SuccessColor;
                logger.LogMessage($"Bot started to target: ({targetX}, {targetZ})");
            }
            else
            {
                statusLabel.Text = "Failed to start bot";
                statusLabel.ForeColor = DangerColor;
                startButton.Enabled = true;
                stopButton.Enabled = false;
                recalculateButton.Enabled = false;
                logger.LogMessage("Failed to start bot", Color.Red);
            }
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            harvestingBot.Stop();

            startButton.Enabled = true;
            stopButton.Enabled = false;
            recalculateButton.Enabled = false;
            statusLabel.Text = "Bot Stopped";
            statusLabel.ForeColor = DangerColor;
            logger.LogMessage("Bot stopped");
        }

        private void RecalculateButton_Click(object sender, EventArgs e)
        {
            harvestingBot.ForceRecalculatePath();
            statusLabel.Text = "Path Recalculated";
            statusLabel.ForeColor = AccentColor;
            logger.LogMessage("Path recalculated");
        }

        private void StartNavmeshButton_Click(object sender, EventArgs e)
        {
            showGridCheckbox.Checked = true;
            pathVisualizer.ShowWalkableGrid = true;
            navmeshManager.Start();
            statusLabel.Text = "Navmesh Generation Started";
            statusLabel.ForeColor = SuccessColor;
            logger.LogMessage("Navmesh generation started");
        }

        private void StopNavmeshButton_Click(object sender, EventArgs e)
        {
            navmeshManager.Stop();
            statusLabel.Text = "Navmesh Generation Stopped";
            statusLabel.ForeColor = DangerColor;
            logger.LogMessage("Navmesh generation stopped");
        }

        private void ClearNavmeshButton_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show(
                "This will clear all walkable and blacklist data. Are you sure?",
                "Confirm Clear Navmesh",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (result == DialogResult.Yes)
            {
                navmeshManager.ClearData();
                statusLabel.Text = "Navmesh Data Cleared";
                statusLabel.ForeColor = WarningColor;
                logger.LogMessage("Navmesh data cleared");
            }
        }

        private void PathVisualizer_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            // Add waypoint at the clicked location
            Vector2 worldPos = pathVisualizer.ScreenToWorld(e.Location);
            harvestingBot.GetWaypointManager().AddWaypoint(worldPos);
            logger.LogMessage($"Added waypoint at ({worldPos.X:F1}, {worldPos.Z:F1})");
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            harvestingBot.Stop();
            navmeshManager.Stop();
            StopPositionMonitoring();

            try
            {
                navigationGrid.SaveWalkableGrid();
                navigationGrid.SaveBlacklistGrid();
            }
            catch { }
        }
        #endregion

        #region Waypoint Import/Export
        private void ImportWaypoints()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*";
            dialog.Title = "Import Waypoints";

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    harvestingBot.GetWaypointManager().ImportWaypointsFromFile(dialog.FileName);
                    statusLabel.Text = "Waypoints Imported";
                    statusLabel.ForeColor = SuccessColor;
                    logger.LogMessage($"Imported waypoints from {dialog.FileName}");
                }
                catch (Exception ex)
                {
                    logger.LogMessage($"Error importing waypoints: {ex.Message}", Color.Red);
                    MessageBox.Show($"Error importing waypoints: {ex.Message}", "Import Error",
                                  MessageBoxButtons.OK, MessageBoxIcon.Error);
                    statusLabel.Text = "Waypoint Import Failed";
                    statusLabel.ForeColor = DangerColor;
                }
            }
        }

        private void ExportWaypoints()
        {
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Filter = "JSON Files (*.json)|*.json";
            dialog.Title = "Export Waypoints";
            dialog.DefaultExt = "json";

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    harvestingBot.GetWaypointManager().ExportWaypointsToFile(dialog.FileName);
                    statusLabel.Text = "Waypoints Exported";
                    statusLabel.ForeColor = SuccessColor;
                    logger.LogMessage($"Exported waypoints to {dialog.FileName}");
                }
                catch (Exception ex)
                {
                    logger.LogMessage($"Error exporting waypoints: {ex.Message}", Color.Red);
                    MessageBox.Show($"Error exporting waypoints: {ex.Message}", "Export Error",
                                  MessageBoxButtons.OK, MessageBoxIcon.Error);
                    statusLabel.Text = "Waypoint Export Failed";
                    statusLabel.ForeColor = DangerColor;
                }
            }
        }
        #endregion

        #region Position Monitoring
        private void StartPositionMonitoring()
        {
            if (isMonitoringPosition)
                return;

            isMonitoringPosition = true;
            monitorCts = new System.Threading.CancellationTokenSource();

            positionMonitorTask = System.Threading.Tasks.Task.Run(() =>
            {
                while (!monitorCts.IsCancellationRequested)
                {
                    try
                    {
                        string fileContents = fileManager.ReadPositionFile(playerPositionFilePath);
                        string[] values = fileContents.Split(',');

                        if (values.Length >= 3 &&
                            float.TryParse(values[0], out float tempX) &&
                            float.TryParse(values[1], out float tempY) &&
                            float.TryParse(values[2], out float tempZ))
                        {
                            this.Invoke((MethodInvoker)delegate
                            {
                                pathVisualizer.UpdatePlayerPosition(new Vector2(tempX, tempZ));
                            });
                        }
                    }
                    catch { }

                    System.Threading.Thread.Sleep(100);
                }
            }, monitorCts.Token);
        }

        private void StopPositionMonitoring()
        {
            if (!isMonitoringPosition)
                return;

            isMonitoringPosition = false;

            if (monitorCts != null)
            {
                monitorCts.Cancel();
                monitorCts.Dispose();
                monitorCts = null;
            }
        }
        #endregion
    }

    #region Custom UI Classes
    // Custom renderer for menu items
    class CustomMenuRenderer : ToolStripProfessionalRenderer
    {
        public CustomMenuRenderer() : base(new CustomColorTable()) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            Rectangle rect = new Rectangle(0, 0, e.Item.Width, e.Item.Height);
            Color color = e.Item.Selected ? Color.FromArgb(60, 60, 65) : Color.FromArgb(35, 35, 38);

            using (SolidBrush brush = new SolidBrush(color))
            {
                e.Graphics.FillRectangle(brush, rect);
            }
        }
    }

    class CustomColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(60, 60, 65);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(60, 60, 65);
        public override Color MenuItemPressedGradientBegin => MainForm.AccentColor;
        public override Color MenuItemPressedGradientEnd => MainForm.AccentColor;
        public override Color MenuItemBorder => Color.FromArgb(70, 70, 75);
        public override Color MenuBorder => Color.FromArgb(50, 50, 53);
        public override Color ToolStripDropDownBackground => Color.FromArgb(35, 35, 38);
        public override Color ImageMarginGradientBegin => Color.FromArgb(35, 35, 38);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(35, 35, 38);
        public override Color ImageMarginGradientEnd => Color.FromArgb(35, 35, 38);
    }

    // Custom button
    public class ModernButton : Button
    {
        public bool IsSelected { get; set; } = false;

        public ModernButton()
        {
            this.FlatStyle = FlatStyle.Flat;
            this.FlatAppearance.BorderSize = 0;
            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 8F);
            this.Cursor = Cursors.Hand;
            this.BackColor = MainForm.AccentColor;
            this.FlatAppearance.MouseOverBackColor = ControlPaint.Light(this.BackColor, 0.1f);
            this.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(this.BackColor, 0.2f);
        }
    }

    // Waypoint panel
    public class WaypointPanel : Panel
    {
        private readonly HarvestingBot harvestingBot;
        private readonly WaypointManager waypointManager;

        // UI components
        private ListView waypointListView;
        private Panel editorPanel;
        private TextBox waypointNameInput;
        private NumericUpDown waypointXInput;
        private NumericUpDown waypointZInput;
        private CheckBox isResourceNodeCheckbox;
        private ModernButton addWaypointButton;
        private ModernButton saveWaypointButton;
        private ModernButton deleteWaypointButton;
        private ModernButton optimizeRouteButton;
        private CheckBox autoHarvestCheckbox;
        private Label waypointTitle;

        // Selected waypoint
        private string selectedWaypointId;

        public WaypointPanel(HarvestingBot harvestingBot)
        {
            this.harvestingBot = harvestingBot;
            this.waypointManager = harvestingBot.GetWaypointManager();

            this.Padding = new Padding(10);

            InitializeComponents();

            // Subscribe to waypoint manager events
            waypointManager.WaypointsChanged += OnWaypointsChanged;
            waypointManager.CurrentWaypointChanged += OnCurrentWaypointChanged;

            // Load waypoints
            RefreshWaypointList();
        }

        private void InitializeComponents()
        {
            // Title
            waypointTitle = new Label
            {
                Text = "WAYPOINTS",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = MainForm.AccentColor,
                Dock = DockStyle.Top,
                Height = 20
            };
            this.Controls.Add(waypointTitle);

            // Main layout - using explicit layouts instead of docking for better control
            Panel listPanel = new Panel
            {
                Location = new Point(10, 25),
                Size = new Size(300, 160),
                BackColor = Color.Transparent
            };
            this.Controls.Add(listPanel);

            // Waypoint list
            waypointListView = new ListView
            {
                Location = new Point(0, 0),
                Size = new Size(300, 130),
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                BackColor = MainForm.BackgroundColor,
                ForeColor = MainForm.TextColor,
                BorderStyle = BorderStyle.FixedSingle
            };
            waypointListView.Columns.Add("Name", 170);
            waypointListView.Columns.Add("Type", 130);
            waypointListView.SelectedIndexChanged += WaypointListView_SelectedIndexChanged;
            listPanel.Controls.Add(waypointListView);

            // Button panel
            Panel buttonPanel = new Panel
            {
                Location = new Point(0, 135),
                Size = new Size(300, 25),
                BackColor = Color.Transparent
            };
            listPanel.Controls.Add(buttonPanel);

            // Optimize route button
            optimizeRouteButton = new ModernButton
            {
                Text = "Optimize",
                Size = new Size(70, 22),
                Location = new Point(0, 0),
                BackColor = MainForm.AccentColor
            };
            optimizeRouteButton.Click += OptimizeRouteButton_Click;
            buttonPanel.Controls.Add(optimizeRouteButton);

            // Auto-harvest checkbox - FIXED: Now using standard CheckBox with proper styling
            autoHarvestCheckbox = new CheckBox
            {
                Text = "Auto-Harvest",
                Location = new Point(175, 0),
                Size = new Size(120, 22),
                Checked = false,
                ForeColor = MainForm.TextColor,
                BackColor = Color.Transparent
            };
            autoHarvestCheckbox.CheckedChanged += AutoHarvestCheckbox_CheckedChanged;
            buttonPanel.Controls.Add(autoHarvestCheckbox);

            // Editor panel
            editorPanel = new Panel
            {
                Location = new Point(10, 195),
                Size = new Size(300, 110),
                BackColor = Color.Transparent
            };
            this.Controls.Add(editorPanel);

            // Name input
            Label nameLabel = new Label
            {
                Text = "Name:",
                Size = new Size(45, 22),
                Location = new Point(0, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };
            editorPanel.Controls.Add(nameLabel);

            waypointNameInput = new TextBox
            {
                Location = new Point(45, 0),
                Size = new Size(140, 22),
                BackColor = MainForm.BackgroundColor,
                ForeColor = MainForm.TextColor,
                BorderStyle = BorderStyle.FixedSingle
            };
            editorPanel.Controls.Add(waypointNameInput);

            // X/Z coordinates
            Label posLabel = new Label
            {
                Text = "X:",
                Size = new Size(20, 22),
                Location = new Point(0, 28),
                TextAlign = ContentAlignment.MiddleLeft
            };
            editorPanel.Controls.Add(posLabel);

            waypointXInput = new NumericUpDown
            {
                Location = new Point(20, 28),
                Size = new Size(60, 22),
                Minimum = -10000,
                Maximum = 10000,
                DecimalPlaces = 1,
                BackColor = MainForm.BackgroundColor,
                ForeColor = MainForm.TextColor,
                BorderStyle = BorderStyle.FixedSingle
            };
            editorPanel.Controls.Add(waypointXInput);

            Label zLabel = new Label
            {
                Text = "Z:",
                Size = new Size(20, 22),
                Location = new Point(95, 28),
                TextAlign = ContentAlignment.MiddleLeft
            };
            editorPanel.Controls.Add(zLabel);

            waypointZInput = new NumericUpDown
            {
                Location = new Point(115, 28),
                Size = new Size(60, 22),
                Minimum = -10000,
                Maximum = 10000,
                DecimalPlaces = 1,
                BackColor = MainForm.BackgroundColor,
                ForeColor = MainForm.TextColor,
                BorderStyle = BorderStyle.FixedSingle
            };
            editorPanel.Controls.Add(waypointZInput);

            // Resource node checkbox - FIXED: Using standard checkbox with proper styling
            isResourceNodeCheckbox = new CheckBox
            {
                Text = "Resource Node",
                Location = new Point(0, 55),
                Size = new Size(120, 22),
                Checked = true,
                ForeColor = MainForm.TextColor,
                BackColor = Color.Transparent
            };
            editorPanel.Controls.Add(isResourceNodeCheckbox);

            // Editor buttons
            addWaypointButton = new ModernButton
            {
                Text = "Add Pos",
                Size = new Size(60, 22),
                Location = new Point(0, 80),
                BackColor = MainForm.SuccessColor,
                ForeColor = Color.White
            };
            addWaypointButton.Click += AddWaypointButton_Click;
            editorPanel.Controls.Add(addWaypointButton);

            saveWaypointButton = new ModernButton
            {
                Text = "Save",
                Size = new Size(60, 22),
                Location = new Point(65, 80),
                BackColor = MainForm.AccentColor,
                Enabled = false
            };
            saveWaypointButton.Click += SaveWaypointButton_Click;
            editorPanel.Controls.Add(saveWaypointButton);

            deleteWaypointButton = new ModernButton
            {
                Text = "Delete",
                Size = new Size(60, 22),
                Location = new Point(130, 80),
                BackColor = MainForm.DangerColor,
                Enabled = false
            };
            deleteWaypointButton.Click += DeleteWaypointButton_Click;
            editorPanel.Controls.Add(deleteWaypointButton);
        }

        private void WaypointListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (waypointListView.SelectedItems.Count > 0)
            {
                ListViewItem selectedItem = waypointListView.SelectedItems[0];
                selectedWaypointId = selectedItem.Tag.ToString();

                Waypoint waypoint = waypointManager.GetWaypoints()
                    .FirstOrDefault(w => w.Id == selectedWaypointId);

                if (waypoint != null)
                {
                    waypointNameInput.Text = waypoint.Name;
                    waypointXInput.Value = (decimal)waypoint.Position.X;
                    waypointZInput.Value = (decimal)waypoint.Position.Z;
                    isResourceNodeCheckbox.Checked = waypoint.IsResourceNode;

                    saveWaypointButton.Enabled = true;
                    deleteWaypointButton.Enabled = true;
                }
            }
            else
            {
                selectedWaypointId = null;
                saveWaypointButton.Enabled = false;
                deleteWaypointButton.Enabled = false;
            }
        }

        private void AddWaypointButton_Click(object sender, EventArgs e)
        {
            Vector2 playerPos = harvestingBot.GetPlayerPosition();
            waypointManager.AddWaypoint(playerPos);
            RefreshWaypointList();
            SelectLastWaypoint();
        }

        private void SaveWaypointButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(selectedWaypointId))
                return;

            string name = waypointNameInput.Text;
            if (string.IsNullOrWhiteSpace(name))
                name = $"Waypoint {waypointManager.GetWaypoints().Count}";

            Vector2 position = new Vector2(
                (float)waypointXInput.Value,
                (float)waypointZInput.Value
            );

            bool isResourceNode = isResourceNodeCheckbox.Checked;

            waypointManager.UpdateWaypoint(selectedWaypointId, name, position, isResourceNode);
            RefreshWaypointList();
        }

        private void DeleteWaypointButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(selectedWaypointId))
                return;

            DialogResult result = MessageBox.Show(
                "Delete this waypoint?", "Confirm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question
            );

            if (result == DialogResult.Yes)
            {
                waypointManager.RemoveWaypoint(selectedWaypointId);
                selectedWaypointId = null;
                RefreshWaypointList();
            }
        }

        private void OptimizeRouteButton_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show(
                "Optimize route?", "Confirm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question
            );

            if (result == DialogResult.Yes)
            {
                waypointManager.OptimizeRoute();
                RefreshWaypointList();
            }
        }

        private void AutoHarvestCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            // FIXED: Added debugging to trace auto-harvest state changes
            bool isChecked = autoHarvestCheckbox.Checked;
            (harvestingBot as HarvestingBot).GetWaypointManager().GetWaypointCount();
            (harvestingBot as HarvestingBot).GetLogger().LogMessage($"Auto-harvest set to: {isChecked}");
            harvestingBot.SetAutoHarvesting(isChecked);
        }

        private void OnWaypointsChanged(object sender, WaypointEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => RefreshWaypointList()));
            }
            else
            {
                RefreshWaypointList();
            }
        }

        private void OnCurrentWaypointChanged(object sender, WaypointEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => HighlightCurrentWaypoint(e.CurrentWaypointIndex)));
            }
            else
            {
                HighlightCurrentWaypoint(e.CurrentWaypointIndex);
            }
        }

        private void RefreshWaypointList()
        {
            waypointListView.Items.Clear();

            foreach (var waypoint in waypointManager.GetWaypoints())
            {
                ListViewItem item = new ListViewItem(waypoint.Name);
                item.SubItems.Add(waypoint.IsResourceNode ? "Resource" : "Waypoint");
                item.Tag = waypoint.Id;

                // Highlight the current waypoint
                if (waypointManager.GetCurrentWaypointIndex() >= 0 &&
                    waypointManager.GetWaypoints()[waypointManager.GetCurrentWaypointIndex()].Id == waypoint.Id)
                {
                    item.BackColor = Color.FromArgb(60, MainForm.AccentColor);
                }
                else
                {
                    item.BackColor = MainForm.BackgroundColor;
                }

                waypointListView.Items.Add(item);
            }

            // Update UI state
            bool hasWaypoints = waypointListView.Items.Count > 0;
            optimizeRouteButton.Enabled = hasWaypoints && waypointListView.Items.Count > 2;
        }

        private void HighlightCurrentWaypoint(int index)
        {
            // Reset all item background colors
            foreach (ListViewItem item in waypointListView.Items)
            {
                item.BackColor = MainForm.BackgroundColor;
            }

            // Highlight the current waypoint
            if (index >= 0 && index < waypointListView.Items.Count)
            {
                waypointListView.Items[index].BackColor = Color.FromArgb(60, MainForm.AccentColor);
            }
        }

        private void SelectLastWaypoint()
        {
            if (waypointListView.Items.Count > 0)
            {
                waypointListView.Items[waypointListView.Items.Count - 1].Selected = true;
                waypointListView.EnsureVisible(waypointListView.Items.Count - 1);
            }
        }
    }
    #endregion
}