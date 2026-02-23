// ProBuilder environment building tools
// Requires com.unity.probuilder package - add to Packages/manifest.json:
// "com.unity.probuilder": "6.0.4"
// The PROBUILDER_ENABLED define is auto-added by ProBuilderDetector.cs when package is installed.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

#if PROBUILDER_ENABLED
using UnityEditor.ProBuilder;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;
#endif

namespace LocalMCP.Tools
{
    /// <summary>
    /// MCP tools for constraint-based environment building with ProBuilder.
    /// Semantic constraints are resolved against scene state to generate geometry.
    /// </summary>
    public static class ProBuilderTools
    {
        private const string PROBUILDER_NOT_INSTALLED = "ProBuilder is not installed. Add to Packages/manifest.json: \"com.unity.probuilder\": \"6.0.4\" and restart Unity.";

        // ==================== SCENE UNDERSTANDING (No ProBuilder required) ====================

        [MCPTool("env_get_scene_bounds", "Get scene bounds with cardinal edges for spatial reference", Category = "Environment", IsReadOnly = true)]
        public static object EnvGetSceneBounds(JObject args)
        {
            var registry = GetOrCreateRegistry();
            var bounds = registry.GetSceneBounds();

            return new
            {
                success = true,
                bounds = new
                {
                    center = Vec3(bounds.center),
                    size = Vec3(bounds.size),
                    min = Vec3(bounds.min),
                    max = Vec3(bounds.max)
                },
                edges = new
                {
                    north = Vec3(EnvironmentRegistry.GetEdgePosition(bounds, "north")),
                    south = Vec3(EnvironmentRegistry.GetEdgePosition(bounds, "south")),
                    east = Vec3(EnvironmentRegistry.GetEdgePosition(bounds, "east")),
                    west = Vec3(EnvironmentRegistry.GetEdgePosition(bounds, "west")),
                    northeast = Vec3(EnvironmentRegistry.GetEdgePosition(bounds, "ne")),
                    northwest = Vec3(EnvironmentRegistry.GetEdgePosition(bounds, "nw")),
                    southeast = Vec3(EnvironmentRegistry.GetEdgePosition(bounds, "se")),
                    southwest = Vec3(EnvironmentRegistry.GetEdgePosition(bounds, "sw"))
                },
                elementCount = registry.GetAll().Length,
                spatialReference = "north=+Z, east=+X, up=+Y"
            };
        }

        [MCPTool("env_get_elements", "Query registered environment elements by type, tags, or proximity", Category = "Environment", IsReadOnly = true)]
        [MCPParam("type", "string", "Filter by element type (wall, floor, pillar, etc.)", false)]
        [MCPParam("tags", "array", "Filter by tags (any match)", false)]
        [MCPParam("near", "object", "Filter by proximity {x, y, z, radius}", false)]
        [MCPParam("parent_id", "string", "Filter by parent element ID", false)]
        public static object EnvGetElements(JObject args)
        {
            var registry = GetOrCreateRegistry();
            var type = args["type"]?.ToString();
            var tags = args["tags"]?.ToObject<string[]>();
            var parentId = args["parent_id"]?.ToString();

            EnvironmentRegistry.EnvironmentElement[] elements;

            if (args["near"] is JObject near)
            {
                var pos = new Vector3(
                    near["x"]?.ToObject<float>() ?? 0,
                    near["y"]?.ToObject<float>() ?? 0,
                    near["z"]?.ToObject<float>() ?? 0);
                var radius = near["radius"]?.ToObject<float>() ?? 10f;
                elements = registry.QueryNear(pos, radius, type);
            }
            else
            {
                elements = registry.Query(type, tags, parentId);
            }

            return new
            {
                success = true,
                count = elements.Length,
                elements = elements.Select(e => new
                {
                    id = e.id,
                    type = e.type,
                    name = e.gameObject?.name,
                    tags = e.tags,
                    parentId = e.parentId,
                    onGrid = e.onGrid,
                    grid = e.onGrid ? new { x = e.gridPosition.x, z = e.gridPosition.z, level = e.gridPosition.y } : null,
                    bounds = new
                    {
                        center = Vec3(e.bounds.center),
                        size = Vec3(e.bounds.size)
                    },
                    position = Vec3(e.gameObject?.transform.position ?? Vector3.zero),
                    rotation = Vec3(e.gameObject?.transform.eulerAngles ?? Vector3.zero)
                }).ToArray()
            };
        }

        [MCPTool("env_describe_scene", "Get natural language description of the current environment", Category = "Environment", IsReadOnly = true)]
        public static object EnvDescribeScene(JObject args)
        {
            var registry = GetOrCreateRegistry();
            var all = registry.GetAll();
            var bounds = registry.GetSceneBounds();

            var byType = all.GroupBy(e => e.type)
                .ToDictionary(g => g.Key, g => g.Count());

            var description = new List<string>();
            description.Add($"Scene bounds: {bounds.size.x:F1}x{bounds.size.z:F1} meters (width x depth)");

            if (all.Length == 0)
            {
                description.Add("No environment elements registered.");
            }
            else
            {
                description.Add($"Total elements: {all.Length}");
                foreach (var kv in byType.OrderByDescending(kv => kv.Value))
                {
                    description.Add($"  - {kv.Value} {kv.Key}(s)");
                }
            }

            var walls = registry.Query("wall");
            if (walls.Length > 0)
            {
                var wallDirections = new List<string>();
                foreach (var wall in walls)
                {
                    var pos = wall.bounds.center;
                    var dir = GetCardinalDirection(pos, bounds.center);
                    if (!wallDirections.Contains(dir))
                        wallDirections.Add(dir);
                }
                description.Add($"Walls on: {string.Join(", ", wallDirections)} sides");
            }

            return new
            {
                success = true,
                description = string.Join("\n", description),
                summary = new
                {
                    totalElements = all.Length,
                    byType = byType,
                    boundsSize = Vec3(bounds.size)
                }
            };
        }

        // ==================== GRID SYSTEM ====================

        [MCPTool("env_grid_config", "Configure or view grid settings for snap-based building", Category = "Environment")]
        [MCPParam("cell_size", "number", "Size of each grid cell in world units (default: 4)", false)]
        [MCPParam("level_height", "number", "Height of each vertical level (default: 3)", false)]
        [MCPParam("origin_x", "number", "X coordinate of grid origin", false)]
        [MCPParam("origin_z", "number", "Z coordinate of grid origin", false)]
        public static object EnvGridConfig(JObject args)
        {
            var registry = GetOrCreateRegistry();

            // Apply settings if provided
            if (args["cell_size"] != null)
                registry.cellSize = args["cell_size"].ToObject<float>();
            if (args["level_height"] != null)
                registry.levelHeight = args["level_height"].ToObject<float>();
            if (args["origin_x"] != null)
                registry.gridOrigin = new Vector3(args["origin_x"].ToObject<float>(), registry.gridOrigin.y, registry.gridOrigin.z);
            if (args["origin_z"] != null)
                registry.gridOrigin = new Vector3(registry.gridOrigin.x, registry.gridOrigin.y, args["origin_z"].ToObject<float>());

            return new
            {
                success = true,
                grid = new
                {
                    cellSize = registry.cellSize,
                    levelHeight = registry.levelHeight,
                    origin = Vec3(registry.gridOrigin)
                },
                usage = new
                {
                    positionType = "grid",
                    example = new { type = "grid", x = 0, z = 0, level = 0 },
                    adjacentExample = new { type = "grid_adjacent", target = "env_wall_1", direction = "east" },
                    note = "Grid coords: x=east/west cells, z=north/south cells, level=vertical floors"
                }
            };
        }

        // ==================== CONSTRUCTION (ProBuilder required) ====================

        [MCPTool("env_create_structure", "Create wall/floor/pillar/platform/stairs from constraints", Category = "Environment", TimeoutMs = 60000)]
        [MCPParam("structure_type", "string", "Type: wall, floor, pillar, platform, stairs, ramp")]
        [MCPParam("position", "object", "Position constraint (see constraint schema)")]
        [MCPParam("dimensions", "object", "Size {width, height, depth} or {radius, height} for pillar")]
        [MCPParam("facing", "string", "Direction the structure faces (north/south/east/west)", false)]
        [MCPParam("material", "string", "Semantic material name (stone, wood, etc.)", false)]
        [MCPParam("tags", "array", "Tags for later reference", false)]
        [MCPParam("parent_id", "string", "Parent element ID for grouping", false)]
        [MCPParam("name", "string", "Custom name for the element", false)]
        public static object EnvCreateStructure(JObject args)
        {
#if !PROBUILDER_ENABLED
            return Error(PROBUILDER_NOT_INSTALLED);
#else
            var structureType = args["structure_type"]?.ToString()?.ToLower();
            if (string.IsNullOrEmpty(structureType))
                return Error("structure_type required");

            var positionConstraint = args["position"] as JObject;
            if (positionConstraint == null)
                return Error("position constraint required");

            var dimensions = args["dimensions"] as JObject;
            if (dimensions == null)
                return Error("dimensions required");

            var registry = GetOrCreateRegistry();
            var sceneBounds = registry.GetSceneBounds();

            var facing = args["facing"]?.ToString() ?? "north";
            var facingDir = EnvironmentRegistry.GetDirection(facing);
            var rotation = facingDir != Vector3.zero
                ? Quaternion.LookRotation(facingDir)
                : Quaternion.identity;

            // Pre-calculate element size for adjacent constraint positioning
            Vector3 elementSize = CalculateElementSize(structureType, dimensions, rotation);

            var position = ResolvePositionConstraint(positionConstraint, registry, sceneBounds, elementSize);
            if (!position.HasValue)
                return Error($"Could not resolve position constraint: {positionConstraint}");

            ProBuilderMesh mesh;
            string actualType = structureType;

