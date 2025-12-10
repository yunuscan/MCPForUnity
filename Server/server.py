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
        "param_string": kwargs.get("string_param", ""),
        "param_pos": kwargs.get("position", None),
        "param_rot": kwargs.get("rotation", None),
        "param_scale": kwargs.get("scale", None)
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
async def delete_object(name: str) -> str:
    """Deletes a GameObject by name."""
    return await send_ws_command("DeleteObject", name=name)

@mcp.tool()
async def add_component(object_name: str, component_name: str) -> str:
    """Adds a component to a GameObject. (e.g. Rigidbody, BoxCollider)"""
    return await send_ws_command("AddComponent", name=object_name, string_param=component_name)

@mcp.tool()
async def find_object(name: str) -> str:
    """Finds a GameObject and returns its details (Transform, Components)."""
    return await send_ws_command("FindObject", name=name)

@mcp.tool()
async def modify_transform(name: str, 
                         pos_x: float = None, pos_y: float = None, pos_z: float = None,
                         rot_x: float = None, rot_y: float = None, rot_z: float = None,
                         scale_x: float = None, scale_y: float = None, scale_z: float = None) -> str:
    """Modifies the transform (Position, Rotation, Scale) of a GameObject."""
    
    pos = {"x": pos_x, "y": pos_y, "z": pos_z} if pos_x is not None else None
    rot = {"x": rot_x, "y": rot_y, "z": rot_z} if rot_x is not None else None
    scale = {"x": scale_x, "y": scale_y, "z": scale_z} if scale_x is not None else None

    return await send_ws_command("ModifyTransform", name=name, position=pos, rotation=rot, scale=scale)

@mcp.tool()
async def get_hierarchy() -> str:
    """Gets the current scene hierarchy."""
    return await send_ws_command("GetHierarchy")

if __name__ == "__main__":
    mcp.run()
