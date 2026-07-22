2026_07_17_23_34-(Objects-Wiring-And-Control)

# Lecture 003 — Objects, Wiring, and Control: OOP and DI Through Your Own Codebase

You asked the right question in the right way: not "explain OOP" but "explain OOP *using this project*". Textbook OOP is taught with `Dog extends Animal`, which teaches you nothing about why large systems are shaped the way they are. Your Tool_Box is small enough to hold in your head and real enough to contain every concept on your list: dependency injection, inversion of control, extension methods, reflection. This lecture walks from philosophy down to mechanics, and every claim points at a file you wrote.

---

## 1. What problem is OOP actually solving?

Not modeling animals. At scale, OOP solves exactly one problem: **change amplification**. In a badly structured system, changing one thing forces changes in ten others; the cost of change grows with system size until the system freezes. Every OOP concept worth knowing is a tool for making change *local*:

- **Encapsulation** — the right to change your insides without asking anyone.
- **Abstraction** — depending on *what* something does, never *how*.
- **Polymorphism** — swapping the *how* without touching the callers.
- **Inheritance** — historically oversold; you'll notice this codebase contains essentially none, and that's deliberate (§10).

Here's the measurement from your own project: plan 002 added an entire network transport and container deployment with **zero diffs** in `ToolSets/` and `Core/`. That number *is* OOP working. Everything below explains the machinery that produced it.

---

## 2. The vocabulary, precisely

A **class** is a blueprint; an **object** is one thing built from it. Fine. The distinction that actually matters in system design is between:

- **Object composition** — who *holds* whom (`BasicsTools` holds a `ServerInfoProvider`).
- **The object graph** — the whole web of who-holds-whom at runtime. Your server's graph:

```
McpServer (SDK's)
   └── BasicsTools
         ├── ServerInfoProvider
         │      ├── TimeProvider.System
         │      └── ToolsetDescriptor("Basics")
         └── TimeProvider.System        ← same single instance, shared
```

The central question of large-scale OOP is not "what classes do I have?" but **"who constructs this graph, and who decides its shape?"** Hold that question; sections 4–6 answer it.

---

## 3. Coupling, and why `new` is glue

Imagine `BasicsTools` written the naive way:

```csharp
public string GetServerInfo()
{
    var provider = new ServerInfoProvider(TimeProvider.System, /* ...what goes here? */);
    ...
}
```

Three disasters hide in that one `new`:

1. **Hard coupling.** `BasicsTools` now knows the concrete type, its constructor signature, and its dependencies' dependencies. Change `ServerInfoProvider`'s constructor → every `new` site breaks. `new` is glue: every use bonds you to a concrete choice.
2. **Broken semantics.** `ServerInfoProvider` captures its start time at construction. A fresh one per call would report an uptime of zero microseconds, forever. Some objects are *meaningfully shared* — creating them freely isn't wasteful, it's **wrong**.
3. **Untestable.** The real `TimeProvider.System` is baked in; no test can control the clock.

Your actual code instead *declares* its needs and constructs nothing:

```csharp
public BasicsTools(ServerInfoProvider serverInfo, TimeProvider clock) { ... }
```

This is the single most important OOP habit: **classes declare dependencies; they do not acquire them.** Everything called "DI" is scaffolding to honor that declaration.

---

## 4. Inversion of Control — the big idea DI belongs to

Traditional program: *your* code sits in `main`, drives the flow, and calls libraries when it wants help. Inverted program: a *framework* owns the flow and calls **you** at the right moments. The nickname is the Hollywood Principle: *don't call us, we'll call you.*

Your Host is inverted twice over, and seeing both makes the concept click:

| Inversion | Who took control | What they call of yours |
|---|---|---|
| The hosting framework | `builder.Build().RunAsync()` — after this line, *the generic host owns the process*: lifetime, shutdown signals, the message loop | nothing directly; it runs the machinery |
| The MCP SDK | receives `tools/call` from the wire | **your `Ping()` method** — you never wrote a dispatcher, a parser, or a call site for your own tool |

