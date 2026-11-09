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
    REC["ToolsetReconcilerService → ToolsetReconciler<br/>starts toolsets, opens the ToolsetGate"]
    GATE["ToolsetGate + MCP filters<br/>hide closed tools, 'toolset is off'"]
    subgraph Enabled["Toolsets (all registered; started and visible only if enabled)"]
      B["Basics<br/>ping · server_info · current_time"]
      V["Voxel<br/>12 shape tools + VoxelWorld"]
      E["Embedded<br/>build tools (off by default)"]
    end
    VW["VoxelViewerBroadcastService<br/>(WebSocket :8090, loopback by default)"]
  end

  Browser["Browser viewer<br/>viewer/index.html"]

  CC -- "stdin/stdout JSON-RPC" --> T
  LM -- "HTTP POST /mcp :8080" --> T
  T --> MCP
  COMP --> CAT --> Enabled
  COMP --> CORE
  COMP --> REC --> GATE
  MCP --> GATE --> B & V & E
  REC -- "Start / Stop" --> VW
  V --> VW -- "world updates" --> Browser
```

Reading the picture:
- **Agents** connect over one of two **transports** (ADR-007). The transport is picked at startup by `--transport`, then the `TOOLBOX_TRANSPORT` env var, then `appsettings.json`; the default is stdio.
- **`AddToolBoxServer()`** is the composition root: the single place that decides what the server *is*. Both transports call it.
- **`ToolBoxCatalog`** is the only place toolsets are named. Every toolset is registered, but only **enabled** ones are started and visible: the reconciler starts them and opens the gate, and the gate hides a disabled toolset's tools and its background services never run (ADR-018, partly superseding ADR-015).
- **Voxel** brings its own background work (ADR-010), now a start/stop lifecycle rather than a container-started service, which streams world changes to a browser viewer. The agent never talks to that channel.

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
  S->>S: DesiredStateStore(Desired:Path).Load() — read only, never throws, never writes
  Note over S: corrupt/unsupported → defaults + components.desiredState (degraded)
  S->>K: EnabledResolution.Resolve(config, desired) → desired + effective + pinnedBy
  S->>K: ValidateIds(effective) — unknown id → throw, listing the valid ids (fail fast)
  S->>K: ComposeAll(services)
  K->>DI: entry.Register(services) — EVERY entry, cheap and side-effect-free (ADR-018)
  Note over K: a raw IHostedService in a toolset → throw
  S->>DI: add gate (closed), status board, ToolsetReconciler, ToolsetReconcilerService, AddMcpServer() + gate filters
  P->>H: Build() + Run()
  Note over P,H: HTTP mode: PortTable defaults + validation first;<br/>then probe the MCP port before Kestrel binds (taken → exit 3)
  H-->>H: ToolsetReconcilerService reconciles to the effective roster
  H-->>H: per toolset: Start() succeeds, then the gate opens (voxel: viewer binds its port, or components.viewer = unavailable)
  H-->>H: stderr log "Enabled toolsets: basics"
  Note over DI,H: the container is frozen after Build(), so every toolset is registered<br/>and the gate decides what is started and visible
```

### Where "enabled" comes from (five layers, later wins)

```mermaid
flowchart LR
  D["1. Code default<br/>basics, voxel"] --> A["2. appsettings.json<br/>Toolsets:Enabled"]
  A --> F["3. Desired-state file<br/>(user intent, GUI/admin-API written)<br/>enabled + opaque settings"]
  F --> DES["DESIRED set"]
  DES --> E["4. TOOLBOX_TOOLSETS<br/>operator pin"]
  E --> T["5. In-memory test overrides"]
  T --> EFF["EFFECTIVE set + pinnedBy"]
  F -. "settings always honoured" .-> EFF
  F -. "load status" .-> H["/health components.desiredState"]
```

- **Desired** is layers 1–3: what the user asked for. **Effective** is all layers: what is composed. The gap is reported as `pinnedBy`, so the GUI can show pinned toggles as locked.
- The file overrides layers 1–2 per toolset id, only where `enabled` is set (absent = no opinion).
- When `TOOLBOX_TOOLSETS` is set it is authoritative for `enabled` on **every known id** (named on, unnamed off) and all are marked pinned. Settings still come from the file. The `appsettings` list works the same way, and the file overrides it per id.
- An empty `TOOLBOX_TOOLSETS` enables nothing (the startup log says `(none)`).
- No file means layers 1, 2, 4 and 5 give exactly the pre-#31 result; there is no mode switch.
- Unknown ids in the **file** are preserved on save, ignored with a stderr warning, and never fail startup. Unknown ids in the env var or appsettings still throw.

