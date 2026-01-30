using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace LocalMCP.Tools
{
    /// <summary>
    /// Tools for working with Unity assets.
    /// </summary>
    public static class AssetTools
    {
        [MCPTool("asset_find", "Find assets by name, type, or path pattern. Uses fast asset cache.", Category = "Assets", IsReadOnly = true)]
        [MCPParam("query", "string", "Search query (name or path pattern)", false)]
        [MCPParam("type", "string", "Asset type filter (e.g., 'Prefab', 'Material', 'ScriptableObject')", false)]
        [MCPParam("folder", "string", "Limit search to folder (e.g., 'Assets/Prefabs')", false)]
        [MCPParam("limit", "integer", "Max results (default: 50)", false)]
        public static object AssetFind(JObject args)
        {
            var query = args["query"]?.ToString();
            var typeFilter = args["type"]?.ToString();
            var folder = args["folder"]?.ToString();
            var limit = args["limit"]?.ToObject<int>() ?? 50;

            // Use the fast asset cache
            var startTime = DateTime.Now;
            var cachedResults = AssetCache.Instance.Query(typeFilter, query, folder, limit);
            var cacheTime = (DateTime.Now - startTime).TotalMilliseconds;

            var results = cachedResults.Select(info => new
            {
                name = info.Name,
                type = info.Type,
                path = info.Path,
                folder = info.Folder,
                modified = info.Modified.ToString("o")
            }).ToList();

            return new
            {
                success = true,
                query,
                type = typeFilter,
                folder,
                count = results.Count,
                results,
                performance = new { queryMs = cacheTime, cached = true }
            };
        }

        [MCPTool("asset_info", "Get information about an asset", Category = "Assets", IsReadOnly = true)]
        [MCPParam("path", "string", "Asset path (e.g., 'Assets/Prefabs/Player.prefab')")]
        [MCPParam("summary", "boolean", "Summary mode - skip detailed fields (default: true)", false)]
        [MCPParam("maxFields", "integer", "Max fields to show for ScriptableObjects (default: 15)", false)]
        public static object AssetInfo(JObject args)
        {
            var path = args["path"]?.ToString();
            var summary = args["summary"]?.ToObject<bool>() ?? true; // Default to summary
            var maxFields = args["maxFields"]?.ToObject<int>() ?? 15;

            if (string.IsNullOrEmpty(path))
            {
                return new { success = false, message = "Path required" };
            }

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (asset == null)
            {
                return new { success = false, message = $"Asset not found: {path}" };
            }

            var guid = AssetDatabase.AssetPathToGUID(path);
            var importer = AssetImporter.GetAtPath(path);

            var info = new Dictionary<string, object>
            {
                ["name"] = asset.name,
                ["type"] = asset.GetType().Name,
                ["fullType"] = asset.GetType().FullName,
                ["path"] = path,
                ["guid"] = guid,
                ["instanceId"] = asset.GetInstanceID()
            };

            // Add type-specific info
            if (asset is GameObject prefab)
            {
                info["isPrefab"] = true;
                info["components"] = prefab.GetComponents<Component>()
                    .Where(c => c != null)
                    .Select(c => c.GetType().Name)
                    .ToArray();
                info["childCount"] = prefab.transform.childCount;

                if (!summary)
                {
                    // Include children names
                    var children = new List<string>();
                    for (int i = 0; i < Math.Min(prefab.transform.childCount, 20); i++)
                    {
                        children.Add(prefab.transform.GetChild(i).name);
                    }
                    if (prefab.transform.childCount > 20)
                        children.Add($"... and {prefab.transform.childCount - 20} more");
                    info["children"] = children;
                }

                info["hint"] = "Use object_dump or component_inspect for detailed component data";
            }
            else if (asset is ScriptableObject so)
            {
                info["isScriptableObject"] = true;

                if (summary)
                {
                    // Just list field names in summary mode
                    var fieldNames = new List<string>();
                    var serializedObj = new SerializedObject(so);
                    var prop = serializedObj.GetIterator();
                    if (prop.NextVisible(true))
                    {
                        do
                        {
                            if (prop.name == "m_Script") continue;
                            fieldNames.Add(prop.name);
                        } while (prop.NextVisible(false));
                    }
                    info["fieldNames"] = fieldNames.Take(30).ToArray();
                    info["totalFields"] = fieldNames.Count;
                    info["hint"] = "Use summary:false to see field values";
                }
                else
                {
                    // Get serialized field values with limit
                    var fields = new Dictionary<string, object>();
                    var serializedObj = new SerializedObject(so);
                    var prop = serializedObj.GetIterator();
                    int fieldCount = 0;
                    int totalFields = 0;

                    if (prop.NextVisible(true))
                    {
                        do
                        {
                            if (prop.name == "m_Script") continue;
                            totalFields++;

                            if (fieldCount < maxFields)
                            {
                                fields[prop.name] = GetPropertyValue(prop);
                                fieldCount++;
                            }
                        } while (prop.NextVisible(false));
                    }
                    info["fields"] = fields;
                    info["fieldsShown"] = fieldCount;
                    info["totalFields"] = totalFields;
                    if (totalFields > maxFields)
                        info["truncated"] = $"Showing {maxFields} of {totalFields} fields. Use maxFields parameter for more.";
                }
            }
            else if (asset is Material mat)
            {
                info["shader"] = mat.shader?.name;
                info["renderQueue"] = mat.renderQueue;
            }
            else if (asset is Texture tex)
            {
                info["width"] = tex.width;
                info["height"] = tex.height;
                if (tex is Texture2D tex2d)
                {
                    info["format"] = tex2d.format.ToString();
                    info["mipmapCount"] = tex2d.mipmapCount;
                }
            }

            if (importer != null)
            {
                info["importerType"] = importer.GetType().Name;
            }

            return info;
        }

        [MCPTool("asset_create", "Create a new asset", Category = "Assets")]
        [MCPParam("type", "string", "Asset type to create (Material, Folder, or ScriptableObject type name)")]
        [MCPParam("path", "string", "Path where to create the asset")]
        [MCPParam("name", "string", "Name for the asset", false)]
        public static object AssetCreate(JObject args)
        {
            var type = args["type"]?.ToString();
            var path = args["path"]?.ToString();
            var name = args["name"]?.ToString();

            if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(path))
            {
                return new { success = false, message = "Type and path required" };
            }

            try
            {
                switch (type.ToLower())
                {
                    case "folder":
                        var parentFolder = Path.GetDirectoryName(path)?.Replace("\\", "/") ?? "Assets";
                        var folderName = name ?? Path.GetFileName(path);
                        var guid = AssetDatabase.CreateFolder(parentFolder, folderName);
                        return new { success = true, message = $"Created folder", path = AssetDatabase.GUIDToAssetPath(guid) };

                    case "material":
                        var mat = new Material(Shader.Find("Standard"));
                        var matPath = path.EndsWith(".mat") ? path : $"{path}/{name ?? "NewMaterial"}.mat";
                        EnsureDirectoryExists(matPath);
                        AssetDatabase.CreateAsset(mat, matPath);
                        return new { success = true, message = "Created material", path = matPath };

                    default:
                        // Try to find ScriptableObject type
                        Type soType = null;
                        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            soType = assembly.GetTypes()
                                .FirstOrDefault(t => t.Name == type && typeof(ScriptableObject).IsAssignableFrom(t));
                            if (soType != null) break;
                        }

                        if (soType == null)
                        {
                            return new { success = false, message = $"Unknown asset type: {type}" };
                        }

                        var so = ScriptableObject.CreateInstance(soType);
                        var soPath = path.EndsWith(".asset") ? path : $"{path}/{name ?? $"New{type}"}.asset";
                        EnsureDirectoryExists(soPath);
                        AssetDatabase.CreateAsset(so, soPath);
                        return new { success = true, message = $"Created {type}", path = soPath };
                }
            }
            catch (Exception e)
            {
                return new { success = false, message = $"Failed to create asset: {e.Message}" };
            }
        }

        [MCPTool("asset_delete", "Delete an asset", Category = "Assets")]
        [MCPParam("path", "string", "Asset path to delete")]
        public static object AssetDelete(JObject args)
        {
            var path = args["path"]?.ToString();
            if (string.IsNullOrEmpty(path))
            {
                return new { success = false, message = "Path required" };
            }

            if (!AssetDatabase.DeleteAsset(path))
            {
                return new { success = false, message = $"Failed to delete: {path}" };
            }

            return new { success = true, message = $"Deleted: {path}" };
        }

        [MCPTool("asset_move", "Move or rename an asset", Category = "Assets")]
        [MCPParam("from", "string", "Current asset path")]
        [MCPParam("to", "string", "New asset path")]
        public static object AssetMove(JObject args)
        {
            var from = args["from"]?.ToString();
            var to = args["to"]?.ToString();

            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
            {
                return new { success = false, message = "From and to paths required" };
            }

            var error = AssetDatabase.MoveAsset(from, to);
            if (!string.IsNullOrEmpty(error))
            {
                return new { success = false, message = error };
            }

            return new { success = true, message = $"Moved to: {to}" };
        }

        [MCPTool("asset_dependencies", "Get asset dependencies", Category = "Assets", IsReadOnly = true)]
        [MCPParam("path", "string", "Asset path")]
        [MCPParam("recursive", "boolean", "Include recursive dependencies (default: false)", false)]
        public static object AssetDependencies(JObject args)
        {
            var path = args["path"]?.ToString();
            var recursive = args["recursive"]?.ToObject<bool>() ?? false;

            if (string.IsNullOrEmpty(path))
            {
                return new { success = false, message = "Path required" };
            }

            var deps = AssetDatabase.GetDependencies(path, recursive);
            var results = deps
                .Where(d => d != path)
                .Select(d => new
                {
                    path = d,
                    type = AssetDatabase.GetMainAssetTypeAtPath(d)?.Name ?? "Unknown"
                })
                .ToArray();

            return new
            {
                success = true,
                path,
                recursive,
                count = results.Length,
                dependencies = results
            };
        }

        [MCPTool("prefab_instantiate", "Instantiate a prefab in the scene", Category = "Assets")]
        [MCPParam("path", "string", "Prefab asset path")]
        [MCPParam("name", "string", "Instance name (optional)", false)]
        [MCPParam("position", "object", "Position {x, y, z} (optional)", false)]
        [MCPParam("rotation", "object", "Rotation {x, y, z} (optional)", false)]
        [MCPParam("parent", "string", "Parent hierarchy path (optional)", false)]
        public static object PrefabInstantiate(JObject args)
        {
            var path = args["path"]?.ToString();
            if (string.IsNullOrEmpty(path))
            {
                return new { success = false, message = "Prefab path required" };
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                return new { success = false, message = $"Prefab not found: {path}" };
            }

            Transform parent = null;
            var parentPath = args["parent"]?.ToString();
            if (!string.IsNullOrEmpty(parentPath))
            {
                var parentGo = GameObject.Find(parentPath);
                if (parentGo != null)
                    parent = parentGo.transform;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);

            if (args["name"] != null)
                instance.name = args["name"].ToString();

            if (args["position"] is JObject pos)
            {
                instance.transform.position = new Vector3(
                    pos["x"]?.ToObject<float>() ?? 0,
                    pos["y"]?.ToObject<float>() ?? 0,
                    pos["z"]?.ToObject<float>() ?? 0
                );
            }

            if (args["rotation"] is JObject rot)
            {
                instance.transform.eulerAngles = new Vector3(
                    rot["x"]?.ToObject<float>() ?? 0,
                    rot["y"]?.ToObject<float>() ?? 0,
                    rot["z"]?.ToObject<float>() ?? 0
                );
            }

            Undo.RegisterCreatedObjectUndo(instance, "Instantiate Prefab");
            Selection.activeGameObject = instance;

            return new
            {
                success = true,
                message = $"Instantiated: {instance.name}",
                instanceId = instance.GetInstanceID()
            };
        }

        private static object GetPropertyValue(SerializedProperty prop)
        {
            return prop.propertyType switch
            {
                SerializedPropertyType.Integer => prop.intValue,
                SerializedPropertyType.Boolean => prop.boolValue,
                SerializedPropertyType.Float => prop.floatValue,
                SerializedPropertyType.String => prop.stringValue,
                SerializedPropertyType.Enum => prop.enumNames[prop.enumValueIndex],
                SerializedPropertyType.ObjectReference => prop.objectReferenceValue?.name,
                SerializedPropertyType.Vector2 => $"({prop.vector2Value.x}, {prop.vector2Value.y})",
                SerializedPropertyType.Vector3 => $"({prop.vector3Value.x}, {prop.vector3Value.y}, {prop.vector3Value.z})",
                SerializedPropertyType.Color => $"RGBA({prop.colorValue.r}, {prop.colorValue.g}, {prop.colorValue.b}, {prop.colorValue.a})",
                _ => $"<{prop.propertyType}>"
            };
        }

        private static void EnsureDirectoryExists(string assetPath)
        {
            var dir = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            if (string.IsNullOrEmpty(dir) || dir == "Assets") return;

            if (!AssetDatabase.IsValidFolder(dir))
            {
                var parts = dir.Split('/');
                var current = parts[0]; // "Assets"
                for (int i = 1; i < parts.Length; i++)
                {
                    var next = current + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(next))
                    {
                        AssetDatabase.CreateFolder(current, parts[i]);
                    }
                    current = next;
                }
            }
        }

        [MCPTool("asset_modify", "Modify properties on a ScriptableObject asset", Category = "Assets")]
        [MCPParam("path", "string", "Asset path")]
        [MCPParam("property", "string", "Property name to modify")]
        [MCPParam("value", "object", "New value for the property")]
        public static object AssetModify(JObject args)
        {
            var path = args["path"]?.ToString();
            var property = args["property"]?.ToString();
            var value = args["value"];

            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(property))
            {
                return new { success = false, message = "Path and property required" };
            }

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (asset == null)
            {
                return new { success = false, message = $"Asset not found: {path}" };
            }

            try
            {
                var so = new SerializedObject(asset);
                var prop = so.FindProperty(property);

                if (prop == null)
                {
                    return new { success = false, message = $"Property not found: {property}" };
                }

                SetPropertyValue(prop, value);
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();

                return new { success = true, message = $"Modified {property} on {asset.name}", path };
            }
            catch (Exception e)
            {
                return new { success = false, message = $"Failed to modify property: {e.Message}" };
            }
        }

        [MCPTool("cache_stats", "Get asset cache statistics", Category = "Assets", IsReadOnly = true)]
        public static object CacheStats(JObject args)
        {
            return AssetCache.Instance.GetStats();
        }

        [MCPTool("cache_refresh", "Force refresh the asset cache", Category = "Assets")]
        public static object CacheRefresh(JObject args)
        {
            var startTime = DateTime.Now;
            AssetCache.Instance.ForceRefresh();
            var elapsed = (DateTime.Now - startTime).TotalMilliseconds;

            return new
            {
                success = true,
                message = "Asset cache refreshed",
                elapsedMs = elapsed
            };
        }

        private static void SetPropertyValue(SerializedProperty prop, JToken value)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    prop.intValue = value.ToObject<int>();
                    break;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = value.ToObject<bool>();
                    break;
                case SerializedPropertyType.Float:
                    prop.floatValue = value.ToObject<float>();
                    break;
                case SerializedPropertyType.String:
                    prop.stringValue = value.ToString();
                    break;
                case SerializedPropertyType.Vector3:
                    if (value is JObject v3)
                    {
                        prop.vector3Value = new Vector3(
                            v3["x"]?.ToObject<float>() ?? 0,
                            v3["y"]?.ToObject<float>() ?? 0,
                            v3["z"]?.ToObject<float>() ?? 0
                        );
                    }
                    break;
                case SerializedPropertyType.Vector2:
                    if (value is JObject v2)
                    {
                        prop.vector2Value = new Vector2(
                            v2["x"]?.ToObject<float>() ?? 0,
                            v2["y"]?.ToObject<float>() ?? 0
                        );
                    }
                    break;
                case SerializedPropertyType.ObjectReference:
                    // Load asset by path if string provided
                    if (value.Type == JTokenType.String)
                    {
                        var refPath = value.ToString();
                        var refAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(refPath);
                        prop.objectReferenceValue = refAsset;
                    }
                    break;
                case SerializedPropertyType.Enum:
                    if (value.Type == JTokenType.Integer)
                    {
                        prop.enumValueIndex = value.ToObject<int>();
                    }
                    else if (value.Type == JTokenType.String)
                    {
                        var enumName = value.ToString();
                        var index = Array.IndexOf(prop.enumNames, enumName);
                        if (index >= 0) prop.enumValueIndex = index;
                    }
                    break;
                default:
                    // Handle arrays
                    if (prop.isArray && value is JArray array)
                    {
                        prop.ClearArray();
                        for (int i = 0; i < array.Count; i++)
                        {
                            prop.InsertArrayElementAtIndex(i);
                            var element = prop.GetArrayElementAtIndex(i);
                            SetPropertyValue(element, array[i]);
                        }
                    }
                    break;
            }
        }
    }
}
