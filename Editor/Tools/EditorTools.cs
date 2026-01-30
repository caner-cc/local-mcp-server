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
                applicationPath = EditorApplication.applicationPath,
                unityVersion = Application.unityVersion
            };
        }

        [MCPTool("execute_menu", "Execute a Unity menu item")]
        [MCPParam("menuPath", "string", "Full menu path (e.g., 'File/Save Project')")]
        public static object ExecuteMenu(JObject args)
        {
            var menuPath = args["menuPath"]?.ToString();
            if (string.IsNullOrEmpty(menuPath))
            {
                return new { success = false, message = "menuPath is required" };
            }

            bool executed = EditorApplication.ExecuteMenuItem(menuPath);
            return new
            {
                success = executed,
                message = executed ? $"Executed: {menuPath}" : $"Menu item not found: {menuPath}"
            };
        }

        [MCPTool("set_selection", "Select objects in the Unity Editor")]
        [MCPParam("paths", "array", "Array of hierarchy paths or asset paths to select")]
        public static object SetSelection(JObject args)
        {
            var paths = args["paths"]?.ToObject<string[]>();
            if (paths == null || paths.Length == 0)
            {
                Selection.activeObject = null;
                return new { success = true, message = "Selection cleared" };
            }

            var objects = new System.Collections.Generic.List<Object>();
            foreach (var path in paths)
            {
                // Try as asset path first
                var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
                if (asset != null)
                {
                    objects.Add(asset);
                    continue;
                }

                // Try as hierarchy path
                var go = GameObject.Find(path);
                if (go != null)
                {
                    objects.Add(go);
                }
            }

            if (objects.Count > 0)
            {
                Selection.objects = objects.ToArray();
                return new { success = true, message = $"Selected {objects.Count} object(s)" };
            }

            return new { success = false, message = "No objects found at specified paths" };
        }

        [MCPTool("get_selection", "Get currently selected objects in Unity Editor")]
        public static object GetSelection(JObject args)
        {
            var selected = Selection.gameObjects;
            var selectedAssets = Selection.objects;

            var gameObjects = new System.Collections.Generic.List<object>();
            foreach (var go in selected)
            {
                gameObjects.Add(new
                {
                    name = go.name,
                    path = GetGameObjectPath(go),
                    instanceId = go.GetInstanceID()
                });
            }

            var assets = new System.Collections.Generic.List<object>();
            foreach (var obj in selectedAssets)
            {
                if (obj is GameObject) continue; // Already in gameObjects list
                var assetPath = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    assets.Add(new
                    {
                        name = obj.name,
                        type = obj.GetType().Name,
                        path = assetPath
                    });
                }
            }

            return new
            {
                gameObjectCount = gameObjects.Count,
                assetCount = assets.Count,
                gameObjects,
                assets
            };
        }

        private static string GetGameObjectPath(GameObject go)
        {
            string path = go.name;
            var parent = go.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }
    }
}
