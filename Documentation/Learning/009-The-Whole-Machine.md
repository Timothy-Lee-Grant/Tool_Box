2026_07_26_16_29-(The-Whole-Machine)

# Lecture 009 — The Whole Machine: Tool_Box 1.0, End to End

You shipped 1.0. Two toolsets, fifteen tools, two transports, one binary, 77 tests, twelve ADRs, a multi-arch image on a public registry, and a live browser viewer. Now you need to be able to defend every line of it cold, in a room, to someone who is actively looking for the soft spot.

That's what this document is. It is **standalone** — it assumes you've forgotten lectures 001–008 and re-teaches every concept from the ground up. It is also **audited**: Part 11 is a list of real weaknesses I found reading your code, because the only thing worse than a bug in an interview is a bug you didn't know about in an interview.

A note on evidence, same discipline as Lecture 008: there is no .NET SDK in the environment this was written in, so **I could not build or run your test suite.** Everything here is from reading the actual source files, and every audit finding is traced to a specific file and line. Two findings were cross-checked against Microsoft's documented behaviour via search. Nothing is inferred from "how this usually works."

---

## How to use this document

| If you have | Read |
|---|---|
| 5 minutes before a call | Part 0, then Part 12.1 |
| An hour | Parts 1–4, then 11–12 |
| A weekend | All of it, with the code open beside you |
| A whiteboard and a hostile interviewer | Part 12 |

---

## Part 0 — The 60-second version

> Tool_Box is an MCP server **platform** written in C#/.NET 10. MCP — Model Context Protocol — is the open protocol Claude Desktop, Claude Code, and a growing set of agent frameworks use to call tools. Most MCP servers wrap one API. This one is a platform: a thin Host that knows nothing about any capability, a Core library of shared plumbing, and independent toolset libraries that plug in through one line of composition each.
>
> The interesting constraint is that the **same binary serves two completely different wires** — stdio for local clients, streamable HTTP for containerized ones — and the toolsets don't know which. Adding the second transport required zero diffs in the toolset or Core projects. That number is the architecture's report card.
>
> It ships as a multi-arch Docker image on GHCR, and it's consumed for real by a separate project of mine, LLM_Monitor, over its compose network.

Then stop talking. The interviewer picks the thread.

---

# PART I — THE PROTOCOL

## Part 1 — What MCP actually is

### 1.1 The problem it solves

An LLM can only emit text. It cannot read a file, call an API, or place a block in a world. To *do* anything, something outside the model has to (a) tell the model what actions exist, (b) notice when the model asks for one, (c) perform it, and (d) put the result back into the conversation.

Before MCP, every agent framework invented its own way to do this. If you wrote a GitHub integration for LangChain, it didn't work in Claude Desktop. **N tools × M frameworks = N×M integrations.** MCP makes it N + M: tool builders implement one protocol, framework builders implement one protocol, and any tool works with any framework.

That's the whole pitch, and it's the same pitch as the Language Server Protocol — which is the analogy to reach for if the interviewer is an IDE person. LSP turned N editors × M languages into N + M. MCP is LSP for agent tooling.

### 1.2 The mechanics: JSON-RPC 2.0

MCP is **JSON-RPC 2.0 over a transport**. JSON-RPC is a tiny, ancient, boring spec, and that's a feature. Three message shapes:

```jsonc
// Request — has an id, expects a response
{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"ping","arguments":{"message":"hi"}}}

// Response — same id, exactly one of result | error
{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"pong: hi"}]}}

// Notification — no id, no response expected
{"jsonrpc":"2.0","method":"notifications/initialized"}
```

The `id` is what allows **pipelining**: a client may send request 2 before response 1 arrives, and match them up by id later. Hold that thought — it comes back as an audit finding in Part 11.

### 1.3 The lifecycle

```
CLIENT                                          SERVER (your ToolBox.Host)
  │                                                    │
  ├─ initialize ─────────────────────────────────────► │   protocol version + capabilities
  │ ◄─────────────────────────── initialize result ────┤   "I have tools; here's my version"
  ├─ notifications/initialized ────────────────────► │   (no response — a notification)
  │                                                    │
  ├─ tools/list ─────────────────────────────────────► │
  │ ◄──────────── [15 tools, each with a JSON Schema] ─┤   THIS is what the model reads
  │                                                    │
  │  ... model decides to call something ...           │
  ├─ tools/call {name:"place_box", arguments:{...}} ─► │
  │ ◄──────────────────── {content:[{type:"text"}]} ───┤
```

Two things about this that interviewers probe:

**The handshake is a version negotiation, not a login.** There's no auth in the base protocol. Security is the transport's job. (Part 10.)

**`tools/list` is the entire user interface for the model.** The model never sees your C# code. It sees a JSON array of `{name, description, inputSchema}`. That is why the phrase "descriptions are prompts" appears in your codebase as an *enforced test* and not a comment. More in Part 5.

### 1.4 The cast of characters

You learn best from personified components, so here's the cast for the whole lecture. Keep them in your head.

| Character | Who they are | Where they live |
|---|---|---|
| **The Doorman** | Reads the config, decides which wire to open, refuses to guess | `Program.cs` |
| **The Registrar** | Knows what the server *is* — the one list of capabilities | `ToolBoxServerComposition.cs` |
| **The Quartermaster** | Hands every component exactly the dependencies it asked for | .NET's DI container |
| **The Herald** | Announces tools to the model in language the model can act on | `[Description]` attributes |
| **The Censor** | Never lets a tool flood the model's context | `OutputLimiter` |
| **The Cartographer** | Turns "a sphere of radius 5" into a set of grid cells | `VoxelRasterizer` |
| **The Ledger** | Holds what's built, and shouts when it changes | `VoxelWorld` |
| **The Town Crier** | Broadcasts changes to anyone watching in a browser | `VoxelViewerBroadcastService` |

---

# PART II — THE ARCHITECTURE

## Part 2 — Host / Core / Toolsets

### 2.1 The layout and the one rule

```
src/
  ToolBox.Host/           ← the ONLY project that knows about transports
    Program.cs                  The Doorman
    ToolBoxServerComposition.cs The Registrar
    ToolBoxHttpApp.cs           the HTTP wire
  ToolBox.Core/           ← shared plumbing. NO MCP DEPENDENCY.
    OutputLimiter.cs
    ServerInfoProvider.cs / ServerInfo.cs / ToolsetDescriptor.cs
    Logging/ToolBoxLogging.cs
    DependencyInjection/ServiceCollectionExtensions.cs
  ToolSets/
    ToolBox.Basics/       ← independent capability library
    ToolBox.Voxel/        ← independent capability library
```

The dependency arrows all point one way:

```
   Host ────────► Core
     │              ▲
     └──► ToolSets ─┘

   Nothing points back at Host. Toolsets never reference each other.
```

**The rule that makes this work:** *only the Host knows the transport.* A toolset references the `ModelContextProtocol` package for its attributes (`[McpServerTool]`), but never for a transport. Look at the actual csproj comment — you wrote this down at the time:

> *"Toolsets still never touch the transport — that remains the Host's exclusive concern."*

And `ToolBox.Core.csproj` goes further:

> *"DI + logging abstractions only. Core deliberately has NO MCP/protocol dependency."*

That is the single most defensible line in the repo. Core could be lifted into a completely different protocol server tomorrow.

### 2.2 Why this is the "composition root" pattern

A **composition root** is the one place in an application where the object graph is assembled. Everywhere else, classes declare what they need and receive it. The value is that dependencies become *visible in one file* instead of scattered across `new` expressions.

`ToolBoxServerComposition.AddToolBoxServer()` is your composition root, and stripped of its comments it is this whole thing:

```csharp
public static IMcpServerBuilder AddToolBoxServer(this IServiceCollection services)
{
    ArgumentNullException.ThrowIfNull(services);

    services.AddToolBoxCore();

    return services
        .AddMcpServer()
        .AddBasicsToolset()
        .AddVoxelToolset();
}
```

You can read the entire capability surface of the product in three lines. And there's a comment in that file that's better than the code:

> *"If the toolset list ever appears twice in this codebase, the two transports have started drifting apart; that's the smell to watch for."*

That's an **invariant written as a tripwire.** Interviewers love this because it shows you thought about how the design decays, not just how it works on day one.

### 2.3 The measured claim

ADR-003 said "separate early, abstract late." Plan 002 added an entire second transport. The result — and this is the number to quote — was **zero diffs in `Core/` and zero diffs in the toolset projects.** Plan 003 then added a toolset that was stateful, write-classified, and brought its own background service: again zero `Core` diffs.

> **Say it exactly this way:** *"The architecture's report card isn't that it looks clean — it's that adding a transport and adding a stateful toolset both required zero changes below the Host. I measured that rather than claiming it."*

### 2.4 The counter-argument you should raise yourself

Three projects and a composition root for fifteen tools is, on its face, over-engineered. **Raise this before the interviewer does**, and answer it:

> *"For fifteen tools, yes, one project would work. I built it this way because the stated goal was a platform that grows toolsets, and I had a specific test in mind: could a second toolset with genuinely different needs — state, a background service, write semantics — land without touching shared code? It could. If that test had failed, the boundary would have been wrong and I'd have collapsed it."*

