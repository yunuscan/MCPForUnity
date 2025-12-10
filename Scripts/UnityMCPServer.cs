using System;
using System.Collections.Generic;
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

        static UnityMCPServer()
        {
            StartServer();
            EditorApplication.quitting += StopServer;
        }

        public static void StartServer()
        {
            if (_isRunning) return;

            _listener = new HttpListener();
            _listener.Prefixes.Add(URL);
            _listener.Start();
            _isRunning = true;

            Debug.Log($"[UnityMCP] WebSocket Server started at {URL}");
            
            Task.Run(ListenLoop);
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
                        // Fallback for simple HTTP ping
                        if (context.Request.Url.AbsolutePath == "/ping")
                        {
                            byte[] buffer = Encoding.UTF8.GetBytes("Pong! Unity WebSocket Server is running.");
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
