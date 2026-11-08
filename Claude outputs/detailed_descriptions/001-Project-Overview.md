2026_11_08_14_34-(Project-Overview)

# Tool_Box — Complete Technical Overview (context document for an AI reader)

## 0. Document metadata
| Field | Value |
|---|---|
| Subject | GitHub repository `Timothy-Lee-Grant/Tool_Box` (public) |
| Owner / sole developer | Timothy Grant (GitHub `Timothy-Lee-Grant`) |
| Snapshot date | 2026-11-08 (America/Los_Angeles) |
| Snapshot commits | `next` = `77047b3`; `main` = `9b280bf` (last tag on main: `v1.1.0`). `next` is 5 commits ahead of `main` and 2 behind (the 2 are release/sync merge commits, no content difference) |
| Last release | `v1.1.0` (GitHub Release + GHCR image). Earlier: `v1.0.0`, `v1.0.1`, `v1.0.2` (v1 archive point) |
| In flight at snapshot | PR #48 (issue #31 steps 6–7) open, unmerged |
| Purpose of this document | Give another AI system complete context on the whole project. Topic-specific deep dives are separate documents in the same folder |

---

## 1. What the project is

**Tool_Box is an MCP (Model Context Protocol) server platform written in C#/.NET 10.** It exposes independent, swappable **toolsets** (groups of MCP tools) to AI agents: Claude Code, Claude Desktop, and, over HTTP, other agent systems such as the owner's `LLM_Monitor` project (a LangGraph tool loop). Tagline in the README: "The LLM is the brain; this project is the hands."

### 1.1 Why it exists (non-technical drivers that shape technical decisions)
- **Learning vehicle and portfolio piece.** The owner is preparing for software-engineering roles (target: Microsoft). The repo is linked from his résumé. Consequently:
  - `main` must always build and run (it is what recruiters see). All in-progress work happens on the `next` branch (§9).
  - Every non-trivial decision is recorded as an ADR (§8).
  - Every feature gets a teaching "lecture" document in `Documentation/Learning/`.
  - README media (GIFs/screenshots) matter. Issue #42 plans reproducible capture tooling.
- **Practising AI-assisted engineering.** Development since 2026-11-06 is done through an issue-driven workflow with Claude running in GitHub Actions (§10).

### 1.2 Product direction (v2, in progress)
A cross-platform desktop application, "**Docker Desktop for MCP tools**":
- A GUI where the user switches toolsets on/off and configures each one.
- A long-lived background Host process that any agent (Claude CLI, VS Code Copilot, later others) connects to, automatically seeing only enabled tools.
- Toolset-specific pages (e.g. a "View world" button for the voxel toolset).
- A local RAG toolset (document collections, local embeddings, vector store).
Details: §11.

---

## 2. Repository layout

```
Tool_Box/
├── Directory.Build.props        # repo-wide MSBuild rules: net10.0, Nullable, ImplicitUsings, TreatWarningsAsErrors, <Version>1.1.0</Version>
├── ToolBox.slnx                 # solution (XML .slnx format)
├── Dockerfile                   # multi-stage build → aspnet:10.0 runtime image, HTTP transport
├── docker-compose.yml           # standalone dev environment (ports 8081:8080, 8090:8090)
├── .dockerignore  .editorconfig  .gitignore
├── README.md                    # portfolio-grade README with "what this demonstrates" table, Configuration section
├── CLAUDE.md                    # global instructions auto-loaded by Claude Code (§10)
├── persona.md                   # owner's profile/goals (used as context for AI sessions)
├── src/
│   ├── ToolBox.Core/            # shared plumbing; references no toolset (ADR-003)
│   │   ├── DependencyInjection/ServiceCollectionExtensions.cs
│   │   ├── Desired/             # desired-state model, JSON options, migrator, store (#31)
│   │   ├── Health/ComponentStatusRegistry.cs
│   │   ├── Logging/ToolBoxLogging.cs
│   │   ├── Ports/PortProbe.cs, PortConflictException.cs
│   │   ├── OutputLimiter.cs  ServerInfo.cs  ServerInfoProvider.cs
│   │   ├── ToolsetCatalog.cs  ToolsetCatalogEntry.cs  ToolsetDescriptor.cs
│   ├── ToolBox.Host/            # the executable (composition root)
│   │   ├── Program.cs  ToolBoxHttpApp.cs  ToolBoxServerComposition.cs
│   │   ├── ToolBoxCatalog.cs  ToolsetStartupLogger.cs  appsettings.json
│   │   └── Ports/PortTable.cs, PortSettings.cs
│   └── ToolSets/
│       ├── ToolBox.Basics/      # ping, server_info, current_time
│       ├── ToolBox.Voxel/       # 12 voxel-world tools + viewer broadcast service
│       └── ToolBox.Embedded/    # firmware build tools (STM32 NUCLEO-F401RE); off by default
├── tests/                       # one xUnit project per source project
│   ├── ToolBox.Core.Tests  ToolBox.Basics.Tests  ToolBox.Voxel.Tests
│   ├── ToolBox.Embedded.Tests (+ Fixtures/valid-firmware, broken-firmware)
│   └── ToolBox.Host.Tests       # integration tests booting the real HTTP app
├── viewer/index.html            # Three.js browser viewer for the voxel world (WebSocket client)
├── firmware/nucleo-blink/       # bare-metal reference firmware (Makefile, linker.ld, startup.s, main.c, regs.h)
├── docs/
│   ├── ARCHITECTURE.md          # living architecture doc with mermaid diagrams, port table, roadmap, v2 decisions
│   ├── DECISIONS.md             # append-only ADR log, ADR-001…ADR-016
│   ├── TOOL_CATALOG.md          # every tool, documented
│   ├── AI_WORKFLOW.md           # the issue-driven AI workflow
│   ├── LLM_MONITOR_INTEGRATION.md
│   └── images/                  # README media (castle JPGs, voxel_world.gif; capture method undocumented)
├── Documentation/               # owner's learning record (conventions in Documentation/CONVENTIONS.md)
│   ├── Brainstorms/ (001–004)   # 004 = v2 GUI control center + RAG proposal
│   ├── ImplementationPlans/ (001–006)  # historic staged plans (pre-issue workflow)
│   ├── Learning/ (001–012)      # lectures
│   ├── SkillGapAnalysis/  ProjectCaptures/
├── Claude outputs/              # owner's folder: human_lectures/, detailed_descriptions/ (this file)
├── scripts/setup-github-workflow.sh   # creates labels, milestones, seed issues via gh
├── .claude/                     # Claude Code configuration (§10)
│   ├── settings.json            # shared permissions (allow/deny)
│   ├── agents/planner.md reviewer.md lecturer.md
│   └── skills/voxel/SKILL.md
└── .github/
    ├── workflows/ci.yml  docker_image_release.yml  claude.yml  claude-queue.yml
    ├── ISSUE_TEMPLATE/task.yml epic.yml bug.yml config.yml
    ├── pull_request_template.md
    └── seed-issues/             # bodies used by the setup script
```

