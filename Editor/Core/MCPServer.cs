using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace LocalMCP
{
    /// <summary>
    /// Minimal local-only MCP server for Unity Editor.
    ///
    /// Key design: HTTP listener starts IMMEDIATELY in static constructor.
    /// This ensures the server is online even when Unity is unfocused.
    /// Request processing waits for Unity's main thread, but connections
    /// are accepted immediately so clients don't get "connection refused".
    ///
    /// Safety features:
    /// - Request queue size limit (prevents memory growth during hangs)
    /// - Reduced request timeout (10s instead of 30s)
    /// - Tool execution timeout logging
    /// - Response size truncation
    /// </summary>
    [InitializeOnLoad]
    public static class MCPServer
    {
        private static HttpListener _listener;
        private static Thread _listenerThread;
        private static readonly Queue<PendingRequest> _pendingRequests = new();
        private static readonly object _lock = new();
        private static bool _running;
        private static bool _serverInitialized;
        private static bool _isCompiling;
        private static int _domainReloadCount;

        // Safety limits
        private const int MaxQueueSize = 20;
        private const int RequestTimeoutMs = 30000; // 30 seconds (needed for compilation)
        private const int MaxResponseSizeBytes = 50 * 1024; // 50KB response limit
        private const int StaleRequestAgeSeconds = 25; // Allow time for compilation

        // Synchronous request handling - HTTP thread waits for main thread
        private class PendingRequest
        {
            public HttpListenerContext Context;
            public ManualResetEventSlim CompletionEvent = new(false);
            public int DomainReloadId; // Track which domain reload this request belongs to
            public DateTime CreatedAt = DateTime.UtcNow;
        }

        public static int Port { get; private set; } = 8090;
        public static int QueuedRequests { get { lock (_lock) { return _pendingRequests.Count; } } }

        /// <summary>
        /// Force stop all MCP operations. Use when Unity is hung or unresponsive.
        /// This clears the request queue and stops the server.
        /// </summary>
        [MenuItem("Tools/MCP/Force Stop MCP (Emergency)", priority = 100)]
        public static void ForceStop()
        {
            Debug.LogWarning("[LocalMCP] Force stop initiated - clearing all pending requests");

            // Clear all pending requests immediately
            lock (_lock)
            {
                while (_pendingRequests.Count > 0)
                {
                    var pending = _pendingRequests.Dequeue();
                    try
                    {
                        pending.CompletionEvent.Set();
                    }
                    catch { }
                }
            }

            // Stop the server
            StopInternal(preserveAutoStart: false);

            Debug.Log("[LocalMCP] Force stop completed. Server stopped and queue cleared.");
        }

        /// <summary>
        /// Clear the request queue without stopping the server.
        /// Use when requests are piling up but you want to keep the server running.
        /// </summary>
        public static int ClearRequestQueue()
        {
            int cleared = 0;
            lock (_lock)
            {
                while (_pendingRequests.Count > 0)
                {
                    var pending = _pendingRequests.Dequeue();
                    try
                    {
                        pending.CompletionEvent.Set();
                    }
                    catch { }
                    cleared++;
                }
            }

            if (cleared > 0)
            {
                Debug.LogWarning($"[LocalMCP] Cleared {cleared} pending requests from queue");
            }

            return cleared;
        }
        public static bool IsRunning => _running && _listener != null && _listener.IsListening;
        public static int ToolCount => _serverInitialized ? MCPToolRegistry.GetToolNames().Length : 0;

        public static event Action OnServerStarted;
        public static event Action OnServerStopped;

        static MCPServer()
        {
            // Register for Unity callbacks
            EditorApplication.update += ProcessPendingRequests;
            EditorApplication.quitting += Stop;
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.compilationFinished += OnCompilationFinished;

            // AUTO-START IMMEDIATELY if enabled - don't wait for delayCall
            // This is the key fix: start the HTTP listener right now, not later
            if (EditorPrefs.GetBool("LocalMCP_AutoStart", false))
            {
                int port = EditorPrefs.GetInt("LocalMCP_Port", 8090);
                StartImmediate(port);
            }
        }

        private static void OnCompilationStarted(object obj)
        {
            _isCompiling = true;

            // Clear ALL pending requests immediately - they will time out gracefully
            // This prevents the "waiting for user code" hang
            lock (_lock)
            {
                while (_pendingRequests.Count > 0)
                {
                    var pending = _pendingRequests.Dequeue();
                    pending.CompletionEvent.Set(); // Release waiting HTTP thread
                }
            }

            // Don't stop the server - just mark as compiling
            // The HTTP listener stays up to accept connections, but returns "compiling" errors
        }

        private static void OnCompilationFinished(object obj)
        {
            _isCompiling = false;
            _domainReloadCount++; // Increment to invalidate any stale requests

            // Re-initialize tools after compilation (domain reload resets everything)
            _serverInitialized = false;

            // If server is still running (listener survived), just reinitialize tools
            if (IsRunning)
            {
                EditorApplication.delayCall += TryInitializeToolRegistry;
            }
            else if (EditorPrefs.GetBool("LocalMCP_AutoStart", false))
            {
                // Restart server if it died
                int port = EditorPrefs.GetInt("LocalMCP_Port", 8090);
                EditorApplication.delayCall += () => StartImmediate(port);
            }
        }

        /// <summary>
        /// Start server immediately without any delays.
        /// Called directly from static constructor.
        /// </summary>
        private static void StartImmediate(int port)
        {
            if (_running) return;

            // Try primary port, then fallback ports
            int[] portsToTry = port == 8090
                ? new[] { 8090, 8091, 8092 }
                : new[] { port };

            foreach (int tryPort in portsToTry)
            {
                try
                {
                    _listener = new HttpListener();
                    _listener.Prefixes.Add($"http://localhost:{tryPort}/");
                    _listener.Start();
                    _running = true;
                    Port = tryPort;

                    _listenerThread = new Thread(ListenLoop)
                    {
                        IsBackground = true,
                        Name = "LocalMCP-Listener"
                    };
                    _listenerThread.Start();

                    // Try to initialize tools immediately (works if Unity APIs are ready)
                    TryInitializeToolRegistry();

                    // Also schedule via delayCall as fallback
                    EditorApplication.delayCall += TryInitializeToolRegistry;

                    if (tryPort != port)
                    {
                        Debug.LogWarning($"[LocalMCP] Port {port} in use, started on fallback port {tryPort}");
                        EditorPrefs.SetInt("LocalMCP_Port", tryPort);
                    }
                    return; // Success!
                }
                catch (Exception e)
                {
                    _listener?.Close();
                    _listener = null;

                    // If this was the last port to try, log the error
                    if (tryPort == portsToTry[portsToTry.Length - 1])
                    {
                        Debug.LogError($"[LocalMCP] Failed to start on any port (tried {string.Join(", ", portsToTry)}): {e.Message}");
                        _running = false;
                    }
                }
            }
        }

        private static void TryInitializeToolRegistry()
        {
            if (!_running || _serverInitialized) return;

            try
            {
                MCPToolRegistry.Refresh();
                _serverInitialized = true;
                OnServerStarted?.Invoke();
            }
            catch (Exception e)
            {
                // Silent fail - will retry on next opportunity
                Debug.LogWarning($"[LocalMCP] Tool init deferred: {e.Message}");
            }
        }

        /// <summary>
        /// Public start method - for manual starts from menu.
        /// </summary>
        public static void Start(int port = 8090, bool silent = false)
        {
            if (IsRunning)
            {
                if (!silent) Debug.Log($"[LocalMCP] Already running on port {Port}");
                return;
            }

            Port = port;
            EditorPrefs.SetBool("LocalMCP_AutoStart", true);
            EditorPrefs.SetInt("LocalMCP_Port", port);

            StartImmediate(port);

            if (!silent && IsRunning)
                Debug.Log($"[LocalMCP] Server started on http://localhost:{port}/");
        }

        public static void Stop()
        {
            StopInternal(preserveAutoStart: false);
        }

        private static void StopInternal(bool preserveAutoStart)
        {
            _running = false;
            _serverInitialized = false;

            if (_listener != null)
            {
                try
                {
                    _listener.Stop();
                    _listener.Close();
                }
                catch { }
                _listener = null;
            }

            // Signal any waiting requests
            lock (_lock)
            {
                while (_pendingRequests.Count > 0)
                {
                    var pending = _pendingRequests.Dequeue();
                    pending.CompletionEvent.Set();
                }
            }

            if (!preserveAutoStart)
            {
                EditorPrefs.SetBool("LocalMCP_AutoStart", false);
                Debug.Log("[LocalMCP] Server stopped");
            }

            OnServerStopped?.Invoke();
        }

        public static void Restart()
        {
            var port = Port;
            StopInternal(preserveAutoStart: true);
            StartImmediate(port);
        }

        private static void ListenLoop()
        {
            while (_running && _listener != null)
            {
                try
                {
                    var context = _listener.GetContext();

                    // If compiling, return immediately with status
                    if (_isCompiling)
                    {
                        try
                        {
                            context.Response.StatusCode = 503;
                            var buffer = Encoding.UTF8.GetBytes("{\"error\":\"Unity is compiling, please retry in a few seconds\",\"isCompiling\":true}");
                            context.Response.ContentType = "application/json";
                            context.Response.ContentLength64 = buffer.Length;
                            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                            context.Response.Close();
                        }
                        catch { }
                        continue;
                    }

                    // Check queue size limit - reject if full
                    int currentQueueSize;
                    lock (_lock)
                    {
                        currentQueueSize = _pendingRequests.Count;
                    }

                    if (currentQueueSize >= MaxQueueSize)
                    {
                        try
                        {
                            context.Response.StatusCode = 503;
                            var buffer = Encoding.UTF8.GetBytes($"{{\"error\":\"Server busy - request queue full ({currentQueueSize}/{MaxQueueSize})\",\"hint\":\"Unity may be unresponsive. Try again in a few seconds.\"}}");
                            context.Response.ContentType = "application/json";
                            context.Response.ContentLength64 = buffer.Length;
                            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                            context.Response.Close();
                        }
                        catch { }
                        continue;
                    }

                    var pending = new PendingRequest
                    {
                        Context = context,
                        DomainReloadId = _domainReloadCount
                    };

                    lock (_lock)
                    {
                        _pendingRequests.Enqueue(pending);
                    }

                    // Wait for main thread to process (with reduced timeout)
                    // This keeps the HTTP connection open until processed
                    if (!pending.CompletionEvent.Wait(RequestTimeoutMs))
                    {
                        try
                        {
                            context.Response.StatusCode = 504;
                            var buffer = Encoding.UTF8.GetBytes($"{{\"error\":\"Request timed out after {RequestTimeoutMs}ms waiting for Unity main thread\",\"hint\":\"Unity may be busy, unfocused, or processing a slow operation\"}}");
                            context.Response.ContentType = "application/json";
                            context.Response.ContentLength64 = buffer.Length;
                            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                            context.Response.Close();
                        }
                        catch { }
                    }

                    pending.CompletionEvent.Dispose();
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (ThreadAbortException) { break; }
                catch (Exception e)
                {
                    if (_running)
                        Debug.LogError($"[LocalMCP] Listener error: {e.Message}");
                }
            }
        }

        private static void ProcessPendingRequests()
        {
            if (!_running) return;

            // Don't process requests while compiling - they'll be rejected in ListenLoop
            if (_isCompiling) return;

            // Try to initialize tools if not yet done (handles unfocused Unity case)
            if (!_serverInitialized)
            {
                TryInitializeToolRegistry();
            }

            int processed = 0;
            while (processed < 10)
            {
                PendingRequest pending = null;
                lock (_lock)
                {
                    if (_pendingRequests.Count > 0)
                        pending = _pendingRequests.Dequeue();
                }

                if (pending == null) break;

                // Skip stale requests from before domain reload
                if (pending.DomainReloadId != _domainReloadCount)
                {
                    pending.CompletionEvent.Set(); // Release the waiting HTTP thread
                    processed++;
                    continue;
                }

                // Skip requests that are too old (stale after domain reload)
                if ((DateTime.UtcNow - pending.CreatedAt).TotalSeconds > StaleRequestAgeSeconds)
                {
                    pending.CompletionEvent.Set(); // Release - the HTTP thread will timeout
                    processed++;
                    continue;
                }

                try
                {
                    HandleRequest(pending.Context);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[LocalMCP] Error handling request: {e.Message}");
                    try { pending.Context.Response.Close(); } catch { }
                }
                finally
                {
                    // Signal HTTP thread that request is complete
                    pending.CompletionEvent.Set();
                }

                processed++;
            }
        }

        private static void HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            try
            {
                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 200;
                    response.Close();
                    return;
                }

                string path = request.Url.AbsolutePath.TrimEnd('/');

                if (path == "/mcp" || path == "")
                {
                    if (request.HttpMethod == "POST")
                    {
                        HandleMCPRequest(request, response);
                    }
                    else
                    {
                        // GET - return status (works even before tools initialized)
                        SendJson(response, new
                        {
                            name = "LocalMCP",
                            version = "1.0.0",
                            status = _serverInitialized ? "running" : "initializing",
                            tools = _serverInitialized ? MCPToolRegistry.GetToolNames() : Array.Empty<string>()
                        });
                    }
                }
                else if (path == "/mcp/heartbeat" || path == "/heartbeat")
                {
                    // Heartbeat endpoint - AI agents can poll this to wait for readiness
                    HandleHeartbeat(response);
                }
                else
                {
                    response.StatusCode = 404;
                    SendJson(response, new { error = "Not found" });
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[LocalMCP] Request error: {e}");
                response.StatusCode = 500;
                SendJson(response, new { error = e.Message });
            }
            finally
            {
                try { response.Close(); } catch { }
            }
        }

        private static void HandleHeartbeat(HttpListenerResponse response)
        {
            // Provide detailed readiness information for AI agents
            // This allows agents to intelligently wait for Unity to be ready
            var isCompiling = EditorApplication.isCompiling;
            var hasErrors = EditorUtility.scriptCompilationFailed;
            var isPlaying = EditorApplication.isPlaying;
            var isPaused = EditorApplication.isPaused;

            // Calculate readiness score
            bool ready = _serverInitialized && !isCompiling && !hasErrors;

            SendJson(response, new
            {
                ready,
                timestamp = DateTime.UtcNow.ToString("o"),
                server = new
                {
                    initialized = _serverInitialized,
                    running = IsRunning,
                    port = Port,
                    toolCount = ToolCount
                },
                unity = new
                {
                    isCompiling,
                    compilationProgress = isCompiling ? GetCompilationProgress() : 0f,
                    hasErrors,
                    isPlaying,
                    isPaused,
                    focused = UnityEditorInternal.InternalEditorUtility.isApplicationActive
                },
                capabilities = new
                {
                    canExecuteTools = ready,
                    canModifyAssets = !isCompiling && !isPlaying,
                    canEnterPlayMode = !isCompiling && !hasErrors && !isPlaying,
                    canRunTests = !isCompiling && !isPlaying
                }
            });
        }

        private static float GetCompilationProgress()
        {
            // Unity doesn't expose compilation progress directly
            // Return 0.5 as a "compiling" indicator
            return 0.5f;
        }

        private static void HandleMCPRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            // If not initialized yet, return error
            if (!_serverInitialized)
            {
                SendJson(response, new
                {
                    jsonrpc = "2.0",
                    error = new { code = -32000, message = "Server still initializing, please retry" }
                });
                return;
            }

            string body;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                body = reader.ReadToEnd();
            }

            JObject json;
            try
            {
                json = JObject.Parse(body);
            }
            catch
            {
                SendJsonRpcError(response, null, -32700, "Parse error");
                return;
            }

            var id = json["id"];
            var method = json["method"]?.ToString();
            var @params = json["params"] as JObject ?? new JObject();

            object result;

            switch (method)
            {
                case "initialize":
                    result = new
                    {
                        protocolVersion = "2024-11-05",
                        capabilities = new { tools = new { listChanged = false } },
                        serverInfo = new { name = "LocalMCP", version = "1.0.0" }
                    };
                    break;

                case "tools/list":
                    result = new { tools = MCPToolRegistry.GetToolDefinitions() };
                    break;

                case "tools/call":
                    result = HandleToolCall(@params);
                    break;

                case "resources/list":
                    result = new { resources = Array.Empty<object>() };
                    break;

                case "prompts/list":
                    result = new { prompts = Array.Empty<object>() };
                    break;

                default:
                    SendJsonRpcError(response, id, -32601, $"Method not found: {method}");
                    return;
            }

            SendJsonRpcResult(response, id, result);
        }

        private static object HandleToolCall(JObject @params)
        {
            var toolName = @params["name"]?.ToString();
            var arguments = @params["arguments"] as JObject ?? new JObject();

            if (string.IsNullOrEmpty(toolName))
            {
                return new
                {
                    content = new[] { new { type = "text", text = "Error: Tool name required" } },
                    isError = true
                };
            }

            try
            {
                var result = MCPToolRegistry.InvokeTool(toolName, arguments);
                var resultJson = JsonConvert.SerializeObject(result, Formatting.Indented);

                // Truncate large responses to prevent memory issues
                if (resultJson.Length > MaxResponseSizeBytes)
                {
                    var truncatedResult = new
                    {
                        _truncated = true,
                        _originalSize = resultJson.Length,
                        _maxSize = MaxResponseSizeBytes,
                        _message = $"Response truncated from {resultJson.Length} to {MaxResponseSizeBytes} bytes. Use more specific queries or add filters.",
                        // Include the beginning of the result for context
                        _partialData = resultJson.Substring(0, Math.Min(MaxResponseSizeBytes - 500, resultJson.Length))
                    };
                    resultJson = JsonConvert.SerializeObject(truncatedResult, Formatting.Indented);
                    Debug.LogWarning($"[LocalMCP] Response for {toolName} truncated: {resultJson.Length} > {MaxResponseSizeBytes} bytes");
                }

                return new { content = new[] { new { type = "text", text = resultJson } } };
            }
            catch (Exception e)
            {
                return new
                {
                    content = new[] { new { type = "text", text = $"Error: {e.Message}" } },
                    isError = true
                };
            }
        }

        private static void SendJsonRpcResult(HttpListenerResponse response, JToken id, object result)
        {
            SendJson(response, new { jsonrpc = "2.0", id = id, result = result });
        }

        private static void SendJsonRpcError(HttpListenerResponse response, JToken id, int code, string message)
        {
            SendJson(response, new { jsonrpc = "2.0", id = id, error = new { code, message } });
        }

        private static void SendJson(HttpListenerResponse response, object obj)
        {
            response.ContentType = "application/json";
            var json = JsonConvert.SerializeObject(obj);
            var buffer = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
        }
    }
}
