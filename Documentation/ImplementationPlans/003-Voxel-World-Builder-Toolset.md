2026_07_19_10_00-(Voxel-World-Builder-Toolset)

# Implementation Plan 003 — Voxel World Builder Toolset

Goal of this plan: give Tool_Box its first **stateful, live-rendered** toolset — a voxel world an agent builds in, viewable in real time in a browser. Where plans 001–002 built the platform and proved it could carry a transport, this plan proves it can carry *domain state* — a capability every future creative/visual toolset will also need.

*(Note: this plan originally also covered a SPICE circuit-design toolset. Split out to `004-SPICE-Circuit-Designer-Toolset.md` on 2026-07-20 — see the Stage 2 discussion below for why. This document is voxel-only from here on.)*

---

# Stage 1 (Design Documentation)

*Timothy's goals, as stated 2026-07-19:*

**The Category A idea from `Documentation/Brainstorms/003-TOOLSET_IDEAS.md`** — "Creative & Visual (the demo stars)." Timothy specifically called out this category as the one that "provides a good amount of ability to showcase something visual which will impress an interviewer and be easy to demonstrate and display."

Timothy asked for an in-depth document covering how to accomplish it, its feasibility, and how it fits within the existing project architecture.

---

# Stage 2 (Discussion)

**[2026-07-19, AI]** Opening position (originally written alongside a SPICE toolset section, now trimmed to voxel-only — see the 2026-07-20 entry below for the corrections that followed).

## 2.1 The one thing this toolset needs that Basics never did

Every toolset shipped so far (`Basics`) is **stateless**: each tool call is a pure function of its arguments plus ambient server info (`ServerInfoProvider`, `TimeProvider`). The voxel world breaks that: it persists across `place_box`, `mirror`, `clear`, etc. `ping`/`current_time`/`server_info` never needed to remember anything between calls; this toolset's whole point is that it does.

That gap belongs at the **Core** layer as a pattern other future stateful toolsets can reuse, the same reasoning that put `OutputLimiter` and `ToolsetDescriptor` in Core rather than in Basics (ADR-003, ADR-005) — not a one-off hack inside the voxel toolset.

### 2.1.1 A stateful-toolset pattern

Register world state the same way `ServerInfoProvider` is registered today — as a singleton service the toolset's tool class takes in its constructor:

```csharp
// Core/DependencyInjection convention, generalized from ADR-005:
public static IMcpServerBuilder AddVoxelToolset(this IMcpServerBuilder builder)
{
    builder.Services.AddSingleton<VoxelWorld>();   // <-- new: stateful singleton
    builder.Services.AddToolsetDescriptor(name: "Voxel", description: "...");
    return builder.WithTools<VoxelTools>();
}
```

This is the simple answer, and section 2.7 below (2026-07-20) confirms it's also the *proven* answer — see the note on how a working reference implementation handles this identically. One global world, shared by every connected client, is fine for stdio (one client, one process) and is an explicit, documented simplification for HTTP (where two simultaneous agents would edit the same world). Deferred rather than solved now: session-scoped state, per "abstract from evidence, not imagination" (the same principle ADR-003's discussion applied to the plugin loader).

## 2.2 What it is

From the brainstorm (A1): tools that describe *form*, not coordinates — `place_box`, `place_cylinder`, `place_cone`, `place_sphere`, `place_tube`, `mirror`, `remove_box`, `describe_world`, `clear`, `world_info` — plus a browser viewer that renders the world live as the agent works.

## 2.3 Data model

A voxel world is a sparse 3D grid. Dense arrays don't scale to anything interesting (a 200×200×200 world is 8M cells for what might be a few thousand occupied blocks):

```csharp
public sealed class VoxelWorld
{
    // Sparse: only occupied cells are stored.
    private readonly Dictionary<(int X, int Y, int Z), string> _blocks = new();

    public event Action<WorldEvent>? Changed;   // the viewer's broadcast hook
    ...
}
```

