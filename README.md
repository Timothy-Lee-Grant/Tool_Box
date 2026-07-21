# Tool_Box

An MCP server platform: a thin host that exposes independent **toolsets** to AI agents — Claude Desktop, Claude Code, and (over HTTP, later) my own agents such as [LLM_Monitor](https://github.com/Timothy-Lee-Grant/LLM_Monitor)'s LangGraph tool loop.

The LLM is the brain; this project is the hands.

## Architecture

```
  MCP client (Claude Desktop / Code / Inspector / LangGraph agent)
        │  stdio  ─or─  streamable HTTP (:8080/mcp, /health)
        ▼
  ToolBox.Host        thin composition root: config → toolsets → transport
        │
  ToolSets/*          independent capability libraries (Basics, Voxel, then more)
        │
  ToolBox.Core        shared plumbing: bounded output, server info, logging rules
```

The Voxel toolset also brings its own companion infrastructure — a `BackgroundService` that broadcasts world changes over a loopback WebSocket (`:8090`, independent of and never colliding with the MCP HTTP transport's own `:8080`) to a live browser viewer. See [Voxel viewer](#voxel-viewer) below.

## Transports

One binary, two wires. Selection precedence: `--transport` flag > `TOOLBOX_TRANSPORT` env var > `appsettings.json` (default: stdio).

| Transport | Start | Use for |
|---|---|---|
| stdio (default) | client launches the DLL | Claude Desktop, Claude Code — local, client-as-parent |
| streamable HTTP | `dotnet run --project src/ToolBox.Host -- --transport http` | remote/containerized consumers (LLM_Monitor); serves `/mcp` + `/health` on :8080, stateless |

Container quickstart:

```
docker compose up --build        # healthy at http://localhost:8081/health
# Inspector → Streamable HTTP → http://localhost:8081/mcp
```

Consuming from LLM_Monitor: see [docs/LLM_MONITOR_INTEGRATION.md](docs/LLM_MONITOR_INTEGRATION.md).

Design rules: toolsets never know about the protocol or each other; the host never contains domain logic; all tool output is bounded; in a stdio server **stdout belongs to the protocol** — logs go to stderr only.

## Build & test

Requires the .NET 10 SDK (current LTS).

```
dotnet build
dotnet test
```

## Connect a client

Build once in Release, then register the built DLL — not `dotnet run` — so no build system sits in the protocol's launch path:

```
dotnet build -c Release
```

**Claude Desktop** (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "toolbox": {
      "command": "dotnet",
      "args": ["/Users/timothygrant/Desktop/projects/Tool_Box/src/ToolBox.Host/bin/Release/net10.0/ToolBox.Host.dll"]
    }
  }
}
```

**Claude Code:**

```
claude mcp add toolbox -- dotnet /Users/timothygrant/Desktop/projects/Tool_Box/src/ToolBox.Host/bin/Release/net10.0/ToolBox.Host.dll
```

**Interactive debugging:**

```
npx @modelcontextprotocol/inspector dotnet src/ToolBox.Host/bin/Release/net10.0/ToolBox.Host.dll
```

Tools available: see [docs/TOOL_CATALOG.md](docs/TOOL_CATALOG.md). Rationale for design choices: [docs/DECISIONS.md](docs/DECISIONS.md).

## Voxel viewer

The Voxel toolset (`place_box`, `place_sphere`, `mirror`, ...) is buildable live in a browser. The viewer is a plain static page — no Tool_Box code serves it, no build step, any static-file tool works:

```
cd viewer
npx --yes serve .        # or: python3 -m http.server 5500
```

Open the printed URL, then run the Host (any transport — the viewer's WebSocket is independent of the MCP wire) and point an agent at it. Each `place_*`/`remove_box`/`mirror`/`clear` call renders live; drag to orbit, scroll to zoom, `F`/`R`/`G`/`E` reframe/auto-rotate/grid/edges. See `.claude/skills/voxel/SKILL.md` for the conventions an agent should follow (call `world_info` first, materials, primitives, build order).

## Status

Plan 001 (MVP foundation), plan 002 (HTTP transport + containerization), and plan 003 (Voxel world-builder toolset) implemented — see `Documentation/ImplementationPlans/`. Current toolsets: `Basics` (`ping`, `server_info`, `current_time`) and `Voxel` (a live-buildable voxel world — first stateful, first write-classified, first toolset with its own companion infrastructure). Plan 004 (SPICE circuit designer) is drafted but deferred. Next: LLM_Monitor consumption (via that repo's own plan).
