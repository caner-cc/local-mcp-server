# Local MCP Server

MCP (Model Context Protocol) server for Unity Editor that enables AI assistants like Claude to interact with Unity projects through standardized tools.

## Features

- **40+ Built-in Tools** for asset management, scene manipulation, component inspection, and more
- **Event-Based Compilation Waiting** - No polling, instant notification when compilation completes
- **Asset Caching** - 10-50x faster repeated queries
- **Extensible** - Add custom tools in your project with simple attributes
- **Autonomous Development** - Write, modify, and delete scripts with compilation feedback

---

## Quick Start (5 minutes)

### Step 1: Install the Package

**Option A: Via Unity Package Manager (Recommended)**

1. Open your Unity project
2. Go to `Window > Package Manager`
3. Click the `+` button in the top-left corner
4. Select `Add package from git URL...`
5. Paste this URL and click Add:
   ```
   https://github.com/caner-cc/local-mcp-server.git
   ```

**Option B: Edit manifest.json directly**

Open `YourProject/Packages/manifest.json` and add this line to the `dependencies` section:

```json
{
  "dependencies": {
    "com.localmcp.server": "https://github.com/caner-cc/local-mcp-server.git",
    ...
  }
}
```

Save the file. Unity will automatically download and install the package.

### Step 2: Enable the MCP Server

1. In Unity, go to `Tools > MCP > Enable MCP Server`
2. You should see a console message: `[MCP] Server started on port 8090`

The server is now running and listening for connections.

### Step 3: Connect Claude Code

**Option A: Copy the mcp.json file (Recommended)**

Copy `mcp.json` from the package folder to your Unity project root:
```
Packages/com.localmcp.server/mcp.json  →  YourProject/mcp.json
```

Claude Code will auto-discover the server when you open the project.

**Option B: Manual registration**

Open a terminal in your project directory and run:

```bash
claude mcp add --transport http unity http://localhost:8090/mcp -s project
```

### Step 4: Verify It Works

Start a Claude Code session and try:

```
"Use the editor_state tool to check Unity's current state"
```

Claude should be able to query Unity and tell you if it's playing, paused, the current scene name, etc.

---

## What Can You Do With It?

Once connected, Claude can:

- **Find and inspect assets** - "Find all ScriptableObjects in Assets/Data"
- **Create GameObjects** - "Create an empty GameObject called 'GameManager' at the origin"
- **Add components** - "Add a Rigidbody to the Player object"
- **Write scripts** - "Create a PlayerHealth.cs script with 100 max health"
- **Control the editor** - "Enter play mode" / "Stop playing"
- **Read console logs** - "Show me the last 10 error messages"
- **Navigate the scene** - "Frame the MainCamera in the scene view"

---

## Built-in Tools Reference

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

---

## Creating Custom Tools

Add custom tools to your project by creating classes with `[MCPTool]` attributes:

```csharp
using Newtonsoft.Json.Linq;
using LocalMCP;

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

Tools are auto-discovered via reflection. Your assembly must reference `LocalMCP.Editor`.

### Importing the Sample

1. Open Package Manager
2. Find "Local MCP Server"
3. Expand "Samples"
4. Click "Import" next to "Custom Tool Example"

---

## Configuration

### MCP Control Panel

Access via `Tools > MCP > Control Panel`:
- Enable/disable server
- View connection status
- Monitor tool calls
- Clear request queue

### EditorPrefs Keys

| Key | Default | Description |
|-----|---------|-------------|
| `LocalMCP_Enabled` | false | Server enabled state |
| `LocalMCP_Port` | 8090 | Server port |
| `LocalMCP_ShowToolbar` | false | Show scene view toolbar |

---

## Troubleshooting

### Server won't start
- Check if port 8090/8091 is already in use
- Try `Tools > MCP > Force Stop MCP (Emergency)` then re-enable

### Tools not appearing
- Run `Tools > MCP > Refresh Tool Registry`
- Check for compilation errors in the Console

### Claude can't connect
- Make sure the server is enabled (check `Tools > MCP > Control Panel`)
- Verify the port: `curl http://localhost:8090/heartbeat`
- Re-register: `claude mcp remove unity -s project` then add again

### WSL2 can't connect
Windows localhost is not accessible from WSL2. Use PowerShell as a bridge:

```bash
powershell.exe -Command "Invoke-WebRequest -Uri 'http://localhost:8090/mcp' -Method POST -ContentType 'application/json' -Body '{...}' -TimeoutSec 30 -UseBasicParsing | Select-Object -ExpandProperty Content"
```

---

## Requirements

- Unity 2021.3 or later
- Newtonsoft.Json (automatically installed as dependency)

## License

MIT License - see LICENSE file for details.
