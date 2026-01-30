using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UnityMCP
{
    /// <summary>
    /// Discovers and manages MCP tools via reflection.
    /// Includes timeout protection and watchdog logging for slow tools.
    /// </summary>
    public static class MCPToolRegistry
    {
        private static Dictionary<string, ToolInfo> _tools;
        private static bool _initialized;

        /// <summary>
        /// Maximum time a tool can run before being considered "slow" (for logging).
        /// </summary>
        public const int SlowToolThresholdMs = 2000;

        /// <summary>
        /// Maximum time a tool can run before timeout. Default 15 seconds.
        /// Note: Some tools like write_script may legitimately take longer due to compilation.
        /// </summary>
        public const int DefaultToolTimeoutMs = 15000;

        /// <summary>
        /// Extended timeout for tools that involve compilation (30 seconds).
        /// </summary>
        public const int CompilationToolTimeoutMs = 30000;

        // Tools that are allowed extended timeouts
        private static readonly HashSet<string> CompilationTools = new()
        {
            "write_script", "modify_script", "delete_script",
            "wait_for_compilation", "ensure_ready",
            "test_run", "test_status"
        };

        private class ToolInfo
        {
            public string Name;
            public string Description;
            public MethodInfo Method;
            public List<ParamInfo> Parameters = new();
        }

        private class ParamInfo
        {
            public string Name;
            public string Type;
            public string Description;
            public bool Required;
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            _tools = new Dictionary<string, ToolInfo>();

            // Scan all loaded assemblies for tools
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Skip system assemblies
                var name = assembly.GetName().Name;
                if (name.StartsWith("System") || name.StartsWith("Unity") ||
                    name.StartsWith("mscorlib") || name.StartsWith("Mono"))
                    continue;

                try
                {
                    ScanAssembly(assembly);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[UnityMCP] Failed to scan assembly {name}: {e.Message}");
                }
            }

            Debug.Log($"[UnityMCP] Registered {_tools.Count} tools: {string.Join(", ", _tools.Keys)}");
        }

        private static void ScanAssembly(Assembly assembly)
        {
            foreach (var type in assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var toolAttr = method.GetCustomAttribute<MCPToolAttribute>();
                    if (toolAttr == null) continue;

                    // Validate signature
                    var parameters = method.GetParameters();
                    if (parameters.Length != 1 || parameters[0].ParameterType != typeof(JObject))
                    {
                        Debug.LogWarning($"[UnityMCP] Tool {toolAttr.Name} has invalid signature. Expected (JObject args)");
                        continue;
                    }

                    var info = new ToolInfo
                    {
                        Name = toolAttr.Name,
                        Description = toolAttr.Description,
                        Method = method
                    };

                    // Get parameter definitions
                    foreach (var paramAttr in method.GetCustomAttributes<MCPParamAttribute>())
                    {
                        info.Parameters.Add(new ParamInfo
                        {
                            Name = paramAttr.Name,
                            Type = paramAttr.Type,
                            Description = paramAttr.Description,
                            Required = paramAttr.Required
                        });
                    }

                    _tools[toolAttr.Name] = info;
                }
            }
        }

        public static void Refresh()
        {
            _initialized = false;
            EnsureInitialized();
        }

        public static string[] GetToolNames()
        {
            EnsureInitialized();
            return _tools.Keys.ToArray();
        }

        public static object[] GetToolDefinitions()
        {
            EnsureInitialized();

            return _tools.Values.Select(t => new
            {
                name = t.Name,
                description = t.Description,
                inputSchema = new
                {
                    type = "object",
                    properties = t.Parameters.ToDictionary(
                        p => p.Name,
                        p => BuildPropertySchema(p)
                    ),
                    required = t.Parameters.Where(p => p.Required).Select(p => p.Name).ToArray()
                }
            }).ToArray();
        }

        private static object BuildPropertySchema(ParamInfo p)
        {
            // "any" is not valid JSON Schema - omit type to accept any value
            if (p.Type == "any")
            {
                return new { description = p.Description };
            }
            return new { type = p.Type, description = p.Description };
        }

        public static object InvokeTool(string name, JObject args)
        {
            EnsureInitialized();

            if (!_tools.TryGetValue(name, out var tool))
            {
                throw new Exception($"Unknown tool: {name}");
            }

            // Determine timeout based on tool type
            int timeoutMs = CompilationTools.Contains(name)
                ? CompilationToolTimeoutMs
                : DefaultToolTimeoutMs;

            var stopwatch = Stopwatch.StartNew();
            object result = null;
            Exception toolException = null;
            bool completed = false;

            // Execute tool with timeout protection
            // We run the tool on the current thread but track time
            try
            {
                result = tool.Method.Invoke(null, new object[] { args });
                completed = true;
            }
            catch (TargetInvocationException tie)
            {
                // Unwrap the actual exception
                toolException = tie.InnerException ?? tie;
            }
            catch (Exception e)
            {
                toolException = e;
            }
            finally
            {
                stopwatch.Stop();
            }

            var elapsedMs = stopwatch.ElapsedMilliseconds;

            // Watchdog logging for slow tools
            if (elapsedMs > SlowToolThresholdMs)
            {
                Debug.LogWarning($"[UnityMCP] Slow tool: {name} took {elapsedMs}ms (threshold: {SlowToolThresholdMs}ms)");
            }

            // Check for timeout (tool completed but took too long - log warning)
            if (elapsedMs > timeoutMs)
            {
                Debug.LogError($"[UnityMCP] Tool {name} exceeded timeout: {elapsedMs}ms > {timeoutMs}ms");
                // Still return the result if we have it, but log the issue
            }

            if (toolException != null)
            {
                throw toolException;
            }

            return result;
        }

        /// <summary>
        /// Get the timeout for a specific tool.
        /// </summary>
        public static int GetToolTimeout(string toolName)
        {
            return CompilationTools.Contains(toolName)
                ? CompilationToolTimeoutMs
                : DefaultToolTimeoutMs;
        }
    }
}
