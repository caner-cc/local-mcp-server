using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityMCP
{
    /// <summary>
    /// Watches for external file changes and auto-refreshes the AssetDatabase.
    /// This enables Claude Code to write scripts without requiring manual editor focus.
    /// </summary>
    [InitializeOnLoad]
    public static class AutoRefreshWatcher
    {
        private static FileSystemWatcher _watcher;
        private static readonly HashSet<string> _pendingChanges = new();
        private static readonly object _lock = new();
        private static double _lastRefreshTime;
        private static double _lastChangeTime;
        private static bool _refreshPending;

        // Configuration
        private const double RefreshDebounceSeconds = 1.0; // Wait for file writes to settle
        private const double MinRefreshIntervalSeconds = 2.0; // Don't refresh more often than this

        public static bool IsEnabled
        {
            get => EditorPrefs.GetBool("UnityMCP_AutoRefresh", true);
            set
            {
                EditorPrefs.SetBool("UnityMCP_AutoRefresh", value);
                if (value) StartWatching();
                else StopWatching();
            }
        }

        public static int PendingChangeCount
        {
            get
            {
                lock (_lock) return _pendingChanges.Count;
            }
        }

        static AutoRefreshWatcher()
        {
            EditorApplication.update += OnUpdate;
            EditorApplication.quitting += StopWatching;

            if (IsEnabled)
            {
                // Delay start to avoid conflicts with other initialization
                EditorApplication.delayCall += StartWatching;
            }
        }

        private static void StartWatching()
        {
            if (_watcher != null) return;

            try
            {
                var assetsPath = Path.GetFullPath("Assets");

                _watcher = new FileSystemWatcher(assetsPath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    Filter = "*.*",
                    EnableRaisingEvents = true
                };

                _watcher.Changed += OnFileChanged;
                _watcher.Created += OnFileChanged;
                _watcher.Deleted += OnFileChanged;
                _watcher.Renamed += OnFileRenamed;
                _watcher.Error += OnWatcherError;

                Debug.Log("[UnityMCP] AutoRefreshWatcher started - external file changes will auto-trigger compilation");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UnityMCP] Failed to start AutoRefreshWatcher: {e.Message}");
            }
        }

        private static void StopWatching()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
                Debug.Log("[UnityMCP] AutoRefreshWatcher stopped");
            }
        }

        private static void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            // Only care about script and meta files
            if (!ShouldTriggerRefresh(e.FullPath)) return;

            lock (_lock)
            {
                _pendingChanges.Add(e.FullPath);
                _lastChangeTime = EditorApplication.timeSinceStartup;
                _refreshPending = true;
            }
        }

        private static void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            if (!ShouldTriggerRefresh(e.FullPath) && !ShouldTriggerRefresh(e.OldFullPath)) return;

            lock (_lock)
            {
                _pendingChanges.Add(e.FullPath);
                _lastChangeTime = EditorApplication.timeSinceStartup;
                _refreshPending = true;
            }
        }

        private static void OnWatcherError(object sender, ErrorEventArgs e)
        {
            Debug.LogWarning($"[UnityMCP] FileSystemWatcher error: {e.GetException().Message}");

            // Restart watcher
            StopWatching();
            EditorApplication.delayCall += StartWatching;
        }

        private static bool ShouldTriggerRefresh(string path)
        {
            // Ignore temp files, .git, Library folder, etc.
            if (path.Contains("Library") || path.Contains(".git") || path.Contains("Temp"))
                return false;

            var ext = Path.GetExtension(path).ToLower();

            // Script files - definitely refresh
            if (ext == ".cs" || ext == ".asmdef" || ext == ".asmref")
                return true;

            // Asset files - also refresh
            if (ext == ".asset" || ext == ".prefab" || ext == ".unity" || ext == ".meta")
                return true;

            // Shader files
            if (ext == ".shader" || ext == ".cginc" || ext == ".hlsl")
                return true;

            return false;
        }

        private static void OnUpdate()
        {
            if (!_refreshPending) return;
            if (EditorApplication.isCompiling) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            double now = EditorApplication.timeSinceStartup;

            // Wait for debounce period (let file writes settle)
            if (now - _lastChangeTime < RefreshDebounceSeconds) return;

            // Don't refresh too frequently
            if (now - _lastRefreshTime < MinRefreshIntervalSeconds) return;

            List<string> changes;
            lock (_lock)
            {
                if (_pendingChanges.Count == 0)
                {
                    _refreshPending = false;
                    return;
                }

                changes = new List<string>(_pendingChanges);
                _pendingChanges.Clear();
                _refreshPending = false;
            }

            _lastRefreshTime = now;

            // Log what triggered the refresh
            int scriptCount = 0;
            foreach (var path in changes)
            {
                if (path.EndsWith(".cs")) scriptCount++;
            }

            if (scriptCount > 0)
            {
                Debug.Log($"[UnityMCP] Auto-refreshing: {scriptCount} script(s) changed externally");
            }

            // This is the key call - forces Unity to detect file changes
            AssetDatabase.Refresh(ImportAssetOptions.Default);
        }

        [MenuItem("Tools/MCP/Toggle Auto-Refresh Watcher")]
        private static void ToggleAutoRefresh()
        {
            IsEnabled = !IsEnabled;
            Debug.Log($"[UnityMCP] Auto-refresh watcher: {(IsEnabled ? "enabled" : "disabled")}");
        }

        [MenuItem("Tools/MCP/Toggle Auto-Refresh Watcher", true)]
        private static bool ToggleAutoRefreshValidate()
        {
            Menu.SetChecked("Tools/MCP/Toggle Auto-Refresh Watcher", IsEnabled);
            return true;
        }
    }
}
