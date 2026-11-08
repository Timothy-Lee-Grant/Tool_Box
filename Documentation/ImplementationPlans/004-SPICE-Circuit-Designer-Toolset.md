2026_07_20_09_00-(SPICE-Circuit-Designer-Toolset)

# Implementation Plan 004 — SPICE Circuit Designer Toolset

**Status: reopened 2026-07-26.** Plan 003 has shipped. Timothy restarted this plan and asked for a concepts/feasibility document before any implementation begins — that document is `Documentation/Learning/008-The-Solver-And-The-Draftsman.md`, with runnable verification code in `Documentation/Learning/008-Spikes/`. Stage 2 continues below at §2.11. Stage 3 is still not drafted and should not begin until the §2.11 open questions are answered and spike #2 (ngspice round-trip) has actually been run.

*Prior status, for the record:* split out of plan 003 on 2026-07-20, which originally covered this toolset alongside the Voxel World Builder. Timothy asked to focus on the simpler toolset (003) first; this document preserved the design work already done so it wasn't lost.

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

---

## 2.11 Spike results and design revisions (2026-07-26, AI)

**[2026-07-26, AI]** Plan 003 shipped, so this discussion resumes. Timothy asked for a concepts and feasibility document before implementation — written as `Documentation/Learning/008-The-Solver-And-The-Draftsman.md`, with the verification code saved and re-runnable in `Documentation/Learning/008-Spikes/`. What follows is what the spikes changed about the §2.1–§2.10 position above. Most of it survived; three things did not.

### What was verified, and what wasn't

`ngspice` could not be installed in the environment this work was done in (no package-install privileges). So the numerical claims were verified a different way: by building a Modified Nodal Analysis solver from scratch and cross-checking it against `ahkab`, a pure-Python SPICE-family simulator. **ngspice's own behaviour remains unverified and is now the top-priority spike** — see "revised spike list" below. Lecture 008 labels every claim accordingly; that labelling should be preserved, not smoothed over.

Verified: the §2.7 worked example lands exactly where it predicted (`V(vout) = 3.333333 V`, `I(V1) = -0.0151515 A`, from the raw matrix and from ahkab independently); the two classic SPICE errors are both matrix singularity with *different* remedies; Newton-Raphson needs step limiting (173 iterations vs 12, and hard `exp()` overflow above 18.33 V); implicit integration is required for stability, not accuracy (forward Euler produced −75 V in a 5 V circuit); schemdraw renders cleanly via a subprocess in ~58 ms.

### Revision 1 — §2.5's schematic risk was under-rated. Raise it, and change the mitigation.

§2.5 called this "Medium–High" and said "scope it hard." Having run it, **High** is the honest score, and "scope it hard" isn't specific enough to act on.

The spike renders both cases. A **series divider** renders at publication quality. A **Wheatstone bridge** — five resistors, still trivially small — came out with three colliding labels and a diagonal cutting through the middle of the figure, *and I placed every element by hand while looking at the output*. The variable that broke it is not size, it's **topology**: the divider is a series chain, the bridge has a cross-link.

The deeper finding is that §2.5 mis-identified where the risk lived. It treated the sidecar as the risky part; the sidecar is fine. **schemdraw is a turtle-graphics library** — each element starts where the last ended and goes the direction you specify. It draws symbols excellently and solves *none* of the layout problem, because the layout problem is precisely "produce that direction list from a graph." §2.5's Path 1 ("place components in call order, break to a new row at parallel branches") is closer to right than wrong, but it was written as a heuristic to be tuned rather than a constraint to be enforced.

Replace Path 1 / Path 2 with three tiers (Lecture 008 §8.5):

- **Tier A — topology-constrained rendering.** Classify the graph first. Series chain and ladder/parallel-rail get real layouts. **Tier 2 composites ship hand-authored layout templates** — they already know their own shape, so auto-layout only ever handles what wasn't built from a template. This is the highest-value idea from the spike and it wasn't in §2.5.
- **Tier A′ — an explicit refusal path.** Unrecognized topology returns *"this topology isn't one I can lay out readably; the netlist is at ./x.cir, open it in KiCad or LTspice."* An honest refusal beats an unreadable image, for the user and for the agent, which can then say something true. This needs to be built, not treated as an error case.
- **Tier B — reframe the deliverable.** The `.cir` file is the durable, portable, professional artifact; it opens in KiCad, LTspice and Xyce. The rendered schematic is a *preview*. This reframing costs nothing and removes the project's dependence on an open research problem.
- **Tier C — general auto-layout.** Explicitly deferred to its own plan, as §2.5 already said. Confirmed correct.

