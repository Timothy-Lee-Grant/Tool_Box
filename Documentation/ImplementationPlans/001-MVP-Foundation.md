2026_07_16_13_40-(MVP-Foundation)

# Implementation Plan 001 — MVP Foundation

Goal of this plan: get Tool_Box from an empty repo to a small, correctly-architected, professionally-built MCP server. Deliberately boring tools; the deliverable is the **base**, not the capabilities.

---

# Stage 1 (Design Documentation)

*Timothy's goals, as stated 2026-07-16:*

Create the first initial viable product. This should not focus on immediately impressive or useful tools. The point is to get the project into a good architecture, build a good base, and get the project into a state where we can start building in a sustainable and professional manner.

Context from prior discussion (Brainstorms 001–003): the eventual goals are (1) feeding tools to LLM_Monitor's agent loop, (2) learning packaging and cross-project consumption, (3) a Microsoft-portfolio-quality platform. This plan intentionally defers all three payoffs in favor of the foundation they stand on.

---

# Stage 2 (Discussion)

**[2026-07-16, AI]** Opening position. Decisions I propose for the MVP, with reasoning — push back on any of these:

1. **Target .NET 10.** It is the current LTS (supported to Nov 2028); .NET 8 and 9 both leave support this November. Starting a new project on a runtime with four months of life would be the wrong professional habit.
2. **Official `ModelContextProtocol` NuGet package (v1.4.x), stdio transport only.** stdio was chosen over HTTP-first in discussion (simplest viable product; zero networking/auth surface). HTTP + LLM_Monitor integration becomes plan 002 — the architecture below is shaped so 002 adds a transport without touching any toolset.
3. **Three projects from day one: `Host`, `Core`, one toolset.** This is the *minimum* structure that forces the boundaries we care about (host knows no domains; toolsets know no protocol). More than three would be speculative.
4. **The first toolset is `ToolBox.Basics` with three deliberately trivial tools** (`ping`, `server_info`, `current_time`). Trivial tools make architecture mistakes obvious, because there is no interesting logic to hide them behind.
5. **"Professional base" means, concretely:** warnings-as-errors + nullable enabled repo-wide, unit tests that test real code, honest CI, a tool catalog doc, and an ADR log (`DECISIONS.md`) recording why choices were made. These are the artifacts a hiring manager actually reads.
6. **One protocol trap worth knowing before we start:** in a stdio server, **stdout belongs to the protocol**. Any stray `Console.WriteLine` or logger writing to stdout corrupts the JSON-RPC stream and produces baffling client failures. All logging goes to stderr. This will be a hard rule in Core's logging setup.

Awaiting Timothy's responses. Stage 3 below is a full draft per Timothy's request (2026-07-16); it remains open to revision through the Stage 3 discussion subsection.

---

Timothy's Response:


1. Yes, I agree we should target dotnet 10

3. In the 001-InitialProjectBrainstorm.md which you created, you said: 
`Building the plugin loader before the second toolset exists. Two concrete toolsets first; abstract from evidence, not imagination.`
Do we need to worry about this?

4. Yes, I like it. Boring and basic.

**[2026-07-16, AI]** On your point-3 question — no, there is no conflict, and the distinction is worth making precise because it's a general principle:

- The warning was about the plugin **loader**: the *mechanism* that discovers and activates toolsets dynamically (config files, assembly scanning, `--toolsets` flags). This plan builds **zero** loader. The Host activates Basics with one explicit line: `services.AddBasicsToolset()` (Step 2.4). When a second toolset exists (plan 002/003), we'll have two real examples to generalize from.
- The three-project **split** is a different kind of decision. Splitting is boundary enforcement — it makes it *impossible* for protocol code and domain code to tangle, and it's cheap and reversible. Abstracting (the loader) is a bet on a pattern you haven't seen twice yet — bets need evidence.

Rule of thumb: *separate early, abstract late.* Folders and projects are free; frameworks are commitments.

Points 2, 5, 6 stand unchallenged. Stage 2 is converged; proceeding to Stage 4 under Timothy's go-ahead (2026-07-16), Step 1 first.

---

# Stage 3 (Implementation Planning)

## Scope

**In:** repo scaffolding, Core library, stdio Host, Basics toolset, unit tests, CI, client integration docs.
**Out (deferred):** HTTP transport, config-driven toolset selection, Docker, packaging/distribution, LLM_Monitor integration, any real toolset. Each gets its own future plan.

## Definition of done

From a fresh clone: `dotnet build` and `dotnet test` pass with zero warnings; the MCP Inspector lists three working tools; Claude Desktop or Claude Code can call `ping` and get `pong`; CI is green on GitHub *and demonstrably runs the real tests*.

## Target structure

```
Tool_Box/
├── src/
│   ├── ToolBox.Host/                # console executable, stdio transport
│   ├── ToolBox.Core/                # shared plumbing (no MCP-hosting deps beyond attributes)
│   └── Toolsets/
│       └── ToolBox.Basics/          # ping, server_info, current_time
├── tests/
│   ├── ToolBox.Core.Tests/
│   └── ToolBox.Basics.Tests/
├── Documentation/                   # (exists)
├── docs/
│   ├── TOOL_CATALOG.md
│   └── DECISIONS.md
├── .github/workflows/ci.yml
├── Directory.Build.props
├── .editorconfig
├── .gitignore
└── ToolBox.sln
```

