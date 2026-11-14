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
- `next` is gated by the `next-gate` ruleset: PRs only (no direct pushes, not even for sync), `build-and-test` and `docker` must pass, the branch must be up to date, and PRs touching `.github/CODEOWNERS` paths need Timothy's review.
- **Syncing `main` into `next` happens only through a PR:** `gh pr create --base next --head main`, merged with a merge commit (not squash) so histories stay connected. Never `git push` to `next`. If GitHub says the branch is out of date or the sync needs anything else, `needs-timothy` and stop.

## How work happens
1. **The issue is the task's memory.** Start every session by reading the whole issue — body *and* every comment. Don't keep task notes anywhere else.
2. **The issue's `stage-*` label says what you're allowed to do** (these are the five stages of Timothy's planning process):
   - `stage-1-design` — Timothy is still writing the goal. Ask clarifying questions only.
   - `stage-2-discussion` — discuss trade-offs and options in comments. No code, no plan yet.
   - `stage-3-plan` — post a numbered, step-by-step plan as a comment (delegate to the `planner` agent). No code until Timothy approves.
   - `stage-4-implement` — implement the next approved step (see **Approval and autonomy** below), open one PR, then stop and report.
   - `stage-5-verify` — show evidence it works (CI run, test output, screenshots/logs); use the `reviewer` agent.
   No stage label → treat as `stage-2-discussion`.
3. **End every session with a comment headed `## Session notes`**: what changed, decisions made (and why), open questions, the exact next step, and one line: **System state now:** what works after this session that didn't before. That comment is how the next session (yours or another agent's) picks up the thread.
4. **If you're blocked or something is ambiguous, ask in a comment, add the `needs-timothy` label, and stop.** Don't guess on architecture or anything that's hard to undo.
5. **Let CI do the testing.** Build once before pushing; after that, push and read the CI results (`gh run list` / `gh run view --log-failed`) instead of re-running the full suite yourself — CI time costs no tokens. To check CI, use `gh run list --branch <branch>` and `gh run view <id> --log-failed` (not `gh pr checks`). If CI is still running when you finish, say so in the session notes and stop; don't wait.
6. Code changes go in small PRs to `next`, **one PR per run** (the push helper allows one branch per run), with `Part of #<n>` in the body. `Closes #<n>` does not auto-close issues on `next` (GitHub honours it only on the default branch), so when the work is finished the agent writes **Ready to close** in its session notes and Timothy closes the issue at milestone acceptance. Fill in the PR template. Phase 1 sizing is **one PR per issue**: an epic's steps are separate issues or separate `APPROVED` comments, one run each.
7. A decision with lasting consequences gets an ADR appended to `docs/DECISIONS.md` in the same PR. Assign the ADR number when writing the ADR (next number after the last one in `docs/DECISIONS.md`), never during planning; plans say "new ADR".
8. When a task introduces a concept Timothy hasn't used before, say so in the session notes. Lectures are written by the `lecturer` agent, and only when the issue asks for one.
9. **Show, don't just tell.** Every Stage 3 plan and every PR description contains: **Where this fits** (epic → milestone → this issue; what it depends on and what it unblocks), a **Before → After** mermaid diagram of the part of the system that changes, and a plain-language explanation of each change. If a PR changes structure (projects, runtime components, ports, config keys, startup flow), it updates `docs/ARCHITECTURE.md` in the same PR. If a PR changes user-facing behaviour (settings, ports, commands, outputs), it updates `README.md` in the same PR. Manual verification commands must be copy-paste safe for zsh: no inline `#` comments; put expectations in the text, not in the command.

## Approval and autonomy (epic #84)
**What counts as approval:** a comment on the issue whose first line starts with `APPROVED` (the comment is written `@claude APPROVED ...` because runs start on `@claude`), authored by an owner. Verify with `gh issue view <n> --json comments` and `authorAssociation == OWNER`; a label never counts, since labels can be changed by agents. "Not approved", or "approved" in the middle of a sentence, does not count. A new plan comment cancels earlier approvals. Approval covers only the steps it names.

**After approval, do the approved step end to end, without further clicks from Timothy:** implement, push, open the PR into `next` (`gh pr create --base next`), then enable auto-merge, then stop.
- Auto-merge is only ever `gh pr merge --auto --squash <n>`, only when the PR base is `next`. Never merge immediately, never use `--admin`, never enable it on a PR whose base isn't `next`.
- If CI fails on your PR, fix it and push. If your merge turned `next` red, fix or revert it in the same run.
- **Never open a new PR while `next` is red** (`gh run list --branch next --limit 1 --json conclusion`).
- If a PR conflicts with `next`, `git merge origin/next` into your branch and push (CI re-runs). If the conflict touches a high-risk file or the resolution isn't mechanical, `needs-timothy`.
- A step N+1 branches from `next` after step N has merged, never from step N's branch.

**Kill switch:** at the start of every run, if the issue has the label `claude:paused`, comment that you are paused and stop. (The workflow also enforces this, but check anyway.)

### Risk tiers
| Tier | Paths | Rule |
|---|---|---|
| Low | tests, docs, toolsets, refactors, ordinary Host/Core code | auto-merge after CI |
| High | everything in `.github/CODEOWNERS` (workflows, `CLAUDE.md`, `.claude/`, `docs/DECISIONS.md`, Docker files, `.dockerignore`, package versions and `*.csproj`, `scripts/`, `Program.cs`, `ToolBoxHttpApp.cs`, `Ports/`, guards, auth and admin code, plane separation, and the tests of those) | open the PR, enable auto-merge as usual, add `needs-timothy`, and stop; GitHub blocks the merge until Timothy's code-owner review, so his approval is the single click (dismiss-stale covers later pushes) |

**A high-risk path in the plan or in the diff means the high tier**, even if the issue looks low-risk. **Naming rule:** code that enforces a security boundary (guards, auth, the admin plane, bind addresses, middleware ordering) must match a CODEOWNERS pattern. If it doesn't, add the pattern in the same PR. CODEOWNERS protects by path, not intent, so also watch for a low-risk file that adds a tool executing commands, reading files or opening network connections, and flag it.

### Session notes additions
Every `## Session notes` comment also contains:
- A 5-line **Timothy summary**: what changed / why / risk / how to verify / what to learn.
- One **"What happens if X?"** question about the change, for the milestone digest.
- **Manual checks** the agent can't run (Docker, macOS, browser, hardware), as an unchecked list, one per line: `- [ ] <command> · <expected output> · <what it proves>`. These feed the next -> main digest.
