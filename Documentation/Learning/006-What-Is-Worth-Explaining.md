2026_07_22_14_00-(What-Is-Worth-Explaining)

# Lecture 006 — What's Worth Explaining: A Curriculum for This Platform

Five lectures now exist on this project (001-005), plus a full staged history in `Documentation/ImplementationPlans/`. This one is different: it isn't teaching a concept — it's an ordered curriculum of the *topics* worth presenting from everything already built, in the sequence that builds the strongest case, each one scoped tightly enough to stand on its own. Every session below points at real files, a real bug, or a real decision — nothing here is hypothetical.

The ordering logic: **lead with the outcome, then earn it.** The first session shows the thing working before anything about architecture, testing, or process is explained — because a working, visually striking system is what earns someone's attention to sit through the reasoning that follows. Everything after that builds the case, in order of what's hardest to fake: a working demo is easy to stage once; a genuine architectural boundary, a real bug found and fixed, and a decision revised on the record are much harder to fake, and that's exactly why they come next.

---

## Session 1 — Lead With the Outcome

**What's shown:** the voxel world, live — an agent given a plain-language build request, calling real tools, a castle assembling itself in a browser in real time (`docs/images/voxel-viewer-castle-grid.jpg` is the still; the real thing moves). No architecture diagram yet, no code yet.

**Why first:** every claim in every later session ("this is well-architected," "this is well-tested," "this decision was reasoned through") is worth more once someone has already seen that the system does something real and visually convincing. Proof before argument.

**Scope discipline:** resist explaining anything here. Show the request, show the result, stop. The explanation is the rest of the curriculum.

---

## Session 2 — The Shape of a Tool Platform

**What's shown:** the Host / Core / Toolset boundary — one composition line (`.AddVoxelToolset()`) is the entire cost of adding the capability from Session 1 to the system. Trace a single tool call through dependency injection: constructor declares what it needs, a container assembles the graph, nothing is `new`-ed by hand.

**Core material:** `Documentation/Learning/003-Objects-Wiring-And-Control.md` — inversion of control, the DI container as a staffing agency, why `new` is glue.

**Why this matters:** distinguishes "I made something work" from "I made something work *and* it's shaped so the next feature costs one line, not a rewrite." The concrete, measurable proof: plan 002 added an entire network transport with zero diffs in `ToolSets/` or `Core/` — a number, not an adjective.

---

## Session 3 — One Binary, Two Wires

**What's shown:** the same server running two completely different ways — `stdio` for a local client, streamable HTTP for a containerized one — selected at startup, zero duplicated toolset code. Then the container itself: multi-stage Dockerfile, non-root, a health check that's actually polled in CI.

**Core material:** `Documentation/Learning/002-One-Server-Two-Wires.md`; `docs/DECISIONS.md` ADR-007.

**Why this matters:** most demos are one script running one way. This shows the same capability surviving a real deployment-shape change without touching the logic underneath it — the difference between a script and a platform.

---

## Session 4 — Turning a Sphere Into Cubes

**What's shown:** the rasterizer — `place_sphere(r: 6)` becomes a few hundred coordinates, computed server-side, not the agent painting cubes one at a time. Live-code (or walk through) the sphere/cylinder/cone math, including the deliberate "fudge factor" tuning constants and why they exist.

**Core material:** `Documentation/Learning/004-State-Sockets-And-Shapes.md` §5; `src/ToolSets/ToolBox.Voxel/VoxelRasterizer.cs`.

**Why this matters:** concrete algorithmic thinking with an immediate, visible payoff — and a good moment to name the actual design principle at work (call-economy: describe a *shape*, not five thousand coordinates) rather than leaving it implicit.

---

## Session 5 — Two Paths, One Truth

**What's shown:** the non-obvious part of the whole system — the path that places a block and the path that tells the agent what happened are almost entirely different code, and the agent *never sees the viewer at all*. Trace one `place_box` call all the way to a rendered pixel, then trace `describe_world` separately and show how short that second path really is.

**Core material:** `Documentation/Learning/005-Following-A-Block.md` §1-4.

**Why this matters:** this is the session that separates "can build a feature" from "can reason precisely about a system's data flow" — a subtle distinction most walkthroughs never surface, which is exactly why surfacing it stands out.

