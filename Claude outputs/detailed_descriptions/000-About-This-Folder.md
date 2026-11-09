2026_11_08_14_34-(About-This-Folder)

# detailed_descriptions/ — what this folder is for

**Audience: other AI systems, not humans.** Timothy does not read these documents. He copies one, whole, into another LLM (ChatGPT, NotebookLM, Gemini, a fresh Claude session…) to give that system complete, precise context about one aspect of Tool_Box before he asks it questions.

## The contract for every document in this folder
1. **Self-contained.** Assume the reader has never seen the repo, the conversation, the issues or any other document. Define project-specific terms. Never write "as discussed" or "see the earlier chat".
2. **Exhaustive and precise over readable.** Prefer completeness to brevity. Use exact file paths, type and member names, config keys, env var names, ports, version numbers, command lines, exit codes, issue/PR/ADR numbers, and default values.
3. **Snapshot-dated.** State the date, branch and commit the description reflects, and what was in progress at that moment. The project changes daily.
4. **Facts vs. intentions vs. uncertainty, marked explicitly.**
   - *Implemented*: true in the code on the stated commit.
   - *Planned/decided*: agreed but not built (cite the issue/ADR).
   - *Unverified/assumed*: anything not checked against source. Label it as such.
5. **Structured for machine parsing.** Consistent headings, tables for enumerations, fenced code for literal values, no decorative prose, no motivational framing, no exercises.
6. **Include the "why".** Rationale and rejected alternatives, citing ADRs, because the downstream AI will be asked to reason about design.
7. **Include known gaps.** Tech debt, inconsistencies between docs and code, limitations, open questions.
8. **End with an "Instructions for the reading AI" section.** Tell it how to use the document and what not to assume.

## Typical requests
"Create a detailed description of how the project uses Docker." → One document covering every Docker-related file, the build stages, env vars, ports, compose, CI usage, release publishing, the security posture, history and known issues, in full.

## Naming
`NNN-Topic.md`, three-digit sequence in creation order. The first line is a timestamp `YYYY_MM_DD_HH_MM-(Topic)`. `001` is the whole-project overview; topic documents follow.

## Not for this folder
Explanations meant to teach Timothy. Those go in `../human_lectures/`.