That answer converts a weakness into evidence of judgment. It also happens to be true.

---

## Part 3 — Dependency injection and the .NET Generic Host

This is the machinery underneath everything above, and it's the area most likely to get drilled in a .NET interview.

### 3.1 Inversion of Control, concretely

Without IoC, a class builds its own dependencies:

```csharp
public sealed class BasicsTools
{
    private readonly ServerInfoProvider _info = new ServerInfoProvider(/* ...and its deps? */);
}
```

That class is now welded to a concrete type and impossible to test in isolation. With IoC, the class *declares* what it needs:

```csharp
public BasicsTools(ServerInfoProvider serverInfo, TimeProvider clock)
{
    ArgumentNullException.ThrowIfNull(serverInfo);
    ArgumentNullException.ThrowIfNull(clock);
    _serverInfo = serverInfo;
    _clock = clock;
}
```

The **Quartermaster** (the DI container) supplies them. Note the null guards — that's the fail-fast discipline: a misconfigured container throws at construction with a useful message, not later with a `NullReferenceException` in a tool call.

### 3.2 The three lifetimes

| Lifetime | One instance per | Use for |
|---|---|---|
| **Singleton** | Process | Shared state, expensive-to-build things. `VoxelWorld`, `ServerInfoProvider`, `TimeProvider` |
| **Scoped** | Request (HTTP) / logical scope | Per-request context, EF DbContext |
| **Transient** | Every resolution | Cheap, stateless helpers |

**The classic interview trap — captive dependency.** If a *singleton* takes a *scoped* dependency in its constructor, the scoped object is captured and lives forever, silently becoming a singleton. .NET's container detects this at build time when scope validation is on (default in Development). Know this one; it gets asked.

### 3.3 Two DI tricks your code uses that are worth being able to explain

**`TryAdd` for idempotent, overridable registration.**

```csharp
services.TryAddSingleton(TimeProvider.System);
services.TryAddSingleton<ServerInfoProvider>();
```

`TryAdd` registers *only if nothing is registered for that type yet*. Two consequences you wrote down in the doc comment: calling `AddToolBoxCore()` twice is harmless, and **a test can pre-register a fake clock and Core will respect it.** That's not a micro-optimization — it's what makes `ServerInfoProviderTests` able to control time with no `Thread.Sleep`.

**`IEnumerable<T>` injection instead of a mutable registry.**

Each toolset registers a descriptor:

```csharp
services.AddSingleton(new ToolsetDescriptor(name, description));
```

And `ServerInfoProvider` takes `IEnumerable<ToolsetDescriptor>`. .NET's container automatically collects *every* registration of a type into an enumerable. So "what toolsets are loaded" needs no registry class, no mutable list, no initialization ordering. Your own comment nails it:

> *"Descriptors are plain singletons, so consumers simply inject `IEnumerable<ToolsetDescriptor>` — no mutable registry needed."*

This is a genuinely elegant pattern and a good thing to be asked about. It's the same mechanism ASP.NET Core uses for `IEnumerable<IStartupFilter>`, validators, and health checks.

### 3.4 `TimeProvider` — testable time

`TimeProvider` is the .NET 8+ abstraction over the clock. Injecting it instead of calling `DateTimeOffset.UtcNow` is what turns this:

```csharp
[Fact]
public void Uptime_MeasuresTimeSinceConstruction()
{
    var clock = new TestClock();
    var provider = new ServerInfoProvider(clock, []);
    clock.Now += TimeSpan.FromMinutes(5);
    Assert.Equal(TimeSpan.FromMinutes(5), provider.Get().Uptime);
}
```

...into a deterministic test instead of a flaky sleep. **Generalize the principle in an interview:** *ambient global state (clock, filesystem, randomness, environment) is the enemy of testability; inject it and tests own it.*

### 3.5 The Generic Host and `BackgroundService`

`IHost` is .NET's application container: it owns DI, configuration, logging, graceful shutdown, and a collection of `IHostedService`s. `StartAsync` starts them all; on SIGTERM/Ctrl-C it signals a `CancellationToken` and calls `StopAsync`.

`BackgroundService` is the convenience base class — you override one method:

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
```

**The critical, non-obvious thing about the Voxel toolset**, and the reason ADR-010 exists: `Host.CreateApplicationBuilder` (stdio path) and `WebApplication.CreateBuilder` (HTTP path) build *different* application shapes, but **both are `IHost` underneath.** So `services.AddHostedService<VoxelViewerBroadcastService>()` works identically on both paths, unchanged. You proved this by booting both shapes with a real WebSocket client attached rather than assuming it.

> **Interview framing:** *"A toolset can bring more than tools. ADR-010 generalized the registration convention so a toolset can register its own background service — and it works on both transports for free because `WebApplication` is an `IHost`."*

---

## Part 4 — One binary, two wires

### 4.1 The Doorman: bootstrap configuration before any host exists

```csharp
var bootstrap = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables(prefix: "TOOLBOX_")
    .AddCommandLine(args, new Dictionary<string, string> { ["--transport"] = "Transport" })
    .Build();

string transport = bootstrap["Transport"]?.Trim().ToLowerInvariant() ?? "stdio";
```

Four things here, each worth a sentence in an interview:

**1. Why a separate bootstrap config at all.** Because the answer determines *which kind of host object to construct*. You cannot build a `WebApplication` and then decide you wanted a console host. The decision must precede construction. This is a genuinely nice bit of design reasoning — most people never hit it.

**2. Precedence: last source added wins.** `appsettings.json` < `TOOLBOX_*` env < `--transport` flag. So:
- Claude Desktop launches the DLL bare → falls through to `appsettings.json`'s `"stdio"`.
- The Dockerfile sets `ENV TOOLBOX_TRANSPORT=http` → outranks the file the moment the container starts.
- **One binary, one config chain, two entirely different object graphs.**

**3. `AppContext.BaseDirectory`, not the current working directory.** Claude Desktop launches the DLL from an arbitrary cwd. A relative path would silently miss `appsettings.json` and fall back to defaults. This is the sort of thing that produces a "works on my machine" bug you cannot reproduce, and you defused it up front.

**4. Fail loud on unknown input.**

```csharp
default:
    Console.Error.WriteLine($"Unknown transport '{transport}'. Valid values: stdio, http.");
    return 2;
```

Non-zero exit code, message on stderr, no guessing. `--transport htpp` is a typo that stops the process instead of silently starting the wrong wire.

### 4.2 stdio: why stdout is sacred

Under stdio, **stdout literally is the wire.** The client parses it as a JSON-RPC stream. One stray `Console.WriteLine` interleaves with protocol frames and the client sees a corrupted stream — and the error surfaces *on the client*, nowhere near the log statement that caused it.

Hence ADR-004 and the first line of Host configuration:

```csharp
builder.Logging.UseStderrOnly();   // stays FIRST so nothing can register ahead of it
```

```csharp
logging.ClearProviders();
logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
```

`ClearProviders()` removes anything default; setting the threshold to `Trace` means *every* level goes to stderr, not just errors.

> **This is the single best "debugging story that never happened" in the repo.** You can say: *"In a stdio MCP server, stdout is the protocol. I made stderr-only logging an architectural rule enforced at the first line of composition, because that class of bug manifests on the client as an unparseable stream with no stack trace pointing anywhere useful."*

### 4.3 Streamable HTTP

"Streamable HTTP" is the literal name of the current MCP transport spec (it superseded an older HTTP+SSE transport). It's not marketing. It means the transport can hold a single HTTP response open and push multiple JSON-RPC messages over it as they occur — the mechanism for server-initiated messages mid-call (progress notifications, sampling requests).

```csharp
builder.Services
    .AddToolBoxServer()
    .WithHttpTransport(options => { options.Stateless = true; });