---

## Session 6 — What Breaks at Scale

**What's shown:** the thought experiment — what happens to this exact system the moment there are two server instances instead of one. State silently diverges; the live-update broadcast stops reaching everyone; and the fix requires naming real, hard problems: eventual consistency, consensus, the CAP theorem, not as slogans but applied to this specific `Dictionary` and this specific WebSocket list.

**Core material:** `Documentation/Learning/005-Following-A-Block.md` §6.

**Why this matters:** distributed-systems fluency is one of the highest-value, hardest-to-fake signals in a senior-leaning conversation, and this session earns it by reasoning from a system already on screen rather than reciting a textbook definition.

---

## Session 7 — A Bug, Start to Finish

**What's shown:** the WebSocket close-handshake bug, told as it actually happened — a real client threw a real error ("closed without completing the close handshake"), the exact log line that pointed at the cause, the two-line fix, and *why* it was two lines and not a rewrite.

**Core material:** `Documentation/Learning/004-State-Sockets-And-Shapes.md` §9 and §17.

**Why this matters:** a design walkthrough proves understanding; a debugging story proves the understanding is real, because staged bugs are easy to spot and this one wasn't staged — it was found by actually running a real client against a real socket, not by reading the code.

---

## Session 8 — When the Container Wouldn't Build

**What's shown:** two back-to-back, genuinely different container failures and how each was actually diagnosed — first a missing line in a Dockerfile's dependency-copy list (a real bug, caught by CI doing its job), then an `apt-get` mirror timeout from a completely different cause (transient network flakiness, not a config mistake) — and the different fix each one actually required.

**Core material:** the two-part fix session — root-caused via the actual CI log, verified locally afterward rather than trusted on the diff alone.

**Why this matters:** tells apart two failure classes that look identical from the outside ("the build broke") but require completely different responses — patch the actual bug versus route around infrastructure flakiness — and shows the verify-locally-before-trusting-it discipline either way.

---

## Session 9 — Deciding Out Loud

**What's shown:** the architecture decision log — specifically the moment a previously-recorded security assumption ("write tools shouldn't ship over an unauthenticated transport") got revisited on the record the instant it stopped being true, rather than quietly ignored or silently edited. Show the append-only rule itself: a reversed decision gets a *new*, dated entry that references the old one, never an edit.

**Core material:** `docs/DECISIONS.md` ADR-008 and ADR-011; `Documentation/Learning/004-State-Sockets-And-Shapes.md` §14-15.

**Why this matters:** shows judgment being exercised in the open, with reasoning attached, rather than asserted after the fact — the single clearest artifact of engineering maturity in the entire project.

---

## Session 10 — Directing an Engineer That Doesn't Get Tired

**What's shown:** the staged process this entire project was actually built through — written goals, a recorded discussion, a reviewed step-by-step plan, permissioned execution one step at a time, each step's checkpoint verified for real (a real browser, a real socket, a real second process) rather than assumed from a diff. Walk through one plan's actual history in `Documentation/ImplementationPlans/` as the exhibit.

**Core material:** `Documentation/ImplementationPlans/003-Voxel-World-Builder-Toolset.md`, start to finish.

**Why this matters:** an increasingly common, still poorly-answered question is "how do you actually direct powerful tools responsibly, instead of accepting whatever they produce." This session's answer is a process someone can inspect line by line, not an anecdote.

---

## Sequencing logic, made explicit

Sessions 1-4 establish that the thing is real and well-built. Sessions 5-6 establish depth beyond the feature itself. Sessions 7-9 establish that the judgment behind it holds up under scrutiny — a bug survived, a container failure diagnosed correctly, a decision revised honestly. Session 10 is the capstone precisely because it only lands *after* everything before it has already demonstrated the underlying engineering is sound — a process is only as credible as the work it produced.

**What to leave out, on purpose:** the SPICE circuit toolset (`Documentation/ImplementationPlans/004-SPICE-Circuit-Designer-Toolset.md`) is designed but deliberately unbuilt — it belongs in a future curriculum once it exists, not this one. Resist padding this list with anything not yet real; every session above is chosen because it can be shown, not described.
