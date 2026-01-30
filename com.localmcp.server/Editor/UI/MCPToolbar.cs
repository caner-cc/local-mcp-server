using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;

namespace LocalMCP
{
    /// <summary>
    /// Toolbar overlay showing unified MCP server status.
    /// </summary>
    [Overlay(typeof(SceneView), "MCP Status", true)]
    public class MCPToolbarOverlay : ToolbarOverlay
    {
        public MCPToolbarOverlay() : base(
            MCPStatusButton.ID
        )
        { }
    }

    /// <summary>
    /// Status button showing MCP server state.
    /// </summary>
    [EditorToolbarElement(ID, typeof(SceneView))]
    public class MCPStatusButton : EditorToolbarButton
    {
        public const string ID = "LocalMCP/StatusButton";

        private static readonly Color RunningColor = new(0.2f, 0.8f, 0.3f);
        private static readonly Color StoppedColor = new(0.8f, 0.3f, 0.2f);
        private static readonly Color CompilingColor = new(0.6f, 0.6f, 0.2f);

        public MCPStatusButton()
        {
            tooltip = "MCP Server Status - Click to open Control Panel";
            clicked += OnClick;

            EditorApplication.update += UpdateStatus;
            UpdateStatus();
        }

        private void OnClick()
        {
            MCPControlPanel.ShowWindow();
        }

        private void UpdateStatus()
        {
            bool running = MCPServer.IsRunning;

            if (EditorApplication.isCompiling)
            {
                text = "MCP: Compiling...";
                style.color = CompilingColor;
            }
            else if (running)
            {
                text = $"MCP: {MCPServer.ToolCount} tools";
                style.color = RunningColor;
            }
            else
            {
                text = "MCP: Off";
                style.color = StoppedColor;
            }
        }
    }

    /// <summary>
    /// Status bar at top-left of Scene view showing MCP server.
    /// </summary>
    [InitializeOnLoad]
    public static class MCPSceneStatus
    {
        private static GUIStyle _statusStyle;
        private static GUIStyle _buttonStyle;
        private static GUIStyle _labelStyle;

        static MCPSceneStatus()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            Handles.BeginGUI();

            if (_statusStyle == null)
            {
                _statusStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    fontSize = 10,
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(6, 6, 3, 3)
                };

                _buttonStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    fontSize = 9,
                    padding = new RectOffset(4, 4, 1, 1)
                };

                _labelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    fontSize = 9,
                    alignment = TextAnchor.MiddleLeft
                };
            }

            float margin = 10;
            float rowHeight = 20;
            float width = 180;

            // Get server state
            bool running = MCPServer.IsRunning;
            bool isCompiling = EditorApplication.isCompiling;

            // Background
            Rect bgRect = new Rect(margin, margin, width, rowHeight + 8);
            EditorGUI.DrawRect(bgRect, new Color(0.15f, 0.15f, 0.15f, 0.9f));

            // MCP row
            Rect row = new Rect(margin + 4, margin + 4, width - 8, rowHeight);
            DrawServerRow(row, "MCP", 8090, MCPServer.ToolCount, running, isCompiling, () =>
            {
                if (running)
                    MCPServer.Stop();
                else
                {
                    MCPToolRegistry.Refresh();
                    MCPServer.Start();
                }
            });

            Handles.EndGUI();
        }

        private static void DrawServerRow(Rect rect, string name, int port, int tools, bool isRunning, bool isCompiling, System.Action toggle)
        {
            // Status dot
            Color dotColor = isRunning ? new Color(0.3f, 0.9f, 0.4f) : new Color(0.5f, 0.5f, 0.5f);
            Rect dotRect = new Rect(rect.x, rect.y + 5, 10, 10);
            EditorGUI.DrawRect(dotRect, dotColor);

            // Label
            string label = isRunning ? $"{name}: {tools} tools (:{port})" : $"{name}: Off";
            _labelStyle.normal.textColor = isRunning ? Color.white : Color.gray;
            GUI.Label(new Rect(rect.x + 14, rect.y, rect.width - 60, rect.height), label, _labelStyle);

            // Toggle button
            Rect btnRect = new Rect(rect.x + rect.width - 40, rect.y + 2, 36, rect.height - 4);
            GUI.enabled = !isCompiling;
            if (GUI.Button(btnRect, isRunning ? "Off" : "On", _buttonStyle))
            {
                toggle();
            }
            GUI.enabled = true;
        }
    }
}