---

## 3. Technology stack

| Concern | Choice | Version / detail |
|---|---|---|
| Language/runtime | C# on .NET | `net10.0` (LTS, support to Nov 2028), pinned once in `Directory.Build.props` (ADR-006) |
| MCP implementation | Official C# SDK | NuGet `ModelContextProtocol` 1.4.1 and `ModelContextProtocol.AspNetCore` 1.4.1 (ADR-001) |
| HTTP stack | ASP.NET Core (Kestrel) | via `<FrameworkReference Include="Microsoft.AspNetCore.App" />` in the Host (not `Sdk.Web`) |
| DI / config / logging / hosting | Microsoft.Extensions.* | DI abstractions 10.0.10, Logging.Console 10.0.10, Hosting.Abstractions 10.0.7 |
| Serial (Embedded) | `System.IO.Ports` | 10.0.10 (referenced for future steps; serial tools not implemented) |
| Tests | xUnit | xunit 2.9.3, runner 3.1.4, Microsoft.NET.Test.Sdk 17.14.1, coverlet 6.0.4 |
| Viewer | Three.js in a static HTML page | `viewer/index.html` (~388 lines) |
| Containers | Docker, multi-stage | SDK image `mcr.microsoft.com/dotnet/sdk:10.0` → runtime `mcr.microsoft.com/dotnet/aspnet:10.0` |
| Registry | GHCR | `ghcr.io/timothy-lee-grant/tool_box`, multi-arch `linux/amd64,linux/arm64` |
| CI/CD | GitHub Actions | §9 |
| Firmware toolchain (Embedded only) | `arm-none-eabi-gcc` + `make` | installed in CI via apt; not in the Docker image |
| Build strictness | warnings are errors | `TreatWarningsAsErrors=true` plus `-warnaserror` in CI |

Known local-environment note: the owner's Mac has a .NET 11 **preview** SDK installed and no `global.json` pins SDK 10, so local builds may use the preview SDK while CI uses `10.0.x` (ADR-006 records an earlier incident of this class). A `global.json` is a known to-do.

---

## 4. Architecture

### 4.1 Layering (ADR-003)
```
ToolBox.Host (exe, composition root)
   ├──> ToolBox.Core
   ├──> ToolBox.Basics   ──> ToolBox.Core
   ├──> ToolBox.Voxel    ──> ToolBox.Core
   └──> ToolBox.Embedded ──> ToolBox.Core
```
- **Core never references any toolset** (hard rule, ADR-003). Core defines generic "seam" types: catalog entry, status registry, port probe, desired-state model/store, output limiter, logging rule.
- **Toolsets** are class libraries that reference Core and the MCP SDK. Each exposes:
  - a public `CatalogEntry` (`ToolsetCatalogEntry`), and
  - an `IMcpServerBuilder Add<Name>Toolset(this IMcpServerBuilder)` extension: the single public doorway (ADR-005).
- **Host** is the only project that names toolsets (in `ToolBoxCatalog`) and the only place transports and ports are decided.

### 4.2 Startup sequence (as implemented on `next` @ 77047b3)
1. `Program.cs` builds a *bootstrap* configuration (`appsettings.json` from `AppContext.BaseDirectory` < `TOOLBOX_*` env vars < `--transport` command-line switch) and reads `Transport` (default `stdio`).
2. Switch:
   - `stdio` → `RunStdioAsync`: `Host.CreateApplicationBuilder`, `Logging.UseStderrOnly()`, adds `TOOLBOX_` env vars, inserts the port defaults layer, `AddToolBoxServer(configuration).WithStdioServerTransport()`.
   - `http` → `ToolBoxHttpApp.Build(args).RunAsync()` (with the MCP port pre-flight check, see §6).
   - anything else → one stderr line, **exit code 2**.
3. `ToolBoxServerComposition.AddToolBoxServer(services, configuration)`:
   - `services.AddToolBoxCore()` → `TryAddSingleton(TimeProvider.System)`, `TryAddSingleton<ServerInfoProvider>()`, `TryAddSingleton<ComponentStatusRegistry>()`.
   - `enabled = ToolBoxCatalog.ResolveEnabled(configuration)`; `ToolBoxCatalog.Create().Compose(services, enabled)` → calls `entry.Register(services)` **only** for enabled entries (unknown id → `InvalidOperationException` listing valid ids).
   - Registers `ToolsetStartupLogger` (hosted service logging `Enabled toolsets: basics, voxel` or `(none)` to stderr).
   - Returns `services.AddMcpServer()` (idempotent, proven by test `AddMcpServer_CalledTwice_IsSafe`), so a transport can attach even with zero toolsets.
