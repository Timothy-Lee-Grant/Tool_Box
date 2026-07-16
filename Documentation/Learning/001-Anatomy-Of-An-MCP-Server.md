2026_07_16_15_28-(Anatomy-Of-An-MCP-Server)

# Lecture 001 — Anatomy of an MCP Server: Everything We Built and Why

This lecture teaches the system you now have running: what every piece is, how the pieces talk to each other, what happens during a single tool call, how to verify and operate it, and where each design decision came from. It follows your preferred order: architecture first, implementation details later.

---

## 1. What problem is being solved?

An LLM is a brain in a jar. It can reason brilliantly about your machine, your build, your logs — but it cannot *see* any of them. Until now, **you** were its sensory system: run the command, copy the error, paste it back. Slow, attention-hungry, and it requires you to already know which evidence matters.

MCP (Model Context Protocol) is the standard plug that fixes this. Your Tool_Box is an MCP **server**: a process that advertises capabilities ("I can ping, I can report server info, I can tell time") and executes them on request. Any MCP **client** — Claude Desktop, Claude Code, the Inspector, eventually LLM_Monitor's agent loop — can plug in and use those hands.

The three tools we shipped are deliberately trivial. Plan 001's deliverable was never the tools; it was the *chassis* the next hundred tools will bolt onto.

---

## 2. The cast of characters

You like personified components, so meet the company:

| Character | Project/Class | Personality & job |
|---|---|---|
| **The Receptionist** | `ToolBox.Host` | Knows nobody's specialty and proudly so. Reads the roster, opens the phone line (transport), routes calls. Twenty lines of code. If the receptionist starts doing domain work, the architecture has failed. |
| **The Office Manager** | `ToolBox.Core` | Runs shared services every specialist relies on: the output-size policy (`OutputLimiter`), the company directory (`ToolsetDescriptor`s), the status report (`ServerInfoProvider`), and the house rule about which door to shout through (`ToolBoxLogging`). |
| **The Specialists** | `ToolBox.Basics` (later: Git, Docker, …) | Each knows one domain and nothing else. The Basics specialist is a trainee hired to test the onboarding process. Specialists don't know each other exist. |
| **The Wire** | stdio (stdin/stdout) | A pneumatic tube between client and server. Sacred rule: only protocol messages travel through it. |
| **The Caller** | The LLM (via its client app) | Never executes anything itself. Reads the specialists' business cards (`[Description]` strings) and decides who to call with what. |
| **The Union Contract** | MCP / JSON-RPC 2.0 | The agreed message format everyone speaks, which is why a C# server can serve a Python agent or Claude Desktop identically. |

---

## 3. The system, top to bottom

```
┌─────────────────────────────────────────────────────────────┐
│  MCP CLIENT (Claude Desktop / Claude Code / Inspector)      │
│  owns the LLM conversation; decides when a tool is needed   │
└───────────────┬─────────────────────────────────────────────┘
                │ launches your process; speaks JSON-RPC over
                │ its stdin/stdout   ← THE WIRE
┌───────────────▼─────────────────────────────────────────────┐
│  ToolBox.Host (Program.cs — composition root)               │
│    1. UseStderrOnly()      ← protect the wire, FIRST        │
│    2. AddToolBoxCore()     ← hire the office manager        │
│    3. AddMcpServer()                                        │
│         .WithStdioServerTransport()   ← choose the wire     │
│         .AddBasicsToolset()           ← one line per        │
│                                          specialist         │
├─────────────────────────────────────────────────────────────┤
│  ToolBox.Basics                     [McpServerToolType]     │
│    ping / server_info / current_time                        │
│    knows: its domain + Core services                        │
│    does NOT know: transport, other toolsets, the Host       │
├─────────────────────────────────────────────────────────────┤
│  ToolBox.Core                                               │
│    OutputLimiter · ServerInfo(Provider) · ToolsetDescriptor │
│    ServiceCollectionExtensions · ToolBoxLogging             │
│    knows: nothing about MCP hosting, nothing about domains  │
└─────────────────────────────────────────────────────────────┘
```

The dependency arrows only point downward: Host → (Basics, Core), Basics → Core, Core → nothing of ours. This is why you can add a Git toolset tomorrow without touching a single existing file except one composition line in `Program.cs`.

---

## 4. Control flow: the complete life of one tool call

This is the part most people never look at, and it's where the magic stops being magic. You ask Claude Desktop: *"ping my toolbox with the message hello."*

