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
        "param_second": kwargs.get("second_param", ""),
        "param_value": kwargs.get("value_param", ""),
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

@mcp.tool()
async def create_script(script_name: str, content: str) -> str:
    """Creates a new C# script in Assets/Scripts/Generated.
    Args:
        script_name: Name of the class/file (e.g. 'MyScript')
        content: The full C# code content.
    """
    return await send_ws_command("CreateScript", name=script_name, string_param=content)

@mcp.tool()
async def create_material(material_name: str, r: float = 1, g: float = 1, b: float = 1) -> str:
    """Creates a new Material in Assets/Materials/Generated with a specific color."""
    return await send_ws_command("CreateMaterial", name=material_name, position={"x":r, "y":g, "z":b})

@mcp.tool()
async def list_assets(path: str = "Assets") -> str:
    """Lists files and directories in the project."""
    return await send_ws_command("ListAssets", string_param=path)

@mcp.tool()
async def read_console() -> str:
    """Reads the last 100 log messages from the Unity Console."""
    return await send_ws_command("ReadConsole")

@mcp.tool()
async def set_play_mode(active: bool) -> str:
    """Enables or disables Play Mode in the Unity Editor."""
    return await send_ws_command("SetPlayMode", string_param=str(active).lower())

@mcp.tool()
async def execute_menu_item(menu_path: str) -> str:
    """Executes a Unity Editor menu item (e.g. 'Assets/Refresh', 'Window/General/Game')."""
    return await send_ws_command("ExecuteMenuItem", string_param=menu_path)

@mcp.tool()
async def is_compiling() -> str:
    """Checks if the Unity Editor is currently compiling scripts."""
    return await send_ws_command("IsCompiling")

@mcp.tool()
async def get_selection() -> str:
    """Gets the name of the currently selected GameObject in the Editor."""
    return await send_ws_command("GetSelection")

@mcp.tool()
async def set_selection(name: str) -> str:
    """Selects a GameObject in the Editor by name."""
    return await send_ws_command("SetSelection", name=name)

@mcp.tool()
async def inspect_object(name: str) -> str:
    """Inspects a GameObject, listing all components and their public fields/properties."""
    return await send_ws_command("InspectObject", name=name)

@mcp.tool()
async def get_screenshot() -> str:
    """Captures a screenshot from the Main Camera and returns it as a Base64 encoded JPG string.
    Useful for 'seeing' the current state of the scene.
    """
    return await send_ws_command("GetScreenshot")

@mcp.tool()
async def set_component_property(object_name: str, component_name: str, property_name: str, value: str) -> str:
    """Sets a property or field value on a component.
    Args:
        object_name: Name of the GameObject.
        component_name: Name of the Component (e.g. 'Light', 'Rigidbody').
        property_name: Name of the field/property (e.g. 'color', 'mass', 'intensity').
        value: The new value as a string (e.g. '10', '5.5', 'true').
    """
    return await send_ws_command("SetComponentProperty", 
                               name=object_name, 
                               string_param=component_name, 
                               second_param=property_name, 
                               value_param=value)

if __name__ == "__main__":
    # Run as SSE server for better compatibility with VS Code / Cursor
    mcp.run(transport="sse")
