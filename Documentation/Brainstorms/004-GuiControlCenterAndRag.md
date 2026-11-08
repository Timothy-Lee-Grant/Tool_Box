2026_11_07_23_00-GuiControlCenterAndRag

# Brainstorm 004 — GUI control center + RAG direction

Tracking issue: #12. Status: **speculative proposal for Timothy's review**. No code changes, no ADRs (see §9).

> **Evidence labels.** `[verified]` = checked against vendor docs on 2026-11-07. `[assumption]` = from memory or reasoning, **not** checked. Treat every `[assumption]` as something to verify before building on it.

---

## 1. Vision and framing

Tool_Box becomes a **control center for MCP tools**, like Docker Desktop is a control center for containers.

1. You start the app. A native GUI opens (and a tray icon keeps it alive in the background).
2. The home page lists every toolset with an on/off switch.
3. Each *enabled* toolset gets its own page (left-hand tab) for settings and toolset-specific buttons, e.g. voxel's **View world**.
4. You choose which agents (Claude CLI, GitHub Copilot, …) the tools are registered with.
5. You forget about it. A new `claude` session just has the tools: "build me a castle" works.

**Declarative desired state.** The GUI stores *what you want* (voxel on, RAG off, Claude registered). A **reconciler** makes reality match: start/stop processes, register/unregister with agents. This is the same idea as Kubernetes controllers and Docker Desktop's engine.

### 1.1 Decisions already made (from the issue thread)

| # | Decision | Source |
|---|---|---|
| D1 | "Off" = **unregistered from the agent**; the toolset stays visible in the GUI | comment 22:19 |
| D2 | The **user** toggles, via the GUI; GUI is declarative | comment 22:19 |
| D3 | RAG is a **separate toolset**, not a platform service | comment 22:19 |
| D4 | New sessions seeing changes is fine; live `tools/list_changed` is a bonus | comment 22:35 |
| D5 | Pick the agent in the GUI if feasible, else one-time manual config | comment 22:35 |
| D6 | Support **Claude CLI and GitHub Copilot**, extensible to more agents | comment 22:35 |
| D7 | **Native desktop app** | comment 22:35 |
| D8 | RAG this round = toggle + "coming soon" stub tab, to prove the multi-toolset architecture | comment 22:35 |
| D9 | RAG is **fully local**; ~30 documents; Postgres/pgvector floated as an idea | comment 22:35 |
| D10 | **Cross-platform** (Windows, macOS, Linux) | latest trigger comment |
| D11 | Work lands on `next`; `main` untouched until a reviewed merge | CLAUDE.md |

### 1.2 Glossary

- **Control plane** — the channel the GUI uses to *manage* the Host (enable toolset, read status).
- **Data plane** — the channel agents use to *call tools* (MCP).
- **Reconciler** — loop that compares desired vs actual state and fixes the difference.
- **`tools/list_changed`** — MCP notification telling a client "re-fetch the tool list".
- **Registrar** — adapter that knows how to add/remove Tool_Box in one agent's config.
- **Embedding** — a vector of numbers representing a text chunk's meaning; similar meaning ⇒ nearby vectors.
- **Chunking** — splitting a document into pieces small enough to embed and retrieve.

### 1.3 How this fits existing ADRs

