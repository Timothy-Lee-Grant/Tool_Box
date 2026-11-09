# Tool_Box — global context

This file is loaded into **every** agent session, so it holds only what is true for every task. Everything task-specific lives in the **GitHub issue** being worked on. Keep this file short; if you are tempted to add something that only matters for one feature, put it in that feature's issue instead.

## Purpose
Timothy Grant's MCP server platform, built to learn what a software engineer at Microsoft needs to know. Learning is the goal, so explain *why*, not just *what*. `persona.md` describes Timothy — read it when a task involves teaching, writing docs for him, or updating it.

## Project map
- `src/ToolBox.Host` — the single binary; stdio or streamable HTTP chosen at startup (ADR-007).
- `src/ToolBox.Core` — shared plumbing. Must never reference a specific toolset (ADR-003).
- `src/ToolSets/ToolBox.<Name>` — one class library per toolset, registered with one `Add<Name>Toolset()` call (ADR-005); may add its own hosted service (ADR-010).
- `tests/ToolBox.<Project>.Tests` — xUnit, one test project per source project.
- `viewer/` — Three.js browser viewer for the Voxel world.
- `docs/ARCHITECTURE.md` — the current system and roadmap, in diagrams. Read it first when a task touches structure.
- `docs/DECISIONS.md` — append-only ADR log. `docs/TOOL_CATALOG.md` — every tool. `docs/AI_WORKFLOW.md` — how we work.
- `Documentation/` — Timothy's learning record. Before writing anything there, read `Documentation/CONVENTIONS.md`.

## Commands
- Build: `dotnet build --configuration Release -warnaserror` (warnings are errors; `Directory.Build.props` pins net10.0).
- Test: `dotnet test --configuration Release`
- Run: `dotnet run --project src/ToolBox.Host` (stdio) · `docker compose up` (HTTP + viewer)

## Branches — non-negotiable
- `main` is linked from Timothy's résumé and must always work. **Never commit or push to `main`, and never open a PR into `main`** unless the issue explicitly says so.
- `next` is the integration branch for the rebuild. PRs target `next`.
- Work branches: `claude/issue-<number>-<short-slug>`.

## How work happens
1. **The issue is the task's memory.** Start every session by reading the whole issue — body *and* every comment. Don't keep task notes anywhere else.
2. **The issue's `stage-*` label says what you're allowed to do** (these are the five stages of Timothy's planning process):
   - `stage-1-design` — Timothy is still writing the goal. Ask clarifying questions only.
   - `stage-2-discussion` — discuss trade-offs and options in comments. No code, no plan yet.
   - `stage-3-plan` — post a numbered, step-by-step plan as a comment (delegate to the `planner` agent). No code until Timothy approves.
   - `stage-4-implement` — implement the approved plan. Do only the steps Timothy approved ("do steps 1–2"), then stop and report.
   - `stage-5-verify` — show evidence it works (CI run, test output, screenshots/logs); use the `reviewer` agent.
   No stage label → treat as `stage-2-discussion`.
3. **End every session with a comment headed `## Session notes`**: what changed, decisions made (and why), open questions, the exact next step, and one line: **System state now:** what works after this session that didn't before. That comment is how the next session (yours or another agent's) picks up the thread.
4. **If you're blocked or something is ambiguous, ask in a comment, add the `needs-timothy` label, and stop.** Don't guess on architecture or anything that's hard to undo.
5. **Let CI do the testing.** Build once before pushing; after that, push and read the CI results (`gh run list` / `gh run view --log-failed`) instead of re-running the full suite yourself — CI time costs no tokens. To check CI, use `gh run list --branch <branch>` and `gh run view <id> --log-failed` (not `gh pr checks`). If CI is still running when you finish, say so in the session notes and stop; don't wait.
6. Code changes go in small PRs to `next` with `Closes #<n>` in the body. Fill in the PR template.
7. A decision with lasting consequences gets an ADR appended to `docs/DECISIONS.md` in the same PR. Assign the ADR number when writing the ADR (next number after the last one in `docs/DECISIONS.md`), never during planning; plans say "new ADR".
8. When a task introduces a concept Timothy hasn't used before, say so in the session notes. Lectures are written by the `lecturer` agent, and only when the issue asks for one.
9. **Show, don't just tell.** Every Stage 3 plan and every PR description contains: **Where this fits** (epic → milestone → this issue; what it depends on and what it unblocks), a **Before → After** mermaid diagram of the part of the system that changes, and a plain-language explanation of each change. If a PR changes structure (projects, runtime components, ports, config keys, startup flow), it updates `docs/ARCHITECTURE.md` in the same PR. If a PR changes user-facing behaviour (settings, ports, commands, outputs), it updates `README.md` in the same PR.