**Phase 0 — Launch (before any of your code runs).**
Claude Desktop reads its config, finds `command: dotnet, args: [...ToolBox.Host.dll]`, and *spawns your process as a child*, holding pipes to its stdin and stdout. This is why registration is a JSON file, not a network address: for stdio servers, the client is your process's parent.

**Phase 1 — Handshake.**
Client writes one line of JSON to your stdin: `initialize` (protocol version, client identity). The SDK — code you didn't write, from `AddMcpServer()` — answers on stdout with the server's identity and capabilities. Then `tools/list` arrives; the SDK reflects over every type registered via `WithTools<T>()`, reads your `[McpServerTool]` and `[Description]` attributes, converts each method signature into a JSON Schema, and replies with the catalog. **This is the moment your C# attributes become the model's knowledge.** The model will never see your code — only this JSON.

**Phase 2 — The model decides.**
The LLM sees the tool catalog injected into its context. Your description — "Connectivity check. Returns 'pong'…" — is *prompt text*. Based on it, the model emits a structured tool-use request instead of prose: `{"name": "ping", "arguments": {"message": "hello"}}`.

**Phase 3 — Execution (finally, your code).**
The SDK receives `tools/call` on stdin, finds `ping`, and:

1. Resolves `BasicsTools` from the DI container → constructor injection hands it the `ServerInfoProvider` and `TimeProvider` singletons.
2. Binds the JSON arguments to your method parameters (deserialization + validation against the schema).
3. Invokes `Ping("hello")` → your method builds `"pong: hello"`, routes it through `OutputLimiter.Limit()`, returns.
4. Serializes the return value into a `tools/call` result and writes it to stdout.

**Phase 4 — The model narrates.**
The client feeds the result back into the LLM's context; the model turns `pong: hello` into a sentence for you.

Total round trip: two processes, one pipe, four JSON messages. No HTTP, no ports, no cloud.

---

## 5. The .NET mechanics you asked about (projects, solutions, DLLs)

The build-system hierarchy, now with everything you saw in practice:

```
Tool_Box/                      ← git repository
├── ToolBox.slnx               ← solution: developer/IDE grouping (you chose the
│                                 modern XML format — good instinct)
├── Directory.Build.props      ← THE RULEBOOK: applies to every csproj below it
├── src/…/*.csproj             ← projects: the actual build units
└── (each project) →  bin/…/ToolBox.*.dll or ToolBox.Host (executable)
```

Four mechanics worth engraving:

1. **`Directory.Build.props` is imported *before* each csproj**, so a property set inside a csproj **silently wins**. That's why we stripped `TargetFramework` etc. out of every csproj — duplicated properties turn the central rulebook into decoration. (You watched this fixed in Step 1.)
2. **`ProjectReference` vs `PackageReference`.** Inside the repo, projects reference each other by path. External code (the MCP SDK) arrives as NuGet packages. The day another repo needs `ToolBox.Core`, you publish it and the reference flips from path to package — that's all "packaging a library" means.
3. **Transitivity.** `Basics.Tests` never declared the `ModelContextProtocol` package, yet its reflection test uses `McpServerToolAttribute`. It flows through: Tests → Basics (project) → ModelContextProtocol (package). References are a graph, not a list.
4. **SDK ≠ runtime — your Step 3 war story.** A .NET 11 *preview SDK* happily *compiles* a `net10.0` target, but *running* the output needs the .NET 10 *runtime* installed. Compilation is a promise about shape; execution needs the actual library implementations present. Remember the symptom: builds green, process dies at startup.

---

## 6. Dependency injection: what the composition root actually composes

`Program.cs` is called a **composition root** because it is the *only* place that knows the full object graph. Everything else just declares what it needs.

```
AddToolBoxCore()               AddBasicsToolset()
   │                               │
   ▼                               ▼
TimeProvider.System (singleton)  ToolsetDescriptor("Basics", …) (singleton)
ServerInfoProvider  (singleton)  WithTools<BasicsTools>()
```

When a tool call needs `BasicsTools`, the container builds it: sees the constructor wants `(ServerInfoProvider, TimeProvider)`, hands over the singletons. Three idioms we used deliberately:

- **`TryAddSingleton`** — "register unless someone already did." This is what lets a test pre-register a fake `TimeProvider` that wins over the real one. Idempotent registration is a courtesy to your future self.
- **Multiple registrations of the same type** — every toolset adds its own `ToolsetDescriptor`; anyone injecting `IEnumerable<ToolsetDescriptor>` receives *all of them*. The DI container **is** the toolset registry — we never needed to build one.
- **`TimeProvider` over `DateTimeOffset.UtcNow`** — the static clock is untestable ambient state. The injected clock made `Uptime_MeasuresTimeSinceConstruction` possible: set time forward five minutes, assert exactly five minutes. No `Thread.Sleep`, no flakes.

