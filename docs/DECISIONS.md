# Architecture Decision Records

Append-only. Each record: context → decision → consequences. Reversing a decision gets a new ADR that supersedes the old one, never an edit.

---

## ADR-001 — C# with the official ModelContextProtocol SDK (2026-07-16)

**Context:** Primary future client is LLM_Monitor (Python), but MCP is a language-neutral protocol; server language is independent of client language. Career target is .NET backend roles.
**Decision:** C#/.NET, using the official `ModelContextProtocol` NuGet package. Never hand-roll the JSON-RPC layer.
**Consequences:** Attribute-based tool registration, hosting/DI integration, stdio + HTTP transports for free. Python agents consume the server via `langchain-mcp-adapters`.

## ADR-002 — stdio transport first, HTTP later (2026-07-16)

**Context:** Simplest viable product wins; HTTP adds networking, auth, and container concerns to plan 001.
**Decision:** MVP speaks stdio only. Streamable HTTP (for LLM_Monitor's compose network) is plan 002.
**Consequences:** Architecture must keep transports swappable — hence ADR-003's boundary: only the Host knows the transport.

## ADR-003 — Host / Core / Toolsets project layout (2026-07-16)

**Context:** The platform will grow many toolsets; domain logic and protocol plumbing must not tangle.
**Decision:** Thin Host (composition root only), Core (shared plumbing, no MCP hosting deps), one project per toolset. Separate early, abstract late: no dynamic plugin loader until two real toolsets exist (plan 003).
**Consequences:** A toolset costs the Host one composition line. Tools are testable as plain methods with no server running.

## ADR-004 — stderr-only logging (2026-07-16)

**Context:** In a stdio MCP server, stdout is the JSON-RPC wire. Anything else written there corrupts the protocol, and the failure surfaces as a baffling parse error on the *client*.
**Decision:** `UseStderrOnly()` clears providers and pins console logging to stderr; it is the first line of Host configuration so nothing can register ahead of it.
**Consequences:** Verified by the stdout purity test: `dotnet run --project src/ToolBox.Host 2>/dev/null` must print nothing.

## ADR-005 — Toolsets register via IMcpServerBuilder extensions (2026-07-16)

**Context:** Step 2.4 originally sketched `IServiceCollection` extensions, but tool registration (`WithTools<T>()`) lives on `IMcpServerBuilder`.
**Decision:** Each toolset exposes exactly one public doorway: `Add<Name>Toolset(this IMcpServerBuilder)`, which registers its `ToolsetDescriptor` and its tool types.
**Consequences:** Host composition reads as a fluent list of capabilities and contains zero tool-type names. Toolsets reference the MCP package for attributes, but the *transport* choice remains exclusively the Host's.

## ADR-006 — Target net10.0 (current LTS) (2026-07-16)

**Context:** Scaffolding defaulted to net11.0 because a .NET 11 *preview* SDK was installed. Lesson learned during Step 3: an SDK can compile a target it cannot run — executing net10.0 binaries requires the net10.0 runtime to be installed separately.
**Decision:** Target `net10.0` (LTS, supported to Nov 2028), pinned once in `Directory.Build.props`. Projects must not set their own TFM (csproj values silently override the props file).
**Consequences:** Stable base until 2028; CI pins `dotnet-version: 10.0.x` to match.

## ADR-007 — Single binary, dual transport (2026-07-16)

**Context:** Plan 002 adds streamable HTTP for containerized/remote consumers (LLM_Monitor) while stdio remains the local-client transport. A second Host project was considered.
**Decision:** One `ToolBox.Host` binary; transport selected at startup with precedence `--transport` flag > `TOOLBOX_TRANSPORT` env > `appsettings.json` (default stdio). Shared composition lives in `AddToolBoxServer()`; each path attaches only its wire. HTTP mode runs stateless (SDK recommendation: no session affinity, simpler clients).
**Consequences:** Toolsets/Core needed zero changes to gain a transport (measured: zero diffs in plan 002). The same binary can later run on a Raspberry Pi with a different toolset roster — deployment identity is configuration, not compilation. Cost: the Host carries ASP.NET Core via FrameworkReference even in stdio mode (accepted; shared framework, not published weight per-mode).

## ADR-008 — HTTP security posture v1: unauthenticated, network-isolated (2026-07-16)

**Context:** The HTTP endpoint executes tools on behalf of anyone who can reach it. v1 has no authentication.
**Decision:** Isolation is the control, and it's layered: (1) inside consuming composes (LLM_Monitor) the service publishes **no ports** — reachable only on the internal network, and exposure is a conscious config change (the lockdown-is-a-config-change pattern); (2) Tool_Box's own dev compose publishes `8081:8080` because that file *is* the dev environment; (3) `AllowedHosts` is pinned to loopback + the compose service name per SDK guidance (DNS-rebinding defense — Kestrel doesn't validate Host headers by default); (4) all current tools are read-only.
**Consequences:** Never deploy this endpoint beyond a trusted network segment. Authentication (HTTP auth on the transport) is scheduled for whenever the first non-trusted network appears — and must land *before* any write-classified toolset ships over HTTP. **Superseded in part by ADR-011** (2026-07-21): item (4) turned out to be a promise this project didn't keep literally — read below.

## ADR-009 — Voxel world state: a single global singleton (2026-07-21)

**Context:** Plan 003 is the platform's first stateful toolset. A `VoxelWorld` must persist across tool calls within a session. The "correct" multi-tenant answer would be state scoped per MCP session (the SDK supports per-connection server instances over streamable HTTP), but no real multi-client scenario has been observed yet.
**Decision:** One process-wide singleton `VoxelWorld`, registered the same way `ServerInfoProvider` is — plain `Dictionary`-backed, no locking, no persistence. A reference implementation this toolset is modeled on (a working voxel-builder MCP server) uses the identical pattern for the identical reason: one client, one process, nothing to protect against. Session-scoped state is explicitly deferred until a real multi-client demo needs it — "abstract from evidence, not imagination," the same principle ADR-003 applied to the plugin loader.
**Consequences:** Two simultaneous HTTP-connected agents would edit the same world — a known, accepted v1 limitation, not an oversight. `world.Clear()` affects every connected viewer/client at once. Revisit if/when a genuine multi-client use case appears.

## ADR-010 — Toolsets may register companion `IHostedService`s (2026-07-21)

**Context:** The Voxel toolset needs a live browser viewer, which needs a WebSocket broadcast running continuously — but that has nothing to do with the MCP transport itself, and must work identically whether the Host is running stdio or HTTP (ADR-007's transport independence shouldn't have exceptions for one toolset's demo feature).
**Decision:** Generalizes ADR-005 one step further: a toolset's `Add<Name>Toolset()` extension may register not just tools, but its own `BackgroundService` (`VoxelViewerBroadcastService`), via the ordinary `services.AddHostedService<T>()`. This works unmodified under both Host composition paths (`Host.CreateApplicationBuilder` for stdio, `WebApplication.CreateBuilder` for HTTP) because both ultimately build on the same `Microsoft.Extensions.Hosting` `IHost` — proven directly, not assumed, by booting both shapes with a real WebSocket client attached during plan 003 Step 4.
**Consequences:** A new pattern precedent for future visual/interactive toolsets (a future scene-composer toolset, for example, could reuse this rather than re-deriving it). The companion service binds its own port range (8090-8093 for Voxel), independent of and never colliding with whatever port the MCP HTTP transport itself uses (8080) — verified directly, both running in the same process simultaneously.

## ADR-011 — Supersedes ADR-008 item (4): isolation, not tool classification, was always the real control (2026-07-21)

**Context:** ADR-008 listed "all current tools are read-only" as one of four mitigating factors for the unauthenticated HTTP posture, and its consequences line said authentication "must land before any write-classified toolset ships over HTTP." Plan 003's Voxel toolset is the first write-classified toolset (`place_*`, `remove_box`, `mirror`, `clear` all mutate), and plan 003 explicitly deferred HTTP authentication to a future plan — so shipping Voxel over HTTP now means literally doing the thing ADR-008 said shouldn't happen yet. This was surfaced explicitly to Timothy (plan 003, Stage 3 Step 7.3) rather than silently decided.
**Decision:** Ship Voxel over both transports, preserving ADR-007's "identical toolset roster regardless of transport" invariant. Re-examining ADR-008's own four items: (1)-(3) — no published ports outside a trusted network, dev-only port mapping, `AllowedHosts` pinned against DNS rebinding — are deployment-topology controls that apply equally to read and write tools; they were always the *actual* protection. Item (4) ("all current tools are read-only") was true by coincidence of what had been built so far, not a designed-in gate, and treating it as a hard precondition for every future toolset would mean no write toolset could *ever* ship over HTTP without a full auth implementation first — a much larger bar than this project's staged, evidence-driven process has applied anywhere else.
**Consequences:** Authentication is still real future work, scheduled for "whenever the first non-trusted network appears" exactly as ADR-008 said — that part stands. What's retired is treating tool read/write classification as itself a gating condition. The operative rule going forward: **never publish the HTTP transport's port beyond a trusted network** (ADR-008 items 1-3), regardless of what the toolset roster can do. This was a conscious, reviewed decision (not a silent policy drift) — recorded here specifically so a future reader doesn't find write tools shipping over HTTP and wonder whether ADR-008 was simply forgotten.
