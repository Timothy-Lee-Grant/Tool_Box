2026_07_21_15_00-(State-Sockets-And-Shapes)

# Lecture 004 — State, Sockets, and Shapes: Everything Behind the Voxel Toolset

Plans 001 and 002 taught you the platform's skeleton — DI, IoC, transports, composition roots. Plan 003 is where the skeleton finally *does* something, and doing something dragged in a genuinely new curriculum: how to represent a 3D world efficiently, how to turn a sphere into cubes, how to model "one of several kinds of event" in a language with no native union type, how a `BackgroundService` actually runs, what a WebSocket handshake really is, and where a lock has to go versus where it doesn't.

This lecture exists for one purpose: **you are about to review this branch before merging it to main, and every concept below is something the diff will ask you to have an opinion about.** Read it in order — it's ordered the way a careful reviewer should actually move through the code: data model first, then the geometry that fills it, then the tools that expose it, then the networking that broadcasts it, then the process/security questions that sit above all of it. Section 19 turns all of it into a literal checklist.

---

## 1. The finished system, one diagram

```
Claude Desktop / Claude Code / an McpClient over HTTP
        │  (stdio or HTTP — the toolset doesn't care which)
        ▼
   ToolBox.Host ── AddVoxelToolset() ──┬── VoxelWorld            (§2: the data model)
                                        │      ▲
                                        │      │ built from
                                        ├── VoxelRasterizer      (§5: shape → cells)
                                        │
                                        ├── VoxelTools           ([McpServerTool]s — §12)
                                        │
                                        └── VoxelViewerBroadcastService : BackgroundService
                                                 │                (§7-11: hosting, sockets, locks)
                                                 ▼
                                     ws://127.0.0.1:809x/voxel/
                                                 ▲
                                     viewer/index.html (Three.js, a browser tab)
```

Five files, five concept clusters. Nothing here required a new *project structure* concept — ADR-003's Host/Core/Toolsets boundary from Lecture 003 held without amendment. What's new is everything *inside* one toolset.

---

## 2. Representing space: why a `Dictionary`, not an array

The naive way to store a 3D world is a 3D array: `string[200,200,200]`. That's 8,000,000 cells for a build that might occupy a few hundred. Your `VoxelWorld` instead uses:

```csharp
private readonly Dictionary<VoxelCoordinate, string> _blocks = [];
```

This is a **sparse** representation: only occupied cells cost memory. A dictionary is the right tool whenever a space is *addressable* (any of billions of coordinates could theoretically be used) but *sparsely occupied* (almost none of them are) — the same reasoning behind sparse matrices, hash-based spatial indexes in game engines, and inverted indexes in search. The trade you're making: O(1) average lookup/insert instead of O(1) *guaranteed* array indexing, in exchange for storage proportional to *usage* instead of *addressable range*.

For this to work, `VoxelCoordinate` has to behave correctly as a dictionary key:

```csharp
public readonly record struct VoxelCoordinate(int X, int Y, int Z);
```

Three words are doing a lot of work here:

- **`record`** — the compiler generates `Equals`, `GetHashCode`, and `ToString` for you, based on the *values* of `X`, `Y`, `Z`. Two `VoxelCoordinate(1,2,3)` instances are equal and hash identically, even though they're different objects in memory.
- **`struct`** — a value type. It lives inline (on the stack, or inline inside the `Dictionary`'s internal array), not as a separate heap allocation reached through a pointer. For a tiny, immutable, frequently-created type like a coordinate, this avoids garbage-collector pressure that a `class` version would create by the thousands during a big `place_sphere` call.
- **`readonly`** — the compiler enforces that no member can mutate `X`/`Y`/`Z` after construction. This isn't just style: a **mutable struct used as a dictionary key is a live bug generator** — if you could do `coord.X = 5` after using `coord` as a key, the dictionary's internal hash bucket would now be wrong for that entry (it filed the object under its *old* hash), and lookups would silently fail. `readonly` makes that class of bug impossible to write.

**What this replaces**, and why it matters for review: imagine `VoxelCoordinate` had been an ordinary mutable `class`:

```csharp
// The version you did NOT write — and should recognize as broken if you see it:
public class VoxelCoordinate
{
    public int X, Y, Z;
}
```

With no `Equals`/`GetHashCode` override, `Dictionary` would use *reference* equality — two coordinates with identical X/Y/Z would be treated as different keys, because they're different objects. `_blocks[new VoxelCoordinate(1,2,3)]` would never find an entry placed with a *different* `new VoxelCoordinate(1,2,3)`. The whole world model would silently never overwrite or find anything. `record struct` sidesteps the entire problem by generating structural equality automatically. When you review a type used as a dictionary/hash-set key anywhere in this codebase or a future one, the first question is always: **does equality mean what I think it means, and did I get it for free or did I have to write it?**

---

## 3. Modeling "one of several kinds" without a union type

Some languages (F#, Rust, Swift) have a built-in **discriminated union** / **sum type**: "this value is *exactly one* of these named shapes, and the compiler forces you to handle every case." C# doesn't have that natively. `VoxelChange` is the pattern that fakes it convincingly:

```csharp
public abstract record VoxelChange
{
    private VoxelChange() { }

    public sealed record Placed(IReadOnlyList<PlacedVoxel> Blocks) : VoxelChange;
    public sealed record Removed(IReadOnlyList<VoxelCoordinate> Coordinates) : VoxelChange;
    public sealed record Cleared : VoxelChange;
}
```

Walk through *why* each keyword is there:

- **`abstract`** — you can never have a bare `VoxelChange`; it must be one of the nested kinds. This is the "sum" in sum type: the set of possible values is the *union* of `Placed`, `Removed`, and `Cleared` — nothing else.
- **`private VoxelChange() { }`** — a private constructor on the *base* type. Normally this would make the class impossible to inherit from anywhere. But C# grants nested types access to their enclosing type's private members — and `Placed`/`Removed`/`Cleared` are nested *inside* `VoxelChange`. So only those three types (and nothing declared outside this file) can ever extend it. This is how you get a **closed hierarchy**: extensible in theory, but only from inside a single guarded location. Nobody in another file can add a fourth kind of `VoxelChange` by accident.
- **`sealed`** on each variant — prevents a *second* level of inheritance (`SpecialPlaced : Placed`), which would break the "exactly these three kinds" guarantee.

Consuming it uses a `switch` **expression** (not statement) with type patterns, in `VoxelViewerBroadcastService`:

```csharp
private static string BuildChangeMessage(VoxelChange change) => change switch
{
    VoxelChange.Placed placed => /* ... */,
    VoxelChange.Removed removed => /* ... */,
    VoxelChange.Cleared => /* ... */,
    _ => throw new NotSupportedException($"Unrecognized {nameof(VoxelChange)} type: {change.GetType()}"),
};
```

Here's the honest limitation worth knowing for review: **C# will not force you to handle every case at compile time** the way F#'s `match` or Rust's `match` would (both refuse to compile if a variant is left unhandled). The `_ => throw ...` branch is your safety net standing in for a compiler guarantee the language doesn't give you. If a fourth `VoxelChange` variant were ever added and this switch weren't updated, the *first* sign would be a runtime exception the next time that new variant fired — not a build failure. **When you review a switch over a closed hierarchy like this, check whether the fallback branch throws (loud, fast failure) rather than silently doing nothing (a bug that ships quietly).** This one throws — correct.

The naive alternative this replaces — worth being able to name because you'll see it in other codebases — is a single class with a "tag" field and every possible field crammed in:

```csharp
// NOT what you wrote — the pattern this avoids:
public class VoxelChange
{
    public string Type;                              // "placed" / "removed" / "cleared"
    public IReadOnlyList<PlacedVoxel>? Blocks;        // only meaningful if Type == "placed"
    public IReadOnlyList<VoxelCoordinate>? Coordinates; // only meaningful if Type == "removed"
}
```

This compiles, but every consumer has to remember which fields are meaningful for which `Type` — nothing stops you from reading `.Coordinates` on a `"placed"` change and getting silent `null`. The closed-hierarchy version makes that mistake *impossible to express*: `VoxelChange.Placed` simply has no `Coordinates` property to misuse.

---

## 4. Iterators, `yield return`, and why you only enumerate once

Every rasterizer method looks like this:

```csharp
public static IEnumerable<VoxelCoordinate> Box(int x1, int y1, int z1, int x2, int y2, int z2, bool hollow)
{
    (int ax, int bx) = (Math.Min(x1, x2), Math.Max(x1, x2));
    // ...
    for (int x = ax; x <= bx; x++)
        for (int y = ay; y <= by; y++)
            for (int z = az; z <= bz; z++)
            {
                // ...
                yield return new VoxelCoordinate(x, y, z);
            }
}
```

`yield return` is not a `return` statement in the normal sense. The compiler rewrites this entire method into a hidden class implementing `IEnumerator<VoxelCoordinate>`, which remembers *where it left off* between calls to `MoveNext()`. The practical consequence: **nothing in this method body runs when you call `VoxelRasterizer.Box(...)`.** Calling it just constructs the state machine. The triple-nested loop only actually executes cell-by-cell, as something *pulls* values out — by calling `.ToList()`, iterating with `foreach`, or spreading it with `[.. ...]`. This is **deferred execution** (a.k.a. laziness), and it's the same mechanism behind all of LINQ.

This matters concretely in `VoxelTools`:

```csharp
List<VoxelCoordinate> coords = [.. VoxelRasterizer.Box(x1, y1, z1, x2, y2, z2, hollow)];
_world.PlaceBlocks(coords, material);
return OutputLimiter.Limit($"placed {coords.Count} {material}");
```

The rasterizer call is spread (`[.. ...]`) into a concrete `List<VoxelCoordinate>` **once**, and that same list is reused both for the count (`coords.Count`) and for the actual placement (`PlaceBlocks(coords, ...)`). If this had instead been written as two separate calls —

```csharp
// NOT what you wrote — a subtle trap:
_world.PlaceBlocks(VoxelRasterizer.Box(...), material);
int count = VoxelRasterizer.Box(...).Count();   // runs the ENTIRE triple loop a second time
```

— the geometry would be computed twice: once to place the blocks, once again just to count them. For a pure function like this rasterizer, that's "only" wasted CPU, not a correctness bug — but the general rule is sharper than that: **if an `IEnumerable` source has any side effect, or is expensive, or (worse) is a one-shot stream that can't be re-read (a network response, a file being read forward-only), enumerating it twice can silently produce wrong results the second time, or throw.** Materializing once into a `List` and reusing that list is the discipline that avoids the whole question. When reviewing any code that calls something returning `IEnumerable<T>` more than once, check whether it's the *same* enumeration reused or the source being re-run.

---

## 5. Rasterization: turning continuous shapes into discrete cells

"Rasterization" is the general term (from computer graphics) for converting a continuous, mathematical shape into a discrete grid of samples — exactly what happens turning a vector image into pixels, and exactly what `VoxelRasterizer.Sphere` does turning a radius into cubes:

```csharp
public static IEnumerable<VoxelCoordinate> Sphere(int cx, int cy, int cz, double r, double ry, double rz, bool hollow)
{
    double effectiveRy = ry > 0 ? ry : r;
    double effectiveRz = rz > 0 ? rz : r;
    int minY = Math.Max(0, Floor(cy - effectiveRy));

    for (int x = Floor(cx - r); x <= Ceil(cx + r); x++)
        for (int y = minY; y <= Ceil(cy + effectiveRy); y++)
            for (int z = Floor(cz - effectiveRz); z <= Ceil(cz + effectiveRz); z++)
            {
                double d = Hypot3((x - cx) / r, (y - cy) / effectiveRy, (z - cz) / effectiveRz);
                if (d > 1.05) continue;
                if (hollow && d < 0.78) continue;
                yield return new VoxelCoordinate(x, y, z);
            }
}
```

The approach: scan every integer cell inside the shape's **bounding box**, and for each one, test whether it's actually inside the shape. The test — normalize each axis offset by its radius, then take the 3D distance — is exactly the implicit equation of an ellipsoid: a point is inside when `(dx/rx)² + (dy/ry)² + (dz/rz)² ≤ 1`. Setting `ry`/`rz` to the same value as `r` collapses the ellipsoid back to a perfect sphere; that's the "0 means use r" convention you saw in `VoxelTools.PlaceSphere`.

The `1.05` and `0.78` are not derived from anything — they're **tuning constants**, ported from a working reference implementation, that decide how forgiving the boundary test is. At `d ≤ 1.0` exactly, a voxelized sphere looks slightly concave/faceted at low resolution (gaps where the mathematically-perfect sphere surface passes *between* cell centers); nudging the outer threshold to `1.05` fills in those gaps so the shape reads as round to the eye. This is a **quality-vs-purity trade you should recognize on sight**: it's not "more correct," it's "looks more like a sphere at the resolution this actually renders at." The textbook alternative here is a proper incremental algorithm — **Bresenham's / the midpoint circle algorithm** — which walks the boundary directly using only integer arithmetic and is the standard answer if you're ever asked "how do you draw a circle without floating point." This code deliberately doesn't use that: at the scale of a voxel build (tens, not millions, of cells), a bounding-box-and-test approach is simpler to read, easier to port faithfully from a proven reference, and fast enough — the more elegant algorithm would be solving a performance problem this code doesn't have.

**The cost that *does* matter, and why it's capped:** a sphere's bounding box is `(2r)³` cells scanned, even though only a fraction end up inside the sphere. Double the radius, and the scan cost goes up **8×** — this is why `VoxelTools.PlaceSphere` (and every other primitive) validates its radius/height against a hard ceiling:

```csharp
private static string? ValidateRange(double value, double min, double max, string paramName) =>
    value < min || value > max ? $"{paramName} must be between {min} and {max} — got {value}." : null;
```

An agent (or a malicious/careless caller) requesting `r: 100000` isn't a correctness bug, it's a **resource-exhaustion risk** — the process would spend real wall-clock time and memory scanning billions of cells for one tool call. Capping the input range is the cheapest possible defense, and it's the same class of thinking as capping upload sizes or query result limits in a web API: **untrusted numeric input isn't just a data-validity question, it's a cost-of-computation question.**

---

## 6. Events, delegates, and keeping `VoxelWorld` ignorant

`VoxelWorld` needs to tell *something* when it changes, so the viewer can broadcast it — but `VoxelWorld` must not know a WebSocket, a viewer, or a browser exists. That would put networking knowledge inside your domain model, exactly the kind of layering violation ADR-003 exists to prevent (Lecture 003, §9's "arrows point one way"). The tool for this is a C# **event**:

```csharp
// VoxelWorld.cs
public event Action<VoxelChange>? Changed;
...
Changed?.Invoke(new VoxelChange.Placed(placed));
```

```csharp
// VoxelViewerBroadcastService.cs
world.Changed += OnWorldChanged;
...
world.Changed -= OnWorldChanged;
```

`Action<VoxelChange>` is just a delegate type — a variable that holds a *reference to a method*, callable like a function. An `event` wraps a delegate field so that outside code can only `+=`/`-=` (subscribe/unsubscribe) to it, never call it directly or replace the whole list of subscribers — `VoxelWorld` alone decides when `Changed` fires. This is the **Observer pattern**: `VoxelWorld` is the subject, `VoxelViewerBroadcastService` is an observer, and `VoxelWorld`'s code contains *zero* references to who's listening or how many observers exist. Delete the entire viewer feature and `VoxelWorld.cs` needs no edits — proof the decoupling actually worked, the same measurement discipline as Lecture 003's "zero diffs in `ToolSets/`" story for plan 002.

Notice the granularity decision, too: one event per *tool call* (`Placed` carrying a whole `IReadOnlyList<PlacedVoxel>`), not one event per individual block. A 2,000-cube sphere fires **one** `Changed` event, not 2,000. This is a deliberate design choice about how "chatty" the observer relationship should be — coarser events mean less overhead per notification but a bigger payload per notification; the right granularity depends entirely on what the observer does with it (here: one WebSocket message per batch, which is exactly what a browser wants — a redraw per meaningful change, not per cube).

---

## 7. `BackgroundService` and the hosted-service lifecycle

`VoxelViewerBroadcastService` is not a tool — it's **infrastructure the toolset brings along**, registered the ordinary ASP.NET Core way:

```csharp
builder.Services.AddHostedService<VoxelViewerBroadcastService>();
```

```csharp
public sealed class VoxelViewerBroadcastService(VoxelWorld world, ILogger<VoxelViewerBroadcastService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) { /* ... */ }
}
```

`BackgroundService` is a base class from `Microsoft.Extensions.Hosting` that implements `IHostedService` for you, reducing the interface down to one method you actually have to write: `ExecuteAsync`. The Generic Host (the same "who owns `main`" IoC concept from Lecture 003 §4) calls `StartAsync()` on every registered hosted service when the application starts — which kicks off `ExecuteAsync` as a background task — and calls `StopAsync()` on shutdown, which cancels `stoppingToken` and waits (with a timeout) for `ExecuteAsync` to actually return. **You never call `ExecuteAsync` yourself** — same Hollywood Principle as the MCP SDK calling `Ping()`. This is why the exact same `AddHostedService<T>()` line works whether `Program.cs` builds a plain `Host.CreateApplicationBuilder()` (stdio path) or a full `WebApplication.CreateBuilder()` (HTTP path): both are, underneath, an `IHost` — ADR-010's whole claim, and the thing Step 4 actually booted both shapes to *prove* rather than assume.

One syntax note worth naming explicitly: `VoxelViewerBroadcastService(VoxelWorld world, ILogger<...> logger)` on the class declaration itself is a **primary constructor** (a C# 12 feature). It's sugar that eliminates the old three-line dance —

```csharp
// The pre-C#12 version of the same thing:
public sealed class VoxelViewerBroadcastService : BackgroundService
{
    private readonly VoxelWorld _world;
    private readonly ILogger<VoxelViewerBroadcastService> _logger;

    public VoxelViewerBroadcastService(VoxelWorld world, ILogger<VoxelViewerBroadcastService> logger)
    {
        _world = world;
        _logger = logger;
    }
}
```

— and the constructor parameters (`world`, `logger`) are simply usable directly, by name, anywhere in the class body, as if they were fields. This is purely dependency-injection plumbing wearing new syntax; nothing about DI's mechanics from Lecture 003 changes.

---

## 8. The specific async gotcha: cancelling an API older than `CancellationToken`

`HttpListener.GetContextAsync()` predates .NET's cooperative cancellation model and has no `CancellationToken` overload — there is no way to *ask* it to stop waiting for a connection. The fix in `ExecuteAsync` is a pattern worth having in your toolkit permanently:

```csharp
using CancellationTokenRegistration registration = stoppingToken.Register(() => _listener.Stop());

try
{
    context = await _listener.GetContextAsync();
}
catch (Exception) when (stoppingToken.IsCancellationRequested)
{
    break; // Expected: the registration above stopped the listener.
}
```

`stoppingToken.Register(callback)` doesn't cancel anything by itself — it says "when this token *is* cancelled, run this callback." The callback calls `_listener.Stop()`, which forcibly aborts the listener's socket — and *that* is what makes the currently-pending `GetContextAsync()` throw. The `catch ... when (stoppingToken.IsCancellationRequested)` is an **exception filter**: it only catches the exception if the token was, in fact, cancelled — meaning a *real*, unrelated `HttpListenerException` (say, from a malformed request) would **not** be silently swallowed by this catch; it would propagate normally. This is the general-purpose recipe for "how do I cancel an operation whose API was written before `CancellationToken` existed": find *something* that, when told to stop, forces the blocking call to fail, register that as the cancellation callback, and filter your catch so you only treat *expected* shutdown as expected.

---

## 9. WebSockets from first principles

Every transport you've studied so far in this project (stdio, streamable HTTP) is fundamentally **request/response**: a message comes in, a message goes out, done. A WebSocket is different — it starts as a normal HTTP request, but the server responds `101 Switching Protocols` instead of a status like `200`, and from that point on, the same TCP connection carries **full-duplex** traffic: either side can send at any time, unprompted, for as long as the connection stays open. That's exactly why it's the right tool for "push world changes to a browser the instant they happen" — HTTP request/response has no way to let the *server* speak first.

A WebSocket connection is a small state machine (`WebSocketState`): `Open` → (either side sends a **Close** message) → `CloseSent`/`CloseReceived` → `Closed`. The subtlety that actually bit this code: **closing is a handshake, not an event.** When one side sends a Close frame, the *other* side is expected to respond with its own Close frame before either party tears down the socket. The first version of `WaitForCloseAsync` detected an incoming Close message and just stopped reading — then `RemoveSocket` disposed the socket without ever sending a reply:

```csharp
// The buggy first version:
if (result.MessageType == WebSocketMessageType.Close)
{
    break;   // ...then RemoveSocket() calls socket.Dispose()
}
```

From the browser's side, this looked like the connection vanishing mid-handshake — precisely the "closed the WebSocket connection without completing the close handshake" error, caught during Step 4's real-socket verification, not by reading the code. The fix completes the handshake before disposing:

```csharp
if (result.MessageType == WebSocketMessageType.Close)
{
    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
    break;
}
```

The lesson generalizes past WebSockets: **any protocol with an explicit close/teardown handshake (TCP's FIN/ACK, TLS's close_notify, WebSocket's Close frames) expects both sides to participate.** Silently vanishing instead of replying is indistinguishable, to the other side, from a crash.

---

## 10. Concurrency: where a lock has to go, and where it doesn't

Two pieces of mutable state live in this feature, guarded completely differently — and the difference is the actual lesson:

| State | Guarded by | Why |
|---|---|---|
| `VoxelWorld._blocks` (a `Dictionary`) | **Nothing** | ADR-009: one MCP session processes tool calls **serially** — there is never a second thread mutating this dictionary while a first one is in progress. No concurrent writers, no lock needed. |
| `VoxelViewerBroadcastService._sockets` (a `List<WebSocket>`) | `Lock _lock` | Genuinely concurrent: the accept loop adds a socket while a broadcast triggered by a tool call is iterating the list on a different async flow, while a closing socket removes itself. Real simultaneous access. |

```csharp
private readonly Lock _lock = new();
...
lock (_lock)
{
    targets = [.. _sockets];
}
```

`System.Threading.Lock` is a newer (.NET 9+) dedicated lock type — previously, C# code locked on a plain `object` (`private readonly object _lock = new();`), and the `lock` keyword compiled down to `Monitor.Enter`/`Monitor.Exit` around it. `Lock` is purpose-built instead of borrowed: faster in the uncontended case, and the compiler specifically recognizes `lock` used on a `Lock`-typed field and emits a more efficient `EnterScope()`/dispose pattern rather than routing through `Monitor`. Functionally, for review purposes, treat it the same as the classic pattern: only one thread may be inside the `lock` block at a time.

The pattern actually worth memorizing is what happens *around* the lock: `BroadcastAsync` takes the lock only long enough to **copy** the current sockets into a fresh local list, then releases the lock immediately and does all the actual (slow, `await`-ing) work — sending over each socket — *outside* it:

```csharp
List<WebSocket> targets;
lock (_lock)
{
    targets = [.. _sockets];     // fast: just copy references
}
// lock is released here — everything below runs without holding it

foreach (WebSocket socket in targets)
{
    if (!await TrySendAsync(socket, message, CancellationToken.None)) { RemoveSocket(socket); }
}
```

**Never hold a lock across an `await`.** Two independent reasons this is a hard rule, not a style preference: (1) an `await` can suspend the current method and resume later on a *different thread* (depending on the synchronization context), and classic monitor-based locks are thread-affine — the thread that resumes might not be the one that acquired the lock, which is undefined behavior at best; (2) even if that weren't true, holding a lock while waiting on network I/O means every *other* thread that wants the lock (including, here, the accept loop trying to add a new socket) blocks for the entire duration of a slow send — turning a lock meant to protect a few list operations into an accidental bottleneck for the whole service. "Snapshot under the lock, work outside it" is the standard shape for exactly this problem.

---

## 11. Fire-and-forget: the `_ = SomethingAsync()` pattern

`VoxelWorld.Changed` is a synchronous event (`Action<VoxelChange>`) — it has no way to `await` anything. But responding to it means sending over a WebSocket, which is inherently asynchronous. The bridge:

```csharp
private void OnWorldChanged(VoxelChange change) => _ = BroadcastAsync(BuildChangeMessage(change));
```

`BroadcastAsync` returns a `Task`; assigning it to the discard `_` explicitly says "I am starting this work and *not* waiting for it, and I acknowledge the compiler's warning about an unawaited task is not a mistake here." This is **fire-and-forget**, and it is usually a code smell worth flagging in review, for two specific reasons: an exception thrown inside a fire-and-forget task normally has nowhere to go (it becomes an "unobserved task exception," which can crash the process on some .NET versions or simply vanish silently on others), and there's no ordering/backpressure guarantee — if `PlaceBlocks` is called twice in quick succession, nothing here guarantees the two broadcasts arrive at the viewer in the order they were fired (in practice they will, since `Changed` fires synchronously within each call before the next one starts — but that ordering is a byproduct of the caller being synchronous, not something `BroadcastAsync` itself guarantees).

Why it's acceptable *here specifically*: `BroadcastAsync` (and everything it calls: `TrySendAsync`) wraps every actual failure point in its own `try`/`catch`, logs, and returns a bool rather than letting anything throw back out. There is no code path where `BroadcastAsync`'s returned `Task` can fault. **The rule for reviewing a fire-and-forget call: don't just check that the discard is intentional — verify the discarded task genuinely cannot throw**, because if it can, the failure becomes invisible.

---

## 12. JSON over the wire: schema generation, one level deeper than Lecture 003

Lecture 003 §8 covered the basic trick — `[McpServerTool]` methods get reflected into a JSON Schema the model reasons over. Plan 003 needed two shapes Basics never exercised: an **array of objects** (`place_tube`'s `path`) and an **enum** (`mirror`'s `axis`). Both were verified by actually generating a tool from the real method and printing the schema, not by assuming the SDK would handle them — here's what came back for `path`:

```json
"path": {
  "type": "array",
  "items": {
    "type": "object",
    "properties": {
      "x": { "type": "integer", "description": "Grid X coordinate." },
      "y": { "type": "integer", "description": "..." },
      "z": { "type": "integer", "description": "..." }
    },
    "required": ["x", "y", "z"]
  }
}
```

...and for the `VoxelAxis` enum parameter:

```json
"axis": { "type": "string", "enum": ["X", "Z"] }
```

Both "just worked" — the SDK's schema generation (built on `Microsoft.Extensions.AI`'s reflection-based JSON Schema exporter) handles nested records and enums the same way it handles primitives, recursively. The practical lesson for you as a reviewer, not just as the implementer: **when a design plan says "I assume the framework supports X," that's a claim, not a fact, until something actually exercises it.** The 003 plan flagged this explicitly as a "watch item" *because* it was a new shape; the resolution wasn't "read the docs and trust them," it was "generate the real schema from the real method and read the actual JSON."

Separately, the wire messages the viewer receives (`BlockWire`, `CoordinateWire`, `SnapshotMessage`, ...) are small private records that exist **only** to control the JSON shape sent to the browser — they are deliberately *not* the same types as the domain model (`VoxelCoordinate`, `PlacedVoxel`):

```csharp
private sealed record BlockWire(int X, int Y, int Z, string Material);
...
return JsonSerializer.Serialize(new SnapshotMessage("snapshot", [.. blocks]), WireOptions);
```

with

```csharp
private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);
```

`JsonSerializerDefaults.Web` sets, among other things, camelCase property naming — so C#'s `X`/`Material` becomes the wire's `x`/`material`, matching what the plain-JavaScript viewer expects. This is a small instance of a real architectural principle: **your wire format and your domain model are allowed to diverge, and often should.** If `VoxelCoordinate` ever grew a field the viewer has no business knowing about, or the wire format needed to change for a new viewer version, neither change would ripple into the other — because they were never the same type to begin with.

---

## 13. New C# syntax, quick reference

Three features appear throughout this codebase that didn't exist in older C#. None of them change semantics you already know — they're purely about writing the same thing with less ceremony. Recognizing them on sight keeps them from slowing down a review.

| Syntax | Example from this codebase | What it means |
|---|---|---|
| **Primary constructors** (C# 12) | `VoxelViewerBroadcastService(VoxelWorld world, ILogger<...> logger) : BackgroundService` | Constructor parameters declared on the class itself; usable directly in the class body as if they were fields. No separate field + assignment needed. |
| **Collection expressions** (C# 12) | `List<VoxelCoordinate> coords = [.. VoxelRasterizer.Box(...)];` / `HashSet<VoxelCoordinate> seen = [];` | `[]` constructs an empty collection of the target type; `[.. x]` *spreads* an existing sequence into a new collection. Replaces `new List<T>()` and `Enumerable.ToList()` in most places. |
| **`record struct`** (C# 10) | `public readonly record struct VoxelCoordinate(int X, int Y, int Z);` | A value-type record: structural equality/hashing (§2) plus stack allocation instead of heap allocation. |

---

## 14. Security review lens: isolation versus authentication

This is the part of the review that isn't about code correctness at all — it's about **what could go wrong if someone other than the intended caller reaches this**. ADR-008 (plan 002) and ADR-011 (this plan) are the record of that thinking; here's the underlying model, spelled out.

A **threat model** starts with one question: *who can reach this, and what can they do once they're there?* Before plan 003, the honest answer for the HTTP transport was "only whoever can reach the port, and once there, only read-only tools" — two independent layers of protection stacked on top of each other (**defense in depth**: no single layer has to be perfect, because a failure in one is still caught by the other). Plan 003 removed the second layer — write tools now exist — which is exactly why ADR-008's own text said authentication should land *before* that happened.

ADR-011's actual argument, worth being able to reproduce yourself in an interview or a real security review: **look at what was actually doing the protecting.** ADR-008's items (1)-(3) — no published port outside a trusted network, a dev-only port mapping, `AllowedHosts` pinned against DNS rebinding — are **deployment-topology controls**. They stop an attacker from ever establishing a TCP connection to the endpoint in the first place, and they don't care whether the tools behind that endpoint are read-only or not. Item (4) ("all tools are read-only") was a **coincidental property** of what had been built so far, not a control anyone had actually engineered — it was never something an attacker would have to defeat, because items (1)-(3) already meant an attacker couldn't get a connection to test it against. Retiring item (4) as a *stated precondition* isn't loosening security; it's correcting which control was doing the actual work, on paper, to match what was always true in practice.

The reviewer's job when reading this kind of decision isn't to just trust the ADR — it's to independently ask: **is the isolation control (no published port) actually still true in every deployment this code will run in?** That's a question about `docker-compose.yml` and the Dockerfile, not about `VoxelTools.cs` — which is exactly why a security review has to look beyond the diff that triggered it.

---

## 15. Architecture Decision Records as a discipline, not just a file

`docs/DECISIONS.md` is a specific, named industry practice — an **Architecture Decision Record** log (the format traces to Michael Nygard's 2011 write-up, and it's now common enough that "write an ADR" is a normal request on a senior engineering team). The rule that makes it valuable rather than just a changelog is right at the top of the file: **append-only; a reversed decision gets a *new* ADR that supersedes the old one, never an edit.**

Why that rule matters, concretely: ADR-011 doesn't rewrite ADR-008 to pretend the "read-only" assumption was never made. It stands next to it, dated, explaining *what changed and why*. Six months from now, someone reading ADR-008 alone would reasonably conclude write tools shouldn't exist over HTTP yet — and then find ADR-011 explaining exactly why that's no longer the constraint, without needing to dig through git blame or guess whether the rule was simply forgotten. **An ADR log is written for the reader who wasn't in the room**, which very much includes future-you. When you review a branch that touches an existing architectural decision, the correct move is never to quietly edit the old ADR — it's to write a new one that references the old one by number, exactly as done here.

---

## 16. The test pyramid, as it actually appeared across seven steps

Four distinct layers of verification were used to build this feature, and each one caught a category of bug the others structurally could not:

```
        ▲  fewer, slower, closer to "does the whole system work"
        │
        │  Manual / browser E2E        real Chrome tab, real screenshots (Steps 5, 7)
        │  ────────────────────────
        │  Wire-level integration      real McpClient, real HTTP, real sockets
        │  (HttpTransportTests +       (HttpTransportTests.cs; throwaway harnesses
        │   throwaway harnesses)        used during Steps 3-5, then deleted)
        │  ────────────────────────
        │  Functional tool tests       VoxelTools methods called directly — no MCP,
        │  (VoxelToolsTests.cs)        no server, no transport (ADR-003's boundary
        │                              paying off exactly as promised)
        │  ────────────────────────
        │  Pure unit tests             VoxelRasterizerTests / VoxelWorldTests —
        │  (fastest, most numerous)    plain math and state, zero framework
        ▼
```

Concretely, three real bugs/risks in this project map one-to-one onto layers that *specifically* could catch them:

- The **array-of-record / enum schema** question (§12) could only be answered by actually generating a schema from the real `[McpServerTool]` method — a pure unit test calling `PlaceTube(...)` directly proves the C# logic is right, but says nothing about whether the SDK can *turn the method into a callable tool at all*. That required the wire-level layer.
- The **WebSocket close-handshake bug** (§9) could only be found with a real `ClientWebSocket` completing a real handshake against a real `HttpListener` — no unit test of `VoxelViewerBroadcastService`'s private methods in isolation would ever open and close an actual socket.
- The **"the numbers don't add up" moment** in §17 below could only surface by actually running a sequence of real tool calls against a live world and checking the real total — a moment worth its own section, next.

**When you review test coverage on a future PR, the question isn't "are there tests" — it's "which layer would actually catch the specific class of bug this change could introduce?"** A schema-generation risk needs a wire-level test; a pure-math risk needs a unit test; a protocol-handshake risk needs a real socket. Testing at the wrong layer gives false confidence.

---

## 17. Debugging story #1 — the close handshake (add this to your interview stories)

Your persona notes already track debugging stories as portfolio material (the Inspector/.NET-runtime story from plan 001, the NU1510/SDK-drift/Dockerfile-case stories from plan 002). This one belongs in the same list, because the shape of the story — *reproduce with a real client, read the exact error text, understand the protocol, fix the two-line root cause* — is exactly what a senior engineer's debugging story sounds like.

**Symptom:** a throwaway `ClientWebSocket` test harness, connecting to the real (not mocked) `VoxelViewerBroadcastService`, threw on the very last line:

```
System.Net.WebSockets.WebSocketException: The remote party closed the WebSocket
connection without completing the close handshake.
```

**Root cause, traced back:** `WaitForCloseAsync` detected the client's Close frame, broke out of its read loop, and returned — after which `RemoveSocket` called `socket.Dispose()`. Disposing a `WebSocket` that has *received* a close request but never *sent* its own acknowledging close frame leaves the handshake half-finished from the server's side. The client is correctly reporting that the server never held up its end.

**Fix:** one `await socket.CloseAsync(...)` call, in direct response to observing the incoming Close message, before the loop breaks.

**The generalizable lesson:** a protocol handshake is a *contract between two parties*, and code that only handles "I noticed the other side wants to close" without also doing "and now I confirm that I'm closing too" will pass every test that doesn't use a real, protocol-conformant peer to check. This is exactly why §16 exists — a mock or an in-process method call would have papered right over this.

---

## 18. Debugging story #2 — when the numbers don't add up

During Step 7's final live verification, a real build sequence was run: `place_box` (169 grass), `place_cylinder` (414 gold), `place_cone` (156 lava), then `mirror`, then `remove_box` (24 removed). Naive arithmetic says the final count should be `169 + 414 + 156 - 24 = 715`. `describe_world` reported **646**.

**The instinct to resist:** assuming this is a bug and starting to hunt for one. **The actual first move:** re-derive what the number *should* be, given what you know the system does, before concluding anything is wrong.

`VoxelWorld.PlaceBlocks` **overwrites** whatever was previously at a coordinate:

```csharp
foreach (VoxelCoordinate coordinate in coordinates)
{
    _blocks[coordinate] = material;   // overwrite, not "add if absent"
    placed.Add(new PlacedVoxel(coordinate, material));
}
```

The grass floor (`place_box`, a single `y = 0` layer) and the gold cylinder (`place_cylinder`, which also occupies `y = 0` near its base) **share coordinates** where they overlap — the cylinder's base ring overwrites some of the grass floor's cells with gold. Each call's own "placed N" count reflects *cells processed in that call*, not "cells newly added to the world that didn't exist before." The world's total (`describe_world`'s 646) reflects the true, deduplicated occupied-cell count *after* all that overwriting — which is naturally smaller than the naive sum of per-call counts whenever shapes overlap in 3D space, exactly as they did here (a cylinder's base sharing a layer with a floor is a completely ordinary thing for real geometry to do).

**The generalizable lesson:** before treating an unexpected number as evidence of a defect, ask what the system's own documented semantics predict, and check whether the "unexpected" result is actually the *correct* consequence of a rule you already know (overwrite-not-add) intersecting with a fact about the specific scenario (these particular shapes physically overlap). Panicking straight into a debugger is slower than five seconds of "wait, do I actually expect these to be additive?"

---

## 19. The review checklist

This is the payoff section — a concrete pass to run before merging, tied back to everything above by section number.

**1. Build and test, cold.**
```
dotnet build            # zero warnings — Directory.Build.props makes any warning fatal
dotnet test              # 77 tests, all four projects
```

**2. The stdout-purity invariant (ADR-004), re-checked, not assumed.**
```
dotnet run --project src/ToolBox.Host 2>/dev/null | wc -c   # must print 0
```
Any new logging anywhere in a toolset is a stdio-corruption risk until proven otherwise — §7 walked through why Voxel's new startup log line is safe (`ILogger`, routed through the same stderr-pinned provider as everything else), but this is exactly the kind of claim to verify, not trust, on every PR that adds logging.

**3. `VoxelCoordinate` / dictionary-key correctness (§2).** Confirm any new type used as a `Dictionary`/`HashSet` key is an immutable `record`/`record struct`, or has deliberate, correct `Equals`/`GetHashCode` overrides. A plain mutable class used this way is a silent correctness bug, not a compile error.

**4. The `VoxelChange` hierarchy (§3).** If it's ever extended, check: is the new variant nested inside `VoxelChange` (so the private-constructor gate still holds)? Does every `switch` over `VoxelChange` get a new arm, and does the fallback still `throw` rather than silently no-op?

**5. Rasterizer changes (§4-5).** Any new shape: does it use `yield return` (deferred, composable) rather than eagerly building a `List` internally? Are radius/height/count parameters validated against a cap *before* the geometry loop runs — not after? Is a magic boundary constant (like `1.05`) commented with *why*, not just left bare?

**6. The event/observer boundary (§6).** Does `VoxelWorld` (or any future domain-state type) still contain **zero** references to networking types? That's the whole point of the `event` — grep for `WebSocket`, `Http`, or similar inside domain files as a five-second sanity check.

**7. Hosted-service additions (§7-8).** Any new `BackgroundService`: does `ExecuteAsync` actually respect `stoppingToken`? If it wraps a blocking/legacy API with no native cancellation support, is there a `Register(...)` callback forcing it to unblock, with a filtered `catch` distinguishing expected shutdown from a real failure?

**8. Anything touching raw sockets (§9).** Does every code path that can observe a `WebSocketMessageType.Close` also call `CloseAsync` in response, before disposing? This is the single highest-value thing to check by hand, because it's exactly the kind of bug no unit test catches.

**9. Locking (§10).** For any new shared mutable collection touched from more than one async flow: is it guarded by a lock? Is the lock ever held across an `await`? (Grep for `lock` blocks and manually check nothing inside one has the `await` keyword.)

**10. Fire-and-forget (§11).** For any `_ = SomeAsync()` pattern: trace every path inside `SomeAsync` and confirm none of them can throw uncaught. If even one can, that's a real bug to fix (add a try/catch), not a style nitpick.

**11. Wire format vs domain model (§12).** Confirm any new networked feature keeps its wire DTOs (things serialized to JSON for an external consumer) separate from its domain types — check for a private nested record block near the serialization code, the same shape as `BlockWire`/`CoordinateWire` here.

**12. Security posture (§14).** For any change to what's reachable over the HTTP transport: re-verify the actual deployment topology (`docker-compose.yml`, the Dockerfile) still keeps the port unpublished outside a trusted network. Don't just trust that an ADR says isolation holds — check the compose file.

**13. ADRs (§15).** Does this PR touch a previously-recorded architectural decision? If so, is there a *new*, dated ADR referencing the old one by number — never a silent edit to the old entry?

**14. Manual end-to-end (§16, §17).** Before merging a feature like this, run it for real: start the actual `ToolBox.Host` binary (not a scratch harness), open the actual `viewer/index.html`, drive it with a real MCP client, and watch it render. Reading the diff is not a substitute for this — the close-handshake bug and the schema-generation risk were both invisible from the diff alone.

---

## 20. Common mistakes (the field guide, continued from Lecture 003)

1. **A mutable class used as a dictionary/hash-set key** — silent lookup failures, not a compile error (§2).
2. **An open-ended "kind" field instead of a closed hierarchy** — lets callers read a field that's meaningless for the actual variant, with no compiler help (§3).
3. **A `switch` over a closed hierarchy with no `default`/`_` arm, or one that silently does nothing** — a future variant gets silently ignored instead of loudly failing (§3).
4. **Enumerating an `IEnumerable` twice when a side effect or expense is involved** — re-runs work, or produces different results on a non-replayable source (§4).
5. **An "elegant algorithm" applied where the input size never justified it** — Bresenham's circle algorithm would be correct *and* overkill here; recognize when "the textbook answer" and "the right answer for this scale" diverge (§5).
6. **No cap on a numeric input that drives a loop's cost** — a correctness-clean feature that's still a resource-exhaustion risk (§5, §14).
7. **A domain type that knows about its transport/network consumer** — breaks the whole point of the event-based decoupling (§6).
8. **A `BackgroundService` that ignores `stoppingToken`** — the host can never shut it down cleanly; it becomes the reason `dotnet run`'s Ctrl+C hangs (§7-8).
9. **Closing a socket without completing its handshake** — a subtle, protocol-level bug invisible to anything except a real peer (§9, and the actual bug in §17).
10. **Holding a lock across an `await`** — a correctness and performance bug at once (§10).
11. **Fire-and-forget on a task that *can* throw** — an invisible failure with no log, no crash, just silence (§11).
12. **Reusing a domain type as a wire DTO** — couples your internal model's shape to an external consumer's expectations, and vice versa (§12).
13. **Treating "isolation" and "authentication" as interchangeable security controls** — they defend against different things, and losing track of which one is actually doing the work is how a real vulnerability slips through (§14).
14. **Editing an old ADR instead of superseding it** — erases the historical record a future reader needs (§15).
15. **Jumping to "this is a bug" before re-deriving what the system's own rules predict** — the numbers-don't-add-up story (§18) is the general antidote.

---

## 21. Interview relevance

This lecture's material answers a different flavor of question than Lecture 003's DI-focused one — expect these from a systems/backend-leaning interview: sparse vs. dense data structures and when each wins; how to model a closed set of variants in a language without native sum types, and the tradeoff versus a tagged struct; deferred execution / iterators and the double-enumeration trap; the Observer pattern and why event-based decoupling matters at architectural boundaries; how a hosted background service fits into an application's lifecycle and shutdown sequence; WebSocket handshake mechanics (including the close handshake specifically — a surprisingly common "have you actually worked with raw sockets" screening question); where a lock genuinely needs to go versus where single-threaded assumptions make one unnecessary, and the "never await inside a lock" rule; and how to reason about a security posture in terms of what control is *actually* doing the protecting rather than what happens to be true today. Every one of these has a two-sentence answer that ends "...and here's the exact bug I found and fixed when I got this wrong the first time" (§17) — a real debugging story beats a memorized definition every time.

---

## 22. Exercises against your own repo

1. **Break the dictionary-key guarantee on purpose.** Temporarily change `VoxelCoordinate` from a `readonly record struct` to a mutable `class` with public fields and no `Equals`/`GetHashCode` override. Run `VoxelWorldTests`. Watch tests that rely on overwrite/lookup semantics fail in confusing ways. Revert. (Now you *own* §2.)
2. **Force the fallback branch.** Add a fourth nested record to `VoxelChange` without updating `VoxelViewerBroadcastService.BuildChangeMessage`'s switch. Trigger it and watch the `NotSupportedException` fire — confirm for yourself that this is a *runtime* safety net, not a compile-time guarantee. Revert.
3. **Reintroduce the close-handshake bug** by commenting out the `CloseAsync` call in `WaitForCloseAsync`. Write a small throwaway `ClientWebSocket` test (or reuse the approach from Step 4) that connects, then closes cleanly, and watch the exact exception reappear. This is the fastest way to make §9 permanent knowledge instead of a story you read once.
4. **Hold a lock across an `await` on purpose.** Move the `await TrySendAsync(...)` call inside `BroadcastAsync`'s `lock` block instead of after it. It will likely still "work" in casual testing — reason through *why* it's still wrong even though nothing visibly breaks (§10), rather than waiting to be burned by it in production.
5. **Trace a `place_sphere` call end to end**, the same way Lecture 003 §11 traced `ping`: from the JSON arguments arriving, through schema-driven deserialization, into `VoxelRasterizer.Sphere`'s deferred iterator, into `VoxelWorld.PlaceBlocks`'s single `Changed` event, into `BroadcastAsync`'s snapshot-under-lock, out over the WebSocket, into the browser's `addBlock` calls. Every concept in this lecture appears somewhere on that path exactly once.
