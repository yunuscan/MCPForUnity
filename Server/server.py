from mcp.server.fastmcp import FastMCP
import requests
import json

# Initialize the MCP Server
mcp = FastMCP("UnityMCP")

UNITY_HOST = "http://localhost:8080"

@mcp.tool()
def ping_unity() -> str:
    """Checks if the Unity Editor is connected and listening."""
    try:
        response = requests.get(f"{UNITY_HOST}/ping", timeout=2)
        return f"Unity responded: {response.text}"
    except requests.exceptions.ConnectionError:
        return "Could not connect to Unity. Make sure the Unity project is open and the server is running."

@mcp.tool()
def create_game_object(name: str, position_x: float = 0, position_y: float = 0, position_z: float = 0) -> str:
    """Creates a new GameObject in the Unity Scene."""
    payload = {
        "action": "create_object",
        "name": name,
        "position": {"x": position_x, "y": position_y, "z": position_z}
    }
    
    try:
        response = requests.post(f"{UNITY_HOST}/execute", json=payload, timeout=5)
        return f"Unity Output: {response.text}"
    except requests.exceptions.ConnectionError:
        return "Failed to send command to Unity."

@mcp.tool()
def read_console() -> str:
    """Reads the latest logs from the Unity Editor Console."""
    try:
        response = requests.get(f"{UNITY_HOST}/console", timeout=2)
        return response.text if response.text else "No logs available."
    except requests.exceptions.ConnectionError:
        return "Could not connect to Unity."

@mcp.resource("unity://console")
def get_console_logs() -> str:
    """Resource: Access the Unity Editor Console logs as a text stream."""
    return read_console()

@mcp.resource("unity://hierarchy")
def get_scene_hierarchy() -> str:
    """Resource: Get the current scene hierarchy (Root objects and first level children)."""
    try:
        response = requests.get(f"{UNITY_HOST}/hierarchy", timeout=2)
        return response.text if response.text else "Scene is empty."
    except requests.exceptions.ConnectionError:
        return "Could not connect to Unity."

if __name__ == "__main__":
    mcp.run()
