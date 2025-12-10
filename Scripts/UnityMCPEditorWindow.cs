using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityMCP
{
    public class UnityMCPEditorWindow : EditorWindow
    {
        // UI State
        private bool showDebugLogs = false;
        private ValidationLevel validationLevel = ValidationLevel.Standard;
        private bool showAdvancedSettings = false;
        private bool showManualConfig = false;
        private TransportType transportType = TransportType.WebSocket;
        private string serverUrl = "ws://localhost:8080";
        private ClientType selectedClient = ClientType.Cursor;
        
        private enum ValidationLevel { Basic, Standard, Strict }
        private enum TransportType { HTTP, WebSocket }
        private enum ClientType { Cursor, VSCode, ClaudeDesktop }

        private string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        private string TargetServerPath => Path.Combine(ProjectRoot, "MCPServer");

        [MenuItem("MCP/Dashboard")]
        public static void ShowWindow()
        {
            GetWindow<UnityMCPEditorWindow>("MCP For Unity");
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawSettings();
            DrawConnection();
            DrawClientConfig();
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space();
            GUILayout.Label("MCP For Unity", new GUIStyle(EditorStyles.largeLabel) { fontSize = 20, fontStyle = FontStyle.Bold });
            EditorGUILayout.Space();
        }

        private void DrawSettings()
        {
            GUILayout.Label("Settings", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Version:", GUILayout.Width(150));
                    EditorGUILayout.LabelField("v1.0.0", EditorStyles.miniLabel);
                }

                showDebugLogs = EditorGUILayout.Toggle("Show Debug Logs:", showDebugLogs);
                validationLevel = (ValidationLevel)EditorGUILayout.EnumPopup("Script Validation Level:", validationLevel);
                EditorGUILayout.HelpBox("Syntax checks + Unity best practices and warnings", MessageType.None);

                showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "Advanced Settings");
                if (showAdvancedSettings)
                {
                    if (GUILayout.Button("Install Server Files to Project Root")) InstallServerFiles();
                    if (GUILayout.Button("Install 'uv' and Requirements")) InstallUVAndRequirements();
                }
            }
            EditorGUILayout.Space();
        }

        private void DrawConnection()
        {
            GUILayout.Label("Connection", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                transportType = (TransportType)EditorGUILayout.EnumPopup("Transport:", transportType);
                serverUrl = EditorGUILayout.TextField("URL:", serverUrl);

                EditorGUILayout.LabelField("Use this command to launch the server manually:");
                string cmd = $"uv run --directory \"{TargetServerPath}\" server.py";
                
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.TextArea(cmd, GUILayout.Height(40));
                    if (GUILayout.Button("Copy", GUILayout.Width(50), GUILayout.Height(40)))
                    {
                        GUIUtility.systemCopyBuffer = cmd;
                        UnityEngine.Debug.Log("Command copied to clipboard!");
                    }
                }

                EditorGUILayout.HelpBox("Run this command in your shell if you prefer to start the server manually.", MessageType.None);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Start Internal Server")) UnityMCPServer.StartServer();
                    if (GUILayout.Button("Stop Internal Server")) UnityMCPServer.StopServer();
                }

                // Status Indicators
                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope())
                {
                    Color statusColor = UnityMCPServer.IsRunning ? Color.green : Color.red;
                    string statusText = UnityMCPServer.IsRunning ? "Running" : "Stopped";
                    
                    var style = new GUIStyle(EditorStyles.label);
                    style.normal.textColor = statusColor;
                    
                    GUILayout.Label("●", style, GUILayout.Width(20));
                    GUILayout.Label($"Internal Server: {statusText}");
                    
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Restart Session")) UnityMCPServer.RestartServer();
                }
                
                using (new EditorGUILayout.HorizontalScope())
                {
                     GUILayout.Label("Health:");
                     GUILayout.Label(UnityMCPServer.IsRunning ? "Healthy" : "Unknown"); 
                     GUILayout.FlexibleSpace();
                     if (GUILayout.Button("Test")) UnityEngine.Debug.Log("Test Ping...");
                }
            }
            EditorGUILayout.Space();
        }

        private void DrawClientConfig()
        {
            GUILayout.Label("Client Configuration", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                selectedClient = (ClientType)EditorGUILayout.EnumPopup("Client:", selectedClient);
                
                if (GUILayout.Button("Configure All Detected Clients"))
                {
                    GenerateVSCodeConfig();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    bool configured = File.Exists(Path.Combine(ProjectRoot, ".vscode/mcp.json"));
                    Color statusColor = configured ? Color.green : Color.red;
                    string statusText = configured ? "Configured" : "Not Configured";

                    var style = new GUIStyle(EditorStyles.label);
                    style.normal.textColor = statusColor;

                    GUILayout.Label("●", style, GUILayout.Width(20));
                    GUILayout.Label(statusText);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Configure")) GenerateVSCodeConfig();
                }

                showManualConfig = EditorGUILayout.Foldout(showManualConfig, "Manual Configuration");
                if (showManualConfig)
                {
                    EditorGUILayout.TextArea(GetMCPConfigJson(), GUILayout.Height(100));
                    if (GUILayout.Button("Copy Config"))
                    {
                        GUIUtility.systemCopyBuffer = GetMCPConfigJson();
                    }
                }
            }
        }

        // --- Helpers ---

        private void InstallServerFiles()
        {
            string packagePath = Path.GetFullPath("Packages/com.yunuscan.unitymcp");
            if (!Directory.Exists(packagePath)) packagePath = ProjectRoot; 

            string sourceServer = Path.Combine(packagePath, "Server");
            
            if (!Directory.Exists(sourceServer))
            {
                // Try Assets/MCP/Server if package not found (dev mode)
                sourceServer = Path.Combine(ProjectRoot, "Assets/MCP/Server");
                if(!Directory.Exists(sourceServer))
                {
                     UnityEngine.Debug.LogError($"Could not find source Server folder.");
                     return;
                }
            }

            if (!Directory.Exists(TargetServerPath)) Directory.CreateDirectory(TargetServerPath);

            foreach (string file in Directory.GetFiles(sourceServer))
            {
                if (file.EndsWith(".meta")) continue;
                string dest = Path.Combine(TargetServerPath, Path.GetFileName(file));
                File.Copy(file, dest, true);
            }

            UnityEngine.Debug.Log($"Server files installed to {TargetServerPath}");
        }

        private void InstallUVAndRequirements()
        {
            string requirementsPath = Path.Combine(TargetServerPath, "requirements.txt");
            RunCommand($"pip install uv && uv pip install -r \"{requirementsPath}\" --system");
        }

        private void GenerateVSCodeConfig()
        {
            string vscodeDir = Path.Combine(ProjectRoot, ".vscode");
            if (!Directory.Exists(vscodeDir)) Directory.CreateDirectory(vscodeDir);

            string mcpJson = GetMCPConfigJson();
            File.WriteAllText(Path.Combine(vscodeDir, "mcp.json"), mcpJson);

            UnityEngine.Debug.Log($"VS Code configuration generated in {vscodeDir}");
            EditorUtility.RevealInFinder(vscodeDir);
        }

        private string GetMCPConfigJson()
        {
            string scriptPath = Path.Combine(TargetServerPath, "server.py").Replace("\\", "/");
            return $@"{{
  ""mcpServers"": {{
    ""unity"": {{
      ""command"": ""uv"",
      ""args"": [""run"", ""{scriptPath}""]
    }}
  }}
}}";
        }

        private void RunCommand(string command, bool waitForExit = true)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                UseShellExecute = true,
                CreateNoWindow = waitForExit
            };

            if (!waitForExit) startInfo.WindowStyle = ProcessWindowStyle.Normal;

            try
            {
                Process process = Process.Start(startInfo);
                if (waitForExit) process.WaitForExit();
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"Failed to run command: {e.Message}");
            }
        }
    }
}
