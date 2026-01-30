using UnityEditor;
using UnityEngine;

namespace LocalMCP
{
    /// <summary>
    /// Ensures the agentic workflow is ready when Unity starts.
    /// This bootstraps the unified MCP server and AutoRefreshWatcher automatically.
    /// </summary>
    [InitializeOnLoad]
    public static class MCPBootstrap
    {
        // Use SessionState to persist across domain reloads within a single Unity session
        private static bool HasShownStartupMessage
        {
            get => SessionState.GetBool("LocalMCP_ShownStartupMessage", false);
            set => SessionState.SetBool("LocalMCP_ShownStartupMessage", value);
        }

        static MCPBootstrap()
        {
            // Run after a delay to ensure everything else is initialized
            EditorApplication.delayCall += OnFirstFrame;
        }

        private static void OnFirstFrame()
        {
            // First-time setup - server defaults to disabled
            if (!EditorPrefs.HasKey("LocalMCP_AutoStart"))
            {
                EditorPrefs.SetBool("LocalMCP_AutoStart", false);
                EditorPrefs.SetInt("LocalMCP_Port", 8090);
                Debug.Log("[LocalMCP] First-time setup: Server disabled by default. Use Tools > MCP > Control Panel to start, or run 'claude mcp add --transport http unity http://localhost:8090/mcp -s project'");
            }

            // Ensure AutoRefreshWatcher is enabled by default
            if (!EditorPrefs.HasKey("LocalMCP_AutoRefresh"))
            {
                EditorPrefs.SetBool("LocalMCP_AutoRefresh", true);
                Debug.Log("[LocalMCP] First-time setup: Auto-refresh watcher enabled");
            }

            // Start server if it should be running but isn't
            if (EditorPrefs.GetBool("LocalMCP_AutoStart", false) && !MCPServer.IsRunning)
            {
                if (!EditorApplication.isCompiling)
                {
                    EditorApplication.delayCall += () =>
                    {
                        if (!MCPServer.IsRunning)
                        {
                            MCPToolRegistry.Refresh();
                            MCPServer.Start(EditorPrefs.GetInt("LocalMCP_Port", 8090));
                        }
                    };
                }
            }

            // Show startup message once per session
            if (!HasShownStartupMessage && !EditorApplication.isCompiling)
            {
                EditorApplication.delayCall += ShowStartupStatus;
            }
        }

        private static void ShowStartupStatus()
        {
            if (HasShownStartupMessage) return;
            HasShownStartupMessage = true;

            // Wait a bit more to let everything settle
            EditorApplication.delayCall += () =>
            {
                if (MCPServer.IsRunning)
                {
                    Debug.Log($"[LocalMCP] Unified server ready - Port {MCPServer.Port}, {MCPServer.ToolCount} tools, AutoRefresh: {(AutoRefreshWatcher.IsEnabled ? "ON" : "OFF")}");
                }
            };
        }

        /// <summary>
        /// Reset all settings to defaults.
        /// </summary>
        [MenuItem("Tools/MCP/Reset to Defaults", priority = 400)]
        public static void ResetToDefaults()
        {
            // Stop everything first
            MCPServer.Stop();

            // Reset preferences
            EditorPrefs.SetBool("LocalMCP_AutoStart", false);
            EditorPrefs.SetBool("LocalMCP_AutoRefresh", true);
            EditorPrefs.SetInt("LocalMCP_Port", 8090);

            Debug.Log("[LocalMCP] Reset complete - Server disabled by default.");
        }

        /// <summary>
        /// Enable MCP auto-start and start the server now.
        /// </summary>
        [MenuItem("Tools/MCP/Enable MCP Server", priority = 199)]
        public static void EnableMCP()
        {
            EditorPrefs.SetBool("LocalMCP_AutoStart", true);
            if (!MCPServer.IsRunning)
            {
                MCPToolRegistry.Refresh();
                MCPServer.Start(EditorPrefs.GetInt("LocalMCP_Port", 8090));
            }
            Debug.Log($"[LocalMCP] Enabled - {MCPServer.ToolCount} tools now available. Run 'claude mcp add --transport http unity http://localhost:8090/mcp -s project' to connect Claude.");
        }

        /// <summary>
        /// Disable MCP auto-start and stop the server.
        /// </summary>
        [MenuItem("Tools/MCP/Disable MCP Server", priority = 201)]
        public static void DisableMCP()
        {
            EditorPrefs.SetBool("LocalMCP_AutoStart", false);
            MCPServer.Stop();
            Debug.Log("[LocalMCP] Disabled. Run 'claude mcp remove unity -s project' to disconnect from Claude.");
        }

        /// <summary>
        /// Quick status check menu item.
        /// </summary>
        [MenuItem("Tools/MCP/Check Status", priority = 401)]
        public static void CheckStatus()
        {
            var status = new System.Text.StringBuilder();
            status.AppendLine("=== MCP Server Status ===");
            status.AppendLine($"Server: {(MCPServer.IsRunning ? $"Running on port {MCPServer.Port}" : "STOPPED")}");
            status.AppendLine($"Tools Registered: {MCPServer.ToolCount}");
            status.AppendLine($"Auto-Refresh Watcher: {(AutoRefreshWatcher.IsEnabled ? "ENABLED" : "DISABLED")}");
            status.AppendLine($"Pending File Changes: {AutoRefreshWatcher.PendingChangeCount}");
            status.AppendLine($"Unity Compiling: {EditorApplication.isCompiling}");
            status.AppendLine($"Compilation Errors: {EditorUtility.scriptCompilationFailed}");
            status.AppendLine($"Play Mode: {EditorApplication.isPlaying}");
            status.AppendLine("==========================");

            Debug.Log(status.ToString());
        }
    }
}