ADR to write when this ships, in ADR-008's spirit (a real limitation, written down, not discovered by a user): *"Schematic rendering is topology-constrained; unrecognized topologies refuse rather than render."*

### Revision 2 — a missing line item: the model library

§2.3's tool list has `add_transistor` and `add_opamp` but never says where device models come from. Two findings make this its own deliverable:

1. **Many manufacturers publish SPICE models only in encrypted form**, locked to LTspice or PSpice. **ngspice cannot read them, at all.** So "simulate this specific real part" is not a general capability — it's conditional on an unencrypted model existing.
2. **The agent will confidently invent `.model` parameters.** It has seen thousands of `.model` lines and will emit a plausible `2N3904` with wrong values. The simulation runs. The numbers are wrong. Nothing errors. This is Lecture 007's confident-error mode in a domain where output *looks* authoritative because it has units and six significant figures.

**Decision proposed:** ship a curated `ModelLibrary` and expose `list_models()`. Component tools take a *model name from that library*, never a free-text `.model` line. **The agent may not author device physics.** Same shape as `Materials.Validate(material)` in `VoxelTools` — a closed vocabulary chosen from rather than invented — which is a good sign the pattern established in 003 was worth establishing.

Relatedly: **`run_raw_netlist(text)` should be named and rejected in an ADR.** It's trivially easy, and it collapses the whole design into "LLM writes SPICE" — discarding the closed vocabulary, the server-side formulas, and every validation. Worth recording as considered-and-rejected so a future reader knows it wasn't overlooked.

### Revision 3 — §2.3's tool tiers are a *correctness* boundary, not just call economy

§2.3 argued for Tier 2 composites on call-economy grounds, by analogy to `place_sphere`. That argument is right but undersold. `add_rc_lowpass(cutoff_hz: 1000)` computes R and C from `f = 1/(2πRC)` **in C#, deterministically** — instead of asking a language model to do arithmetic and hoping.

**Every formula moved into a Tier 2 tool is a class of confident-error permanently eliminated.** That converts "composites save calls" (a performance argument) into "composites are where correctness lives" (an architecture argument). The second is much stronger and should be how Stage 3 justifies the tier split.

Same promotion applies to `check_node_voltage` / `check_current`. §2.3 lists them among the simulation tools; they deserve to be designed **first**. They are the mechanism that makes this a closed-loop agentic tool rather than a wrapper — the voxel toolset's only correctness oracle was a human looking at the viewer, and this toolset's is a numerical solver the *agent* can consult mid-task. That difference is the strongest reason to build this at all, and it should lead the Stage 3 ordering.

### Smaller additions to §2.4 and §2.6

- **`.control` blocks do not auto-run under `-b`.** If a `.control` section is present, the analysis does *not* execute unless the block explicitly contains `run`. Failure mode is an empty output file. Every emitted netlist needs `run` as the first line inside `.control`. **[unverified — spike it]**
- **`.meas` should be used, and it strengthens the §2.6 bounded-output argument.** ngspice can compute rise/fall time, delay, min/max/pp/RMS/average and threshold crossings *inside* the simulation and print a scalar. Instead of returning 10,000 samples for the agent to reason over badly and expensively, return `trise = 2.31e-6`. Same instinct as `place_box` computing coordinates server-side: push the computation to where it's cheap, hand the agent the conclusion. §2.6's decimation plan is still right for raw waveforms; `.meas` is the better answer wherever the question is actually a measurement.
- **`wrdata` writes an x-column per y-column** (time, v1, time, v2, …) rather than one shared x-axis. Confirm the exact shape in the spike rather than assuming. **[unverified]**
- **Transient results are non-uniformly sampled**, because SPICE rejects and halves timesteps adaptively. A parser assuming uniform sampling will be wrong.
- **`CultureInfo.InvariantCulture` on every numeric format.** On a `de-DE` machine `double.ToString()` emits `1,5`, which SPICE mangles silently. Needs a test that sets `CurrentCulture` to `de-DE` and asserts byte-identical netlist output.
- **Enable ngspice SOA (Safe Operating Area) warnings.** Cheap, and it catches the class of error the agent will not catch itself — a "working" design dissipating 40 W in a quarter-watt resistor.
- **§2.4's shell-out-don't-bind decision is confirmed, with a sharper reason:** `libngspice` has process-global state and a history of being awkward to reset between runs. A non-converging child process is a non-zero exit code; the same thing in-process can take the MCP server down. **Process isolation is the feature, not the compromise.** This also means spike #5 below (timeout and kill) is not optional — it's the difference between a failed simulation and a hung server.

