2026_07_20_09_00-(SPICE-Circuit-Designer-Toolset)

# Implementation Plan 004 — SPICE Circuit Designer Toolset

**Status: deferred.** Split out of plan 003 on 2026-07-20, which originally covered this toolset alongside the Voxel World Builder. Timothy asked to focus on the simpler toolset (003) first; this document preserves the design work already done so it isn't lost, and picks back up once 003 ships. Do not begin Stage 3 here until 003 is done and Timothy explicitly restarts this plan's discussion.

---

# Stage 1 (Design Documentation)

*Timothy's goal, as stated 2026-07-19:* a SPICE-based electrical circuit toolset. The user experience: *describe a circuit (an electrical product) in natural language, then "sit back and relax" while Claude builds the circuit, tests it, simulates it, and builds the files required for viewing and exporting it* — fully autonomous, end to end.

---

# Stage 2 (Discussion)

**[2026-07-19, AI]** Opening position, carried over unchanged from the original combined plan 003 draft. Restart this discussion once 003 has shipped and lessons from it (especially around the stateful-toolset pattern and any rich-content spike work) are available to build on.

## 2.1 What it is, restated precisely

Timothy's framing decomposes into four capabilities: (1) an internal circuit representation the agent builds up call-by-call, (2) a SPICE simulation backend, (3) result validation ("tests it"), (4) export artifacts (netlist, schematic, waveform) for viewing outside the tool.

This is a strong idea for this specific portfolio, for a reason beyond "it's a cool demo": it's a **third domain where Timothy's actual background is an unfair advantage** (persona.md: embedded C/C++, hardware/software integration, I2C/SPI). The brainstorm doc's Category D ("nobody else has a logic analyzer") makes this exact argument for hardware toolsets; SPICE circuit design is the same argument from the EE side. Proposed — pending Timothy's agreement — adding this to `Documentation/Brainstorms/003-TOOLSET_IDEAS.md` as **Category E** once this plan converges.

## 2.2 Circuit representation

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

## 2.3 Tool design — the same "call-economy" lesson plan 003 applies to voxels

Don't expose `add_node`/`add_wire` as the only primitives (that's `place_block(x,y,z)` five thousand times, in circuit form). Two tiers:

**Tier 1 — primitive components** (the SPICE building blocks): `add_resistor`, `add_capacitor`, `add_inductor`, `add_voltage_source` (dc/ac/sin/pulse), `add_current_source`, `add_diode`, `add_transistor` (bjt/mosfet), `add_opamp` (ideal macro-model).

**Tier 2 — composite tools**, expanding server-side into several Tier 1 components, mirroring `place_sphere`: `add_voltage_divider(node_in, node_out, ratio, total_ohms)`, `add_rc_lowpass(node_in, node_out, cutoff_hz)`, `add_rc_highpass(...)`, `add_led_current_limiter(supply_v, led_forward_v, target_ma)`. These are exactly the sub-circuits a natural-language circuit description ("I want an LED that doesn't burn out at 5V") maps onto directly — this tier is what makes "describe a circuit and sit back" actually work, rather than making the agent reason out individual resistor values from scratch every time.

**Simulation and analysis**: `set_analysis(kind: operating_point | dc_sweep | ac_sweep | transient, params)`, `run_simulation()`, `get_results(...)` (bounded — see 2.6), `check_node_voltage(node, expect_between)` (the "tests it" verification step — an explicit assertion tool the agent calls to sanity-check its own design, not just eyeball numbers).

**Export/render**: `export_netlist()`, `render_schematic()`, `render_waveform()`, `describe_circuit()`.

## 2.4 Running the simulation — ngspice

