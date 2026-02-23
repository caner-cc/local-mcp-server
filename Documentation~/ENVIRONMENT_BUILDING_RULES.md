# Environment Building Rules

Rules and patterns for building coherent environments with ProBuilder via MCP tools.

## Core Principles

### 1. Calculate Before You Build
Never place elements by guessing positions. Always calculate exact coordinates:
```
For a structure at center (Cx, Cz) with size (W, D):
- West edge = Cx - W/2
- East edge = Cx + W/2
- South edge = Cz - D/2
- North edge = Cz + D/2
```

### 2. Corners Must Meet
When building rectangular enclosures, perpendicular walls must extend to meet parallel walls.

**Wrong approach** (gaps at corners):
```
South wall: z=0, segments at x=2,6 (each 4 wide) → spans x=0-8
West wall: x=0, segments at z=3,7 (each 4 wide) → spans z=1-9  ← GAP at z=0-1!
```

**Correct approach**:
```
South wall: z=0, spans x=0 to x=8
West wall: x=0, must span z=0 to z=8 (match south wall's z to north wall's z)

If using 4-unit wall segments for west wall from z=0 to z=8:
- Segment 1: center z=2, spans z=0-4
- Segment 2: center z=6, spans z=4-8
```

### 3. Build Order (Bottom-Up)
1. **Terrain** - Ground plane, hills, slopes
2. **Foundations** - Platforms where structures will sit
3. **Walls** - All walls for a structure before moving to next
4. **Roofs** - After walls are complete
5. **Details** - Pillars, furniture, decorations
6. **Paths** - Connect all accessible areas

### 4. Structure Template

For a rectangular building at position (X, Z) with dimensions (W, D, H):

```
Floor:
  position: (X, 0, Z)
  size: (W, 0.3, D)
  edges: x=[X-W/2, X+W/2], z=[Z-D/2, Z+D/2]

South Wall (at south edge):
  position: (X, floor_top, Z - D/2)
  length: W (spans full width)

North Wall (at north edge):
  position: (X, floor_top, Z + D/2)
  length: W

West Wall (at west edge, rotated):
  position: (X - W/2, floor_top, Z)
  length: D (spans full depth!)

East Wall (at east edge, rotated):
  position: (X + W/2, floor_top, Z)
  length: D

Roof:
  position: (X, floor_top + wall_height, Z)
  size: (W + overhang, 0.3, D + overhang)
```

### 5. Wall Segment Placement

When using multiple wall segments to form one side:

```
For a wall from position A to position B (total length L):
- Number of segments: ceil(L / segment_width)
- First segment center: A + segment_width/2
- Each subsequent: previous + segment_width
- Last segment may need adjustment to reach exactly B
```

### 6. Perpendicular Wall Positioning

For walls that need to meet at corners:
- Axis-aligned walls (facing north/south): Place at exact Z coordinate
- Rotated walls (facing east/west): Place at exact X coordinate
- The rotated wall's LENGTH becomes its Z-span after rotation

### 7. Roof Considerations

- Flat roof: Platform at wall_top, slightly larger than floor (overhang)
- Sloped roof: Use ramp structure or multiple angled platforms
- Roof height: floor_y + floor_thickness + wall_height

### 8. Path Connectivity Rule

Every structure must be connected to the main path network:
- Main roads: 4-6 units wide
- Side paths: 2-4 units wide
- Paths should reach building entrances, not just nearby

### 9. Terrain Integration

- Structures on flat ground: floor_y = ground_y + small_offset (0.05-0.1)
- Structures on platforms: floor_y = platform_top
- Paths: slightly above ground (0.05) to prevent z-fighting

### 9b. Ground + Path Layering (Recommended Approach)

Create natural-looking paths using a two-layer system:

**Layer 1: Grass Base**
```
env_create_structure:
  structure_type: "floor"
  position: {type: "absolute", x: centerX, y: 0, z: centerZ}
  dimensions: {width: areaWidth, depth: areaDepth, height: 0.1}
  material: "grass"
  name: "Ground_Main"
```

**Layer 2: Dirt Paths (on top of grass)**
```
env_create_structure:
  structure_type: "floor"
  position: {type: "absolute", x: pathCenterX, y: 0.05, z: pathCenterZ}
  dimensions: {width: pathWidth, depth: pathLength, height: 0.1}
  material: "dirt"
  name: "Path_ToTavern"
```

**Key Points:**
- Grass base at y=0, height 0.1
- Dirt paths at y=0.05 (sitting on grass), height 0.1
- Path surfaces end up at y=0.15, clearly visible above grass (y=0.1)
- Use meaningful names: `Path_ToTavern`, `Road_Main`, `Path_House1`
- Paths should be 3-4 units wide for walkways, 5-6 for main roads
- Create fewer, longer path segments rather than many small pieces

### 10. Scale Guidelines

| Element | Recommended Size |
|---------|-----------------|
| Small house | 8x8 to 10x10 |
| Large house | 12x12 to 16x16 |
| Wall height | 3-4 units |
| Door width | 2-3 units |
| Path width | 3-4 units |
| Town square | 16x16 to 24x24 |
| Perimeter (small town) | 48x48 |
| Perimeter (large town) | 64x64 to 96x96 |

### 11. Material Variety

- **Per structure type**: All houses don't need same material
- **Logical grouping**: Rich area = stone, poor area = wood
- **Accent pieces**: Pillars, trim in contrasting material
- **Ground variation**: Paths = tile/stone, yards = grass/dirt

## Common Mistakes

1. **Forgetting wall thickness** - Walls have depth (0.3-0.5), affects corner placement
2. **Mismatched wall lengths** - Perpendicular walls must span full parallel distance
3. **Floating elements** - Always verify Y position matches supporting surface
4. **Z-fighting** - Overlapping surfaces flicker; offset by 0.05 minimum
5. **Missing connections** - Every area needs path access
6. **Uniform materials** - Monotonous; vary by structure or area

## Verification Checklist

Before considering a structure complete:
- [ ] All corners meet (no gaps)
- [ ] Roof covers entire structure
- [ ] Floor is visible (not buried in ground)
- [ ] Path connects to entrance
- [ ] Materials are applied
- [ ] No floating elements
