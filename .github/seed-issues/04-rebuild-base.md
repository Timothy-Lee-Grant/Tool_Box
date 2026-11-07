## Goal (Stage 1)
Decide whether v2 (a GUI where you turn toolsets on and off, plus RAG) evolves the current Host/Core/Toolset code or starts from scratch.

## Acceptance criteria
- [ ] An honest assessment of the current code: what already supports "toolsets enabled at runtime" and what assumes they're fixed at compile time (e.g. `ToolBoxServerComposition`, ADR-005's one-line registration)
- [ ] 2–3 options with trade-offs (evolve / strangler-fig / rewrite)
- [ ] A recommendation; the decision becomes an ADR

## Context
Read the direction issue for what v2 needs. ADR-003, ADR-005, ADR-007, ADR-010.

## How far may the agent go without checking in?
Discuss only — no plan, no code
