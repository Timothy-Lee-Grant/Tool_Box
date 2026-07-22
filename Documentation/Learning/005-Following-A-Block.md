2026_07_22_11_00-(Following-A-Block)

# Lecture 005 — Following a Block: State, Pub/Sub, and the Line Back to the LLM

Lecture 004 explained the Voxel toolset concept-by-concept — sparse grids, closed hierarchies, hosted services, WebSocket handshakes. This lecture takes a different route through the same system: **follow one block, from the moment an LLM decides to place it to the moment a pixel changes on a screen, and then follow the *return trip* back to the LLM** — which turns out to be an almost entirely different, much shorter path. Along the way, this small, single-process system turns out to be a perfect miniature of a much bigger idea your own notes flag as a gap: event-driven, eventually-consistent, distributed systems. Everything here is small enough to run in your head; the concepts scale to systems you'll work on that aren't.

---

## 1. The big picture: two paths, not one

The question "how does the LLM see the world" has a surprising answer: **there are two completely separate paths, and they barely share any code.**

```
                    ┌─────────────────────────────────────────────┐
                    │                  THE AGENT                   │
                    │        (Claude, or any MCP client)           │
                    └───────────────┬───────────────┬─────────────┘
                                    │               ▲
                          "place_box(...)"    "6 blocks. x 0..2,
                                    │           y 0..1, z 0..2."
                                    ▼               │
                    ┌─────────────────────────────────────────────┐
                    │              ToolBox.Voxel                   │
                    │                                               │
   COMMAND PATH →   │  VoxelTools.PlaceBox()                        │
                    │       │                                       │
                    │       ▼                                       │
                    │  VoxelRasterizer.Box()  (shape → coordinates) │
                    │       │                                       │
                    │       ▼                                       │
                    │  VoxelWorld.PlaceBlocks()  (the state)        │
                    │       │                    ▲                  │
                    │       │ Changed event       │ describe_world() │
                    │       ▼                    │  reads the SAME  │
                    │  VoxelViewerBroadcastService│  state directly  │
                    │       │                                       │
                    └───────┼───────────────────────────────────────┘
                            │ WebSocket
                            ▼
                    ┌───────────────────┐
                    │   Browser viewer    │   ← a HUMAN watches here.
                    │   (Three.js scene)   │      The agent never does.
                    └───────────────────┘
```

The **command path** (top-to-bottom on the left) is how a block gets placed and how it reaches the browser. The **query path** (the thin arrow back to the agent on the right) is how the agent finds out what happened — and it never touches the browser, the WebSocket, or anything network-related at all. These two paths only meet at one place: `VoxelWorld`, the single source of truth both of them read from. Understanding this system means understanding that `VoxelWorld` is not "state for the viewer" or "state for the agent" — it's just *state*, and two completely different consumers happen to read it in completely different ways.

---

## 2. Following one block, step by step

Here's the full trip of a single `place_box` call, from the JSON that arrives over the wire to a cube appearing in a browser tab, roughly to scale in terms of what actually executes:

