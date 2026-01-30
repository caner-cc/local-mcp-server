using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LocalMCP
{
    /// <summary>
    /// EditorWindow for controlling the MCP server.
    /// </summary>
    public class MCPServerWindow : EditorWindow
    {
        private int _port = 8090;
        private Vector2 _toolsScroll;
        private bool _showTools = true;
        private string _toolFilter = "";

        [MenuItem("Tools/MCP/Server Window", priority = 100)]
        public static void ShowWindow()
        {
            var window = GetWindow<MCPServerWindow>("MCP Server");
            window.minSize = new Vector2(320, 400);
        }

        [MenuItem("Tools/MCP/Start Server", priority = 200)]
        public static void StartServer()
        {
            MCPServer.Start(EditorPrefs.GetInt("LocalMCP_Port", 8090));
        }

        [MenuItem("Tools/MCP/Stop Server", priority = 201)]
        public static void StopServer()
        {
            MCPServer.Stop();
        }

        [MenuItem("Tools/MCP/Restart Server", priority = 202)]
        public static void RestartServer()
        {
            MCPServer.Restart();
        }

        [MenuItem("Tools/MCP/Refresh Tools", priority = 300)]
        public static void RefreshTools()
        {
            MCPToolRegistry.Refresh();
            Debug.Log($"[LocalMCP] Tools refreshed: {MCPToolRegistry.GetToolNames().Length} tools registered");
        }

        private void OnEnable()
        {
            _port = EditorPrefs.GetInt("LocalMCP_Port", 8090);
            MCPServer.OnServerStarted += Repaint;
            MCPServer.OnServerStopped += Repaint;
            EditorApplication.update += RepaintIfNeeded;
        }

        private void OnDisable()
        {
            MCPServer.OnServerStarted -= Repaint;
            MCPServer.OnServerStopped -= Repaint;
            EditorApplication.update -= RepaintIfNeeded;
        }

        private float _lastRepaint;
        private void RepaintIfNeeded()
        {
            // Repaint every second to update status
            if (EditorApplication.timeSinceStartup - _lastRepaint > 1f)
            {
                _lastRepaint = (float)EditorApplication.timeSinceStartup;
                Repaint();
            }
        }

        private void OnGUI()
        {
            var isRunning = MCPServer.IsRunning;
            var isCompiling = EditorApplication.isCompiling;

            // Header status bar
            DrawStatusBar(isRunning, isCompiling);

            EditorGUILayout.Space(10);

            // Server controls
            EditorGUILayout.LabelField("Server Controls", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(isRunning || isCompiling);
            EditorGUILayout.PrefixLabel("Port");
            _port = EditorGUILayout.IntField(_port, GUILayout.Width(60));
            EditorGUI.EndDisabledGroup();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(isCompiling);

            if (!isRunning)
            {
                if (GUILayout.Button("Start", GUILayout.Height(28)))
                {
                    EditorPrefs.SetInt("LocalMCP_Port", _port);
                    MCPServer.Start(_port);
                }
            }
            else
            {
                if (GUILayout.Button("Stop", GUILayout.Height(28)))
                {
                    MCPServer.Stop();
                }
                if (GUILayout.Button("Restart", GUILayout.Height(28)))
                {
                    MCPServer.Restart();
                }
            }

            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // Quick actions
            EditorGUILayout.LabelField("Quick Actions", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy Config"))
            {
                CopyConfig();
            }
            if (GUILayout.Button("Refresh Tools"))
            {
                MCPToolRegistry.Refresh();
            }
            if (GUILayout.Button("Test Connection"))
            {
                TestConnection();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // Tools list
            var tools = MCPToolRegistry.GetToolNames();
            _showTools = EditorGUILayout.Foldout(_showTools, $"Tools ({tools.Length})", true);

            if (_showTools)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Filter");
                _toolFilter = EditorGUILayout.TextField(_toolFilter);
                if (GUILayout.Button("X", GUILayout.Width(20)))
                    _toolFilter = "";
                EditorGUILayout.EndHorizontal();

                var filteredTools = string.IsNullOrEmpty(_toolFilter)
                    ? tools
                    : tools.Where(t => t.IndexOf(_toolFilter, System.StringComparison.OrdinalIgnoreCase) >= 0).ToArray();

                _toolsScroll = EditorGUILayout.BeginScrollView(_toolsScroll);

                foreach (var toolName in filteredTools)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(toolName, EditorStyles.miniLabel);
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawStatusBar(bool isRunning, bool isCompiling)
        {
            Color bgColor;
            string statusText;

            if (isCompiling)
            {
                bgColor = new Color(0.4f, 0.35f, 0.1f);
                statusText = "COMPILING - Server will auto-restart";
            }
            else if (isRunning)
            {
                bgColor = new Color(0.1f, 0.4f, 0.15f);
                statusText = $"RUNNING on :{MCPServer.Port} - {MCPServer.ToolCount} tools";
            }
            else
            {
                bgColor = new Color(0.4f, 0.15f, 0.1f);
                statusText = "STOPPED";
            }

            var rect = EditorGUILayout.GetControlRect(false, 32);
            EditorGUI.DrawRect(rect, bgColor);

            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                normal = { textColor = Color.white }
            };

            GUI.Label(rect, statusText, style);
        }

        private void CopyConfig()
        {
            var config = $@"{{
  ""mcpServers"": {{
    ""unity"": {{
      ""type"": ""http"",
      ""url"": ""http://localhost:{_port}/mcp""
    }}
  }}
}}";
            GUIUtility.systemCopyBuffer = config;
            ShowNotification(new GUIContent("Config copied!"), 1.5f);
        }

        private void TestConnection()
        {
            if (!MCPServer.IsRunning)
            {
                ShowNotification(new GUIContent("Server not running"), 1.5f);
                return;
            }

            try
            {
                var request = System.Net.WebRequest.Create($"http://localhost:{MCPServer.Port}/");
                request.Timeout = 2000;
                using var response = request.GetResponse();
                ShowNotification(new GUIContent("Connection OK!"), 1.5f);
            }
            catch (System.Exception e)
            {
                ShowNotification(new GUIContent($"Error: {e.Message}"), 2f);
            }
        }
    }
}
