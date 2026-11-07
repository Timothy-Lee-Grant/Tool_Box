## Goal (Stage 1)
Preserve v1 — the current working MCP server and all of its lectures, plans and ADRs — as a clean, permanently findable reference, because it's a good example of a simple MCP server and it's my study record.

My first idea was to move the old code into a folder called something like "OlderImplementation". Discuss whether that's better than a git tag (`v1.0`) plus a GitHub release, or than keeping v1 in place and evolving it. Consider: what résumé readers see, the build and CI (does a second copy of the code get built and tested?), and how I find the lectures later.

## Acceptance criteria
- [ ] Options compared in a comment, with a recommendation
- [ ] Decision recorded as an ADR in `docs/DECISIONS.md`
- [ ] The chosen archive exists (tag/release/folder) and CI is still green

## Context
Depends on the decision in the "rebuild base" issue. `Documentation/` must stay intact either way.

## How far may the agent go without checking in?
Discuss only — no plan, no code
