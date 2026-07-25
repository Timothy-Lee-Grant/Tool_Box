2026_07_24_00_29-(Tool_Box-Project-Capture)

# Tool_Box — Project Capture

## What this document is

This is the single, append-only project-state capture for **Tool_Box**, owned by Timothy Grant. It exists to be copy-pasted, whole, into the context of *other* AI agents/projects — specifically ones helping Timothy with job-search strategy, interview preparation, and resume/portfolio decisions, targeting **software engineer roles at Microsoft** (also relevant: Google, Meta, Amazon, similar large-scale tech companies). It is written for LLM consumption first, human consumption second: dense, headed, tabular where useful, and self-contained — assume the reading agent has never seen this repository and has no other context about Timothy except possibly his `persona.md` (a separate, more general file covering his background, skills, and learning goals across all his projects — this document is Tool_Box-specific and goes deeper on this one project than persona.md does).

**Update discipline:** this file is only appended to, never rewritten, and only when Timothy explicitly asks for a new capture. Each capture below is a dated, self-contained snapshot — later captures should be read as "what changed since the last one," not as corrections to it. If information here conflicts with the actual repository state, the repository is authoritative; treat this file as "true as of its capture date," not as living documentation.

**How to use this document if you are an AI agent reading it:** Timothy is using AI-assisted engineering deliberately and wants that *process* — not just the code — represented as interview material. Sections marked "Interview relevance" or "Debugging story" are pre-packaged answers to likely behavioral/technical interview questions; use them directly rather than inventing generic ones. If asked to help Timothy prep for an interview, pull concrete details from this document (numbers, file names, exact bug root causes) rather than generic paraphrases — specificity is the whole value of this capture.

---

## Capture — 2026-07-24

### 1. Elevator pitch (one paragraph, for a recruiter or resume bullet)

Tool_Box is a C#/.NET 10 implementation of an **MCP (Model Context Protocol) server** — the open protocol Claude Desktop, Claude Code, and a growing set of agent frameworks use to give an LLM real tools to call. Most MCP servers wrap one API; Tool_Box is architected as a **platform**: a thin, domain-ignorant Host, a shared Core of plumbing (bounded output, structured logging, server metadata), and independent **toolsets** that plug in through exactly one line of composition code each. It currently ships two toolsets (trivial connectivity tools, and a stateful live-buildable 3D voxel world with its own WebSocket-broadcast browser viewer), two transports (stdio and streamable HTTP) selected at runtime from one binary, a Dockerized deployment, 77 tests across four projects, and an 11-entry Architecture Decision Record log. It is the "hands" for a sibling project, **LLM_Monitor** (an AI orchestration platform, also Timothy's), which consumes Tool_Box's tools over the network via a LangGraph agent loop.

### 2. Why this project exists (the strategic "why," not just the "what")

Three explicit goals, stated at project inception (see `persona.md` and Documentation/Brainstorms):

1. **Give LLM_Monitor real tools.** LLM_Monitor is Timothy's other major project — an AI orchestration platform (C#/YARP gateway, Python/LangChain+LangGraph, pgvector, Ollama). It needed an actual tool-execution backend for its agent loop; Tool_Box is that backend, consumed over MCP/streamable-HTTP.
2. **Learn packaging and cross-project/cross-language consumption.** Explicit learning goals: dotnet tool packaging, Docker image distribution, NuGet, and — already exercised — a Python (LangChain) client consuming a C# server over a network protocol.
3. **Build a portfolio-grade platform**, demonstrating system architecture, protocol/networking depth, testing discipline, and documented engineering judgment — aimed specifically at Microsoft SWE hiring bars.

A fourth goal emerged and became arguably the project's most distinctive feature: Tool_Box is built through a **deliberate, staged AI-collaboration process** (see §7), with the full design-through-execution log preserved, not summarized after the fact. This is itself a portfolio artifact: a concrete, repeatable answer to "how do you use AI coding tools responsibly as an engineer," backed by primary-source documents rather than a claim.

### 3. What it actually is, technically

An MCP server binary (`ToolBox.Host`) built on the official `ModelContextProtocol` NuGet SDK (never hand-rolled JSON-RPC — see ADR-001), exposing tools over either of two transports from one binary:

