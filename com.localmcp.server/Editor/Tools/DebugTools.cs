using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace LocalMCP.Tools
{
    /// <summary>
    /// Core debug and diagnostics tools for the unified MCP server.
    /// </summary>
    public static class DebugTools
    {
        private static readonly List<CompilerMessage> _compileErrors = new();
        private static bool _listeningToCompile;

        static DebugTools()
        {
            StartCompileListener();
        }

        private static void StartCompileListener()
        {
            if (_listeningToCompile) return;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
            _listeningToCompile = true;
        }

        private static void OnAssemblyCompiled(string assemblyPath, CompilerMessage[] messages)
        {
            lock (_compileErrors)
            {
                foreach (var msg in messages.Where(m => m.type == CompilerMessageType.Error || m.type == CompilerMessageType.Warning))
                {
                    _compileErrors.Add(msg);
                }

                // Keep last 100
                while (_compileErrors.Count > 100)
                    _compileErrors.RemoveAt(0);
            }
        }

        [MCPTool("refresh_tools", "Refresh the MCP tool registry to pick up new tools")]
        public static object RefreshTools(JObject args)
        {
            int beforeCount = MCPToolRegistry.GetToolNames().Length;
            MCPToolRegistry.Refresh();
            int afterCount = MCPToolRegistry.GetToolNames().Length;

            return new
            {
                success = true,
                message = $"Tool registry refreshed. {beforeCount} -> {afterCount} tools",
                toolCount = afterCount,
                newTools = afterCount - beforeCount,
                tools = MCPToolRegistry.GetToolNames()
            };
        }

        [MCPTool("compile_errors", "Get recent compilation errors and warnings")]
        [MCPParam("errorsOnly", "boolean", "Only show errors, not warnings (default: false)", false)]
        [MCPParam("clear", "boolean", "Clear after reading (default: false)", false)]
        public static object CompileErrors(JObject args)
        {
            var errorsOnly = args["errorsOnly"]?.ToObject<bool>() ?? false;
            var clear = args["clear"]?.ToObject<bool>() ?? false;

            List<object> results;
            lock (_compileErrors)
            {
                var filtered = errorsOnly
                    ? _compileErrors.Where(m => m.type == CompilerMessageType.Error)
                    : _compileErrors.AsEnumerable();

                results = filtered.Select(m => (object)new
                {
                    type = m.type.ToString().ToLower(),
                    message = m.message,
                    file = m.file,
                    line = m.line,
                    column = m.column
                }).ToList();

                if (clear)
                    _compileErrors.Clear();
            }

            return new
            {
                isCompiling = EditorApplication.isCompiling,
                count = results.Count,
                messages = results
            };
        }

        [MCPTool("find_missing_refs", "Find missing/null references in the scene")]
        [MCPParam("path", "string", "Limit search to specific hierarchy path (optional)", false)]
        [MCPParam("maxObjects", "integer", "Max objects to scan (default: 500, max: 2000)", false)]
        public static object FindMissingRefs(JObject args)
        {
            var searchPath = args["path"]?.ToString();
            var maxObjects = args["maxObjects"]?.ToObject<int>() ?? 500;
            maxObjects = Math.Min(maxObjects, 2000); // Hard limit

            GameObject[] objects;
            bool truncated = false;

            if (!string.IsNullOrEmpty(searchPath))
            {
                var root = GameObject.Find(searchPath);
                if (root == null)
                {
                    return new { success = false, message = $"GameObject not found: {searchPath}" };
                }
                objects = root.GetComponentsInChildren<Transform>(true).Select(t => t.gameObject).ToArray();
            }
            else
            {
                objects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            }

            // Apply limit to prevent long scans
            int totalObjects = objects.Length;
            if (objects.Length > maxObjects)
            {
                objects = objects.Take(maxObjects).ToArray();
                truncated = true;
            }

            var missing = new List<object>();
            var startTime = DateTime.Now;
            const int maxScanTimeMs = 5000; // 5 second max scan time

            foreach (var go in objects)
            {
                // Check for timeout
                if ((DateTime.Now - startTime).TotalMilliseconds > maxScanTimeMs)
                {
                    truncated = true;
                    break;
                }

                foreach (var component in go.GetComponents<Component>())
                {
                    if (component == null)
                    {
                        missing.Add(new
                        {
                            gameObject = go.name,
                            path = GetGameObjectPath(go),
                            issue = "Missing script (component is null)"
                        });
                        continue;
                    }

                    var so = new SerializedObject(component);
                    var prop = so.GetIterator();

                    while (prop.NextVisible(true))
                    {
                        if (prop.propertyType == SerializedPropertyType.ObjectReference)
                        {
                            if (prop.objectReferenceValue == null && prop.objectReferenceInstanceIDValue != 0)
                            {
                                missing.Add(new
                                {
                                    gameObject = go.name,
                                    path = GetGameObjectPath(go),
                                    component = component.GetType().Name,
                                    field = prop.propertyPath,
                                    issue = "Missing reference"
                                });
                            }
                        }
                    }
                }
            }

            var elapsed = (DateTime.Now - startTime).TotalMilliseconds;

            return new
            {
                success = true,
                scannedObjects = objects.Length,
                totalObjectsInScene = totalObjects,
                truncated,
                truncatedMessage = truncated ? $"Scan limited to {maxObjects} objects. Use 'path' parameter to scan specific hierarchies, or increase 'maxObjects' (max 2000)." : null,
                elapsedMs = elapsed,
                issuesFound = missing.Count,
                issues = missing.Take(100).ToArray()
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
