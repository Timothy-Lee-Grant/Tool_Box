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

---

## Packaging Plan

Three distribution shapes were named in persona.md as explicit learning goals for this project ("learn packaging/cross-project consumption: dotnet tool, Docker image, NuGet") — 1.0 is the natural point to decide which of them actually ship, since building capability without ever packaging it would leave that goal unmet.

| Shape | What it means here | Status | Open question for Stage 2 |
|---|---|---|---|
| **Docker image** | Publish the existing multi-stage image (already built and smoke-tested every CI run) to a registry (GHCR is the natural choice — same GitHub identity, no new account) | Closest to done — image already exists and works | Tag/version strategy; does `docker-compose.yml` change to reference the published image instead of `build: .`? |
| **`dotnet tool`** | Package `ToolBox.Host` as a global/local .NET tool (`dotnet tool install`) so a stdio consumer (Claude Desktop/Code) doesn't need to clone the repo and build | Not started | Does a global tool make sense for something that's really a long-running server, or is this better framed as "the way Claude Code/Desktop launches it," i.e. closer to how other MCP servers via `npx`/`uvx` are consumed? |
| **NuGet package** | Publish `ToolBox.Core` (and maybe the toolset interfaces) as a library so a *third* project could build its own Host against this platform's plumbing | Not started, least clearly motivated | Is there an actual consumer for this (unlike LLM_Monitor, which consumes over MCP, not as a referenced library)? If not, this may be the one packaging goal that's honestly "learned by reading the docs" rather than "shipped," and 1.0 should say that plainly rather than half-do it. |

The packaging plan's job in Stage 2 is to turn "three things I said I wanted to learn" into "here's which of these this release actually does, and why" — not to force all three into 1.0 for completeness' sake.

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

Stage 3 (step-by-step implementation plan) is not drafted yet — pending Timothy's direction on the above.
