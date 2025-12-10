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

        private string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        private string TargetServerPath => Path.Combine(ProjectRoot, "MCPServer");

        private void OnGUI()
        {
            GUILayout.Label("Unity MCP Server Control", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // --- Server Installation ---
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("1. Server Installation", EditorStyles.boldLabel);
            
            bool serverExists = Directory.Exists(TargetServerPath);
            if (!serverExists)
            {
                EditorGUILayout.HelpBox("Server files not found in project root. Please install them first to avoid build inclusion.", MessageType.Warning);
                if (GUILayout.Button("Install Server Files to Project Root"))
                {
                    InstallServerFiles();
                }
            }
            else
            {
                EditorGUILayout.HelpBox($"Server files installed at: {TargetServerPath}", MessageType.Info);
                if (GUILayout.Button("Re-install / Update Server Files"))
                {
                    InstallServerFiles();
                }
            }
            GUILayout.EndVertical();

            if (!serverExists) return; // Stop here if not installed

            EditorGUILayout.Space();

            // --- Server Status ---
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("2. Internal Server Status (WebSocket)", EditorStyles.boldLabel);
            
            if (UnityMCPServer.IsRunning)
            {
                EditorGUILayout.HelpBox($"WebSocket Server is RUNNING at {UnityMCPServer.URL}", MessageType.Info);
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
            GUILayout.Label("3. Python Environment (uv)", EditorStyles.boldLabel);

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
            GUILayout.Label("4. Client Configuration", EditorStyles.boldLabel);

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

        private void InstallServerFiles()
        {
            // Try to find the package path
            string packagePath = Path.GetFullPath("Packages/com.yunuscan.unitymcp");
            if (!Directory.Exists(packagePath))
            {
                // Fallback for local development
                packagePath = ProjectRoot; 
            }

            string sourceServer = Path.Combine(packagePath, "Server");
            
            if (!Directory.Exists(sourceServer))
            {
                UnityEngine.Debug.LogError($"Could not find source Server folder at {sourceServer}");
                return;
            }

            if (!Directory.Exists(TargetServerPath)) Directory.CreateDirectory(TargetServerPath);

            // Copy files
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
            // Install uv via pip if not exists, then sync
            RunCommand($"pip install uv && uv pip install -r \"{requirementsPath}\" --system");
        }

        private void StartPythonServerUV()
        {
            string serverScript = Path.Combine(TargetServerPath, "server.py");
            // Use uv run to execute
            RunCommand($"uv run \"{serverScript}\"", false);
        }

        private void GenerateVSCodeConfig()
        {
            string vscodeDir = Path.Combine(ProjectRoot, ".vscode");
            
            if (!Directory.Exists(vscodeDir)) Directory.CreateDirectory(vscodeDir);

            // 1. tasks.json
            string tasksJson = @"{
    ""version"": ""2.0.0"",
    ""tasks"": [
        {
            ""label"": ""Start Unity MCP Server"",
            ""type"": ""shell"",
            ""command"": ""uv"",
            ""args"": [""run"", ""MCPServer/server.py""],
            ""presentation"": {
                ""reveal"": ""always"",
                ""panel"": ""new""
            },
            ""problemMatcher"": []
        }
    ]
}";
            File.WriteAllText(Path.Combine(vscodeDir, "tasks.json"), tasksJson);

            // 2. mcp.json
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
