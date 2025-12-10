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

            // --- Python Environment ---
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Python Environment", EditorStyles.boldLabel);

            if (GUILayout.Button("Install Requirements (pip)"))
            {
                InstallRequirements();
            }

            if (GUILayout.Button("Start Python MCP Server"))
            {
                StartPythonServer();
            }
            GUILayout.EndVertical();

            EditorGUILayout.Space();

            // --- Configuration ---
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Configuration", EditorStyles.boldLabel);

            if (GUILayout.Button("Generate VS Code Config (.vscode)"))
            {
                GenerateVSCodeConfig();
            }
            
            GUILayout.Label("Use this config for your AI Client (Claude/Cursor):", EditorStyles.miniLabel);
            EditorGUILayout.TextArea(GetMCPConfigJson(), GUILayout.Height(100));
            
            if (GUILayout.Button("Copy Config to Clipboard"))
            {
                GUIUtility.systemCopyBuffer = GetMCPConfigJson();
                UnityEngine.Debug.Log("Config copied to clipboard!");
            }

            GUILayout.EndVertical();
        }

        private void InstallRequirements()
        {
            string requirementsPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Server", "requirements.txt");
            RunCommand($"pip install -r \"{requirementsPath}\"");
        }

        private void StartPythonServer()
        {
            string serverScript = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Server", "server.py");
            RunCommand($"python \"{serverScript}\"", false);
        }

        private void GenerateVSCodeConfig()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string vscodeDir = Path.Combine(projectRoot, ".vscode");
            
            if (!Directory.Exists(vscodeDir)) Directory.CreateDirectory(vscodeDir);

            string tasksJson = @"{
    ""version"": ""2.0.0"",
    ""tasks"": [
        {
            ""label"": ""Start MCP Server"",
            ""type"": ""shell"",
            ""command"": ""python"",
            ""args"": [""Server/server.py""],
            ""presentation"": {
                ""reveal"": ""always"",
                ""panel"": ""new""
            },
            ""problemMatcher"": []
        }
    ]
}";
            File.WriteAllText(Path.Combine(vscodeDir, "tasks.json"), tasksJson);
            UnityEngine.Debug.Log("VS Code configuration generated in .vscode/tasks.json");
            EditorUtility.RevealInFinder(Path.Combine(vscodeDir, "tasks.json"));
        }

        private string GetMCPConfigJson()
        {
            string pythonPath = "python"; // Or absolute path if known
            string scriptPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Server", "server.py").Replace("\\", "/");
            
            return $@"{{
  ""mcpServers"": {{
    ""unity"": {{
      ""command"": ""{pythonPath}"",
      ""args"": [""{scriptPath}""]
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
