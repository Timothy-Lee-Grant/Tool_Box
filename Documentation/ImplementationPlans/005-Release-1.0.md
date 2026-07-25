2026_07_23_23_33-(Release-1.0)

# Implementation Plan 005 — Release 1.0

This plan follows the same staged shape as plans 001-004, but a different purpose: those plans built capability (a transport, a toolset), this one prepares what already exists — Host/Core, Basics, Voxel, two transports, Docker, 77 tests, 11 ADRs — to be *released*: checked, polished, packaged, and shown.

Per CLAUDE.md, this document has three parts instead of the usual free-form Stage 1: a **Release Checklist**, a **Packaging Plan**, and a **Presentation** outline. Stage 2 discussion below is where each of these gets argued into something concrete before Stage 3 turns it into a step-by-step plan.

---

# Stage 1 (Design Documentation)

*Timothy's goal, as of 2026-07-23:* get Tool_Box to a genuine "1.0" — not a new toolset, but the point where a stranger can find it, install it, run it, and understand what it demonstrates without Timothy in the room. This is the release this project has been implicitly building toward since plan 001's "professional base" framing.

## What "done" already looks like (carried over from the repo as it stands today, not aspirational)

- Host/Core/Toolset architecture holding across two toolsets with zero `Core` diffs (ADR-003, ADR-010).
- Two transports, one binary (ADR-007): stdio (Claude Desktop/Code) and streamable HTTP (containerized/remote).
- 2 toolsets, 15 tools, documented in `docs/TOOL_CATALOG.md`.
- 77 tests across four projects; CI builds, tests, and boots the Docker image with a real healthcheck on every push.
- 11 ADRs, including one (ADR-011) that revises an earlier decision on the record.
- A live browser viewer (WebSocket broadcast) demonstrating the Voxel toolset visually.
- README already written to portfolio quality, with a "what this demonstrates" table aimed explicitly at a technical reviewer.

What 1.0 adds is not new engineering — it's the packaging, verification, and narrative layer on top of what's real.

---

## Release Checklist

Organized as a checklist-of-checklists; Stage 3 will convert whichever items survive Stage 2 discussion into ordered, permissioned steps.

### Correctness / hygiene
- [ ] `dotnet build` and `dotnet test` clean from a fresh clone (no local-machine state leaking into the "it works" claim).
- [ ] `docker compose up --build` clean from a fresh clone, Inspector round-trip against `/mcp` confirmed.
- [ ] Re-verify the stdout-purity rule (ADR-004) still holds — `dotnet run --project src/ToolBox.Host 2>/dev/null` prints nothing — now that two toolsets exist, not just Basics.
- [ ] Re-run the Claude Desktop and Claude Code quickstart steps from the README verbatim, on a clean config, to confirm the docs are still accurate (not "should still work").
- [ ] Confirm `docs/TOOL_CATALOG.md` lists exactly the 15 tools that exist in code — a drift check, not a rewrite.

### Documentation completeness
- [ ] Every shipped plan (001-004) has its Stage 5 (verification) filled in, or an honest note on why not.
- [ ] `docs/DECISIONS.md` reviewed end-to-end for internal consistency (ADR-011 already models "supersede, don't edit" — check nothing since has silently drifted the same way).
- [ ] LICENSE file exists and is chosen deliberately (currently absent — this blocks any public "please reuse this" claim implicit in a portfolio release).
- [ ] CONTRIBUTING/community-health files — decide explicitly whether this is a portfolio piece (skip) or genuinely open for outside contribution (needed), rather than leaving it undecided.

### Versioning and release mechanics
- [ ] Decide a version scheme (SemVer starting at `1.0.0` is the natural default given the name of this plan).
- [ ] First annotated git tag (`v1.0.0`) — none exist yet (`git tag` is currently empty).
- [ ] GitHub Release notes drafted from the plan/ADR history (this project already has the raw material — the staged process logs — so release notes are closer to curation than to writing from scratch).
- [ ] Decide whether CI gains a release job (build+push a tagged image / pack a NuGet package on tag push) as part of this plan, or explicitly defer it — CI today (per `.github/workflows/ci.yml`) builds and smoke-tests on every push but has no publish step.