- **ADR-003** — `ToolBox.Core` must never reference a specific toolset. The GUI is a *new project*, not an addition to Core. Core may gain a neutral abstraction (a toolset catalog, from #11) but nothing GUI-specific.
- **ADR-005** — one `Add<Name>Toolset()` per toolset. A catalog seam must wrap these so toolsets can be enabled at runtime rather than hard-wired in composition.
- **ADR-007** — single binary, dual transport. The GUI is a *second* executable (or a mode), it must not break stdio/HTTP.
- **ADR-008/011/012** — HTTP is unauthenticated and relies on network isolation; the viewer port is exposed independently. A desktop app on a user's laptop changes this threat model (see §4).
- **ADR-010** — toolsets may add hosted services. This is how voxel's world keeper and viewer are already modelled.

---

## 2. Option set A — native GUI technology

Criteria: cross-platform (D10), fit with .NET 10 + Core, tray/background, drag-and-drop (needed for RAG), hosting the Three.js viewer, packaging, learning value. All entries are `[assumption]` based on general knowledge unless noted.

| Option | Cross-platform | .NET fit | Tray | Drag-drop | Hosts Three.js viewer | Packaging | Learning value |
|---|---|---|---|---|---|---|---|
| **WPF** | Windows only ✗ | Excellent | Needs 3rd-party/WinForms shim | Good | WebView2 | MSIX/installer | High on Windows, but fails D10 |
| **WinUI 3** | Windows only ✗ | Good | Weak | Good | WebView2 | MSIX | Fails D10 |
| **.NET MAUI** | Win/mac/Android/iOS; **no official Linux** ✗ | Good | Weak (no first-class tray) | OK | BlazorWebView/WebView | Per-platform | Medium; Linux gap |
| **Avalonia UI** | Win/mac/Linux ✓ | Excellent (pure .NET, XAML-like) | Built-in `TrayIcon` | Good | `WebView` via add-on (maturity varies) or open system browser | Per-platform installers | High; XAML/MVVM transfers to WPF, which Microsoft shops use |
| **Electron** | ✓ | Poor (Node/JS shell; Host stays .NET) | Good | Good | Native (it *is* Chromium) | Large (~100 MB+) | Medium; web skills, less .NET |
| **Tauri** | ✓ | Poor (Rust shell + web UI) | Good | Good | System webview | Small | Medium; adds Rust |
| **Blazor Hybrid in a desktop shell** (e.g. Photino) | ✓ | Good (C# + Razor) | Varies | Good | Native webview | Moderate | Medium-high; reuses web skills in C# |

### Recommendation: **Avalonia**

Reasons: only .NET-native option that satisfies D10 including Linux; has a built-in tray icon; C#/MVVM skills map straight onto WPF, which is the stack most Microsoft desktop work uses; it can reference the same `net10.0` libraries directly.

**Strongest rejected alternative: Blazor Hybrid/Photino or Electron.** They make the viewer and drag-drop trivial (it's already a web page), at the cost of a heavier or less .NET-native shell. If Avalonia's embedded-web-view story proves flaky `[assumption]`, the fallback is simply: **View world** opens the viewer in the default browser (which the issue already allows). That keeps Avalonia viable with no embedded browser at all.

---

## 3. Option set B — GUI-to-Host channel and toolset process model

### 3.1 Control channel (GUI ⇄ Host)

The agents' MCP endpoint (data plane) stays separate from the management channel (control plane). Mixing them would let any MCP client toggle toolsets, which ties into ADR-008/011.

| Option | Survives GUI closing | Complexity | Security | Notes |
|---|---|---|---|---|
| **In-process** (GUI *is* the Host) | ✗ closing GUI kills tools unless it lives in the tray | Lowest | No network surface | Simple; tray-resident app makes "closing" = minimize |
| **Loopback HTTP admin API** (separate port, `127.0.0.1` only) | ✓ | Medium | Needs a token; any local process can hit loopback otherwise | Easy to debug with curl; easy to add a CLI later |
| **Named pipe / Unix socket** | ✓ | Medium | OS-level ACLs (good) | Cross-platform in .NET but two flavours |
| **gRPC** (over loopback or pipe) | ✓ | High | Same as above | Strong typing, streaming status; heavy for ~30 methods |
| **Watched config file** | ✓ | Low | File permissions | No live status, laggy, awkward errors |

**Recommendation: Host runs as a long-lived background service; GUI talks to it over a loopback HTTP admin API with a per-install token.** Reason: reuses ASP.NET already in the Host, works for a headless mode (§6), and a tray-resident GUI can still own the Host's lifetime for the common case. In-process is the cheapest *first* step and a valid stepping stone.

### 3.2 Toolset process model

| Option | What "off" costs | Voxel world state when off | Isolation | Weight |
|---|---|---|---|---|
| **All in-process** | Tools unregistered; hosted services stopped by the reconciler | Held in memory until toolset stops; **lost** unless persisted | None (shared crash domain) | Lightest |
| **Child process per toolset** | Process killed | Lost unless persisted | Good; a crash can't kill the Host | Medium; needs IPC for tool calls |
| **Container per toolset** | Container stopped | Volume-backed if configured | Strong | Requires Docker, which contradicts "Docker Desktop-like, but not needing Docker" |
| **Hybrid** (in-process by default; opt-in child process for heavy toolsets such as RAG/embedding) | Per toolset | Per toolset | Per toolset | Most flexible, most to build |

**Recommendation: all in-process first; hybrid later.** Voxel's world is light (it only stores block positions), and the renderer is a separate window/tab. Process isolation matters most for RAG (large models, long indexing jobs), so that is where a child process would first pay off.

**Persistence question for "off" (see §10 Q3):** if off stops the voxel hosted service, the world must be saved and restored, or off must *only* unregister the tools and leave the world keeper running. Recommended default: *off unregisters tools and stops the service, after snapshotting state to disk*.

### 3.3 Dynamic tool registration

- The MCP spec lets a server emit `tools/list_changed`. The C# SDK should be able to expose a tool collection that is filtered by the current enabled set `[assumption — verify against the pinned ModelContextProtocol package version]`.
- **Claude Code** `[verified]`: HTTP servers can send `list_changed`; in interactive sessions Claude Code re-fetches automatically with no reconnect. In `-p` mode only the tool list is refreshed. So D4's "bonus" is real for Claude CLI.
- **Copilot** `[assumption]`: VS Code's MCP support handles tool-list changes in recent versions; Copilot CLI behaviour unknown. Treat as "new session only" until tested.

**Design consequence:** make the tool list a function of reconciled state (e.g. an `IToolCatalog` that filters), and call `NotifyToolListChanged` after each reconcile. Agents that ignore it still get correct state on the next session. This satisfies D4 with no special casing.

---

## 4. Option set C — agent registration

### 4.1 One fixed HTTP URL vs stdio proxy

| Option | How it works | Pros | Cons |
|---|---|---|---|
| **A. One fixed local HTTP URL** (e.g. `http://127.0.0.1:<port>/mcp`) registered once per agent | Agents connect to the always-running Host | Simple; supports `list_changed`; one process serves many agents at once | Needs the app running; port conflicts; loopback exposure (token or Origin checks advisable) |
| **B. stdio proxy** — agents launch `toolbox-proxy`, which forwards to the running Host | Works with agents that only support stdio | Maximum agent compatibility | Extra binary; one proxy process per session; more moving parts |
| **C. Agent launches full Host over stdio** (today's model) | Current behaviour | Zero GUI needed | No shared state between agents; contradicts the control-center idea |

**Recommendation: A, with B as an optional compatibility shim added only if a target agent needs it.**

### 4.2 `IAgentRegistrar` abstraction

```csharp
interface IAgentRegistrar
{
    string AgentId { get; }                 // "claude-cli", "copilot-vscode"
    Task<AgentStatus> DetectAsync();        // installed? already registered?
    Task RegisterAsync(Uri mcpEndpoint);    // idempotent
    Task UnregisterAsync();                 // idempotent
    bool SupportsLiveUpdate { get; }
}
```

A new agent = one new implementation. The GUI lists registrars, shows detect status, and offers a checkbox per agent (D5). If a registrar can't edit config safely, it falls back to showing the exact snippet to paste (the "one-time manual config" in D5).

### 4.3 Per agent

| | Claude CLI | GitHub Copilot |
|---|---|---|
| Detect | `claude` on PATH; read `~/.claude.json` | VS Code user settings / `mcp.json` present; Copilot CLI on PATH `[assumption]` |
| Register | Shell out to `claude mcp add --transport http toolbox <url> --scope user` `[verified]`; or edit JSON with `"type": "http"` `[verified]` | Edit VS Code `mcp.json` (`"servers": { "toolbox": { "type": "http", "url": … } }`) `[assumption]`; Copilot CLI uses its own config file `[assumption]` |
| Unregister | `claude mcp remove toolbox` `[assumption — command name not checked]` | Remove the JSON entry `[assumption]` |
| Live update | Yes in interactive sessions `[verified]` | Unknown; test |

Gotchas worth recording:
- `[verified]` In Claude JSON config an entry with `url` but no `type` is treated as stdio and skipped. A registrar that writes JSON must set `"type": "http"`.
- `[verified]` Same-named server defined in several scopes: the highest-precedence scope wins, and fields are not merged. Pick one scope (user) and a stable name so a stale project entry doesn't shadow ours.
- Editing another tool's config file is intrusive. Prefer the vendor CLI where one exists; otherwise back up the file first and edit only our own key.

I could **not** reach the Copilot docs in this session (the fetch was denied), so the Copilot column is entirely unverified. Verifying it is the first task before building that registrar.

---

## 5. Option set D — the RAG toolset

### 5.1 This round: the stub

A `ToolBox.Rag` project exists, registers via `AddRagToolset()` (ADR-005), appears on the home page with a toggle, and its tab shows **"Coming soon."** It exposes no real tools yet (or one `rag_status` tool that says "not implemented").

**What the stub proves:** the catalog supports more than one GUI-visible toolset; per-toolset pages are discovered rather than hard-coded; enabling a toolset with no background service works; and the GUI doesn't assume every toolset is voxel.

### 5.2 Vector store at ~30 documents

30 documents ≈ a few thousand chunks. Brute-force cosine similarity over that is milliseconds, so scale won't pick the winner; install burden and learning value will.

| Store | Install burden for a desktop user | Fit at ~30 docs | Learning value | Notes |
|---|---|---|---|---|
| **pgvector (PostgreSQL)** | **High** — user needs a Postgres server (or the app ships/manages one) | Heavy overkill | High — real SQL + vector index skills used in industry | You mentioned Postgres; supported by Npgsql; Docker could host it but a hard Docker dependency is undesirable for a desktop app |
| **SQLite + sqlite-vec** | Very low — a single file, in-process | Ideal | Medium-high | sqlite-vec is young; verify .NET loading story `[assumption]` |
| **LanceDB** | Low | Fine | Medium | .NET bindings immature `[assumption]` |
| **Qdrant** | Medium — separate binary/container | Overkill | Medium | Good API, good for later scale |
| **Chroma** | Medium — Python-centric | Overkill | Low for .NET | |
| **Plain file-backed index** (JSON/binary + in-memory cosine) | None | Fine | Medium — you implement the maths | Great for understanding; no filtering or persistence niceties |

**Honest take:** pgvector is more infrastructure than 30 documents need, and it adds a server to install for a *desktop* app. Its value here is **learning** (and an interview story), not necessity. **Recommended default: SQLite + sqlite-vec (or the plain file index as a zero-dependency first cut), behind an `IVectorStore` interface, with pgvector as a second implementation.** That gives you the Postgres learning without making every user install it, and the interface proves the architecture. This is your call (see §10 Q5).

### 5.3 Local embeddings (D9: fully local)

| Option | Notes |
|---|---|
| **ONNX Runtime + a small sentence-embedding model** (e.g. an all-MiniLM-class model) | In-process, no extra install, ships a model file (tens of MB) `[assumption]`. Best fit for "fully local, desktop". |
| **Ollama** | Needs user to install Ollama and pull a model; easy to swap models; HTTP API. |
| Cloud embedding APIs | Rejected by D9. |

Recommendation: ONNX Runtime by default behind `IEmbeddingProvider`; Ollama as an alternative implementation.

**Chunking:** start with fixed-size windows (~500 tokens, ~50 overlap), splitting on paragraph/heading boundaries for Markdown and on function/class boundaries for code later. Store source path, chunk index and heading so results can cite where text came from.

### 5.4 Supported document types (you asked me to decide)

| Type | Extraction | Priority |
|---|---|---|
| `.txt`, `.md` | Read as UTF-8; Markdown headings guide chunking | **P0** |
| Source code (`.cs`, `.c`, `.h`, `.py`, …) | Read as text; later split on symbols | **P0** (firmware docs example) |
| `.pdf` | Text-layer extraction (e.g. a .NET PDF library such as PdfPig `[assumption]`); no OCR at first | **P1** |
| `.html` | Strip tags to text | **P1** |
| `.docx` | OpenXML SDK text extraction | **P2** |
| Scanned PDFs / images | OCR | Out of scope initially |

### 5.5 Collections and tool surface

- A **collection** is a named set of documents with its own index (e.g. "firmware docs", "recipes").
- Add documents by **file picker** or **drag-and-drop** onto a collection in the RAG tab; dragging a document between collections re-assigns it (re-embedding isn't needed, only the membership changes).
- Agents query *one* collection so results stay on-topic.

Sketch of the MCP tool surface (names to be finalised in the implementing issue):

- `list_collections()` → names, doc counts, descriptions (the description is how an agent decides which to query)
- `search(collection, query, top_k = 5)` → chunks with source path and score
- `get_document_chunk(collection, document, chunk)` → surrounding context

Management (add/remove documents, create collections) stays **GUI-only** at first. Letting an agent index arbitrary local files is a safety question (§6.3).

---

## 6. Features and interaction modes

### 6.1 Feature ideas

| Feature | Value | Cost |
|---|---|---|
| Tray icon, close-to-tray | Core to "run and forget" | Low (Avalonia built-in `[assumption]`) |
| Start with OS | Truly forget about it | Low-medium per OS |
| Per-toolset settings page | Core | Medium |
| **View world** button (voxel) | Core; closing the viewer leaves server running | Low |
| Per-agent register/unregister panel | Core (D5, D6) | Medium |
| Logs and health panel | Debugging the reconciler | Low |
| Tool-call activity feed (which tool, when, from which agent) | Great demo, great for trust | Medium |
| Resource indicators (CPU/RAM per toolset) | Addresses the efficiency concern you raised | Medium |
| Import/export config | Move between machines | Low |
| Port conflict detection and auto-pick | Avoids a common failure | Low |
| Toolset settings schema → auto-generated forms | New toolsets get UI free | Medium-high |
| Per-agent tool allow-list | Safety | Medium |

### 6.2 Interaction modes

1. **GUI-first (primary):** everything above.
2. **Headless:** the same Host with no GUI, controlled by a config file or a small CLI, for servers, CI and Docker (keeps `docker compose up` working).
3. **Agent-configures-itself:** an admin MCP tool that lets an agent enable/disable toolsets.

### 6.3 Safety note on mode 3

An agent that can enable toolsets can widen its own tool access. Recommended: **off by default**, and if offered, gated by a GUI confirmation prompt. The same logic applies to letting an agent add arbitrary file paths to RAG.

### 6.4 Recommended end-to-end architecture

```
 ┌────────────────────────────┐
 │ ToolBox.Desktop (Avalonia) │  tray + home + per-toolset pages
 └──────────┬─────────────────┘
            │ control plane: loopback HTTP admin API + token
 ┌──────────▼───────────────────────────────────────────┐
 │ ToolBox.Host (long-lived)                            │
 │  ┌───────────────┐   ┌────────────────────────────┐  │
 │  │ Reconciler    │──▶│ Toolset catalog (#11)      │  │
 │  │ desired→actual│   │ Voxel · Basics · Rag(stub) │  │
 │  └──────┬────────┘   └────────────────────────────┘  │
 │         │ register/unregister     ▲ filtered tool list│
 │  ┌──────▼────────┐        ┌───────┴─────────┐         │
 │  │IAgentRegistrar│        │ MCP endpoint    │◀── data plane: HTTP MCP
 │  │ Claude/Copilot│        │ (fixed port)    │     from Claude CLI / Copilot
 │  └───────────────┘        └─────────────────┘         │
 └──────────────────────────────────────────────────────┘
        Voxel viewer (separate port) ◀── "View world" opens browser / window
```

**Variations:** (a) GUI-in-process, no admin API (simplest first spike); (b) child-process toolsets (when RAG gets heavy); (c) stdio proxy (if an agent can't do HTTP).

---

## 7. Cleanup and upgrade path

### 7.1 Starting point

- ADR-014: v1 is archived as **tag `v1.0.2` plus a GitHub Release**, and v2 evolves in place. So nothing needs to be copied or deleted to preserve v1; the cleanup is to *reshape* rather than rebuild.
- Work lands on `next`; `main` stays on v1 (your résumé link) until you review and merge. `main` may look very different after that merge, which is intended.
- The earlier archive and branch issues (e.g. #10, merged in PR #18) are assumed complete.

### 7.2 Staged path on `next`

| Stage | Outcome | Depends on |
|---|---|---|
| **S0 — Catalog seam (#11)** | `Core` gets a toolset catalog abstraction; Host builds its tool list from it; enable/disable is a runtime concept | Decide Embedded handling (Q4) |
| **S1 — Reconciler + admin API** | Desired-state file + reconcile loop; loopback admin API with token; `list_changed` emitted | S0 |
| **S2 — GUI shell** | Avalonia app: tray, home page with toggles, per-toolset page discovery, **RAG stub tab**, agent panel with Claude registrar | S1 |
| **S3 — Voxel integration** | Voxel page with **View world**; persistence of world on off/on; viewer lifecycle independent of the GUI | S2 |
| **S4 — Copilot registrar** | Verify docs, implement | S2 |
| **S5 — Real RAG** | `IVectorStore`, `IEmbeddingProvider`, ingestion, collections, drag-drop, tools | S2 |
| **S6 — Packaging** | Installers per OS, start-with-OS, release | S1–S3 |

### 7.3 What happens to existing things

- **`viewer/`** — keep; it becomes the thing "View world" opens. Its port stays separate from the MCP port (ADR-012). Binding to all interfaces (ADR-012) is a poor default on a laptop; recommend a loopback default for desktop mode with an opt-in for container mode.
- **Ports** — introduce a documented port table (MCP, admin, viewer) with conflict detection.
- **`docker-compose.yml`** — keep as the *headless* deployment path (§6.2). It should keep working, and the GUI is not required for it.
- **stdio mode (ADR-007)** — keep, so existing Claude/LLM_Monitor configs don't break during the transition.
- **Embedded toolset** — not composed today. Recommendation: **defer past the first cut**; the RAG stub already proves multi-toolset architecture, and wiring Embedded adds hardware-specific scope.
- **Tests** — each new project gets a test project (`tests/ToolBox.<Project>.Tests`). The reconciler is highly unit-testable (fake clock, fake registrars), which is worth highlighting in a portfolio.

### 7.4 Risks

- **Editing other tools' config files** can break them; mitigate by preferring vendor CLIs and backing up.
- **Cross-platform packaging and tray/autostart** differ per OS; budget time.
- **Avalonia embedded web view maturity** `[assumption]`; fallback is the default browser.
- **Scope creep** — GUI + reconciler + RAG is three projects. The staged path exists to keep each PR small.

---

## 8. What this teaches (portfolio angle)

- Control plane vs data plane; declarative reconcilers (the Kubernetes pattern).
- MCP dynamic tools (`list_changed`) and adapter pattern for multiple agents.
- Cross-platform desktop UI (Avalonia/MVVM, transferable to WPF).
- RAG end to end: chunking, embeddings, vector search, and pgvector if chosen.

---

## 9. ADRs to write later

Not written now (this is a brainstorm). Each goes into the PR that implements it:

1. GUI technology
2. Control channel (admin API shape, auth token)
3. Toolset process model (and what "off" does to state)
4. Vector store and embedding provider
5. Agent registration approach (fixed HTTP URL, registrar abstraction)
6. Revisit ADR-012 viewer binding for desktop mode

---

## 10. Open questions for Timothy

Each says what it blocks.

1. **GUI technology:** is Avalonia OK as the pick (with "View world" opening the default browser as the fallback)? *Blocks S2 and its ADR.*
2. **Where does the Host live?** Long-lived background service the GUI connects to (recommended), or the GUI process itself for the first spike? *Blocks S1.*
3. **What does "off" do to voxel's world?** Snapshot to disk and stop the service (recommended), or leave the world keeper running and only unregister tools? *Blocks S3.*
4. **Embedded toolset:** defer past first cut (recommended), or wire it in before S1? *Blocks S0 scope.*
5. **Vector store:** SQLite + sqlite-vec / file index by default with pgvector as a second implementation (recommended), or pgvector first for the learning value even though it needs a Postgres install? *Blocks S5.*
6. **Embeddings:** ONNX in-process (recommended) vs Ollama as the default? *Blocks S5.*
7. **Agent self-configuration (mode 3):** do you want it at all? If yes, with GUI confirmation? *Blocks the admin tool design in S1.*
8. **Agent registration:** may the app edit agent config files itself (with backup), or should it only run vendor CLIs / show a snippet? *Blocks S2's registrar design.*
9. **Copilot target:** VS Code Copilot, Copilot CLI, or both? I could not verify either config format in this session. *Blocks S4.*
10. **Viewer binding:** OK to default the viewer to loopback in desktop mode (changing ADR-012 for that mode)? *Blocks S3.*
11. **Start-with-OS:** in the first release or later? *Blocks S6.*
12. **Naming and layout:** `ToolBox.Desktop` and `ToolBox.Rag` OK as project names? *Blocks S0/S2.*
