using System;
using System.Threading;
using UnityEditor;
using UnityEditor.Compilation;

namespace UnityMCP
{
    /// <summary>
    /// Compilation awaiter that provides both synchronous and event-based waiting.
    ///
    /// IMPORTANT: The synchronous version uses polling with short sleeps.
    /// This is necessary because:
    /// 1. Unity's compilation events fire on the main thread
    /// 2. Blocking the main thread with .GetAwaiter().GetResult() causes deadlocks
    /// 3. We can't use async/await in synchronous tool handlers
    ///
    /// The polling approach is safe because:
    /// - It doesn't block Unity's message pump
    /// - Short sleep intervals (50ms) provide responsive detection
    /// - Total timeout prevents infinite waits
    /// </summary>
    public class CompilationAwaiter
    {
        private volatile bool _compilationFinished;
        private volatile bool _compilationSucceeded;
        private volatile string _lastError;
        private volatile int _errorCount;
        private volatile int _warningCount;
        private readonly object _lock = new object();

        public class CompilationResult
        {
            public bool Success { get; set; }
            public bool TimedOut { get; set; }
            public bool WasCompiling { get; set; }
            public int WaitedMs { get; set; }
            public string Error { get; set; }
            public int ErrorCount { get; set; }
            public int WarningCount { get; set; }
        }

        public CompilationAwaiter()
        {
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
        }

        private void OnCompilationStarted(object obj)
        {
            lock (_lock)
            {
                _compilationFinished = false;
                _compilationSucceeded = false;
                _lastError = null;
                _errorCount = 0;
                _warningCount = 0;
            }
        }

        private void OnCompilationFinished(object obj)
        {
            lock (_lock)
            {
                _compilationFinished = true;
                _compilationSucceeded = !EditorUtility.scriptCompilationFailed;

                if (!_compilationSucceeded && string.IsNullOrEmpty(_lastError))
                {
                    _lastError = "Compilation failed - check Unity Console for details";
                }
            }
        }

        private void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            lock (_lock)
            {
                foreach (var msg in messages)
                {
                    if (msg.type == CompilerMessageType.Error)
                    {
                        _errorCount++;
                        if (string.IsNullOrEmpty(_lastError))
                        {
                            _lastError = $"{msg.file}({msg.line},{msg.column}): {msg.message}";
                        }
                    }
                    else if (msg.type == CompilerMessageType.Warning)
                    {
                        _warningCount++;
                    }
                }
            }
        }

        /// <summary>
        /// Wait for compilation to complete synchronously.
        /// Uses polling with short sleeps to avoid deadlocks.
        ///
        /// This is safe to call from MCP tool handlers.
        /// </summary>
        /// <param name="timeoutMs">Maximum time to wait in milliseconds</param>
        /// <returns>Result indicating success/failure and timing</returns>
        public CompilationResult WaitForCompilationSync(int timeoutMs = 30000)
        {
            var startTime = DateTime.Now;

            // Quick check: if not compiling and no pending refresh, return immediately
            if (!EditorApplication.isCompiling)
            {
                // Trigger a refresh to detect any pending changes
                AssetDatabase.Refresh();

                // Brief wait to see if compilation starts
                Thread.Sleep(100);

                if (!EditorApplication.isCompiling)
                {
                    // No compilation needed
                    bool hasExistingErrors = EditorUtility.scriptCompilationFailed;
                    return new CompilationResult
                    {
                        Success = !hasExistingErrors,
                        WasCompiling = false,
                        WaitedMs = (int)(DateTime.Now - startTime).TotalMilliseconds,
                        Error = hasExistingErrors ? "Project has existing compilation errors" : null
                    };
                }
            }

            // Reset our state tracking
            lock (_lock)
            {
                _compilationFinished = false;
            }

            // Poll for completion with short sleeps
            // This allows Unity's main thread to process compilation events
            const int pollIntervalMs = 50;
            int elapsed = 0;

            while (elapsed < timeoutMs)
            {
                // Check if compilation finished (either via event or by polling)
                bool finished;
                lock (_lock)
                {
                    finished = _compilationFinished || !EditorApplication.isCompiling;
                }

                if (finished)
                {
                    // Double-check we're really done
                    Thread.Sleep(50);
                    if (!EditorApplication.isCompiling)
                    {
                        break;
                    }
                }

                Thread.Sleep(pollIntervalMs);
                elapsed += pollIntervalMs;
            }

            // Gather final state
            bool success;
            string error;
            int errorCount, warningCount;

            lock (_lock)
            {
                success = _compilationSucceeded || !EditorUtility.scriptCompilationFailed;
                error = _lastError;
                errorCount = _errorCount;
                warningCount = _warningCount;
            }

            bool timedOut = elapsed >= timeoutMs && EditorApplication.isCompiling;
            int totalWaited = (int)(DateTime.Now - startTime).TotalMilliseconds;

            return new CompilationResult
            {
                Success = success && !timedOut,
                TimedOut = timedOut,
                WasCompiling = true,
                WaitedMs = totalWaited,
                Error = timedOut ? $"Compilation timed out after {timeoutMs}ms" : error,
                ErrorCount = errorCount,
                WarningCount = warningCount
            };
        }

        /// <summary>
        /// Check if Unity is currently compiling.
        /// </summary>
        public bool IsCompiling => EditorApplication.isCompiling;

        /// <summary>
        /// Check if there are existing compilation errors.
        /// </summary>
        public bool HasErrors => EditorUtility.scriptCompilationFailed;

        public void Dispose()
        {
            CompilationPipeline.compilationStarted -= OnCompilationStarted;
            CompilationPipeline.compilationFinished -= OnCompilationFinished;
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
        }
    }

    /// <summary>
    /// Singleton instance for global access.
    /// </summary>
    public static class CompilationAwaiterInstance
    {
        private static CompilationAwaiter _instance;

        public static CompilationAwaiter Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new CompilationAwaiter();
                return _instance;
            }
        }
    }
}
