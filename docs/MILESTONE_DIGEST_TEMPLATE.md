# Milestone digest template

Use this as the body of the `next` → `main` PR at the end of a milestone. Build it from each issue's `## Session notes` comments. Merge with a merge commit, not squash. Timothy closes the issues after acceptance.

---

## Milestone: <name>

### Issues in this milestone

#### #<n> <title>
- **Summary:** what changed / why / risk tier / how to verify / what to learn (the 5-line Timothy summary).
- **Links:** issue, PRs, lectures, ADRs.
- **What happens if <X>?** One question about the change. Answer it before merging; if you can't, read the PR.

<!-- repeat per issue -->

### Verification checklist
Run once, carefully, on a clean checkout of `next`. Zsh copy-paste safe: no inline `#` comments.

- [ ] `<command>` · `<expected output>` · `<what it proves>`

<!-- collected from each issue's Manual checks -->

### Known gaps
- What CI does not cover, deferred work, open `needs-timothy` items.

### Health of the workflow (review once per milestone)
`needs-timothy` count · reverts on `next` · time `next` was red · Timothy's minutes per issue · could Timothy explain each issue from its summary?
