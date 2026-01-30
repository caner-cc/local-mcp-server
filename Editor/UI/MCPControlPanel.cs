using UnityEditor;
using UnityEngine;

namespace LocalMCP
{
    /// <summary>
    /// Control panel for the unified MCP server.
    /// Provides status indicators and server control.
    /// </summary>
    public class MCPControlPanel : EditorWindow
    {
        private static readonly Color RunningColor = new(0.2f, 0.8f, 0.3f);
        private static readonly Color StoppedColor = new(0.8f, 0.3f, 0.2f);

        private GUIStyle _headerStyle;
        private GUIStyle _statusStyle;
        private GUIStyle _commandStyle;
        private Vector2 _scrollPos;

        [MenuItem("Tools/MCP/Control Panel", priority = 0)]
        public static void ShowWindow()
        {
            var window = GetWindow<MCPControlPanel>("MCP Control");
            window.minSize = new Vector2(320, 400);
        }

        private void OnEnable()
        {
            // Refresh periodically
            EditorApplication.update += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Repaint;
        }

        private void InitStyles()
        {
            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 14,
                    margin = new RectOffset(0, 0, 10, 5)
                };

                _statusStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    fontSize = 11,
                    padding = new RectOffset(10, 10, 8, 8),
                    richText = true
                };