### Security / posture re-check
- [ ] Re-read ADR-008/ADR-011 against whatever the packaging plan below decides — if 1.0 makes the HTTP transport easier to stand up publicly (e.g. a published Docker image), the "never publish the port beyond a trusted network" rule (ADR-011) needs to be *loud* in the README/release notes, not just in the ADR log.

### Deployment-profile toolset scoping (added 2026-07-25, see Stage 2 §Q2)
- [ ] Confirm — and state explicitly somewhere durable (README or a new ADR) — that today's `AddToolBoxServer()` registers **every** toolset unconditionally, on **both** transports. True today only because neither Basics nor Voxel needs anything a container doesn't have.
- [ ] Before any future hardware-touching toolset (I2C/SPI/GPIO — persona.md's "unfair advantage" domain) is added, land a config-driven toolset allowlist (e.g. `ToolBox:EnabledToolsets`) so the Docker/HTTP deployment path cannot register a toolset that assumes physical hardware it doesn't have and that a network caller has no business reaching anyway. Not needed for 1.0 itself — no such toolset exists yet — but 1.0 is the right place to *write down* that this is a known gap, not a silent one, per this project's usual "documented limitation, not an oversight" pattern (ADR-009 is the precedent).

---

## Packaging Plan

Three distribution shapes were named in persona.md as explicit learning goals for this project ("learn packaging/cross-project consumption: dotnet tool, Docker image, NuGet") — 1.0 is the natural point to decide which of them actually ship, since building capability without ever packaging it would leave that goal unmet.

| Shape | What it means here | Status | Open question for Stage 2 |
|---|---|---|---|
| **Docker image** | Publish the existing multi-stage image (already built and smoke-tested every CI run) to a registry (GHCR is the natural choice — same GitHub identity, no new account) | Closest to done — image already exists and works | Tag/version strategy; does `docker-compose.yml` change to reference the published image instead of `build: .`? |
| **`dotnet tool`** | Package `ToolBox.Host` as a global/local .NET tool (`dotnet tool install`) so a stdio consumer (Claude Desktop/Code) doesn't need to clone the repo and build | Not started | Does a global tool make sense for something that's really a long-running server, or is this better framed as "the way Claude Code/Desktop launches it," i.e. closer to how other MCP servers via `npx`/`uvx` are consumed? |
| **NuGet package** | Publish `ToolBox.Core` (and maybe the toolset interfaces) as a library so a *third* project could build its own Host against this platform's plumbing | Not started, least clearly motivated | Is there an actual consumer for this (unlike LLM_Monitor, which consumes over MCP, not as a referenced library)? If not, this may be the one packaging goal that's honestly "learned by reading the docs" rather than "shipped," and 1.0 should say that plainly rather than half-do it. |

The packaging plan's job in Stage 2 is to turn "three things I said I wanted to learn" into "here's which of these this release actually does, and why" — not to force all three into 1.0 for completeness' sake.

### Docker image — no longer just a learning goal, now an active blocker (added 2026-07-25, see Stage 2 §Q4)

`docs/LLM_MONITOR_INTEGRATION.md`'s "Image strategy" note already named this exact problem in advance: `build: context: ../Tool_Box` in a *consuming* project's compose is fine on one machine with both repos checked out side by side, and breaks the moment that consuming project's own CI runs, because that runner never has a `../Tool_Box` checkout — the note's own words were "when that hurts — CI, other machines — the next rung is... eventually a registry with version tags." That rung is now the one being climbed. Concrete steps, promoted out of "eventually":

1. **Registry: GHCR (`ghcr.io`).** Same GitHub identity as this repo — no new account, and CI authenticates with the built-in `GITHUB_TOKEN` (no new secret to manage).
2. **New CI job** (`.github/workflows/ci.yml`, alongside the existing `docker` smoke-test job): on push to `main` and/or on `v*.*.*` tag push, `docker/login-action` against `ghcr.io`, then build and push `ghcr.io/timothy-lee-grant/tool_box:latest` and a version-pinned tag (`:1.0.0`, matching whatever git tag this release cuts — ties directly to the "First annotated git tag" checklist item above).
3. **Package visibility: public.** Set once in the GHCR package's own Settings on GitHub. Lets any consuming project `docker pull`/`docker compose pull` with zero credentials — the registry-level analog of ADR-008's "isolation, not auth" posture, applied to distribution instead of runtime.
4. **The consuming side (LLM_Monitor's `docker-compose.yml`) changes exactly one thing:**
   ```yaml
   # before
   toolbox:
     build:
       context: ../Tool_Box

   # after
   toolbox:
     image: ghcr.io/timothy-lee-grant/tool_box:1.0.0   # pin a tag, never :latest, for reproducibility
   ```
   Everything else in that service block (`environment: AllowedHosts`, the healthcheck, the deliberate absence of a `ports:` section) is untouched — only *how the image is acquired* changes, not how it's run.
5. **This is what actually unblocks LLM_Monitor's own CI**, since its runner can now `docker compose pull` a real, versioned artifact instead of trying to build a sibling checkout that was never cloned there. Cross-repo checkout via a second `actions/checkout` step was considered and rejected here — it needs its own token/permissions setup, rebuilds Tool_Box from whatever HEAD happens to be on every consuming-project CI run instead of a tested tag, and doesn't scale to a second or third consumer. A registry pull is strictly less machinery for more determinism.
6. **Local dev is a separate, smaller question** (Stage 2 open item): keep `build: ../Tool_Box` for Timothy's own sibling-checkout convenience via a compose override file, or switch fully to the pulled image once one exists — not load-bearing for unblocking CI either way.

---

## Presentation

Timothy makes YouTube videos and wants a demo video for this project, with the video linked from his resume. Outline to argue over/refine in Stage 2 — this is a first pass at structure and content, not a final script.

1. **Cold open — the payoff, not the setup.** Show the castle build (already captured — `docs/images/voxel-viewer-castle-grid.jpg`, `docs/images/voxel_world.gif`) happening live: an agent conversation on one side, the browser viewer building in real time on the other. Establish in one line: "every block placed there is a real MCP tool call."
2. **What MCP is, briefly, for a viewer who's never heard of it.** One diagram: agent ↔ MCP server ↔ tools. Position this project as "the hands" the LLM_Monitor README already uses as its own tagline.
3. **Architecture walkthrough.** Host/Core/Toolset boundary, and *why* it matters — the concrete evidence is that Voxel (stateful, its own background service) slotted in with zero diffs to Core. This is the strongest "systems design judgment" beat for a Microsoft SWE audience and should get real screen time, not a rushed mention.
4. **Two transports, one binary.** stdio locally, streamable HTTP in Docker — show the same tool catalog working identically over both, briefly.
5. **Engineering discipline as content, not a footnote.** This is the differentiator from a typical "I built an MCP server" video: the staged process (design → discussion → plan → permissioned execution → verification), the ADR log, a *real* bug story (the WebSocket close-handshake fix, or the CI-mirror-timeout fix) told as a short "here's what broke and how I found it" beat. Debugging stories are interview currency; this video is a rehearsal for that interview answer.
6. **The honest limitations, said out loud.** ADR-008/011's isolation-not-auth posture, ADR-009's single-world-singleton limitation. Saying these on camera, unprompted, is itself a signal of engineering maturity worth more than pretending v1 has no edges.
7. **Close — what's next and where to look.** Point to the GitHub repo, the ADR log, the Learning docs, and (if plan 004 has moved by then) the SPICE toolset. Resume link callout.

Target length and tone are open Stage 2 questions — a 5-8 minute walkthrough is the working assumption, long enough for the architecture beat to land, short enough that a recruiter actually finishes it.

---

# Stage 2 (Discussion)

Not yet started. Open questions carried out of Stage 1, to open the discussion:

1. Release Checklist: is a LICENSE decision (portfolio-only vs. genuinely reusable) something Timothy already has an answer to, or does it need discussion here?
2. Packaging Plan: which of Docker image / dotnet tool / NuGet actually ship in 1.0 — all three, or an honest subset with the rest named as deferred (mirroring how plan 004 was explicitly deferred rather than half-built)?
3. Versioning: `v1.0.0` tag now, with plan 004 (SPICE) landing later as `v1.1.0`-style follow-on? Or does 004 need to ship first for "1.0" to feel earned?
4. Presentation: video length/tone, and whether it's recorded before or after the packaging work lands (a video showing `docker pull` from a real registry is a stronger demo than one showing `git clone && dotnet build`).

**[2026-07-25, Timothy]** Four mechanics questions, asked before any of the above gets decided — how does the transport bootstrap actually pick stdio vs. HTTP; how do we keep a future hardware-touching toolset from getting registered inside the Docker/HTTP path; what does "streamable" actually mean and does the stdout rule still matter once we're in a container; and is the `build: context: ../Tool_Box` line in the *other* project's compose actually going to work in that project's own CI, and does publishing to a registry fix it.

**[2026-07-25, AI]** All four, answered against the actual code (`src/ToolBox.Host/Program.cs`, `ToolBoxServerComposition.cs`, `ToolBoxHttpApp.cs`), not from memory:

**Q1 — the bootstrap.** `Program.cs` builds a plain `ConfigurationBuilder` *before any host object exists* — this has to happen first because the answer decides which kind of host (`Host.CreateApplicationBuilder` for stdio vs. `WebApplication.CreateBuilder` for HTTP) even gets constructed; you can't build the wrong kind of host and then switch it. Three sources, in precedence order where **the last one added wins**: `appsettings.json` (ships with `"Transport": "stdio"` — the default) → `TOOLBOX_*` environment variables → the `--transport` CLI flag. The resolved string feeds a plain `switch`: `"stdio"` calls `RunStdioAsync` (builds via `Host.CreateApplicationBuilder`, attaches `.WithStdioServerTransport()`); `"http"` calls `ToolBoxHttpApp.Build(args).RunAsync()` (builds via `WebApplication.CreateBuilder`, attaches `.WithHttpTransport()`); anything else fails loudly on stderr with exit code 2 — it never silently guesses a transport. Concretely, this is *why* the same DLL behaves differently depending on how it's launched with zero code branching by the caller: Claude Desktop/Code launches it with no flags or env vars, so it falls through to `appsettings.json`'s `"stdio"` default; the Dockerfile sets `ENV TOOLBOX_TRANSPORT=http`, which outranks that default the moment the container starts. One binary, one config precedence chain, two completely different `IHost` object graphs built from it.

**Q2 — toolset exposure inside the HTTP/Docker path.** The concern is valid, and worth being precise about what's true *today* versus what's a forward-looking gap. Today: `ToolBoxServerComposition.AddToolBoxServer()` is called identically by both `RunStdioAsync` and `ToolBoxHttpApp.Build` — it unconditionally registers **both** Basics and Voxel, on **both** transports, with no filtering. That's not currently unsafe, only because neither existing toolset touches physical hardware — Voxel is a pure in-memory model with a software rasterizer; nothing about it assumes a real device is present. But the concern generalizes correctly to a toolset this project has explicitly discussed wanting (persona.md's embedded/I2C/SPI background, floated in the Brainstorms docs): if a future toolset called real GPIO/I2C hardware, this same unconditional composition would try to register it inside the Docker image too — where the hardware doesn't exist (it'd fail at runtime) and where, worse, a network-reachable HTTP caller would have no business invoking physical-hardware control even if it somehow did exist in that environment. `docs/TOOL_CATALOG.md` already has a one-line note anticipating exactly this ("`server_info.toolsets` reports the toolsets loaded *in this process* — with config-driven loading this becomes deployment-dependent") and ADR-003 explicitly deferred building a dynamic plugin loader "until two real toolsets exist" — that condition is now met, but the trigger for *this specific* piece (a hardware toolset) still hasn't happened, so nothing has been built, consistent with this project's "abstract from evidence, not imagination" habit (same reasoning as ADR-009's singleton-world decision). Recommendation, captured as a Release Checklist item above rather than built now: a config-driven toolset allowlist (`ToolBox:EnabledToolsets` or similar) that makes `AddToolBoxServer()` conditional per toolset, with the Docker image's own `appsettings.json`/environment defaulting to a safe, hardware-free subset. This deserves its own ADR *when it's actually built* — not yet, since building it against zero real hardware toolsets would be exactly the imagined-future-requirement this project's process argues against.

**Q3 — what makes it "streamable," and does the stdout rule still apply in a container?** "Streamable HTTP" is the literal name of the current MCP transport spec (successor to an earlier, separate HTTP+SSE transport) — not a marketing adjective. Mechanically: `.WithHttpTransport()` (from the `ModelContextProtocol.AspNetCore` package) wires MCP's JSON-RPC message handling into the ASP.NET Core pipeline, and `app.MapMcp("/mcp")` maps it to that one route; Kestrel is the real server underneath, same as any other ASP.NET Core app. "Streamable" refers to the transport's ability to keep a single HTTP response open and push multiple JSON-RPC messages back over it as they occur, rather than forcing a rigid one-request-per-message pattern — the mechanism the spec would use for server-initiated messages mid-call (progress notifications, sampling requests). `Stateless = true` (set explicitly in `ToolBoxHttpApp.Build`) turns off the *session-affinity* half of that spec — no `Mcp-Session-Id`, no server-side session store — because none of this platform's tools currently need a multi-turn stateful session at the transport level; each request is handled independently, which is also what makes horizontal scaling trivial later. On stdout: **yes, the rule still holds, and the code already enforces it uniformly** — `ToolBoxHttpApp.Build` calls the identical `builder.Logging.UseStderrOnly()` the stdio path does, per the comment at that call site ("Stage 2 decision 3: the stderr rule stays uniform across transports"). Worth being honest about *why*, though, since the HTTP case's justification is subtly different from the stdio case's: under stdio, writing to stdout is a **correctness bug** — it corrupts the JSON-RPC stream a client is actively parsing (ADR-004). Under HTTP, the protocol rides the TCP/HTTP socket, not stdio pipes, so a stray `Console.WriteLine` wouldn't corrupt anything MCP-related — `docker logs` happily captures both stdout and stderr regardless. The rule is kept anyway for a different, still-legitimate reason: **one invariant that holds unconditionally is easier to enforce with a single test and reason about than a transport-conditional one that could quietly rot on whichever path gets less attention.** Precision matters here for anyone asking "why" in an interview: this isn't "stdout is dangerous in Docker too," it's "we chose uniformity over a narrower rule that would technically be sufficient."

**Q4 — the `build: context: ../Tool_Box` line, and whether a registry fixes it.** The suspicion is correct, and it isn't hypothetical — `docs/LLM_MONITOR_INTEGRATION.md` flagged this exact failure mode in advance, in its own "Image strategy" note: that line works only because both repos happen to be checked out side by side on one machine. The moment *that other project's* own CI runs `docker compose build`/`up`, the runner has checked out only that repository — there is no `../Tool_Box` directory on it, and the build fails, for exactly the reason suspected (GitHub Actions has no access to a local machine's filesystem, and nothing here gives it access to a sibling repo either). A second `actions/checkout` step pointed at `Timothy-Lee-Grant/Tool_Box` could technically work around this, but it's the worse option: it needs its own checkout/token wiring, it rebuilds Tool_Box from whatever that repo's `HEAD` happens to be on every single CI run of the *consuming* project rather than a tested, tagged version, and it doesn't scale past one consumer. **Publishing to a registry is the correct fix, and it's what the Packaging Plan section above now spells out concretely**: push the existing (already-built, already-CI-smoke-tested) image to GHCR under this GitHub identity, make the package public so no credentials are needed to pull it, and change exactly one line in the *other* project's compose file — `build: context: ../Tool_Box` becomes `image: ghcr.io/timothy-lee-grant/tool_box:1.0.0` — with the healthcheck, environment, and no-published-ports posture all untouched. That one-line change is also what unblocks that other project's CI specifically, since pulling a versioned image is something a CI runner can always do, regardless of what else is or isn't checked out.

Net effect of this round: Q2 becomes a new Release Checklist item (not built yet, deliberately); Q4 turns the Packaging Plan's Docker-image row from "closest to done, tag strategy TBD" into a concrete, ordered list of steps, since it's now understood to be blocking real cross-project work rather than being a nice-to-have learning goal.

Stage 3 (step-by-step implementation plan) is not drafted yet — pending Timothy's direction on the open items above (LICENSE, which packaging shapes ship, version-vs-plan-004 sequencing, presentation timing) plus the newly concrete registry-publishing steps.