            try
            {
                switch (structureType)
                {
                    case "wall":
                        {
                            var width = dimensions["width"]?.ToObject<float>() ?? 5f;
                            var height = dimensions["height"]?.ToObject<float>() ?? 3f;
                            var depth = dimensions["depth"]?.ToObject<float>() ?? 0.3f;
                            mesh = ShapeGenerator.GenerateCube(PivotLocation.Center,
                                new Vector3(width, height, depth));
                            position = new Vector3(position.Value.x, position.Value.y + height / 2f, position.Value.z);
                        }
                        break;

                    case "floor":
                        {
                            var width = dimensions["width"]?.ToObject<float>() ?? 10f;
                            var depth = dimensions["depth"]?.ToObject<float>() ?? 10f;
                            var height = dimensions["height"]?.ToObject<float>() ?? 0.2f;
                            mesh = ShapeGenerator.GenerateCube(PivotLocation.Center,
                                new Vector3(width, height, depth));
                            position = new Vector3(position.Value.x, position.Value.y + height / 2f, position.Value.z);
                        }
                        break;

                    case "pillar":
                        {
                            var radius = dimensions["radius"]?.ToObject<float>() ?? 0.5f;
                            var height = dimensions["height"]?.ToObject<float>() ?? 4f;
                            var sides = dimensions["sides"]?.ToObject<int>() ?? 8;
                            mesh = ShapeGenerator.GenerateCylinder(PivotLocation.Center,
                                sides, radius, height, 1, -1);
                            position = new Vector3(position.Value.x, position.Value.y + height / 2f, position.Value.z);
                        }
                        break;

                    case "platform":
                        {
                            var width = dimensions["width"]?.ToObject<float>() ?? 4f;
                            var height = dimensions["height"]?.ToObject<float>() ?? 0.5f;
                            var depth = dimensions["depth"]?.ToObject<float>() ?? 4f;
                            mesh = ShapeGenerator.GenerateCube(PivotLocation.Center,
                                new Vector3(width, height, depth));
                            var baseY = dimensions["elevation"]?.ToObject<float>() ?? position.Value.y;
                            position = new Vector3(position.Value.x, baseY + height / 2f, position.Value.z);
                        }
                        break;

                    case "stairs":
                        {
                            var width = dimensions["width"]?.ToObject<float>() ?? 2f;
                            var height = dimensions["height"]?.ToObject<float>() ?? 3f;
                            var depth = dimensions["depth"]?.ToObject<float>() ?? 4f;
                            var steps = Mathf.Max(3, (int)(height / 0.25f));
                            mesh = ShapeGenerator.GenerateStair(PivotLocation.Center,
                                new Vector3(width, height, depth), steps, true);
                            // Adjust Y so stairs sit on ground (base at position.y)
                            position = new Vector3(position.Value.x, position.Value.y + height / 2f, position.Value.z);
                        }
                        break;

                    case "ramp":
                        {
                            var width = dimensions["width"]?.ToObject<float>() ?? 2f;
                            var height = dimensions["height"]?.ToObject<float>() ?? 2f;
                            var depth = dimensions["depth"]?.ToObject<float>() ?? 4f;
                            mesh = ShapeGenerator.GeneratePrism(PivotLocation.Center,
                                new Vector3(width, height, depth));
                            // Adjust Y so ramp sits on ground (base at position.y)
                            position = new Vector3(position.Value.x, position.Value.y + height / 2f, position.Value.z);
                        }
                        break;

                    default:
                        return Error($"Unknown structure type: {structureType}");
                }
            }
            catch (Exception e)
            {
                return Error($"Failed to create ProBuilder mesh: {e.Message}");
            }

            mesh.transform.position = position.Value;
            mesh.transform.rotation = rotation;

            var materialName = args["material"]?.ToString();
            ApplyMaterial(mesh, materialName);

            var customName = args["name"]?.ToString();
            mesh.gameObject.name = !string.IsNullOrEmpty(customName)
                ? customName
                : $"Env_{structureType}_{registry.GetAll().Length + 1}";

            mesh.gameObject.isStatic = true;

            var tags = args["tags"]?.ToObject<string[]>();
            var parentId = args["parent_id"]?.ToString();

            // Check if this was a grid-based placement
            var posType = positionConstraint["type"]?.ToString()?.ToLower();
            bool onGrid = posType == "grid" || posType == "grid_adjacent";
            Vector3Int gridPos = Vector3Int.zero;

            if (onGrid)
            {
                if (posType == "grid")
                {
                    gridPos = new Vector3Int(
                        positionConstraint["x"]?.ToObject<int>() ?? 0,
                        positionConstraint["level"]?.ToObject<int>() ?? 0,
                        positionConstraint["z"]?.ToObject<int>() ?? 0);
                }
                else if (posType == "grid_adjacent")
                {
                    // Calculate grid position from the resolved world position
                    gridPos = registry.WorldToGrid(position.Value);
                }
            }

            var elementId = registry.Register(mesh.gameObject, actualType, tags, parentId, onGrid, gridPos);

            mesh.ToMesh();
            mesh.Refresh();

            Undo.RegisterCreatedObjectUndo(mesh.gameObject, $"Create {structureType}");

            var result = new Dictionary<string, object>
            {
                ["success"] = true,
                ["id"] = elementId,
                ["type"] = actualType,
                ["name"] = mesh.gameObject.name,
                ["position"] = Vec3(mesh.transform.position),
                ["rotation"] = Vec3(mesh.transform.eulerAngles),
                ["bounds"] = new
                {
                    center = Vec3(mesh.GetComponent<MeshRenderer>().bounds.center),
                    size = Vec3(mesh.GetComponent<MeshRenderer>().bounds.size)
                }
            };

            if (onGrid)
            {
                result["grid"] = new { x = gridPos.x, z = gridPos.z, level = gridPos.y };
            }

            return result;
#endif
        }

        /// <summary>
        /// High-level building creation that handles all corner math automatically.
        /// Creates floor, 4 walls with correct corners, and optional roof.
        /// </summary>
        [MCPTool("env_create_building", "Create a complete building (floor + 4 walls + roof) with guaranteed correct corners", Category = "Environment", TimeoutMs = 120000)]
        [MCPParam("center", "object", "Center position {x, z} or {type, ...} constraint")]
        [MCPParam("size", "object", "Building size {width, depth, height} - height is wall height")]
        [MCPParam("wall_material", "string", "Material for walls", false)]
        [MCPParam("floor_material", "string", "Material for floor", false)]
        [MCPParam("roof_material", "string", "Material for roof (omit for no roof)", false)]
        [MCPParam("roof_overhang", "number", "Roof overhang amount (default: 1)", false)]
        [MCPParam("floor_height", "number", "Floor thickness (default: 0.3)", false)]
        [MCPParam("wall_thickness", "number", "Wall thickness (default: 0.3)", false)]
        [MCPParam("door_side", "string", "Side to leave door opening: north/south/east/west (optional)", false)]
        [MCPParam("door_width", "number", "Door opening width (default: 2)", false)]
        [MCPParam("name", "string", "Base name for building elements", false)]
        [MCPParam("tags", "array", "Tags for all elements", false)]
        public static object EnvCreateBuilding(JObject args)
        {
#if !PROBUILDER_ENABLED
            return Error(PROBUILDER_NOT_INSTALLED);
#else
            var registry = GetOrCreateRegistry();

            // Parse center position
            var sceneBounds = registry.GetSceneBounds();
            Vector3 centerPos;
            var centerArg = args["center"];
            if (centerArg is JObject centerObj && centerObj["type"] != null)
            {
                var resolved = ResolvePositionConstraint(centerObj, registry, sceneBounds);
                if (!resolved.HasValue)
                    return Error("Could not resolve center position constraint");
                centerPos = resolved.Value;
            }
            else if (centerArg is JObject coordObj)
            {
                centerPos = new Vector3(
                    coordObj["x"]?.ToObject<float>() ?? 0,
                    0,
                    coordObj["z"]?.ToObject<float>() ?? 0
                );
            }
            else
            {
                return Error("center required: {x, z} or position constraint");
            }

            // Parse size
            var sizeObj = args["size"] as JObject;
            if (sizeObj == null)
                return Error("size required: {width, depth, height}");

            float width = sizeObj["width"]?.ToObject<float>() ?? 10f;
            float depth = sizeObj["depth"]?.ToObject<float>() ?? 10f;
            float wallHeight = sizeObj["height"]?.ToObject<float>() ?? 3f;

            // Parse options
            string wallMat = args["wall_material"]?.ToString() ?? "wall";
            string floorMat = args["floor_material"]?.ToString() ?? "tile";
            string roofMat = args["roof_material"]?.ToString();
            float roofOverhang = args["roof_overhang"]?.ToObject<float>() ?? 1f;
            float floorHeight = args["floor_height"]?.ToObject<float>() ?? 0.3f;
            float wallThickness = args["wall_thickness"]?.ToObject<float>() ?? 0.3f;
            string doorSide = args["door_side"]?.ToString()?.ToLower();
            float doorWidth = args["door_width"]?.ToObject<float>() ?? 2f;
            string baseName = args["name"]?.ToString() ?? "Building";
            var tags = args["tags"]?.ToObject<string[]>() ?? Array.Empty<string>();

            // Calculate exact edge positions
            float westEdge = centerPos.x - width / 2;
            float eastEdge = centerPos.x + width / 2;
            float southEdge = centerPos.z - depth / 2;
            float northEdge = centerPos.z + depth / 2;
            float floorTop = floorHeight;
            float roofY = floorTop + wallHeight;

            var createdElements = new List<object>();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName($"Create Building: {baseName}");

