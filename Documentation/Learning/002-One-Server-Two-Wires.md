2026_07_16_20_22-(One-Server-Two-Wires)

# Lecture 002 — One Server, Two Wires: Everything Plan 002 Taught

Plan 001 built a correct little machine. Plan 002 did something harder: it changed the machine's *relationship to the world* — from a child process whispering through pipes to a network service other systems depend on — without changing the machine itself. Every concept below is illustrated by a file in this repo or a failure we personally hit today.

Reading order follows your preference: architecture → components → interactions → control flow → details → edge cases.

---

## 1. What problem was being solved?

Plan 001's server had one way to exist: launched by a chat client on your desk. But your real consumer, LLM_Monitor's agent, lives in a Docker network where nobody can spawn child processes on your Mac. The gap wasn't capability — the tools were fine — it was *reachability*. Plan 002's thesis: reachability is a deployment property, and deployment properties must never leak into capability code. The proof it demanded: add a whole transport, container, and consumer story with **zero diffs** under `ToolSets/` and `Core/`. That invariant held, and it's now measured history, not aspiration.

---

## 2. The updated cast

| Character | New role in plan 002 |
|---|---|
| **The Receptionist** (`Program.cs`) | Now works a switchboard: reads one word of bootstrap config and decides which office opens today — the pipe desk or the web desk. Still knows no domains. |
| **The Web Desk** (`ToolBoxHttpApp`) | A whole second lobby: Kestrel, `/mcp`, `/health`. Extracted into its own class for one reason — so the integration tests can walk into *the real lobby*, not a cardboard replica. |
| **The Union Contract** (MCP) | Unchanged — which is the entire point. Same JSON-RPC messages now ride HTTP POSTs instead of stdin. |
| **The Health Inspector** (`/health` + compose healthcheck) | Speaks plain HTTP, *deliberately* not MCP: an inspector shouldn't need to speak your trade language to see the lights are on. |
| **The Customs Officer** (`AllowedHosts`) | Checks every visitor's claimed destination (the Host header) against a short list. Turns away DNS-rebinding con artists. |
| **The Shipping Container** (Dockerfile) | The server's traveling form: built in a workshop it doesn't carry with it. |

---

## 3. Transports, conceptually

The transport question is really a **process-relationship** question:

```
stdio:                          streamable HTTP:
  client                          client            server
    └── spawns ──► server           └── HTTP POST ──► :8080/mcp
    (parent/child, one client,      (peers on a network, many clients,
     lifetime = client's whim)       lifetime = its own)
```

Everything else follows from that relationship:

| Property | stdio | streamable HTTP |
|---|---|---|
| Who starts the server | the client | an operator / compose |
| How many clients | exactly one | any number |
| Identity of a "connection" | the process itself | per-request (stateless) or `Mcp-Session-Id` (stateful) |
| Security boundary | process permissions | the network — hence ADR-008 |
| Backpressure | pipe flow control | POST held open until the handler finishes |

**Streamable HTTP in one paragraph:** each client request is an HTTP POST; the server holds that POST's response open as an SSE stream and writes the JSON-RPC reply (plus any progress notifications) through it. Holding the response open is what gives natural backpressure — a client can't flood you faster than you answer. Its legacy predecessor ("SSE transport") split send and receive across two endpoints and returned `202 Accepted` immediately, which is exactly why it's deprecated: no backpressure, floodable.

**Stateless vs stateful:** we chose `Stateless = true`. No session table in memory, horizontal scaling without affinity, and simpler clients can't fumble a session header they never receive. The price: the server can't initiate requests *to* the client (sampling/elicitation). Our server only ever answers; we paid nothing.

---

## 4. Control flow: startup, both wires

```
process start
   │
   ├─ bootstrap ConfigurationBuilder  ← BEFORE any host exists, because the
   │    appsettings.json               transport determines which KIND of host
   │    < TOOLBOX_* env                to construct. Chicken-and-egg resolved by
   │    < --transport flag             a tiny config just for this decision.
   │
   ├─ "stdio" ──► Host.CreateApplicationBuilder     (generic host: services + lifetime, no web)
   │                └─ stderr rule → AddToolBoxServer() → WithStdioServerTransport()
   │
   └─ "http"  ──► WebApplication.CreateBuilder      (generic host + Kestrel + routing)
                    └─ stderr rule → AddToolBoxServer() → WithHttpTransport(Stateless)
                        └─ MapMcp("/mcp"), MapGet("/health")
```

Concepts embedded here:

- **Generic host vs web host.** `WebApplication` *is* the generic host plus an HTTP server (Kestrel) and endpoint routing. That's why both paths could share `UseStderrOnly()` and `AddToolBoxServer()` — the DI/logging substrate is the same machine underneath.
- **Configuration layering** is .NET's standard trick: later sources override earlier ones. Our precedence (file < env < flag) matches the operational reality of *who* sets each — a file is written once by a developer, an env var by a deployment, a flag by a human at a terminal. The more specific the actor, the higher the precedence.
- **`AppContext.BaseDirectory` vs cwd:** Claude Desktop launches the DLL from `/`. A relative `appsettings.json` lookup would silently find nothing. Config that "works on my machine" but vanishes in production is almost always a cwd assumption.
- **The FrameworkReference decision:** the Host stays `Microsoft.NET.Sdk` (console identity) and *opts into* ASP.NET Core via `<FrameworkReference Include="Microsoft.AspNetCore.App"/>`, rather than becoming a web project that happens to do stdio. Identity follows the primary mode; capabilities are additive.

---

## 5. The day's debugging saga, and what each round taught

This deserves its own section because it was the richest learning of the phase.

**Round 0 — NU1510 at restore.** Adding the FrameworkReference made `Microsoft.Extensions.Hosting` redundant (the shared framework ships it), and .NET 10's pruning analysis said so; warnings-as-errors made it a blocker. *Concept: a dependency change redefines "already included" — yesterday's correct reference becomes today's redundancy. Strict build policies surface this immediately instead of never.*

**Round 1 — one missing type (`IMcpClient`).** Diagnosis: "single rename." Fixed it. **Wrong.**

**Round 2 — three MORE missing types.** The first error had been hiding the others: a broken *return type* suppresses diagnostics for the whole method body. *Concept: one visible compiler error is not evidence of one actual error. Errors have shadows.*

**Round 3 — stopped guessing, read the SDK's v1.4 docs.** Real API: `HttpClientTransport` + `McpClient.CreateAsync`. And the docs handed over two things guessing never would: `Stateless = true` (recommended, retired a planned risk outright) and the `AllowedHosts` DNS-rebinding guidance (became part of ADR-008). *Concept — the meta-lesson of the day: our own plan's risk table said "verify against the package docs, not memory," and the implementation guessed from memory anyway. Twice. The process knew better than the practitioner. Primary sources aren't just for correctness; they carry recommendations you don't know to look for.*

**The bonus trap that never sprang — `dockerfile` vs `Dockerfile`.** macOS filesystems are case-insensitive; GitHub's Linux runners are not. The lowercase name worked locally and would have failed only in CI. *Concept: treat filename case as significant everywhere, because somewhere it is. The general form: "works locally" only certifies your platform's forgivenesses.*

---

## 6. Integration testing: the middle of the pyramid

```
        ▲  manual: Inspector, Claude Desktop        (few, human)
       ▲▲  integration: ToolBox.Host.Tests           (5 — the NEW layer)
      ▲▲▲  unit: Core.Tests, Basics.Tests            (22, fast, no I/O)
```

What plan 002's five tests actually pin down, and the ideas inside them:

- **Test the production composition, not a replica.** `ToolBoxHttpApp.Build()` was extracted *so that* the fixture boots the exact app users get. A test that assembles its own copy of the wiring tests the copy. (`InternalsVisibleTo` is the narrow door that lets tests in without making the builder public.)
- **Ephemeral ports** (`127.0.0.1:0`): ask the OS for any free port. Fixed ports in tests are a race condition against parallel CI and your own second terminal.
- **Fixtures own lifetime** (`IAsyncLifetime` + `IClassFixture`): server starts once per class, torn down deterministically — not per-test (slow) and not never (leaks).
- **Set-equality on the tool list**, not `Contains`: a tool *disappearing* and a stranger *appearing* are both contract breaks. Assertions should encode the full contract, not the half you were thinking about.
- **The client is the SDK's own.** We test with the same client library real consumers embed, so schema, serialization, and endpoint quirks surface here — not in someone's Claude session.

---

## 7. Containers, conceptually (with your Dockerfile as the worked example)

**Multi-stage builds** separate the workshop from the product: `sdk:10.0` (~800MB of compilers) builds; `aspnet:10.0` (~110MB) runs; only published output crosses between them. Shipping the workshop is how images bloat to gigabytes.

**Layer caching is a dependency graph you author.** Docker replays cached layers until the first changed input. Our copy order — props/csproj files → `restore` → source → `publish` — means the slow, network-bound restore only reruns when *dependencies* change, not when code does. This is the same reasoning as CI caching, incremental builds, and memoization: order work by how often its inputs change.

**"localhost" inside a container means the container.** Hence `ASPNETCORE_URLS=http://0.0.0.0:8080` in the image (bind all interfaces so the compose network can reach in) while bare-metal dev defaults to `localhost:8080` (bind loopback only, reachable by nobody else). Same word, two networks.

**Healthchecks are for strangers.** `/health` speaks plain HTTP because compose, Kubernetes, and load balancers are curl-shaped. Two lessons embedded: the aspnet image ships no curl (installed explicitly — LLM_Monitor met the same gap in Python slim and went stdlib), and `depends_on: condition: service_healthy` turns "wait and hope" startup ordering into a contract.