## Steps

Each step ends at a verifiable checkpoint and waits for Timothy's permission before the next begins.

### Step 1 — Repository scaffolding
- 1.1 Verify prerequisites: `dotnet --list-sdks` shows a 10.x SDK; install if not.
- 1.2 Add `.gitignore` (standard dotnet), `.editorconfig` (dotnet conventions).
- 1.3 Add `Directory.Build.props` applying to every project: `net10.0`, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<ImplicitUsings>enable</ImplicitUsings>`. *(Why a Build.props: one file enforces standards on all current and future projects — the repo-wide rulebook.)*
- 1.4 Create `ToolBox.sln`; create the five empty projects and solution folders; wire `ProjectReference`s (Host → Core + Basics; Basics → Core; test projects → their subjects).
- 1.5 Rewrite `README.md`: what this is, architecture sketch, how to build.
- **Checkpoint:** `dotnet build` succeeds; solution opens cleanly in the IDE.

### Step 2 — ToolBox.Core
- 2.1 `OutputLimiter`: truncates any tool output beyond a configurable budget, appending an honest marker (`…truncated, N more characters`). Every toolset will route string output through this. *(Why first: bounded output is the platform's core discipline; establishing it before any tool exists means no tool ever ships without it.)*
- 2.2 `ServerInfo` type: version (from assembly), loaded toolset names, uptime — consumed by the `server_info` tool and, later, by diagnostics.
- 2.3 Logging conventions: a helper that configures `ILogger` output to **stderr only** (see Stage 2 point 6), with a category per toolset.
- 2.4 Toolset registration convention: each toolset ships one extension method (`services.AddBasicsToolset()`) and registers its tool types there. Host composes toolsets only through these methods. *(Manual composition now; a config-driven loader is plan 003 material — abstract from two examples, not zero.)*
- **Checkpoint:** Core compiles; no MCP server dependencies leak into it beyond what tool attributes require.

### Step 3 — ToolBox.Host (stdio server)
- 3.1 Add `ModelContextProtocol` package (current 1.4.x) to Host.
- 3.2 `Program.cs`: Generic Host builder → stderr logging → `AddMcpServer().WithStdioServerTransport()` → (no toolsets yet) → run.
- 3.3 Verify with MCP Inspector (`npx @modelcontextprotocol/inspector dotnet run --project src/ToolBox.Host`): connects, handshakes, lists zero tools.
- **Checkpoint:** Inspector session screenshot/notes recorded in Stage 4 log.

### Step 4 — ToolBox.Basics toolset
- 4.1 Tool class with `[McpServerToolType]`; three `[McpServerTool]` methods:
  - `ping` — echoes input; proves round-tripping.
  - `server_info` — returns Core's `ServerInfo`; proves DI into tools.
  - `current_time` — ISO-8601 UTC + local; proves a tool with real (if tiny) logic.
- 4.2 Write `[Description]` attributes on every tool and parameter as if documenting for a smart intern — these are the strings the model reasons over.
- 4.3 `AddBasicsToolset()` extension; Host calls it.
- **Checkpoint:** Inspector lists 3 tools; all three calls succeed; `server_info` reports "Basics" as loaded.

### Step 5 — Tests
- 5.1 `ToolBox.Core.Tests`: OutputLimiter edge cases (under/at/over budget, marker accuracy, multibyte safety).
- 5.2 `ToolBox.Basics.Tests`: invoke tool methods directly (they are plain methods — no server needed); assert shapes and Description attributes are present and non-empty. *(That last assertion encodes "descriptions are prompts" as an executable rule.)*
- **Checkpoint:** `dotnet test` green; a deliberately broken assertion fails (proving tests run real code).

### Step 6 — Honest CI
- 6.1 `.github/workflows/ci.yml`: checkout → setup .NET 10 → `dotnet build -warnaserror` → `dotnet test`.
- 6.2 Honesty check (the LLM_Monitor lesson): push a commit with a failing test, confirm CI goes red, revert. Record the red run's link in Stage 5.
- **Checkpoint:** green badge that has been proven capable of being red.

### Step 7 — Client integration + documentation
- 7.1 README section: registering with Claude Desktop (`mcpServers` JSON) and Claude Code (`claude mcp add toolbox -- dotnet run --project …`).
- 7.2 `docs/TOOL_CATALOG.md`: every tool — name, params, output shape, read/write classification (all three: read).
- 7.3 `docs/DECISIONS.md`: ADR-001 language & SDK, ADR-002 stdio-first, ADR-003 three-project layout, ADR-004 stderr logging rule.
- **Checkpoint / MVP acceptance:** from Claude Desktop or Claude Code, ask the model to ping the toolbox and report server info — it does so via tool calls.

## Risks / open questions

- SDK API drift: `ModelContextProtocol` moved fast pre-1.0; docs found online may show stale APIs. Mitigation: code against the 1.4.x package's own XML docs and samples, not blog posts.
- .NET 10 SDK not yet installed on the dev machine (Step 1.1 resolves).
- Naming: solution/namespaces use `ToolBox` while the repo is `Tool_Box`. Acceptable? (Trivial now, annoying to change later.)

## Stage 3 Discussion Subsection

*(Chronological. Timothy reviews the plan above; revisions to the plan happen in-place, with notable changes summarized here.)*

**[2026-07-16, AI]** Initial draft posted. Areas I most want your eyes on: the three-project split (too much? too little?), the decision to defer config-driven toolset loading to plan 003, and the Step 6.2 deliberate-red-CI ritual.

---

# Stage 4 (Implementation)

**[2026-07-16, AI — Step 1: Repository scaffolding]**

Found on arrival: Timothy had already scaffolded Core, Host, and Core.Tests (via `dotnet new`, evidently), chosen a modern `.slnx` solution file, and pre-added the `ModelContextProtocol` 1.4.1 and `Microsoft.Extensions.Hosting` packages to Host (that's Step 3.1 done early — no objection). I completed the gaps rather than redoing his work.

Done:

- `Directory.Build.props` — net10.0, nullable, implicit usings, `TreatWarningsAsErrors`.
- Slimmed all csprojs to remove properties now owned by Build.props. **Important mechanic:** a property set in a csproj silently *overrides* Directory.Build.props, so leaving the duplicates would have made the rulebook decorative.
- `.editorconfig` added.
- Created `ToolBox.Basics` (src/ToolSets/, matching Timothy's folder naming) + `ToolBox.Basics.Tests`; wired Host → Basics reference.
- `ToolBox.slnx` updated: all five projects, `/src/ToolSets/` and `/tests/` folders.
- README rewritten (architecture sketch, build instructions, status).
- Deleted template `Class1.cs`; kept `UnitTest1.cs` as a placeholder until Step 5.

Deviations & flags:

1. **TFM changed net11.0 → net10.0.** The generated projects targeted net11.0 (a preview — Timothy's installed SDK is likely an 11 preview). Stage 2 agreed on the .NET 10 LTS. An 11-preview SDK builds net10.0 targets fine. If net11.0 was deliberate, say so and I'll revert.
2. **Not touched:** `dockerfile` placeholder (deferred scope) and the empty `src/ToolSets/ToolBox.FindToolProject1/2` directories — Timothy, are those placeholders to keep or should they go? (Git won't track empty dirs either way.)
3. **Verification is on Timothy's machine:** my sandbox has no .NET SDK and can't download one (network-restricted), so the checkpoint — `dotnet build` succeeding — needs you to run it. `dotnet test` should also pass (one empty placeholder test).

State: scaffolding complete, build unverified. **Awaiting: build confirmation + permission for Step 2 (ToolBox.Core).**

**[2026-07-16, Timothy]** Build confirmed green. Permission granted for Step 2.

**[2026-07-16, AI — Step 2: ToolBox.Core]**

Created (all under `src/ToolBox.Core/`):

- `OutputLimiter.cs` — static `Limit(text, maxChars)`, default budget 20,000 chars. Appends an honest marker with the exact omitted count; refuses to cut a surrogate pair in half. Edge cases documented for Step 5's tests.
- `ToolsetDescriptor.cs` — a toolset's identity card (`Name`, `Description`).
- `ServerInfo.cs` / `ServerInfoProvider.cs` — version, loaded toolset names, uptime.
- `DependencyInjection/ServiceCollectionExtensions.cs` — `AddToolBoxCore()` and `AddToolsetDescriptor(name, description)`.
- `Logging/ToolBoxLogging.cs` — `UseStderrOnly()`: clears providers, pins console logging to stderr via `LogToStandardErrorThreshold = Trace` (ADR-004).
- csproj: added `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Logging.Console` (both 10.0.10). **No MCP package in Core** — the checkpoint's boundary rule holds: Core and toolsets stay transport-ignorant.

Design choices worth noting (interview-grade rationale):

1. **`TimeProvider` instead of `DateTimeOffset.UtcNow`.** The modern .NET clock abstraction; tests inject a fake and control time. Never call the static clock in code you intend to test.
2. **Descriptors as plain DI singletons, not a mutable registry class.** Anyone needing "what's loaded?" injects `IEnumerable<ToolsetDescriptor>`. Less machinery, no mutation, and the DI container is already the registry.
3. **`TryAddSingleton` in `AddToolBoxCore`** — idempotent, and tests can pre-register fakes that win.

State: Core code complete, compile unverified (sandbox has no SDK). **Awaiting: `dotnet build` confirmation + permission for Step 3 (Host stdio server).** Note Step 3.3's Inspector run needs Node on your machine (`npx @modelcontextprotocol/inspector`).

---

# Stage 5 (Final Results, Testing, Verification)

*(Verification evidence against the Definition of Done, including the honest-CI red-run link.)*