            try
            {
                // 1. Create Floor
                var floorMesh = ShapeGenerator.GenerateCube(PivotLocation.Center,
                    new Vector3(width, floorHeight, depth));
                floorMesh.name = $"{baseName}_Floor";
                floorMesh.transform.position = new Vector3(centerPos.x, floorHeight / 2, centerPos.z);
                ApplyMaterial(floorMesh, floorMat);
                Undo.RegisterCreatedObjectUndo(floorMesh.gameObject, "Create Floor");
                var floorId = registry.Register(floorMesh.gameObject, "floor", tags);
                createdElements.Add(new { id = floorId, type = "floor", name = floorMesh.name });

                // 2. Create Walls - CRITICAL: perpendicular walls span full depth/width
                // South wall (at south edge, spans full width)
                if (doorSide != "south")
                {
                    var southWall = CreateWallMesh(width, wallHeight, wallThickness, $"{baseName}_Wall_S");
                    southWall.transform.position = new Vector3(centerPos.x, floorTop + wallHeight / 2, southEdge);
                    ApplyMaterial(southWall, wallMat);
                    Undo.RegisterCreatedObjectUndo(southWall.gameObject, "Create South Wall");
                    var southId = registry.Register(southWall.gameObject, "wall", tags);
                    createdElements.Add(new { id = southId, type = "wall", name = southWall.name, side = "south" });
                }
                else
                {
                    // Create wall with door opening (two segments)
                    float segmentWidth = (width - doorWidth) / 2;
                    if (segmentWidth > 0.5f)
                    {
                        var leftWall = CreateWallMesh(segmentWidth, wallHeight, wallThickness, $"{baseName}_Wall_S_L");
                        leftWall.transform.position = new Vector3(westEdge + segmentWidth / 2, floorTop + wallHeight / 2, southEdge);
                        ApplyMaterial(leftWall, wallMat);
                        Undo.RegisterCreatedObjectUndo(leftWall.gameObject, "Create South Wall Left");
                        var leftId = registry.Register(leftWall.gameObject, "wall", tags);
                        createdElements.Add(new { id = leftId, type = "wall", name = leftWall.name });

                        var rightWall = CreateWallMesh(segmentWidth, wallHeight, wallThickness, $"{baseName}_Wall_S_R");
                        rightWall.transform.position = new Vector3(eastEdge - segmentWidth / 2, floorTop + wallHeight / 2, southEdge);
                        ApplyMaterial(rightWall, wallMat);
                        Undo.RegisterCreatedObjectUndo(rightWall.gameObject, "Create South Wall Right");
                        var rightId = registry.Register(rightWall.gameObject, "wall", tags);
                        createdElements.Add(new { id = rightId, type = "wall", name = rightWall.name });
                    }
                }

                // North wall (at north edge, spans full width)
                if (doorSide != "north")
                {
                    var northWall = CreateWallMesh(width, wallHeight, wallThickness, $"{baseName}_Wall_N");
                    northWall.transform.position = new Vector3(centerPos.x, floorTop + wallHeight / 2, northEdge);
                    ApplyMaterial(northWall, wallMat);
                    Undo.RegisterCreatedObjectUndo(northWall.gameObject, "Create North Wall");
                    var northId = registry.Register(northWall.gameObject, "wall", tags);
                    createdElements.Add(new { id = northId, type = "wall", name = northWall.name, side = "north" });
                }
                else
                {
                    float segmentWidth = (width - doorWidth) / 2;
                    if (segmentWidth > 0.5f)
                    {
                        var leftWall = CreateWallMesh(segmentWidth, wallHeight, wallThickness, $"{baseName}_Wall_N_L");
                        leftWall.transform.position = new Vector3(westEdge + segmentWidth / 2, floorTop + wallHeight / 2, northEdge);
                        ApplyMaterial(leftWall, wallMat);
                        Undo.RegisterCreatedObjectUndo(leftWall.gameObject, "Create North Wall Left");
                        var leftId = registry.Register(leftWall.gameObject, "wall", tags);
                        createdElements.Add(new { id = leftId, type = "wall", name = leftWall.name });

                        var rightWall = CreateWallMesh(segmentWidth, wallHeight, wallThickness, $"{baseName}_Wall_N_R");
                        rightWall.transform.position = new Vector3(eastEdge - segmentWidth / 2, floorTop + wallHeight / 2, northEdge);
                        ApplyMaterial(rightWall, wallMat);
                        Undo.RegisterCreatedObjectUndo(rightWall.gameObject, "Create North Wall Right");
                        var rightId = registry.Register(rightWall.gameObject, "wall", tags);
                        createdElements.Add(new { id = rightId, type = "wall", name = rightWall.name });
                    }
                }

                // West wall (at west edge, spans full DEPTH - this is the key insight!)
                if (doorSide != "west")
                {
                    var westWall = CreateWallMesh(depth, wallHeight, wallThickness, $"{baseName}_Wall_W");
                    westWall.transform.position = new Vector3(westEdge, floorTop + wallHeight / 2, centerPos.z);
                    westWall.transform.rotation = Quaternion.Euler(0, 90, 0);
                    ApplyMaterial(westWall, wallMat);
                    Undo.RegisterCreatedObjectUndo(westWall.gameObject, "Create West Wall");
                    var westId = registry.Register(westWall.gameObject, "wall", tags);
                    createdElements.Add(new { id = westId, type = "wall", name = westWall.name, side = "west" });
                }
                else
                {
                    float segmentDepth = (depth - doorWidth) / 2;
                    if (segmentDepth > 0.5f)
                    {
                        var southWall = CreateWallMesh(segmentDepth, wallHeight, wallThickness, $"{baseName}_Wall_W_S");
                        southWall.transform.position = new Vector3(westEdge, floorTop + wallHeight / 2, southEdge + segmentDepth / 2);
                        southWall.transform.rotation = Quaternion.Euler(0, 90, 0);
                        ApplyMaterial(southWall, wallMat);
                        Undo.RegisterCreatedObjectUndo(southWall.gameObject, "Create West Wall South");
                        var southId = registry.Register(southWall.gameObject, "wall", tags);
                        createdElements.Add(new { id = southId, type = "wall", name = southWall.name });

                        var northWall = CreateWallMesh(segmentDepth, wallHeight, wallThickness, $"{baseName}_Wall_W_N");
                        northWall.transform.position = new Vector3(westEdge, floorTop + wallHeight / 2, northEdge - segmentDepth / 2);
                        northWall.transform.rotation = Quaternion.Euler(0, 90, 0);
                        ApplyMaterial(northWall, wallMat);
                        Undo.RegisterCreatedObjectUndo(northWall.gameObject, "Create West Wall North");
                        var northId = registry.Register(northWall.gameObject, "wall", tags);
                        createdElements.Add(new { id = northId, type = "wall", name = northWall.name });
                    }
                }

                // East wall (at east edge, spans full DEPTH)
                if (doorSide != "east")
                {
                    var eastWall = CreateWallMesh(depth, wallHeight, wallThickness, $"{baseName}_Wall_E");
                    eastWall.transform.position = new Vector3(eastEdge, floorTop + wallHeight / 2, centerPos.z);
                    eastWall.transform.rotation = Quaternion.Euler(0, 270, 0);
                    ApplyMaterial(eastWall, wallMat);
                    Undo.RegisterCreatedObjectUndo(eastWall.gameObject, "Create East Wall");
                    var eastId = registry.Register(eastWall.gameObject, "wall", tags);
                    createdElements.Add(new { id = eastId, type = "wall", name = eastWall.name, side = "east" });
                }
                else
                {
                    float segmentDepth = (depth - doorWidth) / 2;
                    if (segmentDepth > 0.5f)
                    {
                        var southWall = CreateWallMesh(segmentDepth, wallHeight, wallThickness, $"{baseName}_Wall_E_S");
                        southWall.transform.position = new Vector3(eastEdge, floorTop + wallHeight / 2, southEdge + segmentDepth / 2);
                        southWall.transform.rotation = Quaternion.Euler(0, 270, 0);
                        ApplyMaterial(southWall, wallMat);
                        Undo.RegisterCreatedObjectUndo(southWall.gameObject, "Create East Wall South");
                        var southId = registry.Register(southWall.gameObject, "wall", tags);
                        createdElements.Add(new { id = southId, type = "wall", name = southWall.name });

                        var northWall = CreateWallMesh(segmentDepth, wallHeight, wallThickness, $"{baseName}_Wall_E_N");
                        northWall.transform.position = new Vector3(eastEdge, floorTop + wallHeight / 2, northEdge - segmentDepth / 2);
                        northWall.transform.rotation = Quaternion.Euler(0, 270, 0);
                        ApplyMaterial(northWall, wallMat);
                        Undo.RegisterCreatedObjectUndo(northWall.gameObject, "Create East Wall North");
                        var northId = registry.Register(northWall.gameObject, "wall", tags);
                        createdElements.Add(new { id = northId, type = "wall", name = northWall.name });
                    }
                }

                // 3. Create Roof (if material specified)
                if (!string.IsNullOrEmpty(roofMat))
                {
                    float roofWidth = width + roofOverhang * 2;
                    float roofDepth = depth + roofOverhang * 2;
                    var roofMesh = ShapeGenerator.GenerateCube(PivotLocation.Center,
                        new Vector3(roofWidth, 0.4f, roofDepth));
                    roofMesh.name = $"{baseName}_Roof";
                    roofMesh.transform.position = new Vector3(centerPos.x, roofY + 0.2f, centerPos.z);
                    ApplyMaterial(roofMesh, roofMat);
                    Undo.RegisterCreatedObjectUndo(roofMesh.gameObject, "Create Roof");
                    var roofId = registry.Register(roofMesh.gameObject, "roof", tags);
                    createdElements.Add(new { id = roofId, type = "roof", name = roofMesh.name });
                }

                return new
                {
                    success = true,
                    message = $"Created building '{baseName}' with {createdElements.Count} elements",
                    building_name = baseName,
                    center = Vec3(centerPos),
                    bounds = new
                    {
                        min = new { x = westEdge, y = 0, z = southEdge },
                        max = new { x = eastEdge, y = roofY + 0.4f, z = northEdge }
                    },
                    elements = createdElements
                };
            }
            catch (Exception ex)
            {
                return Error($"Failed to create building: {ex.Message}");
            }
#endif
        }

#if PROBUILDER_ENABLED
        private static ProBuilderMesh CreateWallMesh(float width, float height, float depth, string name)
        {
            var mesh = ShapeGenerator.GenerateCube(PivotLocation.Center, new Vector3(width, height, depth));
            mesh.name = name;
            return mesh;
        }
#endif

        [MCPTool("env_create_opening", "Cut door/window/archway into existing wall", Category = "Environment", TimeoutMs = 60000)]
        [MCPParam("wall_id", "string", "ID of the wall element to modify")]
        [MCPParam("opening_type", "string", "Type: door, window, archway, hole")]
        [MCPParam("position", "object", "Position on wall: {horizontal: 'left'|'center'|'right', vertical: 'bottom'|'center'|'top'}")]
        [MCPParam("dimensions", "object", "Size {width, height}", false)]
        public static object EnvCreateOpening(JObject args)
        {
#if !PROBUILDER_ENABLED
            return Error(PROBUILDER_NOT_INSTALLED);
#else
            var wallId = args["wall_id"]?.ToString();
            if (string.IsNullOrEmpty(wallId))
                return Error("wall_id required");

            var registry = GetOrCreateRegistry();
            var wall = registry.GetById(wallId);
            if (wall == null || !wall.IsValid)
                return Error($"Wall not found: {wallId}");

            var openingType = args["opening_type"]?.ToString()?.ToLower() ?? "door";
            var positionData = args["position"] as JObject;
            var dimensions = args["dimensions"] as JObject;

            var wallBounds = wall.bounds;
            var pbMesh = wall.gameObject.GetComponent<ProBuilderMesh>();

            if (pbMesh == null)
                return Error("Wall does not have ProBuilder mesh component");

            float openWidth, openHeight;
            switch (openingType)
            {
                case "door":
                    openWidth = dimensions?["width"]?.ToObject<float>() ?? 1.2f;
                    openHeight = dimensions?["height"]?.ToObject<float>() ?? 2.4f;
                    break;
                case "window":
                    openWidth = dimensions?["width"]?.ToObject<float>() ?? 1.0f;
                    openHeight = dimensions?["height"]?.ToObject<float>() ?? 1.0f;
                    break;
                case "archway":
                    openWidth = dimensions?["width"]?.ToObject<float>() ?? 2.0f;
                    openHeight = dimensions?["height"]?.ToObject<float>() ?? 3.0f;
                    break;
                default:
                    openWidth = dimensions?["width"]?.ToObject<float>() ?? 1.0f;
                    openHeight = dimensions?["height"]?.ToObject<float>() ?? 1.0f;
                    break;
            }

            var horizontal = positionData?["horizontal"]?.ToString() ?? "center";
            var vertical = positionData?["vertical"]?.ToString() ?? "bottom";

            float hOffset = horizontal switch
            {
                "left" => -wallBounds.extents.x + openWidth / 2 + 0.5f,
                "right" => wallBounds.extents.x - openWidth / 2 - 0.5f,
                _ => 0
            };

            float vOffset = vertical switch
            {
                "bottom" => -wallBounds.extents.y + openHeight / 2 + 0.1f,
                "top" => wallBounds.extents.y - openHeight / 2 - 0.1f,
                _ => 0
            };

            var wallPos = wall.gameObject.transform.position;
            var wallRot = wall.gameObject.transform.rotation;
            var wallScale = wall.gameObject.transform.localScale;

            var localBounds = pbMesh.GetComponent<MeshFilter>().sharedMesh.bounds;
            var wallWidth = localBounds.size.x * wallScale.x;
            var wallHeight = localBounds.size.y * wallScale.y;
            var wallDepth = localBounds.size.z * wallScale.z;