```
1.  Agent decides to call a tool, sends (over stdio or HTTP):
       {"method":"tools/call","params":{"name":"place_box",
        "arguments":{"x1":0,"y1":0,"z1":0,"x2":2,"y2":1,"z2":2,"material":"stone"}}}

2.  The MCP SDK (reflection-driven, Lecture 003 §8) matches "place_box" to
    VoxelTools.PlaceBox, deserializes the JSON into real C# parameters, and
    calls the method — a normal, synchronous C# method call from here on.

3.  VoxelTools.PlaceBox validates (ground rule, material name — §12 of
    Lecture 004), then asks VoxelRasterizer.Box() for the coordinate set.
    This returns a *lazy* iterator (Lecture 004 §4) — nothing has run yet.

4.  VoxelTools materializes it once:
       List<VoxelCoordinate> coords = [.. VoxelRasterizer.Box(...)];
    NOW the triple-nested loop actually runs, in-process, synchronously.
    For a 3×2×3 box: 18 VoxelCoordinate values, computed in microseconds.

5.  VoxelTools calls world.PlaceBlocks(coords, "stone").
    Inside VoxelWorld: 18 dictionary writes, then ONE event fires:
       Changed?.Invoke(new VoxelChange.Placed(placed));

6.  VoxelViewerBroadcastService's OnWorldChanged handler (subscribed back
    in step 0, when the toolset was composed) receives that event
    SYNCHRONOUSLY, on the SAME thread that's still inside PlaceBox's call
    stack. It immediately hands off to an async broadcast and returns:
       private void OnWorldChanged(VoxelChange change) =>
           _ = BroadcastAsync(BuildChangeMessage(change));
    ↑ this is the exact instant the command path and the async, networked
      part of the system separate — everything above this line was one
      ordinary synchronous call stack; everything below is a task running
      independently, which is *why* it's safe to fire-and-forget it
      (Lecture 004 §11) — the tool call does not wait for the network.

7.  VoxelTools.PlaceBox returns "placed 18 stone" — a plain string — back
    through the MCP SDK, back over the wire, to the agent. THE AGENT'S
    TURN IS OVER. It has no idea a broadcast is even happening.

8.  Meanwhile (genuinely concurrently, on a different async continuation):
    BroadcastAsync serializes the change to JSON, takes a snapshot of the
    current WebSocket list under a lock, releases the lock, and sends the
    message to every open socket (Lecture 004 §10).

9.  The browser's WebSocket.onmessage fires; JavaScript adds 18 cube
    meshes to the Three.js scene; requestAnimationFrame renders the next
    frame — typically within a few tens of milliseconds of step 8.
```

Two things worth sitting with: **step 7 happens before step 9, every time, and nothing in this design makes them happen at any particular relative speed.** The agent's tool call can return in microseconds while the browser is still receiving the WebSocket frame; a slow network to the browser never slows down the agent, because they are, from the moment of the `Changed` event, doing completely unrelated work. This is a real, general distributed-systems shape — **decoupling a writer from its downstream consumers so the writer's latency doesn't depend on the consumers' latency** — showing up in a 300-line toolset.

---

## 3. What "state" actually is here, and what it costs to keep it simple

`VoxelWorld`'s entire state is:

```csharp
private readonly Dictionary<VoxelCoordinate, string> _blocks = [];
```

One in-memory dictionary. No file, no database, no disk write, anywhere. This is a real, deliberate engineering choice (ADR-009), and it's worth understanding exactly what you're trading away, because the tradeoff has a name: **durability** — whether data survives past the lifetime of the process holding it.

```
   ┌─────────────────────────────────────────────────────────────┐
   │  What happens if `kill -9` hits the Host process right now?  │
   ├─────────────────────────────────────────────────────────────┤
   │  The dictionary: gone. Every block ever placed: gone.         │
   │  Nothing was written anywhere durable. This is BY DESIGN.     │
   └─────────────────────────────────────────────────────────────┘
```

Compare this to what a *durable* version would need — the two standard tools for making in-memory state survive a crash, both worth knowing by name:

- **Write-ahead logging (WAL).** Before (or as) you mutate the in-memory structure, append a record of the mutation to a log file on disk — `PLACE (0,0,0) stone`, `PLACE (1,0,0) stone`, `CLEAR`, .... On restart, replay the log from the start to rebuild the in-memory state. This is exactly how PostgreSQL, most real databases, and event-sourced systems recover from a crash without losing committed writes. Every `VoxelChange` your event already carries (`Placed`, `Removed`, `Cleared`) is, not coincidentally, *already shaped like a WAL record* — it's a complete, replayable description of one state transition. Turning this toy into a durable system would mean serializing each `VoxelChange` to an append-only file before broadcasting it, and replaying that file on startup instead of starting from an empty dictionary.
- **Snapshotting.** Replaying a WAL from the very first block, forever, gets slower as history grows. Real systems periodically write out the *entire current state* as a snapshot, then only need to replay the (short) log written *since* that snapshot on recovery. `VoxelWorld.Snapshot()` already exists for a different reason (syncing a newly-connected viewer, §5 below) — but notice it's *exactly* the artifact a snapshot-based recovery scheme would use.