The general principle: **code that reaches out to ambient statics (clock, filesystem, environment) is code you can't test without the world's cooperation.** Inject the world.

---

## 7. The stderr rule (the most transferable lesson in this project)

In a stdio server, stdout is not "the place text goes." **Stdout is the network cable.** One stray `Console.WriteLine("starting…")` interleaves with JSON-RPC frames, and the *client* — a different process, possibly written by a different company — reports a parse error pointing nowhere near your bug.

Defense in depth as built:

| Layer | Mechanism |
|---|---|
| Code | `UseStderrOnly()`: clears all providers, pins console logging to stderr (`LogToStandardErrorThreshold = Trace`) |
| Ordering | It is the **first** line of Host configuration, so nothing can register a stdout logger ahead of it |
| Launch path | Clients run the built DLL, never `dotnet run`, so MSBuild's chatter can't reach the wire either |
| Verification | The purity test: `dotnet run --project src/ToolBox.Host 2>/dev/null` → must print *nothing* |

The transferable version: **know which of your process's channels are protocol and which are diagnostics, and never let them share a pipe.** The same discipline shows up in CGI, git hooks, language servers (LSP), and Unix filters.

---

## 8. Tool design: the disciplines that were the real deliverable

**Bounded output (`OutputLimiter`).** The consumer of a tool result is a context window. Dumping 50 MB of logs doesn't inform the model — it *evicts everything else the model knew*, including your question. So: 20k-char default budget, and a truncation marker that tells the truth ("…41,203 more characters. Narrow the request.") so the model knows to ask a narrower question. Note the subtlety the tests pin down: we refuse to cut a surrogate pair in half — .NET strings are UTF-16, an emoji is two `char`s, and half an emoji is corrupt text.

**Descriptions are prompts.** The `[Description]` attribute is the *only* thing the model knows about your tool. We wrote them like documentation for a smart intern ("Use this instead of guessing the date") and then — the professional move — encoded the rule as a **reflection test** (`DescriptionConventionTests`): any tool or parameter missing a description fails the build. A convention you don't enforce is a suggestion.

**Errors are data (coming in later toolsets).** When `run_build` fails, that's a *successful tool call* whose payload says the build failed. Protocol errors are reserved for "the tool machinery itself broke." The model can reason about a failed build; it can't reason about an exception that killed the pipe.

**Read/write classification.** Every catalog entry is tagged read or write (all three current tools: read). This tag is dormant now but becomes the enforcement point for read-only deployments — you'll want `git_diff` allowed and `git_commit` refused when an agent runs unattended.

---

## 9. Testing: what we test and, more importantly, what we don't need to

```
tests/
├── ToolBox.Core.Tests
│   ├── OutputLimiterTests        7 tests: under/at/over budget, honest marker,
│   │                             surrogate safety, null/range guards
│   └── ServerInfoProviderTests   3 tests: clock-controlled uptime, toolset
│                                 names, version never empty
└── ToolBox.Basics.Tests
    ├── BasicsToolsTests          7 tests: ping variants, truncation wired,
    │                             server_info, deterministic current_time
    └── DescriptionConventionTests  3 tests: tool count, every tool described,
                                  every parameter described
```

Notice the profound absence: **no MCP server, no transport, no process spawning anywhere in the suite.** Tools are plain methods on a plain class; tests call them directly. That's not luck — it's the direct payoff of keeping toolsets transport-ignorant. When someone tells you "good architecture makes testing easy," this is the concrete meaning.

**Honest CI.** The workflow does real restore → Release build with `-warnaserror` → real tests, and you performed the ritual: deliberately broke a test, watched GitHub go red, reverted, watched it go green. That red run is a *credential*. Your LLM_Monitor CI taught you why: a green badge that can't go red is a lie with a checkmark. (Interview line: "I test my CI the way I test my code — by making it fail on purpose.")

---

## 10. Operating manual: verify, use, deploy

**Full verification sequence (fresh machine):**