            var renderer = wall.gameObject.GetComponent<MeshRenderer>();
            var material = renderer?.sharedMaterial;

            Undo.DestroyObjectImmediate(wall.gameObject);
            registry.Unregister(wallId);

            var createdSegments = new List<string>();

            if (horizontal != "left")
            {
                var leftWidth = (wallWidth / 2) + hOffset - (openWidth / 2);
                if (leftWidth > 0.1f)
                {
                    var leftMesh = ShapeGenerator.GenerateCube(PivotLocation.Center,
                        new Vector3(leftWidth, wallHeight, wallDepth));
                    leftMesh.transform.rotation = wallRot;
                    leftMesh.transform.position = wallPos + wallRot * new Vector3(
                        -wallWidth / 2 + leftWidth / 2, 0, 0);
                    ApplyMaterialToMesh(leftMesh, material);
                    leftMesh.gameObject.name = $"{wall.type}_left";
                    leftMesh.gameObject.isStatic = true;
                    leftMesh.ToMesh();
                    leftMesh.Refresh();
                    Undo.RegisterCreatedObjectUndo(leftMesh.gameObject, "Create wall segment");
                    createdSegments.Add(registry.Register(leftMesh.gameObject, "wall", new[] { "segment" }));
                }
            }

            if (horizontal != "right")
            {
                var rightWidth = (wallWidth / 2) - hOffset - (openWidth / 2);
                if (rightWidth > 0.1f)
                {
                    var rightMesh = ShapeGenerator.GenerateCube(PivotLocation.Center,
                        new Vector3(rightWidth, wallHeight, wallDepth));
                    rightMesh.transform.rotation = wallRot;
                    rightMesh.transform.position = wallPos + wallRot * new Vector3(
                        wallWidth / 2 - rightWidth / 2, 0, 0);
                    ApplyMaterialToMesh(rightMesh, material);
                    rightMesh.gameObject.name = $"{wall.type}_right";
                    rightMesh.gameObject.isStatic = true;
                    rightMesh.ToMesh();
                    rightMesh.Refresh();
                    Undo.RegisterCreatedObjectUndo(rightMesh.gameObject, "Create wall segment");
                    createdSegments.Add(registry.Register(rightMesh.gameObject, "wall", new[] { "segment" }));
                }
            }

            var topHeight = (wallHeight / 2) - vOffset - (openHeight / 2);
            if (topHeight > 0.1f)
            {
                var topMesh = ShapeGenerator.GenerateCube(PivotLocation.Center,
                    new Vector3(openWidth, topHeight, wallDepth));
                topMesh.transform.rotation = wallRot;
                topMesh.transform.position = wallPos + wallRot * new Vector3(
                    hOffset, wallHeight / 2 - topHeight / 2, 0);
                ApplyMaterialToMesh(topMesh, material);
                topMesh.gameObject.name = $"{wall.type}_top";
                topMesh.gameObject.isStatic = true;
                topMesh.ToMesh();
                topMesh.Refresh();
                Undo.RegisterCreatedObjectUndo(topMesh.gameObject, "Create wall segment");
                createdSegments.Add(registry.Register(topMesh.gameObject, "wall", new[] { "segment", "lintel" }));
            }

            if (openingType == "window" && vertical != "bottom")
            {
                var bottomHeight = (wallHeight / 2) + vOffset - (openHeight / 2);
                if (bottomHeight > 0.1f)
                {
                    var bottomMesh = ShapeGenerator.GenerateCube(PivotLocation.Center,
                        new Vector3(openWidth, bottomHeight, wallDepth));
                    bottomMesh.transform.rotation = wallRot;
                    bottomMesh.transform.position = wallPos + wallRot * new Vector3(
                        hOffset, -wallHeight / 2 + bottomHeight / 2, 0);
                    ApplyMaterialToMesh(bottomMesh, material);
                    bottomMesh.gameObject.name = $"{wall.type}_bottom";
                    bottomMesh.gameObject.isStatic = true;
                    bottomMesh.ToMesh();
                    bottomMesh.Refresh();
                    Undo.RegisterCreatedObjectUndo(bottomMesh.gameObject, "Create wall segment");
                    createdSegments.Add(registry.Register(bottomMesh.gameObject, "wall", new[] { "segment", "sill" }));
                }
            }

            return new
            {
                success = true,
                openingType = openingType,
                openingSize = new { width = openWidth, height = openHeight },
                wallSegmentsCreated = createdSegments.Count,
                segmentIds = createdSegments.ToArray(),
                message = $"Created {openingType} opening in wall, split into {createdSegments.Count} segments"
            };
#endif
        }

        [MCPTool("env_create_terrain", "Create ground/slope/hill geometry", Category = "Environment", TimeoutMs = 60000)]
        [MCPParam("terrain_type", "string", "Type: ground, slope, hill, pit")]
        [MCPParam("position", "object", "Position constraint")]
        [MCPParam("dimensions", "object", "Size {width, depth, height}")]
        [MCPParam("material", "string", "Semantic material name", false)]
        [MCPParam("tags", "array", "Tags for reference", false)]
        public static object EnvCreateTerrain(JObject args)
        {
#if !PROBUILDER_ENABLED
            return Error(PROBUILDER_NOT_INSTALLED);
#else
            var terrainType = args["terrain_type"]?.ToString()?.ToLower() ?? "ground";
            var positionConstraint = args["position"] as JObject;
            var dimensions = args["dimensions"] as JObject;

            if (positionConstraint == null)
                return Error("position constraint required");
            if (dimensions == null)
                return Error("dimensions required");

            var registry = GetOrCreateRegistry();
            var sceneBounds = registry.GetSceneBounds();
            var position = ResolvePositionConstraint(positionConstraint, registry, sceneBounds);

            if (!position.HasValue)
                return Error("Could not resolve position constraint");

            var width = dimensions["width"]?.ToObject<float>() ?? 10f;
            var depth = dimensions["depth"]?.ToObject<float>() ?? 10f;
            var height = dimensions["height"]?.ToObject<float>() ?? 0.5f;

            ProBuilderMesh mesh;

            switch (terrainType)
            {
                case "ground":
                    mesh = ShapeGenerator.GeneratePlane(PivotLocation.Center,
                        width, depth, 1, 1, Axis.Up);
                    mesh.transform.position = position.Value;
                    break;

                case "slope":
                    mesh = ShapeGenerator.GeneratePrism(PivotLocation.Center,
                        new Vector3(width, height, depth));
                    mesh.transform.position = position.Value;
                    break;

                case "hill":
                    mesh = ShapeGenerator.GenerateCone(PivotLocation.Center,
                        width / 2f, height, 12);
                    mesh.transform.position = position.Value;
                    break;

                case "pit":
                    mesh = ShapeGenerator.GenerateCone(PivotLocation.Center,
                        width / 2f, -height, 12);
                    mesh.transform.position = position.Value;
                    break;

                default:
                    return Error($"Unknown terrain type: {terrainType}");
            }

            var materialName = args["material"]?.ToString();
            ApplyMaterial(mesh, materialName);

            mesh.gameObject.name = $"Env_{terrainType}_{registry.GetAll().Length + 1}";
            mesh.gameObject.isStatic = true;

            var tags = args["tags"]?.ToObject<string[]>();
            var elementId = registry.Register(mesh.gameObject, terrainType, tags);

            mesh.ToMesh();
            mesh.Refresh();

            Undo.RegisterCreatedObjectUndo(mesh.gameObject, $"Create {terrainType}");

            return new
            {
                success = true,
                id = elementId,
                type = terrainType,
                name = mesh.gameObject.name,
                position = Vec3(mesh.transform.position),
                bounds = new
                {
                    center = Vec3(mesh.GetComponent<MeshRenderer>().bounds.center),
                    size = Vec3(mesh.GetComponent<MeshRenderer>().bounds.size)
                }
            };
#endif
        }

        // ==================== MODIFICATION (No ProBuilder required) ====================

        [MCPTool("env_transform_element", "Move/rotate/scale element by constraint or absolute", Category = "Environment")]
        [MCPParam("id", "string", "Element ID to transform")]
        [MCPParam("position", "object", "New position constraint or absolute {x, y, z}", false)]
        [MCPParam("rotation", "object", "Rotation {x, y, z} or {facing: 'north'|...}", false)]
        [MCPParam("scale", "object", "Scale {x, y, z} or uniform float", false)]
        public static object EnvTransformElement(JObject args)
        {
            var id = args["id"]?.ToString();
            if (string.IsNullOrEmpty(id))
                return Error("id required");

            var registry = GetOrCreateRegistry();
            var element = registry.GetById(id);
            if (element == null || !element.IsValid)
                return Error($"Element not found: {id}");

            var go = element.gameObject;
            Undo.RecordObject(go.transform, "Transform environment element");

            if (args["position"] is JObject posConstraint)
            {
                var sceneBounds = registry.GetSceneBounds();
                var pos = ResolvePositionConstraint(posConstraint, registry, sceneBounds);
                if (pos.HasValue)
                    go.transform.position = pos.Value;
            }

            if (args["rotation"] is JObject rot)
            {
                if (rot["facing"] != null)
                {
                    var dir = EnvironmentRegistry.GetDirection(rot["facing"].ToString());
                    if (dir != Vector3.zero)
                        go.transform.rotation = Quaternion.LookRotation(dir);
                }
                else
                {
                    go.transform.eulerAngles = new Vector3(
                        rot["x"]?.ToObject<float>() ?? go.transform.eulerAngles.x,
                        rot["y"]?.ToObject<float>() ?? go.transform.eulerAngles.y,
                        rot["z"]?.ToObject<float>() ?? go.transform.eulerAngles.z);
                }
            }

            if (args["scale"] != null)
            {
                if (args["scale"].Type == JTokenType.Object)
                {
                    var s = args["scale"] as JObject;
                    go.transform.localScale = new Vector3(
                        s["x"]?.ToObject<float>() ?? go.transform.localScale.x,
                        s["y"]?.ToObject<float>() ?? go.transform.localScale.y,
                        s["z"]?.ToObject<float>() ?? go.transform.localScale.z);
                }
                else
                {
                    var uniform = args["scale"].ToObject<float>();
                    go.transform.localScale = Vector3.one * uniform;
                }
            }

            registry.RefreshBounds(id);

            return new
            {
                success = true,
                id = id,
                position = Vec3(go.transform.position),
                rotation = Vec3(go.transform.eulerAngles),
                scale = Vec3(go.transform.localScale)
            };
        }

        [MCPTool("env_set_material", "Apply material to element or specific faces", Category = "Environment")]
        [MCPParam("id", "string", "Element ID")]
        [MCPParam("material", "string", "Semantic material name")]
        [MCPParam("faces", "string", "Face selection: all, top, bottom, sides (default: all)", false)]
        public static object EnvSetMaterial(JObject args)
        {
            var id = args["id"]?.ToString();
            if (string.IsNullOrEmpty(id))
                return Error("id required");

            var materialName = args["material"]?.ToString();
            if (string.IsNullOrEmpty(materialName))
                return Error("material required");

