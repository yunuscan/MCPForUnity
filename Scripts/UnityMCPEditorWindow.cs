using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityMCP
{
    public class UnityMCPEditorWindow : EditorWindow
    {
        [MenuItem("UnityMCP/Dashboard")]
        public static void ShowWindow()
        {
            GetWindow<UnityMCPEditorWindow>("UnityMCP");
        }

        private void OnGUI()
        {
            GUILayout.Label("Unity MCP Server Control", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // --- Server Status ---
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Internal Server Status", EditorStyles.boldLabel);
            
            if (UnityMCPServer.IsRunning)
            {
                EditorGUILayout.HelpBox($"Server is RUNNING at {UnityMCPServer.URL}", MessageType.Info);
                if (GUILayout.Button("Stop Server"))
                {
                    UnityMCPServer.StopServer();
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Server is STOPPED", MessageType.Warning);
                if (GUILayout.Button("Start Server"))
                {
                    UnityMCPServer.StartServer();
                }
            }
            GUILayout.EndVertical();

            EditorGUILayout.Space();

            // --- Python Environment (uv) ---
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Python Environment (uv)", EditorStyles.boldLabel);

            if (GUILayout.Button("Install 'uv' and Requirements"))
            {
                InstallUVAndRequirements();
            }

            if (GUILayout.Button("Start Python MCP Server (uv run)"))
            {
                StartPythonServerUV();
            }
            GUILayout.EndVertical();

            EditorGUILayout.Space();

            // --- Client Configuration ---
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Client Configuration", EditorStyles.boldLabel);

            if (GUILayout.Button("Configure for VS Code (Copilot)"))
            {
                GenerateVSCodeConfig();
            }
            
            GUILayout.Label("Manual Config (Claude/Cursor):", EditorStyles.miniLabel);
            EditorGUILayout.TextArea(GetMCPConfigJson(), GUILayout.Height(100));
            
            if (GUILayout.Button("Copy Config to Clipboard"))
            {
                GUIUtility.systemCopyBuffer = GetMCPConfigJson();
                UnityEngine.Debug.Log("Config copied to clipboard!");
            }

            GUILayout.EndVertical();
        }

        private void InstallUVAndRequirements()
        {
            string requirementsPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Server", "requirements.txt");
            // Install uv via pip if not exists, then sync
            RunCommand($"pip install uv && uv pip install -r \"{requirementsPath}\" --system");
        }

        private void StartPythonServerUV()
        {
            string serverScript = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Server", "server.py");
            // Use uv run to execute
            RunCommand($"uv run \"{serverScript}\"", false);
        }

        private void GenerateVSCodeConfig()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string vscodeDir = Path.Combine(projectRoot, ".vscode");
            
            if (!Directory.Exists(vscodeDir)) Directory.CreateDirectory(vscodeDir);

            // 1. tasks.json
            string tasksJson = @"{
    ""version"": ""2.0.0"",
    ""tasks"": [
        {
            ""label"": ""Start Unity MCP Server"",
            ""type"": ""shell"",
            ""command"": ""uv"",
            ""args"": [""run"", ""Server/server.py""],
            ""presentation"": {
                ""reveal"": ""always"",
                ""panel"": ""new""
            },
            ""problemMatcher"": []
        }
    ]
}";
            File.WriteAllText(Path.Combine(vscodeDir, "tasks.json"), tasksJson);

            // 2. mcp.json (Standard for some extensions) or settings.json
            // For GitHub Copilot, we might need to wait for official support, but we can generate a standard config.
            string mcpJson = GetMCPConfigJson();
            File.WriteAllText(Path.Combine(vscodeDir, "mcp.json"), mcpJson);

            UnityEngine.Debug.Log($"VS Code configuration generated in {vscodeDir}");
            EditorUtility.RevealInFinder(vscodeDir);
        }

        private string GetMCPConfigJson()
        {
            string scriptPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Server", "server.py").Replace("\\", "/");
            
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
                UseShellExecute = true, // Use shell to open new window for server
                CreateNoWindow = waitForExit // Hide window if just installing
            };

            if (!waitForExit)
            {
                startInfo.WindowStyle = ProcessWindowStyle.Normal;
            }

            try
            {
                Process process = Process.Start(startInfo);
                if (waitForExit)
                {
                    process.WaitForExit();
                    UnityEngine.Debug.Log($"Command executed: {command}");
                }
                else
                {
                    UnityEngine.Debug.Log($"Command started: {command}");
                }
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"Failed to run command: {e.Message}");
            }
        }
    }
}