var app = builder.Build();
app.MapMcp("/mcp");
```

**`Stateless = true` is the decision worth defending.** It turns off session affinity: no `Mcp-Session-Id` header, no server-side session store, every request handled independently. Benefits: horizontal scaling with no sticky sessions, simpler clients. Cost: no server-to-client requests (sampling, elicitation) — which this server doesn't use.

⚠️ **This is also where a sharp interviewer will find a real tension in your design. Read Part 11.2 before you go into a room.**

### 4.4 The `/health` endpoint, and why it isn't an MCP tool

```csharp
app.MapGet("/health", (ServerInfoProvider info) => { ... status = "ok" ... });
```

Deliberately a plain HTTP GET. Docker's healthcheck, Kubernetes probes, and load balancers speak HTTP — none of them have heard of MCP. **Health must be checkable by tools that don't know your protocol.** The integration test makes this explicit by using a bare `HttpClient` with no MCP anywhere.

### 4.5 The uniformity argument (be precise about this one)

Your Stage 2 discussion contains a really good piece of reasoning that most people would get slightly wrong. The stderr rule is applied to the HTTP path too — but the *justification differs*:

- **Under stdio**, writing to stdout is a **correctness bug**. It corrupts a stream the client is parsing.
- **Under HTTP**, the protocol rides a TCP socket. A stray `Console.WriteLine` corrupts nothing; `docker logs` captures both streams anyway.

So why keep it? Your own answer:

> *"One invariant that holds unconditionally is easier to enforce with a single test and reason about than a transport-conditional one that could quietly rot on whichever path gets less attention."*

**Use this verbatim.** It's the difference between "stdout is dangerous everywhere" (wrong, and an interviewer may catch you) and "we chose uniformity over a narrower sufficient rule" (right, and demonstrably more thoughtful).

---

# PART III — THE TOOLS

## Part 5 — Tool design: the model is your user

### 5.1 Attributes → reflection → JSON Schema

```csharp
[McpServerToolType]
public sealed class BasicsTools
{
    [McpServerTool(Name = "ping")]
    [Description("Connectivity check. Returns 'pong', echoing back any message you provide. ...")]
    public string Ping(
        [Description("Optional message to echo back, ...")] string? message = null)
```

At startup the SDK reflects over types marked `[McpServerToolType]`, finds methods marked `[McpServerTool]`, and generates a JSON Schema from the method signature — parameter names, CLR types, nullability, defaults. The `[Description]` strings are attached to the schema. That schema is what `tools/list` returns, and it's **all the model ever sees.**

Return-type mapping is automatic: `string` → a text content block; a POCO like `ServerInfo` → JSON-serialized text.

### 5.2 "Descriptions are prompts" — enforced, not hoped

This is the highest-signal idea in the toolset layer, and you made it executable:

```csharp
[Fact]
public void EveryTool_HasANonEmptyDescription() { /* reflection over tool methods */ }

[Fact]
public void EveryToolParameter_HasANonEmptyDescription() { /* ... */ }
```

> **The framing:** *"The model never sees my C# code — it sees a JSON schema and a description string. An undescribed tool is the model flying blind. So I encoded the convention as a reflection test: shipping an undescribed tool is a build failure, not a mystery in production."*

This is a **convention test** (sometimes "architecture test" — NetArchTest and ArchUnit are the library-level versions). Interviewers rarely see them. It's a differentiator.

Notice too that your descriptions do real prompt-engineering work:

```csharp
[Description("Report the world's conventions ... CALL THIS FIRST, before any build — the other
              tool signatures can't tell you how big a block is meant to represent.")]
```

```csharp
[Description("Place one block. Detail only ... Use the bulk primitives (place_box, ...) for
              anything larger; looping this tool to build a wall wastes calls the primitives
              already solve.")]
```

Those aren't documentation. They're instructions steering the model away from a failure mode.

### 5.3 Call economy: describe form, not coordinates

The central insight of the Voxel toolset. A naive design exposes `place_block(x,y,z)` and nothing else. Building a castle then costs **thousands of tool calls** — blowing the context window, taking minutes, and asking a language model to do arithmetic (which it does badly).

Instead the tools take *shapes*:

```
place_box(x1,y1,z1, x2,y2,z2, material, hollow)
place_cylinder(x, z, r, h, material, y, hollow)
place_cone(x, z, y, r, h, material, r2)
place_sphere(x, y, z, r, material, ry, rz, hollow)
place_tube(path[], rStart, rEnd, material)
mirror(axis, plane)
```

A hollow tower is **one call**. The server rasterizes it into cells. And `mirror` is the sharpest one: build one wing, reflect it — perfect symmetry for one call instead of doubling the work and risking arithmetic drift.

> **Generalize it, because this is the transferable lesson:** *"Tool granularity is an architectural decision, not a convenience. Every computation you move server-side is a class of model error you permanently eliminate — and the geometry is deterministic in C# in a way it never is in a language model."*

That sentence is the same conclusion Lecture 008 reached for the SPICE toolset from a different direction. It's your strongest reusable principle.

### 5.4 Errors are values, not exceptions

```csharp
string? error = FirstFailure(ValidateGround(y, nameof(y)), Materials.Validate(material));
if (error is not null) return OutputLimiter.Limit(error);
```

And the messages are written for the model:

```
"y must be 0 or greater — 0 is ground, and this world never goes below it. Got -3."
"Unknown material \"stoen\". Did you mean: stone? Call list_materials for the full list."
```

**Why not throw?** Because an unrecognized material from an agent is *expected, recoverable input* — not a bug. An exception becomes an opaque protocol error; a string becomes a correction the model can act on immediately. The `Materials.Validate` near-match hint is genuinely thoughtful: it does substring matching in both directions and suggests candidates.

> **The principle:** *"In an agentic system, error messages are part of the control loop. A good one lets the model self-correct in one turn; a bad one burns a turn or ends the task."*

### 5.5 Closed vocabularies

`Materials.All` is a fixed list of twelve, and `Validate` gates every write. The agent may not invent a material.

This is the same pattern Lecture 008 recommended for SPICE device models, and it generalizes: **narrow the space of things the model is allowed to invent.** Where a closed vocabulary is possible, use one — validation at the boundary beats hoping the model behaves.

### 5.6 `OutputLimiter` — protecting the context window

```csharp
public static string Limit(string text, int maxChars = DefaultMaxChars)  // 20,000
```

The doc comment states the philosophy well: *"An LLM consumer has a context window, not a scrollbar — a 50 MB log dump doesn't inform the model, it evicts everything else it knew."*

Two details worth knowing cold, because they're the kind of thing that gets probed:

**Surrogate-pair safety.**

```csharp
int cut = maxChars;
if (char.IsHighSurrogate(text[cut - 1])) cut--;
```

C# strings are UTF-16. Characters outside the Basic Multilingual Plane (emoji, rare CJK, historic scripts) are stored as a *surrogate pair* — two `char` values. Cutting between them produces an invalid string. You handle it, and you test it.

**An honest contract that survived a wrong test.** The truncated result can be *longer* than the input when the omitted tail is smaller than the honesty marker. The naive assertion "result is shorter than the original" is wrong, and CI caught it. The real guarantee is stated in the test:

```csharp
// total output is at most the budget plus a small constant marker overhead —
// never proportional to the input.
Assert.InRange(result.Length, 100, 100 + markerAllowance);
```

> **Story value:** *"I wrote an assertion that encoded my assumption rather than the actual contract. CI caught it. The fix wasn't the code — it was restating the invariant precisely: bounded by budget-plus-constant, never proportional to input."*

⚠️ There's an architectural gap here too — see Part 11.5.

---

## Part 6 — State, events, and geometry

### 6.1 `VoxelWorld` — the Ledger

```csharp
public sealed class VoxelWorld
{
    private readonly Dictionary<VoxelCoordinate, string> _blocks = [];
    public event Action<VoxelChange>? Changed;
```

A `Dictionary` keyed by a `readonly record struct` coordinate. Records give you value equality and a correct `GetHashCode` for free — `new VoxelCoordinate(1,2,3)` equals another with the same values, which is exactly what a spatial hash needs. Writing that by hand is a classic source of subtle bugs.

**`ArgumentNullException.ThrowIfNull` vs. returning an error string** — note the split. `VoxelWorld` *throws* (a null coordinate list is a programming bug); `VoxelTools` *returns strings* (bad agent input is expected). Different layers, different contracts, deliberately.

### 6.2 Event batching — a performance decision with a test guarding it

```csharp
if (placed.Count > 0) Changed?.Invoke(new VoxelChange.Placed(placed));
```

One event per **call**, not per block. A 2,000-cube sphere is one WebSocket message, not 2,000. And there's a test named for the invariant:

```csharp
PlaceBlocks_RaisesOneChangedEvent_NotOnePerBlock()
```

Also note the empty-batch guard: no blocks placed → no event. Three separate tests cover the "raises no event" cases. That's disciplined.

### 6.3 Discriminated unions in C#

```csharp
public abstract record VoxelChange
{
    private VoxelChange() { }          // ← the trick

    public sealed record Placed(IReadOnlyList<PlacedVoxel> Blocks) : VoxelChange;
    public sealed record Removed(IReadOnlyList<VoxelCoordinate> Coordinates) : VoxelChange;
    public sealed record Cleared : VoxelChange;
}
```

C# has no native sum type, so this is the idiomatic emulation. **The private constructor is the whole point:** only nested types can inherit, so the hierarchy is *closed*. No outside code can add a fourth case. Consumers pattern-match:

```csharp
change switch
{
    VoxelChange.Placed placed => ...,
    VoxelChange.Removed removed => ...,
    VoxelChange.Cleared => ...,
    _ => throw new NotSupportedException(...)
}
```

If Rust/F#/Kotlin come up, this is your bridge — it's `enum`/sealed-class modelling in a language that lacks it natively.

### 6.4 The Cartographer: rasterization

`VoxelRasterizer` is pure functions — no state, no MCP, no materials. Just "which cells." Everything is `IEnumerable<VoxelCoordinate>` with `yield return`, so it streams lazily rather than materializing giant lists.

The concept is **rasterization**: converting a continuous mathematical description into a discrete grid. Same idea as a GPU turning a triangle into pixels, or Bresenham's line algorithm.

Two things worth understanding well enough to defend:

**The fudge factors are honest.**

```csharp
if (d > r + 0.5) continue;          // cylinder
if (hollow && d < r - 0.9) continue;
if (d > 1.05) continue;             // sphere
```

The class comment says these came from a reference implementation, "not from first principles — they're what makes a rasterized sphere read as round at grid resolution instead of faceted or gappy, and changing them is a visual tuning knob, not a bug fix." **That's exactly the right way to document a magic number**: say where it came from and what category of thing it is. Never pretend a tuned constant was derived.

**The ground-clamp asymmetry is correct, and you should know why.** `Sphere` and `Tube` clamp `minY = Math.Max(0, ...)`; `Box`, `Cylinder`, and `Cone` don't. That's not an inconsistency — sphere and tube are *centered* primitives that naturally extend below their anchor point, so they need the clamp. Box validates both corners at the tool layer; cylinder and cone only ever grow upward from `y`. If asked, that's a crisp answer.

**Sphere ellipsoid math**, in case it comes up: normalize each axis by its own radius and test against the unit sphere.

```csharp
double d = Hypot3((x - cx) / r, (y - cy) / effectiveRy, (z - cz) / effectiveRz);
```

**Tube deduplication:** overlapping sweep steps would emit the same cell repeatedly, so a `HashSet` guards it — and there's a test, `Tube_NeverYieldsTheSameCoordinateTwice`.

---

## Part 7 — The Town Crier: a WebSocket server, hand-rolled

`VoxelViewerBroadcastService` is 270 lines and the most concurrency-dense file in the repo. It's also, per Part 11.7, completely untested — so know it well.

### 7.1 What it is and why it's separate

A `BackgroundService` that runs an `HttpListener`, accepts WebSocket upgrades, and pushes world changes to browsers. **It is not part of MCP at all.** Your own comment:

> *"The agent never talks to this — it only ever talks to `VoxelTools`. This class exists purely for human eyes watching a browser tab."*

That separation is what lets ADR-012 make an *independent* exposure decision about port 8090. (Part 10.)

### 7.2 Port fallback

```csharp
private static readonly int[] CandidatePorts = [8090, 8091, 8092, 8093];
```

Try each; on `HttpListenerException`, move on. If all four fail, log a warning and **disable the viewer while leaving MCP fully working**. Graceful degradation: the optional feature fails, the core capability doesn't.

### 7.3 Cancelling a blocking accept

```csharp
// HttpListener.GetContextAsync() has no CancellationToken overload; Stop()-ing
// the listener from a shutdown callback is what actually unblocks it.
using CancellationTokenRegistration registration = stoppingToken.Register(() => _listener.Stop());
```

This is a genuinely good piece of knowledge. Some older APIs predate `CancellationToken` and can only be interrupted by disposing/stopping the underlying resource. Then the resulting exception is filtered rather than swallowed:

```csharp
catch (Exception) when (stoppingToken.IsCancellationRequested) { break; }
```

**Exception filters (`when`)** are worth naming explicitly: unlike `catch { if (...) throw; }`, a filter is evaluated *before* the stack unwinds, so the original stack is preserved if it doesn't match. Small detail, real .NET depth.

### 7.4 The close handshake — your best pure-protocol story

```csharp
if (result.MessageType == WebSocketMessageType.Close)
{
    // Complete the close handshake — without this, the client sees
    // "closed without completing the close handshake" even though
    // both sides intended a clean shutdown.
    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
    break;
}
```

WebSocket close (RFC 6455) is a **two-way handshake**: one side sends a Close frame, the other must echo one back before the TCP connection is torn down. Skip the echo and the peer reports an abnormal closure. You hit this, diagnosed it, and fixed it.

Note also *why* the read loop exists at all — the comment is exactly right:

> *"We never read anything meaningful from the viewer — it's receive-only — but we still have to keep reading something or we'd never notice it closed, and `_sockets` would grow forever."*

**That's a resource-leak argument**, and it's the sophisticated half of the story. The naive version of this service leaks a socket per browser refresh.

### 7.5 Snapshot-then-diff

```csharp
// A newly-connected (or refreshed) viewer has no history — it needs the full
// current state once, then diffs from here on.
```

On connect: full snapshot. Thereafter: incremental `batch` / `remove` / `clear` messages. This is the same pattern as event sourcing with a materialized snapshot, or a video keyframe followed by delta frames. Name-drop either — both are accurate.

### 7.6 Concurrency primitives

```csharp
private readonly Lock _lock = new();          // .NET 9+ Lock type, not object
```

The `System.Threading.Lock` type (.NET 9+) is the modern replacement for `lock(object)` — same semantics, better codegen, and it can't be accidentally used for something else. And note the copy-then-iterate discipline:

```csharp
lock (_lock) { targets = [.. _sockets]; }
foreach (WebSocket socket in targets) { ... await ... }
```

**You must never `await` while holding a lock**, and you must not mutate a collection while enumerating it. Copying inside the lock and iterating outside solves both. That's the correct pattern and you used it.

⚠️ There is still a real concurrency bug in this file. Part 11.3.

---

# PART IV — THE ENGINEERING

## Part 8 — Testing: a pyramid that's actually a pyramid

### 8.1 The four layers

```
        ┌─────────────────────────────────────────┐
        │  Integration (5)   HttpTransportTests    │  real Kestrel, real sockets,
        │                    real MCP client SDK   │  production app composition
        ├─────────────────────────────────────────┤
        │  Convention (7)    DescriptionConvention │  reflection over attributes —
        │                                          │  rules as executable tests
        ├─────────────────────────────────────────┤
        │  Tool layer (26)   VoxelTools, Basics    │  plain method calls,
        │                                          │  NO server, NO transport
        ├─────────────────────────────────────────┤
        │  Pure logic (39)   Rasterizer, World,    │  no I/O, no framework,
        │                    OutputLimiter, Clock  │  microseconds
        └─────────────────────────────────────────┘
                        77 test cases total
```

(75 `[Fact]` methods plus one `[Theory]` contributing 2 cases.)

**The architectural dividend, and this is the point to make:** tools are testable as plain methods because of the Host/Core/Toolset boundary.

```csharp
private static VoxelTools CreateTools(out VoxelWorld world)
{
    world = new VoxelWorld();
    return new VoxelTools(world);
}
```

No MCP server. No transport. No process. 26 tool-layer tests run in milliseconds. **Good architecture shows up as cheap tests** — that's the causal claim to make out loud.

### 8.2 Integration tests that boot the real thing

```csharp
_app = ToolBoxHttpApp.Build([], overrideUrl: "http://127.0.0.1:0");
await _app.StartAsync();
BaseUrl = _app.Urls.First().TrimEnd('/');
```

Three techniques worth naming:

**Port 0 = "OS, give me any free port."** After `StartAsync`, `app.Urls` holds the *resolved* address. Parallel CI runs can never collide on a hardcoded port. This is a well-known-but-underused trick.

**`IClassFixture<T>`** — xunit creates one fixture per test class, so the server boots once and all tests share it, with deterministic teardown via `IAsyncLifetime`.

**`InternalsVisibleTo`** — `ToolBoxHttpApp` is `internal`, and the csproj grants the test project access. The comment explains why this matters:

> *"Extracted from Program.cs so the integration tests boot the EXACT app production runs... If tests built their own copy of this, they'd be testing the copy."*

That's the sharpest testing insight in the repo. **A test that reconstructs the app under test is testing its own reconstruction.**

### 8.3 Set equality, not `Contains`

```csharp
Assert.Equal(new[] { "clear", "current_time", ..., "world_info" },
             tools.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
```

> *"Set-equality, not Contains: a tool DISAPPEARING or an unexpected tool APPEARING are both contract breaks worth failing on."*

Exactly right. `Contains` would silently pass if you accidentally shipped a debug tool.

### 8.4 Honest CI — the deliberate-red ritual

Your CI does real restore, real build with warnings fatal, real tests, and then builds the Docker image and **boots it**, polling `/health` for 30 seconds.

The habit worth telling people about: **you proved CI could go red before you trusted the badge.** The origin story is from LLM_Monitor — you discovered CI had been green while installing zero dependencies. A green badge that can't go red is worse than no badge, because it manufactures false confidence.

> **This is a top-tier interview answer to "tell me about a time you found a problem nobody was looking for."**

`-warnaserror` on the build line is belt-and-suspenders over `Directory.Build.props`'s `TreatWarningsAsErrors` — the props file makes *compiler* warnings fatal; the flag additionally covers *MSBuild-level* warnings. Knowing that distinction is a nice detail.

### 8.5 Concurrency control in CI

```yaml
concurrency:
  group: ci-${{ github.ref }}
  cancel-in-progress: true
```

A newer push to the same branch cancels the stale run. Saves runner minutes and stops you reading results from a commit you've already replaced.

---

## Part 9 — Containerization and delivery

### 9.1 Multi-stage build

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build     # ~800 MB, compilers and all
...
FROM mcr.microsoft.com/dotnet/aspnet:10.0           # ~110 MB, runtime only
COPY --from=build /app .
```

> *"Nobody ships their workshop."*

The build stage has the SDK; only published output crosses into the runtime image. ~7× smaller, and — the security point most people miss — **a compiler in a production image is an attack tool for anyone who gets in.**

### 9.2 Layer-cache choreography

```dockerfile
COPY Directory.Build.props ./
COPY src/ToolBox.Core/ToolBox.Core.csproj  src/ToolBox.Core/
... (every csproj, individually)
RUN dotnet restore src/ToolBox.Host/ToolBox.Host.csproj
COPY src/ ./src/                                      # ← source LAST
RUN dotnet publish ... --no-restore
```

Docker caches layers and invalidates everything after the first changed input. **Dependency manifests change rarely; source changes constantly.** Copying csprojs first means the slow, network-bound `restore` replays from cache on virtually every build. This is the single highest-leverage Dockerfile optimization for any compiled language — the Node equivalent is `COPY package*.json` before `COPY .`.

### 9.3 Non-root

```dockerfile
USER $APP_UID
```

Microsoft's .NET images define `APP_UID` for exactly this. Container escape via a root process is materially worse than via an unprivileged one. Note the ordering: `apt-get install` must run *before* `USER`, because installing packages needs root — and your Dockerfile comment says so.

### 9.4 The two CI/CD debugging stories in the Dockerfile and release workflow

**Story A — the apt mirror timeout.** The `aspnet` base image ships neither `curl` nor `wget`, so the compose healthcheck needs one installed. But:

> *"GitHub Actions runners are Azure-hosted, and the default Ubuntu mirrors are intermittently unreachable from there — a real, observed CI failure (every IP for both hosts timed out in one actual run), not a config mistake."*

Fix: rewrite sources to Canonical's Azure-hosted mirror, plus `-o Acquire::Retries=5`, plus `|| true` so a non-matching `sed` on a different base never breaks the build. **Three layers of defense, each justified.** Note also this is the *same* trap LLM_Monitor hit with a slim Python image — you recognized a pattern across projects.

**Story B — the amd64-only image.** This is your best "I reviewed something and missed a thing" story, and you wrote it down honestly:

- `v1.0.0` published. `docker compose pull` on Apple Silicon: `no matching manifest for linux/arm64/v8`.
- Root-caused with `docker manifest inspect` → exactly one platform, `amd64`.
- Cause: `docker/build-push-action` **without an explicit `platforms:` key only builds for the runner's own architecture.** There is no implicit multi-arch behaviour to opt out of.
- Fix: `docker/setup-qemu-action` (emulation for the cross-build) + `platforms: linux/amd64,linux/arm64` (produces one **manifest list** covering both).
- And the honest note: *"this is a gap in the Stage-3 review two entries above — that pass caught the lowercase-image-name bug and the missing Buildx setup step, but missed [this]. Worth naming plainly since I did that review: the check wasn't broad enough at the time."*

Two more details from that thread that show real depth:

- **Why QEMU emulation is safe here:** the Dockerfile runs a plain `dotnet publish` with no `-r <RID>` — framework-dependent, portable IL. There's no architecture-specific compilation to get wrong; QEMU only emulates the SDK long enough to run restore/publish. **The slower, riskier native cross-compile problem doesn't apply.**
- **Registry tags are immutable in practice:** `1.0.0` is amd64-only forever; re-running an old workflow doesn't mutate a published tag. A new tag was required.

**Story C — lowercase image names.** `ghcr.io/${{ github.repository }}` yields `Timothy-Lee-Grant/Tool_Box`. OCI registries **reject uppercase in image references**. Caught in review before the first run, fixed with a `tr '[:upper:]' '[:lower:]'` step into `$GITHUB_ENV`.

### 9.5 Tag strategy

```yaml
type=raw,value=latest,enable=${{ github.ref == 'refs/heads/main' }}
type=semver,pattern={{version}}
```

`:latest` only on `main` pushes; a pinned `:X.Y.Z` only on an actual `v*.*.*` tag. And the consuming project **pins a version, never `:latest`** — because `latest` is not a version, it's a moving target that makes builds irreproducible.

### 9.6 Why a registry, and the alternative you rejected

The consuming project's compose had `build: context: ../Tool_Box`. That works only when both repos sit side by side on one machine — and **breaks the moment the consuming project's own CI runs**, because that runner has no sibling checkout.

You considered a second `actions/checkout` and rejected it, for three reasons worth reciting:

1. Needs its own token/permissions wiring.
2. Rebuilds from whatever `HEAD` happens to be, rather than a tested tag.
3. Doesn't scale past one consumer.

> *"A registry pull is strictly less machinery for more determinism."*

That's a clean architectural-tradeoff answer, and it's a real cross-repo integration problem — much better material than a toy example.

---

## Part 10 — Security posture

### 10.1 What's actually true

There is **no authentication.** The HTTP endpoint executes tools for anyone who can reach it. The controls are all deployment-topology:

| Control | Where |
|---|---|
| No published ports in the consuming compose | LLM_Monitor's `docker-compose.yaml` |
| Dev-only port mapping (`8081:8080`) | this repo's `docker-compose.yml` |
| `AllowedHosts` pinned (DNS-rebinding defense) | compose `environment:` — ⚠️ see 11.4 |
| Non-root container user | `Dockerfile` |

### 10.2 ADR-011 — the one to be proud of

ADR-008 originally listed four mitigations, including *"(4) all current tools are read-only."* Then Voxel arrived — the first write-classified toolset — and shipping it over HTTP meant literally doing the thing ADR-008 said shouldn't happen yet.

**You surfaced this explicitly rather than quietly proceeding**, then re-examined the original reasoning and found that items (1)–(3) were deployment controls that apply equally to read and write tools — *they were always the actual protection*. Item (4) was true by coincidence of what had been built, not a designed-in gate.

> **This is your best judgment story, and the framing matters:** *"I found that one of my own security ADRs contained a promise the project was about to break. Rather than silently drift, I wrote a superseding ADR that re-derived which of the original mitigations were load-bearing. Three were structural; the fourth was accidental. The record shows the reasoning, so a future reader doesn't find write tools over HTTP and wonder if the ADR was forgotten."*

The meta-point — **append-only ADRs, supersede rather than edit** — is what makes this possible. An edited ADR would have erased the interesting part.

### 10.3 ADR-012 — capability-based exposure reasoning

Port 8090 (the viewer) is published; port 8080 (MCP) is not. The reasoning isn't "8090 feels safer," it's **what capability does this channel grant a caller?**

- MCP endpoint → executes tools, including writes. Lock down.
- Viewer WebSocket → send-only, no command surface, reads only enough to detect a close frame. A caller can *watch*, and nothing else.

> *"Different channels get different exposure postures, decided by what they let a caller do — not by analogy to the port next door."*

⚠️ There's a nuance here you should own before someone else raises it — Part 11.6.

### 10.4 The gap you should name yourself

**Toolsets are registered unconditionally on both transports.** `AddToolBoxServer()` gives HTTP callers the identical roster stdio gets. That's fine today — neither Basics nor Voxel touches hardware. But your own Release Checklist flags the future problem: a hardware toolset (I2C/SPI/GPIO — your day-job domain) would get registered inside the Docker image too, where the hardware doesn't exist and a network caller has no business reaching it anyway.

The planned fix is a config-driven allowlist (`ToolBox:EnabledToolsets`). **It is deliberately not built**, consistent with "abstract from evidence, not imagination." Saying that out loud — *known gap, documented, deferred with a trigger condition* — is much stronger than being caught without an answer.

---

# PART V — THE AUDIT

## Part 11 — What a sharp interviewer would find

These are real. I found them reading your code. Each has a location, a severity, and — most importantly — **the answer you should give if it comes up.** Owning a limitation with a crisp remediation is a strength; being surprised by one is not.

### 11.1 `Uptime` measures time since first *use*, not process start

**Where:** `ServerInfoProvider.cs` — `_startedAt = clock.GetUtcNow()` in the constructor, registered via `TryAddSingleton<ServerInfoProvider>()`.

**The issue:** .NET's DI container constructs singletons **lazily, on first resolution** — not at startup. Nothing resolves `ServerInfoProvider` during boot. It's first constructed when someone calls `server_info` or hits `/health`. So `Uptime` under-reports, and the XML comment saying *"(process start, in practice)"* is not quite true.

**Severity:** Low — cosmetic. But it's a *documentation-contradicts-behaviour* bug, which is the kind interviewers enjoy.

**Your answer:** *"Correct — that's lazy singleton construction. The fix is one line: resolve it eagerly at startup, or capture the start time from `Environment.TickCount64`/`Process.StartTime` instead of construction time. The doc comment overstates it and should be corrected."*

### 11.2 `VoxelWorld` is not thread-safe, and `Stateless = true` makes concurrency reachable ⚠️ **the big one**

**Where:** `VoxelWorld.cs` (plain `Dictionary`, no locking) + `ToolBoxHttpApp.cs` (`options.Stateless = true`) + `VoxelToolsetExtensions.cs` (`AddSingleton<VoxelWorld>()`).

**The issue — and this is subtler than what ADR-009 documents.** ADR-009 names the *semantic* risk: "two simultaneous HTTP-connected agents would edit the same world." That's honest. But it does not name the **memory-safety** risk, which is worse and doesn't need two agents:

- ASP.NET Core handles requests **concurrently by default.**
- JSON-RPC allows **pipelining** — a single client may have multiple `tools/call` requests in flight.
- `Stateless = true` is explicitly documented (in your own comment) as enabling *"horizontal scaling without affinity"* — i.e. it advertises concurrency safety the state layer doesn't have.
- Concurrent writes to a `Dictionary<K,V>` are **undefined behaviour**: corrupted internal buckets, `IndexOutOfRangeException` during resize, or — the classic — an **infinite loop** on read if a resize is interrupted mid-way.

`MirrorAcross` is the nastiest case: it snapshots the keys, then mutates the live dictionary while iterating that snapshot. A concurrent `Clear()` mid-mirror is a genuinely bad time.

**Severity:** Medium in practice (an MCP agent loop calls tools serially, which is why it hasn't bitten), **High in principle** — because the HTTP configuration explicitly claims scalability the state model can't support.

**Your answer — and lead with the distinction, it's the impressive part:**

> *"ADR-009 documents the semantic risk of a shared world, but you've found something sharper: it doesn't name the memory-safety risk. `Dictionary` under concurrent write is undefined behaviour, not just a lost update, and `Stateless = true` advertises exactly the scaling property the state layer can't honour. That's a real inconsistency between two of my own decisions.*
>
> *Three fixes, in increasing order of correctness: a `lock` around the mutating methods — a few lines, and enough given calls are short; `ConcurrentDictionary`, which is fine per-operation but doesn't make `MirrorAcross` atomic since that's a compound read-modify-write; or the actually-right answer, which is session-scoped state — the SDK supports per-connection server instances over streamable HTTP, so each client gets its own world and the whole class of problem disappears. I'd take the lock today and the session scoping when a real multi-client scenario appears."*

That answer demonstrates you understand the difference between a data race and a lost update, and that `ConcurrentDictionary` is not a magic fix for compound operations. Both are things senior .NET candidates get wrong.

### 11.3 Fire-and-forget broadcast can call `SendAsync` concurrently on one socket

**Where:** `VoxelViewerBroadcastService.cs`

```csharp
private void OnWorldChanged(VoxelChange change) => _ = BroadcastAsync(BuildChangeMessage(change));
```

**The issue:** `_ =` discards the task. Two problems:

1. **Concurrent `SendAsync` on the same `WebSocket` is not allowed** — it throws `InvalidOperationException` ("There is already one outstanding 'SendAsync' call"). Two world changes in quick succession start two overlapping `BroadcastAsync` calls that both iterate the same socket list.
2. **Ordering is not guaranteed.** A `remove` could reach the viewer before the `place` it follows, leaving the browser showing a world that never existed.

It hasn't bitten because MCP tool calls arrive serially — the same reason 11.2 hasn't. **Both bugs are hidden by the same accident.**

**Severity:** Medium latent.

**Your answer:** *"That's an unbounded fire-and-forget with no send serialization. The fix is a single-consumer queue — a `Channel<string>` written by the event handler and drained by one writer loop per socket. That gives ordering and mutual exclusion in one move, and it also bounds memory if a slow viewer can't keep up, which the current code doesn't."*

`System.Threading.Channels` is the right answer here and naming it specifically will land well.

### 11.4 `AllowedHosts` protection exists only in compose, not in the app's defaults

**Where:** `src/ToolBox.Host/appsettings.json` contains exactly `{"Transport": "stdio"}` — **no `AllowedHosts` key.** The value is set only via `docker-compose.yml`'s `environment:`.

**The issue:** ASP.NET Core's host-filtering middleware is effectively **disabled when `AllowedHosts` is absent** — any `Host` header is served. So ADR-008 item (3) ("`AllowedHosts` is pinned... DNS-rebinding defense") describes a control that lives in *one deployment file*, not in the application. Anyone who runs the published image directly — `docker run ghcr.io/timothy-lee-grant/tool_box` — or runs `--transport http` on a dev box gets **no host filtering at all.**

**Verified:** Microsoft's documentation confirms the middleware is not enabled unless `AllowedHosts` is defined, and that the default permits any non-empty host.

**Severity:** Medium — it's a documented control that isn't where the doc implies it is.

**Your answer:** *"Good catch — that's ADR/code drift. The ADR describes the control as if it's a property of the application; it's actually a property of one compose file. The fix is to put a restrictive default in `appsettings.json` so the app is safe by default and compose only ever widens it. Right now the safe configuration is opt-in, and it should be opt-out."*

"Safe by default, opt out" versus "unsafe by default, opt in" is a strong security principle to articulate.

### 11.5 `OutputLimiter` is a convention, not an enforced boundary

**Where:** `OutputLimiter.Limit()` is called manually in every string-returning tool. But `server_info` returns a `ServerInfo` object and `current_time` returns a `CurrentTime` object — **neither passes through the limiter.**

**The issue:** those two are small and bounded, so there's no bug today. But the stated platform invariant is *"no tool ever returns unbounded output,"* and it's enforced by **remembering to call a static method.** A future toolset returning `List<SomeRecord>` bypasses it entirely and nothing fails.

**Severity:** Low today, architectural.

**Your answer:** *"That's a convention where I claimed an invariant. The structural fix is to move it off the call site — either a serialization-time wrapper on the MCP result pipeline, or a convention test like the `[Description]` one that reflects over tool methods and asserts every string-returning tool routes through the limiter. I already used the executable-convention pattern for descriptions; I just didn't extend it here."*

That last sentence is the strong part — you're pointing at a pattern you already established and admitting you under-applied it.

### 11.6 ADR-012's wildcard bind is a Windows regression and widens stdio exposure

**Where:** `VoxelViewerBroadcastService.StartListener()` — `listener.Prefixes.Add($"http://+:{port}/voxel/")`.

Two distinct issues from one change:

**(a) Windows.** `HttpListener` on Windows sits on HTTP.sys, which requires URL ACL reservations. The **strong wildcard `+` requires administrator elevation or a `netsh http add urlacl` reservation**; `http://127.0.0.1:{port}/` — the pre-ADR-012 binding — did not. So on a non-elevated Windows host, all four candidate ports now throw `HttpListenerException`, the fallback loop exhausts, and **the viewer silently disables** with only a warning. ADR-012 was justified entirely by Docker/Linux behaviour and doesn't mention the Windows cost.

**(b) stdio exposure.** `AddVoxelToolset()` registers the broadcast service unconditionally, so **stdio mode also binds `+:8090` — all interfaces.** ADR-012 argued exposure was acceptable for the container case; the local-dev case inherited it silently. On a laptop on public wifi, anyone on the LAN can watch your voxel world. Low impact (read-only, no secrets) but it wasn't a decision anyone made.

**Verified:** Microsoft's `netsh http` docs and multiple sources confirm wildcard prefixes require elevation or an explicit urlacl, and that localhost-scoped prefixes follow different rules.

**Severity:** Medium (a) — a platform-specific regression that CI can't see, since CI is Linux-only.

**Your answer:** *"Two things I'd fix. First, the bind should be environment-aware rather than unconditional — `+` when containerized, loopback otherwise — because the wildcard needs elevation on Windows and my CI is Linux-only, so it can't catch that. Second, and this is the honest one: ADR-012's exposure argument was about the container case, and stdio inherited it without a separate decision. It should be its own line in the ADR."*

### 11.7 The most concurrency-dense file has zero tests

**Where:** `VoxelViewerBroadcastService.cs`, 270 lines. The test list contains nothing for it.

77 tests, and the one file with locks, background threads, fire-and-forget tasks, and a network protocol is untested. It's also where findings 11.3 and 11.6 live — **which is not a coincidence.**

**Your answer:** *"That's the right criticism, and the correlation isn't accidental — two of the three real bugs in this codebase live in the one untested file. It's harder to test because it needs a real socket, but not that much harder: the service exposes the bound `Port`, so a test could start the host, connect a `ClientWebSocket`, assert the snapshot arrives, mutate the world, and assert the diff arrives. That's the gap I'd close first."*

### 11.8 ADR-004's verification is a manual ritual, not a test

ADR-004 says the stdout-purity rule is *"verified by the stdout purity test: `dotnet run --project src/ToolBox.Host 2>/dev/null` must print nothing."* That's a command a human runs. It's **not in CI**, and it appears in the Release Checklist as a manual re-verification item.

So the single most protocol-critical invariant in the codebase depends on somebody remembering.

**Your answer:** *"Fair. And it's automatable — start the process, redirect stderr to null, send an `initialize` frame, assert stdout contains only valid JSON-RPC. I have integration tests for the HTTP transport and none for stdio, which is backwards given stdio is the default and the one with the sharp edge."*

Note this also means **the stdio path has no automated test coverage at all** — including the transport-selection switch and the exit-code-2 unknown-transport path.

### 11.9 Documentation drift: the README says 11 ADRs; there are 12

`docs/DECISIONS.md` contains ADR-001 through ADR-012. `README.md` says *"(11 so far)"*, and plan 005 says *"11 ADRs"* — both written before ADR-012 landed on 2026-07-25.

**Severity:** Trivial, but it's the *exact* class of drift your own Release Checklist has an item for ("confirm `docs/TOOL_CATALOG.md` lists exactly the 15 tools that exist in code — a drift check"). Worth fixing before anyone reads the repo. (The tool catalog itself, I checked — 3 + 12 = 15, no drift there.)

### 11.10 Minor observations

| Observation | Note |
|---|---|
| `RemoveSocket` can run twice for one socket (broadcast failure + close path) | Double `Dispose`; benign but sloppy |
| `WaitForCloseAsync` reads into a 1-byte buffer | Fine for close detection; would spin on a large client message |
| `VoxelRasterizer.Sphere` divides by `r` with no guard | Tool layer validates `r ≥ 1`, but the public static method doesn't |
| `BuildChangeMessage`'s `NotSupportedException` is thrown inside a discarded task | Unobservable; the closed hierarchy makes it unreachable, but it's a silent path |

---

# PART VI — THE INTERVIEW

## Part 12 — The playbook

### 12.1 Three lengths, rehearsed

**The 30-second version (recruiter / warm-up):**
> *"It's an MCP server platform in C#. MCP is the open protocol Claude and other agent frameworks use to call tools. Rather than wrapping one API, I built a platform — a thin host, shared plumbing, and independent toolset libraries. It runs over two different transports from one binary and ships as a multi-arch Docker image that another one of my projects consumes over its compose network."*

**The 3-minute version (technical screen):** add — the Host/Core/Toolset boundary and the zero-diffs measurement; the transport-selection bootstrap and why config must precede host construction; the call-economy tool design (shapes, not coordinates); the four-layer test pyramid and why tool tests need no server; the ADR log with one ADR that supersedes another.

**The 10-minute version (onsite / system design):** draw 12.4, then pick **two** deep dives based on the room — Part 4 (transports/config) for a backend role, Part 5 (tool design + prompt surface) for an AI role, Parts 7–9 (WebSockets, CI/CD, multi-arch) for infra.

### 12.2 Six STAR stories

Real ones from your own plan logs. **Rehearse the Result sentence for each** — that's the part people fumble.

---

**① The CI that couldn't go red** *(best story you have — lead with it)*

- **S:** Building CI for the platform, with a green badge on the README.
- **T:** Make green *mean* something.
- **A:** Before trusting it, deliberately broke the build to prove CI could fail. This came from a prior project where I'd discovered CI had been green while installing zero dependencies — the pipeline was passing because it wasn't doing anything.
- **R:** The badge is backed by real restore, real build with warnings-as-errors, real tests, and a container that boots and answers a healthcheck. **A green badge that can't go red is worse than no badge — it manufactures false confidence.**

---

**② The SDK API-drift saga** *(shows how you handle being wrong)*

- **S:** Writing integration tests against the MCP C# SDK's client API.
- **T:** Connect over streamable HTTP.
- **A:** Guessed the type names from memory. Wrong. Guessed again. Wrong. Stopped guessing and read the package's own XML docs.
- **R:** Correct on the third attempt — **and the docs also surfaced two improvements I wouldn't have found otherwise**: the `Stateless` option and the `AllowedHosts` DNS-rebinding guidance that became ADR-008. The lesson wasn't "read the docs," it was that reading them paid a dividend beyond the immediate fix.

---

**③ The security ADR I had to supersede** *(judgment; use for "disagree with yourself")*

- **S:** ADR-008 justified an unauthenticated HTTP endpoint partly on "all current tools are read-only."
- **T:** Ship the first write-classified toolset over that same endpoint.
- **A:** Surfaced the conflict explicitly instead of proceeding quietly. Re-derived which of the four mitigations were load-bearing: three were deployment-topology controls that apply regardless of read/write; the fourth was true by coincidence, not by design.
- **R:** ADR-011 supersedes item (4) with the reasoning on the record. **Append-only ADRs are what made this possible — editing the original would have erased the interesting part.**

---

**④ The amd64-only image** *(owning a review miss)*

- **S:** `v1.0.0` published to GHCR; consuming project on Apple Silicon failed to pull.
- **T:** Find out why a "successful" publish produced an unusable artifact.
- **A:** `docker manifest inspect` → one platform. Root cause: `docker/build-push-action` without an explicit `platforms:` key builds only for the runner's architecture. Added QEMU + `platforms: linux/amd64,linux/arm64`.
- **R:** One manifest list serving both architectures. **Two things I'd add: I had reviewed that workflow earlier and caught two other bugs but missed this one — I wrote that down rather than quietly fixing it. And the already-published tag stayed amd64-only forever, because registry tags aren't mutated by re-running an old workflow. That's a deployment property worth knowing before you need it.**

---

**⑤ The phantom empty world** *(pure debugging; the best "how do you isolate a problem" answer)*

- **S:** Voxel viewer connected fine through Docker but always showed an empty world — while `describe_world` reported the correct block count every time.
- **T:** Find out where the state was diverging.
- **A:** Worked outward instead of guessing. Confirmed the WebSocket connected and delivered a snapshot. Added temporary hash-code logging on both the tool path and the broadcast service's `VoxelWorld` reference — **same object, `GetHashCode() == 31364015` on both sides**, and the change event fired on every mutation... to zero connected sockets. So the DI wiring was correct and the problem was outside the process. `lsof -nP -iTCP:8090` found a **second, native `ToolBox.Host` process bound to 8090 since four days earlier** — a leftover from local stdio testing. Because the viewer service runs under *any* transport, that orphan had started its own broadcaster and won the port. macOS routes a connection to the literal `127.0.0.1` to the more specific listener ahead of Docker's wildcard proxy — so every test connection landed on an orphaned, always-empty world.
- **R:** The fix was `kill 80681` — **not a code change.** The bind fix and port publishing were both already correct. **The lesson is that "my code is broken" was the wrong hypothesis for four steps, and the thing that broke the deadlock was proving object identity rather than reasoning about it.**

---

**⑥ The preview SDK that compiled what it couldn't run** *(environment debugging)*

- **S:** MCP Inspector handshake failed against a server that built cleanly.
- **T:** Explain a clean build that wouldn't run.
- **A:** Root-caused to a .NET 11 *preview* SDK: scaffolding had defaulted the target framework to `net11.0`, and **an SDK can compile a target it cannot run** — executing needs that runtime installed separately.
- **R:** Pinned `net10.0` (LTS) once in `Directory.Build.props`, with CI pinned to `10.0.x` to match — ADR-006. **Also wrote down that projects must not set their own TFM, because a csproj value silently overrides the props file and defeats central enforcement.**

---

### 12.3 Hostile follow-ups, with answers

| Question | Answer |
|---|---|
| **"Isn't three projects overkill for 15 tools?"** | Part 2.4 — the goal was a platform, and the test was whether a second, differently-shaped toolset could land without touching shared code. It could, with zero `Core` diffs. If that had failed I'd have collapsed the boundary. |
| **"Your world state is a singleton `Dictionary` and you run HTTP concurrently. Isn't that a data race?"** | **Yes.** Part 11.2 — and note it's worse than the lost-update problem my ADR documents; `Dictionary` under concurrent write is undefined behaviour. Lock now, session-scoped state when a real multi-client case appears. Don't reach for `ConcurrentDictionary` — it wouldn't make `MirrorAcross` atomic. |
| **"No auth on a tool-execution endpoint?"** | Correct, and documented in ADR-008/011. Isolation is the control: unpublished ports in consuming composes, dev-only mapping here. The rule is "never publish the MCP port beyond a trusted network." Auth is scheduled for the first non-trusted network. |
| **"How do you know the model will call your tools correctly?"** | I don't — I constrain it. Descriptions are prompts (enforced by a reflection test), the parameter space is a closed vocabulary, invalid input returns a correcting string rather than an exception, and computation lives server-side so the model never does arithmetic. |
| **"What happens if a tool returns 50 MB?"** | `OutputLimiter` bounds it at budget-plus-a-constant marker. Part 11.5 — that's currently a convention on the string path, not a structural boundary, and I'd close it with a convention test or a pipeline wrapper. |
| **"Why not just use the Python MCP SDK?"** | The protocol is language-neutral; server language is independent of client language. My target roles are .NET, and the Python consumer talks to it over HTTP via `langchain-mcp-adapters`. ADR-001. |
| **"How would you scale this?"** | Stateless HTTP is already on, so the transport scales horizontally today. The blocker is toolset state — Voxel's singleton world. Session-scoped state or an external store is the prerequisite, and I'd do that before adding instances, not after. |
| **"What would you do differently?"** | Session-scoped state from the start; a `Channel`-based broadcaster instead of fire-and-forget; and an automated stdio-purity test instead of a manual ritual. All three are in my own audit. |
| **"How much of this did AI write?"** | Substantial parts, through a staged process I designed: written goals → recorded discussion → reviewed plan → step-by-step permissioned execution → verification, all logged. **Every decision and deviation is in the plan documents.** The judgment, the architecture, and the review are mine — including catching a workflow bug in a file the AI had just reviewed. |

**That last question is coming.** Answer it directly and without apology. Your `Documentation/ImplementationPlans` folder is a *better* answer than most candidates can give, because it's auditable.

### 12.4 The whiteboard diagram

Practice drawing this in under 90 seconds.

```
   Claude Desktop / Claude Code            LangGraph agent (LLM_Monitor)
              │                                        │
       stdio (stdout = wire)              streamable HTTP :8080/mcp
              │                                        │
              ▼                                        ▼
   ┌──────────────────────────────────────────────────────────────┐
   │ ToolBox.Host                                                 │
   │   Program.cs        config precedence:                       │
   │                     appsettings < TOOLBOX_env < --transport   │
   │                     ──► switch ──► stdio | http | exit 2     │
   │   ToolBoxServerComposition.AddToolBoxServer()  ← the ONE list │
   │   ToolBoxHttpApp    Kestrel · MapMcp("/mcp") · GET /health   │
   └───────────────┬──────────────────────────────┬───────────────┘
                   │                              │
     ┌─────────────▼────────────┐   ┌─────────────▼──────────────┐
     │ ToolSets/ToolBox.Basics  │   │ ToolSets/ToolBox.Voxel      │
     │  ping, server_info,      │   │  12 shape tools             │
     │  current_time            │   │  VoxelWorld (singleton)     │
     │                          │   │  VoxelRasterizer (pure)     │
     │                          │   │  ViewerBroadcastService ────┼──► ws :8090
     └─────────────┬────────────┘   └─────────────┬──────────────┘    (browser,
                   │                              │                    send-only)
                   └──────────────┬───────────────┘
                                  ▼
                        ┌──────────────────────┐
                        │ ToolBox.Core         │  NO MCP DEPENDENCY
                        │  OutputLimiter       │
                        │  ServerInfoProvider  │
                        │  Logging (stderr)    │
                        └──────────────────────┘
```

**Say while drawing:** *"Arrows point one way. Nothing points back at Host, toolsets never reference each other, and Core has no protocol dependency at all — which is why adding the HTTP transport needed zero diffs below the Host."*

### 12.5 Claims to make, and claims to avoid

| ✅ Say | ❌ Don't say |
|---|---|
| "Zero `Core` diffs when I added a transport — measured." | "It's fully scalable." |
| "77 tests across four layers; tool tests need no server." | "It's well tested." *(vague — 11.7 exists)* |
| "No auth; isolation is the documented control." | "It's secure." |
| "Single-world singleton is a documented v1 limitation." | "It's thread-safe." |
| "Multi-arch image on GHCR, consumed by another project." | "Production-ready." |
| "Twelve ADRs, one of which supersedes another." | *(don't say eleven — see 11.9)* |

**The general rule:** claim *measurements*, not adjectives. "Zero diffs," "77 tests," "one binary, two transports" are checkable. "Robust," "scalable," "production-ready" are invitations to be tested and found wanting.

---

## Part 13 — Concept index

Everything this project touches, with where to look. If you can teach each row in two minutes, you're ready.

| Concept | Where in the code | Part |
|---|---|---|
| MCP / JSON-RPC 2.0 / lifecycle | the wire itself | 1 |
| Composition root, dependency direction | `ToolBoxServerComposition.cs` | 2 |
| IoC, DI lifetimes, captive dependency | `ServiceCollectionExtensions.cs` | 3.1–3.2 |
| `TryAdd` idempotent registration | `AddToolBoxCore()` | 3.3 |
| `IEnumerable<T>` multi-registration | `ToolsetDescriptor` | 3.3 |
| `TimeProvider` / testable time | `ServerInfoProvider.cs` | 3.4 |
| Generic Host, `BackgroundService`, graceful shutdown | `VoxelViewerBroadcastService.cs` | 3.5, 7 |
| Configuration precedence, bootstrap-before-host | `Program.cs` | 4.1 |
| stdio protocol purity | `ToolBoxLogging.cs` | 4.2 |
| Streamable HTTP, `Stateless` | `ToolBoxHttpApp.cs` | 4.3 |
| Attribute → reflection → JSON Schema | `[McpServerTool]` | 5.1 |
| Descriptions as prompts; convention tests | `DescriptionConventionTests.cs` | 5.2 |
| Call economy / tool granularity | `VoxelTools.cs` | 5.3 |
| Errors as values | `Materials.Validate` | 5.4 |
| Closed vocabularies | `Materials.cs` | 5.5 |
| Bounded output; UTF-16 surrogate pairs | `OutputLimiter.cs` | 5.6 |
| Records, value equality, spatial hashing | `VoxelTypes.cs` | 6.1 |
| Event batching | `VoxelWorld.PlaceBlocks` | 6.2 |
| Discriminated unions in C# | `VoxelChange` | 6.3 |
| Rasterization; `yield return` laziness | `VoxelRasterizer.cs` | 6.4 |
| WebSocket upgrade + RFC 6455 close handshake | `WaitForCloseAsync` | 7.4 |
| Snapshot-then-diff sync | `BuildSnapshotMessage` | 7.5 |
| `Lock`, copy-then-iterate, never await under lock | `BroadcastAsync` | 7.6 |
| Exception filters (`catch ... when`) | `ExecuteAsync` | 7.3 |
| Test pyramid; `IClassFixture`; port 0; `InternalsVisibleTo` | `HttpServerFixture.cs` | 8.2 |
| Multi-stage builds, layer-cache ordering, non-root | `Dockerfile` | 9.1–9.3 |
| Multi-arch images, QEMU, buildx, manifest lists | `docker_image_release.yml` | 9.4 |
| Semantic version pinning vs `:latest` | tag strategy | 9.5 |
| Threat modelling by capability; ADR supersession | `DECISIONS.md` | 10 |
| Data race vs. lost update; session-scoped state | audit | 11.2 |
| `System.Threading.Channels` for ordered async | audit | 11.3 |
| Safe-by-default configuration | audit | 11.4 |

---

## Part 14 — What to carry away

1. **The architecture's claim is a measurement, not an adjective.** Zero `Core` diffs across a new transport and a new stateful toolset. Lead with numbers.
2. **Only the Host knows the transport.** That one rule generates the project layout, the testability, and the zero-diff result. If you remember one sentence about the design, remember that one.
3. **The model is your user.** Descriptions are prompts, errors are correction signals, and every computation you move server-side is a class of model error you delete. This is the same conclusion Lecture 008 reached for SPICE — it's your reusable principle.
4. **Executable conventions beat written ones.** The `[Description]` reflection test is the pattern; Part 11.5 is where you didn't apply it and should.
5. **Append-only ADRs let you be wrong in public.** ADR-011 is the most senior artifact in the repo *because* it revises ADR-008 rather than hiding it.
6. **Your best stories are the ones where you were wrong.** The CI that couldn't go red, the two bad SDK guesses, the amd64-only image you'd already reviewed, four steps of debugging aimed at the wrong hypothesis. Interviewers hire for how you handle being wrong, and you have written records of it.
7. **Know your own audit.** Nine real findings in Part 11. Raising one yourself converts it from a gotcha into evidence of rigour — and the concurrency finding in 11.2 is the one to have loaded and ready.
8. **Two of the three real bugs live in the one untested file.** That's not a coincidence, and saying so out loud is a better argument for testing than any principle.

### Where to go next

- **Close 11.2 and 11.3.** A lock and a `Channel`-based broadcaster are maybe forty lines together, and they turn two audit findings into two "I found this and fixed it" stories — which are worth more than clean code that was never broken.
- **Write the stdio integration test (11.8).** It closes the coverage gap on the *default* transport and retires a manual ritual.
- **Fix 11.9 before anyone reads the repo.** One number.
- **Then plan 004 (SPICE).** Lecture 008 has the concepts and the revised plan; it's the first genuinely closed-loop toolset and it'll be the strongest thing in this repo when it lands.

You built a platform, shipped it, packaged it, integrated it with a second system, and documented the reasoning at every step. The hard part now isn't the engineering — it's being able to say all of it in ten minutes without underselling it. That's what Part 12 is for. Rehearse it out loud.

---

## Sources

Two audit findings were verified against external documentation rather than asserted:

- [netsh http — URL ACL reservations (Microsoft Learn)](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netsh-http) — wildcard `HttpListener` prefixes require elevation or an explicit urlacl (finding 11.6)
- [Host filtering with ASP.NET Core Kestrel (Microsoft Learn)](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/host-filtering) and [HostFilteringOptions.AllowedHosts](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.hostfiltering.hostfilteringoptions.allowedhosts) — host filtering is not enabled unless `AllowedHosts` is defined (finding 11.4)

Everything else is traced to files in this repository: `src/`, `tests/`, `docs/DECISIONS.md`, `docs/TOOL_CATALOG.md`, `Dockerfile`, `docker-compose.yml`, `.github/workflows/`, and `Documentation/ImplementationPlans/001`–`005`.
