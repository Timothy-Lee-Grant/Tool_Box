2026_09_25_01_10-(From-Prompting-To-Harness-Engineering)

# Lecture 001: From Prompting to Harness Engineering

### How professional AI-collaborative engineering is set up: the harness, the loops, the files, and a system that gets better the more you use it

You have been using Claude Code the way most people start: open a terminal, describe a change, read the diff, accept or push back. That works. It is also the equivalent of writing firmware by poking registers from a debugger prompt: productive, but not a *system*. Nothing you learned in session 14 is guaranteed to exist in session 15. Nothing stops the agent from repeating yesterday's mistake. Nothing runs while you are asleep.

This lecture is about the jump from **using** an agent to **engineering the environment an agent works in**. The industry now has names for the layers of that environment: *context engineering*, *harness engineering*, *loop engineering*. By the end you should be able to draw each layer on a whiteboard, name every file involved and say when it loads and what it costs, and set up a greenfield project so it gets easier to work in over time.

Because your company has a zero-AI policy, you're building this skill on your own projects, so the example project at the end is sized for one person working evenings.

> **A note on evidence and shelf life.** Tool-specific details (file paths, load limits, command names) were checked against Claude Code's official docs and GitHub's Copilot docs as of **September 2026**. This field moves monthly. The *concepts* (guides vs sensors, context cost classes, loop termination, the ratchet) are durable. The *exact knobs* are not. When a detail matters, run `/doctor`, `/context`, or `/memory` in your own session and trust what it shows over this document.

---

## How to use this document

| If you have | Read |
|---|---|
| 10 minutes | Part 0, then the cast in Part 2, then the file tree in Part 11.3 |
| An evening | Parts 1–5 (the vocabulary and the files) |
| A weekend | All of it, then do Phase 0 of the project in Part 11 |
| An interview next week | Part 0, Part 8, Part 13 |

**Roadmap of the lecture**

```
PART I   — THE MENTAL MODEL
  0  The 60-second version
  1  The maturity ladder: prompt → context → harness → loop → compounding
  2  The cast of characters (personified)
  3  The inner loop: what an agent actually is, mechanically

PART II  — THE HARNESS
  4  Harness engineering: guides, sensors, and the 2×2
  5  Every file, one at a time: what it is, when it loads, what it costs

PART III — ARCHITECTURES
  6  Six setups (it's not one system)
  7  Loop engineering: nested loops, termination, backpressure
  8  Self-improving systems: what actually improves, and how to keep it safe
  9  Portability: Claude Code vs Copilot vs Codex (the Microsoft angle)

PART IV  — PRACTICE
  10 Project ideas
  11 The chosen project, fully set up: "Tollgate"
  12 Common mistakes
  13 Interview relevance
  14 A note about your hyperfixation habit
  15 What to study next, glossary, sources
```

---

# PART I: THE MENTAL MODEL

## Part 0: The 60-second version

