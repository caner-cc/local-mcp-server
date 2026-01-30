# Unity MCP Server

MCP (Model Context Protocol) server for Unity Editor that enables AI assistants like Claude to interact with Unity projects through standardized tools.

## Features

- **40+ Built-in Tools** for asset management, scene manipulation, component inspection, and more
- **Event-Based Compilation Waiting** - No polling, instant notification when compilation completes
- **Asset Caching** - 10-50x faster repeated queries
- **Extensible** - Add custom tools in your project with simple attributes
- **Autonomous Development** - Write, modify, and delete scripts with compilation feedback

## Installation

### Via Package Manager (Git URL)

1. Open Window > Package Manager
2. Click `+` > Add package from git URL
3. Enter: `https://github.com/YOUR_USERNAME/unity-mcp-server.git?path=com.unity-mcp.server`

For private repositories, use SSH:
```
git@github.com:YOUR_USERNAME/unity-mcp-server.git?path=com.unity-mcp.server
```

### Via manifest.json

Add to your `Packages/manifest.json`:
```json
{
  "dependencies": {
    "com.unity-mcp.server": "https://github.com/YOUR_USERNAME/unity-mcp-server.git?path=com.unity-mcp.server"
  }
}
```

## Quick Start

### Enable the Server

1. In Unity: `Tools > MCP > Enable MCP Server`
2. The server starts on port 8090 (fallback: 8091)

### Connect Claude Code

```bash
claude mcp add --transport http unity http://localhost:8090/mcp -s project
```

### Verify Connection

```bash
# Check server health
curl http://localhost:8090/heartbeat

# List available tools
curl -X POST http://localhost:8090/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'
```

## Built-in Tools

### Asset Operations
| Tool | Description |
|------|-------------|
| `asset_find` | Find assets by type/name/folder (cached, fast) |
| `asset_info` | Get asset properties and references |
| `asset_create` | Create new ScriptableObjects |
| `asset_modify` | Modify asset properties |
| `asset_delete` | Delete assets |
| `asset_move` | Move/rename assets |
| `asset_dependencies` | Show asset dependencies |
| `prefab_instantiate` | Instantiate prefabs in scene |

### Scene Operations
| Tool | Description |
|------|-------------|
| `scene_info` | Current scene details |
| `scene_load` | Load a scene |
| `scene_save` | Save current scene |
| `gameobject_create` | Create GameObjects |
| `gameobject_find` | Find objects in hierarchy |
| `gameobject_modify` | Modify transform/properties |
| `gameobject_delete` | Delete GameObjects |

### Component Operations
| Tool | Description |
|------|-------------|
| `component_add` | Add component to GameObject |
| `component_remove` | Remove component |
| `component_inspect` | View component properties |
| `component_set` | Set component property values |

### Editor Control
| Tool | Description |
|------|-------------|
| `editor_control` | Play/pause/stop |
| `editor_state` | Get editor state |
| `sceneview_lookat` | Move scene camera |
| `sceneview_frame` | Frame object in view |
| `ping_asset` | Highlight asset in Project |
| `open_script` | Open script in IDE |

### Development Tools
| Tool | Description |
|------|-------------|
| `write_script` | Create C# scripts (waits for compilation) |
| `modify_script` | Edit existing scripts |
| `delete_script` | Delete scripts |
| `wait_for_compilation` | Wait for Unity to finish compiling |
| `ensure_ready` | Block until Unity is ready |
| `compile_errors` | Get current compile errors |
| `console_read` | Read Unity console logs |
| `console_clear` | Clear console |
| `mcp_health` | Server diagnostics |

## Creating Custom Tools

Add custom tools to your project by creating classes with `[MCPTool]` attributes:

```csharp
using Newtonsoft.Json.Linq;
using UnityMCP;

public static class MyProjectTools
{
    [MCPTool("my_tool", "Description of what the tool does")]
    [MCPParam("myParam", "string", "Description of parameter")]
    [MCPParam("optionalParam", "integer", "Optional parameter", false)]
    public static object MyTool(JObject args)
    {
        var myParam = args["myParam"]?.ToString();
        var optionalParam = args["optionalParam"]?.ToObject<int>() ?? 10;

        // Do something...

        return new { success = true, result = "..." };
    }
}
```

Tools are auto-discovered via reflection. Your assembly must reference `UnityMCP.Editor`.

### Importing the Sample

1. Open Package Manager
2. Find "Unity MCP Server"
3. Expand "Samples"
4. Click "Import" next to "Custom Tool Example"

## Configuration

### Server Settings

Access via `Tools > MCP > Control Panel`:
- Enable/disable server
- View connection status
- Monitor tool calls
- Clear request queue

### EditorPrefs Keys

| Key | Default | Description |
|-----|---------|-------------|
| `UnityMCP_Enabled` | false | Server enabled state |
| `UnityMCP_Port` | 8090 | Server port |
| `UnityMCP_ShowToolbar` | false | Show scene view toolbar |

## WSL2 Usage

If running Claude from WSL2, use PowerShell as a bridge:

```bash
powershell.exe -Command "Invoke-WebRequest -Uri 'http://localhost:8090/mcp' -Method POST -ContentType 'application/json' -Body '{...}' -TimeoutSec 30 -UseBasicParsing | Select-Object -ExpandProperty Content"
```

## Requirements

- Unity 2021.3 or later
- Newtonsoft.Json (automatically installed as dependency)

## License

MIT License - see LICENSE file for details.

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Submit a pull request

## Troubleshooting

### Server won't start
- Check if port 8090/8091 is in use
- Try `Tools > MCP > Force Stop MCP (Emergency)` then re-enable

### Tools not appearing
- Run `Tools > MCP > Refresh Tool Registry`
- Check for compilation errors

### WSL2 can't connect
- Windows localhost is not accessible from WSL2
- Use the PowerShell bridge method described above
