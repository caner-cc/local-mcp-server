using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMCP.Tools
{
    /// <summary>
    /// Tools for scene and GameObject manipulation.
    /// </summary>
    public static class SceneTools
    {
        [MCPTool("scene_info", "Get information about the current scene")]
        public static object SceneInfo(JObject args)
        {
            var scene = SceneManager.GetActiveScene();
            var rootObjects = scene.GetRootGameObjects();

            return new
            {
                name = scene.name,
                path = scene.path,
                isDirty = scene.isDirty,
                isLoaded = scene.isLoaded,
                rootObjectCount = rootObjects.Length,
                totalObjectCount = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Length,
                rootObjects = rootObjects.Select(go => new
                {
                    name = go.name,
                    childCount = go.transform.childCount,
                    active = go.activeSelf
                }).ToArray()
            };
        }

        [MCPTool("scene_load", "Load a scene")]
        [MCPParam("path", "string", "Scene path (e.g., 'Assets/Scenes/Main.unity')")]
        [MCPParam("mode", "string", "Load mode: single, additive (default: single)", false)]
        public static object SceneLoad(JObject args)
        {
            var path = args["path"]?.ToString();
            if (string.IsNullOrEmpty(path))
            {
                return new { success = false, message = "Scene path required" };
            }

            var mode = args["mode"]?.ToString()?.ToLower() == "additive"
                ? OpenSceneMode.Additive
                : OpenSceneMode.Single;

            try
            {
                EditorSceneManager.OpenScene(path, mode);
                return new { success = true, message = $"Loaded scene: {path}" };
            }
            catch (Exception e)
            {
                return new { success = false, message = $"Failed to load scene: {e.Message}" };
            }
        }

        [MCPTool("scene_save", "Save the current scene")]
        public static object SceneSave(JObject args)
        {
            var scene = SceneManager.GetActiveScene();
            bool saved = EditorSceneManager.SaveScene(scene);
            return new
            {
                success = saved,
                message = saved ? $"Saved scene: {scene.path}" : "Failed to save scene"
            };
        }

        [MCPTool("gameobject_create", "Create a new GameObject")]
        [MCPParam("name", "string", "Name for the new GameObject")]
        [MCPParam("parent", "string", "Parent path (optional)", false)]
        [MCPParam("primitive", "string", "Primitive type: cube, sphere, capsule, cylinder, plane, quad (optional)", false)]
        [MCPParam("position", "object", "Position {x, y, z} (optional)", false)]
        [MCPParam("rotation", "object", "Rotation {x, y, z} (optional)", false)]
        [MCPParam("scale", "object", "Scale {x, y, z} (optional)", false)]
        public static object GameObjectCreate(JObject args)
        {
            var name = args["name"]?.ToString() ?? "New GameObject";
            var parentPath = args["parent"]?.ToString();
            var primitive = args["primitive"]?.ToString()?.ToLower();

            GameObject go;
            if (!string.IsNullOrEmpty(primitive))
            {
                PrimitiveType type = primitive switch
                {
                    "cube" => PrimitiveType.Cube,
                    "sphere" => PrimitiveType.Sphere,
                    "capsule" => PrimitiveType.Capsule,
                    "cylinder" => PrimitiveType.Cylinder,
                    "plane" => PrimitiveType.Plane,
                    "quad" => PrimitiveType.Quad,
                    _ => PrimitiveType.Cube
                };
                go = GameObject.CreatePrimitive(type);
                go.name = name;
            }
            else
            {
                go = new GameObject(name);
            }

            // Set parent
            if (!string.IsNullOrEmpty(parentPath))
            {
                var parent = GameObject.Find(parentPath);
                if (parent != null)
                {
                    go.transform.SetParent(parent.transform);
                }
            }

            // Set transform
            ApplyTransform(go.transform, args);

            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
            Selection.activeGameObject = go;

            return new
            {
                success = true,
                message = $"Created: {name}",
                path = GetGameObjectPath(go),
                instanceId = go.GetInstanceID()
            };
        }

        [MCPTool("gameobject_find", "Find GameObjects by name or path")]
        [MCPParam("query", "string", "Name or path to search for")]
        [MCPParam("includeInactive", "boolean", "Include inactive objects (default: true)", false)]
        public static object GameObjectFind(JObject args)
        {
            var query = args["query"]?.ToString();
            if (string.IsNullOrEmpty(query))
            {
                return new { success = false, message = "Query required" };
            }

            var includeInactive = args["includeInactive"]?.ToObject<bool>() ?? true;

            var results = new List<object>();
            var allObjects = includeInactive
                ? Resources.FindObjectsOfTypeAll<GameObject>()
                : GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None);

            foreach (var go in allObjects)
            {
                // Skip prefab assets
                if (!go.scene.IsValid()) continue;

                var path = GetGameObjectPath(go);
                if (go.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    results.Add(new
                    {
                        name = go.name,
                        path = path,
                        active = go.activeInHierarchy,
                        layer = LayerMask.LayerToName(go.layer),
                        tag = go.tag,
                        components = go.GetComponents<Component>()
                            .Where(c => c != null)
                            .Select(c => c.GetType().Name)
                            .ToArray()
                    });
                }
            }

            return new
            {
                success = true,
                query = query,
                count = results.Count,
                results = results.Take(50).ToArray() // Limit results
            };
        }

        [MCPTool("gameobject_modify", "Modify an existing GameObject")]
        [MCPParam("path", "string", "Hierarchy path to the GameObject")]
        [MCPParam("name", "string", "New name (optional)", false)]
        [MCPParam("active", "boolean", "Set active state (optional)", false)]
        [MCPParam("layer", "string", "Set layer (optional)", false)]
        [MCPParam("tag", "string", "Set tag (optional)", false)]
        [MCPParam("position", "object", "Set position {x, y, z} (optional)", false)]
        [MCPParam("rotation", "object", "Set rotation {x, y, z} (optional)", false)]
        [MCPParam("scale", "object", "Set scale {x, y, z} (optional)", false)]
        public static object GameObjectModify(JObject args)
        {
            var path = args["path"]?.ToString();
            if (string.IsNullOrEmpty(path))
            {
                return new { success = false, message = "Path required" };
            }

            var go = GameObject.Find(path);
            if (go == null)
            {
                // Try finding in inactive objects
                var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
                go = allObjects.FirstOrDefault(g => g.scene.IsValid() && GetGameObjectPath(g) == path);
            }

            if (go == null)
            {
                return new { success = false, message = $"GameObject not found: {path}" };
            }

            Undo.RecordObject(go, "Modify GameObject");
            Undo.RecordObject(go.transform, "Modify Transform");

            // Apply modifications
            if (args["name"] != null)
                go.name = args["name"].ToString();

            if (args["active"] != null)
                go.SetActive(args["active"].ToObject<bool>());

            if (args["layer"] != null)
            {
                var layer = LayerMask.NameToLayer(args["layer"].ToString());
                if (layer >= 0) go.layer = layer;
            }

            if (args["tag"] != null)
                go.tag = args["tag"].ToString();

            ApplyTransform(go.transform, args);

            EditorUtility.SetDirty(go);

            return new
            {
                success = true,
                message = $"Modified: {go.name}",
                path = GetGameObjectPath(go)
            };
        }

        [MCPTool("gameobject_delete", "Delete a GameObject")]
        [MCPParam("path", "string", "Hierarchy path to the GameObject")]
        public static object GameObjectDelete(JObject args)
        {
            var path = args["path"]?.ToString();
            if (string.IsNullOrEmpty(path))
            {
                return new { success = false, message = "Path required" };
            }

            var go = GameObject.Find(path);
            if (go == null)
            {
                return new { success = false, message = $"GameObject not found: {path}" };
            }

            Undo.DestroyObjectImmediate(go);
            return new { success = true, message = $"Deleted: {path}" };
        }

        [MCPTool("hierarchy_tree", "Get the full scene hierarchy as a tree")]
        [MCPParam("maxDepth", "integer", "Maximum depth to traverse (default: 10)", false)]
        public static object HierarchyTree(JObject args)
        {
            var maxDepth = args["maxDepth"]?.ToObject<int>() ?? 10;
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();

            var tree = roots.Select(r => BuildTreeNode(r, 0, maxDepth)).ToArray();

            return new
            {
                scene = scene.name,
                tree
            };
        }

        private static object BuildTreeNode(GameObject go, int depth, int maxDepth)
        {
            var children = new List<object>();
            if (depth < maxDepth)
            {
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    children.Add(BuildTreeNode(go.transform.GetChild(i).gameObject, depth + 1, maxDepth));
                }
            }

            return new
            {
                name = go.name,
                active = go.activeSelf,
                components = go.GetComponents<Component>()
                    .Where(c => c != null && !(c is Transform))
                    .Select(c => c.GetType().Name)
                    .ToArray(),
                children = children.Count > 0 ? children : null
            };
        }

        private static void ApplyTransform(Transform t, JObject args)
        {
            if (args["position"] is JObject pos)
            {
                t.position = new Vector3(
                    pos["x"]?.ToObject<float>() ?? t.position.x,
                    pos["y"]?.ToObject<float>() ?? t.position.y,
                    pos["z"]?.ToObject<float>() ?? t.position.z
                );
            }

            if (args["rotation"] is JObject rot)
            {
                t.eulerAngles = new Vector3(
                    rot["x"]?.ToObject<float>() ?? t.eulerAngles.x,
                    rot["y"]?.ToObject<float>() ?? t.eulerAngles.y,
                    rot["z"]?.ToObject<float>() ?? t.eulerAngles.z
                );
            }

            if (args["scale"] is JObject scale)
            {
                t.localScale = new Vector3(
                    scale["x"]?.ToObject<float>() ?? t.localScale.x,
                    scale["y"]?.ToObject<float>() ?? t.localScale.y,
                    scale["z"]?.ToObject<float>() ?? t.localScale.z
                );
            }
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