4. After `Build()`, the DI container is **frozen**: in the current implementation, changing the toolset roster requires a process restart. Runtime toggling is S1 work (§11.3).

### 4.3 Transports (ADR-002, ADR-007)
| Transport | Wire | Notes |
|---|---|---|
| stdio | JSON-RPC over stdin/stdout | Used by Claude Desktop/Code as a launched subprocess. **stdout is reserved for protocol frames; all logs go to stderr** (ADR-004, enforced by `ToolBoxLogging.UseStderrOnly`, which clears providers and sets `LogToStandardErrorThreshold = Trace`) |
| Streamable HTTP | `POST /mcp` (`app.MapMcp("/mcp")`) | `options.Stateless = true` (no session state, no `Mcp-Session-Id`; also means the server cannot push server-initiated notifications such as `tools/list_changed` to HTTP clients). `tools/call` works without a prior `initialize` (verified manually with curl). Responses are `text/event-stream` (`event: message` / `data: {...}`) |
| Health | `GET /health` (plain HTTP, not MCP) | `{"status","components","version","toolsets","uptime"}` (§6.3) |

One binary serves both transports; the toolset roster is identical for both (ADR-007).

### 4.4 Toolset catalog (S0, issue #24, ADR-015)
- `ToolsetCatalogEntry(string Id, string Name, string Description, Action<IServiceCollection> Register)`: Core record.
- `ToolsetCatalog`: case-insensitive dictionary keyed by Id. Constructor rejects null/blank/duplicate ids. `Compose(services, enabledIds)` throws on unknown ids and de-duplicates.
- `ToolBoxCatalog` (Host, `internal static`):
  - `Create()` → `[Basics.CatalogEntry, Voxel.CatalogEntry, Embedded.CatalogEntry]`
  - `DefaultEnabled = ["basics", "voxel"]`
  - `ResolveEnabled(IConfiguration)`: if key `TOOLSETS` (env `TOOLBOX_TOOLSETS`, comma-separated, trimmed, empty entries removed) is non-null it wins (an **empty string enables nothing**); else section `Toolsets:Enabled` (array) if present; else the default.
- Each toolset's `Register` is `services => services.AddMcpServer().Add<Name>Toolset()`.
- ADR-015 rule: **disabled toolsets are never registered.** Their tools, singletons and hosted services do not exist (e.g. with Voxel disabled, port 8090 is never opened). This rule is scheduled to be partially superseded by a new ADR in issue #32 (§11.3).

