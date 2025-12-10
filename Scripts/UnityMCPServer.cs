using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
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
        
        // Log storage
        private static List<string> _logs = new List<string>();
        private const int MAX_LOGS = 100;

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

            // Subscribe to logs
            Application.logMessageReceived += HandleLog;

            Debug.Log($"[UnityMCP] Server started at {URL}");
            
            // Start listening loop
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
            Application.logMessageReceived -= HandleLog;
            Debug.Log("[UnityMCP] Server stopped.");
        }

        private static void HandleLog(string logString, string stackTrace, LogType type)
        {
            lock (_logs)
            {
                string entry = $"[{DateTime.Now:HH:mm:ss}] [{type}] {logString}";
                _logs.Add(entry);
                if (_logs.Count > MAX_LOGS) _logs.RemoveAt(0);
            }
        }

        private static async Task ListenLoop()
        {
            while (_isRunning && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    ProcessRequest(context);
                }
                catch (Exception e)
                {
                    if (_isRunning) Debug.LogError($"[UnityMCP] Error: {e.Message}");
                }
            }
        }

        private static void ProcessRequest(HttpListenerContext context)
        {
            string method = context.Request.HttpMethod;
            string path = context.Request.Url.AbsolutePath;

            // 1. Ping
            if (method == "GET" && path == "/ping")
            {
                SendResponse(context, "Pong! Unity is listening.");
                return;
            }

            // 2. Console Logs
            if (method == "GET" && path == "/console")
            {
                string logsJoined;
                lock (_logs)
                {
                    logsJoined = string.Join("\n", _logs);
                }
                SendResponse(context, logsJoined);
                return;
            }

            // 3. Hierarchy (New)
            if (method == "GET" && path == "/hierarchy")
            {
                // Must run on main thread to access Unity API
                string hierarchy = "";
                bool done = false;
                
                EditorApplication.delayCall += () =>
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (GameObject go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                    {
                        sb.AppendLine($"- {go.name} (ID: {go.GetInstanceID()})");
                        foreach(Transform child in go.transform)
                        {
                            sb.AppendLine($"  - {child.name} (ID: {child.gameObject.GetInstanceID()})");
                        }
                    }
                    hierarchy = sb.ToString();
                    done = true;
                };

                // Simple wait for main thread (not production ready but works for simple example)
                // In production, use TaskCompletionSource
                int timeout = 100;
                while (!done && timeout > 0)
                {
                    System.Threading.Thread.Sleep(10);
                    timeout--;
                }
                
                SendResponse(context, hierarchy);
                return;
            }

            // 4. Execute Command
            if (method == "POST" && path == "/execute")
            {
                using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                {
                    string json = reader.ReadToEnd();
                    
                    // Dispatch to main thread
                    EditorApplication.delayCall += () =>
                    {
                        string result = HandleCommand(json);
                        SendResponse(context, result);
                    };
                }
                return;
            }

            SendResponse(context, "Unknown command", 404);
        }

        private static string HandleCommand(string json)
        {
            try
            {
                CommandData data = JsonUtility.FromJson<CommandData>(json);

                if (data == null || string.IsNullOrEmpty(data.action))
                {
                    return "Invalid JSON or missing 'action' field.";
                }

                switch (data.action)
                {
                    case "create_object":
                        GameObject go = new GameObject(string.IsNullOrEmpty(data.name) ? "New Object" : data.name);
                        if (data.position != null)
                        {
                            go.transform.position = new Vector3(data.position.x, data.position.y, data.position.z);
                        }
                        return $"Created GameObject: {go.name} (ID: {go.GetInstanceID()}) at {go.transform.position}";

                    default:
                        return $"Unknown action: {data.action}";
                }
            }
            catch (Exception e)
            {
                return $"Error executing command: {e.Message}";
            }
        }

        private static void SendResponse(HttpListenerContext context, string responseString, int statusCode = 200)
        {
            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                context.Response.StatusCode = statusCode;
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.OutputStream.Close();
            }
            catch (Exception e)
            {
                Debug.LogError($"[UnityMCP] Error sending response: {e.Message}");
            }
        }
    }

    // --- Data Structures for JSON ---

    [Serializable]
    public class CommandData
    {
        public string action;
        public string name;
        public Vector3Data position;
    }

    [Serializable]
    public class Vector3Data
    {
        public float x;
        public float y;
        public float z;
    }
}
