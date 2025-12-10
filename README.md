# MCP For Unity

This project implements a Model Context Protocol (MCP) server that acts as a bridge to the Unity Editor.

## Structure

- **Server/**: Contains the Python MCP server that AI clients connect to.
- **Scripts/**: Contains the Unity C# scripts.
- **package.json**: Unity Package definition file.

## Installation

### 1. Unity Setup (via Package Manager)
You can install the Unity integration directly from GitHub using the Unity Package Manager.

1. Open Unity.
2. Go to **Window > Package Manager**.
3. Click the **+** button in the top-left corner.
4. Select **Add package from git URL...**.
5. Enter the following URL:
   ```
   https://github.com/yunuscan/MCPForUnity.git
   ```
6. Click **Add**.

### 2. Python Server Setup (via Unity Editor)
1. In Unity, go to **UnityMCP > Dashboard**.
2. Click **Install 'uv' and Requirements**.
3. Click **Start Python MCP Server (uv run)** to test the connection.

### 3. Client Configuration (VS Code / Copilot / Claude)
To use this with your AI assistant:

1. Open the **UnityMCP > Dashboard** window.
2. Click **Configure for VS Code (Copilot)**.
   - This will generate a `.vscode/mcp.json` file in your project root.
   - It will also create a `tasks.json` to easily run the server.
3. Alternatively, copy the JSON config manually for Claude Desktop or Cursor.

## Usage
1. Ensure the Unity project is open.
2. Connect your AI client using the generated configuration.
3. Ask your AI to "Create a cube" or "Check console logs".


