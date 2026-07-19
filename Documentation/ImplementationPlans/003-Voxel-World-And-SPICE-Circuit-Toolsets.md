2026_07_19_10_00-(Voxel-World-And-SPICE-Circuit-Toolsets)

# Implementation Plan 003 — Voxel World Builder & SPICE Circuit Designer Toolsets

Goal of this plan: give Tool_Box its first two **stateful, artifact-producing** toolsets — a live-rendered voxel world builder (Category A from the toolset brainstorm) and a SPICE-backed electrical circuit designer (a new category, proposed below as Category E). Where plans 001–002 built the platform and proved it could carry a transport, this plan proves it can carry *domain state* and *binary output* — the two capabilities every interesting future toolset (audio, scene rendering, embedded debug) will also need.

---

# Stage 1 (Design Documentation)

*Timothy's goals, as stated 2026-07-19:*

Two toolsets, both greenlit conceptually, both wanted in depth before commitment:

1. **The Category A idea from `Documentation/Brainstorms/003-TOOLSET_IDEAS.md`** — "Creative & Visual (the demo stars)." Timothy specifically called out this category as the one that "provides a good amount of ability to showcase something visual which will impress an interviewer and be easy to demonstrate and display."
2. **A new idea, not yet in the brainstorm doc:** a SPICE-based electrical circuit toolset. The user experience Timothy described: *describe a circuit (an electrical product) in natural language, then "sit back and relax" while Claude builds the circuit, tests it, simulates it, and builds the files required for viewing and exporting it* — fully autonomous, end to end.

Timothy asked for an in-depth document covering: how to accomplish both, their feasibility, and how they fit within the existing project architecture. This document is that research and design pass — Stage 2 below is the substance; Stage 3 (the step-by-step build plan) waits for Timothy's response to the open questions at the end.

---

# Stage 2 (Discussion)

**[2026-07-19, AI]** Opening position. This is long because it was asked to be — feasibility and architecture for two genuinely different domains, plus the parts they share. Structure:

- 2.1 What both toolsets have in common (and why that matters more than either toolset alone)
- 2.2 Toolset A — Voxel World Builder
- 2.3 Toolset B — SPICE Circuit Designer
- 2.4 Technical spikes to run *before* committing to a full build
- 2.5 Sequencing recommendation
- 2.6 Open questions for Timothy

---

## 2.1 What both toolsets have in common

Every toolset shipped so far (`Basics`) is **stateless**: each tool call is a pure function of its arguments plus ambient server info (`ServerInfoProvider`, `TimeProvider`). Both new toolsets break that pattern in the same two ways, which is the real architectural story of this plan:

| New capability | Voxel World | SPICE Circuit | Why Basics never needed it |
|---|---|---|---|
| **Cross-call state** | The world persists across `place_box`, `mirror`, `clear`, etc. | The circuit-under-construction persists across `add_resistor`, `add_voltage_source`, `run_simulation`, etc. | `ping`/`current_time`/`server_info` have nothing to remember between calls |
| **Binary/rich output** | Live 3D viewer (WebSocket-pushed geometry), not just text | Schematic SVG, waveform PNG, exported netlist file | All three Basics tools return strings or a small POCO |

Both gaps need to be closed at the **Core** layer, not duplicated per-toolset, exactly the same reasoning that put `OutputLimiter` and `ToolsetDescriptor` in Core rather than in Basics (ADR-003, ADR-005). Concretely, this plan proposes two small Core additions before either toolset's own code:

### 2.1.1 A stateful-toolset pattern

Register world/circuit state the same way `ServerInfoProvider` is registered today — as a singleton service the toolset's tool class takes in its constructor:

```csharp
// Core/DependencyInjection convention, generalized from ADR-005:
public static IMcpServerBuilder AddVoxelToolset(this IMcpServerBuilder builder)
{
    builder.Services.AddSingleton<VoxelWorld>();   // <-- new: stateful singleton
    builder.Services.AddToolsetDescriptor(name: "Voxel", description: "...");
    return builder.WithTools<VoxelTools>();
}
```

