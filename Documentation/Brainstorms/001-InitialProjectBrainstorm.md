# Tool_Box — Architecture & Roadmap Plan

An MCP server that gives AI agents "hands" on my machines: diagnostics, build/test execution, git, docker, and eventually exotic capabilities (3D rendering, Raspberry Pi hardware). One server, many clients: Claude Desktop, Claude Code, Cowork, and my own agents (LLM_Monitor's LangGraph tool loop).

---

## What problem is being solved?

Today, when an AI helps me troubleshoot, **I** am the tool layer: I run the build, copy the error, paste it back. That is slow and requires me to already know what evidence matters. This project moves the evidence-gathering into tools the agent can call itself.

Secondary goals: learn plugin architecture, packaging, and distribution the way professional infrastructure teams do it — and produce a reusable platform every future project can depend on.

---

## Decisions (and why)

| Decision | Choice | Why / tradeoff |
|---|---|---|
| Protocol | MCP | Open standard; instantly usable by Claude Desktop, Claude Code, VS Code, and my Python agents. |
| Language | **C# / .NET** | Official [ModelContextProtocol SDK](https://www.nuget.org/packages/ModelContextProtocol) is stable (v1.4.x, maintained with Microsoft). Long-running service strengths (DI, hosting, async). Aligns with Microsoft career target and pairs with LLM_Monitor's C# gateway. Python clients can still consume it — the protocol is the boundary, not the language. |
| Protocol implementation | Official SDK, **never hand-rolled JSON-RPC** | The SDK gives attribute-based tool registration (`[McpServerToolType]`, `[McpServerTool]`), stdio + HTTP transports, and Microsoft.Extensions hosting integration. Hand-rolling teaches less than it costs. |
| Repo strategy | **Standalone repo, standalone product** | Not a submodule, not buried in another project. Other projects talk to it over MCP transports. Git submodules add workflow pain and couple versions; a network boundary decouples cleanly. |
| Toolset selection | **Runtime config, not build-time** (initially) | `--toolsets diagnostics,git` or appsettings. Same binary, different deployments. Build-time trimming (per-toolset images) is a later optimization, not a starting requirement. |
| First transport | **stdio** | Local clients (Claude Desktop/Code) launch stdio servers directly. Zero networking, zero auth to get wrong. HTTP comes later for containerized/remote use. |

---

## The critical insight Gemini's plan missed: Docker vs. host visibility

The flagship toolset is **host diagnostics** — CPU, memory, processes, installed SDKs, PATH. A Docker container is deliberately blind to all of that. It sees its own cgroup, its own filesystem, its own process namespace.

So "ship it as a Docker image" cannot be the primary story for this project. There are two deployment shapes, chosen **per toolset**:

| Shape | Transport | Runs where | Right for |
|---|---|---|---|
| Host-native process | stdio | Directly on my machine | Diagnostics, filesystem, git, build/test, processes, Raspberry Pi GPIO |
| Containerized service | Streamable HTTP | docker-compose | Rendering (Three.js), database tools, anything talking to other containers; Docker tools (via mounted `/var/run/docker.sock`) |

Personified: the **stdio server is a local handyman** — he lives in your house and can open every closet. The **HTTP server is a contractor in a trailer out front** — great for jobs you bring to him, but he can't see inside your walls. Don't hire the contractor to check your plumbing.

Consequence: docker-compose targeting specific toolsets (my original question) is valid, but only for the service-shaped toolsets. Diagnostics ships as a host executable.

---

## Architecture

```
   Claude Desktop      Claude Code       LLM_Monitor (LangGraph)
        │                  │                      │
        │ stdio            │ stdio                │ streamable HTTP
        └─────────┬────────┘                      │
                  ▼                               ▼
        ┌─────────────────────────────────────────────────┐
        │                 ToolBox.Host                    │
        │   Program.cs: read config → load enabled        │
        │   toolsets → register tools → start transport   │
        ├─────────────────────────────────────────────────┤
        │  Toolsets (each an independent class library)   │
        │  Diagnostics │ FileSearch │ Git │ Build │ ...   │
        ├─────────────────────────────────────────────────┤
        │  ToolBox.Core: shared plumbing                  │
        │  process runner, output truncation, audit log,  │
        │  permission policy, result types                │
        └─────────────────────────────────────────────────┘
```

Roles (who's who):

- **Host** — the receptionist. Knows nothing about any domain; only reads config, wires up whoever is on today's roster, and answers the phone (transport).
- **Toolset** — a specialist on the roster. The Git specialist doesn't know the Docker specialist exists.
- **Core** — the office manager. Provides the shared services every specialist needs (safe process execution, logging, output limits) so no one reinvents them.
- **The LLM** — the caller. It never executes anything; it asks specialists questions and dispatches jobs.

---

## Repository layout

```
Tool_Box/
├── src/
│   ├── ToolBox.Host/                 # executable (console app, stdio + HTTP)
│   ├── ToolBox.Core/                 # shared abstractions (class library)
│   └── Toolsets/
│       ├── ToolBox.Diagnostics/      # first toolset
│       ├── ToolBox.Git/              # later
│       └── ToolBox.Build/            # later
├── tests/
│   ├── ToolBox.Core.Tests/
│   └── ToolBox.Diagnostics.Tests/
├── docs/
│   ├── TOOL_CATALOG.md               # every tool: name, args, output shape
│   └── DECISIONS.md                  # ADR-style log, like LLM_Monitor's plans
├── docker/                           # only for HTTP-shaped deployments
├── ToolBox.sln
└── README.md
```

### Project mechanics (answering my own .csproj questions)

- **`.csproj` = the build unit.** One project → one output: a class library → `ToolBox.Diagnostics.dll`, the host → `ToolBox.Host` executable. Yes, the dot in `ToolBox.Diagnostics` is just a naming convention; the `.csproj` file is what makes it a project.
- **`.sln` = a developer convenience.** It groups projects so `dotnet build` / the IDE build them together and resolve `ProjectReference`s. It does **not** handle versioning — versions live in each `.csproj` (`<Version>`), and matter only once you publish packages.
- **`ProjectReference` vs `PackageReference`:** inside this repo, projects reference each other by path (ProjectReference). External consumers would use NuGet (PackageReference). You graduate a project from one to the other when outsiders need it — that's all "making a NuGet package" is.

---

## Tool design rules (this is where quality lives)

These matter more than the architecture. An agent-facing tool is an API whose consumer has a context window, not a debugger.

1. **Return structured, bounded output.** Never dump 50MB of logs. Core provides truncation with an honest marker: `"...truncated, 41,203 more lines. Use offset param."`
2. **Errors are data, not crashes.** A failed build is a *successful* tool call whose payload says the build failed and why. Reserve protocol errors for "the tool itself broke."
3. **Descriptions are prompts.** The `[Description]` on each tool/parameter is what the model reads to decide when and how to call it. Write them like documentation for a smart intern.
4. **Read-only by default.** Tag each tool read/write. Host config can run a toolset in read-only mode (`git_diff` yes, `git_commit` no). This is the professional security posture — and a great interview talking point.
5. **Audit everything.** Core logs every invocation: tool, args, duration, outcome. (This is LLM_Monitor's telemetry-middleware instinct applied here.)
6. **15–20 excellent tools before breadth.** Consistency across toolsets (naming, error shape, pagination) is the signature of a platform.

---

## Toolset roadmap

| Order | Toolset | Example tools | Shape |
|---|---|---|---|
| 1 | Diagnostics | os_info, cpu/memory, disk, list_processes, env_vars, installed_sdks, port_check | stdio, host |
| 2 | Build & Test | run_build, run_tests, parse_errors_warnings (dotnet, pytest, npm) | stdio, host |
| 3 | Git | status, diff, log, branch info (read-only first) | stdio, host |
| 4 | Docker | list containers, logs, inspect, compose status | stdio host or HTTP w/ socket mount |
| 5 | Rendering | create_scene, add_object, render_image (headless Three.js via a Node sidecar) | HTTP, container |
| 6 | Raspberry Pi | GPIO, I2C, SPI, camera | stdio on the Pi itself — same binary, different config. This is the payoff of runtime toolset selection. |

Toolsets 1–2 alone solve the original copy-paste-the-error problem.

---

## Phased plan

### Phase 0 — Hello, hands (a weekend)
One solution, `ToolBox.Host` + `ToolBox.Core` + `ToolBox.Diagnostics` as separate projects from day one (cheap now, painful to retrofit). Three tools. stdio transport. Wire into Claude Desktop config / `claude mcp add` and **watch an AI read your machine's vitals**. Test interactively with the MCP Inspector (`npx @modelcontextprotocol/inspector`).

- Definition of done: I ask Claude "why is my machine slow?" and it answers from tool calls, not from me pasting `top` output.

### Phase 1 — The troubleshooting loop (2–3 weeks)
Finish Diagnostics (~8 tools) + Build & Test toolset. Add Core's process runner, truncation, audit log, read-only policy. Unit tests + CI (the *honest* kind — lesson already learned on LLM_Monitor). TOOL_CATALOG.md.

- Definition of done: point Claude Code at a broken project; it builds, reads errors, and proposes fixes without me pasting anything.

### Phase 2 — Platform shape (weeks 4–6)
Runtime toolset selection via appsettings/`--toolsets`. Add Git toolset to prove the third-toolset pattern holds. Add streamable HTTP transport (`ModelContextProtocol.AspNetCore`) behind a flag. **Integrate with LLM_Monitor**: add `toolbox` to its compose (or run host-side) and consume it from the LangGraph tool loop via [langchain-mcp-adapters](https://github.com/langchain-ai/langchain-mcp-adapters)' `MultiServerMCPClient` — the C#-server/Python-client question answered in practice.

### Phase 3 — Distribution (when someone else could use it)
In order of increasing ceremony:

1. **Run from source** — `dotnet run --project src/ToolBox.Host` (fine for months).
2. **.NET global tool** — `dotnet tool install -g toolbox-mcp`. NuGet-based, but ships an *executable*, not a library. Likely the sweet spot for stdio servers.
3. **Self-contained / AOT executable** — GitHub Releases; no .NET runtime needed on the target.
4. **Docker image** — for the HTTP-shaped toolsets only.
5. **NuGet *library* packages** (`ToolBox.Core`, plugin SDK) — only if/when third parties want to write toolsets in-process. Don't do this speculatively.
6. **MCP registry listing** — discoverability, once stable.

### Phase 4 — Exotic capabilities
Rendering, Raspberry Pi, whatever's fun. By now they're just new folders under `Toolsets/`.

---

## Packaging cheat sheet (the general question)

| Mechanism | You ship | Consumer | Use when |
|---|---|---|---|
| NuGet / PyPI / npm library | Code to link into *their* process | Developers | Reusable logic, SDKs |
| dotnet tool / npx / pipx | CLI executable via a package manager | Developers | Dev tools, **stdio MCP servers** |
| Self-contained binary | Executable, batteries included | Anyone | No-runtime-assumed distribution |
| Docker image | Frozen environment | Operators | Services, reproducible deployment |
| Installer (MSI/PKG) | Executable + system integration | End users | GUI desktop apps — not this project |

Rule of thumb: **library → package manager; process → executable or image.** Tool_Box is a process, so NuGet libraries are Phase-3-optional, not the core plan. (My instinct in question 8 was correct.)

---

## Common mistakes to avoid (aimed at me specifically)

- **Building the plugin loader before the second toolset exists.** Two concrete toolsets first; abstract from evidence, not imagination.
- **Understanding-completeness paralysis.** Use the SDK's attributes without reading the SDK's internals first. Budget: ship a working tool, *then* allow one deep-dive. The abstraction-trust muscle is the skill being trained here.
- **Trusting model-supplied arguments.** A path argument can be `../../.ssh/id_rsa`. Validate and sandbox at the tool boundary — the LLM is an untrusted caller.
- **Unbounded output.** One `cat` of a giant log destroys the agent's context and the demo.

## Interview relevance

Plugin architecture + DI, config-driven deployment, transport abstraction (stdio vs HTTP), security posture for AI-executed actions, packaging/distribution tradeoffs, and a live demo where Claude diagnoses a machine through code I wrote. Pairs with LLM_Monitor as "I built the brain's environment *and* its hands."
