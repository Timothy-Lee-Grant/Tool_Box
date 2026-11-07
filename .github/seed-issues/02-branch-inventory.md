## Goal (Stage 1)
Every old branch and PR either gets merged into `next`'s history or is deliberately closed, so that eventually only `main` and `next` remain. Nothing is deleted until we've agreed on its fate here.

State as of 2026-11-06 (compared with `main` @ 813d99e):

| Branch | Commits not in main | Commits in main it lacks | Last commit |
|---|---|---|---|
| `development` (local + origin) | 3 | 1 | 2026-07-28 "green dot" |
| `feature/firmware-toolbox-addition` (local + origin) | 10 | 1 | 2026-08-04 — embedded toolset plan steps 1–4 + lecture |
| `origin/feature/002-streamable-http` | 0 | 34 | fully merged |
| `origin/feature/003-extra-tools` | 0 | 20 | fully merged |
| `origin/feature/Release-1.0` | 0 | 7 | fully merged |

Open PRs: not checked yet. List them with `gh pr list --state all`.

## Acceptance criteria
- [ ] A table posted here: each branch and PR → *merge into next* / *keep as tag* / *close and delete*, with the reason
- [ ] I've approved the table
- [ ] The approved merges are done into `next` (merge commits, so the history is kept); CI is green
- [ ] Branches with no remaining commits are deleted (I'll do the deleting)

## Context
The firmware branch holds unfinished work (implementation plan 004/embedded toolset). Decide whether it comes into `next` as-is, behind a flag, or parked as a tag.

## Out of scope
Changing any code on `main`.

## How far may the agent go without checking in?
Plan, then wait for my approval (default)
