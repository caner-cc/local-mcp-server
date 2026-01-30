using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMCP.Tools
{
    /// <summary>
    /// Tools for reading Unity console logs.
    /// Automatically reduces logging overhead when editor is unfocused.
    /// </summary>
    [InitializeOnLoad]
    public static class ConsoleTools
    {
        private static readonly List<LogEntry> _logBuffer = new();
        private static bool _isListening;
        private const int MaxLogEntries = 500;
        private const int MaxLogEntriesUnfocused = 50; // Smaller buffer when unfocused

        // Focus tracking
        private static bool _editorFocused = true;
        private static bool _silentModeWhenUnfocused = true; // Skip non-error logs when unfocused

        private class LogEntry
        {
            public string Message;
            public string StackTrace;
            public LogType Type;
            public DateTime Timestamp;
        }

        static ConsoleTools()
        {
            StartListening();

            // Subscribe to focus changes
            EditorApplication.focusChanged += OnEditorFocusChanged;

            // Also check on update for initial state
            EditorApplication.update += CheckInitialFocus;
        }

        private static void CheckInitialFocus()
        {
            // Only run once
            EditorApplication.update -= CheckInitialFocus;
            _editorFocused = UnityEditorInternal.InternalEditorUtility.isApplicationActive;
        }

        private static void OnEditorFocusChanged(bool hasFocus)
        {
            _editorFocused = hasFocus;

            // When regaining focus, trim buffer if it got too large
            if (hasFocus)
            {
                lock (_logBuffer)
                {
                    // Aggressive trim on focus regain to prevent slowdown
                    while (_logBuffer.Count > MaxLogEntries)
                    {
                        _logBuffer.RemoveAt(0);
                    }
                }
            }
        }

        private static void StartListening()
        {
            if (_isListening) return;
            Application.logMessageReceived += OnLogMessage;
            _isListening = true;
        }

        private static void OnLogMessage(string message, string stackTrace, LogType type)
        {
            // When unfocused with silent mode, only capture errors/exceptions
            if (!_editorFocused && _silentModeWhenUnfocused)
            {
                if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
                {
                    return; // Skip non-error logs when unfocused
                }
            }

            lock (_logBuffer)
            {
                _logBuffer.Add(new LogEntry
                {
                    Message = message,
                    StackTrace = stackTrace,
                    Type = type,
                    Timestamp = DateTime.Now
                });

                // Use smaller buffer limit when unfocused
                var maxEntries = _editorFocused ? MaxLogEntries : MaxLogEntriesUnfocused;
                while (_logBuffer.Count > maxEntries)
                {
                    _logBuffer.RemoveAt(0);
                }
            }
        }

        [MCPTool("console_read", "Read Unity console logs")]
        [MCPParam("count", "integer", "Number of recent entries to return (default: 50)", false)]
        [MCPParam("type", "string", "Filter by type: log, warning, error, exception, assert, all (default: all)", false)]
        [MCPParam("search", "string", "Filter by text content (optional)", false)]
        [MCPParam("clear", "boolean", "Clear buffer after reading (default: false)", false)]
        public static object ConsoleRead(JObject args)
        {
            var count = args["count"]?.ToObject<int>() ?? 50;
            var typeFilter = args["type"]?.ToString()?.ToLower() ?? "all";
            var search = args["search"]?.ToString();
            var clear = args["clear"]?.ToObject<bool>() ?? false;

            List<object> results;
            lock (_logBuffer)
            {
                var filtered = _logBuffer.AsEnumerable();

                // Type filter
                if (typeFilter != "all")
                {
                    LogType? targetType = typeFilter switch
                    {
                        "log" => LogType.Log,
                        "warning" => LogType.Warning,
                        "error" => LogType.Error,
                        "exception" => LogType.Exception,
                        "assert" => LogType.Assert,
                        _ => null
                    };

                    if (targetType.HasValue)
                    {
                        filtered = filtered.Where(e => e.Type == targetType.Value);
                    }
                }

                // Text search
                if (!string.IsNullOrEmpty(search))
                {
                    filtered = filtered.Where(e =>
                        e.Message.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
                }

                results = filtered
                    .TakeLast(count)
                    .Select(e => (object)new
                    {
                        type = e.Type.ToString().ToLower(),
                        message = e.Message,
                        stackTrace = e.Type == LogType.Error || e.Type == LogType.Exception
                            ? e.StackTrace
                            : null,
                        timestamp = e.Timestamp.ToString("HH:mm:ss.fff")
                    })
                    .ToList();

                if (clear)
                {
                    _logBuffer.Clear();
                }
            }

            return new
            {
                count = results.Count,
                logs = results
            };
        }

        [MCPTool("console_clear", "Clear the Unity console and log buffer")]
        public static object ConsoleClear(JObject args)
        {
            // Clear internal buffer
            lock (_logBuffer)
            {
                _logBuffer.Clear();
            }

            // Clear Unity console using reflection (internal API)
            try
            {
                var assembly = Assembly.GetAssembly(typeof(Editor));
                var logEntries = assembly.GetType("UnityEditor.LogEntries");
                var clearMethod = logEntries.GetMethod("Clear", BindingFlags.Static | BindingFlags.Public);
                clearMethod?.Invoke(null, null);

                return new { success = true, message = "Console cleared" };
            }
            catch (Exception e)
            {
                return new { success = false, message = $"Buffer cleared, but failed to clear Unity console: {e.Message}" };
            }
        }
    }
}