            var registry = GetOrCreateRegistry();
            var element = registry.GetById(id);
            if (element == null || !element.IsValid)
                return Error($"Element not found: {id}");

            Material mat = GetMaterialByName(materialName);

            if (mat == null)
                return Error($"Material not found: {materialName}");

            var renderer = element.gameObject.GetComponent<MeshRenderer>();
            if (renderer == null)
                return Error("Element has no MeshRenderer");

            Undo.RecordObject(renderer, "Set material");
            renderer.sharedMaterial = mat;

            return new
            {
                success = true,
                id = id,
                material = materialName,
                faces = args["faces"]?.ToString() ?? "all"
            };
        }

        [MCPTool("env_delete_element", "Remove element by ID", Category = "Environment")]
        [MCPParam("id", "string", "Element ID to delete")]
        public static object EnvDeleteElement(JObject args)
        {
            var id = args["id"]?.ToString();
            if (string.IsNullOrEmpty(id))
                return Error("id required");

            var registry = GetOrCreateRegistry();
            var element = registry.GetById(id);
            if (element == null)
                return Error($"Element not found: {id}");

            if (element.IsValid)
            {
                Undo.DestroyObjectImmediate(element.gameObject);
            }

            registry.Unregister(id);

            return new
            {
                success = true,
                id = id,
                message = $"Deleted element {id}"
            };
        }

        // ==================== UTILITY ====================

        [MCPTool("env_batch_execute", "Execute multiple operations atomically (single undo group)", Category = "Environment", TimeoutMs = 120000)]
        [MCPParam("operations", "array", "Array of operation objects with {tool, args}")]
        public static object EnvBatchExecute(JObject args)
        {
            var operations = args["operations"] as JArray;
            if (operations == null || operations.Count == 0)
                return Error("operations array required");

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Batch Environment Operations");

            var results = new List<object>();
            var errors = new List<string>();

            foreach (var op in operations)
            {
                var opObj = op as JObject;
                if (opObj == null) continue;

                var tool = opObj["tool"]?.ToString();
                var toolArgs = opObj["args"] as JObject ?? new JObject();

                try
                {
                    object result = tool switch
                    {
                        "env_create_structure" => EnvCreateStructure(toolArgs),
                        "env_create_opening" => EnvCreateOpening(toolArgs),
                        "env_create_terrain" => EnvCreateTerrain(toolArgs),
                        "env_transform_element" => EnvTransformElement(toolArgs),
                        "env_set_material" => EnvSetMaterial(toolArgs),
                        "env_delete_element" => EnvDeleteElement(toolArgs),
                        _ => new { success = false, message = $"Unknown tool: {tool}" }
                    };
                    results.Add(new { tool, result });
                }
                catch (Exception e)
                {
                    errors.Add($"{tool}: {e.Message}");
                    results.Add(new { tool, error = e.Message });
                }
            }

            Undo.CollapseUndoOperations(undoGroup);

            return new
            {
                success = errors.Count == 0,
                operationsExecuted = results.Count,
                results = results.ToArray(),
                errors = errors.Count > 0 ? errors.ToArray() : null,
                undoGroupId = undoGroup
            };
        }

        [MCPTool("env_undo_last", "Undo last N environment operations", Category = "Environment")]
        [MCPParam("count", "integer", "Number of operations to undo (default: 1)", false)]
        public static object EnvUndoLast(JObject args)
        {
            var count = args["count"]?.ToObject<int>() ?? 1;

            for (int i = 0; i < count; i++)
            {
                Undo.PerformUndo();
            }

            return new
            {
                success = true,
                undoneCount = count,
                message = $"Undid {count} operation(s)"
            };
        }

        [MCPTool("env_list_materials", "List available semantic materials", Category = "Environment", IsReadOnly = true)]
        public static object EnvListMaterials(JObject args)
        {
            var library = EnvironmentMaterialLibrary.Instance;

            if (library != null)
            {
                return new
                {
                    success = true,
                    libraryFound = true,
                    materials = library.GetMaterialInfo()
                };
            }

            return new
            {
                success = true,
                libraryFound = false,
                message = "Using built-in material mappings from project assets.",
                materials = new object[]
                {
                    new { category = "Terrain", materials = new object[]
                    {
                        new { name = "grass", description = "Primary grass ground" },
                        new { name = "grass_wild", description = "Taller wild grass" },
                        new { name = "grass_tall", description = "Dense tall grass" },
                        new { name = "moss", description = "Moss-covered ground" },
                        new { name = "ground", description = "Basic ground/earth" },
                        new { name = "dirt", aliases = "dirt_forest", description = "Forest dirt path" },
                    }},
                    new { category = "Rocks & Cliffs", materials = new object[]
                    {
                        new { name = "cliff", aliases = "cliff_rock", description = "Cliff face texture" },
                        new { name = "stone", aliases = "stone_ground", description = "Natural stone surface" },
                        new { name = "stone_medium", description = "Medium stone texture" },
                        new { name = "rock", aliases = "rock_medium", description = "Rock surface" },
                        new { name = "boulder", description = "Large boulder texture" },
                        new { name = "mossy_stone", description = "Stone with moss" },
                        new { name = "ruined_stone", description = "Weathered ancient stone" },
                    }},
                    new { category = "Cave Materials", materials = new object[]
                    {
                        new { name = "cave_wall", description = "Cave wall surface" },
                        new { name = "cave_floor", description = "Cave floor" },
                        new { name = "cave_ceiling", description = "Cave ceiling" },
                        new { name = "cave_pit", description = "Cave pit floor" },
                        new { name = "cave_cliff", description = "Cave cliff face" },
                        new { name = "crystal", description = "Crystal formations" },
                        new { name = "stalactite", description = "Cave stalactites" },
                        new { name = "mushroom", description = "Cave mushrooms" },
                    }},
                    new { category = "Architecture (Stone)", materials = new object[]
                    {
                        new { name = "wall", aliases = "wall_stone, dungeon_wall", description = "Stone wall" },
                        new { name = "tile", aliases = "floor_tile, floor_stone", description = "Stone floor tiles" },
                        new { name = "pillar", aliases = "pillar_stone", description = "Stone pillar" },
                        new { name = "arch", description = "Archway stone" },
                        new { name = "step", aliases = "stairs, stairs_stone", description = "Stone stairs" },
                        new { name = "handrail", description = "Metal handrail" },
                        new { name = "torch", description = "Wall torch mount" },
                        new { name = "roof", description = "Terracotta-style roof" },
                    }},
                    new { category = "Wood & Bark", materials = new object[]
                    {
                        new { name = "wood", aliases = "wall_wood", description = "Wood wall surface" },
                        new { name = "floor_wood", description = "Wood floor planks" },
                        new { name = "bark", aliases = "bark2", description = "Tree bark texture" },
                        new { name = "fir_bark", description = "Fir tree bark" },
                        new { name = "branch", description = "Tree branch" },
                        new { name = "fir_branch", description = "Fir tree branch" },
                    }},
                    new { category = "Bridges", materials = new object[]
                    {
                        new { name = "bridge_stone", description = "Stone bridge" },
                        new { name = "bridge_rope", description = "Rope bridge" },
                        new { name = "bridge_cave", description = "Cave/natural bridge" },
                    }},
                    new { category = "Paths", materials = new object[]
                    {
                        new { name = "path_stone", description = "Formal stone pathway (dungeon tile)" },
                        new { name = "path_dirt", description = "Forest dirt pathway" },
                        new { name = "path_grass", description = "Grass pathway" },
                        new { name = "cobblestone", aliases = "cobble", description = "Rustic cobblestone for village paths" },
                        new { name = "packed_dirt", aliases = "worn_path, gravel", description = "Compacted earth/gravel path" },
                    }},
                    new { category = "Water", materials = new object[]
                    {
                        new { name = "water", aliases = "ocean", description = "Ocean/water surface" },
                        new { name = "lake", description = "Lake water surface" },
                    }},
                    new { category = "Vegetation", materials = new object[]
                    {
                        new { name = "plant", description = "Generic plant" },
                        new { name = "bush", aliases = "bush2, bush3", description = "Bush foliage" },
                        new { name = "flower", aliases = "flower_meadow", description = "Flowers" },
                        new { name = "cattail", description = "Cattail plants" },
                        new { name = "reeds", description = "Water reeds" },
                        new { name = "lilypad", description = "Lily pads" },
                        new { name = "waterlily", description = "Water lily flowers" },
                    }},
                    new { category = "Tree Colors", materials = new object[]
                    {
                        new { name = "leaves_green", description = "Green tree leaves" },
                        new { name = "leaves_red", description = "Red tree leaves" },
                        new { name = "leaves_blue", description = "Blue tree leaves" },
                        new { name = "leaves_purple", description = "Purple tree leaves" },
                        new { name = "willow_green", description = "Green willow" },
                        new { name = "willow_purple", description = "Purple willow" },
                        new { name = "willow_pink", description = "Pink willow" },
                        new { name = "blossoms", description = "Tree blossoms" },
                    }},
                    new { category = "Prototyping (Gridbox)", materials = new object[]
                    {
                        new { name = "grey", description = "Neutral grey prototype" },
                        new { name = "white", description = "White prototype" },
                        new { name = "brown", description = "Brown prototype" },
                        new { name = "green", description = "Green prototype" },
                        new { name = "blue", description = "Blue prototype" },
                        new { name = "red", description = "Red prototype" },
                        new { name = "orange", description = "Orange prototype" },
                        new { name = "yellow", description = "Yellow prototype" },
                    }}
                }
            };
        }

        [MCPTool("env_save_prefab", "Save current environment as a prefab", Category = "Environment")]
        [MCPParam("name", "string", "Prefab name")]
        [MCPParam("path", "string", "Save path (default: Assets/Prefabs/Environments)", false)]
        [MCPParam("include_tags", "array", "Only include elements with these tags (optional)", false)]
        public static object EnvSavePrefab(JObject args)
        {
            var name = args["name"]?.ToString();
            if (string.IsNullOrEmpty(name))
                return Error("name required");

            var path = args["path"]?.ToString() ?? "Assets/Prefabs/Environments";
            var includeTags = args["include_tags"]?.ToObject<string[]>();

            if (!AssetDatabase.IsValidFolder(path))
            {
                var parts = path.Split('/');
                var currentPath = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    var newPath = $"{currentPath}/{parts[i]}";
                    if (!AssetDatabase.IsValidFolder(newPath))
                    {
                        AssetDatabase.CreateFolder(currentPath, parts[i]);
                    }
                    currentPath = newPath;
                }
            }

            var registry = GetOrCreateRegistry();
            var elements = includeTags != null && includeTags.Length > 0
                ? registry.Query(tags: includeTags)
                : registry.GetAll();

            if (elements.Length == 0)
                return Error("No elements to save");

            var root = new GameObject(name);

            foreach (var element in elements)
            {
                if (element.IsValid)
                {
                    element.gameObject.transform.SetParent(root.transform, true);
                }
            }

            var prefabPath = $"{path}/{name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

