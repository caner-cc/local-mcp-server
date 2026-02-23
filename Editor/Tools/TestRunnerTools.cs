using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace LocalMCP.Tools
{
    /// <summary>
    /// MCP tools for running Unity Test Runner tests programmatically.
    /// Allows Claude to run tests and verify changes automatically.
    /// </summary>
    public static class TestRunnerTools
    {
        private static TestRunnerApi _testRunnerApi;
        private static TestResultData _lastResults;
        private static bool _isRunning;
        private static DateTime _runStartTime;

        private class TestResultData
        {
            public DateTime StartTime;
            public DateTime EndTime;
            public int Passed;
            public int Failed;
            public int Skipped;
            public int Inconclusive;
            public List<TestResultEntry> Results = new();
        }

        private class TestResultEntry
        {
            public string Name;
            public string FullName;
            public string Status;
            public double Duration;
            public string Message;
            public string StackTrace;
        }

        private static TestRunnerApi GetApi()
        {
            if (_testRunnerApi == null)
            {
                _testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
            }
            return _testRunnerApi;
        }

        [MCPTool("test_run", "Run Unity Test Runner tests")]
        [MCPParam("mode", "string", "Test mode: playmode, editmode, or all (default: playmode)", false)]
        [MCPParam("filter", "string", "Filter tests by name pattern (optional)", false)]
        [MCPParam("category", "string", "Filter tests by category (optional)", false)]
        [MCPParam("assembly", "string", "Filter by assembly name (optional)", false)]
        [MCPParam("timeout", "integer", "Timeout in seconds (default: 300, max: 600)", false)]
        public static object TestRun(JObject args)
        {
            if (_isRunning)
            {
                // Check if tests have been running too long (stale state)
                var elapsed = DateTime.Now - _runStartTime;
                if (elapsed.TotalMinutes > 10)
                {
                    Debug.LogWarning("[MCP TestRunner] Resetting stale test runner state (was running for 10+ minutes)");
                    _isRunning = false;
                    _lastResults = null;
                }
                else
                {
                    return new
                    {
                        success = false,
                        message = "Tests are already running. Use test_status to check progress, or test_cancel to reset.",
                        elapsedSeconds = elapsed.TotalSeconds
                    };
                }
            }

            var mode = args["mode"]?.ToString()?.ToLower() ?? "playmode";
            var filterPattern = args["filter"]?.ToString();
            var category = args["category"]?.ToString();
            var assembly = args["assembly"]?.ToString();

            // Determine test mode
            TestMode testMode = mode switch
            {
                "editmode" => TestMode.EditMode,
                "all" => TestMode.EditMode | TestMode.PlayMode,
                _ => TestMode.PlayMode
            };

            // Build filter
            var filter = new Filter
            {
                testMode = testMode
            };

            if (!string.IsNullOrEmpty(filterPattern))
            {
                filter.testNames = new[] { filterPattern };
            }

            if (!string.IsNullOrEmpty(category))
            {
                filter.categoryNames = new[] { category };
            }

            if (!string.IsNullOrEmpty(assembly))
            {
                filter.assemblyNames = new[] { assembly };
            }

            // Initialize results
            _lastResults = new TestResultData
            {
                StartTime = DateTime.Now
            };
            _isRunning = true;
            _runStartTime = DateTime.Now;

            // Register callbacks
            var api = GetApi();
            api.RegisterCallbacks(new TestCallbacks());

            // Execute tests
            try
            {
                api.Execute(new ExecutionSettings(filter));

                return new
                {
                    success = true,
                    message = $"Tests started in {mode} mode. Use test_status to monitor progress.",
                    startTime = _runStartTime.ToString("HH:mm:ss")
                };
            }
            catch (Exception e)
            {
                _isRunning = false;
                return new
                {
                    success = false,
                    message = $"Failed to start tests: {e.Message}"
                };
            }
        }

        [MCPTool("test_results", "Get detailed results from the last test run")]
        [MCPParam("filter", "string", "Filter results: all, passed, failed, skipped (default: all)", false)]
        [MCPParam("count", "integer", "Maximum number of results to return (default: 50)", false)]
        public static object TestResults(JObject args)
        {
            // Try to load from XML file if in-memory results are not available
            if (_lastResults == null)
            {
                _lastResults = TryLoadResultsFromXml();
            }

            if (_lastResults == null)
            {
                return new
                {
                    success = false,
                    message = "No test results available. Run tests first with test_run."
                };
            }

            var filter = args["filter"]?.ToString()?.ToLower() ?? "all";
            var count = args["count"]?.ToObject<int>() ?? 50;

            var results = _lastResults.Results.AsEnumerable();

            // Apply filter
            results = filter switch
            {
                "passed" => results.Where(r => r.Status == "Passed"),
                "failed" => results.Where(r => r.Status == "Failed"),
                "skipped" => results.Where(r => r.Status == "Skipped"),
                _ => results
            };

            var filteredResults = results.Take(count).Select(r => new
            {
                name = r.Name,
                fullName = r.FullName,
                status = r.Status,
                duration = r.Duration,
                message = r.Message,
                stackTrace = r.StackTrace
            }).ToList();

            return new
            {
                summary = new
                {
                    passed = _lastResults.Passed,
                    failed = _lastResults.Failed,
                    skipped = _lastResults.Skipped,
                    inconclusive = _lastResults.Inconclusive,
                    total = _lastResults.Results.Count,
                    duration = (_lastResults.EndTime - _lastResults.StartTime).TotalSeconds
                },
                results = filteredResults,
                truncated = _lastResults.Results.Count > count
            };
        }

        /// <summary>
        /// Callbacks for test execution
        /// </summary>
        private class TestCallbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                Debug.Log($"[MCP TestRunner] Started running {CountTests(testsToRun)} tests");
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                _isRunning = false;
                if (_lastResults != null)
                {
                    _lastResults.EndTime = DateTime.Now;
                }

                var summary = $"Tests completed: {_lastResults?.Passed ?? 0} passed, {_lastResults?.Failed ?? 0} failed";
                if (_lastResults?.Failed > 0)
                {
                    Debug.LogWarning($"[MCP TestRunner] {summary}");
                }
                else
                {
                    Debug.Log($"[MCP TestRunner] {summary}");
                }
            }

            public void TestStarted(ITestAdaptor test)
            {
                // Individual test started
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (_lastResults == null) return;

                // Only track leaf tests (actual test methods, not fixtures)
                if (!result.HasChildren)
                {
                    var entry = new TestResultEntry
                    {
                        Name = result.Test.Name,
                        FullName = result.Test.FullName,
                        Status = result.TestStatus.ToString(),
                        Duration = result.Duration,
                        Message = result.Message,
                        StackTrace = result.StackTrace
                    };

                    _lastResults.Results.Add(entry);

                    switch (result.TestStatus)
                    {
                        case UnityEditor.TestTools.TestRunner.Api.TestStatus.Passed:
                            _lastResults.Passed++;
                            break;
                        case UnityEditor.TestTools.TestRunner.Api.TestStatus.Failed:
                            _lastResults.Failed++;
                            Debug.LogWarning($"[MCP TestRunner] FAILED: {result.Test.Name} - {result.Message}");
                            break;
                        case UnityEditor.TestTools.TestRunner.Api.TestStatus.Skipped:
                            _lastResults.Skipped++;
                            break;
                        case UnityEditor.TestTools.TestRunner.Api.TestStatus.Inconclusive:
                            _lastResults.Inconclusive++;
                            break;
                    }
                }
            }

            private int CountTests(ITestAdaptor test)
            {
                if (!test.HasChildren) return 1;
                return test.Children.Sum(CountTests);
            }
        }

        /// <summary>
        /// Collects test names from a test tree
        /// </summary>
        private static void CollectTestNames(ITestAdaptor test, List<string> results, string search)
        {
            if (!test.HasChildren)
            {
                if (string.IsNullOrEmpty(search) ||
                    test.FullName.ToLower().Contains(search.ToLower()))
                {
                    results.Add(test.FullName);
                }
            }
            else
            {
                foreach (var child in test.Children)
                {
                    CollectTestNames(child, results, search);
                }
            }
        }

        /// <summary>
        /// Try to load test results from the TestResults.xml file.
        /// This is useful when PlayMode tests cause a domain reload and in-memory results are lost.
        /// </summary>
        private static TestResultData TryLoadResultsFromXml()
        {
            try
            {
                // Unity writes test results to this location
                string resultsPath = Path.Combine(
                    Application.persistentDataPath,
                    "TestResults.xml"
                );

                if (!File.Exists(resultsPath))
                {
                    return null;
                }

                // Only use results from the last 5 minutes
                var fileInfo = new FileInfo(resultsPath);
                if ((DateTime.Now - fileInfo.LastWriteTime).TotalMinutes > 5)
                {
                    return null;
                }

                var doc = XDocument.Load(resultsPath);
                var testRun = doc.Root;

                if (testRun == null || testRun.Name != "test-run")
                {
                    return null;
                }

                var result = new TestResultData
                {
                    Passed = int.Parse(testRun.Attribute("passed")?.Value ?? "0"),
                    Failed = int.Parse(testRun.Attribute("failed")?.Value ?? "0"),
                    Skipped = int.Parse(testRun.Attribute("skipped")?.Value ?? "0"),
                    Inconclusive = int.Parse(testRun.Attribute("inconclusive")?.Value ?? "0")
                };

                // Parse timestamps
                if (DateTime.TryParse(testRun.Attribute("start-time")?.Value, out var startTime))
                {
                    result.StartTime = startTime;
                }
                if (DateTime.TryParse(testRun.Attribute("end-time")?.Value, out var endTime))
                {
                    result.EndTime = endTime;
                }

                // Parse individual test cases
                foreach (var testCase in doc.Descendants("test-case"))
                {
                    var entry = new TestResultEntry
                    {
                        Name = testCase.Attribute("name")?.Value ?? "Unknown",
                        FullName = testCase.Attribute("fullname")?.Value ?? "Unknown",
                        Status = testCase.Attribute("result")?.Value ?? "Unknown",
                        Duration = double.TryParse(testCase.Attribute("duration")?.Value, out var d) ? d : 0
                    };

                    // Get failure message if present
                    var failure = testCase.Element("failure");
                    if (failure != null)
                    {
                        entry.Message = failure.Element("message")?.Value;
                        entry.StackTrace = failure.Element("stack-trace")?.Value;
                    }

                    result.Results.Add(entry);
                }

                Debug.Log($"[MCP TestRunner] Loaded {result.Results.Count} test results from XML");
                return result;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MCP TestRunner] Failed to load XML results: {e.Message}");
                return null;
            }
        }
    }
}