Notice you never call `Ping()` anywhere in the codebase. Grep it: the only caller is a framework you handed control to, plus your tests. That's IoC. **Dependency injection is one specific form of IoC** — inverting control over *object construction* — the same way the SDK inverts control over *dispatch*. Framework callbacks, event handlers, LangGraph invoking your pipeline functions in LLM_Monitor: all the same principle wearing different clothes.

Why invert? Because the framework's flow logic (protocol parsing, lifetime, signal handling) is generic and hardened, and yours would be bespoke and buggy. You trade control for correctness and keep only the parts that are actually yours: the tools.

---

## 5. The DI container: a staffing agency in three acts

Personified: the container is a **staffing agency**. You give it a hiring policy, it builds a roster, and when a job comes in it assembles the right team — including the team's teams.

**Act 1 — Registration (writing the hiring policy).** `IServiceCollection` is nothing but a list of job descriptions. Every `Add*` line in your composition says three things: the *position* (service type), the *candidate* (implementation), and the *contract length* (lifetime):

```csharp
services.TryAddSingleton(TimeProvider.System);        // position: TimeProvider; candidate: this exact instance
services.TryAddSingleton<ServerInfoProvider>();       // position = candidate; build on demand
services.AddSingleton(new ToolsetDescriptor(...));    // pre-built candidate
```

Nothing is constructed yet. It's a recipe book, not a kitchen.

**Act 2 — Build.** `builder.Build()` turns the policy into a `ServiceProvider` — the agency opens for business, recipe book indexed and validated.

**Act 3 — Resolution (the interesting one).** A `tools/call` for `ping` arrives. The SDK asks the agency for a `BasicsTools`. The agency:

1. Reads `BasicsTools`'s constructor **via reflection** (§8): "needs a `ServerInfoProvider` and a `TimeProvider`."
2. Recursively resolves each: `ServerInfoProvider` is a singleton — built already? Hand it over; else construct it first (which recursively demands *its* constructor's needs: the `TimeProvider` and every registered `ToolsetDescriptor`).
3. Invokes the constructor with the assembled arguments.

The graph in §2 was never written down anywhere — **it's computed from constructor signatures**. Add a dependency to a constructor and the graph reshapes itself. That's why DI scales: N classes need N declarations, not N² wiring statements.

---

## 6. Lifetimes — the part everyone gets wrong once

| Lifetime | The agency's contract | In your codebase |
|---|---|---|
| **Singleton** | one instance, forever, shared by all | `ServerInfoProvider`, `TimeProvider`, `ToolsetDescriptor`s |
| **Scoped** | one instance *per scope* (in ASP.NET: per HTTP request) | none yet — arrives when a toolset holds per-request state (e.g., a DbContext) |
| **Transient** | fresh instance every time anyone asks | tool classes are effectively per-invocation |

Two truths worth engraving:

1. **Lifetime is a semantic decision disguised as a performance one.** `ServerInfoProvider` is a singleton *because uptime means time-since-start* — make it transient and uptime is always zero. The bug wouldn't throw; it would just quietly lie. When choosing a lifetime, ask "what does sharing *mean* for this object?", not "is construction expensive?"
2. **The captive dependency trap:** a singleton that injects a scoped service *captures* it — the "per-request" object secretly lives forever, leaking one request's state into all others. Rule: **never inject a shorter-lived service into a longer-lived one.** (.NET validates this in dev builds; understand *why* rather than relying on the guardrail.)

And the small print that pays off in tests: `TryAddSingleton` means "register unless someone already has" — which is precisely what lets a test pre-register a `TestClock` that *wins* over `TimeProvider.System`. Registration order became a seam.

**Multiple registrations** are the other underrated feature: every toolset adds its own `ToolsetDescriptor`; `ServerInfoProvider` asks for `IEnumerable<ToolsetDescriptor>` and receives *all of them*. You needed a registry of loaded toolsets — the container already *was* one. No registry class, no mutation, no locking.

---

## 7. Extension methods — syntax sugar with architectural consequences

Demystified first: an extension method is **just a static method** with `this` on its first parameter:

```csharp
public static IServiceCollection AddToolBoxCore(this IServiceCollection services) { ... }
// caller writes:            services.AddToolBoxCore()
// compiler actually emits:  ServiceCollectionExtensions.AddToolBoxCore(services)
```

No inheritance, no runtime magic, no access to private state, resolved entirely at **compile time** (they are *not* polymorphic — a `virtual`-looking call that can never be overridden). So why does all of .NET composition — and your codebase — lean on them?

1. **You can extend types you don't own.** `IServiceCollection` belongs to Microsoft; `AddBasicsToolset` belongs to you. Extension methods let your vocabulary attach to their nouns.
2. **They make composition read as language.** Your `Program.cs` is the demonstration: `AddMcpServer().WithStdioServerTransport()` — each method returns the thing it extended (or a builder), so calls chain into a **fluent** sentence describing the system.
3. **They encapsulate registration knowledge.** `AddBasicsToolset()` is the *only* public doorway to that toolset; the Host composes capabilities without knowing a single type inside them. Each toolset's wiring complexity is its own business — encapsulation applied to *configuration*, not just data.

The convention you've now built twice (`AddToolBoxCore`, `AddBasicsToolset`, `UseStderrOnly`, `AddToolBoxServer`) is the idiomatic .NET pattern for exactly this: **a static entry point that teaches the container about a subsystem.**

---

## 8. Reflection and attributes — the code that reads code

Reflection is .NET asking, at runtime, "what shape is this type?" — constructors, methods, parameters, and the metadata stapled to them. Your project uses it three ways, escalating in interest:

1. **The DI container** (§5) reads constructor signatures to compute the object graph. You've been using reflection all along without writing any.
2. **The MCP SDK** reflects over `[McpServerToolType]` classes, finds `[McpServerTool]` methods, reads each parameter's type and `[Description]`, and *generates a JSON Schema* — the tool catalog the model receives. Read that twice: **your C# method signature is compiled into a prompt.** Attributes are inert metadata — stickers on the code that do nothing until some reader (the SDK) gives them meaning. This style is called *declarative*: you state facts (`[Description("...")]`), a framework derives behavior.
3. **Your own `DescriptionConventionTests`** — the most instructive one, because *you're* the reflector: enumerate `BasicsTools` methods, find tool attributes, assert every tool and parameter carries a non-empty description. This is a **convention test**: architecture rules ("descriptions are prompts") encoded as executable checks instead of review comments. Senior engineers reach for this pattern constantly; you already have one in your suite.

Cost worth knowing: reflection trades compile-time certainty and speed for runtime flexibility. Frameworks mitigate it (the container caches its plans; source generators move work to compile time), but the principle stands — reflection at startup and in tests: fine; reflection in a hot loop: smell.

---

## 9. The composition root — one place to know everything

The pattern name for what `Program.cs` (plus `ToolBoxServerComposition`) is: the **composition root**. The rule: exactly one place in the application knows how the object graph is assembled; everywhere else merely *declares needs*.

The anti-pattern it defends against is **service location**: classes reaching into the container themselves (`provider.GetService<ServerInfoProvider>()`). It looks similar — the container's still involved — but inverts the honesty: a constructor is a public, compiler-checked *list of needs*; a service-locator call is a hidden dependency buried in a method body, invisible until it fails at runtime. Constructor injection makes dependency count *painful on purpose* — a constructor demanding seven services is the design smell announcing itself.

The macro-consequence, visible in your repo's dependency arrows:

```
Host ──► Toolsets ──► Core          arrows point one way:
  │                     ▲            things that change often depend on
  └─────────────────────┘            things that change rarely — never the reverse
```

`Core` knows nothing of toolsets; toolsets know nothing of transports; only the root knows all. This is the small-scale version of what architecture texts call *hexagonal / ports-and-adapters*: a stable core, volatile adapters (stdio, HTTP) at the edges, and the dependency arrows enforcing which side absorbs change. Plan 002's zero-diff result was those arrows doing their job.

---

## 10. Where's the inheritance?

Almost nowhere — one abstract class (`TimeProvider`, subclassed once, in a *test*) and zero inheritance hierarchies of your own. This is modern .NET style, and the principle is **composition over inheritance**: inheritance welds you to a parent's implementation at compile time (the strongest coupling that exists); composition lets you assemble behavior from parts at runtime. `BasicsTools` doesn't *inherit* server-info capability — it *holds* a provider. When you need a variant, you swap the part (TestClock), not re-parent the class.

Where inheritance-like flexibility is genuinely needed, .NET reaches for **interfaces and abstract seams** (`IMcpServerBuilder`, `TimeProvider`) — contracts to stand behind, not implementations to be welded to. Rule of thumb: inherit to *be substitutable*, never to *reuse code*.

---

## 11. One request, every concept at once

The full trace of `ping`, annotated with this lecture's vocabulary:

```
tools/call "ping" arrives on the wire
  → SDK (IoC: the framework calls you)
    → asks the ServiceProvider for BasicsTools          (composition root's graph)
      → reflection reads the constructor                 (§8, use 1)
        → ServerInfoProvider: singleton, already built   (lifetime semantics, §6)
        → TimeProvider: the shared System instance       (the testability seam, §3)
      → constructor injection assembles the object       (declared, not acquired)
  → SDK invokes Ping("hello")                            (found via [McpServerTool] reflection, §8 use 2)
    → OutputLimiter.Limit(...)                           (static: pure function, no state → no injection needed)
  → return value serialized against the reflected schema → wire
```

Ten seconds of runtime; six concepts. When any of these terms comes up in an interview, this trace is your worked example.

---

## 12. Common mistakes (the field guide)

1. **`new`-ing a dependency inside a class** — glue, semantics bugs, untestability (§3). `new` is fine for *data* (a `ToolsetDescriptor`, a result record); it's glue for *services*.
2. **Captive dependency** — scoped-inside-singleton (§6).
3. **Service locator** — hiding needs from the constructor (§9).
4. **Lifetime chosen for performance, not meaning** — the zero-uptime bug that never throws (§6).
5. **Static mutable state as a shortcut** — the untestable ambient world; `TimeProvider` exists because `DateTime.UtcNow` is exactly this mistake, institutionalized.
6. **Inheritance for code reuse** — welding where you needed a part (§10).
7. **God constructors** — seven dependencies isn't a DI problem, it's a "this class has three jobs" problem the DI made visible. Don't shoot the messenger.
8. **Extension-method sprawl** — they're for composition vocabulary and extending types you don't own, not a place to hide business logic (no state, no polymorphism, hard to fake in tests).

## 13. Interview relevance

This lecture's material maps directly onto the questions .NET backend interviews actually ask: DI lifetimes and the captive-dependency trap (a favorite), constructor injection vs service locator and *why*, IoC as a principle broader than DI, how attribute-driven frameworks work under the hood (reflection → schema/dispatch), composition-over-inheritance with a reason rather than a slogan, and the composition-root/hexagonal shape of a service. Your differentiator is that every answer can end with "…and in my MCP platform, this is the file where that shows up."

## 14. Exercises against your own repo (do these; reading isn't knowing)

1. **Break a lifetime on purpose:** change `ServerInfoProvider` to transient, run the integration tests, watch `Uptime` lie without anything throwing. Revert. (Now you *own* §6.)
2. **Trace the reflection:** put a breakpoint in `BasicsTools`'s constructor, run one Inspector `ping`, walk the call stack upward and identify the container's frames.
3. **Write a convention test** that fails if any toolset class forgets `[McpServerToolType]` — you'll reuse this pattern for real in plan 003.
4. **Add a fake second toolset** (`ToolBox.Scratch`, one silly tool) end to end: project, extension method, one composition line. Time yourself — the speed *is* the architecture. Delete it after.
5. **Find the IoC in LLM_Monitor:** identify who calls your pipeline functions in the LangGraph service. Same principle, Python clothes.