[ngspice](https://ngspice.sourceforge.io/) is the standard open-source SPICE engine (BSD/GPL-mixed licensing, actively maintained, packaged for apt/brew/choco). The integration shape mirrors the brainstorm's A2 idea (Node sidecar): **shell out to a process, don't bind a library.**

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

The key feasibility detail: rather than parsing ngspice's native binary/ASCII "rawfile" format (documented but fiddlier), the netlist's own `.control` block can call ngspice's built-in `wrdata` command to dump exactly the requested signals as flat CSV — turning "parse a SPICE simulator's output format" into "parse a CSV." Meaningfully lower implementation risk than it first looks.

Deployment: a real external dependency, but not a new *kind* of one — plan 002 already put a Debian-based image in the Dockerfile; `apt-get install -y ngspice` is one line, the same "external tool the container needs" shape as anything else that would get containerized here.

## 2.5 Schematic rendering — the highest-risk piece, be honest about it

Automatic, *good-looking* schematic layout from an arbitrary netlist graph is not a solved weekend problem — it's close to a research topic in EDA tooling (this is why KiCad and every other schematic editor makes you place symbols by hand). Two honest paths:

- **Path 1 (recommended for v1): constrain the layout.** Most demo-worthy circuits an agent will actually build from a natural-language description are simple — series/parallel ladders, filters, dividers, single-stage amps. A left-to-right placement heuristic (place components in call order, break to a new row at parallel branches, snap to a grid) covers those well and is honest to build. Rendering itself can use [`schemdraw`](https://schemdraw.readthedocs.io/) (Python, MIT-licensed, draws standard resistor/cap/inductor/source/op-amp symbols with SVG/PNG export) via a subprocess-sidecar pattern — feed it a small JSON description of "component, position, orientation," get back SVG bytes.
- **Path 2 (explicitly deferred): general graph auto-layout** for arbitrary topologies. Real technical depth here (force-directed layout, orthogonal wire routing, symbol-overlap avoidance) — worth its own future plan and probably its own Learning doc, not something to fold into v1's estimate.

Document Path 1's constraint explicitly (an ADR, same instinct as ADR-008's "unauthenticated, network-isolated" — a real limitation, written down, not silently discovered by a user).

## 2.6 Waveform rendering and bounded output

Transient/AC sweep results can be large (thousands of time points). Two things route this through Core's existing discipline rather than around it: (1) `render_waveform()` produces a PNG plot — pure plotting of already-in-process numeric data, so it doesn't need a sidecar at all; [ScottPlot](https://scottplot.net/) is a mature, pure-.NET charting library that can render directly from the parsed `SimulationResult` and save a PNG. Worth noting as a deliberate *contrast* to the schematic renderer: not every rendering need justifies a cross-language sidecar, and knowing which is which is itself a design judgment worth demonstrating. (2) `get_results()` (the text/JSON path back to the agent) never returns raw sample arrays — it returns summary statistics (min/max/final value, a decimated handful of points) through `OutputLimiter`, with the full-resolution data available only via the exported CSV/PNG file path.

## 2.7 A worked example, to make "sit back and relax" concrete

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
  export_netlist()     → divider_5v_to_3v3.cir

Agent's reply: "Built it — R1=110Ω, R2=220Ω, VOUT settles at 3.33V (within
  spec). Schematic and SPICE netlist are in ./circuits/divider_5v_to_3v3/."
```

## 2.8 Effort and risk summary

| Piece | Effort | Risk | Notes |
|---|---|---|---|
| Circuit model + netlist emission | S | Low | Pure C# string formatting |
| ngspice process integration + CSV results | S–M | Low–Medium | `wrdata` sidesteps rawfile parsing |
| Tier 1 + Tier 2 tools, descriptions | M | Low | Bulk of the "call-economy" design work |
| Waveform PNG (ScottPlot) | S | Low | No sidecar needed |
| Schematic SVG (schemdraw sidecar, constrained layout) | M | **Medium–High** | The one piece that could balloon; scope it hard |
| Docker/CI (ngspice + python + schemdraw in image) | S | Low | Same shape as existing Dockerfile work |

Overall: **M** for a v1 that stops at Path 1 schematics, with schematic rendering the one line item worth spiking before it's put on a committed timeline.

## 2.9 Spikes to run before committing to a full build

1. **Rich content from `[McpServerTool]`.** Confirm exactly how `ModelContextProtocol` 1.4.x wants a tool method to return image content (a special return type? a `CallToolResult` builder? attribute-driven?) — this *does* gate this toolset's rendering tools, unlike plan 003's voxel toolset (which turned out not to need it — see 003 §2.7).
2. **ngspice batch + `wrdata` round-trip.** One resistor, one voltage source, `ngspice -b`, confirm CSV comes out parseable, on the actual dev machine and inside a container.
3. **schemdraw from a JSON-ish description via subprocess.** Render one 3-component series circuit end to end, confirm the subprocess/sidecar pattern actually works from C#.

## 2.10 Open questions for Timothy (unresolved — revisit when this plan restarts)

- Comfortable committing v1 to Path 1 schematic layout (2.5), with general graph auto-layout explicitly deferred?
- OK to append a new "Category E — Electrical Engineering / SPICE Design" section to `Documentation/Brainstorms/003-TOOLSET_IDEAS.md` once this plan converges?
- Once plan 003 ships, does its stateful-toolset pattern (singleton state, `Dictionary`-backed) transfer directly to the circuit-under-construction here, or does anything learned from 003 change this design?

Stage 3 (step-by-step build plan) is not drafted yet. Pick this back up after plan 003.
