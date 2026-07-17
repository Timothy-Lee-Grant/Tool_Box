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
**Consequences:** Never deploy this endpoint beyond a trusted network segment. Authentication (HTTP auth on the transport) is scheduled for whenever the first non-trusted network appears — and must land *before* any write-classified toolset ships over HTTP.