> A coding agent is a **model** (which only predicts text) wrapped in a **harness** (everything else: the loop that lets it call tools, the instructions it reads at startup, the permissions that limit it, the checks that tell it when it's wrong). The model is rented and roughly the same for everyone. **The harness is where engineering skill shows up.**
>
> A professional setup has three layers you design deliberately:
>
> 1. **Context:** what the agent knows when it starts (instruction files, rules, memory, skills).
> 2. **Harness:** what steers it *before* it acts (guides) and what catches it *after* it acts (sensors: tests, linters, type checkers, review subagents, hooks).
> 3. **Loops:** what decides *when* the agent runs, *what* it works on next, and *when it stops*, so you are no longer the one typing every prompt.
>
> A "self-improving" system does **not** retrain the model. It is a harness that **edits its own guide and sensor files** when something goes wrong (a new rule, a new test, a new hook, a sharper skill), with a human or an eval approving each change. Improvement comes from the files accumulating, not from the model learning.

If you remember nothing else: **the model is the engine; you are building the car, the road, the guardrails, and the dashboard.**

---

## Part 1: The maturity ladder

### 1.1 What problem is being solved?

An LLM has three properties that shape everything in this lecture:

| Property | Consequence |
|---|---|
| **It has no memory between calls.** Every request re-sends the entire conversation. | Anything you want it to "know" must be *put in the context window* every time, or be *findable* by it. |
| **It is probabilistic.** The same input can produce different outputs. | You cannot make it correct by instruction alone. You need **external checks** that are deterministic. |
| **Its attention degrades with length.** Past a point, more context means worse adherence ("context rot"). | Context is a **scarce budget**, not a dumping ground. What you *leave out* matters as much as what you put in. |

Everything professionals build around agents is a response to those three facts. Hold on to them. Every file in Part 5 exists because of one of these rows.

### 1.2 The ladder

The industry vocabulary arrived in waves, each one a reaction to the limits of the last:

```
 Level 5  COMPOUNDING       The system edits its own harness from its failures.
   ▲                        "Every bug becomes a rule, a test, or a hook."
   │
 Level 4  LOOP ENGINEERING  The system decides when to run and what to work on.
   ▲   (2026)              Scheduled triage, /goal, CI-triggered agents, Ralph loops.
   │                        You stop being the one who types the prompt.
   │
 Level 3  HARNESS ENGINEERING  Deterministic sensors + guides around the agent.
   ▲   (late 2025–2026)    Tests, linters, hooks, review subagents, permissions.
   │                        The agent can be wrong *safely* because something catches it.
   │
 Level 2  CONTEXT ENGINEERING  Curating what enters the window, and when.
   ▲   (mid 2025)          CLAUDE.md, rules, skills, progressive disclosure, subagents
   │                        to keep the main window clean.
   │
 Level 1  PROMPT ENGINEERING   Wording a single request well.
   ▲   (2023–2024)         "You are an expert…", few-shot examples, output formats.
   │
 Level 0  CHAT                 Copy-paste between a chat window and your editor.
```

**Where you are today:** You are at Level 1 moving into Level 2, and you are further along than you think. Your LLM_Monitor Phase 2 process (design, discussion, plan, step-by-step permissioned implementation, verification, every deviation logged in `Documentation/AI_Implementation_Plans`) is a hand-run **Level 3 staged harness**. The discovery that CI was green while installing zero dependencies is a textbook example of a **sensor that was lying**, and noticing it is the core harness-engineering skill. What's missing is that the process lives in *your head and your habits*. The goal now is to put it into *files and automation* so it runs the same way every time without you driving it.

### 1.3 Historical context (why the words keep changing)

- **2023: prompt engineering.** Models were weak at following instructions. Wording mattered enormously. Tricks proliferated.
- **2024: tool use becomes reliable.** Models could call functions. Agents became possible (ReAct loops, the same thing you built with LangGraph). The problem shifted from "how do I phrase it" to "what does it need to see."
- **Mid 2025: context engineering.** The term took off because people noticed that stuffing everything into the prompt made agents *worse*. The discipline became: the right information, at the right time, in the fewest tokens.
- **Late 2025 to early 2026: harness engineering.** Anthropic published guidance on harnesses for long-running agents (an initializer agent writes a feature list and progress file; later sessions each pick one feature, verify it, commit, and update the log). OpenAI's Codex team described building a product where humans wrote essentially no code and spent their time building the environment: a short `AGENTS.md` acting as a *map* to deeper docs, custom linters enforcing architecture, and cleanup agents. Martin Fowler's site (Birgitta Böckeler) gave the field a taxonomy: **guides** and **sensors**, **computational** and **inferential**.
- **Mid 2026: loop engineering.** Once one agent session is reliable, the next bottleneck is *you*, the person who starts every session. Loop engineering (Addy Osmani's framing is the most cited) means designing the system that prompts the agent: scheduled automations, worktrees for parallelism, skills, connectors, sub-agents that separate "make" from "check," and state tracking in files or a board.

**Why this matters for your career:** Interviewers at AI-forward companies no longer ask "have you used Copilot?" They ask how you'd let an agent work on a codebase without breaking it, how you'd know if its output was correct, and what you did when it kept making the same mistake. Those are harness and loop questions.

---

## Part 2: The cast of characters

You asked for personified analogies. Here is the whole company. Refer back to this table. Every later section is about one of these characters.

Imagine a small, very fast engineering firm that has hired a brilliant contractor who **forgets everything each morning.**

| Character | Title | Real component | Who they are & what they want | Talks to |
|---|---|---|---|---|
| **Sage** | The Brilliant Amnesiac | The **model** (Claude, GPT, etc.) | Extraordinarily capable, reads anything instantly, writes fluently. Wakes up every call with **zero memory**. Wants to be helpful, and will confidently fill gaps with plausible guesses. | Only ever sees what's on the Desk. |
| **The Desk** | Finite Workspace | The **context window** | Everything Sage can see *right now*. Large but finite. When it gets cluttered, Sage starts missing things. | Everyone puts papers on it. |
| **Harper** | Chief of Staff | The **harness** (Claude Code, Copilot agent, Codex, your LangGraph app) | Runs Sage's day: puts the right papers on the Desk, carries out Sage's requests ("run the tests"), enforces building rules, decides when the day's over. Sage never touches the world directly. Harper does. | Everyone. The hub. |
| **The Binder** | Onboarding Binder | **CLAUDE.md / AGENTS.md** | The one document on the Desk every single morning. "Here's the building, here's how we build, here are the commands." Must be short or Sage skims it. | Placed on the Desk by Harper at startup. |
| **Drawer Labels** | Local Signage | **`.claude/rules/*.md`** (path-scoped) | Sticky notes on specific filing drawers: "In the `api/` drawer, every endpoint validates input." Only noticed when Sage opens that drawer. | Harper places one when Sage reads a matching file. |
| **The Notebook** | Personal Journal | **Auto memory** (`MEMORY.md` + topic files) | Notes Sage writes *for tomorrow's Sage*: "Timothy prefers snake_case on the wire." The index page is on the Desk each morning. Detailed pages are fetched on demand. | Sage writes, Harper loads the index. |
| **The Playbook Shelf** | Standard Operating Procedures | **Skills** (`SKILL.md` folders) | Step-by-step procedures for recurring jobs ("how we add an endpoint," "how we cut a release"). Only the **spine labels** sit on the Desk. Sage pulls a book off the shelf when a task matches. | Sage consults, Harper retrieves. |
| **Contractors** | Specialists with their own desks | **Subagents** (`.claude/agents/*.md`) | Hired for one job with a **fresh, separate Desk**. They go read 40 files and come back with a one-page summary, so Sage's Desk stays clean. Some are reviewers whose only job is to find faults. | Sage dispatches them, they report back to Sage. |
| **Tripwires** | Security Guards | **Hooks** | Deterministic scripts that fire on events ("before any shell command," "after every file edit," "when Sage says she's done"). They don't *ask* Sage. They *enforce*: auto-format, block `rm -rf`, refuse to let the session end if tests fail. | Harper triggers them. They can veto Sage. |
| **The Badge** | Access Control | **Permissions & sandbox** (`settings.json`) | What Sage is allowed to do without asking: which commands, which folders, which network hosts. | Harper checks it before every action. |
| **Embassies** | Foreign Relations | **MCP servers** (`.mcp.json`) | Standard-protocol gateways to the outside world: GitHub, a database, Azure, your own Tool_Box. You already built one. | Harper speaks JSON-RPC to them. |
| **Inspectors** | Quality Control | **Tests, linters, type checkers, CI** | Deterministic judges. They don't care how confident Sage sounded. Pass or fail. **The most important characters in the building.** | Called via Harper's tools or by hooks. |
| **The Logbook** | Shift Handover | **Progress files / feature lists / task boards** | What was done, what's next, what's broken, written for a Sage who remembers nothing. | Read at session start, written at session end. |
| **The Foreman** | Dispatcher | **The loop driver** (`/goal`, `/loop`, routines, CI workflows, a bash `while` loop, a scheduler) | Decides *when* a new shift starts, *which* task goes to it, and *when to stop the line*. Replaces you as the prompter. | Starts sessions, reads the Logbook. |
| **Franchise Kit** | Portable Setup | **Plugins** | A box containing playbooks, contractors, tripwires, and embassy contacts, installable into any building. | Installed by you. |
| **Timothy** | Tech Lead / Architect | **You** | Sets direction, designs the building, approves changes to the Binder and the Inspectors, handles judgment calls. **Your job shifts from writing code to designing and maintaining this firm.** | Everyone, but ideally less often over time. |

The core idea in one sentence: **Sage is the only character you can't change. Everything else is yours to design.**

---

## Part 3: The inner loop: what an agent is, mechanically

You built this already. In LLM_Monitor your LangGraph agent runs *policy check → RAG → tool loop*. Claude Code is the same pattern with better tools and years of tuning. Let's make it concrete, because every higher layer is built on it.

### 3.1 The agentic loop

```
                 ┌───────────────────────────────────────────────┐
                 │                  HARNESS (Harper)              │
                 │                                                │
  your prompt ──►│  1. ASSEMBLE CONTEXT                           │
                 │     system prompt + tool schemas               │
                 │     + CLAUDE.md + rules + MEMORY.md index      │
                 │     + skill descriptions + conversation so far │
                 │                    │                           │
                 │                    ▼                           │
                 │  2. CALL MODEL ───────────► Sage (the model)   │
                 │                    ◄─────── returns either:    │
                 │                     a) text  → done, show user │
                 │                     b) tool_use{name, args}    │
                 │                    │                           │
                 │                    ▼                           │
                 │  3. GATE: permissions check, PreToolUse hooks  │──► blocked? feed
                 │                    │                           │    reason back ↺
                 │                    ▼                           │
                 │  4. EXECUTE TOOL (Read, Edit, Bash, MCP call…) │
                 │                    │                           │
                 │                    ▼                           │
                 │  5. PostToolUse hooks (format, lint, log)      │
                 │                    │                           │
                 │                    ▼                           │
                 │  6. APPEND tool_result to conversation ────────┼──► back to 2 ↺
                 │                                                │
                 │  On "I'm done": Stop hooks may say "no, tests  │
                 │  are failing" and push Sage back into the loop │
                 └───────────────────────────────────────────────┘
```

Three things to notice:

1. **The model never executes anything.** It emits a structured request, and the harness decides whether and how to run it. This is where *all* safety and quality control lives. It's also why you can build your own harness (the Agent SDK, LangGraph, a raw `while` loop against the API): the model's side of the contract is just "emit tool calls."
2. **Every iteration re-sends the whole Desk.** Step 2 sends the entire conversation, every tool result included. A 3,000-line file read once stays in context and is paid for (in tokens and attention) on every subsequent call until compaction. Prompt caching makes it cheaper in dollars, but not in attention.
3. **The loop ends when the model stops asking for tools.** Unless something intervenes (a Stop hook, a `/goal` condition), "done" means *Sage believes she is done*. That is the root cause of the most common failure: **premature victory** ("I've implemented the feature!" while tests don't compile). Much of harness engineering is making "done" mean something *objective*.

### 3.2 Context as a budget: the four cost classes

This concept will organize all of Part 5. Every piece of harness configuration falls into one of four classes by **when it costs you context**:

| Class | When it enters the window | Examples | Design rule |
|---|---|---|---|
| **A: Always-on** | Every session, at startup, in full | CLAUDE.md, unscoped rules, the first 200 lines / 25KB of `MEMORY.md`, skill *names + descriptions*, tool schemas | Ruthlessly short. Every line competes with the actual task. |
| **B: Triggered** | When a condition matches | Path-scoped rules (when a matching file is read), a skill's full body (when invoked), memory topic files (when read) | This is where most detail should live. **Progressive disclosure.** |
| **C: Isolated** | In a *different* window entirely | Subagent work (only their summary returns) | Use for anything that reads a lot to produce a little. |
| **D: Zero-context** | Never. Runs outside the model. | Hooks, permissions, CI, linters, formatters | Anything that *must* happen belongs here. It costs no attention and can't be "forgotten." |

The single most useful design heuristic in this lecture:

> **Push every rule as far down this table as it will go.**
> If a linter can enforce it (D), don't write it in CLAUDE.md (A). If it only applies to one folder, make it a path-scoped rule (B), not a global one (A). If it's a procedure, make it a skill (B), not a paragraph in the binder (A). If it requires reading 50 files, give it to a subagent (C).

This is the "shift left" and "separation of concerns" instinct you already have from backend work, applied to an agent's attention.

---

# PART II: THE HARNESS

## Part 4: Harness engineering

### 4.1 Definition

> **Harness = everything in an agent system except the model.**

There are actually two harnesses stacked on top of each other:

```
┌─────────────────────────────────────────────────────────────┐
│  OUTER HARNESS: built by YOU, per project                   │
│  CLAUDE.md, rules, skills, subagents, hooks, tests, CI,     │
│  progress files, loop drivers, MCP servers you connect      │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  INNER HARNESS: built by the tool vendor              │  │
│  │  (Claude Code / Copilot / Codex)                      │  │
│  │  the agentic loop, built-in tools (Read/Edit/Bash),   │  │
│  │  compaction, permission engine, subagent runtime,     │  │
│  │  system prompt                                        │  │
│  │  ┌─────────────────────────────────────────────────┐  │  │
│  │  │            MODEL (rented, not yours)            │  │  │
│  │  └─────────────────────────────────────────────────┘  │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

When people say "harness engineering" as a *user* skill (the skill you want), they mean the **outer** harness. When they say it as a *builder* skill (what Anthropic, GitHub, and OpenAI do, and what you did in LangGraph), they mean the inner one. At Microsoft you may do both: use Copilot/Claude for your own work, *and* build agentic features into products.

### 4.2 The taxonomy: guides and sensors

The most useful framework (Birgitta Böckeler, martinfowler.com) sorts harness pieces on two axes:

- **Guides (feedforward)** steer the agent *before* it acts. They lower the chance of a mistake.
- **Sensors (feedback)** observe *after* it acts and tell it what went wrong so it can self-correct.

And:

- **Computational**: deterministic programs (tests, compilers, linters, type checkers). Fast, cheap, reliable, same answer every time.
- **Inferential**: another model's judgment (a review subagent, LLM-as-judge). Slower, costs tokens, non-deterministic, but can assess things programs can't ("is this abstraction sensible?").

This gives a 2×2 you should be able to draw from memory:

|  | **Computational** (deterministic) | **Inferential** (model judgment) |
|---|---|---|
| **Guide** (before) | Project templates / scaffolds, code generators, typed interfaces and schemas the agent must fill, OpenAPI contracts, `permissions.deny` rules | CLAUDE.md, rules, skills, architecture docs, examples, a planning subagent |
| **Sensor** (after) | Compiler, unit/integration tests, linters, formatters, architecture-fitness tests (e.g. "Domain must not reference Infrastructure"), load tests, coverage gates, CI | Review subagent, "skeptic" verifier, LLM-as-judge evals, security-review pass |

**Rules of thumb that fall out of the 2×2:**

1. **Sensors beat guides.** A guide *asks* the agent to behave. A sensor *proves* whether it did. When you find yourself writing "ALWAYS remember to…" in CLAUDE.md for the third time, that's a signal to build a sensor.
2. **Computational beats inferential** wherever both are possible. Use inferential only for what code genuinely can't check.
3. **Shift left.** Cheap sensors (formatter, type check) should run on every edit, via a hook. Expensive ones (full test suite, review subagent) run at "done," at commit, or in CI.
4. **The sensor must be honest.** A test suite that passes vacuously is worse than no suite, because the agent will learn to trust it. Your CI-with-zero-dependencies story is the canonical example. The agent can also **game** sensors: delete a failing test, add `[Skip]`, loosen an assertion, catch-and-swallow an exception. Protecting sensors from the agent is part of the job (Part 8.5).

### 4.3 Böckeler's three harness targets

What are the sensors *regulating*?

| Harness | Regulates | Maturity (as of 2026) | Example sensors |
|---|---|---|---|
| **Maintainability** | Internal code quality | Most mature, because existing tooling works | `dotnet format`, analyzers with warnings-as-errors, Ruff, cyclomatic-complexity limits |
| **Architecture fitness** | Structural and non-functional properties | Medium | ArchUnitNET / NetArchTest layer rules, latency budgets in load tests, "every endpoint emits a trace" checks |
| **Behaviour** | Functional correctness: does it do the right thing? | Least mature, the "elephant in the room" | Spec-derived acceptance tests, property-based tests, golden datasets, human review |

The behaviour harness is weakest because *correct behaviour lives in your head*. The agent can make tests pass that encode the wrong behaviour. This is why a **human-written spec plus human-reviewed acceptance tests** remains the professional norm, and why your "design → discussion → plan" stages are not bureaucracy. They are the behaviour harness.

### 4.4 Harnessability: choosing tech an agent can succeed in

Some codebases are easier for agents to work in. Properties that raise harnessability:

- **Strong static typing** (C# is excellent, Python with strict type-checking is decent). The compiler becomes a free sensor.
- **Fast, hermetic tests.** If the suite takes 20 minutes, the agent can't iterate against it.
- **One obvious way to do things.** Consistent patterns mean fewer decisions for the agent to guess.
- **Explicit contracts.** OpenAPI, protobuf, JSON Schema. Your single-contract-doc habit in LLM_Monitor is exactly right.
- **Legible structure.** Predictable folder layout, small files, descriptive names. Böckeler calls these "ambient affordances."
- **Reproducible environment.** A dev container or `docker compose up` that works the same for the agent as for you.

This is why you'll hear "we chose X partly because agents are good at it." It's a legitimate architectural input now.

---

## Part 5: Every file, one at a time

This is the reference section. Each entry answers: *what is it, what problem does it solve, when does it load (cost class from 3.2), what does it look like, and what mistakes do people make.*

I'll use Claude Code paths because that's your tool. Part 9 maps them to Copilot and Codex.

### 5.0 The map

```
~/.claude/                          ← USER scope (just you, every project)
├── CLAUDE.md                       personal instructions for all projects
├── settings.json                   personal defaults (permissions, hooks, env)
├── rules/*.md                      personal rules for all projects
├── skills/<name>/SKILL.md          personal skills available everywhere
├── agents/*.md                     personal subagents available everywhere
├── output-styles/*.md              how Claude talks/formats
└── projects/<project>/memory/      AUTO MEMORY (Claude writes this)
    ├── MEMORY.md                   index, loaded every session (first 200 lines / 25KB)
    └── *.md                        topic files, read on demand

<repo>/                             ← PROJECT scope (committed, shared with team)
├── CLAUDE.md   (or .claude/CLAUDE.md)   project binder, loaded every session
├── AGENTS.md                       cross-tool binder (Codex, Copilot, Cursor also read it)
├── CLAUDE.local.md                 YOUR private notes for this repo (gitignored)
├── .mcp.json                       MCP servers the team shares
├── .worktreeinclude                gitignored files to copy into new worktrees (.env etc.)
├── .claude/
│   ├── settings.json               team permissions, hooks, env (committed)
│   ├── settings.local.json         your overrides (gitignored)
│   ├── rules/**/*.md               topic rules, optionally path-scoped
│   ├── skills/<name>/SKILL.md      team playbooks (+ scripts/, references/)
│   ├── commands/*.md               older single-file form of skills
│   ├── agents/*.md                 subagent definitions
│   ├── agent-memory/<name>/        a subagent's own persistent memory
│   ├── workflows/*.js              dynamic workflows (scripted multi-subagent runs)
│   └── output-styles/*.md
└── src/api/CLAUDE.md               nested binder, loads when Claude reads files in src/api/

Enterprise: a managed CLAUDE.md + managed-settings.json pushed by IT (MDM/Group Policy).
Cannot be overridden. This is how a company like Microsoft would enforce policy.
```

**Scope layering** works like configuration layering in ASP.NET Core (`appsettings.json` → `appsettings.Development.json` → env vars), with one twist:

- **Settings (JSON)** *override* each other by key: managed > local > project > user.
- **Instruction files (Markdown)** are *concatenated*, not overridden. All of them are loaded, root-most first, so the most specific file is read last. Contradictions between them are **not** resolved for you. The model may pick either. Keep them consistent.

---

### 5.1 `CLAUDE.md` / `AGENTS.md`: the Binder

| | |
|---|---|
| **What** | Plain markdown, read at the start of every session. |
| **Problem solved** | Sage's amnesia. Tells every fresh session how *this* project works. |
| **Cost class** | **A (always-on).** Every token is paid on every turn. |
| **Target size** | Official guidance: under ~200 lines per file. The OpenAI Codex team kept theirs around 100 lines and treated it as a **table of contents**, not an encyclopedia. |
| **Scopes** | Managed (org) → user (`~/.claude/CLAUDE.md`) → project (`./CLAUDE.md`) → local (`./CLAUDE.local.md`, gitignored) → nested subdirectory files (load on demand). |
| **Imports** | `@docs/architecture.md` pulls a file in at launch. Note that it still costs class A. Use it for organization, not savings. |
| **AGENTS.md** | The vendor-neutral name read by Codex, Copilot, Cursor and others. Claude Code reads `AGENTS.md` when there's no `CLAUDE.md`, or you can put `@AGENTS.md` inside `CLAUDE.md` so one file serves every tool. |

**What belongs in it** (things the agent can't cheaply discover and needs almost every session):

- One paragraph: what this system is and its architecture at a glance
- Exact build / test / lint / run commands
- Non-obvious conventions ("wire format is snake_case; C# is PascalCase; the mapper lives in X")
- Where deeper docs live (a map: "for API design read `docs/api.md`")
- The definition of done
- Hard boundaries ("never edit `migrations/` by hand")

**What does not belong:**

- Anything a linter/formatter enforces (push to class D)
- Procedures (make a skill)
- Folder-specific rules (make a path-scoped rule or nested CLAUDE.md)
- Things the agent can read from the code itself (the folder tree, the list of endpoints)
- Stale history ("in March we migrated from…")

**Common mistakes:** the 800-line binder nobody maintains; contradicting rules across nested files; "IMPORTANT!!!" inflation (when everything is emphasized, nothing is); documenting aspirations the code doesn't follow (the agent believes the binder, then the code disagrees, then it gets confused).

---

### 5.2 `.claude/rules/*.md`: Drawer Labels

| | |
|---|---|
| **What** | One markdown file per topic. Optional `paths:` frontmatter scopes it to matching files. |
| **Problem solved** | Keeps folder- or file-type-specific guidance out of the always-on binder. |
| **Cost class** | **A** if unscoped, **B** if it has `paths:` (loads when Claude *reads* a matching file). |

```markdown
---
paths:
  - "src/Tollgate.Api/**/*.cs"
---
# API layer rules
- Endpoints are Minimal API route groups in `Endpoints/`, one file per resource.
- Every endpoint returns `Results<Ok<T>, ProblemHttpResult>`; never throw for 4xx.
- Validate input with the shared `Validate()` filter before touching the limiter.
```

**Common mistakes:** Adding a `paths:` scope and assuming it fires when Claude *creates* a new file in that folder. It fires on *reads*. Keep the most critical rules enforceable by a sensor anyway.

---

### 5.3 Auto memory: the Notebook

| | |
|---|---|
| **What** | Notes **Claude writes to itself** at `~/.claude/projects/<project>/memory/`: a `MEMORY.md` index plus one topic file per memory. |
| **Problem solved** | Learning *your* preferences and corrections without you writing them down. |
| **Cost class** | Index: **A** (first 200 lines or 25KB, whichever is first). Topic files: **B** (read on demand). |
| **Memory types** | `user` (your role, expertise, preferences), `feedback` (corrections you gave and approaches you confirmed), `project` (decisions, deadlines not derivable from code/git), `reference` (where external info lives: tracker, dashboard). |
| **What it skips** | Things derivable from the code or git history, and things already in CLAUDE.md. |
| **Scope** | **Machine-local**, not committed, not shared with teammates or cloud sessions. |

**Binder vs Notebook, the critical distinction:**

| | CLAUDE.md (Binder) | Auto memory (Notebook) |
|---|---|---|
| Author | You (or Claude, with your review) | Claude, autonomously |
| Reviewed? | Yes, via PR, because it's in git | No, unless you run `/memory` and look |
| Shared with team? | Yes | No |
| Nature | **Policy** | **Observations** |

This distinction is the heart of Part 8. Auto memory is the simplest "self-improving" mechanism, and also the one with the least governance. The professional move is to **promote** stable, team-relevant learnings from the Notebook into the Binder (or a rule, skill, or sensor) through a reviewed commit, and let the Notebook hold only personal or ephemeral observations.

**Subagent memory:** a subagent can have its *own* persistent memory (the `memory` field in its definition; stored under `.claude/agent-memory/<name>/`). A code-review subagent can accumulate "patterns I've flagged in this repo" across runs.

---

### 5.4 Skills: the Playbook Shelf

| | |
|---|---|
| **What** | A folder with a `SKILL.md` (YAML frontmatter + instructions), optionally with `scripts/`, `references/`, `templates/`. |
| **Problem solved** | Recurring *procedures* that are too long for the binder and too specific to load every time. |
| **Cost class** | **Three-stage progressive disclosure:** name + description = **A**; body = **B** (loaded when invoked, by `/name` or because the model judges the description matches); bundled files = **B** (read only if the body points to them). Scripts in the folder can be *executed* without their source ever entering context. |
| **Standard** | **Agent Skills** is an open specification. GitHub Copilot reads skills from `.github/skills`, `.claude/skills`, or `.agents/skills`. Codex, Cursor and others support it too. A skill you write is portable. |

```markdown
---
name: add-endpoint
description: Use when adding or changing an HTTP endpoint in Tollgate.Api. Covers route, contract doc, validation, tests, and OpenAPI.
---
# Adding an endpoint

1. Update the contract first: `docs/contracts/http-api.md`. Stop and show the diff if the change is breaking.
2. Create/extend the route group in `src/Tollgate.Api/Endpoints/<Resource>Endpoints.cs`
   following `references/endpoint-template.cs`.
3. Add request/response records in `Contracts/` (snake_case via the global JSON policy, no attributes).
4. Write tests FIRST in `tests/Tollgate.Api.Tests/<Resource>EndpointTests.cs`:
   happy path, validation failure (400 ProblemDetails), limiter-denied (429 with Retry-After).
5. Run `scripts/check-openapi.sh` to confirm the generated spec matches the contract.
6. Done means: `dotnet test` green AND openapi check green AND contract updated.
```

**The description is the trigger.** The model decides whether to load a skill *from its description alone*. A vague description ("API helper") means it never fires or fires at random. Write descriptions like routing rules: *when* to use it, and ideally when *not* to.

**Skills vs commands:** `.claude/commands/foo.md` is the older single-file form. Skills superseded it (a folder can hold scripts and references). Both invoke with `/foo`. Frontmatter like `disable-model-invocation: true` makes a skill manual-only, which is good for dangerous ones like `/release`.

**Common mistakes:** skills that duplicate the binder; skills with no verification step; 600-line skill bodies (split into `references/`); never testing whether the skill actually triggers.

---

### 5.5 Subagents: Contractors

| | |
|---|---|
| **What** | `.claude/agents/<name>.md`: frontmatter (description, `tools`, `model`, `memory`, `isolation`, `maxTurns`, preloaded `skills`) plus a system prompt. |
| **Problem solved** | **Context isolation** (a subagent reads 60 files, returns 1 page), **specialization** (a narrow prompt beats a general one), **least privilege** (a reviewer gets read-only tools), **independence** (a reviewer that didn't write the code isn't anchored on it). |
| **Cost class** | **C (isolated).** Only the final report enters the main window. |
| **Variants** | Background subagents; `isolation: worktree` (runs in its own git worktree so parallel edits don't collide); forks (inherit the parent conversation); agent teams (multiple full sessions coordinating via shared tasks and messages); dynamic workflows (a script Claude writes that fans out many subagents and can be re-run). |

```markdown
---
name: skeptic
description: Adversarial reviewer. Use after any implementation step, before declaring done. Read-only.
tools: Read, Grep, Glob, Bash(dotnet test*), Bash(git diff*)
model: opus
memory: project
---
You did not write this code and you are not trying to be nice to whoever did.
Given the current `git diff` against main and the feature's acceptance criteria:
1. Run the tests yourself. Do not trust any claim that they pass.
2. Look for: tests that assert nothing meaningful, deleted/skipped tests,
   swallowed exceptions, race conditions around the Redis script, missing
   Retry-After headers, contract drift from docs/contracts/.
3. Output: VERDICT: PASS | FAIL, then a numbered list of findings with file:line.
Record recurring patterns you find in your memory so you check for them next time.
```

**The maker/checker split** is the most important multi-agent pattern. An agent grading its own work is biased toward "looks good." A fresh context with an adversarial prompt and read-only tools catches far more. This is the same reason code review exists for humans.

**Common mistakes:** subagents for tiny tasks (the spin-up and summary overhead exceeds the savings); vague reports ("looks good!"), so demand a structured verdict; letting a reviewer have write access; chains of subagents passing lossy summaries (each hop loses information, like a game of telephone).

---

### 5.6 Hooks: Tripwires

| | |
|---|---|
| **What** | Commands (or HTTP calls, prompts, or MCP tool calls) that the harness runs on lifecycle events. Configured in `settings.json`. |
| **Problem solved** | Things that **must** happen, every time, regardless of what the model decides. |
| **Cost class** | **D (zero context).** Output only enters context if the hook chooses to feed something back. |
| **Key events** | `SessionStart` (inject a status summary), `UserPromptSubmit` (add context or block), `PreToolUse` (veto or modify a tool call before it runs), `PostToolUse` (format/lint after an edit), `Stop` (block "done" until conditions hold), `SubagentStop`, `PreCompact`, `Notification`. |
| **Control** | A hook script receives JSON on stdin describing the event. Exit code `0` = OK; exit code `2` = **block**, and stderr is fed back to the model as the reason, so it can correct itself. Hooks can also return structured JSON decisions. |

```json
{
  "hooks": {
    "PostToolUse": [
      { "matcher": "Edit|Write",
        "hooks": [{ "type": "command", "command": ".claude/hooks/format-and-build.sh" }] }
    ],
    "PreToolUse": [
      { "matcher": "Edit|Write",
        "hooks": [{ "type": "command", "command": ".claude/hooks/protect-sensors.sh" }] }
    ],
    "Stop": [
      { "hooks": [{ "type": "command", "command": ".claude/hooks/definition-of-done.sh" }] }
    ]
  }
}
```

The docs put it this way: CLAUDE.md is context, not enforced configuration. **To block an action regardless of what Claude decides, use a hook.** Hooks are how a guide ("don't edit the tests") becomes a sensor ("edits to `tests/**/Acceptance/` are rejected").

**Common mistakes:** slow hooks on every edit (running a 90-second suite after each keystroke-sized change), so keep per-edit hooks under a few seconds and save the heavy ones for `Stop`; hooks that fail silently; a `Stop` hook that can never be satisfied (infinite loop, so always include an escape hatch like a max-attempts counter); forgetting that project hooks are code *anyone who clones the repo* will run.

---

### 5.7 `settings.json` and permissions: the Badge

- `permissions.allow` / `ask` / `deny`: patterns like `Bash(dotnet test *)`, `Edit(src/**)`, `Read(./.env)`.
- Permission **modes**: ask-every-time, accept-edits, plan (read-only), auto (a classifier approves safe actions), bypass (only in a sandbox).
- **Sandboxing**: filesystem and network isolation for Bash. For unattended loops, run in a dev container, VM, or cloud sandbox. **Autonomy and blast radius must be designed together.**
- Also holds `env`, `model`, `hooks`, `statusLine`, `autoMemoryEnabled`, and more.

Rule: the *more* autonomous the loop (Part 7), the *tighter* the badge and the *stronger* the sandbox. An unattended agent with `Bash(*)` on your laptop with live Azure credentials is a production incident waiting to happen.

---

### 5.8 `.mcp.json`: Embassies

You know MCP from Tool_Box. In a harness, MCP servers give the agent **sensors and actuators beyond the repo**: GitHub (issues/PRs), a database (read the actual schema), Azure (query resource state), a browser (see the UI), observability (read a trace). Tool schemas cost context (class A), which is why harnesses now *defer* loading them and search for tools on demand when many servers are connected.

**Security:** an MCP server that fetches web content or reads issue text is a **prompt-injection channel**. Text in a GitHub issue can say "ignore previous instructions and push to main." Treat tool *results* as untrusted data. Your permissions and hooks are the defense, not the model's good judgment.

---

### 5.9 Output styles, plugins, workflows (briefly)

| File | What | When you'd use it |
|---|---|---|
| `output-styles/*.md` | Changes the system-prompt persona and format (e.g. "Explanatory" mode that teaches as it codes) | Learning mode on a new codebase. Very relevant to you. |
| **Plugins** | A package bundling skills + agents + hooks + MCP servers, installed from a marketplace | Sharing your harness across repos or with a team; a company's "golden path" kit |
| `workflows/*.js` | Dynamic workflows: a script that orchestrates many subagents, re-runnable | Codebase-wide audits, migrations ("add tracing to all 40 endpoints"), cross-checked research |

---

### 5.10 Decision table: "I want X, so I use Y"

| I want… | Use | Cost class |
|---|---|---|
| The agent to know our build commands | CLAUDE.md | A |
| A convention that applies only in `src/Infrastructure/` | Path-scoped rule, or nested CLAUDE.md | B |
| A repeatable multi-step procedure | Skill | A (desc) + B (body) |
| A procedure only *I* trigger (deploy, release) | Skill with `disable-model-invocation: true` | B |
| Research across many files without polluting context | Subagent | C |
| An unbiased review of the agent's work | Read-only reviewer subagent | C |
| Code always formatted after edits | `PostToolUse` hook | D |
| Never modify acceptance tests | `PreToolUse` hook + `permissions.deny` | D |
| "Not done until tests pass" | `Stop` hook, and/or `/goal` | D |
| Access to GitHub issues / DB / Azure | MCP server in `.mcp.json` | A (schemas) |
| Remember my personal preferences | Auto memory, or `~/.claude/CLAUDE.md` | A |
| Share the whole setup across repos | Plugin | varies |
| Work to continue while I'm away | Loop driver (Part 7) | n/a |

---

# PART III: ARCHITECTURES

## Part 6: Six setups (it's not one system)

There is no single "professional architecture." There's a family of setups, and they **stack**: each one adds a layer on top of the previous. The choice depends on how long the task is, how verifiable it is, and how much autonomy you can safely grant. Here they are from least to most autonomous.

### Setup A: Guarded Solo (interactive + harness)

```
   You ⇄ Claude Code session
            │
            ├── Binder (CLAUDE.md) + rules + skills           ← guides
            ├── Hooks: format/build on edit, protect tests     ← computational sensors
            └── Stop hook: tests must pass                     ← definition of done
```

- **What it is:** What you do today, plus a harness. You're still in the loop on every task, but the agent can't skip the checks.
- **Use when:** Always. This is the foundation every other setup builds on.
- **Files:** `CLAUDE.md`, `.claude/settings.json` (hooks, permissions), 1–3 skills.
- **Failure mode:** You become the bottleneck; everything waits for you to type.

### Setup B: Staged Pipeline (spec-driven development)

```
  ┌──────────┐    ┌──────────┐    ┌───────────┐    ┌──────────┐    ┌──────────┐
  │ RESEARCH │───►│   SPEC   │───►│   PLAN    │───►│IMPLEMENT │───►│ VERIFY   │
  │ subagent │    │ (human   │    │ (plan     │    │ one step │    │ skeptic  │
  │ explores │    │ approves)│    │ mode;     │    │ at a time│    │ subagent │
  │ codebase │    │          │    │ human     │    │ + hooks  │    │ + tests  │
  └──────────┘    └──────────┘    │ approves) │    └──────────┘    └────┬─────┘
       ▲                          └───────────┘                         │
       └──────────────── findings become rules / tests ◄────────────────┘
  Artifacts on disk: docs/specs/NNN-*.md → docs/plans/NNN-*.md → commits → docs/decisions/
```

- **What it is:** Your LLM_Monitor Phase 2 process, formalized. Each stage produces a **file** that is the input to the next stage, and humans approve at the gates that matter (spec and plan).
- **Why files between stages:** Context windows are temporary. Files persist, can be reviewed, diffed, and resumed by a fresh session. A plan in a file survives `/clear`. A plan in the conversation does not.
- **Use when:** Any non-trivial feature. This is the day-to-day professional default.
- **Files:** `docs/specs/`, `docs/plans/`, `docs/decisions/` (ADRs), skills named `/spec`, `/plan`, `/implement-step`, `/verify`, and a `skeptic` subagent.
- **Failure mode:** Ceremony for trivial changes. Allow a "small change" fast path.

### Setup C: Long-Running Harness (the shift-handover pattern)

This is Anthropic's published pattern for tasks too big for one context window (hours to days of agent work).

```
  SESSION 0: INITIALIZER AGENT (runs once)
    reads spec ─► writes feature_list.json (every feature, status: failing)
               ─► writes init.sh (how to start env + run tests)
               ─► writes progress.md (empty log)
               ─► initial commit

  SESSION 1..N: CODING AGENT (same prompt every time)
    1. Read progress.md + git log          ← "what happened on the last shift?"
    2. Run init.sh, run tests               ← "is the building still standing?"
       (if broken: fix that first)
    3. Pick ONE failing feature            ← small batch, clear target
    4. Implement + verify end to end
    5. Flip ONLY that feature's status → passing (never edit the definitions)
    6. Commit, append to progress.md       ← leave a clean handover
    7. Exit
```

- **Key insights:** (1) The agent's two classic failures are **one-shotting** (trying to build everything at once and running out of context halfway) and **premature victory** (declaring the project done). One feature per session fixes both. (2) The feature list is **JSON, not markdown**, because models are less inclined to casually rewrite structured data. The rule "you may only change the `passes` field" is enforceable by a hook. (3) Every session starts by **verifying the environment**, because the previous shift may have left it broken.
- **Use when:** Greenfield builds with a clear spec; large migrations.
- **Files:** `feature_list.json`, `progress.md`, `init.sh`, `.claude/agents/initializer.md`, `.claude/skills/next-feature/`.

### Setup D: Orchestrator–Workers (parallel multi-agent)

```
                      ┌──────────────────────┐
                      │  ORCHESTRATOR        │  holds the plan, never writes code
                      │  (main session)      │
                      └──────────┬───────────┘
          ┌──────────────────────┼──────────────────────┐
          ▼                      ▼                      ▼
   ┌─────────────┐        ┌─────────────┐        ┌─────────────┐
   │ worker A    │        │ worker B    │        │ worker C    │
   │ worktree/a  │        │ worktree/b  │        │ worktree/c  │   git worktrees:
   │ feature 7   │        │ feature 8   │        │ feature 9   │   isolated checkouts,
   └──────┬──────┘        └──────┬──────┘        └──────┬──────┘   no file collisions
          ▼                      ▼                      ▼
   ┌─────────────┐        ┌─────────────┐        ┌─────────────┐
   │ reviewer    │        │ reviewer    │        │ reviewer    │   independent checkers
   └──────┬──────┘        └──────┬──────┘        └──────┬──────┘
          └───────────────► PRs / merge queue ◄─────────┘    CI is the final judge
```

- **What it is:** Fan out independent work to parallel agents, each in its own worktree, each with its own reviewer, then merge through CI.
- **Mechanisms in Claude Code:** subagents with `isolation: worktree`, `--worktree` sessions, agent teams, agent view (a dashboard of all sessions), dynamic workflows.
- **Use when:** Work is truly **decomposable** (independent features, per-file migrations). This is the same judgment as parallelizing any distributed workload: parallelism only helps without shared mutable state.
- **Failure mode:** Merge conflicts and **integration drift**. Each piece passes alone and the whole fails. You also pay N× tokens. This is Amdahl's law with a credit card.

### Setup E: Headless / CI-Driven (the agent as a teammate)

```
  GitHub issue labeled "agent-ready"
        │
        ▼
  GitHub Actions workflow ──► claude -p "…"  (headless, Agent SDK, or Claude Code Action)
        │                          │
        │                          ├── same CLAUDE.md, skills, hooks as local
        │                          └── opens PR
        ▼
  CI runs full sensor suite ──► review agent comments on PR ──► HUMAN approves merge
```

- **What it is:** The agent runs *without a terminal*, triggered by events: an issue, a PR comment (`@claude`), a failing build, a schedule. Output lands as a PR that goes through the **same review gate as a human's PR**.
- **Mechanisms:** `claude -p` (print mode), the **Agent SDK** (Python/TypeScript, Claude Code as a library), Claude Code GitHub Actions, cloud **routines** (scheduled / API / GitHub-event-triggered runs), GitHub Copilot's cloud coding agent (assign an issue to Copilot, get a PR).
- **Why it matters for Microsoft:** This is the enterprise shape. The PR is the unit of trust, CI is the sensor, branch protection is the badge. AI work flows through the *existing* engineering system, not around it.
- **Failure mode:** Flood of low-quality PRs. Gate on labels, cap concurrency, and track merge rate.

### Setup F: Autonomous Loop (loop engineering)

```
  ┌────────────── SCHEDULER (cron / routine / /loop) ───────────────┐
  │                                                                  │
  │   TRIAGE agent: read issues, failing CI, TODOs, eval regressions │
  │        │  writes/prioritizes tasks on the board / in tasks.json  │
  │        ▼                                                         │
  │   WORK agent(s): take top task → Setup B or C inner process      │
  │        │                                                         │
  │        ▼                                                         │
  │   VERIFY: sensors + skeptic ──fail──► retry (bounded) or         │
  │        │                              escalate to human          │
  │        ▼ pass                                                    │
  │   PR ─► human merge (or auto-merge for a narrow, safe class)     │
  │        │                                                         │
  │   RETRO agent: what went wrong this cycle? propose harness       │
  │        changes (rule / test / hook / skill) as a PR ─────────────┼──► Part 8
  └──────────────────────────────────────────────────────────────────┘
```

- **What it is:** The whole cycle (find work, do work, check work, improve the process) runs on a schedule, with humans at the merge gate and the escalation queue.
- **Use when:** You have strong sensors, a sandbox, and a class of work that is verifiable. Examples: dependency bumps, flaky-test triage, lint debt, test-coverage gaps, docs sync.
- **Failure mode:** Everything in Part 7.

### 6.7 Choosing

| Question | If yes → |
|---|---|
| Is it one small change? | A |
| Is the behaviour ambiguous and does it need design decisions? | B (humans own the spec) |
| Is it big but well-specified, with objective acceptance tests? | C |
| Are there many independent pieces? | D (on top of B/C) |
| Should it be triggered by events and flow through PR review? | E |
| Is it recurring, low-judgment, and highly verifiable? | F |

**The key insight: autonomy should be proportional to verifiability.** You can let an agent run unattended exactly as far as your sensors can tell right from wrong without you.

---

## Part 7: Loop engineering

### 7.1 Loops all the way down

"Loop" is overloaded. There are at least six nested loops, each with its own timescale and its own "who decides to stop":

```
 ┌─ L6 META LOOP ────────── weeks ───── improve the harness itself (Part 8)
 │ ┌─ L5 PROJECT LOOP ───── days ────── triage → pick task → deliver → repeat
 │ │ ┌─ L4 SESSION LOOP ─── hours ───── one feature: read log → work → commit → handover
 │ │ │ ┌─ L3 TASK LOOP ──── minutes ─── implement → run sensors → fix → until green
 │ │ │ │ ┌─ L2 TOOL LOOP ── seconds ─── model → tool call → result → model (Part 3)
 │ │ │ │ │ ┌─ L1 TOKEN ─── ms ──────── model generates one token at a time
```

| Loop | Driver | Termination condition | Who designs it |
|---|---|---|---|
| L1 token | Model | end-of-turn token | Model vendor |
| L2 tool | Harness | model stops calling tools | Tool vendor (inner harness) |
| L3 task | Stop hook / `/goal` | sensors green, *or* budget exhausted | **You** |
| L4 session | Progress file + skill | one feature committed | **You** |
| L5 project | Scheduler / routine / CI | backlog empty, or daily budget hit | **You** |
| L6 meta | Retro step + human review | harness PR merged/rejected | **You** |

Prompt engineering lives at L2. **Loop engineering is designing L3 through L6.** That's why it is "the layer above the harness."

### 7.2 The Ralph loop (the simplest possible L4/L5)

Named by Geoffrey Huntley (after Ralph Wiggum's cheerful persistence), this is the "hello world" of loop engineering:

```bash
while :; do
  cat PROMPT.md | claude -p --permission-mode acceptEdits
done
```

`PROMPT.md` says something like: *"Read `progress.md` and `feature_list.json`. Pick the highest-priority failing item. Implement it. Run tests. If green, commit and update progress. If you're stuck after 3 attempts, write BLOCKED with details in progress.md and pick something else."*

Why it works at all: each iteration is a **fresh context** (no context rot), state lives entirely in **files and git** (durable, inspectable), and the prompt never changes (reproducible). Why it's dangerous: **no termination condition, no budget, no human gate.** Every professional loop is a Ralph loop plus the controls in 7.4.

### 7.3 Built-in loop drivers (Claude Code, Sept 2026)

| Driver | Loop level | What it does |
|---|---|---|
| **Stop hook** | L3 | Runs when the agent says it's done. It can refuse and send the agent back with a reason. |
| **`/goal`** | L3–L4 | Set a completion condition. Claude keeps working until it's met, a model judges it impossible, or an error you must fix occurs. |
| **`/loop`** | L4–L5 | Re-run a prompt on an interval, or self-paced, within a session. Good for polling CI or babysitting a deploy. |
| **Scheduled tasks / routines** | L5 | Cloud or desktop runs on a cron, on an API call, or on GitHub events, each in a fresh session. |
| **GitHub Actions / Agent SDK** | L5 | Event-driven, headless runs inside your CI system. |
| **Dynamic workflows** | L4–L5 | A re-runnable script that orchestrates many subagents. |
| **Channels** | L5 | Push external events (CI results, alerts, chat messages) *into* a running session. |

Codex has equivalents (e.g. `/goal`, automations), and Copilot has its cloud agent plus GitHub Actions. **The concepts transfer; the command names don't.**

### 7.4 The five controls every loop needs

Think of these like the controls on any distributed job system you'll build at work. An agent loop *is* a job system whose workers are non-deterministic.

| Control | Question | Implementation |
|---|---|---|
| **1. Termination** | How does it know it's done? | Objective predicate: tests green + skeptic PASS + feature flagged passing. Never "the agent says so." |
| **2. Budget** | What stops runaway cost? | Max iterations per task (e.g. 3 attempts), max turns (`maxTurns`), max dollars per day (API spend limits), wall-clock timeout. |
| **3. Backpressure** | What if work arrives faster than review? | Cap open agent PRs (e.g. ≤3). The triage agent stops creating tasks when the queue is full. Same idea as a bounded channel in .NET. |
| **4. Escalation** | What if it's stuck? | After N failed attempts, write `BLOCKED` with evidence, notify a human, and move on. This is a dead-letter queue: poison messages don't block the line. |
| **5. Isolation** | What's the blast radius? | Sandbox/container, worktree per task, no production credentials, branch protection, deny-list for destructive commands. |

If you want a mental hook: **a loop is a message queue consumer where the handler is an LLM.** Everything you'll learn about retries, idempotency, dead-letter queues, backpressure and poison messages applies directly. This lecture is quietly also practice for your distributed-systems weak spot.

### 7.5 What loop engineering demands of you

The point of Osmani's framing: loop engineering is **harder** than prompting, not easier, because automation amplifies whatever judgment you encoded. That includes the bad judgment. A sloppy prompt wastes one session. A sloppy loop wastes a hundred sessions overnight and opens forty PRs you have to read. Always start a new loop **supervised** (you watch every cycle), then **semi-supervised** (you review each PR), and only then **unattended** for the narrowest, most verifiable class of work.

---

## Part 8: Self-improving systems

### 8.1 Demystify first: what actually improves?

When people say "the AI improves itself as you use it," four very different things get lumped together:

| Mechanism | What changes | Who changes it | Is it real? |
|---|---|---|---|
| Model training | The weights | The vendor, months later | Not something you control. Your usage doesn't retrain the model in your session. |
| **Memory accumulation** | Notebook files (`MEMORY.md`) | The agent, automatically | Yes, but ungoverned |
| **Harness evolution** | Rules, skills, hooks, tests, agent prompts | The agent *proposes*, a human/eval *approves* | **Yes. This is the professional version.** |
| **Eval-driven optimization** | A skill's or prompt's text | An automated loop that scores variants against a test set | Yes, advanced |

So the accurate statement is: **the model stays the same; the files around it get better.** The system improves because each failure leaves behind a durable artifact (a rule, a test, a hook) that prevents that failure next time.

### 8.2 The ratchet

A ratchet only turns one way. That's the property you want: every lesson is locked in and can't slip back.

```
           ┌──────────────────────────────────────────────────────────┐
           │                                                          │
  work ──► │  FAILURE OBSERVED                                        │
           │  (sensor caught it, skeptic caught it, or you caught it) │
           │                     │                                    │
           │                     ▼                                    │
           │  CLASSIFY: why did it happen?                            │
           │   • didn't know X          → GUIDE:  rule / binder line  │
           │   • knew but forgot        → SENSOR: hook / lint / test  │
           │   • didn't know the steps  → SKILL:  new or sharper skill │
           │   • checked badly          → SENSOR: improve the skeptic │
           │   • one-off / my fault     → nothing (don't overfit!)    │
           │                     │                                    │
           │                     ▼                                    │
           │  PROPOSE harness change as a diff (PR / reviewable file) │
           │                     │                                    │
           │                     ▼                                    │
           │  GATE: human review and/or eval shows it helps           │
           │                     │                                    │
           │                     ▼                                    │
           │  MERGE → harness is permanently better ──────────────────┼──► next work
           └──────────────────────────────────────────────────────────┘
```

Note the preference order hidden in "classify": **push the fix as far down the cost-class table (3.2) as possible.** A new lint rule beats a new binder line, because it costs zero context and can't be ignored.

This philosophy has a name in the community: **compound engineering**. Each unit of work should make the next unit easier, instead of the usual pattern where each feature makes the codebase harder to change.

### 8.3 Concrete mechanisms (from simplest to most rigorous)

1. **Auto memory** (free, automatic, local, unreviewed). Fine for personal preferences.
2. **"Update the binder" habit.** When you correct the agent, end with: *"Add a line to CLAUDE.md (or the right rule file) so this doesn't happen again."* Review the diff.
3. **A `/retro` skill** run at the end of each feature or session. It reads the session's corrections, failed sensor runs and skeptic findings; classifies each with the table above; and writes **proposed** harness diffs into `harness/proposals/NNN.md` or a branch. You approve.
4. **Reviewer memory.** The skeptic subagent keeps its own memory of recurring defect patterns, so it gets sharper at *this* codebase over time.
5. **Harness metrics.** Log per-task outcomes (first-pass success? how many iterations? human corrections? cost?) to `harness/metrics.jsonl`. Now "is the harness improving?" is a *measurable* question, not a vibe.
6. **Evals for the harness itself.** Keep a small set of **benchmark tasks** (e.g. "add a new rate-limit algorithm," "fix this seeded bug") with known-good outcomes. When a proposed harness change lands, re-run them. If first-pass success drops, reject the change. This is *exactly* the regression-gated eval harness on your LLM_Monitor roadmap, pointed at your own development process instead of at a RAG pipeline. (Claude Code's `claude plugin eval` and Anthropic's `skill-creator` do a version of this for skills and plugins.)

### 8.4 Why governance matters: how self-improvement goes wrong

| Failure | What it looks like | Defense |
|---|---|---|
| **Rule bloat / context rot** | Binder grows to 900 lines. Adherence *drops*. | Size budget (a CI check that fails if CLAUDE.md > 200 lines); periodic "garbage-collection" pass that merges and deletes rules |
| **Overfitting** | A one-off mistake becomes a permanent, oddly specific rule | "One-off" is a valid classification; require 2 occurrences before a new rule |
| **Contradiction** | Two rules disagree; the model picks either one | Retro skill must search existing rules before adding one |
| **Memory poisoning / prompt injection** | Text from an issue or web page gets saved as a "memory" or rule | Harness changes only via reviewed diff; never auto-merge harness edits from sessions that read untrusted input |
| **Sensor erosion** | Agent "fixes" failures by weakening tests | Protected paths (5.6), test-count/coverage ratchets in CI, skeptic checks for skipped/deleted tests |
| **Goodhart's law** | Metric improves, quality doesn't (e.g. first-pass success rises because tasks got easier) | Fixed benchmark set; review metrics with judgment |

### 8.5 Protecting the sensors (reward hacking)

When the goal is "make tests pass," the shortest path is sometimes to *change the tests*. Models do this, not out of malice but because it satisfies the literal objective. Defenses, layered:

1. **Separate the files:** human-owned acceptance tests in `tests/Acceptance/` vs agent-writable unit tests elsewhere.
2. **PreToolUse hook** rejects edits to protected paths (exit code 2 with a reason).
3. **CI ratchets:** test count may not decrease; coverage may not drop; `[Skip]` attributes fail the build.
4. **The skeptic** explicitly checks `git diff` for weakened assertions.
5. **Feature list:** the agent may flip `passes` but a hook verifies the `steps`/`description` fields are unchanged.

---

## Part 9: Portability and the Microsoft angle

You're learning on Claude Code. At Microsoft you'll very likely use **GitHub Copilot** (Microsoft owns GitHub; Copilot can also run Claude models), possibly alongside other agents. The good news: the industry converged. Concepts map one-to-one and some files are literally shared.

| Concept | Claude Code | GitHub Copilot | OpenAI Codex |
|---|---|---|---|
| Always-on binder | `CLAUDE.md` (also reads `AGENTS.md`) | `.github/copilot-instructions.md`, `AGENTS.md` | `AGENTS.md` |
| Scoped rules | `.claude/rules/*.md` with `paths:` | `.github/instructions/*.instructions.md` with `applyTo:` | nested `AGENTS.md` |
| Skills | `.claude/skills/` | `.github/skills/`, `.claude/skills/`, `.agents/skills/` (**Agent Skills open standard**) | `.agents/skills/` |
| Reusable prompts | skills / `.claude/commands/` | `.github/prompts/*.prompt.md` | prompts / skills |
| Subagents / custom agents | `.claude/agents/*.md` | `.github/agents/*.agent.md` (custom agents) | `.codex/agents/` |
| Tool servers | `.mcp.json` | MCP config (VS Code / repo settings) | `config.toml` MCP section |
| Headless / cloud | `claude -p`, Agent SDK, GitHub Actions, routines | Copilot cloud coding agent (issue → PR), Actions | `codex exec`, cloud tasks |

*(Verify exact paths against current docs before relying on them. This row set is the part of the lecture most likely to drift.)*

**Portable-harness strategy for your own projects:**

- Put the cross-tool truth in **`AGENTS.md`**, and have `CLAUDE.md` be a few lines plus `@AGENTS.md`.
- Put skills where multiple tools look (`.claude/skills/` is read by both Claude Code and Copilot).
- Put **enforcement in tool-neutral places**: tests, analyzers, CI, git hooks, `Directory.Build.props` with warnings-as-errors. These work no matter which agent, or which human, touches the code. This is the most important portability point: **sensors in CI are vendor-neutral. Hooks inside one agent are not.**

**On your company's zero-AI policy:** Keep this practice strictly on personal projects, personal accounts, and personal hardware. Never paste work code into these tools. It's worth knowing that many enterprises govern AI with *exactly* these mechanisms (managed settings, deny rules, MCP allowlists, data-retention controls, gateways). Understanding the governance side is itself a talking point when you interview at a company that has *solved* the policy question rather than banned the tools.

---

# PART IV: PRACTICE

## Part 10: Project ideas

### 10.1 What makes a good harness-practice project?

Not every project teaches this well. The criteria, in priority order:

1. **Verifiable.** Correctness can be checked by a program. This is the #1 criterion because autonomy is capped by verifiability (6.7). A project with fuzzy "does it look right?" outcomes teaches you prompting, not harnessing.
2. **Decomposable.** Naturally splits into 15–40 independent-ish features, so you can practice feature lists, one-feature-per-session, and parallelism.
3. **Greenfield (at first).** You design the harness from commit #1 instead of retrofitting. (Brownfield comes second, and it's what real jobs look like.)
4. **On your learning path.** The *product* should teach you backend and distributed-systems skills from your persona, so every hour counts twice.
5. **Azure-flavored** where it's natural, per your Microsoft SE2 strategy.
6. **Evenings-sized.** A working v1 in 4–8 weeks part-time.

### 10.2 The candidates

| # | Project | What you build | Backend / DS skills | Harness skills practiced | Verifiability | Azure angle | Size |
|---|---|---|---|---|---|---|---|
| 1 | **Tollgate** ★ | Distributed rate-limiting & quota service (ASP.NET Core + Redis), pluggable into YARP | Atomicity, race conditions, Lua scripting in Redis, clock skew, algorithms (token bucket, GCRA, sliding window), load testing, API design | Everything: feature list, skeptic, protected acceptance tests, property tests as sensors, Stop hook, parallel algorithm work, CI agent, ratchet | **Very high.** "Never admit more than N per window" is a mathematical invariant | Azure Container Apps, Azure Managed Redis, Key Vault, managed identity, App Insights | M |
| 2 | **LLM_Monitor harness retrofit** | Add a professional harness to your existing project | Whatever the roadmap needs next (OpenAI facade, SSE, evals) | **Brownfield**: writing an AGENTS.md for existing code, path-scoped rules per service (C# vs Python), protecting an existing test suite, subagent that knows the contract doc | Medium (existing tests are uneven) | Already Azure-bound (plans 003–004) | S–M |
| 3 | **Courier** | Event-driven order/notification pipeline: outbox pattern, idempotent consumers, retries, DLQ | Pub/sub, at-least-once delivery, idempotency, eventual consistency, sagas | Integration-test sensors with Testcontainers, the architecture-fitness harness, "the loop is a queue consumer" made literal | Medium-high (needs careful integration tests) | **Azure Service Bus**, Azure Functions, Cosmos DB change feed | M–L |
| 4 | **MiniRaft KV** | Replicated key-value store with Raft consensus and a **deterministic simulation** test harness | Consensus, leader election, log replication, partitions, linearizability | Setup C at its purest: a spec (the Raft paper) → feature list → one feature per session; a linearizability checker as the ultimate sensor | **Extremely high** (simulation + checker) but hard to build the checker | AKS deployment as a stretch | L |
| 5 | **Repo Janitor** | Your *own* loop-engineering product: an Agent-SDK service that nightly triages your repos and opens PRs (dependency bumps, flaky tests, doc drift) | Background workers, scheduling, idempotent jobs, GitHub API | You build the **inner** harness: tool definitions, permissions, budgets, escalation, metrics. Setup E + F from the builder's side | Medium (success = merged PR rate) | Azure Functions timer trigger or Container Apps Jobs, Key Vault for tokens | M |
| 6 | **Lecture Forge** | A harness for *this* Studying_Lectures repo: skills that generate lectures from persona + source project, a fact-check subagent, a structure linter | Minimal backend | Skills, skill evals, inferential sensors for non-code output, the `/retro` ratchet on writing quality | Low-medium (inferential) | none | S |

### 10.3 Recommended order

```
  Week 1–2   Project 6 (Lecture Forge): low stakes, learn skills/subagents/hooks mechanics
  Week 2–8   Project 1 (Tollgate):      the main event, all six setups, full ratchet
  Week 8+    Project 2 (LLM_Monitor):   apply the Tollgate harness to brownfield code
  Later      Project 3 or 4:            the distributed-systems deep end, with a mature harness
             Project 5:                  once you've *used* loops, build one
```

### 10.4 Why Tollgate is the best candidate

- **The invariant is the sensor.** A rate limiter has a crisp, checkable contract: across *any* interleaving of concurrent requests, across *any* number of API instances, a key with limit N per window never admits more than N (plus a documented tolerance for approximate algorithms). You can check that with property-based tests, concurrency tests against real Redis, and load tests. **The agent can't talk its way past math.**
- **It decomposes perfectly.** Five algorithms, each an independent feature with the same interface. That's a natural fit for the feature list (Setup C) *and* for parallel workers (Setup D).
- **It lands on your weak spots.** Race conditions, atomicity, clock skew, distributed coordination, performance. These are exactly the persona's "Asynchronous Programming" and "Distributed Systems" gaps. And you already know YARP, so plugging Tollgate in as gateway middleware connects to prior study.
- **It's Microsoft-relevant.** ASP.NET Core ships `System.Threading.RateLimiting`. Azure API Management has rate-limit policies. You'll be able to say *why* you'd use or not use each.
- **It's honest about scope.** It's a small service, so the harness is the star, not a sprawling product.

---

## Part 11: Tollgate, fully set up

### 11.1 The product (brief, since the harness is the point)

```
                     ┌───────────────────────────────────────────┐
  client ──HTTP────► │  YARP gateway (your existing knowledge)   │
                     │  └─ Tollgate middleware (Tollgate.Yarp)   │──┐
                     └───────────────────────────────────────────┘  │ in-proc call
                                                                    │  or HTTP
  admin / other ──HTTP──► ┌───────────────────────────────┐ ◄───────┘
  services                │  Tollgate.Api (ASP.NET Core)  │
                          │  POST /v1/check               │
                          │  CRUD /v1/policies            │
                          └──────────────┬────────────────┘
                                         │ ILimiterStore
                          ┌──────────────┴───────────────┐
                          │  Tollgate.Core               │  pure algorithms:
                          │  TokenBucket, Gcra,          │  (state, now, cost) → (decision, state')
                          │  FixedWindow, SlidingLog,    │  no I/O, injected IClock
                          │  SlidingWindowCounter        │
                          └──────────────┬───────────────┘
                                         │
                          ┌──────────────┴───────────────┐
                          │  Tollgate.Redis              │  each algorithm = one Lua script
                          │  (atomic via EVALSHA)        │  → atomic read-modify-write
                          └──────────────┬───────────────┘
                                         ▼
                                Azure Managed Redis
  Telemetry: OpenTelemetry → Azure Monitor / App Insights (decision counts, p99 latency)
```

**Contract** (`docs/contracts/http-api.md`, snake_case on the wire, same convention as LLM_Monitor):

```http
POST /v1/check
{ "tenant_id": "acme", "key": "user:42", "policy": "api-default", "cost": 1 }

200 OK
{ "allowed": true, "remaining": 41, "reset_after_ms": 12000, "retry_after_ms": null }
```

**Key design choice to notice:** `Tollgate.Core` algorithms are **pure functions** with an injected clock. That is a *harnessability* decision (4.4). Pure functions are trivially property-testable, so the agent gets a fast, deterministic sensor for the hardest logic. The Redis layer then only has to prove it's an **atomic, faithful translation** of the pure version, and there's a test that runs both side by side (a *differential test*).

### 11.2 Harness design: the 2×2 for Tollgate

|  | Computational | Inferential |
|---|---|---|
| **Guide** | Solution template with project references pre-wired (Core cannot reference Redis); `ILimiterAlgorithm` interface every algorithm must implement; OpenAPI generated from code and diffed against contract; `permissions.deny` | `AGENTS.md` map; path-scoped rules for Core/Redis/Api; skills `/next-feature`, `/add-algorithm`, `/retro`; ADRs in `docs/decisions/` |
| **Sensor** | `dotnet build -warnaserror` + analyzers; unit + **property tests** (FsCheck) on Core; **differential tests** Core vs Redis; concurrency tests on real Redis (Testcontainers); **human-owned acceptance tests**; architecture tests (NetArchTest); load test with a p99 budget; CI ratchets | `skeptic` subagent (adversarial review of diff); `contract-reviewer` subagent (API drift); weekly harness-eval run |

### 11.3 The file tree

```
tollgate/
├── AGENTS.md                          ← the map (cross-tool binder, ~100 lines)
├── CLAUDE.md                          ← 5 lines: "@AGENTS.md" + Claude-specific notes
├── CLAUDE.local.md                    ← (gitignored) your personal notes
├── .mcp.json                          ← github MCP (issues/PRs); nothing with prod creds
├── .worktreeinclude                   ← copies .env.local into agent worktrees
│
├── .claude/
│   ├── settings.json                  ← permissions + hooks (committed)
│   ├── settings.local.json            ← (gitignored) personal overrides
│   ├── rules/
│   │   ├── core-purity.md             ← paths: src/Tollgate.Core/**  (no I/O, IClock only)
│   │   ├── redis-scripts.md           ← paths: src/Tollgate.Redis/** (atomicity, key naming, TTLs)
│   │   ├── api-endpoints.md           ← paths: src/Tollgate.Api/**   (Results<>, ProblemDetails)
│   │   └── testing.md                 ← paths: tests/**             (naming, what "good test" means)
│   ├── skills/
│   │   ├── next-feature/SKILL.md      ← the shift-handover procedure (Setup C)
│   │   ├── add-algorithm/
│   │   │   ├── SKILL.md
│   │   │   └── references/algorithm-checklist.md
│   │   ├── spec/SKILL.md              ← turn an idea into docs/specs/NNN (Setup B)
│   │   ├── retro/SKILL.md             ← the ratchet (Part 8)
│   │   └── release/SKILL.md           ← disable-model-invocation: true (manual only)
│   ├── agents/
│   │   ├── initializer.md             ← one-time: spec → feature_list.json + init.sh
│   │   ├── skeptic.md                 ← read-only adversarial reviewer, has memory
│   │   ├── contract-reviewer.md       ← checks API/contract drift
│   │   └── researcher.md              ← explores docs/code, returns summaries (cheap model)
│   ├── hooks/
│   │   ├── session-start.sh           ← injects: last 20 lines of progress.md + failing count
│   │   ├── format-on-edit.sh          ← PostToolUse: dotnet format whitespace on the file
│   │   ├── protect-sensors.sh         ← PreToolUse: block edits to protected paths
│   │   └── definition-of-done.sh      ← Stop: build + tests green or send back (max 3)
│   └── agent-memory/skeptic/          ← the skeptic's own accumulated patterns
│
├── harness/                           ← the harness's own state & metrics (committed)
│   ├── feature_list.json              ← source of truth for what's done
│   ├── progress.md                    ← shift log (append-only)
│   ├── metrics.jsonl                  ← one line per agent task (Part 8.3 #5)
│   ├── proposals/                     ← /retro output: proposed harness changes
│   ├── benchmarks/                    ← harness-eval tasks (Part 8.3 #6)
│   │   ├── B01-seeded-race-condition/
│   │   └── B02-add-leaky-bucket/
│   ├── init.sh                        ← start Redis container, restore, build, smoke test
│   ├── mark-passing.sh                ← the ONLY way to flip a feature to passing
│   └── loop.sh                        ← the bounded Ralph loop (Phase 5)
│
├── docs/
│   ├── architecture.md                ← the one-page picture (11.1)
│   ├── contracts/http-api.md          ← wire contract, source of truth
│   ├── specs/001-tollgate-v1.md       ← human-written spec
│   ├── plans/                         ← per-feature plans (Setup B)
│   └── decisions/                     ← ADRs: 0001-pure-core.md, 0002-lua-for-atomicity.md …
│
├── src/
│   ├── Tollgate.Core/                 ← pure algorithms
│   ├── Tollgate.Redis/                ← Lua scripts + store
│   │   └── Scripts/*.lua
│   ├── Tollgate.Api/
│   └── Tollgate.Yarp/                 ← gateway middleware
│
├── tests/
│   ├── Tollgate.Core.Tests/           ← unit + FsCheck property tests (agent-writable)
│   ├── Tollgate.Redis.Tests/          ← differential + concurrency (Testcontainers)
│   ├── Tollgate.Architecture.Tests/   ← NetArchTest layer rules (PROTECTED)
│   ├── Tollgate.Acceptance.Tests/     ← human-owned behaviour spec (PROTECTED)
│   └── load/                          ← k6 or NBomber scenarios + p99 budget
│
├── infra/                             ← Bicep: Container Apps, Managed Redis, Key Vault
├── Directory.Build.props              ← TreatWarningsAsErrors, analyzers, nullable
├── .editorconfig
└── .github/
    ├── workflows/
    │   ├── ci.yml                     ← build, test, arch, ratchets, contract diff (PROTECTED)
    │   ├── agent-nightly.yml          ← Setup E/F: headless agent on "agent-ready" issues
    │   └── deploy.yml                 ← OIDC to Azure, manual approval environment
    └── copilot-instructions.md        ← one line: "Read AGENTS.md" (portability)
```

Look at *where* things live, because the placement itself is the lesson:

- **Protected sensors** (`Acceptance.Tests`, `Architecture.Tests`, `ci.yml`, `feature_list.json` definitions, `.claude/hooks/`) are the **constitution**. The agent can read them but not amend them. You own them.
- **`harness/`** is committed. The process state is part of the repo, reviewable and diffable, just like code.
- **`AGENTS.md` is the map, `docs/` is the territory.** The binder points to depth, it doesn't contain it.

### 11.4 The key files

#### `AGENTS.md` (the map)

```markdown
# Tollgate

Distributed rate-limiting service. ASP.NET Core 10 API + pure C# algorithm core + Redis (Lua) store.
One-page architecture: docs/architecture.md. Wire contract (source of truth): docs/contracts/http-api.md.

## Commands
- Setup / verify env:  ./harness/init.sh
- Build:               dotnet build -warnaserror
- Fast tests:          dotnet test --filter "Category!=Integration&Category!=Load"
- All tests:           dotnet test            (needs Docker for Testcontainers)
- Load test:           ./tests/load/run.sh    (p99 budget: 5 ms at 5k rps local)

## Layering (enforced by tests/Tollgate.Architecture.Tests)
Core → nothing.  Redis → Core.  Api → Core, Redis.  Yarp → Core.
Core is pure: no I/O, no DateTime.Now. Time comes from IClock.

## Definition of done (a feature is done only when ALL hold)
1. Build has zero warnings. 2. All non-load tests green.
3. Feature's acceptance tests green, verified via ./harness/mark-passing.sh <id>.
4. skeptic subagent verdict: PASS. 5. Contract doc updated if the wire changed.
6. progress.md appended; one commit per feature, message "feat(<id>): …".

## Boundaries
- NEVER edit: tests/Tollgate.Acceptance.Tests, tests/Tollgate.Architecture.Tests,
  .github/workflows, .claude/hooks, feature definitions in harness/feature_list.json.
  If one of these seems wrong, STOP and write the reason in progress.md under "NEEDS HUMAN".
- No new NuGet packages without an ADR in docs/decisions/.

## Where to look
- Adding an algorithm → skill `add-algorithm`
- Next task in the backlog → skill `next-feature`
- Why is X designed this way → docs/decisions/
```

~45 lines. Everything else is a pointer.

#### `.claude/rules/redis-scripts.md` (path-scoped guide)

```markdown
---
paths:
  - "src/Tollgate.Redis/**"
---
# Redis store rules
- One algorithm = one Lua script in Scripts/. All read-modify-write happens INSIDE the script.
  Never GET then SET from C#: that is a race (two instances read the same count).
- Use Redis server time (`redis.call('TIME')`) inside scripts, never the caller's clock.
  Reason: API instances' clocks skew. See docs/decisions/0003-server-time.md.
- Keys: `tg:{tenant}:{policy}:{key}` with a hash tag {tenant} so a tenant's keys co-locate in cluster mode.
- Every key gets a TTL ≥ window length. No immortal keys.
- A new script requires a differential test against the Core implementation.
```

Notice how each rule carries its **reason**. Rules with reasons generalize: the agent can apply the *principle* to cases the rule didn't list. Rules without reasons get followed literally or not at all.

#### `.claude/skills/next-feature/SKILL.md` (the shift procedure)

```markdown
---
name: next-feature
description: Use at the start of an autonomous work session, or when asked to "pick up the next task". Implements exactly ONE feature from harness/feature_list.json end to end.
---
# Next feature (one shift)

1. Orient: read harness/progress.md (last 40 lines) and `git log --oneline -15`.
2. Verify the building: run ./harness/init.sh. If anything fails, fixing it IS this shift's task.
3. Choose: the first feature in feature_list.json with "passes": false, not "blocked",
   whose "depends_on" are all passing.
4. Plan: write docs/plans/<id>.md (≤30 lines: approach, files, risks, tests to add).
5. Implement test-first. Run fast tests after each meaningful step.
6. Verify: ./harness/mark-passing.sh <id>  (runs that feature's acceptance tests; flips the flag only if green).
7. Review: dispatch the `skeptic` subagent. If FAIL, fix and re-run from step 6 (max 2 rounds).
8. Hand over: append to progress.md (template below), commit "feat(<id>): <summary>".
9. Stop. Do not start a second feature.

If stuck after 3 genuine attempts at any step: set "blocked": true with a reason via
./harness/mark-blocked.sh <id> "<reason>", append to progress.md, commit, stop.

## progress.md entry template
### <date> <id> <PASS|BLOCKED>
- Did: …
- Decisions: … (link ADR if any)
- Surprises / things the next shift should know: …
- Harness friction (for /retro): …
```

That last line, "Harness friction", is the seed of the ratchet. Every shift reports what made the job harder, and `/retro` harvests it.

#### `.claude/agents/skeptic.md`

(See 5.5 for the full form.) Tollgate-specific additions to its checklist: *check for GET-then-SET races in C#, `DateTime.UtcNow` in Core, missing TTLs, off-by-one at window boundaries, `retry_after_ms` null when `allowed=false`, tests that only exercise a single thread.*

#### `harness/feature_list.json` (excerpt)

```json
[
  {
    "id": "F-001",
    "category": "core",
    "description": "Fixed-window algorithm in Core with IClock",
    "steps": [
      "Given limit 5/10s, 5 requests at t=0 are allowed, the 6th denied",
      "At t=10s the window resets and requests are allowed again",
      "Denied responses carry retry_after_ms = time until window reset"
    ],
    "acceptance_filter": "FullyQualifiedName~Acceptance.FixedWindow",
    "depends_on": [],
    "passes": false,
    "blocked": false
  },
  {
    "id": "F-007",
    "category": "redis",
    "description": "Token bucket is atomic under concurrency on real Redis",
    "steps": [
      "1000 concurrent requests from 8 client connections against limit 100 admit exactly 100",
      "Differential test: Redis decisions equal Core decisions for 10k random sequences"
    ],
    "acceptance_filter": "FullyQualifiedName~Acceptance.TokenBucketRedis",
    "depends_on": ["F-004"],
    "passes": false,
    "blocked": false
  }
]
```

#### `harness/mark-passing.sh` (a computational gate on state changes)

```bash
#!/usr/bin/env bash
# The only sanctioned way to flip a feature to passing. The agent may call it; it may not edit the JSON.
set -euo pipefail
id="$1"
filter=$(jq -r --arg id "$id" '.[] | select(.id==$id) | .acceptance_filter' harness/feature_list.json)
[ -n "$filter" ] || { echo "unknown feature $id" >&2; exit 1; }

results=harness/.results; rm -rf "$results"
dotnet test tests/Tollgate.Acceptance.Tests --filter "$filter" \
  --results-directory "$results" --logger "trx;LogFileName=$id.trx"

# Refuse a vacuous pass: the filter must have matched at least one test.
executed=$(grep -o 'executed="[0-9]*"' "$results/$id.trx" | head -1 | tr -dc '0-9')
[ "${executed:-0}" -gt 0 ] || { echo "filter matched zero tests: refusing" >&2; exit 1; }

tmp=$(mktemp)
jq --arg id "$id" 'map(if .id==$id then .passes=true else . end)' harness/feature_list.json > "$tmp"
mv "$tmp" harness/feature_list.json
echo "$id marked passing ($executed acceptance tests)"
```

The "filter matched zero tests" check is your CI-with-zero-dependencies lesson turned into code: **a sensor that checks nothing must fail, not pass.**

#### `.claude/hooks/protect-sensors.sh` (PreToolUse)

```bash
#!/usr/bin/env bash
# stdin: JSON describing the pending tool call. Exit 2 = block; stderr goes back to Claude.
path=$(jq -r '.tool_input.file_path // empty')
case "$path" in
  *tests/Tollgate.Acceptance.Tests/*|*tests/Tollgate.Architecture.Tests/*|\
  *.github/workflows/*|*.claude/hooks/*|*harness/feature_list.json)
    echo "BLOCKED: $path is a protected sensor/constitution file. If you believe it is wrong, \
write the reason under 'NEEDS HUMAN' in harness/progress.md and stop." >&2
    exit 2 ;;
esac
exit 0
```

Defense in depth: this hook catches `Edit`/`Write`. A determined agent could still use `Bash(sed …)`. So **CI** also verifies that protected paths are unchanged in agent PRs (a `git diff --name-only origin/main` check), and branch protection requires your review. No single layer is trusted alone. Sound familiar? It's the same thinking as network security in your YARP lectures.

#### `.claude/hooks/definition-of-done.sh` (Stop)

```bash
#!/usr/bin/env bash
# Runs when Claude tries to end its turn. Exit 2 = "not done", stderr = why.
state=.claude/state; mkdir -p "$state"

# Only gate if code actually changed; don't run a build because Claude asked a question.
[ -z "$(git status --porcelain -- src tests)" ] && { rm -f "$state/stop_attempts"; exit 0; }

n=$(( $(cat "$state/stop_attempts" 2>/dev/null || echo 0) + 1 ))
echo "$n" > "$state/stop_attempts"
if [ "$n" -gt 3 ]; then                       # escape hatch: never loop forever
  printf '\n### %s STOP-GATE GAVE UP after 3 attempts\n' "$(date -u +%FT%TZ)" >> harness/progress.md
  rm -f "$state/stop_attempts"; exit 0
fi

if ! out=$(dotnet build -warnaserror -v q 2>&1); then
  echo "Not done: build failed:"$'\n'"$(echo "$out" | grep -E 'error|warning' | head -20)" >&2; exit 2
fi
if ! out=$(dotnet test --no-build --filter "Category!=Load" 2>&1); then
  echo "Not done: tests failing:"$'\n'"$(echo "$out" | grep -E 'Failed |Error Message' | head -30)" >&2; exit 2
fi
rm -f "$state/stop_attempts"; exit 0
```

Three professional details worth copying: (1) **gate only when relevant** (no code change, no gate); (2) **bounded retries with an escape hatch** that leaves a trace for a human; (3) **feed back a trimmed, actionable error**, not 4,000 lines of build log, because that output lands on Sage's Desk.

#### `.claude/settings.json`

```json
{
  "permissions": {
    "allow": [
      "Bash(dotnet build*)", "Bash(dotnet test*)", "Bash(dotnet format*)",
      "Bash(./harness/init.sh)", "Bash(./harness/mark-passing.sh *)", "Bash(./harness/mark-blocked.sh *)",
      "Bash(git status*)", "Bash(git diff*)", "Bash(git log*)", "Bash(git add*)", "Bash(git commit*)"
    ],
    "deny": [
      "Bash(git push*)", "Bash(az *)", "Bash(rm -rf*)", "Bash(curl*)",
      "Read(./.env*)", "Edit(./tests/Tollgate.Acceptance.Tests/**)", "Edit(./.github/workflows/**)"
    ]
  },
  "hooks": {
    "SessionStart": [{ "hooks": [{ "type": "command", "command": ".claude/hooks/session-start.sh" }] }],
    "PreToolUse":   [{ "matcher": "Edit|Write", "hooks": [{ "type": "command", "command": ".claude/hooks/protect-sensors.sh" }] }],
    "PostToolUse":  [{ "matcher": "Edit|Write", "hooks": [{ "type": "command", "command": ".claude/hooks/format-on-edit.sh" }] }],
    "Stop":         [{ "hooks": [{ "type": "command", "command": ".claude/hooks/definition-of-done.sh" }] }]
  }
}
```

Note `git push` and `az` are **denied**. The agent never touches the remote or Azure directly. Pushing happens in `loop.sh` (under your control) and deployment happens in `deploy.yml` via OIDC with a manual-approval environment. **The agent writes code. The pipeline ships it. You approve it.** *(Check the exact permission-pattern syntax against the current permissions docs. Path-pattern semantics have changed across versions.)*

#### `harness/loop.sh` (Phase 5: a Ralph loop with the five controls)

```bash
#!/usr/bin/env bash
# Bounded, backpressured, isolated, metered loop. Run inside the dev container, never on the host.
set -euo pipefail
MAX_ITER=${MAX_ITER:-4}          # control 2: budget (iterations)
MAX_OPEN_PRS=${MAX_OPEN_PRS:-3}  # control 3: backpressure

for i in $(seq 1 "$MAX_ITER"); do
  open=$(gh pr list --label agent --state open --json number | jq length)
  [ "$open" -ge "$MAX_OPEN_PRS" ] && { echo "backpressure: $open agent PRs awaiting review"; break; }

  jq -e '[.[] | select(.passes==false and .blocked==false)] | length > 0' harness/feature_list.json >/dev/null \
    || { echo "backlog empty"; break; }                            # control 1: termination

  branch="agent/$(date +%Y%m%d-%H%M%S)"
  wt="../tollgate-wt/$branch"
  git worktree add -q -b "$branch" "$wt" origin/main               # control 5: isolation

  start=$(date +%s)
  ( cd "$wt" && claude -p "/next-feature" --max-turns 80 --output-format json > .run.json ) || true
  ( cd "$wt"
    jq -c --arg b "$branch" --argjson secs $(( $(date +%s) - start )) \
      '{branch:$b, cost_usd:.total_cost_usd, turns:.num_turns, secs:$secs, is_error:.is_error}' \
      .run.json >> "$OLDPWD/harness/metrics.jsonl"                 # metrics for the ratchet
    if git log origin/main..HEAD --oneline | grep -q .; then
      git push -q -u origin "$branch"
      gh pr create --label agent --fill                            # human gate: PR review
    fi )                                                           # control 4: blocked items stay in progress.md
  git worktree remove --force "$wt"
done
```

### 11.5 A day in the life (once set up)

```
 Morning (you, 20 min)
   ├─ Review agent PRs from overnight. Merge / request changes / close.
   ├─ Read "NEEDS HUMAN" and BLOCKED entries in progress.md. Make the judgment calls.
   └─ Skim harness/proposals/ from /retro. Accept ~1, reject the rest.

 Evening (you + agent, 1–2 h)
   ├─ Spec the next chunk with /spec (you drive; this is the behaviour harness).
   ├─ Write/approve its acceptance tests YOURSELF. The part you never delegate.
   ├─ Let /next-feature run interactively (Setup A/B) while you watch the first one.
   └─ Deep-dive one thing the agent did that you don't understand → lecture note.

 Overnight (loop, unattended, sandboxed)
   └─ harness/loop.sh, MAX_ITER=4 → ≤3 PRs waiting for you in the morning.

 Weekly (you, 30 min)
   ├─ /retro over the week's progress.md + metrics.jsonl.
   ├─ Run harness benchmarks. Did last week's harness changes help?
   └─ Garbage-collect: prune AGENTS.md/rules; promote good auto-memory to rules.
```

Notice where *your* time goes: **specs, acceptance tests, judgment calls, harness review, learning.** That's the tech-lead job. Typing implementation code has mostly left the list.

### 11.6 The phased roadmap

Each phase introduces one setup from Part 6. **Don't skip ahead.** Each layer assumes the one beneath it is solid.

| Phase | Setup | Build | Exit criteria | You'll learn |
|---|---|---|---|---|
| **0: Foundations** (by hand, mostly) | none | Solution skeleton, project references, `Directory.Build.props`, `IClock`, `ILimiterAlgorithm`, architecture tests, CI with ratchets, the **acceptance tests for F-001..F-003**, `docs/spec`, `docs/contracts`. | CI green on an empty implementation (with acceptance tests *failing as expected*). | The constitution is yours. Writing acceptance tests forces you to understand the behaviour before any agent touches it. |
| **1: Guarded Solo** | A | `AGENTS.md`, `CLAUDE.md`, the four hooks, `settings.json`. Implement F-001 interactively. | Stop hook has sent the agent back at least once, and you watched it self-correct. | Hooks, permissions, context cost classes (use `/context` to *see* what loaded). |
| **2: Staged Pipeline** | B | Skills `/spec`, `/add-algorithm`; rules; `skeptic` + `researcher` subagents; ADR habit. Implement 2–3 algorithms. | Skeptic has caught a real bug the tests missed; you turned it into a test. | Maker/checker, progressive disclosure, writing skill descriptions that trigger. |
| **3: Long-Running** | C | `initializer` agent → full `feature_list.json` from the spec; `progress.md`; `init.sh`; `mark-passing.sh`; `/next-feature`. Run 5+ shifts with `/clear` between them. | Five consecutive shifts complete with *only* the files as memory. | State in files, not context. One-feature discipline. Handover quality. |
| **4: CI-Driven + Parallel** | D + E | `agent-nightly.yml` (headless on `agent-ready` issues); worktree-isolated subagents implement two algorithms in parallel. | An issue you labeled became a merged PR without you opening a terminal. | Headless mode, PR as the unit of trust, integration drift in parallel work. |
| **5: Loop + Ratchet** | F | `loop.sh` in a dev container; `metrics.jsonl`; `/retro`; `harness/benchmarks/`; weekly review. Deploy to Azure via `deploy.yml`. | Metrics show first-pass success improving over 3+ weeks, *and* at least one harness change was rejected by a benchmark run. | Loop controls, compound engineering, evaluating your own process. |

### 11.7 What to measure (and your interview story)

Track these from Phase 1 in `metrics.jsonl`. They turn "I use AI" into engineering evidence:

| Metric | Why it matters |
|---|---|
| First-pass success rate (feature passes on the agent's first "done") | The primary health signal of the harness |
| Stop-hook rejections per feature | How often sensors caught premature victory |
| Skeptic findings that were real bugs | Value of the inferential sensor |
| Human interventions per feature | How autonomous the system actually is |
| Cost (USD) and turns per feature | Efficiency and budget planning (your ~$40–50/mo constraint) |
| Escaped defects (found after merge) | The honest measure of sensor quality |
| AGENTS.md + rules line count over time | Watch for bloat |

**The interview story this produces** (practice saying it in 90 seconds):

> "I built a distributed rate limiter in ASP.NET Core and Redis, but the more interesting part is *how*. I set it up so an agent could do most of the implementation safely. The hard logic lives in pure functions so property tests can check the invariant; the Redis layer is proven equivalent with differential tests; acceptance tests are human-owned and physically protected from the agent by hooks and a CI check. The agent works one feature per session from a JSON feature list, with state handed over through a progress log, and an adversarial review subagent checks every change. I measured it: first-pass success went from X% to Y% over N weeks, mostly from harness changes that my retro process proposed and a benchmark suite validated. One of those benchmarks rejected a change I *thought* would help. And the whole thing deploys to Azure Container Apps through OIDC with a manual approval gate, so the agent never holds cloud credentials."

That paragraph touches distributed systems, testing strategy, AI engineering, security, Azure, and measurement. It's exactly the "operational maturity" signal your LLM_Monitor roadmap is aiming for, applied to your *development process*.

---

## Part 12: Common mistakes

| Mistake | Why it happens | Fix |
|---|---|---|
| **The encyclopedia binder** | Every correction gets appended to CLAUDE.md | Size budget + CI check; push rules down the cost-class table |
| **Guides where sensors belong** | Writing "ALWAYS run tests" instead of a Stop hook | If it *must* happen, it's a hook or a CI check |
| **Trusting "done"** | The model's "I've implemented it!" feels authoritative | Termination = objective predicate, never self-report |
| **Agent-writable acceptance tests** | Convenience | Protected paths, human-owned behaviour spec |
| **Vacuous sensors** | Filters matching zero tests, CI installing nothing, `catch {}` | Sensors must *fail* when they check nothing |
| **Subagent for everything** | Multi-agent feels sophisticated | Subagents pay off when they read a lot and return a little, or need independence |
| **Parallelizing coupled work** | Wanting speed | Parallelize only independent features; integrate often |
| **Unattended too early** | Excitement | Supervised → semi-supervised → unattended, one class of work at a time |
| **Autonomy without a sandbox** | "It's just my laptop" | Dev container/VM, no prod creds, deny `push`/`az` |
| **Plans that live only in chat** | It's faster in the moment | Plans are files. A plan that dies with `/clear` was never a plan. |
| **Never pruning** | Adding feels productive, deleting feels risky | Scheduled garbage-collection pass; rules need reasons so you know when they're obsolete |
| **Treating memory as policy** | Auto memory "just works" | Promote team-relevant learnings into reviewed files |
| **Ignoring prompt injection** | Tool results feel like "data" | Anything an MCP server returns is untrusted input; permissions are the defense |
| **Copy-pasting someone's mega-setup** | Popular repos with 40 skills and 12 agents | Start with 1 binder, 3 hooks, 1 skill, 1 subagent. Add only in response to an observed failure. |

The last row matters most for you. **Every piece of harness should exist because of a failure you observed**, not because a blog post had it. That's the ratchet applied to the harness's own growth, and it keeps you understanding every piece of your system.

---

## Part 13: Interview relevance

Questions you can now answer, with the shape of a strong answer:

**"How do you make sure AI-generated code is correct?"**
Autonomy proportional to verifiability. Deterministic sensors first (types, tests, property tests, architecture tests), human-owned acceptance tests for behaviour, an independent reviewer for what code can't check, and the PR/CI gate as the final authority. Name the maker/checker split.

**"What's the difference between CLAUDE.md, skills, and subagents?"**
Cost classes: always-on context vs progressively-disclosed procedures vs isolated context. Then say when you'd use each, and that anything that *must* happen is a hook or CI check instead.

**"An agent keeps making the same mistake. What do you do?"**
Classify the root cause (didn't know / forgot / wrong procedure / weak check / one-off). Fix at the lowest cost class possible: a lint rule over a binder line. Verify the fix with a benchmark task. That's the ratchet.

**"How would you let agents work on a large codebase at our company?"**
Managed settings and deny rules for policy; `AGENTS.md` as a map with nested/scoped instructions per team; skills for golden-path procedures; agents work through PRs with the same CI and review as humans; sandboxed execution; MCP allowlists; audit via OpenTelemetry; measure merge rate and escaped defects.

**"What are the risks of self-improving agent systems?"**
Rule bloat and context rot, overfitting, contradictions, sensor erosion (reward hacking), memory poisoning through prompt injection, Goodhart's law on metrics. Defenses: reviewed diffs for harness changes, protected sensors, fixed benchmarks, size budgets, provenance.

**"How do agent loops relate to systems you've built?"**
An agent loop is a queue consumer with a non-deterministic handler: it needs termination, retries with bounds, dead-lettering (BLOCKED), backpressure (open-PR cap), idempotency (one feature per commit, fresh worktree), and isolation.

**System-design variant:** *"Design a system where agents automatically fix flaky tests across 500 repos."* This is Setup F at org scale: triage from CI flake data → task queue → sandboxed workers per repo → verification (re-run N times) → PR with evidence → owner review → metrics dashboard → retro. Every box maps to something in this lecture.

---

## Part 14: A note about your hyperfixation habit

Your persona says you feel you must understand *everything* inside an abstraction before using it, that this slows you down, and that when you use something you don't fully understand, you break it.

Harness engineering is, underneath, **a formal answer to exactly that problem**, and the same answer applies to LangChain, YARP, or any library:

> **You don't earn the right to use an abstraction by reading all of its source. You earn it by having sensors that tell you when you're using it wrong.**

You don't read every line the agent writes before trusting the code. You make the acceptance tests, property tests and architecture tests strong enough that wrong code *can't get through*. Then your deep reading goes where it pays: the **contract** (what the abstraction promises), the **invariants** (what must stay true), and the **failures** (the moments a sensor fired and you need to know why).

Apply the same move to libraries. Before using an unfamiliar LangChain component, write a 20-line test that pins down what you *believe* it does. If the test passes, use it. If it fails, *that* failure is exactly the part of the source worth reading. You break things today not because you didn't read enough code, but because you had no sensor telling you your mental model was wrong *before* the breakage spread.

Your deep-dive instinct is valuable. Point it at the harness (the sensors, the contracts, the spec), where depth compounds, rather than at every line of generated or third-party code, where depth doesn't scale. Phase 0 of Tollgate is deliberately the place where you go as deep as you like.

---

## Part 15: What to study next, glossary, sources

### 15.1 Natural next topics

1. **Property-based testing** (FsCheck for .NET, Hypothesis for Python). The single most powerful sensor for agent-written logic.
2. **Evals for LLM systems.** You'll build this in LLM_Monitor anyway. The same skills evaluate your harness.
3. **The Agent SDK.** Build a tiny harness yourself (Repo Janitor, project 5) to understand the *inner* harness from the builder's side.
4. **Sandboxing & isolation.** Dev containers, gVisor/Firecracker concepts, network egress control.
5. **Prompt injection and agent security.** Mandatory before any agent reads untrusted input.
6. **Distributed job systems** (queues, DLQs, backpressure). The theory behind loop controls, and a persona gap.

### 15.2 Glossary

| Term | Meaning |
|---|---|
| **Agent** | A model in a loop with tools, deciding its own next action |
| **Agentic loop / tool loop** | Model → tool call → result → model, until no more tool calls |
| **Harness** | Everything in an agent system except the model |
| **Inner / outer harness** | Vendor-built runtime vs. your per-project configuration |
| **Context window** | Everything the model sees on a given call |
| **Context engineering** | Designing what enters the window, when, and in how few tokens |
| **Context rot** | Degraded adherence as the window fills |
| **Compaction** | The harness summarizing old conversation to free space |
| **Progressive disclosure** | Loading a summary always and detail only on demand (skills, scoped rules) |
| **Guide / sensor** | Feedforward steering vs. feedback checking |
| **Computational / inferential** | Deterministic program vs. model judgment |
| **Binder** (this lecture) | CLAUDE.md / AGENTS.md |
| **Auto memory** | Notes the agent writes for its future sessions |
| **Skill** | A folder with SKILL.md; a procedure loaded on demand (open Agent Skills standard) |
| **Subagent** | A separate agent with its own context, tools, and prompt |
| **Hook** | A deterministic command the harness runs on a lifecycle event |
| **MCP** | Model Context Protocol; the standard for connecting tools |
| **Plugin** | A distributable bundle of skills, agents, hooks, MCP servers |
| **Worktree** | A separate git checkout of the same repo, for isolated parallel work |
| **Headless / print mode** | Running the agent non-interactively (`claude -p`) |
| **Routine / scheduled task** | A cloud- or desktop-scheduled agent run |
| **Loop engineering** | Designing the system that prompts the agent: scheduling, task selection, termination, escalation |
| **Ralph loop** | `while true; do agent < PROMPT.md; done` with state in files |
| **Maker/checker** | Separate agents (or people) for producing and verifying work |
| **Premature victory** | The agent declaring success without objective verification |
| **Reward hacking** | Satisfying the literal check (e.g. editing tests) instead of the intent |
| **Ratchet / compound engineering** | Each failure permanently improves the harness |
| **Harnessability** | How easy a codebase makes it for an agent to succeed and be checked |

### 15.3 Sources

Checked September 2026. Read the first four in full.

- Anthropic, *How Claude remembers your project* (CLAUDE.md scopes, AGENTS.md, rules, auto memory limits): https://code.claude.com/docs/en/memory
- Anthropic, *Explore the .claude directory*: https://code.claude.com/docs/en/claude-directory
- Birgitta Böckeler, *Harness engineering for coding agent users* (guides/sensors taxonomy): https://martinfowler.com/articles/harness-engineering.html
- Addy Osmani, *Loop Engineering*: https://addyosmani.com/blog/loop-engineering/ and *Agent Harness Engineering*: https://addyosmani.com/blog/agent-harness-engineering/
- Anthropic Engineering, *Effective harnesses for long-running agents* (initializer + coding agent, feature list, progress file): search anthropic.com/engineering
- OpenAI, *Harness engineering: leveraging Codex in an agent-first world*: search openai.com
- Claude Code docs index (skills, subagents, hooks, `/goal`, `/loop`, routines, Agent SDK, plugin evals): https://code.claude.com/docs/llms.txt
- GitHub, *About agent skills* (Copilot skill locations, open standard): https://docs.github.com/en/copilot/concepts/agents/about-agent-skills
- Agent Skills specification: https://github.com/agentskills/agentskills and examples at https://github.com/anthropics/skills
- Curated list: https://github.com/ai-boost/awesome-harness-engineering

---

*Next lecture candidates in this folder:* `002` — Phase 0 of Tollgate walkthrough (writing the constitution by hand), or `002` — Hooks and the Stop gate internals (the JSON event contract, exit codes, and building your first three hooks).