### 4.5 Configuration keys (complete list as of snapshot)
| Key | Env var | Default | Purpose |
|---|---|---|---|
| `Transport` | `TOOLBOX_TRANSPORT` (also `--transport`) | `stdio` | Transport selection |
| `TOOLSETS` | `TOOLBOX_TOOLSETS` | unset | Comma-separated enabled toolset ids; highest-priority roster source |
| `Toolsets:Enabled` | (array; appsettings) | `["basics","voxel"]` in `appsettings.json` | Baseline roster |
| `Ports:Mcp:Port` / `Ports:Mcp:Address` | `TOOLBOX_Ports__Mcp__Port` / `__Address` | `8080` / `127.0.0.1` | MCP + /health listener |
| `Ports:Admin:Port` / `Ports:Admin:Address` | `TOOLBOX_Ports__Admin__…` | `8095` / `127.0.0.1` | Reserved for the admin API (#35); non-loopback address refused at startup |
| `Ports:Viewer:Port` / `Ports:Viewer:Address` | `TOOLBOX_Ports__Viewer__…` | `8090` / `127.0.0.1` | Voxel viewer WebSocket |
| `ASPNETCORE_URLS` / `--urls` | — | unset | Legacy MCP override; wins over `Ports:Mcp:*` with a logged warning |
| `AllowedHosts` | `AllowedHosts` | (compose sets `localhost;127.0.0.1;toolbox`) | DNS-rebinding defence (ADR-008) |
| — | `TOOLBOX_ALLOW_HARDWARE_ACTIONS` | unset | Embedded: `true`/`1` would enable physical-tier tools (none implemented yet) |
| `Desired:Path` | `TOOLBOX_Desired__Path` | `LocalApplicationData/ToolBox/desired-state.json` | **Planned in PR #48 (not merged at snapshot)** |

Precedence (highest wins) for port settings: test `overrideUrl` (MCP only) > `--urls`/`ASPNETCORE_URLS` (MCP only) > test in-memory settings > command line > `TOOLBOX_` env > `appsettings.json` > `PortTable` code defaults (inserted at configuration source index 0 by `PortTable.AddDefaults`). The port defaults are deliberately **not** duplicated in `appsettings.json` ("one place" rule).

---

## 5. Toolsets in detail

### 5.1 Basics (`ToolBox.Basics`, id `basics`, enabled by default)
| Tool | Behaviour |
|---|---|
| `ping(message?)` | Returns `pong` or `pong: <message>`; whitespace counts as no message; output passes through `OutputLimiter` |
| `server_info()` | `ServerInfo(Version, Toolsets, Uptime)` from `ServerInfoProvider` (assembly version, names of registered `ToolsetDescriptor`s, uptime from an injected `TimeProvider`) |
| `current_time()` | UTC and local ISO-8601 strings plus time-zone id, from injected `TimeProvider` |

### 5.2 Voxel (`ToolBox.Voxel`, id `voxel`, enabled by default)
A live, agent-buildable 3D block world.
- **State:** `VoxelWorld` singleton (ADR-009: one process-wide world, `Dictionary<VoxelCoordinate,string>` of coordinate→material, no locking, no persistence; shared by all connected clients). Raises `Changed` events.
- **Tools (12):** `world_info` (coordinate conventions, ground rule, reference sizes), `list_materials` (12 materials: stone, brick, wood, glass, gold, iron, grass, sand, water, snow, lava, obsidian), `place_block`, `place_box(x1,y1,z1,x2,y2,z2,material)`, `place_cylinder`, `place_cone`, `place_sphere` (ellipsoid via radii), `place_tube` (swept along a path of points), `mirror` (across a plane), `remove_box`, `clear`, `describe_world` (block count + bounding box). Shapes are rasterised server-side by `VoxelRasterizer`. `y = 0` is ground; negative `y` is rejected. Descriptions are written as prompts for the model, enforced by `DescriptionConventionTests` in each toolset's test project.
- **Viewer broadcast:** `VoxelViewerBroadcastService` (hosted service, ADR-010) runs an `HttpListener` WebSocket at `/voxel/` on `Ports:Viewer:Port` (default 8090) and `Ports:Viewer:Address` (default `127.0.0.1`; loopback mode registers prefixes for both `127.0.0.1` and `localhost`; `0.0.0.0` maps to the `http://+:{port}/voxel/` wildcard). It pre-probes the port with `PortProbe` and reports `components.viewer = "ok"` or `"unavailable: …"` to `ComponentStatusRegistry`. If the port is taken, the viewer is disabled with a warning and **MCP keeps running** (graceful degradation, ADR-016). The old 8091–8093 fallback was removed in #37. Agents never talk to this channel; it is one-way to browsers.
- **Viewer page:** `viewer/index.html` (Three.js) connects to `ws://127.0.0.1:8090/voxel/` (hard-coded constant `WS_URL`), shows a live/offline status pill, orbit/zoom/pan controls, and block count/bounds metrics.
- **Agent skill:** `.claude/skills/voxel/SKILL.md` teaches agents the conventions: call `world_info` first, use primitives rather than block-by-block placement, use 3–5 materials, keep builds centred near the origin.

### 5.3 Embedded (`ToolBox.Embedded`, id `embedded`, **disabled by default**)
Firmware troubleshooting for an STM32 NUCLEO-F401RE board. Implementation plan 006 is complete only through step 4 of ~8.
- **Tools (3):** `build_firmware`, `get_build_log`, `clean_build`. They shell out to `make` with `arm-none-eabi-gcc` in a firmware directory.
- `FirmwareDirectory` is resolved by walking up from `AppContext.BaseDirectory` to the folder containing `ToolBox.slnx`, then `firmware/nucleo-blink`. This throws if not found, so the toolset only works from a repo checkout; it is not designed for the container (the Docker image contains neither the firmware nor the toolchain).
- `PhysicalActionGate`: reads `TOOLBOX_ALLOW_HARDWARE_ACTIONS` at construction; future physical-tier tools (flash/GDB debug/serial, steps 5–7) would be registered only when allowed. It also has a single-use, 2-minute confirm-token mechanism (`IssueToken` / redeem) for the future `request_flash` → `flash_firmware(token)` pattern. It takes a `TimeProvider`, plus a test-seam constructor taking the raw env value.
- Note: `AddEmbeddedToolset` constructs `new PhysicalActionGate(TimeProvider.System)` directly during registration (to decide descriptor text) and performs the firmware-directory lookup at registration time. Issue #32's decision moves side effects into a `Start()` lifecycle method.
- Tests: `BuildToolsTests` invoke the real compiler on fixtures and **fail on machines without `arm-none-eabi-gcc`** (including the Claude Code Actions runner). CI installs the toolchain, so CI passes.

### 5.4 Cross-cutting rules for all tools
- **Bounded output:** `OutputLimiter.Limit(text, maxChars = 20_000)` truncates with an explicit marker (`…[output truncated: N more characters. Narrow the request to see the rest.]`), surrogate-pair safe. Rationale: an LLM has a context window, not a scrollbar.
- **Descriptions are prompts**, enforced by reflection tests.
- Tools are plain methods on classes marked `[McpServerToolType]`, testable without any MCP machinery.

---

## 6. Runtime behaviour: ports, health, exit codes, failure handling (issue #37, ADR-016)

### 6.1 Ports
| Service | Default | Bind default | On conflict |
|---|---|---|---|
| MCP + /health | 8080 | 127.0.0.1 | **Fail fast**: one stderr line, e.g. `Port 8080 (MCP) is already in use on 127.0.0.1. Set TOOLBOX_Ports__Mcp__Port (or Ports:Mcp:Port) to another port, or stop the other process.` → **exit code 3** |
| Admin API | 8095 | 127.0.0.1 (non-loopback refused) | table/validation only until #35 |
| Voxel viewer | 8090 | 127.0.0.1 | **Degrade**: viewer off, `/health` status `degraded`, MCP continues |

- `PortSettings.Resolve` validates IPs and ranges and rejects two services on the same non-zero port.
- Detection = pre-flight `PortProbe.Check` (binds a throwaway `TcpListener` on the *same* address as the real bind; port `0` skipped), plus translation of the real bind failure into `PortConflictException` as a backstop for the probe-to-bind race (TOCTOU).
- Port `0` = OS-assigned; used throughout tests (`overrideUrl = "http://127.0.0.1:0"`; `Ports:Viewer:Port=0` triggers `PortProbe.FindFreePort`).
- Default bind changed in #37 from `localhost` to `127.0.0.1` (IPv4 only). `curl localhost:<port>` was verified to still work on macOS.

### 6.2 Containers
Inside a container, loopback-bound sockets are unreachable from published ports. So the Dockerfile and compose set `TOOLBOX_Ports__Mcp__Address=0.0.0.0` and `TOOLBOX_Ports__Viewer__Address=0.0.0.0` explicitly. `ASPNETCORE_URLS` was **removed** from the Dockerfile in #37 because it would outrank `Ports:*`.

### 6.3 Health
`GET /health` → HTTP 200 always while the process runs:
```json
{"status":"ok|degraded","components":{"viewer":"ok"},"version":"1.1.0.0","toolsets":["Basics","Voxel"],"uptime":"00:00:05.1234567"}
```
`status` is `degraded` if any component's status is not `ok`. `components` is `{}` when nothing reports (e.g. Voxel disabled). `version` is the assembly version from `<Version>` in `Directory.Build.props` (1.1.0 → `1.1.0.0`). The compose healthcheck uses `curl -f`, so `degraded` still passes.

### 6.4 Exit codes
`0` clean shutdown · `2` unknown transport · `3` required (MCP) port in use.

---

## 7. Testing
| Project | Approx. test methods (`[Fact]`/`[Theory]`, snapshot) | Covers |
|---|---|---|
| ToolBox.Core.Tests | 54 | OutputLimiter, ServerInfoProvider (fake clock), ToolsetCatalog, PortProbe, ComponentStatusRegistry, DesiredState model/migrator/store |
| ToolBox.Basics.Tests | 13 | tools, catalog entry, description conventions |
| ToolBox.Voxel.Tests | 52 | world, rasterizer, tools, catalog entry, conventions |
| ToolBox.Embedded.Tests | 17 | build tools (real compiler), PhysicalActionGate (fake clock), conventions, catalog entry |
| ToolBox.Host.Tests | 31 | real Kestrel app over HTTP with the SDK's MCP client (`HttpServerFixture`), toolset selection, port settings/precedence, port conflicts, health components |

Patterns:
- `TimeProvider` fakes instead of sleeps.
- DI-container inspection (`IServiceCollection`, `IHostedService` registrations) instead of booting where possible.
- Port `0` everywhere.
- A non-parallel xUnit collection `VoxelViewerCollection` (`DisableParallelization = true`) for classes that start the viewer.
- Tests wait for the viewer's status because `BackgroundService.ExecuteAsync` may not have run when `StartAsync` returns. This was a latent race found in #37.
- Tests pass `settings` overrides to `ToolBoxHttpApp.Build`; desired-state tests use per-test temp directories.

---

## 8. Architecture Decision Records (`docs/DECISIONS.md`, append-only)
| ADR | Decision (short) |
|---|---|
| 001 | C# with the official ModelContextProtocol SDK |
| 002 | stdio transport first, HTTP later |
| 003 | Host / Core / Toolsets layout; Core never references a toolset; "abstract from evidence, not imagination" (no plugin loader) |
| 004 | stderr-only logging (stdout is the protocol in stdio mode) |
| 005 | Toolsets register via one `IMcpServerBuilder` extension each |
| 006 | Target net10.0, pinned centrally; lesson: an SDK can compile a target it cannot run |
| 007 | Single binary, dual transport; precedence `--transport` > env > appsettings; HTTP stateless |
| 008 | HTTP security v1: unauthenticated, network-isolated; `AllowedHosts` pinned; never publish the MCP port beyond a trusted network |
| 009 | Voxel world is a single global singleton (multi-tenant state deferred) |
| 010 | Toolsets may register companion `IHostedService`s (viewer) |
| 011 | Supersedes ADR-008 item 4: isolation, not read/write tool classification, is the real control |
| 012 | Viewer bound all interfaces; viewer port exposed independently of MCP (*superseded in part by 016*) |
| 013 | Issue-driven AI workflow; the GitHub issue is the task's memory |
| 014 | v1 archived as tag `v1.0.2` + GitHub Release (no legacy folder); v2 evolves in place |
| 015 | Toolsets composed from catalog + config; disabled toolsets never registered |
| 016 | Bind address is configuration, loopback by default; port conflicts fail fast (exit 3); optional viewer degrades visibly |
| *next free* | 017 is expected from #31 (desired-state file). A "registration is cheap; startup is gated" ADR is expected from #32 (number assigned when written, per CLAUDE.md rule 7) |

---

## 9. Branching, CI/CD and releases

### 9.1 Branch model
- `main`: released, always working, linked from the résumé. Receives only reviewed merges from `next` (merge commits). Workflow-file changes must land on `main` because GitHub runs `issue_comment`/`issues` workflows from the default branch.
- `next`: integration branch for v2. All agent PRs target it.
- `claude/issue-<n>-<timestamp>`: per-task branches created by the Claude action.
- Merges use **merge commits** (history preserved), not squash.

### 9.2 Release policy
Semantic versioning. **Tag at the end of a milestone** (v1.1.0 after S0; planned v1.2.0 after S1, v2.0.0-alpha after S2, v2.0.0 after S6). Untagged `next`→`main` merges are allowed between tags when `main`'s README/demo benefits. The version in `Directory.Build.props` changes only at tagged releases.

### 9.3 Workflows
| File | Trigger | Does |
|---|---|---|
| `ci.yml` | every push and PR (concurrency per ref, cancel-in-progress) | Job `build-and-test`: setup .NET 10.0.x, apt install `gcc-arm-none-eabi make`, `dotnet restore`, `dotnet build --no-restore -c Release -warnaserror`, `dotnet test --no-build -c Release`. Job `docker`: `docker build`, run with `-p 8080:8080`, poll `GET /health` up to 30 s, teardown |
| `docker_image_release.yml` | push to `main`; tags `v*.*.*` | Buildx + QEMU, multi-arch push to GHCR; `latest` on main, semver tags on version tags |
| `claude.yml` | `issue_comment`, `pull_request_review_comment`, `pull_request_review`, `issues` (opened/labeled/assigned) | Runs `anthropics/claude-code-action@v1` (§10.2) |
| `claude-queue.yml` | `workflow_dispatch` only (cron commented out) | Agent-mode queue runner: picks the oldest `claude:queued` issue |

### 9.4 Dockerfile summary
- Build stage: copy `Directory.Build.props` and each csproj individually (layer caching), `dotnet restore` of the Host, copy `src/`, `dotnet publish -c Release -o /app --no-restore`. Every new project's csproj must be added to the COPY list, or restore fails; this happened once when Embedded was merged.
- Runtime stage: `aspnet:10.0`; apt mirror rewritten to `azure.archive.ubuntu.com` (observed CI reachability issue) and `curl` installed for healthchecks; `ENV TOOLBOX_TRANSPORT=http`, `TOOLBOX_Ports__Mcp__Address=0.0.0.0`, `TOOLBOX_Ports__Viewer__Address=0.0.0.0`; `EXPOSE 8080 8090`; non-root `USER $APP_UID`; `ENTRYPOINT ["dotnet","ToolBox.Host.dll"]`.
- `.dockerignore` excludes tests, docs, Documentation, .git, .github, *.md, bin/obj.
- Compose: build `.`, container `toolbox`, `8081:8080` (MCP; Inspector uses `http://localhost:8081/mcp`), `8090:8090` (viewer), explicit bind env vars, `AllowedHosts`, curl healthcheck.

---

## 10. AI-assisted development workflow (issue-driven)

### 10.1 Concept
Since 2026-11-06 (ADR-013), work is done by Claude through GitHub. **The GitHub issue (body + all comments) is the agent's task memory.** `CLAUDE.md` holds only global facts. Memory layers:
- `CLAUDE.md` (always loaded)
- issue thread (per task)
- ADRs (durable decisions)
- `Documentation/CONVENTIONS.md` (loaded only when writing docs)
- `.claude/agents/*` (subagents)
- `.claude/skills/*` (procedures)

### 10.2 `claude.yml` details
- Job-level `if:` requires the commenter/issue author to be `OWNER` (`author_association`) and the text to contain `@claude`, or an issue to be labeled `claude` by the repo owner. Edited comments are ignored (only `created`). Bot replies are filtered out, which prevents self-trigger loops; they appear as "Skipped" runs.
- Concurrency group per issue/PR number, no cancellation.
- Permissions:
  - `contents: write`, `pull-requests: write`, `issues: write`: push work branches, open/update PRs, comment and label
  - `id-token: write`: the action's GitHub App authentication
  - `actions: read`: read CI results
- Steps: checkout (`fetch-depth: 0`), setup .NET 10, `anthropics/claude-code-action@v1` with:
  - `claude_code_oauth_token: ${{ secrets.CLAUDE_CODE_OAUTH_TOKEN }}` (subscription token)
  - `label_trigger: claude`, `base_branch: next`, `branch_prefix: claude/`
  - `additional_permissions: actions: read`
  - `claude_args`: `--max-turns 40` and an `--allowedTools` allowlist: Read/Write/Edit/Glob/Grep/Task; `dotnet restore|build|test`; `git status|diff|log|add|commit|checkout|switch|fetch|merge|branch|mv|rm|push|show|ls-tree|rev-parse|merge-base|tag --list`; `gh issue view|list|create|comment|edit`; `gh label list`; `gh pr view|diff|create|list`; `gh run list|view`; `gh release view|list`
- Default deny: unlisted commands are refused (no human present to approve).
- The Claude GitHub App cannot modify `.github/workflows/*` (no `workflows` permission). The owner edits workflow files by hand via PRs to `main`.
- Comment-triggered runs commonly end with a pre-filled "Create PR" link rather than opening the PR. Multi-line `gh pr create` bodies are sometimes blocked by the sandbox; inline `--body` works.
- Issue-triggered runs create a fresh branch from `next`; PR-comment-triggered runs work on the PR's branch.
- Observed transient failure: HTTP 504 downloading Bun during action setup; fixed by re-running.

### 10.3 Process state machine (labels)
`stage-1-design` (clarify only) → `stage-2-discussion` (options, no code) → `stage-3-plan` (numbered plan via `planner` subagent; no code) → `stage-4-implement` (only approved steps; small PRs to `next`) → `stage-5-verify` (evidence, `reviewer` subagent). No stage label = discussion.

Other labels:
- type: `type:task`, `type:epic`, `type:bug`
- hand-off: `claude` (hand to Claude), `claude:queued`, `claude:working` (queue runner)
- blocking: `needs-timothy`

Milestones: M0 (cleanup, closed), M1 (direction, closed), M2 (S0, closed), M3 (S1, open), M4–M8 planned.

### 10.4 Rules in `CLAUDE.md` (summary)
- Never commit/push/PR to `main`; PRs target `next`.
- Read the whole issue first.
- Obey the stage label.
- End every session with a `## Session notes` comment that includes "System state now".
- When blocked, comment, add `needs-timothy` and stop.
- Let CI test:
  - build once, then read CI via `gh run list --branch <b>` / `gh run view <id> --log-failed`, not `gh pr checks`
  - if CI is still running, say so and stop
- Small PRs with `Part of #n` / `Closes #n`.
- ADRs for lasting decisions; the number is assigned when the ADR is written.
- Mention new concepts for the owner. Lectures only via the `lecturer` subagent when asked.
- Rule 9 ("show, don't tell"):
  - every plan and PR has **Where this fits**, a **Before → After** mermaid diagram and plain-language explanations
  - structural PRs update `docs/ARCHITECTURE.md`
  - user-facing changes update `README.md`

### 10.5 Subagents and settings
- `.claude/agents/planner.md`: read-only; produces "Stage 3 plan" (goal, where this fits, approach, Before→After, steps with verification, risks, ADR needed, new concepts).
- `.claude/agents/reviewer.md`: read-only independent review; checks acceptance criteria, ADR-003/004/005 compliance, tests, docs, diagram/diff consistency, target branch, CI.
- `.claude/agents/lecturer.md`: writes `Documentation/Learning/NNN-*.md`.
- `.claude/settings.json`: local permissions (allow build/test/read-only git/gh; deny `git push origin main` and force pushes). `.claude/settings.local.json` is gitignored.

### 10.6 Human verification practice
The owner verifies user-visible behaviour by hand before merging:
- Docker runs: `docker build`, `docker run -e TOOLBOX_TOOLSETS=basics`, `curl /health`.
- Port-conflict checks with `nc -l`.
- Raw MCP calls with curl.
- Live agent builds via `claude mcp add --transport http toolbox http://127.0.0.1:<port>/mcp` while watching the viewer.

---

## 11. Roadmap and current work

### 11.1 v2 decisions (epic #23; mirrored in `docs/ARCHITECTURE.md` "v2 decisions")
| # | Topic | Decision |
|---|---|---|
| 1 | GUI technology | Avalonia (cross-platform .NET); "View world" may fall back to the default browser |
| 2 | Where the Host lives | Long-lived background service; GUI connects to it |
| 3 | Voxel "off" | Save world to disk, stop the service (S1 keeps it in memory; S3 adds disk persistence) |
| 4 | Embedded toolset | Out of the first v2 version |
| 5 | Vector store | SQLite + sqlite-vec default; pgvector as second implementation behind the same interface |
| 6 | Embeddings | ONNX Runtime in-process |
| 7 | Agent changes its own settings | No (prompt-injection risk); control plane separate from data plane |
| 8 | Registering with agents | Run the agent's own CLI or show a snippet; no editing agent config files for now |
| 9 | Copilot target | VS Code Copilot first |
| 10 | Viewer binding in desktop mode | Loopback only |
| 11 | Start with OS | Later (S6) |
| 12 | Project names | `ToolBox.Desktop` (GUI), `ToolBox.Rag` |

Also decided: cross-platform target; RAG at roughly 30 documents and fully local; supported doc types proposed: txt/md/code first, then pdf/html, then docx.

### 11.2 Phases (Brainstorm 004 §7.2) and milestones
| Phase | Milestone | Content | Status |
|---|---|---|---|
| S0 | M2 | Toolset catalog + config | ✅ released v1.1.0 |
| S1 | M3 | Desired-state file, reconciler with runtime on/off, lifecycle contract, `list_changed`, loopback admin API + token, port table, background run mode | in progress |
| S2 | M4 | Avalonia GUI shell: tray, home toggles, per-toolset pages, RAG "coming soon" tab, Claude registrar | planned |
| S3 | M5 | Voxel page, "View world", world persistence | planned |
| S4 | M6 | Copilot registrar | planned |
| S5 | M7 | Real RAG (`IVectorStore`, `IEmbeddingProvider`, ingestion, collections, drag-drop) | planned |
| S6 | M8 | Installers per OS, start-with-OS, v2.0 | planned |

### 11.3 M3 (S1) issue status at snapshot
| Issue | Topic | State |
|---|---|---|
| #37 | Port table + conflict detection | ✅ done (PRs #40, #41, #43; lecture #44; ADR-016) |
| #31 | Desired-state file | `stage-4-implement`; PR A (#46, model + migrator) and PR B (#47, load + atomic save) merged; **PR C (#48, merge into enabled-resolution + Host wiring + /health) open**; PR D (docs + ADR-017) and lecture pending |
| #32 | Reconciler core | Discussion concluded: **Option B**, register everything and gate at runtime: `IToolsetLifecycle` Start/Stop, one gate choke point for `tools/list`, `tools/call` and hosted services; disabled tool calls return an explicit "toolset is off" error; side effects (Embedded firmware lookup, viewer bind) move into `Start()`; Voxel "off" keeps the world. Requires a new ADR superseding ADR-015 in part ("registration is cheap and side-effect-free; startup is gated"). `list_changed` not required for first merge. Awaiting `stage-3-plan` after #31's plan |
| #33 | Lifecycle contract / meaning of "off" | stage-2 |
| #34 | Emit `tools/list_changed` | stage-2 (note: HTTP stateless mode cannot push; stdio can) |
| #35 | Loopback admin API (control plane, port 8095, not exposed as MCP tools) | stage-2 |
| #36 | Admin API token auth (+ Origin/Host checks vs DNS rebinding) | stage-2 |
| #38 | Background host run mode | stage-2 |
| #42 | Reproducible README media (Playwright capture of scene scripts → screenshots/GIF; `to-gif` helper; `docs/MEDIA.md`) | stage-1 |

Rejected alternatives for #32:
- **A**: rebuild the inner server per change. Loses Voxel state on every rebuild, drops stdio sessions, fights the SDK.
- **C**: one host/process per toolset. Contradicts ADR-007 and is the highest complexity. It remains possible later through the same lifecycle seam.

### 11.4 #31 design (decided; partly implemented)
- **Model (implemented)**, Core `ToolBox.Core.Desired`:
  - `DesiredState { int SchemaVersion = 1; Dictionary<string, DesiredToolset> Toolsets (case-insensitive) ; [JsonExtensionData] Extra }`
  - `DesiredToolset { bool? Enabled (null = no opinion); Dictionary<string, JsonElement>? Settings (opaque); [JsonExtensionData] Extra }`
  - Shared `DesiredStateJson.Options`: web defaults, indented, comments and trailing commas tolerated on read, nulls omitted.
- **Migrator (implemented):** `DesiredStateMigrator` on `JsonNode` with a sequential step table (empty at v1).
  - Outcomes `Ok`, `Corrupt` (missing / non-integer / <1 version), `Unsupported` (newer than the build).
- **Store (implemented):** `DesiredStateStore(path?)`; `DefaultPath()` = `LocalApplicationData/ToolBox/desired-state.json`. On .NET 8+ that resolves to `%LOCALAPPDATA%` (Windows), `~/Library/Application Support` (macOS) and `$XDG_DATA_HOME` or `~/.local/share` (Linux).
  - `Load()` never throws, never writes; statuses `Missing | Ok | Corrupt | Unsupported`.
  - `Save()` is the only writer:
    - process-wide lock; temp file in the same directory, then flush, then `File.Move(overwrite: true)`; 0600 permissions on Unix
    - refuses (`RefusedUnsupported`) if the existing file is from a newer build
    - copies a corrupt file aside to `desired-state.json.bad-<UTC timestamp>` before writing
    - returns `Failed` without crashing on IO errors
- **Layering (PR #48, pending):**
  - Precedence: code default < appsettings < **desired-state file** < `TOOLBOX_TOOLSETS` (operator pin).
  - When the env var is set, it is authoritative for `enabled` on every known id (named on, unnamed off), with `pinnedBy` reported; file settings are still honoured.
  - The resolution yields Desired vs Effective.
  - Unknown ids in the file are preserved on save and ignored with a warning (unlike unknown ids in env/appsettings, which fail startup).
  - No file means behaviour identical to v1.1.0.
  - A corrupt or unsupported file causes a fallback to lower layers plus `components.desiredState = "corrupt: …"` / `"unsupported: …"` and `status: degraded`; a missing file is absent from components.
  - Path override: `Desired:Path` / `TOOLBOX_Desired__Path`.
  - Docker skips the file by default (volume opt-in documented).
  - Writers: only the GUI/admin API in future; hand-editing is allowed but unsupported; the host never creates or "fixes" the file.

---

## 12. Security posture
- MCP HTTP endpoint is **unauthenticated**. Protection is network isolation:
  - loopback binding by default (ADR-016)
  - never publish the MCP port beyond a trusted network (ADR-008/011)
  - consuming composes (LLM_Monitor) publish no MCP port
  - `AllowedHosts` pinning against DNS rebinding
- The viewer channel is receive-only and display-only; exposing it carries no tool-execution capability (ADR-012 reasoning).
- Planned admin API: loopback-only (non-loopback address refused at startup), per-install token, separate port from MCP (control plane vs data plane).
- Agents may not reconfigure the system (decision 7).
- Embedded hardware actions require an explicit env opt-in and, for flashing, a single-use confirm token (design; not yet implemented).
- CI/agent security:
  - owner-only triggers
  - tool allowlist (default deny)
  - no workflow-edit permission for the agent
  - human review before merge
  - prompt-injection risk acknowledged: the agent reads all issue comments on a public repo

---

## 13. Known gaps, inconsistencies and tech debt (snapshot)
1. `docs/ARCHITECTURE.md` §4 says the viewer page connects to `ws://localhost:8090/voxel/`; the actual constant in `viewer/index.html` is `ws://127.0.0.1:8090/voxel/`.
2. No `global.json`; local preview SDK 11 vs CI SDK 10.
3. The owner's local NuGet configuration referenced an external volume (`/Volumes/T7`) and failed with permission denied on 2026-11-08 (local environment issue, not repo).
4. Embedded tests require `arm-none-eabi-gcc`; they fail in the Claude Actions runner (which does not install it), causing noise in agent reports.
5. Embedded registration has side effects (firmware-dir lookup, gate construction); scheduled to move to `Start()` (#32).
6. The DI container is frozen after `Build()`; runtime toggling is not yet possible (S1).
7. HTTP transport is stateless, so `tools/list_changed` can't be pushed to HTTP clients.
8. README media in `docs/images/` was captured manually; the method is undocumented (#42).
9. CI's Docker smoke test does not assert `components` (requires a workflow edit by the owner).
10. MCP HTTP has no authentication (by design; trusted networks only).
11. IPv4-only default bind (`127.0.0.1`); IPv6-only `localhost` resolution would fail (judged rare; ADR-016 notes it).
12. Agent-generated status reports have occasionally contained unverified claims (e.g. an issue "already closed", the viewer URL host). The owner verifies claims against source and GitHub.

---

## 14. Glossary (project-specific)
| Term | Meaning here |
|---|---|
| Toolset | A class library exposing a group of MCP tools plus its own services, registered through one `CatalogEntry` |
| Catalog / catalog seam | `ToolsetCatalog` + per-toolset `CatalogEntry`; the S0 mechanism that turned the hard-coded roster into data + configuration |
| Composition root | The Host's `ToolBoxServerComposition` / `ToolBoxCatalog` / `PortTable`, the only place choices are wired |
| Desired state | User intent (which toolsets on, their settings), persisted in `desired-state.json` |
| Effective state | What actually gets composed after operator pins (env var) are applied |
| Reconciler | Planned S1 loop that makes running toolsets match desired state |
| Gate | Planned single choke point that hides/blocks disabled toolsets at `tools/list`, `tools/call` and hosted services |
| Control plane / data plane | Admin API + GUI (configuration) vs MCP (tool calls by agents) |
| `next` | Integration branch; `main` is the released, résumé-facing branch |
| Stage labels | `stage-1-design` … `stage-5-verify`: the per-issue process state machine |
| S0–S6 / M2–M8 | Roadmap phases and their GitHub milestones |
| Session notes | Structured end-of-run comment by the agent; the hand-off memory between agent sessions |

---

## 15. Instructions for the reading AI
- Treat §0's snapshot as the point in time. Anything marked planned, pending or "PR #48 open" may have changed since. Ask the user for the current state if it matters.
- Distinguish implemented facts (§4–§9, §11.4 "implemented") from decisions not yet built (§11).
- Respect the project's hard constraints when proposing changes:
  - Core must not reference any toolset (ADR-003)
  - stdout is protocol-only in stdio mode (ADR-004)
  - `main` must always work; changes go via `next`
  - every lasting decision needs an ADR
  - tool output must be bounded
  - tool descriptions are prompts
  - warnings are errors
- When asked for designs, prefer evolving existing seams (catalog, lifecycle, status registry, configuration layering) over rewrites; the owner explicitly chose "evolve in place" (ADR-014).
- The owner is learning. Explanations that include *why* and trade-offs are preferred over bare answers.
- Do not assume details not stated here (e.g. exact tool parameter lists beyond those given, internal method bodies). Say when something would need checking against source.
