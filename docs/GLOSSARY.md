# Glossary: workflow terms

Plain-language meanings for the terms used in [AI_WORKFLOW.md](AI_WORKFLOW.md). Alphabetical.

## APPROVED convention
The only thing that starts implementation: a comment on the issue whose first line starts with `APPROVED` (written `@claude APPROVED ...`), authored by the repo owner. Labels never count, because labels can be changed by agents.

## Auto-merge
A GitHub setting on a PR that says "merge this as soon as every rule is satisfied". The agent turns it on with `gh pr merge --auto --squash <n>`, only for PRs into `next`. It waits for the gate; it cannot skip it.

## CODEOWNERS
A file (`.github/CODEOWNERS`) that maps paths to the people who must review changes to them. Here it lists the high-risk paths, so GitHub itself blocks an agent PR touching them until Timothy reviews. It protects by path, not by intent.

## Kill switch
A way to stop the agent immediately. The label `claude:paused` pauses one issue; the repo variable `CLAUDE_PAUSED=true` pauses everything. Both are checked in the workflow, not just in the prompt.

## Merge commit
A merge that keeps every commit of the branch and adds one commit joining the two histories. Used for syncing `main` into `next` and for `next` → `main`, so the histories stay connected. See also **Squash merge**.

## Required check
A CI job that must pass before GitHub allows a merge. For `next` these are `build-and-test` and `docker`.

## Risk tier
How much autonomy a change gets, based on what it can break. **Low** (tests, docs, toolsets, ordinary code) auto-merges after CI. **High** (anything in CODEOWNERS) waits for Timothy's review.

## Ruleset
GitHub's server-side rules for a branch. The `next-gate` ruleset on `next` allows PRs only, requires the checks above, requires the branch to be up to date, and requires code-owner review. Unlike a convention, it cannot be forgotten or talked around.

## Single-flight
Only one agent run is active at a time, because `claude.yml` uses one repo-wide concurrency group. The group keeps one running and one pending run, so wait for a run to finish before triggering the next, or the older pending one is cancelled.

## Squash merge
A merge that collapses all of a branch's commits into one new commit on the target. Agent PRs into `next` use it to keep history tidy. See also **Merge commit**.

## Up-to-date branch
A PR branch that already contains the latest commits of its target. The ruleset requires it so CI tests the code as it will actually look after merging. If `next` moves on, merge `origin/next` into the branch and push.