### The desired-state file

| OS | `LocalApplicationData` | File |
|---|---|---|
| Windows | `%LOCALAPPDATA%` | `%LOCALAPPDATA%\ToolBox\desired-state.json` |
| macOS (.NET 8+) | `~/Library/Application Support` | `~/Library/Application Support/ToolBox/desired-state.json` |
| Linux | `$XDG_DATA_HOME`, else `~/.local/share` | `~/.local/share/ToolBox/desired-state.json` |
| Docker | skipped by default | opt in with a volume plus `TOOLBOX_Desired__Path` |

- Model, versioning and store live in `ToolBox.Core/Desired/` (toolset-agnostic, ADR-003); the merge lives in `ToolBox.Host/EnabledResolution.cs`.
- Schema: integer `schemaVersion` plus sequential migrations. A newer version is `Unsupported` and never overwritten.
- `Load()` never throws and never creates the file. `Save()` (called by the admin API, #35, the only writer) writes a temp file in the same directory, flushes, then renames. A corrupt file is copied to `desired-state.json.bad-<UTC>` only when the next save happens.
- Hand-editing is allowed but unsupported. The host never writes the file.
- **Live changes (#32 step 7):** `ToolsetReconcilerService` also reconciles on a file change and on a manual `IToolsetReconciler.RequestReconcile()`. `DesiredStateWatcher` watches the file's directory (so atomic temp-file renames are seen); events are debounced (300 ms, on `TimeProvider`) into one run. `DesiredRosterSource` calls `Load()` then `EnabledResolution.Resolve` on each trigger, so env pins stay authoritative, and a corrupt or unsupported file keeps the last good roster and reports `components.desiredState`. If the directory is missing or the watcher fails, only manual requests apply.

## 4. Ports and endpoints

### Port table

Defined in one place, `src/ToolBox.Host/Ports/PortTable.cs`. Every port and bind address is configuration; there is no automatic reassignment (ADR-016).

| Service | Default port | Default bind | Config keys | Env vars | If the port is taken |
|---|---|---|---|---|---|
| MCP + `/health` (`/mcp`) | 8080 | `127.0.0.1` | `Ports:Mcp:Port`, `Ports:Mcp:Address` | `TOOLBOX_Ports__Mcp__Port`, `TOOLBOX_Ports__Mcp__Address` | **Fail fast:** one stderr line naming the service, port and key, exit code 3 |
| Admin API (#35) | 8095 | `127.0.0.1` (non-loopback is refused at startup) | `Ports:Admin:Port`, `Ports:Admin:Address` | `TOOLBOX_Ports__Admin__Port`, `TOOLBOX_Ports__Admin__Address` | Table entry only until #35 adds the listener and its probe |
| Voxel viewer feed (`/voxel/`) | 8090 | `127.0.0.1` (answers `127.0.0.1` and `localhost`) | `Ports:Viewer:Port`, `Ports:Viewer:Address` | `TOOLBOX_Ports__Viewer__Port`, `TOOLBOX_Ports__Viewer__Address` | **Degrade visibly:** MCP keeps running, `/health` shows `components.viewer = "unavailable: port 8090 in use"` |
| MCP over stdio | none | none | none | none | stdout is reserved for protocol messages; logs go to stderr (ADR-004) |

- Port `0` means "let the OS pick" and skips the probe. Tests use it.
- `0.0.0.0` means all interfaces. Containers set it explicitly in the `Dockerfile` and `docker-compose.yml`, because a loopback socket inside a container is unreachable from published ports (ADR-016, revisiting ADR-012).
- The viewer only exists when Voxel is enabled (ADR-012, ADR-015). Never publish the MCP port beyond a trusted network (ADR-008/011).
- Voxel viewer: `viewer/index.html` connects to `ws://127.0.0.1:8090/voxel/`.
- Desired-state file path: `Desired:Path` (env `TOOLBOX_Desired__Path`), default `LocalApplicationData/ToolBox/desired-state.json`. Not a port, but the other deployment key the Host reads at startup (ADR-017).

### Config precedence (highest wins)

```mermaid
flowchart TB
  P1["1. Test overrideUrl, e.g. http://127.0.0.1:0 (MCP only)"]
  P2["2. --urls / ASPNETCORE_URLS (MCP only, kept for compatibility)"]
  P3["3. Test in-memory settings"]
  P4["4. Command line: --Ports:Mcp:Port=9000"]
  P5["5. TOOLBOX_ env: TOOLBOX_Ports__Mcp__Port"]
  P6["6. appsettings.json"]
  P7["7. PortTable code defaults<br/>Mcp 8080 · Admin 8095 · Viewer 8090 · bind 127.0.0.1"]
  P1 --> P2 --> P3 --> P4 --> P5 --> P6 --> P7
```

Rows 3–7 are ordinary layered .NET configuration and apply to all three services. Rows 1–2 are MCP-only escape hatches; if `ASPNETCORE_URLS` and `Ports:Mcp:*` are both set, the URLs win and a warning is logged.

### Health check and exit codes

`GET /health` returns `{"status","components","version","toolsets","toolsetDetails","uptime"}`. `toolsets` lists only toolsets that are on. `toolsetDetails` is `{id: {desired, effective, actual, reason?}}` built from the reconciler's `ToolsetStatusBoard`; it is kept apart from `components` because there any non-"ok" string means degraded, whereas an `off` toolset is normal and only `failed` degrades (#32 step 8). `status` is `"ok"` when every enabled component is healthy and no toolset failed, and `"degraded"` when an optional component (today: the viewer) is down or a toolset failed. Both are HTTP 200, so the container healthcheck keeps passing. `components` is `{}` when no component reports (for example Voxel disabled). `components.desiredState` appears only when the desired-state file is unusable: `"corrupt: <reason>"` or `"unsupported: schemaVersion N (this build understands 1)"`. A missing or valid file reports nothing; the host falls back to defaults and keeps running (HTTP 200, `degraded`).

| Exit code | Meaning |
|---|---|
| 0 | Clean shutdown |
| 2 | Unknown transport |
| 3 | A required port (MCP) is already in use |

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
| S1 | M3 | Adds a **desired-state file**, a **reconciler**, a loopback **admin API** with a token, a port table, and a background run mode. The roster becomes changeable **while running** | 🔄 reconciler core done (#32: gate, lifecycles, live file watch, `/health` status); admin API (#35), port table and background mode remain |
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

## 7. v2 decisions

These twelve decisions apply to every task in the v2 epic. They are copied from [epic #23](https://github.com/Timothy-Lee-Grant/Tool_Box/issues/23) so agents see them without reading the epic. **The epic is the source of truth; update both together.**

| # | Question | Decision |
|---|---|---|
| 1 | GUI technology | Avalonia; "View world" falls back to the default browser |
| 2 | Where the Host lives | Long-lived background service; the GUI connects to it |
| 3 | "Off" for voxel | Save the world to disk, stop the service |
| 4 | Embedded toolset | Out of the first version |
| 5 | Vector store | SQLite + sqlite-vec by default, pgvector as a second implementation behind the same interface |
| 6 | Embeddings | ONNX in-process |
| 7 | Agent changes its own settings | No, not for now (prompt-injection risk) |
| 8 | Registering with agents | Run the agent's own CLI or show a snippet; no editing agents' config files for now |
| 9 | Copilot target | VS Code Copilot first |
| 10 | Viewer binding in desktop mode | Loopback only |
| 11 | Start with OS | Later (S6) |
| 12 | Project names | ToolBox.Desktop, ToolBox.Rag |

### Decisions from #31 (desired-state file, ADR-017)

- Layering: the file sits between appsettings and the `TOOLBOX_TOOLSETS` pin; the env var is authoritative for `enabled` on every known id, file settings still apply.
- Location: `LocalApplicationData/ToolBox/desired-state.json`, override `TOOLBOX_Desired__Path`; Docker skips it by default.
- Settings are opaque JSON in Core; no per-toolset validation until a toolset needs settings.
- Unknown ids: preserve, warn, never fail. Corrupt file: fall back and report in `/health`. Newer schema: unreadable, never overwritten.