```
  MCP client (Claude Desktop / Code / Inspector / LangGraph agent)
        │  stdio  ─or─  streamable HTTP (:8080/mcp, /health)
        ▼
  ToolBox.Host        thin composition root: config → toolsets → transport
        │
  ToolSets/*          independent capability libraries (Basics, Voxel, ...)
        │
  ToolBox.Core        shared plumbing: bounded output, server info, logging rules
```

The Voxel toolset additionally runs its own companion `BackgroundService` broadcasting world-state diffs over a raw loopback WebSocket (`:8090`, independent of the MCP HTTP transport's `:8080`) to a Three.js browser viewer — a one-directional, human-eyes-only channel; the agent itself never sees rendered output, only plain-text tool return values.

### 4. Current state as of this capture (2026-07-24)

| Metric | Value |
|---|---|
| Toolsets shipped | 2 (Basics, Voxel) |
| Tools exposed | 15, catalogued in `docs/TOOL_CATALOG.md` |
| Tests | 77, across 4 test projects (unit, tool-layer functional with zero MCP involved, wire-level integration using the real MCP client SDK) |
| ADRs | 11, append-only, one (ADR-011) explicitly supersedes an earlier one on the record |
| Transports | stdio (default, for Claude Desktop/Code) + streamable HTTP (for containerized/remote consumers) — one binary, runtime-selected |
| CI | GitHub Actions: build+test job, and a Docker job that builds the image and polls a real `/health` endpoint on every push (not just "does it build") |
| Containerization | Multi-stage, non-root, layer-cache-ordered Dockerfile; `docker compose up --build` works standalone |
| Git tags / releases | **None yet** — no version has been tagged; this is explicitly the subject of the in-progress "Release 1.0" plan (§9) |
| Implementation plans completed | 001 (MVP foundation), 002 (HTTP transport + LLM_Monitor integration), 003 (Voxel toolset) — all shipped |
| Implementation plans deferred | 004 (SPICE circuit-design toolset) — fully designed, Stage 3+ intentionally not started, parked until after 003 shipped and now until Release 1.0 direction is decided |
| Implementation plans in progress | 005 (Release 1.0) — Stage 1 outline drafted, Stage 2 discussion not yet started |

### 5. Repository map (for an agent that needs to locate something)

```
Tool_Box/
├── CLAUDE.md                        — meta-instructions for AI collaboration on this repo itself
├── persona.md                       — Timothy's cross-project developer persona (background, goals, learning style)
├── README.md                        — portfolio-quality overview, "what this demonstrates" table
├── src/
│   ├── ToolBox.Host/                — composition root; Program.cs branches by transport
│   ├── ToolBox.Core/                — shared plumbing, no MCP-hosting deps
│   └── ToolSets/
│       ├── ToolBox.Basics/          — ping / server_info / current_time
│       └── ToolBox.Voxel/           — voxel world model, rasterization, tools, WebSocket viewer service
├── tests/                           — ToolBox.Core.Tests, ToolBox.Host.Tests, ToolBox.Basics.Tests, ToolBox.Voxel.Tests
├── viewer/index.html                — static Three.js voxel viewer (no server-side rendering)
├── Dockerfile / docker-compose.yml  — multi-stage build, non-root, healthchecked
├── docs/
│   ├── DECISIONS.md                 — the 11 ADRs, append-only
│   ├── TOOL_CATALOG.md              — every tool, by toolset, with read/write classification
│   └── LLM_MONITOR_INTEGRATION.md   — the consumer-side recipe for the sibling project
└── Documentation/
    ├── Brainstorms/                 — speculative, pre-scoping exploration (3 docs)
    ├── ImplementationPlans/         — 001-005, the full staged design→build→verify logs (see §7)
    ├── Learning/                    — 7 teaching lectures, each grounded in this repo's own code/bugs
    ├── SkillGapAnalysis/            — (currently empty)
    └── ProjectCaptures/             — this file
```

### 6. Skills demonstrated, mapped to concrete evidence (not claims)

| Area | Evidence (file/decision to point to) |
|---|---|
| System architecture under real constraint | Host/Core/Toolset boundary held through a second toolset (Voxel) with genuinely different needs — stateful, write-classified, its own background service — with **zero diffs to `Core/`**, measured not claimed (ADR-003, ADR-010). |
| Protocol & networking depth | One binary serving two MCP transports selected at runtime (ADR-007); a hand-rolled `HttpListener`-based WebSocket server with a *correct* close handshake — a real protocol bug found and fixed, not shipped (§8, Debugging Story 3). |
| Testing discipline | 77 tests, four projects: pure-logic unit tests, tool-layer tests with zero MCP/server involved, and wire-level integration tests booting the real HTTP app and calling it with the real MCP client SDK. |
| CI/CD discipline | CI proven capable of going red before it earned a green badge (a deliberate-break ritual in plan 001); a Docker job that boots the actual container and polls `/health`, not just `docker build`. |
| Engineering judgment under review | 11 dated ADRs, including one (ADR-011) that explicitly revises an earlier security assumption once circumstances changed, reasoning written down rather than silently overridden. |
| Security posture, stated not assumed | HTTP transport threat model documented and re-examined on the record the moment a write-classified toolset made the original read-only assumption stale (ADR-008, ADR-011). |
| Cross-language / cross-project integration | `docs/LLM_MONITOR_INTEGRATION.md` — a Python/LangChain agent consuming this C# server's tools over streamable HTTP via `langchain-mcp-adapters`, including a `MultiServerMCPClient` → `ToolNode` wiring and a troubleshooting table for the failure modes actually hit. |
| Containerization | Multi-stage Dockerfile with deliberate layer-cache ordering (manifests before source), non-root user, and a documented, non-obvious CI fix for an Azure-runner apt-mirror timeout (see Dockerfile comments). |
| API/tool design for LLM consumption | Tools describe *shape* (`place_sphere(radius, ...)`), not raw coordinates — a deliberate "call economy" design so an agent doesn't need thousands of primitive calls to build something; enforced by a reflection test (`DescriptionConventionTests`) that every tool/parameter carries a `[Description]`, because descriptions are the prompt the model actually reads. |
| Directing AI-assisted engineering deliberately | The entire staged process itself (§7) — this is arguably the single most differentiated, interview-relevant skill this project demonstrates, because most candidates cannot show *process* discipline around AI tool use, only output. |

### 7. The engineering process (a differentiator worth explaining explicitly)

Every implementation plan in this repo follows the same five-stage shape, defined once in `CLAUDE.md` and followed for real, not just described:

1. **Stage 1 — Design Documentation.** Timothy states the goal in his own words, before any code or AI proposal exists.
2. **Stage 2 — Discussion.** A recorded, dated, back-and-forth between Timothy and the AI: architecture options, tradeoffs, risk tables, open questions — argued out, not silently decided.
3. **Stage 3 — Implementation Planning.** The AI turns the discussion into a step-by-step plan, itself open to a further discussion sub-round before being locked.
4. **Stage 4 — Implementation.** Timothy grants permission **one step at a time**. Each step gets a dated log entry: what changed, what broke, what was verified, on a real system (a real browser, a real socket, a real second process) — not assumed from reading a diff.
5. **Stage 5 — Final Results, Testing, Verification.** Evidence against the plan's original Definition of Done, item by item.

Why this matters for interviews: it produces a primary-source, timestamped record of real engineering decisions and real failures (see §8) — the opposite of a résumé bullet that can't be interrogated. An interviewer can be pointed at `Documentation/ImplementationPlans/002-HTTP-Transport-And-LLM-Monitor-Integration.md` and see, verbatim, three rounds of an AI-suggested API guess failing against the real SDK before Timothy's process ("read the docs, don't guess from memory") caught it — including the meta-lesson written down at the time, not reconstructed afterward.

### 8. Debugging stories (pre-packaged for behavioral interview answers — "tell me about a bug you found," "tell me about a time you had to debug something tricky")

**Story 1 — the runtime-vs-SDK trap (plan 001).**
*Symptom:* MCP Inspector showed "Connection Error," never completed the JSON-RPC handshake, though the server process visibly started and logged.
*Root cause:* a .NET **11 preview SDK** was installed, which can *compile* a `net10.0`-targeted binary but cannot *run* it — the net10.0 **runtime** must be installed separately. The compiler succeeding masked a runtime mismatch.
*Fix:* installed .NET 10 SDK (10.0.302) and runtime (10.0.10) explicitly; pinned `net10.0` once in `Directory.Build.props` so no project can silently drift to a different target (ADR-006).
*Why it's a good interview story:* it's a subtle, non-obvious distinction (compile-time SDK vs. runtime) that trips up a lot of engineers moving between .NET versions, root-caused methodically rather than by trial-and-error reinstalling things.

**Story 2 — the three-round SDK API-drift saga (plan 002, Step 3).**
*Symptom:* writing integration tests against the `ModelContextProtocol` client SDK, guessed API surface (`SseClientTransport`, `IMcpClient`, `McpClientFactory`) from memory/documentation-adjacent naming conventions. Compile failures came in three separate rounds — first one symbolic name wrong, then (after fixing that) three *more* names wrong that had been masked by the first error suppressing downstream diagnostics in the same method body.
*Root cause:* the actual 1.4.x stable SDK uses different names than the guessed ones (`HttpClientTransport`/`HttpClientTransportOptions` instead of `SseClientTransport`; `McpClient.CreateAsync(transport)` instead of a factory class; a concrete `McpClient` class instead of an `IMcpClient` interface).
*Fix:* stopped guessing after round two, read the SDK's actual documentation, fixed all names in one pass — and picked up two additional improvements the docs surfaced along the way (`Stateless = true` on the HTTP transport; `AllowedHosts` DNS-rebinding guidance, later formalized as ADR-008).
*The generalizable lesson, recorded verbatim at the time:* "one visible error is not evidence of one actual error — a type error early in a method can mask everything downstream of it," and "the plan's own risk table said 'verify against the installed package's docs, not memory' — and the guess happened anyway, twice. The process knew better than the practitioner."
*Why it's a good interview story:* demonstrates both humility (documenting your own process failing, twice, in the artifact itself) and the corrective discipline (stop guessing, go to the primary source) — a strong answer to "tell me about a time you were wrong and how you recovered."

**Story 3 — the WebSocket close-handshake bug (plan 003).**
*Symptom:* a test harness using a real (not mocked) `ClientWebSocket` against the voxel viewer's broadcast service threw `WebSocketException: The remote party closed the WebSocket connection without completing the close handshake` on teardown.
*Root cause:* the server's read loop detected the client's incoming Close frame and returned, after which the socket was disposed — but a `WebSocket` that has *received* a close request without *sending* its own acknowledging close frame leaves the handshake half-finished from the server's side. The client's exception was correctly reporting that the server never held up its end of a two-way protocol contract.
*Fix:* one added line — `await socket.CloseAsync(...)` — issued in direct response to observing the incoming Close message, before breaking the loop.
*The generalizable lesson, recorded verbatim at the time:* "a protocol handshake is a contract between two parties, and code that only handles 'I noticed the other side wants to close' without also doing 'and now I confirm that I'm closing too' will pass every test that doesn't use a real, protocol-conformant peer to check." This is also the argument, made explicitly in this project, for why the test pyramid needs a real-socket integration test layer, not just mocks.
*Why it's a good interview story:* a genuine protocol-level bug (not a typo), found via a real client rather than a mock, with a one-line fix and a correctly generalized lesson about two-way handshakes/contracts — good for networking/distributed-systems-flavored interview questions.

**Story 4 — "the numbers don't add up" (plan 003) — a reasoning-before-debugging story, not a bug at all.**
*Symptom:* a live build sequence (place_box 169 blocks, place_cylinder 414, place_cone 156, then mirror, then remove_box 24) implied a naive total of 715 occupied cells; `describe_world` reported 646.
*Investigation:* the correct first move (taken) was **not** to assume a bug and start debugging, but to re-derive the expected number from the system's own documented semantics first. `VoxelWorld.PlaceBlocks` *overwrites* whatever occupied a coordinate previously rather than additively counting; the cylinder's base ring geometrically overlapped the floor's cells at `y=0`, so some of the 414 "placed" cells were overwrites, not new cells — meaning the true deduplicated total was correctly smaller than the naive per-call sum.
*Why it's a good interview story:* a clean example of resisting the urge to reflexively debug, and instead checking whether an "unexpected" result is actually the correct consequence of a known rule (overwrite-not-add) meeting a specific scenario fact (these shapes overlap) — a mindset question ("tell me about a time a number looked wrong and wasn't") more than a technical one.

**Story 5 — the case-sensitive filename trap (plan 002, Step 5), a cheap but real cross-platform lesson.**
*Symptom:* would have failed, but was caught before it did — the Dockerfile was named lowercase `dockerfile`, which works fine on Timothy's case-insensitive macOS filesystem but would silently fail `docker build` on GitHub's case-sensitive Linux CI runners (which require exactly `Dockerfile`).
*Fix:* renamed pre-emptively, before ever pushing and hitting the failure in the least debuggable place possible — CI, not a local machine.
*Why it's worth keeping:* a real instance of "assume filename case matters everywhere, because somewhere it does" — cheap to tell, demonstrates cross-platform awareness.

**Story 6 — Azure CI runner apt-mirror timeouts (plan 002/Dockerfile), infrastructure-flavored.**
*Symptom:* a real, observed CI failure where every IP for both default Ubuntu apt mirrors (`archive.ubuntu.com`, `security.ubuntu.com`) timed out from a GitHub Actions (Azure-hosted) runner — not a config mistake, an actual reachability problem specific to that hosting environment.
*Fix:* the Dockerfile now `sed`-rewrites the apt sources to Canonical's Azure-hosted mirror (`azure.archive.ubuntu.com`) before `apt-get update`, with a `|| true` so the same Dockerfile still works unmodified on a dev machine using a different mirror (e.g. `ports.ubuntu.com`), plus `Acquire::Retries=5` as a second line of defense.
*Why it's a good interview story:* a real example of environment-specific infrastructure flakiness diagnosed and fixed with a portable, non-invasive workaround rather than a hack that breaks other environments.

**Story 7 — NU1510 redundant package reference (plan 002, Step 3).**
*Symptom:* `dotnet test` failed at restore with `NU1510` (escalated to a hard error by this repo's warnings-as-errors policy): "Microsoft.Extensions.Hosting will not be pruned — automatically available, remove the PackageReference."
*Root cause:* adding a `FrameworkReference` to `Microsoft.AspNetCore.App` (needed for the new HTTP transport) changed what the project already provides transitively — the ASP.NET Core shared framework itself contains `Microsoft.Extensions.Hosting`, so a package reference that was correct back when the Host was a plain console app (plan 001) became redundant the moment the framework reference was added.
*Fix:* deleted the now-redundant `PackageReference`.
*The generalizable lesson, recorded verbatim:* "a dependency change ripples — adding a framework reference redefines 'already included,' and yesterday's correct reference becomes today's redundancy," and this is exactly what the warnings-as-errors policy was bought for: forcing the codebase to state its dependencies honestly rather than carrying a redundant reference silently forever.

### 9. Open design tensions and known, documented limitations (say these out loud in interviews — they read as maturity, not weakness)

- **HTTP transport is unauthenticated by design (ADR-008, re-examined by ADR-011).** The control is network isolation (no published ports outside a trusted network segment), not authentication. This was explicitly re-examined — not silently ignored — when the first write-classified toolset (Voxel) shipped over HTTP, which technically violated ADR-008's own original stated precondition ("auth before any write toolset ships over HTTP"). ADR-011 records the reasoning for proceeding anyway: the isolation controls were always the real protection; read/write classification was never actually load-bearing as a gate. Authentication remains real, explicitly deferred future work, scheduled for "whenever the first non-trusted network appears."
- **Voxel world state is a single global, unlocked singleton (ADR-009).** Two simultaneous HTTP-connected agents would edit the same world; no session scoping exists yet. Documented as a deliberate v1 scope decision ("abstract from evidence, not imagination" — no real multi-client scenario has been observed yet), not an oversight.
- **Plan 004 (SPICE circuit-design toolset) is fully designed but deliberately not started.** A real, EE-flavored differentiator idea (Timothy's actual embedded/hardware background gives him an edge here) with an honest risk assessment already done — the schematic-auto-layout piece is flagged as the one genuinely hard sub-problem (close to an EDA research topic), with a scoped-down v1 approach (constrained left-to-right layout) explicitly chosen over general graph auto-layout, and three concrete spikes identified before committing to a timeline. Parked, not abandoned.
- **No packaging/versioning has shipped yet.** No git tags, no published Docker image to a registry, no `dotnet tool` or NuGet package — despite these being named explicit learning goals in `persona.md`. This is precisely the gap the in-progress Release 1.0 plan (§10 below) targets.
- **No LICENSE file exists yet.** An open decision (portfolio-only vs. genuinely reusable) not yet made — flagged in the Release 1.0 checklist.

### 10. Roadmap / what's next (as of this capture)

- **Plan 005 — Release 1.0** (`Documentation/ImplementationPlans/005-Release-1.0.md`): in progress, Stage 1 (design outline) drafted 2026-07-23, Stage 2 discussion not yet started. Covers three areas: a release checklist (correctness re-verification, doc completeness, first version tag, security re-check), a packaging plan (deciding which of Docker-image/dotnet-tool/NuGet actually ship, rather than forcing all three), and a presentation plan for a YouTube demo video (to be linked from Timothy's resume) with a 7-beat outline already drafted — cold-open on the live castle build, MCP explainer, architecture walkthrough emphasizing the zero-diff Host/Core/Toolset boundary, dual-transport demo, a debugging-story beat (candidates: Story 2 or Story 3 above), the honest-limitations beat (§9), and a close pointing to the repo/ADR log/Learning docs.
- **Plan 004 — SPICE toolset:** parked behind Release 1.0; will resume once release direction is settled, per this capture's §9.
- **Cross-repo:** LLM_Monitor's own implementation plan for actually consuming Voxel-toolset tools live in its LangGraph agent loop (walkthrough already written from Tool_Box's side — `docs/LLM_MONITOR_INTEGRATION.md` — execution belongs to that repo).

### 11. Interview relevance — quick-reference by likely question category

| If asked about... | Point to |
|---|---|
| "Tell me about a system you designed" | §3 architecture, ADR-003 (Host/Core/Toolset boundary), the "zero diffs to Core" claim — it's measured, not asserted, across two toolsets with genuinely different needs. |
| "Tell me about a tricky bug" | §8, any story — Story 3 (WebSocket handshake) is the most protocol/networking-flavored; Story 2 (SDK API drift) is the best "I was wrong, here's how I recovered" story. |
| "Tell me about a security decision" | ADR-008/ADR-011 — isolation-vs-authentication, and the honest on-the-record reversal when a write toolset shipped. |
| "Tell me about working with AI tools / Copilot / agents" | §7, the full staged process — this is the strongest, most differentiated answer available in this project, because it's process evidence, not a claim. |
| "Tell me about a time you disagreed with a design or caught a mistake" | Plan 002's ADR insertion-order slip (the AI initially violated `DECISIONS.md`'s own append-only rule, caught and corrected) or Story 2/7's "the process knew better than the practitioner" framing. |
| "How do you approach testing?" | The three-tier test pyramid (pure unit / tool-layer functional with no MCP / real-wire integration with the actual SDK client) and Story 3's explicit argument for why mocks alone would have hidden the WebSocket bug. |
| "How do you make architecture decisions and keep them documented?" | The 11-entry `docs/DECISIONS.md` ADR log itself — point an interviewer at it directly; it's real, dated, and append-only with a genuine supersession (ADR-011). |
| System-design-style questions about protocol design, tool design for LLMs | The "call economy" tool design principle (§6) — shape-describing tools (`place_sphere`) instead of raw-coordinate primitives, and the `[Description]`-as-prompt discipline enforced by a reflection test. |

### 12. Cross-reference — Timothy's broader context (see persona.md for full detail, not duplicated here)

Timothy currently works as an embedded/firmware software engineer (C/C++, Raspberry Pi, microcontrollers, I2C/SPI, some C#/.NET), targeting a transition into backend/cloud/AI-engineering roles at companies like Microsoft. Tool_Box is one of two active flagship projects (the other is LLM_Monitor, the AI orchestration platform it serves as a tool backend for) that together are meant to demonstrate: backend system architecture, distributed-systems-adjacent thinking, Docker/containerization, CI/CD discipline, and deliberate, well-documented AI-assisted engineering practice — directly targeting the gap between his current embedded background and the backend/cloud/AI roles he's pursuing.
