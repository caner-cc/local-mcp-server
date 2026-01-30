using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace UnityMCP.Tools
{
    /// <summary>
    /// Tools designed for fully autonomous agentic development.
    /// These tools enable Claude Code to work without manual intervention.
    /// </summary>
    public static class AgenticTools
    {
        private static bool _compilationSucceeded;
        private static string _lastCompilationError;

        static AgenticTools()
        {
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
        }

        private static void OnCompilationFinished(object obj)
        {
            _compilationSucceeded = !EditorUtility.scriptCompilationFailed;
        }

        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            foreach (var msg in messages)
            {
                if (msg.type == CompilerMessageType.Error)
                {
                    _lastCompilationError = $"{msg.file}({msg.line}): {msg.message}";
                    break;
                }
            }
        }

        [MCPTool("write_script", "Write a C# script and trigger compilation. Returns after compilation completes.")]
        [MCPParam("path", "string", "Path relative to project root (e.g., 'Assets/Scripts/MyScript.cs')")]
        [MCPParam("content", "string", "Full C# script content")]
        [MCPParam("waitForCompilation", "boolean", "Wait for compilation to complete (default: true)", false)]
        public static object WriteScript(JObject args)
        {
            var path = args["path"]?.ToString();
            var content = args["content"]?.ToString();
            var waitForCompilation = args["waitForCompilation"]?.ToObject<bool>() ?? true;

            if (string.IsNullOrEmpty(path))
                return new { success = false, error = "path is required" };

            if (string.IsNullOrEmpty(content))
                return new { success = false, error = "content is required" };

            // Ensure it's a valid script path
            if (!path.EndsWith(".cs"))
                return new { success = false, error = "path must end with .cs" };

            // Normalize path
            path = path.Replace("\\", "/");
            if (!path.StartsWith("Assets/"))
                path = "Assets/" + path;

            try
            {
                // Ensure directory exists
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Write the file
                var fullPath = Path.GetFullPath(path);
                File.WriteAllText(fullPath, content);

                // Clear previous error
                _lastCompilationError = null;
                _compilationSucceeded = true;

                // Force Unity to detect the change
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                if (!waitForCompilation)
                {
                    return new
                    {
                        success = true,
                        message = $"Script written to {path}. Compilation triggered but not awaited.",
                        path,
                        isCompiling = EditorApplication.isCompiling
                    };
                }

                // Wait for compilation using event-based system (no polling!)
                var result = CompilationAwaiterInstance.Instance.WaitForCompilationSync(30000);

                return new
                {
                    success = result.Success,
                    message = result.Success
                        ? $"Script written and compiled successfully: {path}"
                        : $"Script written but compilation failed: {result.Error}",
                    path,
                    compiledSuccessfully = result.Success,
                    compilationError = result.Error,
                    waitedMs = result.WaitedMs,
                    timedOut = result.TimedOut
                };
            }
            catch (Exception e)
            {
                return new { success = false, error = $"Failed to write script: {e.Message}" };
            }
        }

        [MCPTool("wait_for_compilation", "Wait for Unity script compilation to complete. Use after making code changes.")]
        [MCPParam("timeoutMs", "integer", "Maximum wait time in milliseconds (default: 30000)", false)]
        public static object WaitForCompilation(JObject args)
        {
            var timeoutMs = args["timeoutMs"]?.ToObject<int>() ?? 30000;

            // Use event-based awaiter - no polling!
            var result = CompilationAwaiterInstance.Instance.WaitForCompilationSync(timeoutMs);

            return new
            {
                success = result.Success,
                message = result.Success
                    ? "Compilation completed successfully"
                    : result.TimedOut
                        ? $"Compilation timed out after {timeoutMs}ms"
                        : $"Compilation failed: {result.Error}",
                wasCompiling = result.WasCompiling,
                waitedMs = result.WaitedMs,
                hasErrors = !result.Success,
                lastError = result.Error,
                timedOut = result.TimedOut
            };
        }

        [MCPTool("force_refresh", "Force Unity to refresh and detect all file changes. Use after writing files externally.")]
        [MCPParam("synchronous", "boolean", "Wait for import to complete (default: true)", false)]
        public static object ForceRefresh(JObject args)
        {
            var synchronous = args["synchronous"]?.ToObject<bool>() ?? true;

            try
            {
                var options = synchronous
                    ? ImportAssetOptions.ForceSynchronousImport
                    : ImportAssetOptions.Default;

                int pendingBefore = AutoRefreshWatcher.PendingChangeCount;

                AssetDatabase.Refresh(options);

                return new
                {
                    success = true,
                    message = "Asset database refreshed",
                    synchronous,
                    isCompiling = EditorApplication.isCompiling,
                    pendingChangesCleared = pendingBefore
                };
            }
            catch (Exception e)
            {
                return new { success = false, error = $"Refresh failed: {e.Message}" };
            }
        }

        [MCPTool("mcp_health", "Get detailed MCP server health and Unity editor state for diagnostics.")]
        public static object MCPHealth(JObject args)
        {
            bool autoRefreshEnabled = false;
            int pendingChanges = 0;

            try
            {
                autoRefreshEnabled = AutoRefreshWatcher.IsEnabled;
                pendingChanges = AutoRefreshWatcher.PendingChangeCount;
            }
            catch { }

            return new
            {
                success = true,
                mcp = new
                {
                    serverRunning = MCPServer.IsRunning,
                    port = MCPServer.Port,
                    toolCount = MCPServer.ToolCount
                },
                unity = new
                {
                    isCompiling = EditorApplication.isCompiling,
                    isPlaying = EditorApplication.isPlaying,
                    isPaused = EditorApplication.isPaused,
                    hasCompilationErrors = EditorUtility.scriptCompilationFailed,
                    lastCompilationError = EditorUtility.scriptCompilationFailed ? _lastCompilationError : null
                },
                autoRefresh = new
                {
                    enabled = autoRefreshEnabled,
                    pendingChanges
                },
                readiness = new
                {
                    canExecuteTools = !EditorApplication.isCompiling && !EditorUtility.scriptCompilationFailed,
                    canModifyAssets = !EditorApplication.isCompiling && !EditorApplication.isPlaying,
                    canEnterPlayMode = !EditorApplication.isCompiling && !EditorUtility.scriptCompilationFailed && !EditorApplication.isPlaying
                }
            };
        }

        [MCPTool("ensure_ready", "Block until Unity is ready for operations (not compiling, no errors). Use before important operations.")]
        [MCPParam("timeoutMs", "integer", "Maximum wait time (default: 60000)", false)]
        [MCPParam("requireNoErrors", "boolean", "Fail if there are compilation errors (default: true)", false)]
        public static object EnsureReady(JObject args)
        {
            var timeoutMs = args["timeoutMs"]?.ToObject<int>() ?? 60000;
            var requireNoErrors = args["requireNoErrors"]?.ToObject<bool>() ?? true;

            // Use event-based awaiter - much more efficient than polling
            var result = CompilationAwaiterInstance.Instance.WaitForCompilationSync(timeoutMs);

            if (result.TimedOut)
            {
                return new
                {
                    success = false,
                    ready = false,
                    error = "Timed out waiting for compilation to complete",
                    waitedMs = result.WaitedMs,
                    stillCompiling = EditorApplication.isCompiling
                };
            }

            bool hasErrors = !result.Success;

            if (requireNoErrors && hasErrors)
            {
                return new
                {
                    success = false,
                    ready = false,
                    error = $"Compilation errors present: {result.Error}",
                    hasCompilationErrors = true,
                    waitedMs = result.WaitedMs
                };
            }

            return new
            {
                success = true,
                ready = true,
                message = "Unity is ready for operations",
                hasCompilationErrors = hasErrors,
                waitedMs = result.WaitedMs,
                serverRunning = MCPServer.IsRunning
            };
        }

        [MCPTool("delete_script", "Delete a C# script and wait for compilation.")]
        [MCPParam("path", "string", "Path to the script to delete")]
        [MCPParam("waitForCompilation", "boolean", "Wait for compilation (default: true)", false)]
        public static object DeleteScript(JObject args)
        {
            var path = args["path"]?.ToString();
            var waitForCompilation = args["waitForCompilation"]?.ToObject<bool>() ?? true;

            if (string.IsNullOrEmpty(path))
                return new { success = false, error = "path is required" };

            // Normalize path
            path = path.Replace("\\", "/");
            if (!path.StartsWith("Assets/"))
                path = "Assets/" + path;

            try
            {
                // Delete via AssetDatabase for proper cleanup
                bool deleted = AssetDatabase.DeleteAsset(path);

                if (!deleted)
                {
                    // Try direct file delete
                    var fullPath = Path.GetFullPath(path);
                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                        // Also delete meta file
                        var metaPath = fullPath + ".meta";
                        if (File.Exists(metaPath))
                            File.Delete(metaPath);

                        AssetDatabase.Refresh();
                        deleted = true;
                    }
                }

                if (!deleted)
                {
                    return new { success = false, error = $"File not found: {path}" };
                }

                if (!waitForCompilation)
                {
                    return new
                    {
                        success = true,
                        message = $"Deleted {path}",
                        isCompiling = EditorApplication.isCompiling
                    };
                }

                // Wait for compilation using event-based system
                var result = CompilationAwaiterInstance.Instance.WaitForCompilationSync(30000);

                return new
                {
                    success = result.Success,
                    message = result.Success
                        ? $"Script deleted and project recompiled: {path}"
                        : $"Script deleted but compilation failed: {result.Error}",
                    path,
                    hasErrors = !result.Success,
                    waitedMs = result.WaitedMs,
                    timedOut = result.TimedOut
                };
            }
            catch (Exception e)
            {
                return new { success = false, error = $"Failed to delete script: {e.Message}" };
            }
        }

        [MCPTool("modify_script", "Read, modify, and rewrite a script with compilation wait.")]
        [MCPParam("path", "string", "Path to the script")]
        [MCPParam("oldContent", "string", "Content to find and replace")]
        [MCPParam("newContent", "string", "Replacement content")]
        [MCPParam("waitForCompilation", "boolean", "Wait for compilation (default: true)", false)]
        public static object ModifyScript(JObject args)
        {
            var path = args["path"]?.ToString();
            var oldContent = args["oldContent"]?.ToString();
            var newContent = args["newContent"]?.ToString();
            var waitForCompilation = args["waitForCompilation"]?.ToObject<bool>() ?? true;

            if (string.IsNullOrEmpty(path))
                return new { success = false, error = "path is required" };
            if (string.IsNullOrEmpty(oldContent))
                return new { success = false, error = "oldContent is required" };
            if (newContent == null) // Allow empty string
                return new { success = false, error = "newContent is required" };

            // Normalize path
            path = path.Replace("\\", "/");
            if (!path.StartsWith("Assets/"))
                path = "Assets/" + path;

            try
            {
                var fullPath = Path.GetFullPath(path);

                if (!File.Exists(fullPath))
                    return new { success = false, error = $"File not found: {path}" };

                var content = File.ReadAllText(fullPath);
                int matchCount = 0;

                // Count matches
                int index = 0;
                while ((index = content.IndexOf(oldContent, index)) != -1)
                {
                    matchCount++;
                    index += oldContent.Length;
                }

                if (matchCount == 0)
                {
                    return new
                    {
                        success = false,
                        error = "oldContent not found in file",
                        path,
                        searched = oldContent.Length > 100 ? oldContent.Substring(0, 100) + "..." : oldContent
                    };
                }

                // Perform replacement
                var newFileContent = content.Replace(oldContent, newContent);
                File.WriteAllText(fullPath, newFileContent);

                _lastCompilationError = null;
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                if (!waitForCompilation)
                {
                    return new
                    {
                        success = true,
                        message = $"Modified {path} ({matchCount} replacement(s))",
                        matchCount,
                        isCompiling = EditorApplication.isCompiling
                    };
                }

                // Wait for compilation using event-based system
                var result = CompilationAwaiterInstance.Instance.WaitForCompilationSync(30000);

                return new
                {
                    success = result.Success,
                    message = result.Success
                        ? $"Script modified and compiled: {path}"
                        : $"Script modified but compilation failed: {result.Error}",
                    path,
                    matchCount,
                    hasErrors = !result.Success,
                    compilationError = result.Error,
                    waitedMs = result.WaitedMs,
                    timedOut = result.TimedOut
                };
            }
            catch (Exception e)
            {
                return new { success = false, error = $"Failed to modify script: {e.Message}" };
            }
        }
    }
}