                _commandStyle = new GUIStyle(EditorStyles.textField)
                {
                    fontSize = 10,
                    wordWrap = true
                };
            }
        }

        private void OnGUI()
        {
            InitStyles();
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            // Header
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("MCP Server Control", _headerStyle);

            if (EditorApplication.isCompiling)
            {
                EditorGUILayout.HelpBox("Unity is compiling... Server may restart automatically.", MessageType.Warning);
            }

            EditorGUILayout.Space(10);

            // MCP Server Section
            DrawMCPSection();

            EditorGUILayout.Space(15);

            // Cache Section
            DrawCacheSection();

            EditorGUILayout.Space(15);

            // Claude CLI Commands Section
            DrawClaudeCommandsSection();

            EditorGUILayout.Space(15);

            // Emergency Section
            DrawEmergencySection();

            EditorGUILayout.EndScrollView();
        }

        private void DrawEmergencySection()
        {
            EditorGUILayout.LabelField("Emergency Controls", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(_statusStyle);

            int queuedRequests = MCPServer.QueuedRequests;

            EditorGUILayout.LabelField("Queued Requests:", queuedRequests.ToString());

            if (queuedRequests > 5)
            {
                EditorGUILayout.HelpBox(
                    $"Request queue has {queuedRequests} pending requests. This may indicate Unity is slow to respond. " +
                    "Consider clearing the queue or force stopping MCP.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Clear Queue", GUILayout.Height(25)))
            {
                int cleared = MCPServer.ClearRequestQueue();
                Debug.Log($"[MCP] Cleared {cleared} queued requests");
            }

            GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
            if (GUILayout.Button("Force Stop MCP", GUILayout.Height(25)))
            {
                MCPServer.ForceStop();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            EditorGUILayout.HelpBox(
                "If Unity hangs frequently, try:\n" +
                "1. Use 'Clear Queue' to drop pending requests\n" +
                "2. Reduce concurrent MCP requests\n" +
                "3. Use 'path' parameter in find_missing_refs\n" +
                "4. Use smaller limits for asset searches",
                MessageType.Info);

            EditorGUILayout.EndVertical();
        }

        private void DrawMCPSection()
        {
            bool running = MCPServer.IsRunning;
            int port = MCPServer.Port;
            int tools = MCPServer.ToolCount;

            // Header with status indicator
            EditorGUILayout.BeginHorizontal();
            DrawStatusDot(running);
            EditorGUILayout.LabelField("Unified MCP Server", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginVertical(_statusStyle);

            // Status
            EditorGUILayout.LabelField("Port:", port.ToString());
            EditorGUILayout.LabelField("Status:", running ? "<color=#50C878>Running</color>" : "<color=#FF6B6B>Stopped</color>", _statusStyle);
            EditorGUILayout.LabelField("Tools:", running ? $"{tools} tools available" : "N/A (not running)");

            EditorGUILayout.Space(5);

            // Auto-start toggle
            bool autoStart = EditorPrefs.GetBool("LocalMCP_AutoStart", false);
            bool newAutoStart = EditorGUILayout.Toggle("Auto-Start", autoStart);
            if (newAutoStart != autoStart)
            {
                EditorPrefs.SetBool("LocalMCP_AutoStart", newAutoStart);
            }

            EditorGUILayout.Space(5);

            // Tool categories hint
            if (running)
            {
                EditorGUILayout.HelpBox(
                    "Tool Categories: Assets, Scene, Inspector, SceneView, Debug, Console, Editor, Agentic",
                    MessageType.Info);
            }

            // Control buttons
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !EditorApplication.isCompiling;

            if (running)
            {
                if (GUILayout.Button("Stop", GUILayout.Height(25)))
                {
                    MCPServer.Stop();
                }
                if (GUILayout.Button("Restart", GUILayout.Height(25)))
                {
                    MCPServer.Restart();
                }
            }
            else
            {
                if (GUILayout.Button("Start", GUILayout.Height(25)))
                {
                    MCPToolRegistry.Refresh();
                    MCPServer.Start();
                }
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawCacheSection()
        {
            EditorGUILayout.LabelField("Asset Cache", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(_statusStyle);

            var stats = AssetCache.Instance.GetStats();

            // Use reflection to get stats values safely
            var statsType = stats.GetType();
            var totalAssets = statsType.GetProperty("totalAssets")?.GetValue(stats);
            var cacheAge = statsType.GetProperty("cacheAge")?.GetValue(stats);

            EditorGUILayout.LabelField("Cached Assets:", totalAssets?.ToString() ?? "N/A");
            EditorGUILayout.LabelField("Cache Age:", cacheAge != null ? $"{cacheAge:F1}s" : "N/A");

            EditorGUILayout.Space(5);

            if (GUILayout.Button("Refresh Cache"))
            {
                AssetCache.Instance.ForceRefresh();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawClaudeCommandsSection()
        {
            EditorGUILayout.LabelField("Claude CLI Commands", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(_statusStyle);

            EditorGUILayout.LabelField("Connect MCP to Claude:", EditorStyles.miniLabel);
            string connectCmd = "claude mcp add --transport http unity http://localhost:8090/mcp -s project";
            EditorGUILayout.SelectableLabel(connectCmd, _commandStyle, GUILayout.Height(35));

            if (GUILayout.Button("Copy Connect Command"))
            {
                GUIUtility.systemCopyBuffer = connectCmd;
                Debug.Log("[MCP] Connect command copied to clipboard");
            }

            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("Disconnect MCP from Claude:", EditorStyles.miniLabel);
            string disconnectCmd = "claude mcp remove unity -s project";
            EditorGUILayout.SelectableLabel(disconnectCmd, _commandStyle, GUILayout.Height(20));

            if (GUILayout.Button("Copy Disconnect Command"))
            {
                GUIUtility.systemCopyBuffer = disconnectCmd;
                Debug.Log("[MCP] Disconnect command copied to clipboard");
            }

            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("Check MCP Status:", EditorStyles.miniLabel);
            string statusCmd = "claude mcp list";
            EditorGUILayout.SelectableLabel(statusCmd, _commandStyle, GUILayout.Height(20));

            EditorGUILayout.EndVertical();
        }

        private void DrawStatusDot(bool isRunning)
        {
            Rect rect = GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16), GUILayout.Height(16));
            rect.y += 2;

            Color color = isRunning ? RunningColor : StoppedColor;

            // Draw outer circle
            Handles.BeginGUI();
            Handles.color = color;
            Handles.DrawSolidDisc(rect.center, Vector3.forward, 6);
            Handles.color = Color.white * 0.8f;
            Handles.DrawWireDisc(rect.center, Vector3.forward, 6);
            Handles.EndGUI();
        }
    }
}