            foreach (var element in elements)
            {
                if (element.IsValid)
                {
                    element.gameObject.transform.SetParent(null, true);
                }
            }

            UnityEngine.Object.DestroyImmediate(root);

            return new
            {
                success = prefab != null,
                prefabPath = prefabPath,
                elementCount = elements.Length,
                message = prefab != null
                    ? $"Saved prefab with {elements.Length} elements to {prefabPath}"
                    : "Failed to save prefab"
            };
        }

        // ==================== HELPERS ====================

        private static EnvironmentRegistry GetOrCreateRegistry()
        {
            // In editor mode, Instance might be null even if a registry exists
            // because Awake() doesn't run reliably. Use FindObjectOfType as fallback.
            if (EnvironmentRegistry.Instance != null)
                return EnvironmentRegistry.Instance;

            // Look for existing registry in scene
            var existing = UnityEngine.Object.FindObjectOfType<EnvironmentRegistry>();
            if (existing != null)
            {
                // Force initialize the static Instance for edit mode
                EnvironmentRegistry.InitializeInstance(existing);
                return existing;
            }

            // Create new registry
            var go = new GameObject("EnvironmentRegistry");
            var registry = go.AddComponent<EnvironmentRegistry>();
            EnvironmentRegistry.InitializeInstance(registry);
            return registry;
        }

        private static Vector3? ResolvePositionConstraint(JObject constraint, EnvironmentRegistry registry, Bounds sceneBounds, Vector3? newElementSize = null)
        {
            var type = constraint["type"]?.ToString()?.ToLower();

            switch (type)
            {
                case "absolute":
                    return new Vector3(
                        constraint["x"]?.ToObject<float>() ?? 0,
                        constraint["y"]?.ToObject<float>() ?? 0,
                        constraint["z"]?.ToObject<float>() ?? 0);

                case "edge":
                    var edge = constraint["edge"]?.ToString() ?? "center";
                    return EnvironmentRegistry.GetEdgePosition(sceneBounds, edge);

                case "corner":
                    var corner = constraint["corner"]?.ToString() ?? "center";
                    return EnvironmentRegistry.GetEdgePosition(sceneBounds, corner);

                case "center":
                    return new Vector3(sceneBounds.center.x, 0, sceneBounds.center.z);

                case "adjacent":
                    var targetId = constraint["target"]?.ToString();
                    var side = constraint["side"]?.ToString() ?? "north";
                    var gap = constraint["gap"]?.ToObject<float>() ?? 0;

                    var target = registry.GetById(targetId);
                    if (target == null || !target.IsValid)
                        return null;

                    var dir = EnvironmentRegistry.GetDirection(side);
                    // Calculate extent in the specific direction (not diagonal magnitude!)
                    // This gives us the half-size in just the direction we're moving
                    float targetExtentInDir = Mathf.Abs(Vector3.Dot(target.bounds.extents, dir));

                    // Also account for new element's size if provided
                    float newElementExtentInDir = 0f;
                    if (newElementSize.HasValue)
                    {
                        // Calculate extent of new element in the movement direction
                        newElementExtentInDir = Mathf.Abs(Vector3.Dot(newElementSize.Value / 2f, dir));
                    }

                    var adjacentPos = target.bounds.center + dir * (targetExtentInDir + newElementExtentInDir + gap);
                    // Inherit Y from target element's base for multi-level support
                    adjacentPos.y = target.bounds.min.y;
                    return adjacentPos;

                case "offset":
                    var fromId = constraint["from"]?.ToString();
                    var fromElement = registry.GetById(fromId);
                    if (fromElement == null || !fromElement.IsValid)
                        return null;

                    var offset = new Vector3(
                        constraint["offset"]?["x"]?.ToObject<float>() ?? 0,
                        constraint["offset"]?["y"]?.ToObject<float>() ?? 0,
                        constraint["offset"]?["z"]?.ToObject<float>() ?? 0);

                    return fromElement.bounds.center + offset;

                case "span":
                    var fromSpanId = constraint["from"]?.ToString();
                    var toSpanId = constraint["to"]?.ToString();
                    var fromSpan = registry.GetById(fromSpanId);
                    var toSpan = registry.GetById(toSpanId);

                    if (fromSpan == null || toSpan == null || !fromSpan.IsValid || !toSpan.IsValid)
                        return null;

                    var midpoint = (fromSpan.bounds.center + toSpan.bounds.center) / 2f;
                    midpoint.y = 0;
                    return midpoint;

                // ==================== GRID-BASED POSITIONING ====================
                case "grid":
                    var cellX = constraint["x"]?.ToObject<int>() ?? 0;
                    var cellZ = constraint["z"]?.ToObject<int>() ?? 0;
                    var level = constraint["level"]?.ToObject<int>() ?? 0;
                    return registry.GridToWorld(cellX, cellZ, level);

                case "grid_adjacent":
                    var gridTargetId = constraint["target"]?.ToString();
                    var gridDirection = constraint["direction"]?.ToString() ?? "east";

                    var gridTarget = registry.GetById(gridTargetId);
                    if (gridTarget == null || !gridTarget.IsValid)
                        return null;

                    if (!gridTarget.onGrid)
                    {
                        // Target not on grid - convert its position to grid coords
                        gridTarget.gridPosition = registry.WorldToGrid(gridTarget.bounds.center);
                    }

                    var gridOffset = EnvironmentRegistry.GetGridDirection(gridDirection);
                    var newGridPos = gridTarget.gridPosition + gridOffset;
                    return registry.GridToWorld(newGridPos);

                default:
                    if (constraint["x"] != null || constraint["z"] != null)
                    {
                        return new Vector3(
                            constraint["x"]?.ToObject<float>() ?? 0,
                            constraint["y"]?.ToObject<float>() ?? 0,
                            constraint["z"]?.ToObject<float>() ?? 0);
                    }
                    return null;
            }
        }

        private static readonly Dictionary<string, string> MaterialPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            // ==================== TERRAIN ====================
            // Primary ground materials
            ["grass"] = "Assets/Materials/m_grass.mat",
            ["grass_wild"] = "Assets/Idyllic Fantasy Nature/Materials/Grass/Grass_02.mat",
            ["grass_tall"] = "Assets/Idyllic Fantasy Nature/Materials/Grass/Grass_03.mat",
            ["moss"] = "Assets/Materials/m_moss.mat",
            ["ground"] = "Assets/Materials/m_ground.mat",
            ["dirt"] = "Assets/Materials/m_dirt_forest.mat",
            ["dirt_forest"] = "Assets/Materials/m_dirt_forest.mat",

            // ==================== ROCKS & CLIFFS ====================
            ["cliff"] = "Assets/Materials/Cave/M_CliffRock.mat",
            ["cliff_rock"] = "Assets/Materials/Cave/M_CliffRock.mat",
            ["cave_cliff"] = "Assets/Materials/Cave/M_CaveCliff.mat",
            ["stone"] = "Assets/Idyllic Fantasy Nature/Materials/Rocks/Stone_Big_01.mat",
            ["stone_ground"] = "Assets/Idyllic Fantasy Nature/Materials/Rocks/Stone_Big_01.mat",
            ["stone_medium"] = "Assets/Idyllic Fantasy Nature/Materials/Rocks/Stone_Medium_01.mat",
            ["rock"] = "Assets/Idyllic Fantasy Nature/Materials/Rocks/Rock_Big_01.mat",
            ["rock_medium"] = "Assets/Idyllic Fantasy Nature/Materials/Rocks/Rock_Medium_01.mat",
            ["boulder"] = "Assets/Idyllic Fantasy Nature/Materials/Rocks/Rock_Big_02.mat",
            ["mossy_stone"] = "Assets/Materials/Cave/M_MossyStone.mat",
            ["ruined_stone"] = "Assets/Materials/Cave/M_RuinedStone.mat",

            // ==================== CAVE MATERIALS ====================
            ["cave_wall"] = "Assets/Materials/Cave/M_CaveWall.mat",
            ["cave_floor"] = "Assets/Materials/Cave/M_CaveFloor.mat",
            ["cave_ceiling"] = "Assets/Materials/Cave/M_CaveCeiling.mat",
            ["cave_pit"] = "Assets/Materials/Cave/M_CavePitFloor.mat",
            ["crystal"] = "Assets/Materials/Cave/M_Crystal.mat",
            ["crystal_stair"] = "Assets/Materials/Cave/M_CrystalStair.mat",
            ["stalactite"] = "Assets/Materials/Cave/M_Stalactite.mat",
            ["mushroom"] = "Assets/Materials/Cave/M_Mushroom.mat",

            // ==================== ARCHITECTURE (DungeonModularPack) ====================
            ["wall"] = "Assets/DungeonModularPack/Materials/M_Wall.mat",
            ["wall_stone"] = "Assets/DungeonModularPack/Materials/M_Wall.mat",
            ["dungeon_wall"] = "Assets/DungeonModularPack/Materials/M_Wall.mat",
            ["tile"] = "Assets/DungeonModularPack/Materials/M_Tile.mat",
            ["floor_tile"] = "Assets/DungeonModularPack/Materials/M_Tile.mat",
            ["floor_stone"] = "Assets/DungeonModularPack/Materials/M_Tile.mat",
            ["pillar"] = "Assets/DungeonModularPack/Materials/M_Pillar_A.mat",
            ["pillar_stone"] = "Assets/DungeonModularPack/Materials/M_Pillar_A.mat",
            ["arch"] = "Assets/DungeonModularPack/Materials/M_Arch_A.mat",
            ["step"] = "Assets/DungeonModularPack/Materials/M_Step_A.mat",
            ["stairs"] = "Assets/DungeonModularPack/Materials/M_Step_A.mat",
            ["stairs_stone"] = "Assets/DungeonModularPack/Materials/M_Step_A.mat",
            ["handrail"] = "Assets/DungeonModularPack/Materials/M_Handrail.mat",
            ["torch"] = "Assets/DungeonModularPack/Materials/M_Torch.mat",
            ["roof"] = "Assets/DungeonModularPack/Materials/M_Step_A.mat", // Terracotta-like

            // ==================== WOOD & BARK ====================
            ["wood"] = "Assets/Idyllic Fantasy Nature/Materials/Trees/Tree_Bark.mat",
            ["wall_wood"] = "Assets/Idyllic Fantasy Nature/Materials/Trees/Tree_Bark.mat",
            ["floor_wood"] = "Assets/Idyllic Fantasy Nature/Materials/Branches/Branch.mat",
            ["bark"] = "Assets/Idyllic Fantasy Nature/Materials/Trees/Tree_Bark.mat",
            ["bark2"] = "Assets/Idyllic Fantasy Nature/Materials/Trees/Tree_Bark_02.mat",
            ["fir_bark"] = "Assets/Idyllic Fantasy Nature/Materials/Trees/Fir_Bark.mat",
            ["branch"] = "Assets/Idyllic Fantasy Nature/Materials/Branches/Branch.mat",
            ["fir_branch"] = "Assets/Idyllic Fantasy Nature/Materials/Trees/Fir_Branch.mat",

