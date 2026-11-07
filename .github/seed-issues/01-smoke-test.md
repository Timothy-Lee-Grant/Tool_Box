## Goal (Stage 1)
Prove the issue-driven workflow works end to end before giving it real work: issue → Claude → branch → PR into `next` → CI → session notes.

The change itself is deliberately tiny: add a short "How this project is built" section to `README.md` that links to `docs/AI_WORKFLOW.md`.

## Acceptance criteria
- [ ] A branch `claude/issue-<n>-...` exists, with a PR into **`next`** (not `main`) that says `Closes #<n>`
- [ ] CI (`ci.yml`) ran on the PR and is green
- [ ] Claude left a `## Session notes` comment on this issue
- [ ] `main` is unchanged

## Context
`CLAUDE.md`, `docs/AI_WORKFLOW.md`

## Out of scope
Any change other than that README section.

## How far may the agent go without checking in?
Plan and implement, then ask me to review
