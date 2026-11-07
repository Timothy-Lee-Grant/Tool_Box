#!/usr/bin/env bash
# One-time setup for the issue-driven AI workflow (docs/AI_WORKFLOW.md).
# Creates the labels, the first two milestones, and the seed issues in .github/seed-issues/.
# Run it from the repo root after `gh auth login`. Safe to re-run for labels; it will create
# duplicate issues if run twice, so it asks first.
set -euo pipefail

REPO="${REPO:-$(gh repo view --json nameWithOwner -q .nameWithOwner)}"
echo "Repository: $REPO"

label() { gh label create "$1" --repo "$REPO" --color "$2" --description "$3" --force >/dev/null && echo "  label $1"; }

echo "Labels..."
label "type:task"          "1D76DB" "One unit of work"
label "type:epic"          "5319E7" "Outcome made of several tasks"
label "type:bug"           "D73A4A" "Something broken"
label "stage-1-design"     "C5DEF5" "Stage 1: Timothy is writing the goal"
label "stage-2-discussion" "BFD4F2" "Stage 2: discuss options, no code"
label "stage-3-plan"       "FEF2C0" "Stage 3: agent posts a step plan for approval"
label "stage-4-implement"  "C2E0C6" "Stage 4: implement approved steps only"
label "stage-5-verify"     "0E8A16" "Stage 5: prove it works"
label "claude"             "D97757" "Hand this issue to Claude (triggers claude.yml)"
label "claude:queued"      "F9D0C4" "Waiting for the queue runner"
label "claude:working"     "E99695" "Queue runner is on it"
label "needs-timothy"      "B60205" "Blocked on a decision or answer from Timothy"

milestone() {
  gh api "repos/$REPO/milestones" -f title="$1" -f description="$2" >/dev/null 2>&1 \
    && echo "  milestone $1" || echo "  milestone $1 (already exists)"
}
echo "Milestones..."
milestone "M0 — Workflow & cleanup" "Issue-driven workflow running; old branches and PRs consolidated into next; v1 archived."
milestone "M1 — v2 direction"       "Decide what v2 is (GUI control center + RAG) and what it is built on."

read -r -p "Create the seed issues now? [y/N] " ans
[[ "$ans" == [yY] ]] || { echo "Skipping issues."; exit 0; }

S=.github/seed-issues
new_issue() { # title labels milestone file
  gh issue create --repo "$REPO" --title "$1" --label "$2" --milestone "$3" --body-file "$4" | grep -o '[0-9]*$'
}
M0="M0 — Workflow & cleanup"; M1="M1 — v2 direction"
n1=$(new_issue "[Task] Workflow smoke test: README link to AI_WORKFLOW" "type:task,stage-4-implement" "$M0" "$S/01-smoke-test.md")
n2=$(new_issue "[Task] Inventory old branches/PRs and consolidate into next" "type:task,stage-2-discussion" "$M0" "$S/02-branch-inventory.md")
n3=$(new_issue "[Task] Archive v1 (tag vs legacy folder)" "type:task,stage-2-discussion" "$M0" "$S/03-archive-v1.md")
n4=$(new_issue "[Task] Decide v2 base: evolve current code or start fresh" "type:task,stage-2-discussion" "$M1" "$S/04-rebuild-base.md")
n5=$(new_issue "[Task] Proposal: GUI control center + RAG direction" "type:task,stage-1-design" "$M1" "$S/05-direction-proposal.md")

body=$(printf '## Outcome\nThe repo is ready for v2 work: the AI workflow is running, the old branches and PRs are consolidated into `next`, v1 is archived, and the v2 direction is decided.\n\n## Tasks\n- [ ] #%s\n- [ ] #%s\n- [ ] #%s\n- [ ] #%s\n- [ ] #%s\n\n## Constraints for every task\n`main` is not touched until `next` is a working v2.\n' "$n1" "$n2" "$n3" "$n4" "$n5")
gh issue create --repo "$REPO" --title "[Epic] Clean up and point Tool_Box at v2" --label "type:epic" --milestone "$M0" --body "$body"
echo "Done. Add the 'claude' label to #$n1 to run the smoke test."
