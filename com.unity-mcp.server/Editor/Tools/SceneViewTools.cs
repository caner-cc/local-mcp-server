using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMCP.Tools
{
    /// <summary>
    /// Tools for controlling the Scene View.
    /// </summary>
    public static class SceneViewTools
    {
        [MCPTool("sceneview_lookat", "Move Scene View to look at a position or object")]
        [MCPParam("target", "string", "GameObject path to look at (optional)", false)]
        [MCPParam("position", "object", "Position {x, y, z} to look at (optional)", false)]
        [MCPParam("instant", "boolean", "Instant move vs animated (default: false)", false)]
        public static object SceneViewLookAt(JObject args)
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null)
            {
                return new { success = false, message = "No active Scene View" };
            }

            var target = args["target"]?.ToString();
            var posObj = args["position"] as JObject;
            var instant = args["instant"]?.ToObject<bool>() ?? false;

            Vector3 position;
            float size = 10f;

            if (!string.IsNullOrEmpty(target))
            {
                var go = GameObject.Find(target);
                if (go == null)
                {
                    return new { success = false, message = $"GameObject not found: {target}" };
                }

                // Calculate bounds
                var bounds = new Bounds(go.transform.position, Vector3.zero);
                foreach (var renderer in go.GetComponentsInChildren<Renderer>())
                {
                    bounds.Encapsulate(renderer.bounds);
                }
                position = bounds.center;
                size = bounds.size.magnitude * 1.5f;
                if (size < 1f) size = 5f;
            }
            else if (posObj != null)
            {
                position = new Vector3(
                    posObj["x"]?.ToObject<float>() ?? 0,
                    posObj["y"]?.ToObject<float>() ?? 0,
                    posObj["z"]?.ToObject<float>() ?? 0
                );
            }
            else
            {
                return new { success = false, message = "Either target or position required" };
            }

            if (instant)
            {
                sv.pivot = position;
                sv.size = size;
            }
            else
            {
                sv.Frame(new Bounds(position, Vector3.one * size), false);
            }
            sv.Repaint();

            return new
            {
                success = true,
                message = $"Looking at ({position.x:F2}, {position.y:F2}, {position.z:F2})"
            };
        }

        [MCPTool("sceneview_frame", "Frame selection or specific object in Scene View")]
        [MCPParam("path", "string", "GameObject path to frame (uses selection if not specified)", false)]
        public static object SceneViewFrame(JObject args)
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null)
            {
                return new { success = false, message = "No active Scene View" };
            }

            var path = args["path"]?.ToString();
            if (!string.IsNullOrEmpty(path))
            {
                var go = GameObject.Find(path);
                if (go == null)
                {
                    return new { success = false, message = $"GameObject not found: {path}" };
                }
                Selection.activeGameObject = go;
            }

            if (Selection.activeGameObject == null)
            {
                return new { success = false, message = "No object selected or specified" };
            }

            sv.FrameSelected();
            return new { success = true, message = $"Framed: {Selection.activeGameObject.name}" };
        }

        [MCPTool("ping_asset", "Ping (highlight) an asset in the Project window")]
        [MCPParam("path", "string", "Asset path to ping")]
        public static object PingAsset(JObject args)
        {
            var path = args["path"]?.ToString();
            if (string.IsNullOrEmpty(path))
            {
                return new { success = false, message = "Path required" };
            }

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (asset == null)
            {
                return new { success = false, message = $"Asset not found: {path}" };
            }

            EditorGUIUtility.PingObject(asset);
            return new { success = true, message = $"Pinged: {path}" };
        }

        [MCPTool("open_script", "Open a script file in the default IDE")]
        [MCPParam("path", "string", "Script asset path")]
        [MCPParam("line", "integer", "Line number to jump to (default: 1)", false)]
        public static object OpenScript(JObject args)
        {
            var path = args["path"]?.ToString();
            var line = args["line"]?.ToObject<int>() ?? 1;

            if (string.IsNullOrEmpty(path))
            {
                return new { success = false, message = "Path required" };
            }

            var script = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (script == null)
            {
                return new { success = false, message = $"Script not found: {path}" };
            }

            AssetDatabase.OpenAsset(script, line);
            return new { success = true, message = $"Opened: {path} at line {line}" };
        }
    }
}
