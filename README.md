# Tool_Box

An MCP server platform: a thin host that exposes independent **toolsets** to AI agents — Claude Desktop, Claude Code, and (over HTTP, later) my own agents such as [LLM_Monitor](https://github.com/Timothy-Lee-Grant/LLM_Monitor)'s LangGraph tool loop.

The LLM is the brain; this project is the hands.

## Architecture

```
  MCP client (Claude Desktop / Code / agent)
        │  stdio (HTTP planned)
        ▼
  ToolBox.Host        thin composition root: config → toolsets → transport
        │
  ToolSets/*          independent capability libraries (Basics, then more)
        │
  ToolBox.Core        shared plumbing: bounded output, server info, logging rules
```

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

Implementation plan 001 (MVP foundation) in progress — see `Documentation/ImplementationPlans/`. Current toolset: `Basics` (`ping`, `server_info`, `current_time`) — deliberately trivial; the deliverable of plan 001 is the architecture, not the tools.
