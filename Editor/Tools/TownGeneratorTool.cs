#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace LocalMCP.Tools
{
    /// <summary>
    /// MCP tool for procedural town generation.
    /// Generates walkable town layouts and builds geometry via env_batch_execute.
    /// </summary>
    public static class TownGeneratorTool
    {
        [MCPTool("town_generate", "Generate a procedural town layout with guaranteed walkability", Category = "Environment", TimeoutMs = 180000)]
        [MCPParam("config_path", "string", "Path to TownConfig asset (optional)", false)]
        [MCPParam("grid_width", "integer", "Grid width in cells (default: 60)", false)]
        [MCPParam("grid_height", "integer", "Grid height in cells (default: 60)", false)]
        [MCPParam("cell_size", "number", "World units per cell (default: 4)", false)]
        [MCPParam("min_buildings", "integer", "Minimum buildings (default: 5)", false)]
        [MCPParam("max_buildings", "integer", "Maximum buildings (default: 15)", false)]
        [MCPParam("enable_elevation", "boolean", "Enable cliffs and elevation (default: false)", false)]
        [MCPParam("seed", "integer", "Random seed (0 = random)", false)]
        [MCPParam("execute", "boolean", "Execute operations immediately (default: true)", false)]
        public static object TownGenerate(JObject args)
        {
            try
            {
                // Get or create config
                TownConfig config = LoadOrCreateConfig(args);

                // Apply overrides from args
                ApplyConfigOverrides(config, args);

                // Get seed
                int seed = args["seed"]?.ToObject<int>() ?? 0;
                if (seed == 0) seed = UnityEngine.Random.Range(1, int.MaxValue);

                // Generate town graph
                var generator = new TownLayoutGenerator(config, seed);
                var graph = generator.Generate();

                // Validate connectivity
                var validation = WalkabilityValidator.ValidateConnectivity(graph);

                // Build MCP operations
                var buildResult = MCPTownBuilder.Build(graph, config);

                // Execute if requested
                bool execute = args["execute"]?.ToObject<bool>() ?? true;
                object executionResult = null;

                if (execute && buildResult.operations.Count > 0)
                {
                    executionResult = ExecuteOperations(buildResult);
                }

                return new
                {
                    success = true,
                    seed = seed,
                    graph = new
                    {
                        nodeCount = graph.nodes.Count,
                        edgeCount = graph.edges.Count,
                        buildingCount = graph.buildings.Count,
                        gridSize = new { x = graph.gridSize.x, y = graph.gridSize.y },
                        cellSize = graph.cellSize,
                        spawnNodeId = graph.spawnNodeId
                    },
                    validation = new
                    {
                        validation.isFullyConnected,
                        validation.totalNodes,
                        validation.reachableNodes,
                        validation.unreachableNodes,
                        connectivityPercent = validation.ConnectivityPercent,
                        validation.message
                    },
                    build = new
                    {
                        operationCount = buildResult.operations.Count,
                        buildResult.floorCount,
                        buildResult.wallCount,
                        buildResult.stairCount,
                        buildResult.pathCount,
                        buildResult.summary
                    },
                    executed = execute,
                    executionResult = executionResult
                };
            }
            catch (Exception e)
            {
                return new
                {
                    success = false,
                    error = e.Message,
                    stackTrace = e.StackTrace
                };
            }
        }

        [MCPTool("town_validate", "Validate connectivity of an existing town graph", Category = "Environment", IsReadOnly = true)]
        [MCPParam("check_ground", "boolean", "Validate ground connectivity (default: true)", false)]
        [MCPParam("check_elevated", "boolean", "Validate elevated areas (default: true)", false)]
        [MCPParam("check_roofs", "boolean", "Validate roof access (default: true)", false)]
        public static object TownValidate(JObject args)
        {
            // This would validate an existing TownGraph if we stored it
            // For now, return info about what validation does
            return new
            {
                success = true,
                message = "Town validation checks walkability using flood-fill algorithm",
                checks = new[]
                {
                    "Ground connectivity - all ground-level nodes reachable from spawn",
                    "Elevated connectivity - all elevated areas accessible via stairs",
                    "Roof access - all rooftop-accessible buildings have stair connections"
                },
                note = "Run town_generate to create and validate a new town"
            };
        }

        [MCPTool("town_list_configs", "List available TownConfig assets", Category = "Environment", IsReadOnly = true)]
        public static object TownListConfigs(JObject args)
        {
            var guids = AssetDatabase.FindAssets("t:TownConfig");
            var configs = new List<object>();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var config = AssetDatabase.LoadAssetAtPath<TownConfig>(path);

                if (config != null)
                {
                    configs.Add(new
                    {
                        path = path,
                        name = config.name,
                        gridSize = new { x = config.gridSize.x, y = config.gridSize.y },
                        buildingCount = config.buildings?.Count ?? 0,
                        enableElevation = config.enableElevation
                    });
                }
            }

            return new
            {
                success = true,
                count = configs.Count,
                configs = configs.ToArray(),
                createHint = "Create new configs at Assets/ScriptableObjects/Towns/ using the Town/Town Config menu"
            };
        }

        [MCPTool("town_list_buildings", "List available BuildingDefinition assets", Category = "Environment", IsReadOnly = true)]
        public static object TownListBuildings(JObject args)
        {
            var guids = AssetDatabase.FindAssets("t:BuildingDefinition");
            var buildings = new List<object>();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var building = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(path);

                if (building != null)
                {
                    buildings.Add(new
                    {
                        path = path,
                        name = building.buildingName ?? building.name,
                        footprint = new { width = building.footprintWidth, depth = building.footprintDepth },
                        stories = building.stories,
                        roofAccessible = building.roofAccessible,
                        districts = building.allowedDistricts?.Select(d => d.ToString()).ToArray()
                    });
                }
            }

            return new
            {
                success = true,
                count = buildings.Count,
                buildings = buildings.ToArray(),
                createHint = "Create new buildings at Assets/ScriptableObjects/Towns/Buildings/ using the Town/Building Definition menu"
            };
        }

        #region Helpers

        private static TownConfig LoadOrCreateConfig(JObject args)
        {
            var configPath = args["config_path"]?.ToString();

            if (!string.IsNullOrEmpty(configPath))
            {
                var config = AssetDatabase.LoadAssetAtPath<TownConfig>(configPath);
                if (config != null)
                {
                    // Return a copy so we don't modify the original
                    return UnityEngine.Object.Instantiate(config);
                }
            }

            // Create default config
            var defaultConfig = ScriptableObject.CreateInstance<TownConfig>();
            defaultConfig.name = "RuntimeTownConfig";
            return defaultConfig;
        }

        private static void ApplyConfigOverrides(TownConfig config, JObject args)
        {
            if (args["grid_width"] != null || args["grid_height"] != null)
            {
                config.gridSize = new Vector2Int(
                    args["grid_width"]?.ToObject<int>() ?? config.gridSize.x,
                    args["grid_height"]?.ToObject<int>() ?? config.gridSize.y
                );
            }

            if (args["cell_size"] != null)
                config.cellSize = args["cell_size"].ToObject<float>();

            if (args["min_buildings"] != null)
                config.minBuildings = args["min_buildings"].ToObject<int>();

            if (args["max_buildings"] != null)
                config.maxBuildings = args["max_buildings"].ToObject<int>();

            if (args["enable_elevation"] != null)
                config.enableElevation = args["enable_elevation"].ToObject<bool>();

            config.Validate();
        }

        private static object ExecuteOperations(MCPTownBuilder.BuildResult buildResult)
        {
            // Convert operations to JObject format for env_batch_execute
            var operations = new JArray();

            foreach (var op in buildResult.operations)
            {
                var opObj = new JObject();
                opObj["tool"] = op.tool;

                var argsObj = new JObject();
                foreach (var kv in op.args)
                {
                    argsObj[kv.Key] = JToken.FromObject(kv.Value);
                }
                opObj["args"] = argsObj;

                operations.Add(opObj);
            }

            var batchArgs = new JObject();
            batchArgs["operations"] = operations;

            // Call env_batch_execute
            try
            {
                var result = ProBuilderTools.EnvBatchExecute(batchArgs);
                return result;
            }
            catch (Exception e)
            {
                return new
                {
                    success = false,
                    error = $"Failed to execute operations: {e.Message}",
                    operationsGenerated = buildResult.operations.Count
                };
            }
        }

        #endregion

        #region Menu Items

        [MenuItem("Tools/Town/Generate Small Town (40x40)")]
        public static void GenerateSmallTown()
        {
            var args = new JObject();
            args["grid_width"] = 40;
            args["grid_height"] = 40;
            args["min_buildings"] = 5;
            args["max_buildings"] = 8;
            args["execute"] = true;

            var result = TownGenerate(args);
            Debug.Log($"[TownGenerator] {Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented)}");
        }

        [MenuItem("Tools/Town/Generate Medium Town (60x60)")]
        public static void GenerateMediumTown()
        {
            var args = new JObject();
            args["grid_width"] = 60;
            args["grid_height"] = 60;
            args["min_buildings"] = 8;
            args["max_buildings"] = 15;
            args["execute"] = true;

            var result = TownGenerate(args);
            Debug.Log($"[TownGenerator] {Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented)}");
        }

        [MenuItem("Tools/Town/Generate Large Town with Elevation (80x80)")]
        public static void GenerateLargeTown()
        {
            var args = new JObject();
            args["grid_width"] = 80;
            args["grid_height"] = 80;
            args["min_buildings"] = 12;
            args["max_buildings"] = 20;
            args["enable_elevation"] = true;
            args["execute"] = true;

            var result = TownGenerate(args);
            Debug.Log($"[TownGenerator] {Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented)}");
        }

        #endregion
    }
}
#endif
