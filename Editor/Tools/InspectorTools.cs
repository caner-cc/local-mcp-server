using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace LocalMCP.Tools
{
    /// <summary>
    /// Tools for inspecting components and their properties.
    /// </summary>
    public static class InspectorTools
    {
        [MCPTool("component_inspect", "Get serialized fields and values from a component")]
        [MCPParam("path", "string", "Hierarchy path to the GameObject")]
        [MCPParam("component", "string", "Component type name to inspect")]
        [MCPParam("maxFields", "integer", "Maximum fields to return (default: 30)", false)]
        [MCPParam("includePrivate", "boolean", "Include private/internal fields (default: false)", false)]
        public static object ComponentInspect(JObject args)
        {
            var path = args["path"]?.ToString();
            var componentName = args["component"]?.ToString();
            var maxFields = args["maxFields"]?.ToObject<int>() ?? 30;
            var includePrivate = args["includePrivate"]?.ToObject<bool>() ?? false;

            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(componentName))
            {
                return new { success = false, message = "Path and component required" };
            }

            var go = GameObject.Find(path);
            if (go == null)
            {
                return new { success = false, message = $"GameObject not found: {path}" };
            }

            var component = go.GetComponents<Component>()
                .FirstOrDefault(c => c != null && c.GetType().Name == componentName);

            if (component == null)
            {
                return new { success = false, message = $"Component not found: {componentName}" };
            }

            var fields = new Dictionary<string, object>();
            var serializedObj = new SerializedObject(component);
            var prop = serializedObj.GetIterator();
            int fieldCount = 0;
            int totalFields = 0;
            bool truncated = false;

            if (prop.NextVisible(true))
            {
                do
                {
                    if (prop.name == "m_Script") continue;
                    
                    // Skip private fields (starting with m_) unless requested
                    if (!includePrivate && prop.name.StartsWith("m_") && prop.name != "m_Name")
                        continue;
                    
                    totalFields++;
                    
                    if (fieldCount < maxFields)
                    {
                        fields[prop.name] = GetPropertyValueDetailed(prop);
                        fieldCount++;
                    }
                    else
                    {
                        truncated = true;
                    }
                } while (prop.NextVisible(false));
            }

            return new
            {
                success = true,
                gameObject = path,
                component = componentName,
                fullType = component.GetType().FullName,
                fieldCount,
                totalFields,
                truncated,
                truncatedMessage = truncated ? $"Showing {maxFields} of {totalFields} fields. Use maxFields parameter to see more, or includePrivate:true for internal fields." : null,
                fields
            };
        }

        [MCPTool("component_set", "Set a serialized field value on a component")]
        [MCPParam("path", "string", "Hierarchy path to the GameObject")]
        [MCPParam("component", "string", "Component type name")]
        [MCPParam("field", "string", "Field name to set")]
        [MCPParam("value", "any", "Value to set (type must match)")]
        public static object ComponentSet(JObject args)
        {
            var path = args["path"]?.ToString();
            var componentName = args["component"]?.ToString();
            var fieldName = args["field"]?.ToString();
            var value = args["value"];

            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(componentName) || string.IsNullOrEmpty(fieldName))
            {
                return new { success = false, message = "Path, component, and field required" };
            }

            var go = GameObject.Find(path);
            if (go == null)
            {
                return new { success = false, message = $"GameObject not found: {path}" };
            }

            var component = go.GetComponents<Component>()
                .FirstOrDefault(c => c != null && c.GetType().Name == componentName);

            if (component == null)
            {
                return new { success = false, message = $"Component not found: {componentName}" };
            }

            var serializedObj = new SerializedObject(component);
            var prop = serializedObj.FindProperty(fieldName);

            if (prop == null)
            {
                return new { success = false, message = $"Field not found: {fieldName}" };
            }

            Undo.RecordObject(component, $"Set {fieldName}");

            try
            {
                SetPropertyValue(prop, value);
                serializedObj.ApplyModifiedProperties();
                EditorUtility.SetDirty(component);

                return new { success = true, message = $"Set {componentName}.{fieldName}" };
            }
            catch (Exception e)
            {
                return new { success = false, message = $"Failed to set value: {e.Message}" };
            }
        }

        [MCPTool("object_dump", "Get serialized data for a GameObject and its components")]
        [MCPParam("path", "string", "Hierarchy path to the GameObject")]
        [MCPParam("summary", "boolean", "Only list component names, not fields (default: true for efficiency)", false)]
        [MCPParam("maxFieldsPerComponent", "integer", "Max fields per component when summary=false (default: 10)", false)]
        public static object ObjectDump(JObject args)
        {
            var path = args["path"]?.ToString();
            var summary = args["summary"]?.ToObject<bool>() ?? true; // Default to summary mode
            var maxFieldsPerComponent = args["maxFieldsPerComponent"]?.ToObject<int>() ?? 10;
            
            if (string.IsNullOrEmpty(path))
            {
                return new { success = false, message = "Path required" };
            }

            var go = GameObject.Find(path);
            if (go == null)
            {
                return new { success = false, message = $"GameObject not found: {path}" };
            }

            var components = new List<object>();
            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null)
                {
                    components.Add(new { type = "(Missing Script)", enabled = false });
                    continue;
                }

                if (summary)
                {
                    // Summary mode: just list component names and enabled state
                    components.Add(new
                    {
                        type = component.GetType().Name,
                        enabled = component is Behaviour b ? b.enabled : true
                    });
                }
                else
                {
                    // Full mode: include fields (with limit)
                    var fields = new Dictionary<string, object>();
                    var serializedObj = new SerializedObject(component);
                    var prop = serializedObj.GetIterator();
                    int fieldCount = 0;

                    if (prop.NextVisible(true))
                    {
                        do
                        {
                            if (prop.name == "m_Script") continue;
                            if (prop.name.StartsWith("m_")) continue; // Skip internal fields
                            
                            if (fieldCount < maxFieldsPerComponent)
                            {
                                fields[prop.name] = GetPropertyValueDetailed(prop);
                                fieldCount++;
                            }
                        } while (prop.NextVisible(false));
                    }

                    components.Add(new
                    {
                        type = component.GetType().Name,
                        fullType = component.GetType().FullName,
                        enabled = component is Behaviour b ? b.enabled : true,
                        fields,
                        fieldsShown = fieldCount
                    });
                }
            }

            var result = new
            {
                success = true,
                name = go.name,
                path,
                tag = go.tag,
                layer = LayerMask.LayerToName(go.layer),
                isStatic = go.isStatic,
                activeSelf = go.activeSelf,
                activeInHierarchy = go.activeInHierarchy,
                childCount = go.transform.childCount,
                componentCount = components.Count,
                summaryMode = summary,
                hint = summary ? "Use summary:false to see component fields, or use component_inspect for detailed single component view" : null,
                components
            };

            return result;
        }

        [MCPTool("find_components", "Find all GameObjects with a specific component type")]
        [MCPParam("component", "string", "Component type name to find")]
        [MCPParam("includeInactive", "boolean", "Include inactive objects (default: false)", false)]
        public static object FindComponents(JObject args)
        {
            var componentName = args["component"]?.ToString();
            var includeInactive = args["includeInactive"]?.ToObject<bool>() ?? false;

            if (string.IsNullOrEmpty(componentName))
            {
                return new { success = false, message = "Component name required" };
            }

            // Find the component type
            Type componentType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                componentType = assembly.GetTypes()
                    .FirstOrDefault(t => t.Name == componentName && typeof(Component).IsAssignableFrom(t));
                if (componentType != null) break;
            }

            if (componentType == null)
            {
                return new { success = false, message = $"Component type not found: {componentName}" };
            }

            Component[] found;
            if (includeInactive)
            {
                found = Resources.FindObjectsOfTypeAll(componentType)
                    .Cast<Component>()
                    .Where(c => c.gameObject.scene.IsValid())
                    .ToArray();
            }
            else
            {
                found = UnityEngine.Object.FindObjectsByType(componentType, FindObjectsSortMode.None)
                    .Cast<Component>()
                    .ToArray();
            }

            var results = found.Select(c => new
            {
                gameObject = c.gameObject.name,
                path = GetGameObjectPath(c.gameObject),
                enabled = c is Behaviour b ? b.enabled : true
            }).ToArray();

            return new
            {
                success = true,
                component = componentName,
                count = results.Length,
                results
            };
        }

        private static object GetPropertyValueDetailed(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return prop.intValue;
                case SerializedPropertyType.Boolean:
                    return prop.boolValue;
                case SerializedPropertyType.Float:
                    return prop.floatValue;
                case SerializedPropertyType.String:
                    return prop.stringValue;
                case SerializedPropertyType.Enum:
                    return new { index = prop.enumValueIndex, value = prop.enumNames[prop.enumValueIndex] };
                case SerializedPropertyType.ObjectReference:
                    if (prop.objectReferenceValue == null)
                        return null;
                    return new
                    {
                        name = prop.objectReferenceValue.name,
                        type = prop.objectReferenceValue.GetType().Name,
                        instanceId = prop.objectReferenceValue.GetInstanceID()
                    };
                case SerializedPropertyType.Vector2:
                    return new { x = prop.vector2Value.x, y = prop.vector2Value.y };
                case SerializedPropertyType.Vector3:
                    return new { x = prop.vector3Value.x, y = prop.vector3Value.y, z = prop.vector3Value.z };
                case SerializedPropertyType.Vector4:
                    return new { x = prop.vector4Value.x, y = prop.vector4Value.y, z = prop.vector4Value.z, w = prop.vector4Value.w };
                case SerializedPropertyType.Rect:
                    return new { x = prop.rectValue.x, y = prop.rectValue.y, width = prop.rectValue.width, height = prop.rectValue.height };
                case SerializedPropertyType.Color:
                    return new { r = prop.colorValue.r, g = prop.colorValue.g, b = prop.colorValue.b, a = prop.colorValue.a };
                case SerializedPropertyType.AnimationCurve:
                    return $"<AnimationCurve with {prop.animationCurveValue.keys.Length} keys>";
                case SerializedPropertyType.Bounds:
                    return new { center = prop.boundsValue.center.ToString(), size = prop.boundsValue.size.ToString() };
                case SerializedPropertyType.Quaternion:
                    return new { x = prop.quaternionValue.x, y = prop.quaternionValue.y, z = prop.quaternionValue.z, w = prop.quaternionValue.w };
                case SerializedPropertyType.LayerMask:
                    return prop.intValue;
                case SerializedPropertyType.ArraySize:
                    return prop.intValue;
                default:
                    if (prop.isArray)
                    {
                        var items = new List<object>();
                        for (int i = 0; i < Math.Min(prop.arraySize, 20); i++)
                        {
                            items.Add(GetPropertyValueDetailed(prop.GetArrayElementAtIndex(i)));
                        }
                        if (prop.arraySize > 20)
                            items.Add($"... and {prop.arraySize - 20} more");
                        return items;
                    }
                    return $"<{prop.propertyType}>";
            }
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
                case SerializedPropertyType.Enum:
                    if (value.Type == JTokenType.Integer)
                        prop.enumValueIndex = value.ToObject<int>();
                    else
                    {
                        var enumName = value.ToString();
                        var index = Array.IndexOf(prop.enumNames, enumName);
                        if (index >= 0) prop.enumValueIndex = index;
                    }
                    break;
                case SerializedPropertyType.Vector2:
                    var v2 = value as JObject;
                    prop.vector2Value = new Vector2(
                        v2?["x"]?.ToObject<float>() ?? 0,
                        v2?["y"]?.ToObject<float>() ?? 0
                    );
                    break;
                case SerializedPropertyType.Vector3:
                    var v3 = value as JObject;
                    prop.vector3Value = new Vector3(
                        v3?["x"]?.ToObject<float>() ?? 0,
                        v3?["y"]?.ToObject<float>() ?? 0,
                        v3?["z"]?.ToObject<float>() ?? 0
                    );
                    break;
                case SerializedPropertyType.Color:
                    var c = value as JObject;
                    prop.colorValue = new Color(
                        c?["r"]?.ToObject<float>() ?? 0,
                        c?["g"]?.ToObject<float>() ?? 0,
                        c?["b"]?.ToObject<float>() ?? 0,
                        c?["a"]?.ToObject<float>() ?? 1
                    );
                    break;
                case SerializedPropertyType.ObjectReference:
                    // Support setting by:
                    // 1. null - clear the reference
                    // 2. string - asset path for ScriptableObjects/Prefabs
                    // 3. object with gameObjectPath + componentType - for scene component references
                    if (value == null || value.Type == JTokenType.Null)
                    {
                        prop.objectReferenceValue = null;
                    }
                    else if (value.Type == JTokenType.String)
                    {
                        var assetPath = value.ToString();
                        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                        if (asset == null)
                            throw new Exception($"Asset not found: {assetPath}");
                        prop.objectReferenceValue = asset;
                    }
                    else if (value.Type == JTokenType.Object)
                    {
                        var obj = value as JObject;
                        var goPath = obj?["gameObjectPath"]?.ToString();
                        var compType = obj?["componentType"]?.ToString();
                        
                        if (!string.IsNullOrEmpty(goPath) && !string.IsNullOrEmpty(compType))
                        {
                            // Scene component reference
                            var targetGo = GameObject.Find(goPath);
                            if (targetGo == null)
                                throw new Exception($"GameObject not found: {goPath}");
                            
                            var targetComp = targetGo.GetComponents<Component>()
                                .FirstOrDefault(c => c != null && c.GetType().Name == compType);
                            if (targetComp == null)
                                throw new Exception($"Component '{compType}' not found on '{goPath}'");
                            
                            prop.objectReferenceValue = targetComp;
                        }
                        else
                        {
                            throw new Exception("Object must have 'gameObjectPath' and 'componentType' for scene references");
                        }
                    }
                    else
                    {
                        throw new Exception("ObjectReference must be: null, asset path string, or {gameObjectPath, componentType}");
                    }
                    break;
                default:
                    throw new Exception($"Cannot set property type: {prop.propertyType}");
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

        [MCPTool("prefab_set_property", "Set a property on a component inside a prefab asset")]
        [MCPParam("prefabPath", "string", "Asset path to the prefab (e.g., 'Assets/Prefabs/UI/MainUICanvas.prefab')")]
        [MCPParam("objectPath", "string", "Path to child object within prefab (e.g., 'HUDPanel/TargetFrame'), or empty for root")]
        [MCPParam("component", "string", "Component type name (e.g., 'Image')")]
        [MCPParam("field", "string", "Field name to set (e.g., 'm_Sprite', 'm_Color')")]
        [MCPParam("value", "any", "Value to set - for sprites use asset path string")]
        public static object PrefabSetProperty(JObject args)
        {
            var prefabPath = args["prefabPath"]?.ToString();
            var objectPath = args["objectPath"]?.ToString() ?? "";
            var componentName = args["component"]?.ToString();
            var fieldName = args["field"]?.ToString();
            var value = args["value"];

            if (string.IsNullOrEmpty(prefabPath) || string.IsNullOrEmpty(componentName) || string.IsNullOrEmpty(fieldName))
            {
                return new { success = false, message = "prefabPath, component, and field are required" };
            }

            // Load the prefab asset
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null)
            {
                return new { success = false, message = $"Prefab not found: {prefabPath}" };
            }

            // Open prefab for editing
            var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            if (prefabRoot == null)
            {
                return new { success = false, message = $"Failed to load prefab contents: {prefabPath}" };
            }

            try
            {
                // Find the target object within the prefab
                GameObject targetGO;
                if (string.IsNullOrEmpty(objectPath))
                {
                    targetGO = prefabRoot;
                }
                else
                {
                    var targetTransform = prefabRoot.transform.Find(objectPath);
                    if (targetTransform == null)
                    {
                        PrefabUtility.UnloadPrefabContents(prefabRoot);
                        return new { success = false, message = $"Object not found in prefab: {objectPath}" };
                    }
                    targetGO = targetTransform.gameObject;
                }

                // Find the component
                var component = targetGO.GetComponents<Component>()
                    .FirstOrDefault(c => c != null && c.GetType().Name == componentName);

                if (component == null)
                {
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                    return new { success = false, message = $"Component not found: {componentName} on {objectPath}" };
                }

                // Get serialized object and property
                var serializedObj = new SerializedObject(component);
                var prop = serializedObj.FindProperty(fieldName);

                if (prop == null)
                {
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                    return new { success = false, message = $"Field not found: {fieldName}" };
                }

                // Set the value
                SetPropertyValue(prop, value);
                serializedObj.ApplyModifiedPropertiesWithoutUndo();

                // Save the prefab
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                PrefabUtility.UnloadPrefabContents(prefabRoot);

                // Refresh to pick up changes
                AssetDatabase.Refresh();

                return new
                {
                    success = true,
                    message = $"Set {componentName}.{fieldName} on {(string.IsNullOrEmpty(objectPath) ? "root" : objectPath)} in {prefabPath}"
                };
            }
            catch (Exception e)
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
                return new { success = false, message = $"Failed to set property: {e.Message}" };
            }
        }

        [MCPTool("prefab_get_hierarchy", "Get the hierarchy structure of a prefab asset")]
        [MCPParam("prefabPath", "string", "Asset path to the prefab")]
        [MCPParam("maxDepth", "integer", "Maximum depth to traverse (default: 5)", false)]
        public static object PrefabGetHierarchy(JObject args)
        {
            var prefabPath = args["prefabPath"]?.ToString();
            var maxDepth = args["maxDepth"]?.ToObject<int>() ?? 5;

            if (string.IsNullOrEmpty(prefabPath))
            {
                return new { success = false, message = "prefabPath is required" };
            }

            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null)
            {
                return new { success = false, message = $"Prefab not found: {prefabPath}" };
            }

            var tree = BuildPrefabTree(prefabAsset.transform, "", maxDepth, 0);

            return new
            {
                success = true,
                prefabPath,
                name = prefabAsset.name,
                tree
            };
        }

        private static object BuildPrefabTree(Transform t, string currentPath, int maxDepth, int depth)
        {
            var components = t.GetComponents<Component>()
                .Where(c => c != null)
                .Select(c => c.GetType().Name)
                .ToList();

            var children = new List<object>();
            if (depth < maxDepth)
            {
                foreach (Transform child in t)
                {
                    var childPath = string.IsNullOrEmpty(currentPath) ? child.name : $"{currentPath}/{child.name}";
                    children.Add(BuildPrefabTree(child, childPath, maxDepth, depth + 1));
                }
            }

            return new
            {
                name = t.name,
                path = currentPath,
                components,
                children = children.Count > 0 ? children : null
            };
        }
    }
}