This is the *simple* answer, and I think it's the right one for v1, but it comes with an honest limitation that needs to be written down rather than discovered later: **a singleton is one world/circuit shared by every connected client.** Under stdio (Claude Desktop) that's a non-issue — one client, one process. Under HTTP (plan 002's transport), if two agents connect to the same running server, they'd be editing the same world. The MCP C# SDK does have a notion of per-session context for HTTP (each streamable-HTTP connection gets its own `McpServer`/session), so a *correct* multi-tenant answer is session-scoped state rather than a process-wide singleton. I'm proposing we **explicitly scope this plan to singleton/global state, document it as a known limitation (a new ADR), and defer session-scoped state** to whenever multi-client demos actually require it — same "abstract from evidence, not imagination" principle ADR-003's discussion already established for the plugin loader. Flagging it now so it's a decision, not an accident.

### 2.1.2 Rich tool output (images, files)

MCP's tool-result content model supports more than text — `TextContent`, `ImageContent` (base64 + mime type), and embedded resources are part of the spec, and the official C# SDK exposes matching types. We have not yet used any of them; every Basics tool returns a `string` or a POCO that the SDK serializes to text/JSON. Before designing tool signatures for "render a waveform PNG," we need to confirm exactly how the current SDK version (1.4.x, per `ToolBox.Basics.csproj`) wants image content returned from a `[McpServerTool]` method — this is Spike 1 in section 2.4, and it gates real design work on both toolsets' rendering tools.

A secondary, simpler pattern both toolsets will also use: tools that write an artifact to disk (a `.cir` netlist, a `.svg` schematic) and return a **path plus a short summary**, rather than inlining large content into the MCP response. This keeps `OutputLimiter` meaningful (large binary blobs shouldn't be text-truncated) and mirrors how a real CLI tool behaves — closer to `git diff > file.patch` than to dumping everything to the terminal.

---

## 2.2 Toolset A — Voxel World Builder

### 2.2.1 What it is

From the brainstorm (A1): tools that describe *form*, not coordinates — `place_box`, `place_cylinder`, `place_cone`, `place_sphere`, `place_tube`, `mirror`, `remove_box`, `describe_world`, `clear`, `world_info` — plus a browser viewer that renders the world live as the agent builds it.

### 2.2.2 Data model

A voxel world is a sparse 3D grid. Dense arrays don't scale to anything interesting (a 200×200×200 world is 8M cells for what might be a few thousand occupied blocks), so:

```csharp
public sealed class VoxelWorld
{
    // Sparse: only occupied cells are stored.
    private readonly ConcurrentDictionary<(int X, int Y, int Z), BlockType> _blocks = new();

    public IReadOnlyCollection<WorldDiff> LastChange { get; private set; }
    public event Action<WorldDiff>? Changed;   // the viewer's broadcast hook
    ...
}
```

`ConcurrentDictionary` because tool calls could interleave with the WebSocket broadcast loop even in a single-client scenario; cheap insurance, not over-engineering.

### 2.2.3 Server-side rasterization (the actual interesting logic)

This is the part of the brainstorm worth taking seriously: `place_sphere(center, radius, block_type)` must expand into the *set* of voxel coordinates inside that sphere. These are standard, well-documented algorithms — not research:

- **Box**: trivial triple loop over the bounding coordinates.
- **Sphere**: iterate the bounding cube, keep points where `dx²+dy²+dz² ≤ r²` (or a midpoint-circle-style boundary variant for a hollow shell).
- **Cylinder/cone**: iterate by height layer; at each layer, rasterize a 2D circle (Bresenham's midpoint circle algorithm, extended to filled or hollow) with a radius that's constant (cylinder) or interpolated (cone).
- **Tube**: cylinder minus a smaller concentric cylinder.
- **Mirror**: reflect a given bounding region across an axis/plane through existing world coordinates — a coordinate transform over the block set, no new geometry math.

None of this requires a library; it's a few hundred lines of integer math, and it is exactly the kind of "prove you understand the primitive before reaching for a package" exercise that fits the learning goal of this project.

### 2.2.4 The live viewer — where does it actually live?

This is the one genuinely new architectural question. The viewer needs: (a) a way to serve an HTML/Three.js page, and (b) a WebSocket the page can subscribe to for world-diff pushes. Two options:

**Option 1 — the viewer rides on the MCP HTTP transport.** Since plan 002, the Host already runs a Kestrel/ASP.NET Core app when `--transport http` is selected. Adding `app.MapGet("/viewer", ...)` and a WebSocket endpoint alongside `app.MapMcp()` is cheap — it's the same web app. **Downside:** the viewer would only exist when running in HTTP mode. Claude Desktop, which uses stdio, would have a voxel toolset with no way to *see* the result — a real gap for exactly the client most people demo with first.

**Option 2 — the voxel toolset carries its own companion web server, independent of MCP transport.** The toolset's registration extension (`AddVoxelToolset`) also registers an `IHostedService` that starts a small dedicated Kestrel instance (e.g., `http://localhost:5299/viewer`) purely for the viewer + WebSocket, regardless of whether the Host itself is running stdio or MCP-HTTP. This generalizes ADR-005 ("toolsets register themselves") one step further: a toolset can bring not just tools, but **companion infrastructure**. I'm recommending Option 2 — it keeps the viewer working identically under Claude Desktop and Claude Code, it doesn't entangle voxel-specific concerns with the MCP HTTP pipeline plan 002 built, and "one toolset, one extension method, everything it needs" is a cleaner story to tell in an interview than "some toolsets need a specific transport."

```
                 ToolBox.Host (stdio OR http — voxel toolset doesn't care)
                        │
                 AddVoxelToolset()
                   ├── VoxelWorld (singleton state)
                   ├── VoxelTools ([McpServerTool] place_box, place_sphere, ...)
                   └── VoxelViewerHostedService
                              │  starts its own Kestrel on :5299
                              ▼
                 http://localhost:5299/viewer  (static Three.js page)
                 ws://localhost:5299/viewer/ws (diff broadcast)
                        ▲
                 VoxelWorld.Changed event ──┘
```

### 2.2.5 Effort, risk, and the LLM_Monitor tie-in

Brainstorm estimate: Medium (1–3 weeks). I'd break it down as: state model + rasterization math (S), tool layer + descriptions (S), viewer hosted service + static page + WebSocket (M — this is genuinely new plumbing for this repo), Three.js frontend (S if kept to instanced cubes, no textures/lighting fanciness). The companion-server pattern (2.2.4) is the one piece worth spiking early since it sets precedent for every future visual toolset (A2 scene composer would reuse it).

The brainstorm's "twist" — routing this through LLM_Monitor's own LangGraph agent, not just Claude Desktop — costs **nothing extra** given plan 002's work: LLM_Monitor already consumes any Tool_Box toolset generically via `langchain-mcp-adapters` (`docs/LLM_MONITOR_INTEGRATION.md`). Once `AddVoxelToolset()` is wired into the Host's composition, "my agent, my tools, my infrastructure, end to end" is just running a "build me a castle" prompt through LLM_Monitor and pointing a browser at the viewer — a good demonstration that the plan 001–002 investment in clean boundaries pays off exactly as promised.

---

## 2.3 Toolset B — SPICE Circuit Designer

### 2.3.1 What it is, restated precisely

Timothy's framing — describe a circuit in words, then Claude autonomously builds, tests, simulates, and exports it — decomposes into four capabilities: (1) an internal circuit representation the agent builds up call-by-call, (2) a SPICE simulation backend, (3) result validation ("tests it"), (4) export artifacts (netlist, schematic, waveform) for viewing outside the tool.

This is a strong idea for this specific portfolio, for a reason beyond "it's a cool demo": it's a **third domain where Timothy's actual background is an unfair advantage** (persona.md: embedded C/C++, hardware/software integration, I2C/SPI). The brainstorm doc's Category D ("nobody else has a logic analyzer") makes this exact argument for hardware toolsets; SPICE circuit design is the same argument from the EE side. I'd suggest — pending Timothy's agreement — adding this to the brainstorm doc as **Category E** after this plan converges, so the taxonomy stays complete.

### 2.3.2 Circuit representation

A netlist is fundamentally a graph: named nodes (0 = ground, by SPICE convention) and two-or-more-terminal components connecting them.

```csharp
public sealed class Circuit
{
    public string Name { get; }
    private readonly List<Component> _components = new();   // R1, C1, V1, ...
    private readonly HashSet<string> _nodes = new() { "0" }; // ground always exists
    ...
}

public abstract record Component(string RefDes, IReadOnlyList<string> Nodes);
public sealed record Resistor(string RefDes, string A, string B, double Ohms) : Component(RefDes, [A, B]);
public sealed record Capacitor(string RefDes, string A, string B, double Farads) : Component(RefDes, [A, B]);
public sealed record VoltageSource(string RefDes, string Pos, string Neg, SourceSpec Spec) : Component(RefDes, [Pos, Neg]);
// Inductor, CurrentSource, Diode, Bjt, Mosfet, OpAmp(as a subckt) follow the same shape.
```

Emitting a SPICE netlist from this model is pure string formatting — no library needed, and it's the part of the SPICE ecosystem that's genuinely simple and well-specified (`.title`, one line per component in `<RefDes> <node> <node> <value>` form, a `.control`/analysis block, `.end`).

### 2.3.3 Tool design — apply the same "call-economy" lesson as the voxel toolset

This is worth stating explicitly because it's the same principle from a different domain, and drawing that parallel for Timothy is one of the more valuable things this document can do: don't expose `add_node`/`add_wire` as the only primitives (that's `place_block(x,y,z)` five thousand times, in circuit form). Two tiers:

**Tier 1 — primitive components** (the SPICE building blocks): `add_resistor`, `add_capacitor`, `add_inductor`, `add_voltage_source` (dc/ac/sin/pulse), `add_current_source`, `add_diode`, `add_transistor` (bjt/mosfet), `add_opamp` (ideal macro-model).

**Tier 2 — composite tools**, expanding server-side into several Tier 1 components, mirroring `place_sphere`: `add_voltage_divider(node_in, node_out, ratio, total_ohms)`, `add_rc_lowpass(node_in, node_out, cutoff_hz)`, `add_rc_highpass(...)`, `add_led_current_limiter(supply_v, led_forward_v, target_ma)`. These are exactly the sub-circuits a natural-language circuit description ("I want an LED that doesn't burn out at 5V") maps onto directly — this tier is what makes "describe a circuit and sit back" actually work, rather than making the agent reason out individual resistor values from scratch every time.

**Simulation and analysis**: `set_analysis(kind: operating_point | dc_sweep | ac_sweep | transient, params)`, `run_simulation()`, `get_results(...)` (bounded — see 2.3.6), `check_node_voltage(node, expect_between)` (the "tests it" verification step — an explicit assertion tool the agent calls to sanity-check its own design, not just eyeball numbers).

**Export/render**: `export_netlist()`, `render_schematic()`, `render_waveform()`, `describe_circuit()`.

### 2.3.4 Running the simulation — ngspice

[ngspice](https://ngspice.sourceforge.io/) is the standard open-source SPICE engine (BSD/GPL-mixed licensing, actively maintained, packaged for apt/brew/choco). The integration shape is the same cross-language pattern the brainstorm already called out for A2 (Node sidecar): **shell out to a process, don't bind a library.**

```
Circuit (C# model)
   │ ToSpiceNetlist()
   ▼
circuit.cir  (text file, includes a .control block ending in `wrdata results.csv <signals>`)
   │
   │ Process.Start("ngspice", "-b circuit.cir")
   ▼
results.csv  (plain columns: time/frequency, then one column per requested signal)
   │
   │ trivial CSV parse
   ▼
SimulationResult (C# model) ──► get_results() / render_waveform() / check_node_voltage()
```

The key feasibility detail: rather than parsing ngspice's native binary/ASCII "rawfile" format (a documented but fiddlier format), the netlist's own `.control` block can call ngspice's built-in `wrdata` command to dump exactly the requested signals as flat CSV. That turns "parse a SPICE simulator's output format" into "parse a CSV" — meaningfully lower implementation risk, worth calling out now rather than discovering the rawfile parser is the hard part three weeks in.

Deployment: this is a real external dependency, but not a new *kind* of one — plan 002 already put a Debian-based image in the Dockerfile; `apt-get install -y ngspice` is one line, and it's the same "external tool the container needs" shape as anything else that would eventually get containerized here.

### 2.3.5 Schematic rendering — the highest-risk piece, be honest about it

This is where I want to flag a real limitation rather than have it surface mid-implementation. Automatic, *good-looking* schematic layout from an arbitrary netlist graph is not a solved weekend problem — it's close to a research topic in EDA tooling (this is why KiCad and every other schematic editor makes you place symbols by hand). Two honest paths:

- **Path 1 (recommended for v1): constrain the layout.** Most demo-worthy circuits an agent will actually build from a natural-language description are simple — series/parallel ladders, filters, dividers, single-stage amps. A left-to-right placement heuristic (place components in call order, break to a new row at parallel branches, snap to a grid) covers those well and is honest to build. Rendering itself can use [`schemdraw`](https://schemdraw.readthedocs.io/) (Python, MIT-licensed, draws standard resistor/cap/inductor/source/op-amp symbols with SVG/PNG export) via the same subprocess-sidecar pattern as ngspice — feed it a small JSON description of "component, position, orientation," get back SVG bytes.
- **Path 2 (explicitly deferred): general graph auto-layout** for arbitrary topologies. Real technical depth here (force-directed layout, orthogonal wire routing, symbol-overlap avoidance) — worth its own future plan and probably its own Learning doc, not something to fold into v1's estimate.

I'd propose documenting Path 1's constraint explicitly (an ADR, same instinct as ADR-008's "unauthenticated, network-isolated" — a real limitation, written down, not silently discovered by a user).

### 2.3.6 Waveform rendering and bounded output

Transient/AC sweep results can be large (thousands of time points). Two things route this through Core's existing discipline rather than around it: (1) `render_waveform()` produces a PNG plot — and since this is pure plotting of already-in-process numeric data, it doesn't need a sidecar at all; [ScottPlot](https://scottplot.net/) is a mature, pure-.NET charting library that can render directly from the parsed `SimulationResult` and save a PNG. Worth noting as a deliberate *contrast* to the schematic renderer: not every rendering need justifies a cross-language sidecar, and knowing which is which is itself a design judgment worth demonstrating. (2) `get_results()` (the text/JSON path back to the agent) never returns raw sample arrays — it returns summary statistics (min/max/final value, a decimated handful of points) through `OutputLimiter`, with the full-resolution data available only via the exported CSV/PNG file path. Same "summarize for the model, keep the artifact for the human" split as 2.1.2.

### 2.3.7 A worked example, to make "sit back and relax" concrete

```
User: "Build me a 5V-to-3.3V resistive divider that can source about 10mA,
       and show me it actually lands at 3.3V."

Agent's tool calls (illustrative):
  create_circuit("divider_5v_to_3v3")
  add_voltage_divider(node_in="VIN", node_out="VOUT", node_gnd="0",
                       ratio=0.66, total_ohms=330)      # composite tool, Tier 2
  add_voltage_source(RefDes="V1", pos="VIN", neg="0", kind="dc", volts=5.0)
  set_analysis(kind="operating_point")
  run_simulation()
  check_node_voltage(node="VOUT", expect_between=[3.2, 3.4])   # "tests it"
  render_schematic()   → schematic.svg
  render_waveform()    → (n/a for op-point; would apply to a transient/AC run)
  export_netlist()     → divider_5v_to_3v3.cir

Agent's reply: "Built it — R1=110Ω, R2=220Ω, VOUT settles at 3.33V (within
  spec). Schematic and SPICE netlist are in ./circuits/divider_5v_to_3v3/."
```

That sequence is achievable with the Tier 1/Tier 2 tool set above and no invented capability — which is the point of walking through it now rather than after committing to a step plan.

### 2.3.8 Effort and risk summary

| Piece | Effort | Risk | Notes |
|---|---|---|---|
| Circuit model + netlist emission | S | Low | Pure C# string formatting |
| ngspice process integration + CSV results | S–M | Low–Medium | `wrdata` sidesteps rawfile parsing |
| Tier 1 + Tier 2 tools, descriptions | M | Low | Bulk of the "call-economy" design work |
| Waveform PNG (ScottPlot) | S | Low | No sidecar needed |
| Schematic SVG (schemdraw sidecar, constrained layout) | M | **Medium–High** | The one piece that could balloon; scope it hard |
| Docker/CI (ngspice + python+schemdrav in image) | S | Low | Same shape as existing Dockerfile work |

Overall: **M** for a v1 that stops at Path 1 schematics (matches the brainstorm's own effort banding for comparable ideas), with schematic rendering the one line item worth spiking before it's put on a committed timeline.

---

## 2.4 Technical spikes to run before committing to a full build

Both toolsets share enough unknowns that I'd rather spend a day or two on throwaway spikes than write a confident Stage 3 step plan on top of untested assumptions — the same instinct that made plan 001's Inspector debugging story worth logging rather than papering over.

1. **Spike: rich content from `[McpServerTool]`.** Confirm exactly how `ModelContextProtocol` 1.4.x wants a tool method to return image content (a special return type? a `CallToolResult` builder? attribute-driven?). Gates schematic/waveform/viewer design on both toolsets.
2. **Spike: companion `IHostedService` alongside the MCP host.** Prove a toolset's extension method can start a second Kestrel instance cleanly under both `--transport stdio` and `--transport http`, with no port conflicts and clean shutdown. Gates the voxel viewer architecture (2.2.4).
3. **Spike: ngspice batch + `wrdata` round-trip.** One resistor, one voltage source, `ngspice -b`, confirm CSV comes out parseable, on the actual dev machine and inside a container. Cheap, de-risks the entire SPICE toolset's simulation core.
4. **Spike: schemdraw from a JSON-ish description via subprocess.** Render one 3-component series circuit end to end, confirm the subprocess/sidecar pattern (established conceptually by brainstorm A2, not yet built anywhere in this repo) actually works from C#. Gates 2.3.5's scope commitment.

None of these need a step-by-step plan of their own — they're closer to what plan 001's "verify with MCP Inspector" checkpoints were: fast, falsifiable, and they belong in Stage 3 as the literal first steps once this discussion converges.

---

## 2.5 Sequencing recommendation

Build the **SPICE toolset first**, voxel world second — the reverse of the brainstorm's original "A1/A2 as showpiece first" arc, and worth explaining why: the SPICE toolset's riskiest piece (schematic rendering) is scoped and bounded (2.3.5), while the voxel toolset's core architectural question (2.2.4, companion server pattern) is *shared infrastructure* — the same hosted-service pattern will make the voxel viewer, a future A2 scene composer, and any future rendering toolset all easier. Sequencing SPICE first means Spike 1 (rich content) and the sidecar pattern (Spike 4) get proven on the lower-risk toolset, and Spike 2 (companion server) gets built once, deliberately, for voxel — rather than rushed as a dependency of whichever toolset goes first. Also: SPICE is the toolset with zero precedent anywhere in the brainstorm doc, so getting it fully designed and reviewed benefits most from being tackled with full attention rather than second, tired from a first toolset.

This is a recommendation, not a decision — happy to reverse it if the demo-value argument (voxel is the "ten minutes later" memory) matters more to Timothy than the risk-sequencing argument.

---

## 2.6 Open questions for Timothy

- **Q1 — Category A choice.** Confirm: Voxel World Builder (A1) as designed above, or did "Category A" mean something broader (e.g., also wanting A2 Scene Composer or A3 Diagrams scoped now)? This document designs A1 in depth; A2/A3 are mentioned only for context.
- **Q2 — Singleton world/circuit state (2.1.1).** OK to explicitly scope this plan to one global world and one global circuit-under-construction (documented limitation, ADR-009), deferring session-scoped multi-client state until a real demo needs it?
- **Q3 — Companion server pattern (2.2.4).** Agree that the voxel viewer should run as its own always-on hosted service (independent of stdio/HTTP transport choice), rather than only existing when the Host runs in HTTP mode?
- **Q4 — Schematic layout scope (2.3.5).** Comfortable committing v1 to Path 1 (constrained ladder/series-parallel layout via schemdraw), with general graph auto-layout explicitly deferred to a future plan?
- **Q5 — Sequencing (2.5).** SPICE first, voxel second — as recommended — or voxel first for earlier demo value?
- **Q6 — Category E.** OK to append a new "Category E — Electrical Engineering / SPICE Design" section to `Documentation/Brainstorms/003-TOOLSET_IDEAS.md` once this plan converges, so the brainstorm doc stays the single source of truth for the toolset roadmap?

Stage 3 (the concrete step-by-step build plan, spikes-first per 2.4) will be drafted once these are resolved — same process plans 001 and 002 followed.