**Non-root (`USER $APP_UID`)** is container hygiene: a compromised process shouldn't hold root, even inside a namespace. The .NET images pre-create the user; using it costs one line.

**The dev/prod port split (ADR-008).** Tool_Box's own compose publishes `8081:8080` — that file *is* the dev environment. LLM_Monitor's compose gives the service **no ports at all**: reachable only on the internal network, and *exposure is a conscious config change*. Notice this is your own LLM_Monitor lockdown pattern, generalized: security posture as configuration diff, greppable and reviewable.

---

## 8. Security concepts this phase introduced

| Threat | Control | Where |
|---|---|---|
| Anyone on the network can execute tools | Don't be on their network: no published ports in consuming composes | ADR-008, LLM_MONITOR_INTEGRATION.md |
| DNS rebinding (browser tricked into calling localhost with an attacker's Host header) | `AllowedHosts` pinned to loopback + service name — Kestrel doesn't validate Host by default | compose env, ADR-008 |
| Future write-tools over HTTP | Rule recorded now: auth must land *before* any write-classified toolset ships over HTTP | ADR-008 |
| Model-supplied args (path traversal etc.) | Still ahead of us — becomes real with the first filesystem/git toolset | plan 003+ |

The posture worth internalizing: v1 doesn't *have* security, it has **isolation, written down**. An unauthenticated endpoint that's documented, network-fenced, and read-only is a defensible engineering decision; the same endpoint undocumented is negligence. The difference is one ADR.

---

## 9. Cross-repo integration as a discipline

Step 6 produced a *walkthrough*, not commits in LLM_Monitor. Concepts at work:

- **A dependency edge crosses a process boundary, so it should cross a process-of-work boundary too.** Tool_Box documents how to be consumed; LLM_Monitor decides how to consume, through its own staged plan. Two repos, two audit trails — this is how platform teams and consumer teams actually interact.
- **Additive integration:** the walkthrough recommends a *new* registry entry (`graph-tools`) over editing existing pipelines — the same additive-contract discipline LLM_Monitor already applies to its API.
- **The zero-code-growth payoff:** once the adapter wiring exists, every future ToolBox toolset becomes an agent capability with no LLM_Monitor changes. That's the definition of platform leverage, and you can now state it with a concrete example.

---

## 10. State of the system (end of plan 002)

- One binary, two verified transports; `Stateless` HTTP at `/mcp`, health at `/health`.
- 27 tests (22 unit + 5 integration), all green; CI = build-and-test job + docker job that *boots* the image and polls health.
- Image: multi-stage, non-root, curl-equipped, `aspnet:10.0` base. Compose: standalone dev file with healthcheck and published dev port.
- Docs: 8 ADRs, transport-independent tool catalog, LLM_Monitor integration walkthrough.
- Invariant achieved: `ToolSets/` and `Core/` untouched across the entire plan.
- Outstanding: Stage 5 evidence (container verification, CI links, and eventually LLM_Monitor's cross-network tool call).

## 11. Common mistakes catalogued this phase

1. Guessing library APIs from memory when the docs are one fetch away — twice.
2. Reading one compiler error as one defect (errors cast shadows).
3. Trusting "works locally" across OS forgivenesses (filename case).
4. Duplicating composition between production and tests (test the real lobby).
5. Fixed ports in tests.
6. Assuming base images contain diagnostic tools (curl).
7. Publishing container ports out of habit rather than decision.
8. Treating an append-only decision log as reorderable (the AI misfiled ADR-006 below 008 today; append-only means append-only).

## 12. Interview relevance

New material this phase hands you: transport abstraction with a measured zero-diff proof; streamable HTTP vs SSE and why backpressure killed the old design; stateless-vs-stateful tradeoffs you actually chose between; integration-test design (production composition, ephemeral ports, SDK-client-as-consumer); Docker layer-cache choreography and multi-stage rationale; healthcheck-driven startup ordering; a security ADR that says "unauthenticated" out loud and fences it; and a debugging story with a self-critical arc — "my process document predicted my mistake, I made it anyway, and now I read primary sources first." Interviewers remember the candidate who tells that last one.

## 13. What to study next

- **Auth on the HTTP transport** (the ADR-008 debt): HTTP auth headers via the SDK, then OAuth's ID-JAG flow the docs describe — enterprise SSO for MCP.
- **Sessions doc of the SDK** — you chose stateless; know precisely what you gave up (server-initiated sampling/elicitation) before a toolset wants it back.
- **Compose → Kubernetes translation:** healthcheck→probes, depends_on→initContainers/readiness, internal networks→Services+NetworkPolicies. Your compose file is a Rosetta stone for the k8s conversation.
- **First real toolset (plan 003):** config-driven toolset selection — where the "same binary, different roster" promise gets cashed.