            // ==================== BRIDGES ====================
            ["bridge_stone"] = "Assets/Materials/Cave/M_StoneBridge.mat",
            ["bridge_rope"] = "Assets/Materials/Cave/M_RopeBridge.mat",
            ["bridge_cave"] = "Assets/Materials/Cave/M_CaveBridge.mat",

            // ==================== PATHS ====================
            ["path_stone"] = "Assets/DungeonModularPack/Materials/M_Tile.mat",
            ["path_dirt"] = "Assets/Materials/m_dirt_forest.mat",
            ["path_grass"] = "Assets/Materials/m_grass.mat",
            ["cobblestone"] = "Assets/Idyllic Fantasy Nature/Materials/Rocks/Stone_Medium_01.mat",
            ["cobble"] = "Assets/Idyllic Fantasy Nature/Materials/Rocks/Stone_Medium_01.mat",
            ["packed_dirt"] = "Assets/Materials/m_ground.mat",
            ["worn_path"] = "Assets/Materials/m_ground.mat",
            ["gravel"] = "Assets/Materials/m_ground.mat",

            // ==================== WATER ====================
            ["water"] = "Assets/Idyllic Fantasy Nature/Materials/Waterplants/Ocean.mat",
            ["ocean"] = "Assets/Idyllic Fantasy Nature/Materials/Waterplants/Ocean.mat",
            ["lake"] = "Assets/Idyllic Fantasy Nature/Demo/Materials/Lake.mat",

            // ==================== VEGETATION ====================
            ["plant"] = "Assets/Idyllic Fantasy Nature/Materials/Plants/Plant.mat",
            ["bush"] = "Assets/Idyllic Fantasy Nature/Materials/Bushes/Bush_01.mat",
            ["bush2"] = "Assets/Idyllic Fantasy Nature/Materials/Bushes/Bush_02.mat",
            ["bush3"] = "Assets/Idyllic Fantasy Nature/Materials/Bushes/Bush_03.mat",
            ["flower"] = "Assets/Idyllic Fantasy Nature/Materials/Flowers/Flower.mat",
            ["flower_meadow"] = "Assets/Idyllic Fantasy Nature/Materials/Flowers/FlowerMeadow.mat",
            ["cattail"] = "Assets/Idyllic Fantasy Nature/Materials/Waterplants/Cattail.mat",
            ["reeds"] = "Assets/Idyllic Fantasy Nature/Materials/Waterplants/Reeds_01.mat",
            ["lilypad"] = "Assets/Idyllic Fantasy Nature/Materials/Waterplants/LilyPad.mat",
            ["waterlily"] = "Assets/Idyllic Fantasy Nature/Materials/Waterplants/Waterlily.mat",

            // ==================== TREE COLORS ====================
            ["leaves_green"] = "Assets/Idyllic Fantasy Nature/Materials/Trees/Broadleaf_Green.mat",
            ["leaves_red"] = "Assets/Idyllic Fantasy Nature/Materials/Trees/Broadleaf_Red.mat",
            ["leaves_blue"] = "Assets/Idyllic Fantasy Nature/Materials/Trees/Broadleaf_Blue.mat",
            ["leaves_purple"] = "Assets/Idyllic Fantasy Nature/Materials/Trees/Broadleaf_Purple.mat",
            ["willow_green"] = "Assets/Idyllic Fantasy Nature/Materials/Trees/Willow_Branch_Green.mat",
            ["willow_purple"] = "Assets/Idyllic Fantasy Nature/Materials/Trees/Willow_Branch_Purple.mat",
            ["willow_pink"] = "Assets/Idyllic Fantasy Nature/Materials/Trees/Willow_Branch_Pink.mat",
            ["willow_blue"] = "Assets/Idyllic Fantasy Nature/Materials/Trees/Willow_Branch_Blue.mat",
            ["willow_red"] = "Assets/Idyllic Fantasy Nature/Materials/Trees/Willow_Branch_Red.mat",
            ["blossoms"] = "Assets/Idyllic Fantasy Nature/Materials/Trees/Tree_Blossoms.mat",

            // ==================== PROTOTYPING (Gridbox) ====================
            ["grey"] = "Assets/Thirdparty/Ciathyza/Gridbox Prototype Materials/Materials/URP/Prototype_512x512_Grey1.mat",
            ["white"] = "Assets/Thirdparty/Ciathyza/Gridbox Prototype Materials/Materials/URP/Prototype_512x512_White.mat",
            ["red"] = "Assets/Thirdparty/Ciathyza/Gridbox Prototype Materials/Materials/URP/Prototype_512x512_Red.mat",
            ["blue"] = "Assets/Thirdparty/Ciathyza/Gridbox Prototype Materials/Materials/URP/Prototype_512x512_Blue1.mat",
            ["green"] = "Assets/Thirdparty/Ciathyza/Gridbox Prototype Materials/Materials/URP/Prototype_512x512_Green1.mat",
            ["brown"] = "Assets/Thirdparty/Ciathyza/Gridbox Prototype Materials/Materials/URP/Prototype_512x512_Brown.mat",
            ["orange"] = "Assets/Thirdparty/Ciathyza/Gridbox Prototype Materials/Materials/URP/Prototype_512x512_Orange.mat",
            ["yellow"] = "Assets/Thirdparty/Ciathyza/Gridbox Prototype Materials/Materials/URP/Prototype_512x512_Yellow.mat",
        };

        private static Material GetMaterialByName(string materialName)
        {
            if (string.IsNullOrEmpty(materialName))
                return null;

            var library = EnvironmentMaterialLibrary.Instance;
            if (library != null)
            {
                var mat = library.GetMaterial(materialName);
                if (mat != null) return mat;
            }

            if (MaterialPaths.TryGetValue(materialName, out var path))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null) return mat;
            }

            var guids = AssetDatabase.FindAssets($"{materialName} t:Material");
            if (guids.Length > 0)
            {
                return AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            return null;
        }

#if PROBUILDER_ENABLED
        private static void ApplyMaterial(ProBuilderMesh mesh, string materialName)
        {
            Material mat = GetMaterialByName(materialName);

            if (mat == null)
            {
                mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/DungeonModularPack/Materials/M_Wall.mat");
            }

            if (mat == null)
            {
                mat = AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/Thirdparty/Ciathyza/Gridbox Prototype Materials/Materials/URP/Prototype_512x512_Grey1.mat");
            }

            if (mat != null)
            {
                var renderer = mesh.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = mat;
                }
            }
        }

        private static void ApplyMaterialToMesh(ProBuilderMesh mesh, Material mat)
        {
            if (mat == null) return;

            var renderer = mesh.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = mat;
            }
        }