### Revised spike list (replaces §2.9)

| # | Spike | Status |
|---|---|---|
| 1 | Rich content from `[McpServerTool]` | **✅ resolved by docs.** The C# SDK maps return types automatically: `string` → text, `IEnumerable<ContentBlock>` → several blocks, `CallToolResult` → as-is. Images via `ImageContentBlock.FromBytes(bytes, "image/png")`. *Caveat:* an open csharp-sdk issue reports `EmbeddedResourceBlock` not rendering in Claude Desktop — verify `ImageContentBlock` specifically in the demo client before building on it. |
| 2 | ngspice batch + `wrdata` round-trip | **🔴 still open, now top priority.** Confirm: `.control` needs `run` under `-b`; exact `wrdata` column layout; exit code on non-convergence vs. parse error; whether errors go to stdout or stderr; behaviour on a deliberately singular circuit. Run on the dev machine **and** in the container — package versions differ. |
| 3 | ~~schemdraw via subprocess~~ | **✅ done — but it tested the wrong thing.** The sidecar was never the risk; layout was. **Replacement spike:** take five circuits a user would plausibly ask for and hand-classify which Tier A template each falls into. If fewer than four are covered, the rendering scope needs rethinking *before* Stage 3, not during it. |
| 4 | Locale test | **🔴 new.** Emit a netlist under `de-DE`, assert byte-identical output. |
| 5 | Timeout and kill | **🔴 new.** Feed ngspice a deliberately non-converging circuit; confirm the `Process` wrapper kills it and returns a useful message rather than hanging the server. This is the one that becomes an incident if skipped. |

### Revised effort/risk table (replaces §2.8)

| Piece | Effort | Risk | Change |
|---|---|---|---|
| Circuit model + netlist emission | S | Low | Unchanged — add the `InvariantCulture` test |
| ngspice process + CSV results | S–M | Low–Med | Unchanged; `.control`-needs-`run` now a known trap |
| Tier 1 + Tier 2 tools | M | Low | **Reframed** — the correctness boundary, not just call economy |
| Model library | S | Low | **New line item** (Revision 2) |
| Waveform PNG (ScottPlot) | S | Low | Unchanged — confirmed no sidecar needed |
| Schematic SVG | M | **High** | **Raised** from Medium–High; mitigation changed to Tier A + refusal |
| Result assertions (`check_*`) | S | Low | **Promoted to first-class** — the agentic core |
| Docker/CI (ngspice + python + schemdraw) | S | Low | Unchanged |

Still **M** overall for a v1 that stops at Tier A schematics. §2.5 was right that this is the one piece that could balloon; the spike just made the containment strategy specific.

### §2.10's open questions, updated

- **Path 1 schematic layout** — supersede the question. The new ask is: *is Tier A + an explicit refusal path acceptable*, given the bridge render shows even hand-placement fails on cross-linked topologies?
- **Category E in `Brainstorms/003-TOOLSET_IDEAS.md`** — still open, still proposed.
- **Does 003's stateful pattern transfer?** — **yes.** ADR-009's singleton `VoxelWorld` maps directly onto a singleton `Circuit`, with the same "one client, one process" caveat to record. ADR-010's companion-`IHostedService` pattern is available if a live browser schematic viewer is wanted later. Nothing learned from 003 contradicts the §2.2 design.

### New open questions for Timothy

1. **Model library scope for v1** — generic parts only (R/C/L/D/ideal op-amp/generic BJT+MOSFET), or bundle a handful of real, unencrypted parts too? Generic-only is smaller and honest; real parts are more demo-able.
2. **Is `run_raw_netlist` rejected for v1?** (Recommend: yes, and record the reasoning in an ADR.)
3. **Which analyses ship in v1?** `.op` + `.tran` is the smallest set that demonstrates the closed loop. `.ac` adds real value for filters but brings the small-signal caveat the agent has to be told about (Lecture 008 Part 6).
4. **Does the ngspice licensing mix** (BSD-ish Berkeley core, GPL contributed parts) need a real look before it goes into the public GHCR image? Tool_Box publishes one; this is the first dependency where it matters.

Stage 3 (step-by-step build plan) is still not drafted. Draft it once questions 1–4 are answered and spike #2 has been run for real.