*(2026-07-20 correction: originally specified as `ConcurrentDictionary` "for safety." Walked back to a plain `Dictionary` — see 2.7. A single active session processes tool calls one at a time; there's no concurrent writer to protect against yet.)*

## 2.4 Server-side rasterization (the actual interesting logic)

This is the part of the brainstorm worth taking seriously: `place_sphere(center, radius, block_type)` must expand into the *set* of voxel coordinates inside that sphere. These are standard, well-documented algorithms — not research:

- **Box**: trivial triple loop over the bounding coordinates.
- **Sphere**: iterate the bounding cube, keep points where `dx²+dy²+dz² ≤ r²` (or a boundary-only variant for a hollow shell).
- **Cylinder/cone**: iterate by height layer; at each layer, rasterize a 2D circle (Bresenham's midpoint circle algorithm, extended to filled or hollow) with a radius that's constant (cylinder) or interpolated (cone).
- **Tube**: swept spheres/circles along a path, radius interpolated along the path — the primitive for organic, curved forms.
- **Mirror**: reflect existing world coordinates across an axis/plane — a coordinate transform over the block set, no new geometry math.

None of this requires a library; it's a few hundred lines of integer math, and it is exactly the kind of "prove you understand the primitive before reaching for a package" exercise that fits the learning goal of this project.

## 2.5 The live viewer — where does it actually live?

*(2026-07-20: this section's original two options are superseded by 2.7 below, which found a concrete, simpler precedent. Kept here for the record of how the thinking evolved; skip to 2.7 for the current answer.)*

Original framing: the viewer needs (a) a way to serve an HTML/Three.js page, and (b) a WebSocket the page can subscribe to for world-diff pushes, and the open question was whether that infrastructure should ride on the MCP HTTP transport (only exists in HTTP mode) or live independently as companion infrastructure the toolset brings itself (works under any transport). The recommendation was the latter — confirmed correct by 2.7, though "companion infrastructure" turned out to mean something much lighter-weight than a second Kestrel instance.

## 2.6 Effort and the LLM_Monitor tie-in

Brainstorm estimate: Medium (1–3 weeks). Rough breakdown: state model + rasterization math (S), tool layer + descriptions (S), viewer broadcast + static page (S–M, see 2.7 — lighter than originally scoped), Three.js frontend (S if kept to instanced cubes, no textures/lighting fanciness).

The brainstorm's "twist" — routing this through LLM_Monitor's own LangGraph agent, not just Claude Desktop — costs **nothing extra** given plan 002's work: LLM_Monitor already consumes any Tool_Box toolset generically via `langchain-mcp-adapters` (`docs/LLM_MONITOR_INTEGRATION.md`). Once `AddVoxelToolset()` is wired into the Host's composition, "my agent, my tools, my infrastructure, end to end" is just running a "build me a castle" prompt through LLM_Monitor and pointing a browser at the viewer.

---

## 2.7 [2026-07-20] Grounding the design in a real reference implementation

**[2026-07-20, Timothy]** Raised several questions before committing to the above: whether the design was over-complicated relative to a known-working example (`techleadhd/blockworld` on GitHub, which the brainstorm's A1 idea was itself inspired by); whether that project's use of Claude Code Skills was worth adopting here too; whether the viewer needs its own Docker container or a build script to start everything; a suspicion that the AI in that project is only ever responsible for placement, with the viewer entirely decoupled — and a question about how the LLM would then ever learn the world's current state; a concern about stdio cleanliness now that Timothy's LLM_Monitor project is building this repo's Dockerfile directly into its own compose network; and a proposal to split this plan in two, since bundling voxel and SPICE together had been driven by a "let's build in scalability" instinct rather than the toolsets actually being related.

**[2026-07-20, AI]** Cloned and read `blockworld` (`server.js`, `viewer/index.html`, `package.json`, `.claude/skills/**`) rather than answer from memory. Findings, and what they change here:

**State (confirms 2.1.1, simplifies 2.3).** The reference implementation's entire world state is one line — `const blocks = new Map()` — a plain, non-thread-safe, in-process map. No database, no persistence, no session scoping. It survives only as long as the server process runs. This is the same singleton pattern already proposed above; the only correction is dropping `ConcurrentDictionary` in 2.3 in favor of a plain `Dictionary`, since the reference implementation didn't need thread-safety machinery either — a single MCP session processes tool calls serially, so there is no concurrent writer to protect against. Matches this project's own "don't add validation/robustness for scenarios that can't happen" discipline.

**Skills — adopt them.** The reference project ships `.claude/skills/blockworld/SKILL.md` plus two "style" skills (`castle`, `dragon`). This is the same Claude Code Skills mechanism already available in this environment (`.claude/skills/*/SKILL.md`, `name`/`description` frontmatter) — not a separate integration to design, just an artifact to write. Its core skill teaches scale discipline (call `world_info` first; the tool signatures alone can't tell the agent whether a block is 10cm or 10m), a materials/primitive cheat sheet, and a hard budget ("150–400 tool calls; if you're looping `place_block` more than ~30 times, you reached for the wrong primitive"). **Decision: Tool_Box's voxel toolset ships a `.claude/skills/voxel/SKILL.md` alongside the tools**, written the same way — this is cheap, and it's real prompt-engineering-as-a-shipped-artifact rather than an afterthought.

**No Docker, no build script — rendering is client-side.** This was the main place the original design overreached. The reference implementation has zero containers. It's two plain OS processes: (1) the MCP server, spawned directly by the AI client via stdio exactly like Tool_Box's Host is today, which *also* opens a raw WebSocket listener on `:8080` in the same process purely to broadcast state diffs; (2) the viewer, which is `npx serve .` — a generic static-file server with no application logic — serving a page that hardcodes `ws://localhost:8080` and does all rendering itself via Three.js/WebGL in the browser. **No process ever renders a pixel server-side; a browser tab does, via the GPU.** For Tool_Box: no container is needed for the voxel toolset's local/demo path at all. Docker stays scoped to what plan 002 actually built it for — the HTTP-transport path for LLM_Monitor consumption — entirely orthogonal to this toolset's viewer.

This also resolves 2.5 concretely: the WebSocket broadcast starts **inside the Host process itself**, the moment the voxel toolset registers — not a second Kestrel instance (as originally proposed), just a `System.Net.HttpListener` with `AcceptWebSocketAsync` (built into the BCL, no new package, mirroring the reference project's one small dependency, `ws`). The static viewer page doesn't need C# code to serve it either — point `npx serve`, Python's `http.server`, or even a browser's `file://` directly at it, exactly as the reference project does. Lighter than the "companion `IHostedService` running its own Kestrel" design in the original 2.5 — same principle (works under any transport), less machinery.

**The LLM never sees the viewer — confirmed, and it simplifies scope.** Timothy's suspicion was exactly right. The WebSocket/viewer channel is one-directional, server → browser, for human eyes only. The agent's only channel back is the plain-text return value of each tool call — `place_box` returns `"placed 42 stone"`; `describe_world` returns a block count and bounding box as a string. No image content, no vision loop, ever. Concretely, this means the original document's "Spike 1 — rich content from `[McpServerTool]`" is **not needed for this toolset** — that spike only matters for a future toolset that needs to return images (e.g., SPICE's schematic/waveform exports, or a hypothetical vision-critique toolset). Removed from this plan's scope.

**stdio cleanliness and the LLM_Monitor container — not a risk, if HTTP transport is what's actually wired up.** The stdout-purity rule (ADR-004) is scoped to the stdio transport specifically — it's about a literal shared pipe between a parent process and a spawned child's stdin/stdout. Plan 002's HTTP transport exists precisely so container-to-container consumption doesn't need that pipe: LLM_Monitor's LangGraph service talks to Tool_Box's container over a normal HTTP socket (`langchain-mcp-adapters`), and Docker's own log capture of a container's stdout (for `docker logs`) doesn't corrupt anything regardless of how noisy it is. **Action for Timothy:** confirm the compose service is running Tool_Box in HTTP mode (`TOOLBOX_TRANSPORT=http` or the container default) rather than attempting stdio across the container boundary (unusual, would need something like `docker exec -i`) — if it's HTTP, LLM_Monitor's own stdout hygiene is irrelevant to this integration.

**Splitting the plan — done.** Agreed the SPICE toolset doesn't belong in the same document; it was bundled in out of a "force scalability" instinct rather than any real relationship between the two toolsets, and Timothy asked to focus on the simpler one first. SPICE material moved to `Documentation/ImplementationPlans/004-SPICE-Circuit-Designer-Toolset.md`, explicitly deferred until this plan ships.

### Revised architecture (supersedes 2.5's original diagram)

```
Claude Desktop / Claude Code / LLM_Monitor's LangGraph agent
        │  (stdio or HTTP — voxel toolset doesn't care which)
        ▼
   ToolBox.Host
        │
   AddVoxelToolset()
     ├── VoxelWorld            (plain Dictionary, singleton, in-process)
     ├── VoxelTools            ([McpServerTool] place_box, place_sphere, mirror, ...)
     └── HttpListener on :8080 (started at registration; broadcasts world diffs)
                │
                ▼ ws://localhost:8080
     viewer/index.html  (static file — `npx serve`, `python -m http.server`, or
                          opened via file:// — Three.js/WebGL renders in-browser)
```

The agent only ever talks to `VoxelTools`. The viewer only ever talks to the WebSocket. Nothing bridges the two except the human looking at both.

### Updated open questions for Timothy

- **Q1 confirm scope.** Voxel World Builder only, per the split above — SPICE is now plan 004, revisit later.
- **Q2 confirm state.** Global singleton `VoxelWorld`, plain `Dictionary`, documented single-active-session limitation (per 2.1.1/2.7) — OK to proceed on this basis?
- **Q3 confirm viewer architecture.** `HttpListener`-based WebSocket broadcast inside the Host process, static viewer page served by any generic static-file tool (not our code) — OK per 2.7's revised diagram?
- **Q4 confirm skills.** Ship `.claude/skills/voxel/SKILL.md` alongside the toolset, modeled on the reference project's skill (scale-first discipline, primitive cheat sheet, call-budget guidance) — any style skills (a "castle" or similar) wanted for v1, or tools-plus-core-skill only?
- **Q5 confirm LLM_Monitor container transport.** Can you confirm the compose service builds Tool_Box for HTTP transport, not stdio, so the stdio-cleanliness question in 2.7 is fully closed?

Once these are confirmed, Stage 3 (concrete step-by-step build plan) gets drafted — same process plans 001 and 002 followed.

**[2026-07-21, Timothy]** Confirmed all five updated open questions as proposed. Proceeding to Stage 3.

---

# Stage 3 (Implementation Planning)

## Scope

**In:** `ToolBox.Voxel` toolset project (state, rasterization, tools, viewer broadcast), its test project, a static viewer page, a `.claude/skills/voxel` skill, Host wiring, catalog/ADR/README updates.
**Out (deferred):** session-scoped multi-client state, any style skill beyond the core one (a "castle"-equivalent), config-driven viewer port, authentication for the HTTP transport (ADR-008 still governs — see Step 7.3 for what changes and what doesn't).

## Definition of done

1. `dotnet build`/`dotnet test` pass with zero warnings, same as every prior plan's bar.
2. From a fresh clone: wire `AddVoxelToolset()` into the Host, connect with Claude Desktop or Code (stdio) or the Inspector, call `world_info` then `place_box` — get back a text confirmation.
3. With the Host running (either transport) and `viewer/index.html` opened in a browser (any static file server, or `file://` directly), a `place_sphere` call renders visibly within about a second.
4. `describe_world` reports an accurate block count and bounding box after a build.
5. A reflection test (mirroring `DescriptionConventionTests`) enforces that every Voxel tool and parameter carries a `[Description]`.
6. `docs/TOOL_CATALOG.md` has a Voxel section; two new ADRs record the global-state limitation and the companion-broadcast pattern; README documents how to open the viewer.

## Target structure

```
Tool_Box/
├── src/
│   ├── ToolBox.Host/                      # +1 composition line, no other changes
│   ├── ToolBox.Core/                      # unchanged — no Core changes needed (see note below)
│   └── ToolSets/
│       ├── ToolBox.Basics/                # unchanged
│       └── ToolBox.Voxel/                 # NEW
│           ├── ToolBox.Voxel.csproj
│           ├── VoxelWorld.cs              # state: Dictionary, events, snapshot/bounds
│           ├── VoxelRasterizer.cs         # pure geometry: box/sphere/cylinder/cone/tube/mirror
│           ├── Materials.cs               # v1 fixed palette (~10–12 names)
│           ├── VoxelTools.cs              # [McpServerToolType]
│           ├── VoxelViewerBroadcastService.cs  # BackgroundService: HttpListener + WebSocket
│           └── VoxelToolsetExtensions.cs  # AddVoxelToolset()
├── viewer/                                # NEW — static page, not built/served by our code
│   └── index.html                         # Three.js via CDN, no build step
├── tests/
│   └── ToolBox.Voxel.Tests/               # NEW — mirrors ToolBox.Basics.Tests shape
├── .claude/
│   └── skills/
│       └── voxel/SKILL.md                 # NEW
└── docs/ (TOOL_CATALOG.md, DECISIONS.md updated; README updated)
```

**Note on Core:** Stage 2 originally proposed "two small Core additions" (§2.1) before either toolset's code. Re-examining against the actual `ServerInfoProvider`/`ToolsetDescriptor` pattern already in Core: nothing there needs to change. `AddSingleton<VoxelWorld>()` inside `AddVoxelToolset()` is already fully expressible with the existing DI/ADR-005 convention — a toolset-local singleton needs no new Core abstraction. This is "separate early, abstract late" doing its job again: no generic "stateful toolset base" gets built until a *second* stateful toolset shows it's the right shape (same reasoning ADR-003 applied to the plugin loader, and 004 applied to itself). **Core changes: none.**

## Architecture (restates Stage 2 §2.7's diagram, now as the build target)

```
Claude Desktop / Claude Code / LLM_Monitor's LangGraph agent
        │  (stdio or HTTP — voxel toolset doesn't care which)
        ▼
   ToolBox.Host ── AddVoxelToolset() ──┬── VoxelWorld (singleton, plain Dictionary)
                                        ├── VoxelTools ([McpServerTool] place_box, ...)
                                        └── VoxelViewerBroadcastService : BackgroundService
                                                 │  HttpListener, loopback-only,
                                                 │  ports 8090→8093 (first free), independent
                                                 │  of whichever port the MCP HTTP transport
                                                 │  itself is using (8080) — no collision
                                                 ▼
                                     ws://127.0.0.1:809x/voxel/
                                                 ▲
                                     viewer/index.html (opened via `npx serve`,
                                     `python -m http.server`, or file://)
                                     Three.js/WebGL renders in-browser — no
                                     server-side rendering, ever.
```

## Steps

Each step ends at a verifiable checkpoint and waits for Timothy's permission before the next begins.

### Step 1 — Project scaffolding
- 1.1 Create `src/ToolSets/ToolBox.Voxel/ToolBox.Voxel.csproj` — same shape as `ToolBox.Basics.csproj` (ProjectReference to Core, `ModelContextProtocol` package reference).
- 1.2 Create `tests/ToolBox.Voxel.Tests/ToolBox.Voxel.Tests.csproj`.
- 1.3 Wire both into `ToolBox.sln` (same solution-folder grouping as Basics/Basics.Tests); add `ProjectReference` from `ToolBox.Host` to `ToolBox.Voxel`.
- **Checkpoint:** `dotnet build` succeeds; no tools wired yet, nothing runtime-visible changes.

### Step 2 — World state + rasterization (pure logic, no MCP yet)
- 2.1 `VoxelWorld`: `Dictionary<(int X, int Y, int Z), string> _blocks`; methods `SetBlock`, `RemoveBlock`, `Clear`, `Snapshot()` (full block list, for the viewer's on-connect sync), `BoundingBox()`; `event Action<VoxelEvent>? Changed` raised on every mutation.
- 2.2 `VoxelRasterizer`: static pure functions, each returning the coordinate set for a shape — `Box`, `Sphere` (with `ry`/`rz` stretch per blockworld's ellipsoid variant), `Cylinder`, `Cone` (with `r2` for a truncated cone/frustum), `Tube` (swept along a path, radius interpolated start→end), `Mirror` (reflects existing coordinates across an axis/plane). Kept independent of `VoxelWorld` so the geometry is unit-testable with no DI, no MCP, no event wiring — same "prove the primitive" discipline as plan 001's trivial tools.
- 2.3 Unit tests: known-shape assertions (e.g. a filled box's count equals the volume formula; a hollow box's count matches the shell formula; a sphere's count falls within a tolerance band of `4/3·π·r³`; mirroring an existing block set produces no duplicate coordinates when a plane bisects occupied space).
- **Checkpoint:** `dotnet test` green. No `[McpServerTool]` exists yet — this step is proven correct independent of the protocol layer, deliberately, before any tool wraps it.

### Step 3 — Tool layer
- 3.1 `Materials.cs`: a fixed v1 palette of ~10–12 named materials (e.g. `stone`, `brick`, `wood`, `glass`, `gold`, `grass`, ...) — an explicit, documented reduction from blockworld's 100-item palette; expand later if a build actually needs more.
- 3.2 `VoxelTools` (`[McpServerToolType]`), constructor takes `VoxelWorld`. Tools, in the build-order blockworld's own skill recommends (mass → structure → carve → detail):
  - `world_info` — scale/reference-dimension text (blockworld's own justification applies verbatim: the tool signatures alone can't tell an agent whether a block is 10cm or 10m).
  - `list_materials` — the fixed palette, joined into one string.
  - `place_box(x1,y1,z1,x2,y2,z2,material,hollow=false)`, `place_cylinder`, `place_cone`, `place_sphere`, `place_tube(path,r_start,r_end,material)`, `mirror(axis,plane)`, `remove_box`, `place_block` (detail only), `clear`, `describe_world`.
  - Every tool validates `material` against the fixed palette and returns a clear text error (not an exception) with a suggestion, mirroring blockworld's `checkMat`/"did you mean" pattern — cheap, and it keeps a wrong material name from silently failing.
  - Every string return routed through `OutputLimiter.Limit(...)`, per the platform's standing discipline.
- 3.3 **Watch item:** `place_tube`'s `path` parameter is an array of `{x,y,z}` points — the first parameter shape in this codebase that isn't a primitive. Confirm the SDK's schema generation handles `IReadOnlyList<VoxelPoint>` (a small record) correctly before relying on it elsewhere; this is this step's actual checkpoint risk, not a separate spike.
- 3.4 `VoxelToolsetExtensions.AddVoxelToolset()` — `services.AddSingleton<VoxelWorld>()`, `AddToolsetDescriptor("Voxel", ...)`, `WithTools<VoxelTools>()`.
- 3.5 `DescriptionConventionTests` for Voxel — copy of the Basics version, `typeof(VoxelTools)`, asserting tool count and full `[Description]` coverage.
- **Checkpoint:** Inspector session against stdio Host with Voxel wired in (Step 7.1 pulled forward for this test only, or a throwaway local composition): `world_info`, `place_box`, `describe_world` all behave as expected.

### Step 4 — Live viewer: broadcast service
- 4.1 `VoxelViewerBroadcastService : BackgroundService` — owns a `System.Net.HttpListener` bound to loopback only (`http://127.0.0.1:{port}/voxel/`), trying ports `8090, 8091, 8092, 8093` in order (mirroring blockworld's own fallback list) — deliberately outside the MCP HTTP transport's own port (8080), since under `--transport http` both would run in the same process.
- 4.2 On an incoming request: if `IsWebSocketRequest`, accept it, immediately send a `snapshot` message (`VoxelWorld.Snapshot()`) so a newly-opened or refreshed viewer tab syncs to current state, then track the socket. Subscribe to `VoxelWorld.Changed` once at service start; on each event, serialize a diff message and send it to every open socket, pruning any that are closed.
- 4.3 Register with `services.AddHostedService<VoxelViewerBroadcastService>()` inside `AddVoxelToolset()`. This is the step that actually proves Stage 2 §2.7's claim: both `Program.cs` paths (`Host.CreateApplicationBuilder` for stdio, `WebApplication.CreateBuilder` for HTTP) build on `Microsoft.Extensions.Hosting`'s common `IHost`, so one `BackgroundService` registration works unmodified under either transport — a toolset really can carry its own companion infrastructure, transport-agnostically.
- **Checkpoint:** start the Host under stdio; connect to `ws://127.0.0.1:8090/voxel/` with any WebSocket client (`wscat`, a browser console) and confirm a `snapshot` arrives; call `place_box` via Inspector; confirm the same socket receives a diff. Repeat once under `--transport http` to confirm no port collision with `:8080`.

### Step 5 — Static viewer page
- 5.1 `viewer/index.html` — Three.js via CDN `<script>` tag (no build step, matching blockworld's own approach exactly), hardcoding `ws://127.0.0.1:8090/voxel/`, rendering instanced cubes with basic orbit/zoom camera controls. No C# serves this file; it's opened via `npx serve viewer`, `python -m http.server` from inside `viewer/`, or `file://` directly.
- **Checkpoint:** with the Host running and a build in progress, `place_sphere` renders visibly within about a second of the tool call returning.

### Step 6 — Skill
- 6.1 `.claude/skills/voxel/SKILL.md` — frontmatter `name: voxel`, one-line `description`. Content modeled directly on blockworld's core skill: call `world_info` first and build in blocks (never assume a metric scale), the materials/primitive cheat sheet, a call-budget guideline, and build order (mass → structure → carve → detail).
- **Checkpoint:** read-through only — no code, but review it the way a `[Description]` gets reviewed: as a prompt, not documentation.

### Step 7 — Host wiring + docs
- 7.1 One line in `ToolBoxServerComposition.cs`: `.AddVoxelToolset()`.
- 7.2 `docs/TOOL_CATALOG.md`: new "Toolset: Voxel" section, tools table with read/write classification. **Note:** this is the first **write**-classified toolset in the catalog (`place_*`, `remove_box`, `mirror`, `clear` all mutate); `world_info`/`list_materials`/`describe_world` stay **read**.
- 7.3 ADR-008 said, as one of four mitigating factors for the unauthenticated HTTP posture, "all current tools are read-only" — that becomes false the moment this toolset ships over HTTP. Rather than silently letting that sentence go stale, write a short superseding note on ADR-008 (or a new ADR-011) stating explicitly: the mitigating control for write tools is still isolation (no published ports outside a trusted compose network, per ADR-008's items 1–3), *not* read-only-ness; that assumption is retired as of this toolset. This is a real judgment call worth Timothy's eyes before it ships over HTTP, not something to wave through.
- 7.4 New ADR-009: global/singleton world state is a documented v1 limitation (one world, no session scoping), superseded whenever multi-client demos need otherwise.
- 7.5 New ADR-010: toolsets may register companion `IHostedService`s independent of transport (generalizes ADR-005); the voxel viewer broadcaster is the first example.
- 7.6 README: mention the Voxel toolset and how to open the viewer.
- **Checkpoint:** fresh clone, `dotnet build`/`dotnet test` clean; Claude Desktop or Code drives a live build with the viewer open.

### Step 8 — Demo pass
- 8.1 Run a real build prompt (e.g. "build me a castle") through Claude Code with the viewer open; capture a screenshot or short recording for the portfolio.
- 8.2 Optionally repeat through LLM_Monitor's LangGraph agent (per plan 002's integration — no Tool_Box code changes required) for the "my agent, my tools, my infrastructure, end to end" tie-in from the original brainstorm.
- **Checkpoint:** none code-based — this step is the payoff, not a gate.

Steps 1–3 have no dependency on 4–6 and could be built and demoed (via Inspector, text-only) before the viewer exists at all — worth keeping in mind if time runs short between sessions: a working, tested toolset with no visible viewer is still a genuine checkpoint, not a stall.
