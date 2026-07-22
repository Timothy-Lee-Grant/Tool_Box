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
  ToolSets/*          independent capability libraries (Basics, then more)
        │
  ToolBox.Core        shared plumbing: bounded output, server info, logging rules
```

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

## Status

Plan 001 (MVP foundation) and plan 002 (HTTP transport + containerization) implemented — see `Documentation/ImplementationPlans/`. Current toolset: `Basics` (`ping`, `server_info`, `current_time`) — deliberately trivial; the deliverable so far is the platform, not the tools. Next: LLM_Monitor consumption (via that repo's own plan), then real toolsets.