```bash
git clone <repo> && cd Tool_Box
dotnet build                                   # zero warnings expected
dotnet test                                    # 17 passed expected
dotnet run --project src/ToolBox.Host 2>/dev/null   # purity: prints NOTHING (Ctrl+C)
dotnet build -c Release
npx @modelcontextprotocol/inspector dotnet src/ToolBox.Host/bin/Release/net10.0/ToolBox.Host.dll
#   → Connect → Tools tab: 3 tools → call each
```

**Client registration** — same server, three doors:

| Client | How | Notes |
|---|---|---|
| Claude Desktop | `claude_desktop_config.json` → `mcpServers.toolbox` → `dotnet <path-to-Release-DLL>` | Restart the app; tools appear in the chat's tool picker |
| Claude Code | `claude mcp add toolbox -- dotnet <path-to-Release-DLL>` | Scoped per-project or `--scope user` for everywhere |
| Inspector | `npx @modelcontextprotocol/inspector dotnet <dll>` | Your interactive debugger; always test here first |

**Deployment shapes — today and planned:**

| Shape | Transport | Status | Right for |
|---|---|---|---|
| Host-native process, client-launched | stdio | ✅ **now** | Claude Desktop/Code on your machine; anything needing host visibility |
| Long-running service in Docker | streamable HTTP | plan 002 | LLM_Monitor's compose network; service-shaped toolsets |
| `dotnet tool install -g` | stdio | plan 003+ | Distribution to other developers via NuGet |
| Self-contained binary (GitHub Releases) | stdio | later | Machines without .NET installed |
| Same binary on a Raspberry Pi, `--toolsets raspberrypi` | stdio | the dream | The payoff of config-driven toolset selection |

Remember the container caveat from Brainstorm 001: a Docker-deployed server **cannot see the host** (processes, CPU, installed SDKs). Host-visibility toolsets ship native; service toolsets ship containerized. Deployment shape is chosen *per toolset*, and that's why the transport had to be swappable.

---

## 11. State of the system (2026-07-16)

- 5 projects, dependency arrows all pointing the right way; `net10.0` pinned in one place.
- 3 tools live over stdio; handshake verified in Inspector; stdout purity verified.
- 17 tests green locally and in CI; CI proven capable of red.
- Docs: README with client registration, `TOOL_CATALOG.md`, `DECISIONS.md` (6 ADRs — two of them *emergent*, which is what makes the log real).
- Plan 001: implemented, awaiting your Stage 5 evidence. Plan 002 (HTTP + LLM_Monitor) unopened.

---

## 12. Common mistakes (several of which we personally met)

1. **Letting anything share the protocol channel** — the stderr rule, §7. We met the *launcher* variant: `dotnet run` puts a build system inside your protocol's launch path.
2. **Confusing "compiles" with "runs"** — the SDK/runtime split, §5.4. You found this one yourself; it's now ADR-006.
3. **Duplicating properties that a central props file owns** — silent override, rulebook becomes decoration.
4. **Abstracting before the second example exists** — we deferred the plugin loader *twice* and lost nothing. Separate early, abstract late.
5. **Testing through the heaviest layer** — if your tool tests need a running server, your tools know too much about the server.
6. **Unbounded tool output** — the context window is the scarcest resource in the whole system; treat it like memory in your embedded work.
7. **Undescribed tools** — a perfect implementation the model can't reason about is dead weight. Descriptions are the API.

---

## 13. Interview relevance

This little project lets you speak, from experience, about: composition roots and DI lifetimes (`TryAdd`, multi-registration, injected clocks); process models and IPC (parent/child, pipes, why stdout discipline exists); build systems (props inheritance, reference graphs, SDK-vs-runtime); protocol thinking (JSON-RPC framing, schema generation from reflection, capability negotiation); test design (behavior tests without infrastructure, executable conventions, CI you've falsified on purpose); and AI-agent architecture (tools as the hands, descriptions as prompts, context budget as a resource). The strongest story is the arc itself: *"I built the smallest thing that could be architecturally correct, proved every boundary with a test or a ritual, and wrote down every decision — then started adding capabilities."*

## 14. What to study next

- **Plan 002 preview:** `ModelContextProtocol.AspNetCore`, streamable HTTP, and what changes when your server outlives any single client connection (sessions, concurrency — your async-programming growth area, on real ground).
- **MCP beyond tools:** resources (server-pushed context) and prompts (server-defined templates) — two-thirds of the protocol we haven't touched.
- **Read the SDK's `WithTools<T>()` source** — one deliberate deep-dive, *after* shipping with it: see the reflection → JSON Schema pipeline you've been trusting. This is the abstraction-trust muscle: use first, verify once, internalize.
