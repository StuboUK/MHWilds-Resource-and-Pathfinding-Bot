# Monster Hunter Wilds Pathfinding & Harvesting Bot



https://github.com/user-attachments/assets/081ac47f-5610-4f0f-b4d0-f726f6bd39cb




This project is a C# Windows Forms application designed to assist with navigation and automated resource harvesting in Monster Hunter Wilds. It features pathfinding, dynamic navmesh generation, waypoint management, and virtual controller input for character movement and actions. It is in a usable state, however it still needs a lot of work. I do not condone the use of this in a cheating context, it's purely an educational tool. A get_player_position.lua script is provided also, you will need REFramework to run it. 

## Overview

The bot reads the player's in-game position from an external file and uses this information to:
*   Navigate the player to specified X, Z coordinates.
*   Generate a "navmesh" (a grid of walkable areas) by observing player movement.
*   Follow a predefined sequence of waypoints for tasks like automated resource gathering.
*   Simulate controller inputs for movement and interactions (e.g., harvesting).

## Features

*   **A* Pathfinding:** Calculates efficient paths to a target destination using the A* algorithm ([`Core/Navigation/Pathfinding/Pathfinder.cs`](Core/Navigation/Pathfinding/Pathfinder.cs:15)) on a 2D grid.
*   **Dynamic Navmesh Generation:** A dedicated mode ([`Core/Bot/NavmeshManager.cs`](Core/Bot/NavmeshManager.cs:16)) allows the bot to learn and map walkable areas as the player moves through the game world. This data is stored in [`NavigationGrid.cs`](Core/Navigation/NavigationGrid.cs) and can be saved/loaded.
*   **Movement Control:** Simulates Xbox 360 controller inputs (specifically the left analog stick) via `Nefarius.ViGEm.Client` for character movement ([`Core/Bot/MovementController.cs`](Core/Bot/MovementController.cs:16)). Assumes a fixed North-facing camera for consistent directional input.
*   **Automated Harvesting:** The [`Core/Bot/HarvestingBot.cs`](Core/Bot/HarvestingBot.cs:18) can follow a route of waypoints. If a waypoint is marked as a resource node, the bot will simulate pressing the 'B' button to harvest.
*   **Waypoint Management:** Create, edit, save, load, import, export, and optimize (nearest-neighbor) harvesting routes using the [`Core/Bot/WaypointManager.cs`](Core/Bot/WaypointManager.cs:16).
*   **Stuck Detection:** Includes a [`Core/Bot/StuckDetector.cs`](Core/Bot/StuckDetector.cs) to identify when the bot is unable to make progress and can blacklist problematic areas on the navmesh.
*   **Path Visualization:** A GUI ([`Core/UI/MainForm.cs`](Core/UI/MainForm.cs:17)) displays the current navmesh, calculated path, player position, and waypoints in real-time using [`Core/UI/PathVisualizer.cs`](Core/UI/PathVisualizer.cs).
*   **Simulation Mode:** Allows path planning and visualization without sending actual movement commands to the game.
*   **Position Input:** Reads player coordinates (X, Y, Z) from an external text file (e.g., `player_position.txt`).

## How It Works

1.  **Position Tracking:** An external tool (like an REFramework script) must continuously update a text file with the player's current X, Y, and Z coordinates. The bot reads this file ([`Core/Utils/FileManager.cs`](Core/Utils/FileManager.cs:1)) via the path specified in the UI.
2.  **Navmesh:**
    *   **Generation:** In "Navmesh Generation" mode, as the player moves, the bot marks the corresponding cells in its internal `NavigationGrid` as walkable.
    *   **Usage:** During pathfinding, the `Pathfinder` uses this grid to determine valid movement areas.
3.  **Pathfinding:**
    *   When a target is set, the `Pathfinder` uses the A* algorithm to find a sequence of waypoints from the player's current position (adjusted to the nearest walkable grid cell) to the target position.
    *   The pathfinding considers blacklisted areas (e.g., from stuck detection) and includes optimizations like path smoothing and simplification ([`Core/Navigation/Pathfinding/PathOptimizer.cs`](Core/Navigation/Pathfinding/PathOptimizer.cs)).
4.  **Movement:**
    *   The [`Core/Bot/Bot.cs`](Core/Bot/Bot.cs:16) follows the calculated path.
    *   The `MovementController` translates the desired world-space movement direction into normalized X and Z values for a virtual left analog stick.
    *   These values are sent to a virtual Xbox 360 controller emulated by `Nefarius.ViGEm.Client` ([`Core/Utils/InputSimulator.cs`](Core/Utils/InputSimulator.cs)).
5.  **Harvesting:**
    *   The `HarvestingBot` iterates through a list of waypoints managed by `WaypointManager`.
    *   It navigates to each waypoint.
    *   If a waypoint is a "resource node" and auto-harvest is enabled, it stops movement, simulates a 'B' button press for a defined duration, then proceeds to the next waypoint.

## Prerequisites

