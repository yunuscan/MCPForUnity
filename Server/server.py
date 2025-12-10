from mcp.server.fastmcp import FastMCP
import asyncio
import websockets
import json

# Initialize the MCP Server
mcp = FastMCP("UnityMCP")

UNITY_WS_URL = "ws://localhost:8080"

async def send_ws_command(method: str, **kwargs) -> str:
    """Helper to send WebSocket commands to Unity."""
    payload = {
        "method": method,
        # Flatten params for simple Unity JsonUtility parsing
        "param_name": kwargs.get("name", ""),
        "param_pos": kwargs.get("position", {"x":0,"y":0,"z":0})
    }
    
    try:
        async with websockets.connect(UNITY_WS_URL) as websocket:
            await websocket.send(json.dumps(payload))
            response = await websocket.recv()
            
            # Parse response
            data = json.loads(response)
            if data.get("status") == "success":
                return data.get("result", "Success")
            else:
                return f"Error: {data.get('message')}"
                
    except ConnectionRefusedError:
        return "Could not connect to Unity. Is the project open?"
    except Exception as e:
        return f"WebSocket Error: {str(e)}"

@mcp.tool()
async def ping_unity() -> str:
    """Checks if the Unity Editor is connected via WebSocket."""
    # Simple HTTP ping fallback is still available in Unity server, but let's try WS
    try:
        async with websockets.connect(UNITY_WS_URL) as websocket:
            return "Connected to Unity WebSocket Server!"
    except:
        return "Failed to connect to Unity."

@mcp.tool()
async def create_game_object(name: str, position_x: float = 0, position_y: float = 0, position_z: float = 0) -> str:
    """Creates a new GameObject in the Unity Scene."""
    return await send_ws_command("CreateObject", 
                               name=name, 
                               position={"x": position_x, "y": position_y, "z": position_z})

@mcp.tool()
async def get_hierarchy() -> str:
    """Gets the current scene hierarchy."""
    return await send_ws_command("GetHierarchy")

if __name__ == "__main__":
    mcp.run()