#endif

        /// <summary>
        /// Pre-calculate element size for adjacent constraint positioning.
        /// Accounts for rotation to get accurate size in world space.
        /// </summary>
        private static Vector3 CalculateElementSize(string structureType, JObject dimensions, Quaternion rotation)
        {
            Vector3 localSize;

            switch (structureType)
            {
                case "wall":
                    localSize = new Vector3(
                        dimensions["width"]?.ToObject<float>() ?? 5f,
                        dimensions["height"]?.ToObject<float>() ?? 3f,
                        dimensions["depth"]?.ToObject<float>() ?? 0.3f);
                    break;

                case "floor":
                    localSize = new Vector3(
                        dimensions["width"]?.ToObject<float>() ?? 10f,
                        dimensions["height"]?.ToObject<float>() ?? 0.2f,
                        dimensions["depth"]?.ToObject<float>() ?? 10f);
                    break;

                case "pillar":
                    var radius = dimensions["radius"]?.ToObject<float>() ?? 0.5f;
                    var pillarHeight = dimensions["height"]?.ToObject<float>() ?? 4f;
                    localSize = new Vector3(radius * 2f, pillarHeight, radius * 2f);
                    break;

                case "platform":
                    localSize = new Vector3(
                        dimensions["width"]?.ToObject<float>() ?? 4f,
                        dimensions["height"]?.ToObject<float>() ?? 0.5f,
                        dimensions["depth"]?.ToObject<float>() ?? 4f);
                    break;

                case "stairs":
                case "ramp":
                    localSize = new Vector3(
                        dimensions["width"]?.ToObject<float>() ?? 2f,
                        dimensions["height"]?.ToObject<float>() ?? (structureType == "stairs" ? 3f : 2f),
                        dimensions["depth"]?.ToObject<float>() ?? 4f);
                    break;

                default:
                    localSize = Vector3.one;
                    break;
            }

            // Rotate to get world-space bounds
            var rotatedSize = rotation * localSize;
            return new Vector3(Mathf.Abs(rotatedSize.x), Mathf.Abs(rotatedSize.y), Mathf.Abs(rotatedSize.z));
        }

        // ==================== VALIDATION ====================

        [MCPTool("env_validate", "Validate environment for gaps, overlaps, and structural issues", Category = "Environment", IsReadOnly = true)]
        [MCPParam("check_gaps", "boolean", "Check for gaps between walls that should connect (default: true)", false)]
        [MCPParam("check_overlaps", "boolean", "Check for overlapping elements (default: true)", false)]
        [MCPParam("check_floating", "boolean", "Check for floating elements not on ground (default: true)", false)]
        [MCPParam("gap_tolerance", "number", "Maximum gap in units considered acceptable (default: 0.5)", false)]
        [MCPParam("overlap_tolerance", "number", "Minimum overlap in units to report (default: 0.1)", false)]
        public static object EnvValidate(JObject args)
        {
            // Scan scene for all environment elements (both registered and from markers)
            var all = ScanSceneForElements();

            if (all.Length == 0)
            {
                return new
                {
                    success = true,
                    valid = true,
                    message = "No elements to validate",
                    issues = Array.Empty<object>()
                };
            }

            var checkGaps = args["check_gaps"]?.ToObject<bool>() ?? true;
            var checkOverlaps = args["check_overlaps"]?.ToObject<bool>() ?? true;
            var checkFloating = args["check_floating"]?.ToObject<bool>() ?? true;
            var gapTolerance = args["gap_tolerance"]?.ToObject<float>() ?? 0.5f;
            var overlapTolerance = args["overlap_tolerance"]?.ToObject<float>() ?? 0.1f;

            var issues = new List<object>();

            // Check for gaps between walls
            if (checkGaps)
            {
                var walls = all.Where(e => e.type == "wall" || e.type == "tower").ToArray();
                var gapIssues = FindWallGaps(walls, gapTolerance);
                issues.AddRange(gapIssues);
            }

            // Check for overlaps
            if (checkOverlaps)
            {
                var overlapIssues = FindOverlaps(all, overlapTolerance);
                issues.AddRange(overlapIssues);
            }

            // Check for floating elements
            if (checkFloating)
            {
                var floatingIssues = FindFloatingElements(all);
                issues.AddRange(floatingIssues);
            }

            return new
            {
                success = true,
                valid = issues.Count == 0,
                elementCount = all.Length,
                issueCount = issues.Count,
                issues = issues.ToArray(),
                summary = issues.Count == 0
                    ? "All validations passed"
                    : $"Found {issues.Count} issue(s): " +
                      $"{issues.Count(i => ((dynamic)i).type == "gap")} gaps, " +
                      $"{issues.Count(i => ((dynamic)i).type == "overlap")} overlaps, " +
                      $"{issues.Count(i => ((dynamic)i).type == "floating")} floating"
            };
        }

        // Simple class to hold element data for validation
        private class ValidationElement
        {
            public string id;
            public string type;
            public GameObject gameObject;
            public Bounds bounds;
            public string parentId;

            public static Bounds CalculateBounds(GameObject go)
            {
                var renderers = go.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                    return new Bounds(go.transform.position, Vector3.one);

                var bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);
                return bounds;
            }
        }

        private static ValidationElement[] ScanSceneForElements()
        {
            var elements = new List<ValidationElement>();

            // Find all GameObjects with EnvironmentElementMarker
            var markers = GameObject.FindObjectsByType<EnvironmentElementMarker>(FindObjectsSortMode.None);

            foreach (var marker in markers)
            {
                if (marker == null || marker.gameObject == null) continue;

                var go = marker.gameObject;
                var name = go.name.ToLower();

                // Infer type from name
                string type = "unknown";
                if (name.Contains("wall")) type = "wall";
                else if (name.Contains("floor") || name.Contains("base")) type = "floor";
                else if (name.Contains("roof") || name.Contains("top")) type = "roof";
                else if (name.Contains("pillar") || name.Contains("post")) type = "pillar";
                else if (name.Contains("ground")) type = "ground";
                else if (name.Contains("platform")) type = "platform";
                else if (name.Contains("stairs") || name.Contains("step")) type = "stairs";
                else if (name.Contains("path") || name.Contains("road")) type = "path";
                else if (name.Contains("hill") || name.Contains("terrain")) type = "terrain";
                else if (name.Contains("tower")) type = "tower";
                else if (name.Contains("gate") || name.Contains("arch")) type = "gate";

                elements.Add(new ValidationElement
                {
                    id = marker.elementId ?? go.name,
                    type = type,
                    gameObject = go,
                    bounds = ValidationElement.CalculateBounds(go),
                    parentId = null
                });
            }

            return elements.ToArray();
        }

        private static List<object> FindWallGaps(ValidationElement[] walls, float tolerance)
        {
            var issues = new List<object>();

            // Group walls by structure (same name prefix like "House1_", "Wall_South_", etc.)
            var wallGroups = walls
                .GroupBy(w => GetStructurePrefix(w.gameObject?.name ?? ""))
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .ToList();

            foreach (var group in wallGroups)
            {
                var groupWalls = group.ToArray();
                if (groupWalls.Length < 2) continue;

                for (int i = 0; i < groupWalls.Length; i++)
                {
                    var wall1 = groupWalls[i];
                    var bounds1 = wall1.bounds;
                    var corners1 = GetBoundsCorners2D(bounds1);

                    for (int j = i + 1; j < groupWalls.Length; j++)
                    {
                        var wall2 = groupWalls[j];
                        var bounds2 = wall2.bounds;
                        var corners2 = GetBoundsCorners2D(bounds2);

                        // Check if walls are close enough to potentially connect
                        float minDistance = float.MaxValue;
                        Vector2 closest1 = Vector2.zero, closest2 = Vector2.zero;

                        foreach (var c1 in corners1)
                        {
                            foreach (var c2 in corners2)
                            {
                                var dist = Vector2.Distance(c1, c2);
                                if (dist < minDistance)
                                {
                                    minDistance = dist;
                                    closest1 = c1;
                                    closest2 = c2;
                                }
                            }
                        }

                        // If walls are close but not touching, it's a gap
                        // Use tighter threshold (5 units) since we're within same structure
                        if (minDistance > tolerance && minDistance < 5f)
                        {
                            // Check if they should connect (aligned on an axis)
                            bool shouldConnect = ShouldWallsConnect(bounds1, bounds2);

                            if (shouldConnect)
                            {
                                issues.Add(new
                                {
                                    type = "gap",
                                    severity = minDistance > 2f ? "major" : "minor",
                                    element1 = wall1.id,
                                    element2 = wall2.id,
                                    structure = group.Key,
                                    gap = minDistance,
                                    point1 = new { x = closest1.x, z = closest1.y },
                                    point2 = new { x = closest2.x, z = closest2.y },
                                    message = $"Gap of {minDistance:F2} units between {wall1.gameObject?.name} and {wall2.gameObject?.name}"
                                });
                            }
                        }
                    }
                }
            }

            return issues;
        }

        private static string GetStructurePrefix(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";

            // Common patterns: "House1_Wall_S", "Wall_South_1", "Temple_North", "Tower_SW_South"
            // Extract prefix up to the wall direction indicator

            var parts = name.Split('_');
            if (parts.Length < 2) return name;

            // Check for patterns like "House1_Wall_X" or "Temple_X" where X is a direction
            var directionKeywords = new[] { "South", "North", "East", "West", "S", "N", "E", "W", "Floor", "Roof" };

            for (int i = 0; i < parts.Length; i++)
            {
                if (directionKeywords.Any(d => parts[i].Equals(d, StringComparison.OrdinalIgnoreCase)))
                {
                    // Return everything before the direction
                    return string.Join("_", parts.Take(i));
                }
            }

            // For "Wall_South_1" pattern, group by first two parts
            if (parts[0].Equals("Wall", StringComparison.OrdinalIgnoreCase) && parts.Length >= 2)
            {
                return $"{parts[0]}_{parts[1]}";
            }

            // Default: first part
            return parts[0];
        }

        private static bool ShouldWallsConnect(Bounds b1, Bounds b2)
        {
            // Walls should connect if they share an axis alignment and are adjacent
            // Check if they're on the same horizontal line (same Z range) or same vertical line (same X range)

            bool sameZRange = Mathf.Abs(b1.center.z - b2.center.z) < 2f ||
                              (b1.min.z < b2.max.z && b1.max.z > b2.min.z);
            bool sameXRange = Mathf.Abs(b1.center.x - b2.center.x) < 2f ||
                              (b1.min.x < b2.max.x && b1.max.x > b2.min.x);

            // If they share an axis, they might need to connect
            // Also check if one wall's edge is near the other wall's edge
            bool edgesNear = Mathf.Abs(b1.max.x - b2.min.x) < 5f ||
                             Mathf.Abs(b1.min.x - b2.max.x) < 5f ||
                             Mathf.Abs(b1.max.z - b2.min.z) < 5f ||
                             Mathf.Abs(b1.min.z - b2.max.z) < 5f;

            return edgesNear && (sameZRange || sameXRange);
        }

        private static Vector2[] GetBoundsCorners2D(Bounds b)
        {
            return new[]
            {
                new Vector2(b.min.x, b.min.z),
                new Vector2(b.max.x, b.min.z),
                new Vector2(b.max.x, b.max.z),
                new Vector2(b.min.x, b.max.z)
            };
        }

        private static List<object> FindOverlaps(ValidationElement[] elements, float tolerance)
        {
            var issues = new List<object>();

            // Only check elements that shouldn't overlap (walls, floors, pillars, buildings)
            var solidElements = elements.Where(e =>
                e.type == "wall" || e.type == "floor" || e.type == "pillar" ||
                e.type == "platform" || e.type == "ground" || e.type == "tower").ToArray();

            for (int i = 0; i < solidElements.Length; i++)
            {
                for (int j = i + 1; j < solidElements.Length; j++)
                {
                    var e1 = solidElements[i];
                    var e2 = solidElements[j];

                    // Skip if they're in a parent-child relationship
                    if (e1.parentId == e2.id || e2.parentId == e1.id)
                        continue;

                    // Check for 2D overlap (XZ plane) - more relevant for buildings
                    var overlap = CalculateOverlap2D(e1.bounds, e2.bounds);

                    if (overlap.x > tolerance && overlap.y > tolerance)
                    {
                        // Also check Y overlap to confirm true 3D intersection
                        float yOverlap = Mathf.Min(e1.bounds.max.y, e2.bounds.max.y) -
                                        Mathf.Max(e1.bounds.min.y, e2.bounds.min.y);

                        if (yOverlap > tolerance)
                        {
                            issues.Add(new
                            {
                                type = "overlap",
                                severity = (overlap.x > 1f || overlap.y > 1f) ? "major" : "minor",
                                element1 = e1.id,
                                element2 = e2.id,
                                overlapX = overlap.x,
                                overlapZ = overlap.y,
                                overlapY = yOverlap,
                                message = $"Overlap of {overlap.x:F2}x{overlap.y:F2} between {e1.gameObject?.name} ({e1.type}) and {e2.gameObject?.name} ({e2.type})"
                            });
                        }
                    }
                }
            }

            return issues;
        }

        private static Vector2 CalculateOverlap2D(Bounds b1, Bounds b2)
        {
            float overlapX = Mathf.Min(b1.max.x, b2.max.x) - Mathf.Max(b1.min.x, b2.min.x);
            float overlapZ = Mathf.Min(b1.max.z, b2.max.z) - Mathf.Max(b1.min.z, b2.min.z);

            return new Vector2(Mathf.Max(0, overlapX), Mathf.Max(0, overlapZ));
        }

        private static List<object> FindFloatingElements(ValidationElement[] elements)
        {
            var issues = new List<object>();

            // Ground level threshold
            const float groundLevel = 0.1f;

            foreach (var element in elements)
            {
                // Skip floors, ground, terrain, paths, and roads - they define the ground
                if (element.type == "floor" || element.type == "ground" || element.type == "terrain" ||
                    element.type == "path" || element.type == "road")
                    continue;

                var bottomY = element.bounds.min.y;

                // If bottom is significantly above ground and not on another element
                if (bottomY > groundLevel + 0.5f)
                {
                    // Check if it's sitting on another element
                    bool hasSupport = elements.Any(other =>
                        other.id != element.id &&
                        other.bounds.max.y >= bottomY - 0.2f &&
                        other.bounds.max.y <= bottomY + 0.2f &&
                        BoundsOverlapXZ(element.bounds, other.bounds));

                    if (!hasSupport)
                    {
                        issues.Add(new
                        {
                            type = "floating",
                            severity = bottomY > 2f ? "major" : "minor",
                            element = element.id,
                            bottomY = bottomY,
                            message = $"{element.gameObject?.name} ({element.type}) is floating at Y={bottomY:F2} with no visible support"
                        });
                    }
                }
            }

            return issues;
        }

        private static bool BoundsOverlapXZ(Bounds b1, Bounds b2)
        {
            return b1.min.x < b2.max.x && b1.max.x > b2.min.x &&
                   b1.min.z < b2.max.z && b1.max.z > b2.min.z;
        }

        private static string GetCardinalDirection(Vector3 position, Vector3 center)
        {
            var delta = position - center;
            var absX = Mathf.Abs(delta.x);
            var absZ = Mathf.Abs(delta.z);

            if (absX > absZ)
                return delta.x > 0 ? "east" : "west";
            else
                return delta.z > 0 ? "north" : "south";
        }

        private static object Vec3(Vector3 v) => new { x = v.x, y = v.y, z = v.z };

        private static object Error(string message) => new { success = false, error = message };
    }
}
#endif
