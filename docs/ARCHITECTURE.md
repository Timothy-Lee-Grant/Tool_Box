# Tool_Box architecture — the current system, in diagrams

**Living document.** It describes what is true on `next` *right now*, and it is updated in the same PR as any change to structure: projects, runtime components, ports, config keys, or the startup flow (CLAUDE.md rule 9). The *why* behind each piece lives in `docs/DECISIONS.md`. This file is the *what* and *how it connects*.

**State as of v1.1.0 (S0 complete):** a single process that serves MCP over stdio or HTTP. Its toolsets are chosen at **startup** from a catalog plus configuration.

---

## 1. The big picture

```mermaid
flowchart LR
  subgraph Agents["Agents (MCP clients)"]
    CC["Claude Code / Desktop<br/>(stdio)"]
    LM["LLM_Monitor, others<br/>(HTTP)"]
  end

  subgraph Process["ToolBox.Host — one process"]
    T{"Transport<br/>stdio | http"}
    MCP["MCP server<br/>(ModelContextProtocol SDK)"]
    COMP["AddToolBoxServer()<br/>composition root"]
    CAT["ToolBoxCatalog<br/>(the only list of toolsets)"]
    CORE["ToolBox.Core<br/>catalog types · descriptors · server info · logging"]
    subgraph Enabled["Toolsets enabled at startup"]
      B["Basics<br/>ping · server_info · current_time"]
      V["Voxel<br/>12 shape tools + VoxelWorld"]
      E["Embedded<br/>build tools (off by default)"]
    end
    VW["VoxelViewerBroadcastService<br/>(WebSocket :8090)"]
  end

  Browser["Browser viewer<br/>viewer/index.html"]

  CC -- "stdin/stdout JSON-RPC" --> T
  LM -- "HTTP POST /mcp :8080" --> T
  T --> MCP
  COMP --> CAT --> Enabled
  COMP --> CORE
  MCP --> B & V & E
  V --> VW -- "world updates" --> Browser
```

Reading the picture:
- **Agents** connect over one of two **transports** (ADR-007). The transport is picked at startup by `--transport`, then the `TOOLBOX_TRANSPORT` env var, then `appsettings.json`; the default is stdio.
- **`AddToolBoxServer()`** is the composition root: the single place that decides what the server *is*. Both transports call it.
- **`ToolBoxCatalog`** is the only place toolsets are named. Only the toolsets that are **enabled** get registered. A disabled toolset's tools and background services do not exist at all (ADR-015).
- **Voxel** brings its own background service (ADR-010), which streams world changes to a browser viewer. The agent never talks to that channel.

## 2. Projects and dependencies

```mermaid
flowchart TD
  Host["ToolBox.Host<br/>(exe)"] --> Core["ToolBox.Core"]
  Host --> Basics["ToolBox.Basics"] --> Core
  Host --> Voxel["ToolBox.Voxel"] --> Core
  Host --> Embedded["ToolBox.Embedded"] --> Core
```

Rule (ADR-003): **Core knows no toolset.** Only the Host references toolset projects. Each toolset exposes one `CatalogEntry` and one `Add<Name>Toolset()` method (ADR-005, refined by ADR-015).

## 3. Startup flow: how the toolset roster is decided

```mermaid
sequenceDiagram
  participant P as Program.cs
  participant C as Configuration
  participant S as AddToolBoxServer()
  participant K as ToolBoxCatalog / ToolsetCatalog
  participant DI as Service collection
  participant H as Host (running)

  P->>C: build config (appsettings.json, TOOLBOX_* env vars, args)
  P->>S: AddToolBoxServer(services, config)
  S->>K: ResolveEnabled(config) → e.g. ["basics"]
  S->>K: Create().Compose(services, enabled)
  K->>DI: entry.Register(services) — ONLY for enabled entries
  Note over K: unknown id → throw, listing the valid ids (fail fast)
  S->>DI: add ToolsetStartupLogger, AddMcpServer()
  P->>H: Build() + Run()
  H-->>H: stderr log "Enabled toolsets: basics"
  Note over DI,H: after Build() the service collection is FROZEN —<br/>changing toolsets today means a restart
```

### Where "enabled" comes from (later sources win)

```mermaid
flowchart LR
  D["Default in code<br/>basics, voxel"] --> A["appsettings.json<br/>Toolsets:Enabled"] --> EV["Env var<br/>TOOLBOX_TOOLSETS=basics,voxel"] --> T["Test overrides<br/>(in-memory)"]
```

An empty `TOOLBOX_TOOLSETS` enables nothing (the startup log says `(none)`).

## 4. Ports and endpoints

| What | Where | Notes |
|---|---|---|
| MCP over stdio | stdin/stdout | No port. stdout is reserved for protocol messages, so all logs go to stderr (ADR-004) |
| MCP over HTTP | `:8080` `/mcp` | Stateless streamable HTTP. Never publish it beyond a trusted network (ADR-008/011) |
| Health check | `:8080` `/health` | `{"status","version","toolsets","uptime"}` |
| Voxel viewer feed | `:8090` (falls back to 8091–8093) `/voxel/` | Only exists when Voxel is enabled (ADR-012) |

## 5. Branches and releases

```mermaid
gitGraph
  commit id: "v1.0.2 (archived v1)" tag: "v1.0.2"
  branch next
  commit id: "M0 cleanup + firmware merge"
  commit id: "S0 catalog (#25 #26 #27)"
  checkout main
  merge next id: "Release v1.1.0" tag: "v1.1.0"
  checkout next
  commit id: "S1 work…"
```

`main` = released and always working (the résumé link). `next` = integration. One `claude/issue-*` branch per task.

## 6. Roadmap: where this is heading

| Phase | Milestone | What it changes in the diagrams above | Status |
|---|---|---|---|
| S0 | M2 | Hard-coded toolset chain → catalog + config (§1, §3) | ✅ v1.1.0 |
| S1 | M3 | Adds a **desired-state file**, a **reconciler**, a loopback **admin API** with a token, a port table, and a background run mode. The roster becomes changeable **while running** | ⏳ next |
| S2 | M4 | Adds the **ToolBox.Desktop** GUI (Avalonia), which talks to the admin API | — |
| S3 | M5 | Voxel page, "View world", the world persists across off/on | — |
| S4 | M6 | Copilot registration | — |
| S5 | M7 | **ToolBox.Rag** toolset (SQLite + sqlite-vec, ONNX embeddings) | — |
| S6 | M8 | Installers, start with the OS, v2.0 | — |

Target shape after S2 (from Brainstorm 004):

```mermaid
flowchart LR
  GUI["ToolBox.Desktop (GUI)"] -- "admin API (loopback + token)<br/>CONTROL PLANE" --> HOST
  subgraph HOST["Background ToolBox.Host"]
    DS[("desired-state file")]
    R["Reconciler"]
    TS["Toolsets (on/off at runtime)"]
    DS --> R --> TS
  end
  Agents["Claude CLI · Copilot"] -- "MCP<br/>DATA PLANE" --> HOST
```
