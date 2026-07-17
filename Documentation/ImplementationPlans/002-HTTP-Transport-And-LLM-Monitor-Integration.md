2026_07_16_16_27-(HTTP-Transport-And-LLM-Monitor-Integration)

# Implementation Plan 002 — HTTP Transport & LLM_Monitor Integration

Goal of this plan: give the ToolBox a second transport (streamable HTTP), a Docker deployment shape, and its first *real consumer* — LLM_Monitor's LangGraph agent loop. This is the plan where the platform stops being a demo for chat clients and becomes infrastructure another system depends on.

---

# Stage 1 (Design Documentation)

*Goals, drafted by AI from Timothy's stated project priorities (2026-07-16) — Timothy: edit/confirm.*

1. **Feed LLM_Monitor** (project goal #1): the LangGraph agent (policy check → RAG → tool loop) must be able to discover and call ToolBox tools over the compose network, in both mock and live modes.
2. **Learn packaging** (project goal #2): this plan deliberately delivers the first rung of the packaging ladder — a proper multi-stage Docker image — and the first taste of configuration-driven behavior (transport selection), which plan 003's config-driven toolset loading will build on.
3. **Preserve plan 001's foundation**: toolsets and Core must not change at all. If adding a transport touches a toolset, ADR-003's boundary was a lie; this plan is that boundary's first real test.
4. Non-goals (explicitly out): authentication (documented as a risk, scheduled later), new toolsets, config-driven toolset selection, NuGet packaging, `dotnet tool` distribution.

---

# Stage 2 (Discussion)

**[2026-07-16, AI]** Opening position — the decisions this plan turns on, with my reasoning. Push back before Stage 3 execution begins:

1. **Both transports live in one Host binary, selected at startup.** `--transport stdio|http` (plus `TOOLBOX_TRANSPORT` env var for containers, plus appsettings default = stdio). Alternative rejected: a second Host project (`ToolBox.Host.Http`) — that duplicates composition and packaging for zero benefit at this stage. One binary, different modes, is also the pattern the Raspberry Pi dream requires.
2. **`ModelContextProtocol.AspNetCore` for the HTTP path.** The official SDK's HTTP package: `AddMcpServer().WithHttpTransport()` + `app.MapMcp()`. Streamable HTTP is the current MCP standard for remote servers (SSE-based transport is its predecessor). We write zero protocol code, same as stdio.
3. **The stderr logging rule stays global, even though HTTP mode doesn't need it.** In HTTP mode stdout is harmless (Docker captures both streams identically). Carving out a per-transport logging exception buys nothing and creates a trap for the next stdio change. Uniform rules are cheaper than clever ones. ADR-004 gets a note, not an amendment.
4. **Security posture v1: unauthenticated, network-isolated.** Inside LLM_Monitor's compose network, the ToolBox binds `0.0.0.0` but the port is *not* published to the host except behind a dev-only compose profile (mirroring LLM_Monitor's own "lockdown is a config change" pattern — deleting one port mapping). An unauthenticated MCP server must never be exposed beyond a trusted network; this gets its own ADR so the risk is written down, not just known.
5. **Integration tests use the SDK's own client.** The `ModelContextProtocol` package contains a *client* too. A new test project boots the HTTP host on a random localhost port and connects with the real client: list tools, expect 3; call `ping`, expect `pong`. This tests the actual wire, not a mock of it — and it runs in CI with no Docker needed.
6. **Cross-repo boundary:** this plan changes Tool_Box and *documents* the LLM_Monitor side as a walkthrough (Step 6). The actual LLM_Monitor changes should go through that repo's own AI_Implementation_Plans process — two repos, two process trails, which is itself the professional norm.
7. **Container healthcheck via a plain `/health` endpoint**, not an MCP call — compose healthchecks are curl-shaped, and LLM_Monitor's startup-ordering pattern (gateway waits on healthy services) can then adopt the ToolBox with one `depends_on: condition: service_healthy` line.

Open questions for Timothy before/while Stage 3 executes:

- Q1: Should the dev port mapping (`localhost:8081 → toolbox:8080`) exist at all, or is Inspector-in-compose enough? (I propose yes, behind a `dev` profile — you'll want Inspector against the containerized server.)
- Q2: In LLM_Monitor, should ToolBox tools be available in **mock mode** too? (I propose yes — the tools themselves are real and cheap; only *models* are mocked in that system.)
- Q3: Base image: `mcr.microsoft.com/dotnet/aspnet:10.0` (my proposal; ~110 MB, boring, correct) vs. Alpine/chiseled variants (smaller, more edge cases). Boring first?

---

# Stage 3 (Implementation Planning)

## Scope

**In:** transport selection, HTTP host path, `/health`, integration test project, multi-stage Dockerfile + .dockerignore + local compose file, CI docker-build step, LLM_Monitor integration walkthrough, ADRs 007–008, README/catalog updates.
**Out:** auth, new toolsets, config-driven toolset loading, publishing images to a registry, NuGet/dotnet-tool packaging.

## Definition of done

1. `dotnet run --project src/ToolBox.Host -- --transport http` serves MCP at `http://localhost:8080/mcp`; Inspector connects over streamable HTTP and lists 3 tools.
2. stdio mode still works exactly as in plan 001 (purity test still silent; Claude Desktop still connects).
3. Integration tests: SDK client over real HTTP lists 3 tools and round-trips `ping` — green locally and in CI.
4. `docker compose up` in Tool_Box builds the image and reports **healthy**; Inspector connects through the dev port.
5. From LLM_Monitor's compose network, a LangGraph agent lists ToolBox tools via `langchain-mcp-adapters` and successfully calls `ping` (evidence: pytest or logged agent run).
6. ADR-007 (dual transport) and ADR-008 (v1 security posture) recorded; README documents both transports.

## Architecture after this plan

```
                  ┌──────────────── stdio ───────────────┐
 Claude Desktop / │                                      │
 Claude Code ─────┘        ToolBox.Host                  │
                     ┌──────────────────────────┐        │
                     │ TransportSelector        │◄── --transport / TOOLBOX_TRANSPORT / appsettings
                     │   ├─ stdio path (001)    │
                     │   └─ http path (NEW)     │
                     │ AddToolBoxServer() ◄──── shared composition: Core + all toolsets
                     └──────────┬───────────────┘
                                │ streamable HTTP :8080/mcp   + /health
              ┌─────────────────┼───────────────────┐
              │ docker network: llm_monitor_default  │
              │                 ▼                    │
              │   langchain_service (Python)         │
              │   MultiServerMCPClient ──► LangGraph │
              │   tool loop (agent's "hands")        │
              └──────────────────────────────────────┘
```

The critical invariant: **`src/ToolSets/**` and `src/ToolBox.Core/**` end this plan with zero diffs.** That's the measurable proof of ADR-003.

## Steps

Each step ends at a verifiable checkpoint and waits for Timothy's permission.

### Step 1 — Extract shared composition; add transport selection
- 1.1 New file in Host: `ToolBoxServerComposition.cs` — one extension method `AddToolBoxServer(this IServiceCollection)` containing what today lives inline in `Program.cs`: `AddToolBoxCore()` + `AddMcpServer()` + `.AddBasicsToolset()` (returning the `IMcpServerBuilder` so each path can attach its transport). *(Why: the two transport paths must share one definition of "what this server is"; duplicating the toolset list would rot immediately.)*
- 1.2 Transport selection precedence, standard .NET config layering: command line `--transport` > env `TOOLBOX_TRANSPORT` > `appsettings.json` (`"Transport": "stdio"`). Add `appsettings.json` to Host (copy-to-output).
- 1.3 `Program.cs` branches: `stdio` → exactly today's generic-host path; `http` → Step 2's path; unknown value → fail fast with a clear stderr message and non-zero exit (never guess a transport).
- **Checkpoint:** stdio mode byte-for-byte behavior unchanged: purity test silent, Inspector lists 3 tools, all 22 tests green.

### Step 2 — The HTTP path
- 2.1 Add `ModelContextProtocol.AspNetCore` to Host. Host's SDK stays `Microsoft.NET.Sdk` with a `FrameworkReference` to `Microsoft.AspNetCore.App` — or switch to `Microsoft.NET.Sdk.Web`; decide by what the SDK package documents as canonical (verify against the package README at implementation time, not blog posts — ADR-001's discipline).
- 2.2 HTTP branch: `WebApplication.CreateBuilder` → `UseStderrOnly()` (uniform rule) → `AddToolBoxServer().WithHttpTransport()` → `app.MapMcp("/mcp")` → `app.MapGet("/health", …)` returning `{ status: "ok", toolsets: [...] }` from `ServerInfoProvider` → listen on `ASPNETCORE_URLS` (default `http://localhost:8080` outside containers; compose sets `http://0.0.0.0:8080`).
- 2.3 Manual verification: `--transport http`, then Inspector with transport "Streamable HTTP" → `http://localhost:8080/mcp` → 3 tools, all callable; `curl localhost:8080/health` → ok.
- **Checkpoint:** both transports verified by hand; zero diffs under `src/ToolSets/` and `src/ToolBox.Core/`.

### Step 3 — Integration tests (`tests/ToolBox.Host.Tests`)
- 3.1 New xunit project referencing Host + the SDK's client types.
- 3.2 Fixture: start the HTTP host on port 0 (ephemeral), capture the bound URL, tear down cleanly (`IAsyncLifetime`).
- 3.3 Tests: client connects + handshake succeeds; `tools/list` returns exactly `ping`, `server_info`, `current_time`; `ping("integration")` round-trips `"pong: integration"`; `server_info` reports `Basics`; `/health` returns 200.
- 3.4 These join `dotnet test` — CI covers them with no workflow change.
- *(Teaching note: this is the test-pyramid middle layer plan 001 didn't need — unit tests prove the methods, these prove the wire. The stdio transport gets no equivalent because the Inspector + purity test cover it manually; automating PTY-style stdio tests costs more than it returns right now.)*
- **Checkpoint:** `dotnet test` green (22 unit + ~5 integration); a deliberately broken assertion fails (honesty spot-check).

### Step 4 — Containerization
- 4.1 Replace the placeholder `dockerfile` with a multi-stage `Dockerfile`:
  - build stage: `mcr.microsoft.com/dotnet/sdk:10.0` — restore (solution-level, layer-cached), publish Host Release;
  - runtime stage: `mcr.microsoft.com/dotnet/aspnet:10.0` (per Stage 2 Q3), non-root user, `TOOLBOX_TRANSPORT=http`, `ASPNETCORE_URLS=http://0.0.0.0:8080`, `EXPOSE 8080`, `ENTRYPOINT ["dotnet", "ToolBox.Host.dll"]`.
- 4.2 `.dockerignore`: `bin/`, `obj/`, `.git/`, `Documentation/` — build context hygiene.
- 4.3 `docker-compose.yml` in Tool_Box for standalone dev: service `toolbox`, healthcheck `curl -f localhost:8080/health` (interval/retries/start_period budgeted like LLM_Monitor's), dev profile publishing `8081:8080`.
- 4.4 Verify: `docker compose up --build` → healthy; Inspector → `http://localhost:8081/mcp`.
- **Checkpoint:** container healthy; tools callable through the published dev port; image size noted in the log (baseline for future trimming).

### Step 5 — CI extension
- 5.1 New job `docker`: `docker build .` on every push (catches Dockerfile rot), then a smoke test: run the container, poll `/health` until 200 (bounded retries), assert, tear down.
- 5.2 No registry push yet (out of scope; that rung of the ladder comes with versioning/tagging decisions later).
- **Checkpoint:** CI green with the new job; smoke test demonstrably runs (its log shows the health poll).

### Step 6 — LLM_Monitor integration (walkthrough; executed under that repo's own process)
- 6.1 LLM_Monitor compose gains service `toolbox` (image built from the Tool_Box repo path or a local build), on the internal network, healthchecked; `langchain_service` gets `depends_on: toolbox: condition: service_healthy`.
- 6.2 `langchain_service` adds `langchain-mcp-adapters`; config maps `toolbox → {"transport": "streamable_http", "url": "http://toolbox:8080/mcp"}` via `MultiServerMCPClient`; `client.get_tools()` feeds the LangGraph tool node.
- 6.3 Available in mock mode too (per Q2 proposal): the tool loop is real even when the model is fake — a mock-mode pytest can assert the agent graph *can* call `ping` end-to-end.
- 6.4 Evidence for this plan's DoD: pytest output or an agent trace showing a ToolBox tool call crossing the compose network.
- **Checkpoint:** DoD item 5 satisfied; both repos' documentation cross-reference each other.

### Step 7 — Documentation
- 7.1 README: "Transports" section (when stdio, when HTTP; the selection precedence), container quickstart, LLM_Monitor pointer.
- 7.2 `docs/DECISIONS.md`: **ADR-007** (single binary, dual transport, selection precedence; rejected second-Host alternative) and **ADR-008** (v1 security: unauthenticated by design, network isolation as the control, port publication only behind dev profile, auth scheduled when first non-trusted network appears).
- 7.3 `docs/TOOL_CATALOG.md`: note that the catalog is transport-independent (nothing else changes — that's the point).
- 7.4 Update Learning lecture backlog: candidate topics for Lecture 002 — ASP.NET hosting model vs generic host, streamable HTTP/SSE mechanics, container networking, test fixtures with `IAsyncLifetime`.
- **Checkpoint / plan acceptance:** full Definition of Done walked through and evidenced in Stage 5.

## Risks & mitigations

| Risk | Mitigation |
|---|---|
| SDK's HTTP API differs from my sketch (`WithHttpTransport`/`MapMcp` names, session config) | Verify against the installed package's own docs/samples at Step 2, not memory or blogs; adjust plan in Stage 3 discussion if names moved |
| Streamable HTTP session semantics (stateful vs stateless) surprise the Python client | Integration test with the *official* client first (Step 3), `langchain-mcp-adapters` second (Step 6) — isolates "server wrong" from "client config wrong" |
| Container can't see what host-shaped future toolsets need | Already understood (Brainstorm 001): this image is for *service-shaped* toolsets; Basics is shape-neutral. No action, but ADR-007 restates it |
| `langchain-mcp-adapters` version drift vs LLM_Monitor's pinned LangChain | Pin in LLM_Monitor's requirements; its own CI gates the integration |
| Unauthenticated server accidentally exposed | ADR-008 + compose dev-profile pattern; port publication is opt-in and documented as dev-only |
| Two repos drift (image vs consumer) | Compose builds from local path during development; registry + version tags deliberately deferred until that hurts |

## Stage 3 Discussion Subsection

*(Chronological. Revisions to the plan above happen in-place; notable changes summarized here.)*

**[2026-07-16, AI]** Initial draft posted. Where I most want Timothy's judgment: the three Stage 2 open questions (dev port, mock-mode availability, base image), whether Step 6 should wait for an LLM_Monitor-side implementation plan to be written first, and whether integration tests belong in this plan or deserve splitting out if Step 3 balloons.

---

# Stage 4 (Implementation)

**[2026-07-16, Timothy — Stage 2 resolution]** All three open questions resolved per AI's proposals: Q1 dev port yes, behind a `dev` compose profile; Q2 ToolBox tools available in LLM_Monitor mock mode; Q3 `mcr.microsoft.com/dotnet/aspnet:10.0` base image. Permission granted for Step 1.

**[2026-07-16, AI — Step 1: Shared composition + transport selection]**

- 1.1 `ToolBoxServerComposition.cs` — `AddToolBoxServer(this IServiceCollection)`: Core + `AddMcpServer()` + toolsets, returning `IMcpServerBuilder` so each transport path attaches its own wire. The toolset list now exists in exactly one place; if it ever appears twice, the transports are drifting.
- 1.2 Bootstrap configuration (built *before* any host, because the transport decides which kind of host to construct): `appsettings.json` < `TOOLBOX_*` env vars < `--transport` flag — last-added-wins gives the agreed precedence. Two deliberate details: **`SetBasePath(AppContext.BaseDirectory)`**, because clients like Claude Desktop launch the DLL from an arbitrary working directory and a cwd-relative lookup would silently miss the file; and a **switch mapping** so `--transport http` binds to the `Transport` key.
- 1.3 `Program.cs` branches: `stdio` → exactly plan 001's path (stderr-first, then transport); `http` → clear stderr message + exit code 2 ("not implemented yet, Step 2"); unknown → fail fast with the valid values listed, exit code 2. Never guess a transport.
- csproj: `appsettings.json` copies next to the DLL.

No new packages (configuration providers arrive transitively via `Microsoft.Extensions.Hosting`). Zero diffs under `src/ToolSets/` and `src/ToolBox.Core/` — the invariant holds so far.

Verification (Timothy's machine):

1. `dotnet build` + `dotnet test` — green, 22 passed.
2. Purity test unchanged: `dotnet run --project src/ToolBox.Host 2>/dev/null` → prints nothing.
3. Inspector against the rebuilt DLL → still 3 tools (stdio is the default from appsettings).
4. `dotnet run --project src/ToolBox.Host -- --transport http` → stderr message about Step 2, exit code 2.
5. `dotnet run --project src/ToolBox.Host -- --transport banana` → "Unknown transport" on stderr, exit code 2.

State: Step 1 code complete, unverified. **Awaiting: verification results + permission for Step 2 (the HTTP path).**

**[2026-07-16, Timothy]** Permission granted for Step 2.

**[2026-07-16, AI — Step 2: The HTTP path]**

- 2.1 Host csproj: added `ModelContextProtocol.AspNetCore` 1.4.1 and a **`FrameworkReference` to `Microsoft.AspNetCore.App`** rather than switching to `Microsoft.NET.Sdk.Web`. Rationale: the Host is a console app that *opts into* the web stack for one of its modes; making it a web project would invert that identity and drag web conventions (launchSettings, wwwroot) into stdio mode.
- 2.2 `RunHttpAsync` in `Program.cs`: `WebApplication.CreateBuilder` → `UseStderrOnly()` (uniform rule, Stage 2 decision 3) → default URL `http://localhost:8080` unless `ASPNETCORE_URLS` is set (containers will set `0.0.0.0:8080`) → `AddToolBoxServer().WithHttpTransport()` → `app.MapMcp("/mcp")` → `app.MapGet("/health", …)` returning `{status, version, toolsets, uptime}` from `ServerInfoProvider` (curl-shaped on purpose — health must be checkable by tools that never heard of MCP).
- Note the composition payoff: the *entire* HTTP mode is one method. `AddToolBoxServer()` is identical in both paths; the diff between "local stdio server" and "network HTTP service" is the transport line, the URL, and a health endpoint.
- **API-name risk flag (per plan risk table):** `WithHttpTransport()` and `MapMcp("/mcp")` are written from the SDK's documented pattern; if 1.4.1's actual names/signatures differ (e.g. path parameter shape), expect a compile error at exactly those two lines — adjust to the package's XML docs and record the correction here.

Verification (Timothy's machine):

1. `dotnet build` + `dotnet test` — green (22 passed).
2. Regression: purity test still silent; Inspector over stdio still lists 3 tools.
3. `dotnet run --project src/ToolBox.Host -- --transport http` → starts, logs to stderr, listens on 8080.
4. `curl http://localhost:8080/health` → `{"status":"ok","version":…,"toolsets":["Basics"],…}`.
5. Inspector, transport **Streamable HTTP**, URL `http://localhost:8080/mcp` → Connect → 3 tools → call each.

State: Step 2 code complete, unverified. Zero diffs under `src/ToolSets/` and `src/ToolBox.Core/` — invariant still holds. **Awaiting: verification + permission for Step 3 (integration tests with the SDK client).**

**[2026-07-16, Timothy]** Permission granted for Step 3.

**[2026-07-16, AI — Step 3: Integration tests]**

A refactor first, then the tests:

- **Extracted `ToolBoxHttpApp.Build(args, overrideUrl?)`** out of `Program.cs`. Reason: the fixture must boot the *exact* app production runs — if tests assembled their own copy of the composition, they'd be testing the copy. `Program.cs`'s http case is now one line. `overrideUrl` lets tests pass `http://127.0.0.1:0` (port 0 = OS-assigned free port, so parallel CI runs can't collide).
- Host csproj: `InternalsVisibleTo → ToolBox.Host.Tests` (the app builder stays `internal`; tests are the one sanctioned outsider).

New project `tests/ToolBox.Host.Tests` (added to slnx):

- `HttpServerFixture` (`IAsyncLifetime` + `IClassFixture`): starts the real app on an ephemeral port once per test class, reads the *resolved* URL from `app.Urls` after `StartAsync`, tears down deterministically.
- `HttpTransportTests` — five tests over the real wire using the SDK's own client (`McpClientFactory` + streamable-HTTP client transport):
  1. `Handshake_Succeeds` — reaching the assertion means initialize/capabilities negotiated.
  2. `ToolsList_ExposesExactlyTheCatalog` — **set-equality** on tool names, not `Contains`: a tool disappearing and an unexpected tool appearing are both contract breaks.
  3. `Ping_RoundTripsThroughTheRealWire` — argument serialization → schema binding → execution → content blocks, end to end.
  4. `ServerInfo_ReportsTheBasicsToolset`.
  5. `Health_IsCurlShaped` — plain `HttpClient`, deliberately no MCP: probes must work without the protocol.

CI needs no change — the new project joins `dotnet test` automatically.

**API-name risk, called in advance:** the client-side transport types (`SseClientTransport` + `TransportMode = StreamableHttp`) follow the SDK's documented 1.4.x pattern; if the names moved, the compile error lands in `HttpTransportTests.ConnectAsync` and the fix is the package's XML docs. Record any correction here.

Verification (Timothy's machine): `dotnet test` — expect 22 unit + 5 integration = 27 passed (integration tests take a few seconds; Kestrel actually starts). Then the honesty spot-check from the plan: break one integration assertion, watch it fail, revert.

State: Step 3 code complete, unverified. Invariant holds (`ToolSets/`, `Core/` untouched). **Awaiting: test results + permission for Step 4 (containerization).**

**[2026-07-16, AI — Step 3 verification: restore failure, fixed]**

`dotnet test` failed at *restore* with `NU1510` (as error, via our warnings-as-errors rule): `Microsoft.Extensions.Hosting will not be pruned — automatically available, remove the PackageReference`.

Root cause: Step 2's `FrameworkReference Include="Microsoft.AspNetCore.App"` changed what the project already has. The ASP.NET Core shared framework *contains* `Microsoft.Extensions.Hosting`, so the explicit package reference (correct in plan 001, when the Host was a plain console app) became redundant. .NET 10's package-pruning analysis noticed; our rulebook escalated it from hint to blocker.

Fix: deleted the `Microsoft.Extensions.Hosting` PackageReference from the Host. Nothing else changes — the same types now arrive via the framework reference.

Worth keeping: (1) *a dependency change ripples — adding a framework reference redefines "already included", and yesterday's correct reference becomes today's redundancy;* (2) this is the warnings-as-errors policy doing exactly what we bought it for: forcing the codebase to state its dependencies honestly. A softer config would have carried the redundant reference forever.

**Awaiting: fresh `dotnet test` run (expect 27 passed).**

**[2026-07-16, AI — Step 3 verification round 2: the pre-declared API drift, resolved]**

Second run: restore clean, all 22 unit tests green, but `ToolBox.Host.Tests` failed to compile — `CS0246: IMcpClient not found`. This is exactly the risk pre-declared in the Step 3 log and the plan's risk table.

The diagnostic value of a *single* error: every other SDK type resolved (`SseClientTransport`, `HttpTransportMode`, `McpClientFactory`, `CallToolResult`, `TextContentBlock`). Only the interface was missing → the 1.x stable SDK exposes the concrete `McpClient` class where pre-1.0 docs showed an `IMcpClient` interface. Fix: three type references in `HttpTransportTests.cs`; nothing else touched.

Worth keeping: when one name out of six fails, that's a *rename*, not a wrong package or missing reference — read the error's shape before reaching for bigger hammers. Also: declaring a risk in advance is what made this a 2-minute fix instead of a surprise; we knew exactly where the error would land.

**Awaiting: fresh `dotnet test` run (expect 27 passed) + permission for Step 4 (containerization).**

**[2026-07-16, AI — Step 3 verification round 3: guessed twice, then read the docs]**

Round 2 exposed that the "single rename" diagnosis was wrong: with `McpClient` fixed, three MORE names failed (`SseClientTransport`, `SseClientTransportOptions`, `McpClientFactory`). The first run's single error had been misleading — the failing *return type* suppressed the method body's diagnostics, so the other broken names never got reported. Lesson amended: one visible error is not evidence of one actual error; a type error early in a member can mask everything downstream of it.

Correct response after two failed guesses: stop guessing, read the source of truth. Verified against the SDK's official v1.4 transport documentation:

- HTTP client transport is **`HttpClientTransport` + `HttpClientTransportOptions`** (`SseClientTransport` is the *legacy* SSE transport's lineage; streamable HTTP got its own type).
- Client creation is **`McpClient.CreateAsync(transport)`** — no factory class.
- `TransportMode = HttpTransportMode.StreamableHttp` was correct as written.

Bonus from actually reading the docs — two improvements adopted:

1. **`Stateless = true`** on `WithHttpTransport` (SDK-recommended for servers not needing sampling/elicitation): no session tracking, horizontal scaling without affinity, no `Mcp-Session-Id` for simpler clients to fumble — this directly retires the plan's "session semantics surprise the Python client" risk. Set explicitly for forward compatibility per the docs.
2. Noted for Step 4/7: the docs advise configuring **`AllowedHosts`** to loopback/known names (DNS-rebinding defense) rather than `*` — added to ADR-008's scope for Step 7.

Meta-lesson for the record: the plan's risk table said "verify against the installed package's docs, not memory" — and the implementation guessed from memory anyway, twice. The process knew better than the practitioner. Read the docs *first* next time; they also handed us two improvements guessing never would have.

**Awaiting: fresh `dotnet test` run (expect 27 passed) + permission for Step 4 (containerization).**

**[2026-07-16, Timothy]** All 27 tests pass. Permission granted for Step 4.

**[2026-07-16, AI — Step 4: Containerization]**

- 4.1 The placeholder `dockerfile` is now the real multi-stage build:
  - **Build stage** (`sdk:10.0`): csproj/props files copied *first*, `dotnet restore`, *then* source, then publish. This ordering is deliberate layer-caching choreography — dependency manifests change rarely, source constantly, so the slow network-bound restore replays from cache on most rebuilds.
  - **Runtime stage** (`aspnet:10.0`, per Q3): `curl` installed (the aspnet image ships without it — LLM_Monitor's slim-image healthcheck lesson, .NET edition), published output copied in, `TOOLBOX_TRANSPORT=http`, `ASPNETCORE_URLS=http://0.0.0.0:8080` (inside a container, "localhost" means only the container itself), **non-root via `USER $APP_UID`**, `EXPOSE 8080`.
- 4.2 `.dockerignore`: bin/obj everywhere, git, docs, tests — the build context stays lean.
- 4.3 `docker-compose.yml`: `toolbox` service with curl healthcheck (interval/retries/start_period budgeted), `AllowedHosts=localhost;127.0.0.1;toolbox` env (the SDK docs' DNS-rebinding guidance, applied), port `8081:8080` published.
- **Small deviation from the plan, with rationale:** the plan said the dev port hides behind a `dev` compose profile. In Tool_Box's *own* compose, the entire file is the dev environment — a profile inside it would be ceremony. The profile/no-ports posture applies where it matters: LLM_Monitor's compose (Step 6) gives the toolbox service **no ports section at all**. The compose file says this in a comment so the intent survives.

Verification (Timothy's machine, Docker running):

1. `docker compose up --build` — build completes, service starts, status reaches **healthy** (`docker compose ps` shows it, or watch the logs).
2. `curl http://localhost:8081/health` → `{"status":"ok",…}`.
3. Inspector → Streamable HTTP → `http://localhost:8081/mcp` → 3 tools, call `ping`.
4. Note the image size (`docker images`) in this log — the baseline for future trimming.
5. Regression: `dotnet test` still 27 green (nothing in this step touches code, but rituals are rituals).

State: Step 4 files complete, unverified. Invariant holds. **Awaiting: container verification + permission for Step 5 (CI docker job).**

**[2026-07-16, Timothy]** Permission granted for Step 5.

**[2026-07-16, AI — Step 5: CI docker job]**

- **Pre-step fix worth its own paragraph:** renamed `dockerfile` → `Dockerfile`. Timothy's macOS filesystem is case-insensitive, so the lowercase name worked locally — but GitHub's Linux runners are case-sensitive, and `docker build` looks for `Dockerfile`. This would have failed *only in CI*, the most confusing place to fail. Cross-platform rule: treat filename case as significant everywhere, because somewhere it is.
- New `docker` job in `ci.yml`, parallel to `build-and-test`:
  1. `docker build` on every push — a Dockerfile that isn't built in CI is untested deployment code that rots silently.
  2. **Smoke test:** run the container, poll `/health` up to 30s, pass on first 200. On failure: emit a CI error annotation, dump `docker logs` (the evidence future-you needs), exit 1.
  3. Teardown with `if: always()` — cleanup must run whether the test passed or not.
- No registry push (per plan scope): that rung needs versioning/tagging decisions we haven't earned yet.

Verification (Timothy): push, then check both CI jobs — `build-and-test` (27 tests) and `docker` (smoke log should show the health JSON and "healthy after ~Ns"). Paste the run link here.

State: workflow updated, unverified. **Awaiting: green CI (both jobs) + permission for Step 6 (LLM_Monitor integration walkthrough).**

---

# Stage 5 (Final Results, Testing, Verification)

*(Evidence against the Definition of Done, item by item.)*
