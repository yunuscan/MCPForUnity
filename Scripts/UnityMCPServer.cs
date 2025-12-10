using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UnityMCP
{
    [InitializeOnLoad]
    public class UnityMCPServer
    {
        private static HttpListener _listener;
        private static bool _isRunning;
        public static bool IsRunning => _isRunning;
        public const string URL = "http://localhost:8080/";
        
        // WebSocket storage
        private static List<WebSocket> _clients = new List<WebSocket>();
        private static List<string> _logBuffer = new List<string>();

        static UnityMCPServer()
        {
            AssemblyReloadEvents.beforeAssemblyReload += StopServer;
            EditorApplication.quitting += StopServer;
            Application.logMessageReceived += HandleLog;
            StartServer();
        }

        [MenuItem("MCP/Restart Server")]
        public static void RestartServer()
        {
            StopServer();
            StartServer();
        }

        private static void HandleLog(string logString, string stackTrace, LogType type)
        {
            if (_logBuffer.Count > 100) _logBuffer.RemoveAt(0);
            _logBuffer.Add($"[{type}] {logString}");
        }

        public static void StartServer()
        {
            if (_isRunning) return;

            try 
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add(URL);
                _listener.Start();
                _isRunning = true;

                Debug.Log($"[UnityMCP] WebSocket Server started at {URL}");
                Task.Run(ListenLoop);
            }
            catch (Exception e)
            {
                Debug.LogError($"[UnityMCP] Failed to start server: {e.Message}");
            }
        }

        public static void StopServer()
        {
            _isRunning = false;
            if (_listener != null)
            {
                _listener.Stop();
                _listener.Close();
            }
            
            // Close all sockets
            foreach(var client in _clients)
            {
                if(client.State == WebSocketState.Open)
                    client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server stopping", CancellationToken.None);
            }
            _clients.Clear();
            
            Debug.Log("[UnityMCP] Server stopped.");
        }

        private static async Task ListenLoop()
        {
            while (_isRunning && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    if (context.Request.IsWebSocketRequest)
                    {
                        ProcessWebSocketRequest(context);
                    }
                    else
                    {
                        // Handle HTTP POST requests (REST API support)
                        if (context.Request.HttpMethod == "POST")
                        {
                            using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                            {
                                string json = await reader.ReadToEndAsync();
                                
                                // Run on main thread
                                string response = null;
                                var tcs = new TaskCompletionSource<bool>();
                                
                                EditorApplication.delayCall += () => 
                                {
                                    response = HandleMessage(json);
                                    tcs.SetResult(true);
                                };
                                
                                await tcs.Task;

                                byte[] buffer = Encoding.UTF8.GetBytes(response);
                                context.Response.ContentType = "application/json";
                                context.Response.ContentLength64 = buffer.Length;
                                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                                context.Response.Close();
                            }
                        }
                        // Fallback for simple HTTP ping
                        else if (context.Request.Url.AbsolutePath == "/ping")
                        {
                            byte[] buffer = Encoding.UTF8.GetBytes("Pong! Unity MCP Server is running (WebSocket + HTTP).");
                            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                            context.Response.Close();
                        }
                        else
                        {
                            context.Response.StatusCode = 400;
                            context.Response.Close();
                        }
                    }
                }
                catch (Exception e)
                {
                    if (_isRunning) Debug.LogError($"[UnityMCP] Error: {e.Message}");
                }
            }
        }

        private static async void ProcessWebSocketRequest(HttpListenerContext context)
        {
            WebSocketContext webSocketContext = null;
            try
            {
                webSocketContext = await context.AcceptWebSocketAsync(subProtocol: null);
                WebSocket webSocket = webSocketContext.WebSocket;
                _clients.Add(webSocket);
                
                Debug.Log("[UnityMCP] Client connected.");

                byte[] receiveBuffer = new byte[1024 * 4];

                while (webSocket.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), CancellationToken.None);
                    
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        _clients.Remove(webSocket);
                        Debug.Log("[UnityMCP] Client disconnected.");
                    }
                    else
                    {
                        string message = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);
                        // Handle message on Main Thread
                        EditorApplication.delayCall += () => 
                        {
                            string response = HandleMessage(message);
                            SendMessage(webSocket, response);
                        };
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[UnityMCP] WebSocket Error: {e.Message}");
                if(webSocketContext != null) _clients.Remove(webSocketContext.WebSocket);
            }
        }

        private static async void SendMessage(WebSocket socket, string message)
        {
            if (socket.State != WebSocketState.Open) return;
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            await socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        // --- REFLECTION BASED COMMAND HANDLING ---

        private static string HandleMessage(string json)
        {
            try
            {
                CommandData data = JsonUtility.FromJson<CommandData>(json);
                if (data == null || string.IsNullOrEmpty(data.method)) return ErrorJson("Invalid JSON");

                // Find method in this class
                MethodInfo method = typeof(UnityMCPServer).GetMethod(data.method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                if (method == null) return ErrorJson($"Method '{data.method}' not found.");

                // Invoke method
                // Note: For simplicity, we assume methods take the raw CommandData or specific params.
                // A robust system would map 'data.params' dictionary to method arguments.
                // Here we pass the full data object for manual extraction inside methods.
                object result = method.Invoke(null, new object[] { data });

                return SuccessJson(result?.ToString());
            }
            catch (Exception e)
            {
                return ErrorJson(e.InnerException?.Message ?? e.Message);
            }
        }

        // --- EXPOSED METHODS (API) ---

        private static string CreateObject(CommandData data)
        {
            string name = "New Object";
            Vector3 pos = Vector3.zero;

            // Basic param parsing (In a real lib, use a proper JSON parser to get dict)
            // Since JsonUtility is limited, we rely on the structure defined below
            if (!string.IsNullOrEmpty(data.param_name)) name = data.param_name;
            if (data.param_pos != null) pos = new Vector3(data.param_pos.x, data.param_pos.y, data.param_pos.z);

            GameObject go = new GameObject(name);
            go.transform.position = pos;
            
            return $"Created {go.name} at {pos}";
        }

        private static string GetHierarchy(CommandData data)
        {
            StringBuilder sb = new StringBuilder();
            foreach (GameObject go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                sb.AppendLine($"- {go.name}");
            }
            return sb.ToString();
        }

        private static string DeleteObject(CommandData data)
        {
            if (string.IsNullOrEmpty(data.param_name)) return "Name required";
            
            GameObject go = GameObject.Find(data.param_name);
            if (go == null) return $"Object '{data.param_name}' not found.";
            
            string name = go.name;
            GameObject.DestroyImmediate(go);
            return $"Deleted object: {name}";
        }

        private static string AddComponent(CommandData data)
        {
            if (string.IsNullOrEmpty(data.param_name)) return "Object Name required";
            if (string.IsNullOrEmpty(data.param_string)) return "Component Type required";

            GameObject go = GameObject.Find(data.param_name);
            if (go == null) return $"Object '{data.param_name}' not found.";

            // Try to find type (simplified, might need full qualified name or assembly search)
            // For built-in types, this often works if fully qualified or common
            // Better approach: Search all assemblies
            Type type = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(data.param_string);
                if (type == null) type = assembly.GetType($"UnityEngine.{data.param_string}");
                if (type != null) break;
            }

            if (type == null) return $"Component Type '{data.param_string}' not found.";

            Component comp = go.AddComponent(type);
            return $"Added component {type.Name} to {go.name}";
        }

        private static string ModifyTransform(CommandData data)
        {
            if (string.IsNullOrEmpty(data.param_name)) return "Name required";
            
            GameObject go = GameObject.Find(data.param_name);
            if (go == null) return $"Object '{data.param_name}' not found.";

            if (data.param_pos != null) 
                go.transform.position = new Vector3(data.param_pos.x, data.param_pos.y, data.param_pos.z);
            
            if (data.param_rot != null) 
                go.transform.eulerAngles = new Vector3(data.param_rot.x, data.param_rot.y, data.param_rot.z);
            
            if (data.param_scale != null) 
                go.transform.localScale = new Vector3(data.param_scale.x, data.param_scale.y, data.param_scale.z);

            return $"Modified transform of {go.name}";
        }

        private static string FindObject(CommandData data)
        {
            if (string.IsNullOrEmpty(data.param_name)) return "Name required";
            
            GameObject go = GameObject.Find(data.param_name);
            if (go == null) return "Object not found.";

            string info = $"Name: {go.name}\nPosition: {go.transform.position}\nRotation: {go.transform.eulerAngles}\nScale: {go.transform.localScale}\nComponents:";
            foreach(var c in go.GetComponents<Component>())
            {
                info += $"\n- {c.GetType().Name}";
            }
            return info;
        }

        private static string CreateScript(CommandData data)
        {
            if (string.IsNullOrEmpty(data.param_name)) return "Script Name required";
            if (string.IsNullOrEmpty(data.param_string)) return "Script Content required";

            string folderPath = "Assets/Scripts/Generated";
            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

            string fileName = data.param_name.EndsWith(".cs") ? data.param_name : data.param_name + ".cs";
            string filePath = Path.Combine(folderPath, fileName);

            File.WriteAllText(filePath, data.param_string);
            AssetDatabase.Refresh();

            return $"Script created at {filePath}. Compiling...";
        }

        private static string CreateMaterial(CommandData data)
        {
            if (string.IsNullOrEmpty(data.param_name)) return "Material Name required";

            string folderPath = "Assets/Materials/Generated";
            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

            string fileName = data.param_name.EndsWith(".mat") ? data.param_name : data.param_name + ".mat";
            string filePath = Path.Combine(folderPath, fileName);

            Material mat = new Material(Shader.Find("Standard"));
            if (data.param_pos != null) // Using param_pos as RGB color for simplicity
            {
                mat.color = new Color(data.param_pos.x, data.param_pos.y, data.param_pos.z);
            }

            AssetDatabase.CreateAsset(mat, filePath);
            AssetDatabase.SaveAssets();

            return $"Material created at {filePath}";
        }

        private static string ListAssets(CommandData data)
        {
            string path = string.IsNullOrEmpty(data.param_string) ? "Assets" : data.param_string;
            if (!Directory.Exists(path)) return $"Directory not found: {path}";

            string[] files = Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly);
            string[] dirs = Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Contents of {path}:");
            foreach (string d in dirs) sb.AppendLine($"[DIR] {Path.GetFileName(d)}");
            foreach (string f in files) 
            {
                if (!f.EndsWith(".meta")) sb.AppendLine($"[FILE] {Path.GetFileName(f)}");
            }

            return sb.ToString();
        }

        private static string ReadConsole(CommandData data)
        {
            StringBuilder sb = new StringBuilder();
            if (_logBuffer.Count == 0) return "No logs.";
            
            foreach (var log in _logBuffer)
            {
                sb.AppendLine(log);
            }
            return sb.ToString();
        }

        private static string SetPlayMode(CommandData data)
        {
            bool play = data.param_string.ToLower() == "true";
            EditorApplication.isPlaying = play;
            return $"Play mode set to {play}";
        }

        private static string ExecuteMenuItem(CommandData data)
        {
            if (string.IsNullOrEmpty(data.param_string)) return "Menu path required";
            bool result = EditorApplication.ExecuteMenuItem(data.param_string);
            return result ? $"Executed menu item: {data.param_string}" : $"Failed to execute menu item: {data.param_string}";
        }

        private static string IsCompiling(CommandData data)
        {
            return EditorApplication.isCompiling.ToString();
        }

        private static string GetSelection(CommandData data)
        {
            if (Selection.activeGameObject == null) return "None";
            return Selection.activeGameObject.name;
        }

        private static string SetSelection(CommandData data)
        {
            if (string.IsNullOrEmpty(data.param_name)) return "Name required";
            GameObject go = GameObject.Find(data.param_name);
            if (go == null) return "Object not found";
            
            Selection.activeGameObject = go;
            return $"Selected {go.name}";
        }

        private static string InspectObject(CommandData data)
        {
            if (string.IsNullOrEmpty(data.param_name)) return "Name required";
            GameObject go = GameObject.Find(data.param_name);
            if (go == null) return "Object not found";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Object: {go.name}");
            sb.AppendLine($"Layer: {LayerMask.LayerToName(go.layer)}");
            sb.AppendLine($"Tag: {go.tag}");
            sb.AppendLine("Components:");

            foreach (Component comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                Type type = comp.GetType();
                sb.AppendLine($"  [{type.Name}]");

                // Fields
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    sb.AppendLine($"    {field.Name}: {field.GetValue(comp)}");
                }
                // Properties
                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (prop.CanRead)
                    {
                        try { sb.AppendLine($"    {prop.Name}: {prop.GetValue(comp, null)}"); } catch {}
                    }
                }
            }
            return sb.ToString();
        }

        private static string GetScreenshot(CommandData data)
        {
            Camera cam = Camera.main;
            if (cam == null) return "No Main Camera found in the scene. Please add one.";

            int width = 512;
            int height = 512;
            
            RenderTexture rt = new RenderTexture(width, height, 24);
            RenderTexture currentRT = RenderTexture.active;
            RenderTexture currentCamTarget = cam.targetTexture;

            try 
            {
                cam.targetTexture = rt;
                cam.Render();
                
                RenderTexture.active = rt;
                Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                
                byte[] bytes = tex.EncodeToJPG(75);
                GameObject.DestroyImmediate(tex);
                
                string base64 = Convert.ToBase64String(bytes);
                return base64;
            }
            finally
            {
                cam.targetTexture = currentCamTarget;
                RenderTexture.active = currentRT;
                if (rt != null) GameObject.DestroyImmediate(rt);
            }
        }

        private static string SetComponentProperty(CommandData data)
        {
            // param_name: Object Name
            // param_string: Component Name
            // param_second: Property/Field Name
            // param_value: New Value (as string)

            if (string.IsNullOrEmpty(data.param_name)) return "Object Name required";
            if (string.IsNullOrEmpty(data.param_string)) return "Component Name required";
            if (string.IsNullOrEmpty(data.param_second)) return "Property Name required";
            if (data.param_value == null) return "Value required";

            GameObject go = GameObject.Find(data.param_name);
            if (go == null) return "Object not found";

            Component comp = go.GetComponent(data.param_string);
            if (comp == null) return $"Component '{data.param_string}' not found on {go.name}";

            Type type = comp.GetType();
            MemberInfo member = type.GetField(data.param_second);
            if (member == null) member = type.GetProperty(data.param_second);
            if (member == null) return $"Property/Field '{data.param_second}' not found on {type.Name}";

            try
            {
                object value = null;
                Type targetType = (member is FieldInfo f) ? f.FieldType : ((PropertyInfo)member).PropertyType;

                if (targetType == typeof(string)) value = data.param_value;
                else if (targetType == typeof(int)) value = int.Parse(data.param_value);
                else if (targetType == typeof(float)) value = float.Parse(data.param_value);
                else if (targetType == typeof(bool)) value = bool.Parse(data.param_value);
                else if (targetType.IsEnum) value = Enum.Parse(targetType, data.param_value);
                // Add more types as needed (Vector3, Color parsing is harder from single string, maybe later)

                if (value != null)
                {
                    if (member is FieldInfo field) field.SetValue(comp, value);
                    else if (member is PropertyInfo prop) prop.SetValue(comp, value, null);
                    return $"Set {data.param_second} to {value}";
                }
                else
                {
                    return $"Unsupported type: {targetType.Name}";
                }
            }
            catch (Exception e)
            {
                return $"Error setting value: {e.Message}";
            }
        }

        // --- HELPERS ---

        private static string ErrorJson(string msg) => $"{{\"status\":\"error\",\"message\":\"{msg}\"}}";
        private static string SuccessJson(string result) => $"{{\"status\":\"success\",\"result\":\"{result}\"}}";
    }

    [Serializable]
    public class CommandData
    {
        public string method;
        // Flattened params for JsonUtility simplicity
        public string param_name;
        public string param_string; // Generic string param (e.g. component name)
        public string param_second; // Extra string param (e.g. property name)
        public string param_value;  // Value param
        public Vector3Data param_pos;
        public Vector3Data param_rot;
        public Vector3Data param_scale;
    }

    [Serializable]
    public class Vector3Data
    {
        public float x, y, z;
    }
}
