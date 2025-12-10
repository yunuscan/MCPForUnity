# MCP For Unity

This project implements a Model Context Protocol (MCP) server that acts as a bridge to the Unity Editor.

## Structure

- **Server/**: Contains the Python MCP server that AI clients connect to.
- **UnityPackage/**: Contains the Unity Package (C# scripts) to be installed in your Unity project.

## Installation

### 1. Unity Setup (via Package Manager)
You can install the Unity integration directly from GitHub using the Unity Package Manager.

1. Open Unity.
2. Go to **Window > Package Manager**.
3. Click the **+** button in the top-left corner.
4. Select **Add package from git URL...**.
5. Enter the following URL:
   ```
   https://github.com/yunuscan/MCPForUnity.git?path=/UnityPackage
   ```
6. Click **Add**.

### 2. Python Server Setup
1. Install dependencies:
   ```bash
   pip install -r Server/requirements.txt
   ```
2. Run the server:
   ```bash
   python Server/server.py
   ```

## Usage
1. Ensure the Unity project is open and the package is installed. You should see `[UnityMCP] Server started...` in the Unity Console.
2. Run the Python server.
3. Connect your AI client (e.g., Claude Desktop, Cursor) to the MCP server.

