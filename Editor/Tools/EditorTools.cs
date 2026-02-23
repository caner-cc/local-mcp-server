using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace LocalMCP.Tools
{
    /// <summary>
    /// Tools for controlling the Unity Editor.
    /// </summary>
    public static class EditorTools
    {
        [MCPTool("editor_control", "Control Unity Editor play mode and refresh assets")]
        [MCPParam("action", "string", "Action: play, pause, stop, step, refresh")]
        public static object EditorControl(JObject args)
        {
            var action = args["action"]?.ToString()?.ToLower();

            switch (action)
            {
                case "play":
                    if (!EditorApplication.isPlaying)
                    {
                        EditorApplication.isPlaying = true;
                        return new { success = true, message = "Entered play mode" };
                    }
                    return new { success = true, message = "Already in play mode" };

                case "pause":
                    if (EditorApplication.isPlaying)
                    {
                        EditorApplication.isPaused = !EditorApplication.isPaused;
                        return new { success = true, message = EditorApplication.isPaused ? "Paused" : "Resumed" };
                    }
                    return new { success = false, message = "Not in play mode" };

                case "stop":
                    if (EditorApplication.isPlaying)
                    {
                        EditorApplication.isPlaying = false;
                        return new { success = true, message = "Stopped play mode" };
                    }
                    return new { success = true, message = "Already stopped" };

                case "step":
                    if (EditorApplication.isPlaying && EditorApplication.isPaused)
                    {
                        EditorApplication.Step();
                        return new { success = true, message = "Stepped one frame" };
                    }
                    return new { success = false, message = "Must be playing and paused to step" };

                case "refresh":
                    AssetDatabase.Refresh();
                    return new { success = true, message = "Asset database refreshed" };

                default:
                    return new { success = false, message = $"Unknown action: {action}. Use: play, pause, stop, step, refresh" };
            }
        }

        [MCPTool("editor_state", "Get current Unity Editor state")]
        public static object EditorState(JObject args)
        {
            return new
            {
                isPlaying = EditorApplication.isPlaying,
                isPaused = EditorApplication.isPaused,
                isCompiling = EditorApplication.isCompiling,
                currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                currentScenePath = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
                timeSinceStartup = EditorApplication.timeSinceStartup,
                unityVersion = Application.unityVersion
            };
        }
    }
}