**Why this project doesn't do any of that: because nothing in its actual use case needs it.** A demo build that dies with the process is an acceptable loss for a portfolio toolset; it would be an unacceptable loss for, say, a bank ledger. The lesson isn't "always add a WAL" — it's **durability is a deliberate, costed decision, and the right amount of it depends entirely on what happens if the data is gone.** Recognizing when "just keep it in memory" is the *correct* engineering answer (here) versus when it's a landmine (a shopping cart, a game's saved progress, a financial transaction) is a real skill, not a default to memorize.

---

## 4. The query path: how the agent actually "sees" anything

Contrast the elaborate machinery in §2 with how the agent finds out what the world looks like:

```csharp
[McpServerTool(Name = "describe_world")]
public string DescribeWorld()
{
    VoxelBounds? bounds = _world.BoundingBox();
    if (bounds is null)
    {
        return OutputLimiter.Limit("The world is empty.");
    }

    VoxelBounds b = bounds.Value;
    return OutputLimiter.Limit(
        $"{_world.Count} blocks. x {b.MinX}..{b.MaxX}, y {b.MinY}..{b.MaxY}, z {b.MinZ}..{b.MaxZ}.");
}
```

That's it. No event, no WebSocket, no async — `DescribeWorld` just reads `_world` **directly and synchronously**, the instant it's called, and turns whatever it finds into a sentence. The agent's *only* channel for "what does the world look like right now" is this kind of direct read of the live state, formatted as text. There is no image, no vision model call, nothing resembling the browser's experience at all.

This split — some tools *change* the state (`place_box`, `mirror`, `clear`, all returning a short confirmation) and other tools *only read* it (`describe_world`, `world_info`, `list_materials`) — already has a name in real systems architecture: **CQRS, Command Query Responsibility Segregation.** The core idea: the code path that *changes* data and the code path that *reads* data don't have to be the same path, don't have to return the same shape of thing, and — in bigger systems than this one — often don't even hit the same storage (a write goes to a primary database; a read comes from a denormalized, replicated read-model optimized for queries, updated asynchronously from the writes). Here, both paths happen to hit the same `Dictionary` — but the *shape* of the split (mutate-and-confirm vs. read-and-report) is the same pattern at small scale. `docs/TOOL_CATALOG.md`'s **read**/**write** classification for every tool is this same idea, named explicitly, before you'd necessarily have called it CQRS.

---

## 5. The viewer path is a pub/sub system — with a name and a family

Zoom out on §2's steps 6-9 and the roles are exactly the roles in **publish/subscribe messaging**, a pattern that underlies Kafka, RabbitMQ, Redis Pub/Sub, and most real-time systems you'll ever build or maintain:

```
   PUBLISHER                TOPIC / CHANNEL              SUBSCRIBERS
  ┌──────────┐      ┌─────────────────────────┐      ┌───────────────┐
  │VoxelWorld│──────▶  VoxelWorld.Changed       ──────▶ each connected │
  │          │ fires │  (an Action<VoxelChange>  │      │ WebSocket in  │
  └──────────┘       │   event — Lecture 004 §6) │      │ VoxelViewer-  │
                      └─────────────────────────┘      │ Broadcast     │
                                                         │ Service      │
                                                         └───────────────┘
```

`VoxelWorld` is the **publisher** — it doesn't know or care who's listening, or how many subscribers exist (zero, one, ten browser tabs — identical code path). `VoxelWorld.Changed` is the **topic**. `VoxelViewerBroadcastService` plays the **broker** role, fanning one published event out to every current subscriber. Naming this correctly matters because it means everything you already know (or will learn) about pub/sub systems at scale applies directly to reasoning about this one — including where this toy version is weaker than a production message broker, which is the genuinely instructive part.

**Delivery semantics — the question every message system has to answer.** What happens if a send to one subscriber fails mid-broadcast?

```csharp
private async Task<bool> TrySendAsync(WebSocket socket, string message, CancellationToken cancellationToken)
{
    if (socket.State != WebSocketState.Open) return false;
    try
    {
        byte[] bytes = Encoding.UTF8.GetBytes(message);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        return true;
    }
    catch (Exception ex)
    {
        logger.LogDebug(ex, "Dropping a voxel viewer socket that failed to receive a broadcast.");
        return false;
    }
}
```

If the send fails, the message is simply **dropped** for that subscriber — no retry, no queue, no "redeliver later." This is **at-most-once delivery**: a message is delivered zero or one times, never guaranteed, never duplicated. It's the *simplest* point in a well-known three-way tradeoff:

| Delivery guarantee | Meaning | Cost |
|---|---|---|
| **At-most-once** (this system) | Message sent zero or one time; drop-and-forget on any failure | Cheapest; simplest; **can silently lose messages** |
| **At-least-once** | Retry until acknowledged; the same message might arrive twice | Needs a retry mechanism and idempotent consumers (§ below) |
| **Exactly-once** | Arrives once, guaranteed, no more no less | The hardest and most expensive to build correctly at scale; real systems (Kafka with transactions, for instance) approximate it with significant machinery |

Why at-most-once is a perfectly fine choice *here*: a dropped `batch` message means a browser's render is momentarily out of sync with the true world — cosmetically wrong for a few seconds, fully self-healing the moment that viewer reconnects (§ below). Nothing about the actual *state* (the `Dictionary` on the server) is ever at risk — only the human's *view* of it can lag. **The right delivery guarantee always depends on what a lost message actually costs** — a dropped chat notification is at-most-once territory; a dropped financial transaction is not.

**The new-subscriber problem, and how this system already solves it.** A brand-new (or refreshed) browser tab has no history — it's never seen any of the `batch`/`remove`/`clear` messages that already happened. `AcceptSocketAsync` solves this by sending a full **snapshot** the instant a socket connects, before any live diffs:

```csharp
if (!await TrySendAsync(socket, BuildSnapshotMessage(), stoppingToken))
{
    RemoveSocket(socket);
    return;
}
await WaitForCloseAsync(socket, stoppingToken);
```

**"Snapshot, then tail the live stream"** is one of the most common onboarding patterns in real distributed systems, under several names depending on where you meet it: a new database replica gets a full dump, then starts applying the replication log from that point forward; a Kafka consumer group can start a new consumer at `latest` (only new messages) or replay from an offset, but a *fresh* read-model rebuild typically bootstraps from a snapshot and then tails the topic; front-end frameworks "hydrate" a page with an initial state blob, then apply live updates over a socket on top of it. This toolset reinvented that exact shape, at toy scale, because the problem ("how does someone who joins late catch up") is universal the moment more than one consumer can exist.

**Where this toy broker is weaker than a real one — and why that's fine here, but wouldn't always be.** A real message broker (Kafka is the canonical example) keeps a **durable, ordered log** of every message, and any consumer can rejoin and replay from wherever it left off — even hours later. This system keeps nothing: if a viewer is disconnected *during* a build, the missed `batch` messages are gone forever, permanently unrecoverable except by the blunt instrument of "reconnect and get a fresh snapshot of *current* state" (which is fine, because the *current state* is all this particular consumer ever needed — nobody's replaying "the history of how the castle was built," only "what does it look like now"). If a future feature needed *history* — an undo button, a build-timelapse feature, an audit log of who placed what — that snapshot-only recovery would no longer be enough, and this is exactly the point where you'd reach for a real durable log instead of an in-memory event.

---

## 6. The thought experiment: what breaks at two servers?

Everything above assumes **one process**. This is the single biggest reason the design stays simple — and walking through what changes the moment there's a *second* process is the fastest way to build real intuition for distributed systems, one of the areas worth deliberately strengthening. This is a thought experiment — nothing below is built, and it doesn't need to be — but reasoning through it is the actual skill an interviewer is checking for when they ask a "design a real-time collaborative X" question.

Picture two `ToolBox.Host` instances (call them **A** and **B**) behind a load balancer, both handling agent tool calls for what's supposed to be *one* shared voxel world:

```
        Agent 1 ──────► Host A ──┐
                      (VoxelWorld A)   ← two SEPARATE in-memory
                                  │       dictionaries. Nothing links them.
        Agent 2 ──────► Host B ──┘
                      (VoxelWorld B)
```

**Problem 1 — state instantly diverges.** Agent 1's `place_box` mutates *only* `VoxelWorld A`'s dictionary. Host B has never heard of it. The instant there are two processes, "the world" stops being one thing and becomes two independent, silently-diverging copies — exactly the failure mode **eventual consistency** exists to name and manage. There are, broadly, two honest fixes, and naming them is the point of this exercise:

- **Move the state out of the process, into something both instances share** — a real database, or an in-memory store like Redis that both hosts talk to over the network instead of holding their own copy. This trades local speed (a `Dictionary` read is nanoseconds; a network round-trip to shared storage is milliseconds) for a single source of truth. Almost every real multi-instance web service makes exactly this trade for anything that must be consistent across instances.
- **Let each instance keep its own copy, and make them agree with each other** via a **replication or consensus protocol** — the family of algorithms (Raft and Paxos are the two names you'll hear most) that let a set of independent processes agree on a single, ordered sequence of events despite network delays and individual failures. This is dramatically harder to build correctly than it sounds — it's *why* Raft and Paxos are famous, celebrated results in computer science rather than routine engineering — and it's the actual mechanism underneath things like a Kafka partition's leader election, or etcd/ZooKeeper (which many other distributed systems lean on so they don't have to solve consensus themselves).

**Problem 2 — the viewer broadcast doesn't reach everyone either.** A browser connected to Host A's `VoxelViewerBroadcastService` only ever hears about writes that happened to land on Host A. A viewer connected to Host B, watching the "same" world, would silently miss everything Agent 1 did through Host A. This is the *exact* problem a real production message broker (Kafka, Redis Pub/Sub, NATS) is built to solve: **decouple "who received the write" from "who needs to know about it," across process and machine boundaries** — every instance publishes to the shared broker instead of holding its own subscriber list, and every subscriber, wherever it's connected, hears everything regardless of which instance produced it.

**Problem 3 — this is where CAP theorem stops being an abstract slogan.** The CAP theorem says a distributed data system can't simultaneously guarantee **C**onsistency (every read sees the latest write, everywhere), **A**vailability (every request gets *some* response), and **P**artition tolerance (it keeps working when part of the network can't talk to another part) — you can only fully have two of the three the instant a network partition actually happens. Concretely, for the two-`VoxelWorld` picture above: if Host A and Host B briefly can't reach whatever they'd use to stay in sync, do they (a) keep accepting writes locally and risk disagreeing once reconnected (favoring **availability** over consistency), or (b) refuse writes until they can confirm agreement (favoring **consistency** over availability)? There is no third option that dodges the choice — that's the actual content of the theorem, and it's *why* real systems documentation always states, explicitly, which side of that tradeoff they picked (this project's own [`docs/DECISIONS.md`](../../docs/DECISIONS.md) is the same kind of explicit tradeoff-recording, just for different tradeoffs).

None of this is implemented, and it shouldn't be — a single demo world with one active builder has no actual multi-instance requirement, and building Raft support for a toy voxel world would be exactly the kind of solving-a-problem-you-don't-have this project's own ADRs (see ADR-003's "abstract from evidence, not imagination") explicitly argue against. The value of this section is entirely that you can now *recognize* these three problems the instant a real system asks you to scale past one process — which is precisely the gap this lecture set out to close.

---

## 7. Common mistakes (the field guide, continued)

1. **Conflating the command path and the query path.** They are different tools, different code, different guarantees — "does the agent see the viewer" is a query-path question the command path's design has no bearing on at all (§1, §4).
2. **Assuming in-memory state is "just a shortcut" rather than a real, load-bearing decision.** It's the right call when the cost of losing it is low; it's a landmine when it isn't (§3).
3. **Building a durable log before anything needs one.** The WAL/snapshot discussion in §3 is knowledge to have ready, not a checklist to apply reflexively — matching effort to actual requirements is the harder and more valuable skill.
4. **Assuming "pub/sub" always means "Kafka" or some heavyweight broker.** An in-process `event` between two classes in the same `.dll` is pub/sub too, at the smallest possible scale — recognizing the *shape* of the pattern matters more than the specific technology (§5).
5. **Picking a delivery guarantee without asking what a lost/duplicated message actually costs.** At-most-once is fine for a viewer redraw; it would be a real bug for a payment (§5).
6. **Reaching for a full replication/consensus solution before confirming multiple instances are actually needed.** §6 exists to build the intuition for *when* that complexity earns its cost, not to suggest every system should carry it by default.
7. **Treating CAP as a checkbox ("we chose AP") rather than a per-operation, per-failure-mode analysis.** Real systems often make different choices for different pieces of their own data — the theorem applies at the level of one specific read/write path during one specific kind of failure, not as a single global label for an entire system.

---

## 8. Interview relevance

This lecture's material is the one most likely to show up as a **system design** question rather than a "explain this term" question: "design a real-time collaborative whiteboard/document/game state," "how would you scale this WebSocket service to multiple instances," "what does CAP theorem actually mean for this specific feature," "walk me through what happens if this write succeeds but the notification to other clients fails." Every one of those questions is, structurally, "explain sections 5 and 6 of this lecture, but about a system you've never seen before" — the reason to build the vocabulary here (publisher/subscriber/broker, at-most/at-least/exactly-once, WAL/snapshot, CAP, consensus) is that it transfers completely: the *names* generalize even when the *system* doesn't. A strong answer in that kind of interview sounds like: "the naive version has one copy of state and one broadcast list, which works until you need a second instance — then you either centralize the state or replicate it, and you need a real broker instead of an in-process event, and you need to decide which side of CAP you're on for this specific write" — which is, almost verbatim, §6.

---

## 9. Exercises against your own repo

1. **Kill the process mid-build and confirm the durability claim yourself.** Start the Host, place a few blocks, `kill -9` it, restart, call `describe_world`. Confirm it reports empty — you've now *watched* the tradeoff in §3 rather than read about it.
2. **Simulate a message loss.** Temporarily make `TrySendAsync` always return `false` (never actually sending), leave the rest of the system alone, and watch a connected viewer silently stop updating while `describe_world` (the query path) keeps reporting the true, growing state correctly. This is the clearest possible demonstration that the command path's broadcast failing has *zero* effect on the actual state or the agent's own visibility into it.
3. **Sketch (on paper, no code) what `VoxelWorld` would look like backed by Redis instead of a local `Dictionary`**, shared by two `ToolBox.Host` processes. Where would the `Changed` event's job move to? (Hint: something has to fan a Redis-observed change out to *each* process's own locally-connected WebSockets — that something is playing the broker role §5 already named.)
4. **Pick one real system you've used** (a multiplayer game, a shared doc editor, a stock ticker) **and identify its delivery guarantee.** Does a dropped update self-heal (at-most-once is fine), or would a customer notice and complain (you need at-least-once, minimum)? This is the single fastest way to make §5's table stick.
5. **Explain CAP theorem out loud, in one breath, using this project's own words** — "if Host A and Host B can't talk to each other for a second, does a `place_box` on Host A succeed immediately (favoring availability) or wait to confirm Host B agrees (favoring consistency)? You cannot have both guaranteed at once during that gap — that's the theorem, not a design flaw." If that sentence comes out fluently, §6 has done its job.