*   **Windows Operating System**
*   **.NET 6.0 SDK (or Runtime if only running pre-compiled binaries):** The project targets `net6.0-windows` ([`MHWildsPathfindingBot.csproj`](MHWildsPathfindingBot.csproj:4)).
*   **ViGEmBus Driver:** This driver is required for virtual controller emulation. The application checks for its presence on startup ([`Program.cs`](Program.cs:33)) and will prompt for installation if missing. It can be downloaded from the [ViGEmBus Releases Page](https://github.com/ViGEm/ViGEmBus/releases).
*   **Monster Hunter Wilds Player Position Provider:** An external mechanism (e.g., an REFramework script) is needed to continuously write the player's current X, Y, and Z coordinates to a text file. The default path expected by the bot is `S:\Games\SteamLibrary\steamapps\common\MonsterHunterWilds\reframework\data\reframework\player_position.txt`, but this can be changed in the bot's UI.
*   From memory, Monster Hunter Wilds needs some settings changed, resetting camera on climbing should be disabled, and player movement must be independent from the camera, I forgot what specific setting is needed. 

## Setup & Installation

1.  **Install .NET 6.0 SDK/Runtime:** If not already installed, download and install it from the [official .NET website](https://dotnet.microsoft.com/download/dotnet/6.0).
2.  **Install ViGEmBus Driver:** Download and install the latest ViGEmBus driver from [here](https://github.com/ViGEm/ViGEmBus/releases).
3.  **Set up Player Position File:**
    *   Ensure you have a tool (like REFramework for Monster Hunter Wilds) that can output the player's X, Y, Z coordinates to a `.txt` file.
    *   Configure that tool to write to a known location.
4.  **Build or Download the Bot:**
    *   **Build from source:**
        *   Clone this repository.
        *   Open `MHWildsPathfindingBot_Refactored.sln` in Visual Studio.
        *   Build the solution. The executable will be in a subfolder like `bin\Debug\net6.0-windows\`.
    *   **(If pre-compiled binaries are provided):** Download the latest release and extract it.

## Usage

1.  **Launch the Bot:** Run `MHWildsPathfindingBot.exe`.
2.  **Configure Player Position File:**
    *   In the "NAVIGATION" panel of the bot's UI, click the "..." browse button next to "Position file:".
    *   Select the text file that your external tool is updating with player coordinates.
3.  **Navmesh Generation (Recommended First Step):**
    *   Go to the "NAVMESH GENERATION" panel.
    *   Click "START".
    *   Move your character around in Monster Hunter Wilds to explore the areas you want the bot to be able to navigate. The visualizer will update to show walkable areas.
    *   Click "STOP" when done. The navmesh data is saved automatically when stopping or closing the bot.
    *   You can "CLEAR" existing navmesh data or "OPTIMIZE" the grid.
4.  **Navigation to a Target:**
    *   In the "NAVIGATION" panel, enter the desired "Target X" and "Target Z" coordinates.
    *   Optionally, check "Simulation Mode" to only plan and visualize the path without moving the character.
    *   Click "START". The bot will calculate a path and attempt to move your character.
    *   Click "STOP" to halt navigation.
    *   Click "RECALC" to force a path recalculation if the bot seems stuck or off-course.
5.  **Waypoint Management & Harvesting:**
    *   Use the "WAYPOINTS" panel to manage harvesting routes.
    *   **Adding Waypoints:**
        *   Manually: Enter Name, X, Z, and check "Resource Node" if applicable, then click "Save" (after selecting an existing one to edit or if one is auto-selected after adding via map).
        *   Via Map: Double-click on the map in the visualizer to add a waypoint at that location.
        *   Via Player Position: Click "Add Pos" to add a waypoint at the player's current in-game position.
    *   **Editing Waypoints:** Select a waypoint from the list, modify its details in the editor fields, and click "Save".
    *   **Deleting Waypoints:** Select a waypoint and click "Delete".
    *   **Route Operations:**
        *   "Optimize": Attempts to reorder waypoints for a shorter route (simple nearest-neighbor).
        *   Import/Export: Use the "File" menu to save/load waypoint lists to/from JSON files.
    *   **Auto-Harvesting:**
        *   Load or create a waypoint route.
        *   Check the "Auto-Harvest" checkbox in the "WAYPOINTS" panel.
        *   The bot will navigate to the first waypoint in the list. When it reaches a waypoint marked as "Resource Node", it will attempt to harvest. After harvesting (or if it's not a resource node), it will proceed to the next waypoint.
        *   To start auto-harvesting, ensure "Auto-Harvest" is checked and then start the bot normally using the main "START" button in the "NAVIGATION" panel (it will use the first waypoint as its initial target).

**To ensure consistent movement, please move the camera and line it up with the north arrow in the minimap, the arrow on the edge of the minimap should be exactly north (top) of the minimap.**
## Configuration

*   **Player Position File Path:** Configurable via the UI. This is crucial for the bot to know the player's location.
*   **Internal Constants:** Various constants for pathfinding, movement, and harvesting behavior are defined within the source code (e.g., `Globals.cs`, `Bot.cs`, `HarvestingBot.cs`). Modifying these requires rebuilding the application.

## Troubleshooting

*   **Bot not moving / No controller input:**
    *   Ensure ViGEmBus driver is installed correctly.
    *   Verify Monster Hunter Wilds is the active window. The bot attempts to focus the game window named "Monster Hunter Wilds".
    *   Check the log for any errors related to controller initialization.
*   **Bot not finding paths / "No path found!":**
    *   Ensure you have generated a sufficient navmesh for the area.
    *   The target coordinates might be in an unreachable or unmapped area.
    *   Try clearing blacklisted areas if stuck detection has been aggressive.
*   **Player position not updating in the bot:**
    *   Verify that your external tool (e.g., REFramework script) is running and correctly writing coordinates to the specified file.
    *   Ensure the file path in the bot's UI matches the actual file location.

## Contributing

Contributions are welcome! Please feel free to fork the repository, make changes, and submit pull requests.
