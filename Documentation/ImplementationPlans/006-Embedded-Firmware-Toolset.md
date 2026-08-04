2026_08_03_23_13-(Embedded-Firmware-Toolset)

# Implementation Plan 006 — Embedded Firmware Toolset

**Status: Stage 2, opening discussion — blocked on hardware/scope questions.** Started 2026-08-03 on `feature/firmware-toolbox-addition`, the branch this plan's name was chosen for from the start (unlike that branch's earlier commits, which were actually plan 004/SPICE work — a naming/sequencing mismatch worth noting once, not fixing retroactively). This is the toolset the brainstorm document called D2 — *"the boldest idea in this file"* (`Documentation/Brainstorms/003-TOOLSET_IDEAS.md`, Category D).

---

# Stage 1 (Design Documentation)

*Timothy's goal, as stated 2026-08-03:* give the AI agent the ability to do **firmware troubleshooting** end to end. Four capabilities named explicitly:

1. **Compile embedded code.**
2. **Flash** the compiled code onto a microcontroller.
3. **Run and troubleshoot via GDB.**
4. **Serial communication** — invoke and work with a serial connection (and, potentially, other communication forms common in embedded systems — I2C/SPI are named elsewhere in `persona.md` as day-job territory).

Also stated: it should be architecturally reasonable to develop this toolset on its own branch, in isolation from whatever else is in flight (plan 004/SPICE, currently parked), because the Host/Core/Toolset boundary (ADR-003) means a new toolset costs the rest of the platform nothing until it's composed in. That premise is correct and is the same one that let plan 003 (Voxel) and plan 004 (SPICE) develop independently — confirmed, not just asserted, below in §2.2.

---

# Stage 2 (Discussion)

**[2026-08-03, AI]** Opening position.

## 2.1 Why this is a different kind of toolset than anything shipped so far

Worth saying plainly before any design: Voxel and SPICE are both **closed-world simulations.** The worst-case failure of a bad `place_sphere` call is an ugly castle; the worst-case failure of a bad SPICE netlist is a wrong number in a CSV file. Nothing either toolset does leaves the process.

This toolset is different in kind, not degree: it **compiles and executes arbitrary code on a physical device**, and if that device is wired to anything beyond its own onboard LED, the agent's tool calls have real-world physical consequences that no `git revert` undoes. That's not a reason not to build it — it's the actual differentiator the brainstorm doc identified ("nobody else has a logic analyzer") — but it changes what "Stage 2 discussion" needs to cover before Stage 3 gets written. SPICE's Stage 2 spent its effort on *feasibility* (can this even work). This one has to spend real effort on *safety* (what happens when it works exactly as instructed and that's the problem).

## 2.2 The cast — four components, four different trust levels

Personifying these the way `persona.md` asks for, because the trust boundary between them is the entire design problem:

| Character | Tool | What it can do | What it can undo |
|---|---|---|---|
| **The Translator** | cross-compiler (`arm-none-eabi-gcc` or similar) + build system | Turns source into a binary. Never touches the board. | Everything — it's a local file, `rm` fixes any mistake. |
| **The Courier** | flashing tool (target-specific — OpenOCD, `esptool.py`, `avrdude`, `picotool`, ...) | Makes a **one-way trip**: hands the Translator's binary to the chip's program memory, overwriting whatever was there. | Nothing, once written — the *only* undo is flashing something else over it, and on some chips a bad enough image (e.g. one that disables the debug port) can make even that impossible without a hardware recovery procedure. |
| **The Puppeteer** | GDB, attached over the debug probe | Halts the chip mid-instruction, reads/writes registers and memory, single-steps, resumes. The most powerful of the four — a `continue` on a chip already flashed with real logic is **the same as flipping the board's power switch on**. | Nothing — it's not undoing anything, it's *causing* real execution, including whatever GPIOs that execution drives. |
| **The Scribe** | serial reader (UART) | Passively transcribes whatever the chip already chose to say. Read-only by construction — it cannot make the chip do anything by itself. | N/A — it's the only one of the four that's inherently safe; it can only *observe*. (Sending data back over the same serial line is a different action — see §2.4 — because the firmware on the other end might interpret received bytes as a command.) |

The reason this table matters for the plan: **the Scribe and the Translator are low-risk in the same way every existing tool in this platform is low-risk** (local, reversible, no physical side effect). **The Courier and the Puppeteer are not**, and that's genuinely new — the platform's existing read/write classification (`docs/TOOL_CATALOG.md`) doesn't have a category for "reversible in software but not in the physical world," and this toolset is the first one that needs it.

## 2.3 Proposed: a third tool classification — `physical`

`TOOL_CATALOG.md` currently classifies every tool **read** (observes) or **write** (mutates). Proposal: add a third axis, orthogonal to read/write, for tools whose consequence isn't contained in Tool_Box's own process — call it `physical`. `flash_firmware`, `continue_execution`, `step`, and `send_serial` (§2.4) all get it; nothing in Basics, Voxel, or SPICE ever needed it, which is exactly why it hasn't existed yet.

Two concrete mechanisms follow from that classification, both reusing things the platform or the protocol already has rather than inventing new machinery:

1. **Registration-time gating, same pattern as ADR-008's "exposure is a conscious config change."** `physical`-classified tools are registered only if an explicit opt-in is present — proposed `TOOLBOX_ALLOW_HARDWARE_ACTIONS=true` (env var, checked once at composition time in `AddEmbeddedToolset()`, same shape as `AllowedHosts`). A fresh clone of this repo, or a copy running on a machine with no board attached, never has `flash_firmware` on the tool list at all — not refused at call time, **absent from the catalog**, which is the stronger guarantee (an agent can't even attempt what it can't see).
2. **MCP's own `destructiveHint` tool annotation, if the C# SDK exposes it — needs a spike (§2.8).** The MCP spec defines tool annotations (`readOnlyHint`, `destructiveHint`, `idempotentHint`, `openWorldHint`) that a well-behaved client can use to gate its own confirmation UI — this would mean the *protocol itself* carries the risk signal to Claude Desktop/Code, rather than Tool_Box inventing a bespoke confirm-token dance. Worth doing properly if the SDK supports it; if it doesn't, fall back to a two-call confirm pattern (`request_flash()` returns a short-lived token, `confirm_flash(token)` executes) for `flash_firmware` specifically, since that's the single most irreversible individual action in the toolset.

This is proposed, not decided — see the open questions in §2.9.

## 2.4 Send-over-serial belongs in the `physical` tier too

Easy to miss: the Scribe (reading) is safe, but **writing** to the same serial line is not, because the firmware on the far end almost certainly has a command interpreter, and "send bytes to a UART" is indistinguishable, from Tool_Box's side, from "issue a command to whatever the firmware does with that command." If the demo firmware's serial protocol includes e.g. `motor on`, `send_serial("motor on")` is a physical action wearing a text-tool costume. `send_serial` is classified `write, physical`; `read_serial_window` stays plain `read`.

## 2.5 Tool design, by area

Following the same tiering instinct as Voxel's shape-primitives and SPICE's Tier 1/Tier 2 split — but the "composite tool" argument doesn't transfer directly (there's no R/C-style formula to push server-side here). What *does* transfer directly from SPICE's Revision 3 (§2.11 of plan 004) is the **correctness-boundary** argument: GDB's MI protocol returns structured records, not prose, and if Tool_Box hands the agent *parsed, typed results* (`{ "pc": "0x08000188", "registers": {...} }`) instead of raw GDB console text, that's one more class of "the model misread a hex dump" eliminated server-side — same shape as SPICE's `add_rc_lowpass` computing R/C instead of asking the model to do the arithmetic.

**Area 1 — Build** (local, no `physical` tag)
| Tool | Class | Notes |
|---|---|---|
| `build_firmware(config?)` | write | Shells out to the project's own build system (`make`/`cmake --build`) — same "shell out, don't bind a library" instinct SPICE's ADR-pending decision made for ngspice, and for the same reason: a bad build is a non-zero exit code, not a hung server. |
| `get_build_log()` | read | Bounded via `OutputLimiter` — a real build log can be enormous; summarize errors/warnings, not the full transcript. |
| `clean_build()` | write | |

**Area 2 — Flash** (`physical`)
| Tool | Class | Notes |
|---|---|---|
| `list_connected_boards()` | read | Enumerate attached probes/boards — this alone is genuinely useful troubleshooting ("is the board even detected") and has zero physical risk. |
| `flash_firmware(image_path)` | write, physical | The Courier. One-way. Candidate for the two-step confirm pattern from §2.3. |

**Area 3 — Debug (GDB)** (mixed; execution tools are `physical`)
| Tool | Class | Notes |
|---|---|---|
| `start_debug_session()` / `end_debug_session()` | write, physical | Attaching halts the chip; that's a real state change the instant it happens. |
| `set_breakpoint(location)` / `list_breakpoints()` / `remove_breakpoint(id)` | write | Software-only bookkeeping in GDB until something resumes execution — no physical tag needed on these three specifically. |
| `continue_execution()` / `step()` / `halt()` | write, physical | The Puppeteer's actual moves. |
| `read_registers()` / `read_memory(addr, len)` / `get_backtrace()` / `evaluate_expression(expr)` | read | `evaluate_expression` needs a hard rule: GDB's `print` can *call functions* on the target (a side effect wearing a read-tool costume, same trap as §2.4) — v1 should restrict this to variable/register inspection only and refuse anything that looks like a function call syntactically, or just document the limitation and defer the harder version. |
| `write_memory(addr, bytes)` | — | **Proposed: out of scope for v1 entirely** (§2.9). It's an arbitrary-write primitive with the least legitimate "troubleshooting" use of anything in this list — you observe your way to a diagnosis far more often than you memory-poke your way to one — and it's the single tool an adversarial or simply confused agent could do the most damage with. |

**Area 4 — Serial**
| Tool | Class | Notes |
|---|---|---|
| `list_serial_ports()` | read | |
| `open_serial(port, baud)` / `close_serial()` | write | Opens/closes the connection; no send. |
| `send_serial(text)` | write, physical | See §2.4. |
| `read_serial_window(...)` | read | Reads from a bounded ring buffer, not the live stream directly — see §2.6. |

## 2.6 State model and companion infrastructure — direct precedent from 003 and 010

**State.** A session-scoped singleton, same shape as `VoxelWorld` (ADR-009) and the `Circuit` proposed in plan 004: one process, one active build/debug/serial session, no locking, documented as a v1 limitation rather than an oversight, for the identical reason ADR-009 gives — no real multi-client scenario has been observed yet, and designing for one now would be designing from imagination, not evidence.

**Serial needs a companion `BackgroundService`, not a request/response tool alone.** MCP tools are call-and-response; a live UART is a continuous stream. `read_serial_window()` can't "watch" a stream between calls unless *something* is draining the port in the background the whole time. This is the same architectural move as `VoxelViewerBroadcastService` (ADR-010) — a toolset's own `IHostedService`, registered in its one `Add<Name>Toolset()` doorway — just **inverted**: Voxel's service pushes state *out* to a browser; this one pulls state *in* from a physical port into a bounded ring buffer the agent polls. ADR-010 already establishes that this pattern works identically under both Host composition paths (stdio Generic Host, HTTP `WebApplication`), so that's confirmed infrastructure, not a new risk.

A live browser viewer for this toolset (a scrolling console showing GDB events and serial output as they happen, human-eyes-only, exactly like the Voxel viewer's own doc comment: *"the agent never talks to this"*) is a nice-to-have demo visual worth naming now and building later — not core to v1, and it would reuse ADR-010's pattern a second time rather than invent a third one.

## 2.7 CI — this toolset has a structural limitation the other two don't

Worth stating plainly rather than discovering it during Stage 4: **GitHub Actions runners have no attached microcontroller.** Voxel's 77 tests include real-socket integration tests; SPICE's plan assumed a containerized `ngspice`. Neither option exists here — there is no way to CI-test "does `flash_firmware` actually flash a real board" or "does GDB actually attach over SWD," ever, on hosted CI. What CI *can* cover: the C# build itself, unit tests for anything that doesn't need hardware (the GDB/MI parser against canned input, the serial ring buffer's bounding logic, `OutputLimiter` usage, the `physical`-gating logic itself with the env var unset). The hardware-dependent path stays a manual, human-verified step **every time**, permanently, not just until a future CI upgrade — this is a fundamentally different honesty commitment than "honest CI" has meant so far in this project, and the README/status docs should say so explicitly once this ships, the same way ADR-009 says "singleton, not scoped" instead of staying silent about it.

## 2.8 Spikes to run before committing to a full build

Bigger and more foundational than SPICE's spike list, because SPICE had a software fallback when `ngspice` wasn't installable (the from-scratch MNA solver); **there is no software fallback for "does flashing this specific board actually work."** Every spike here needs real hardware in hand.

1. **GDB/MI round-trip from C#.** Spawn the target's GDB (`--interpreter=mi2` or `mi3`) as a child process, issue one command (`-exec-run` or similar), parse one result record. No obvious maintained C# GDB/MI client library is known to exist — confirm that gap is real (search first) before committing to hand-rolling a parser for GDB/MI's record grammar (`^`/`*`/`=`/`~`/`&` prefixes).
2. **`System.IO.Ports.SerialPort` on macOS.** This has a real, documented history of incomplete support on macOS in .NET — needs verification on Timothy's actual dev machine before the Serial area's design is trusted, the same "don't assume the environment matches the reference" instinct that caught plan 003's loopback-vs-wildcard Docker bug (ADR-012) and plan 004's ngspice-not-installable gap.
3. **The actual target board, end to end, by hand, before any tool is written.** Compile a trivial blink program, flash it with the chosen tool, attach GDB, set one breakpoint, hit it, read one register, resume, then open the serial port and read one line of UART output — all from the command line, no C# involved yet. If this doesn't work by hand, no amount of tool design fixes it; this spike is the actual feasibility gate the way spike #2 (ngspice round-trip) was for plan 004.
4. **MCP tool annotations in the C# SDK (§2.3).** Confirm whether `ModelContextProtocol` 1.4.x exposes `destructiveHint` (or equivalent) on `[McpServerTool]`, and whether Claude Desktop/Code actually surface it as a confirmation prompt — if the answer to either half is no, the two-step confirm-token fallback becomes the real v1 mechanism, not a backup.

## 2.9 Open questions for Timothy (blocking Stage 3)

These are answered via the accompanying question prompt in this session, and the answers get appended below as this discussion continues — same append-only shape as plan 004's §2.10/§2.11.

1. **Target board/architecture for v1.** This single-handedly determines the toolchain, flashing tool, and debug-probe story, and — per §2.5's "one board family" instinct (the same "scope hard, ship one thing well" call Release 1.0 made explicitly on packaging, and SPICE made on schematic layout) — v1 should target exactly one board, not be architecture-generic from day one.
2. **Which machine actually runs the hardware-facing tools** — matters for spike #2 (serial) and for whether OpenOCD/the toolchain need installing on this dev Mac specifically or on a separate Linux box nearer the hardware.
3. **Comfortable scoping the v1 demo circuit to onboard-only peripherals (LED, button, UART)** — nothing wired to an external actuator (motor, relay, heater) — so that a `continue_execution()` call can never have a physical consequence beyond the board itself, matching §2.2's honest framing?
4. **The `physical` gating mechanism from §2.3** — opt-in env var + (if spike #4 confirms it) MCP `destructiveHint` + a two-step confirm specifically on `flash_firmware`, or a different posture?

Smaller questions, non-blocking, answerable later: whether this toolset bundles a reference firmware project in-repo (`viewer/`-style, so the demo is reproducible without an external repo) or points at an external path Timothy supplies; whether `write_memory` (§2.5) is deferred permanently or revisited once the rest is stable.

---

## 2.10 Decisions (2026-08-03, Timothy + AI)

Three of §2.9's four questions came back settled directly: **onboard-only peripherals for v1** (LED/button/UART, no external actuators), and **opt-in env var + `destructiveHint` + confirm-token on flash** as the gating posture. Host machine came back "not sure yet," resolved below as a direct consequence of the board decision. Board choice came back as a question to me — *"what would be the best to appeal to Microsoft?"* — answered here with real reasoning, not a coin flip.

### Board: STM32 Nucleo, specifically **NUCLEO-F401RE** (or F411RE — near-identical, slightly faster clock, same price class)

**Recommendation and why, weighted specifically toward the Microsoft narrative:**

- **ARM Cortex-M is the actual industry default**, not a hobbyist simplification of it. RP2040 (Pico) and ESP32 are both excellent chips, but STM32's Cortex-M line is what shows up under "embedded" at most companies with a device team — including Microsoft's own (Azure Sphere's MT3620 is Cortex-A+M, Azure RTOS/ThreadX targets Cortex-M directly, Windows IoT-adjacent work assumes this class of chip). Building the demo on the same architecture family reads as "this transfers to our stack," not "this transfers if we happen to also use Pico."
- **SWD + OpenOCD + GDB is the professional debugging workflow**, unmodified — the same one a firmware engineer uses at a real company, not a simplified educational substitute. That distinction matters for an interview story: "I built an MCP tool that drives the *actual* tool I use at work" is a stronger sentence than "I built an MCP tool that drives a hobbyist-friendly alternative to it." It also directly extends the `persona.md` day-job line (embedded C/C++, hardware/software integration) instead of sitting next to it as an unrelated hobby project.
- **One USB cable does three of this plan's four capability areas.** The Nucleo's onboard ST-Link exposes SWD (flash + debug) *and* a UART-over-USB virtual COM port on the same cable — no second probe to buy (unlike Pico's GDB story, which needs a second Pico wired up as a Picoprobe), no separate USB-serial adapter. That's not just convenient, it's a smaller, more honest v1 scope: one physical setup step, not three.
- **Cost and availability are a non-factor either way** (all three options are $10–25, in stock everywhere) — so the tiebreaker is legitimately the professional-relevance argument above, not price.

This changes §2.9's board line from open to resolved; §2.5's tool tables and §2.6 below assume STM32/SWD/OpenOCD/GDB from here on.

### Host machine: **this same Mac**, for v1

Direct consequence of the board choice, not a separate decision: the Nucleo connects over one USB cable carrying both the debug/flash interface (ST-Link/SWD) and the serial VCP, so there's no reason to introduce a second machine for v1. A remote/Linux-box topology is a real future generalization (worth naming once: it would look like Tool_Box on the Mac talking to a small agent process next to the board, or Tool_Box itself running on a Pi with the Nucleo plugged into it) but designing for that now would be the exact "abstract from imagination, not evidence" mistake ADR-003/009 already warn against in this codebase.

---

# Stage 3 (Implementation Planning)

## Scope

**In:** `ToolBox.Embedded` toolset project (build/flash/debug/serial tools, `physical`-tier gating), its test project, a bundled bare-metal reference firmware project (`firmware/nucleo-blink/`, outside `ToolBox.slnx` — same "not part of the .NET solution" pattern as `viewer/`), Host wiring gated behind an opt-in env var, catalog/ADR/README updates.
**Out (deferred, explicitly):** `write_memory` (§2.5), a live browser debug/serial viewer (§2.6 — real future work, reuses ADR-010's pattern, not core to v1), any board beyond the one Nucleo family, any Docker/HTTP-transport deployment path (§3.1 below — new finding), general multi-target toolchain abstraction, I2C/SPI tools (named in `persona.md` as day-job territory but not asked for in Stage 1 — worth its own future plan once this one ships, not folded in now).

## 3.1 New finding: this toolset is very likely native/stdio-only for v1 — flag before Stage 4, not after

Every prior toolset in this platform is deployment-topology-agnostic by design (ADR-007: "deployment identity is configuration, not compilation"). This one probably can't be, for a reason that has nothing to do with this project's own code: **Docker Desktop on macOS does not pass USB devices through to containers** (a well-known Docker Desktop limitation — the engine runs inside a lightweight VM with no USB passthrough layer; Linux hosts fare better via explicit `--device=/dev/ttyACM0` mapping, but that's not Timothy's dev environment). Practically: `flash_firmware`, the GDB session, and the serial tools cannot reach a USB-attached Nucleo from inside Tool_Box's existing container path. **Labeled assumed, not yet spiked** — worth a five-minute confirmation (`docker run --device=/dev/tty.usbmodem... ...` against a minimal image) before it's written down as a permanent ADR, but the honest expectation going in is that this toolset ships stdio-only, with HTTP/Docker explicitly out of scope for v1 and recorded as such (same instinct as ADR-008 and SPICE's Tier A/refusal-path: a real limitation, written down, not discovered by a user).

## Definition of done

1. `dotnet build`/`dotnet test` pass with zero warnings for everything that doesn't require the board attached (same bar as every prior plan).
2. From a fresh clone, with `TOOLBOX_ALLOW_HARDWARE_ACTIONS` **unset**: `physical`-tagged tools are absent from `server_info`'s tool list entirely (not present-but-refusing).
3. With the Nucleo attached, the env var set, and Claude Desktop/Code connected: `build_firmware()` compiles the bundled blink/button/UART reference project; `flash_firmware()` (via the confirm-token flow) puts it on the board; `start_debug_session()` + `set_breakpoint()` + `continue_execution()` actually halts at the breakpoint; `read_registers()`/`get_backtrace()` return parsed, structured data, not raw GDB console text; `read_serial_window()` shows the UART output the running firmware produced.
4. A reflection test enforces `[Description]` coverage on every Embedded tool, same convention as `DescriptionConventionTests` elsewhere.
5. `docs/TOOL_CATALOG.md` gets an "Embedded" section with the new `physical` column; new ADRs record the `physical` tool tier, the one-board-family v1 scope, the structural CI gap (§2.7), and the Docker/USB-passthrough limitation (§3.1) once confirmed.

## Target structure

```
Tool_Box/
├── src/
│   ├── ToolBox.Host/                       # +1 gated composition line
│   ├── ToolBox.Core/                       # unchanged (same "no Core change needed
│   │                                        #   until a second toolset proves it" pattern
│   │                                        #   as plan 003 §"Note on Core")
│   └── ToolSets/
│       └── ToolBox.Embedded/                # NEW
│           ├── ToolBox.Embedded.csproj      # +System.IO.Ports package reference
│           ├── EmbeddedSession.cs           # state: build/flash/debug/serial, singleton
│           ├── BuildTools.cs                # [McpServerToolType] — Area 1
│           ├── FlashTools.cs                # [McpServerToolType] — Area 2, physical
│           ├── DebugSession.cs              # OpenOCD + GDB child-process management
│           ├── GdbMiParser.cs               # hand-rolled MI record parser (spike-gated)
│           ├── DebugTools.cs                # [McpServerToolType] — Area 3, mostly physical
│           ├── SerialReaderService.cs       # BackgroundService: drains VCP into a ring buffer
│           ├── SerialTools.cs               # [McpServerToolType] — Area 4
│           ├── PhysicalActionGate.cs        # env-var check + confirm-token issuance/validation
│           └── EmbeddedToolsetExtensions.cs # AddEmbeddedToolset() — the one gated doorway
├── firmware/                                 # NEW — outside ToolBox.slnx, same as viewer/
│   └── nucleo-blink/
│       ├── Makefile                          # arm-none-eabi-gcc/objcopy, no vendor HAL
│       ├── linker.ld                          # hand-written, teaches the memory map directly
│       ├── startup.s                          # vector table + reset handler
│       └── main.c                             # LED blink, button read, UART echo — the whole demo
├── tests/
│   └── ToolBox.Embedded.Tests/               # NEW — hardware-independent pieces only (§2.7):
│                                              #   GdbMiParser against canned MI text, ring-buffer
│                                              #   bounding, PhysicalActionGate logic with the
│                                              #   env var unset/set/token-expired
└── docs/ (TOOL_CATALOG.md, DECISIONS.md updated; README updated)
```

## Architecture

```
Claude Desktop / Claude Code
        │  stdio only (§3.1 — HTTP/Docker deferred, pending the passthrough spike)
        ▼
   ToolBox.Host ── AddEmbeddedToolset() ──┬── EmbeddedSession (singleton: build/flash state)
                                            ├── BuildTools / FlashTools / DebugTools / SerialTools
                                            ├── DebugSession ──┬── OpenOCD (child process,
                                            │                  │   long-lived, bridges USB↔TCP)
                                            │                  └── arm-none-eabi-gdb (child
                                            │                      process, MI protocol, per
                                            │                      debug session)
                                            └── SerialReaderService : BackgroundService
                                                     │  drains the Nucleo's VCP continuously
                                                     ▼
                                              bounded ring buffer ── read_serial_window() polls it
        USB (ST-Link: SWD + VCP, one cable) ══════════════════════▶ NUCLEO-F401RE
```

The `DebugSession` box is the one genuinely new process-management shape in this codebase: every prior external-tool integration (ngspice, planned) is one short-lived child process per call. GDB debugging needs **two coordinated processes** — OpenOCD staying up for the session's duration as the USB↔TCP bridge, GDB attaching to it per debug session — which is closer to the Voxel viewer's "long-lived background service" shape than to a single `Process.Start`/wait/parse call. Worth designing `DebugSession` with that precedent in mind rather than reaching for the simpler shell-out pattern and discovering mid-build that it doesn't fit.

## Steps

Each step ends at a verifiable checkpoint and waits for Timothy's permission before the next begins, same discipline as every prior plan. Steps marked **[HW]** cannot be checked off without the physical Nucleo in hand; steps without that marker can proceed on toolchain/logic alone.

### Step 1 — Project scaffolding
- 1.1 `src/ToolSets/ToolBox.Embedded/ToolBox.Embedded.csproj` — same shape as `ToolBox.Voxel.csproj`, plus a `System.IO.Ports` package reference.
- 1.2 `tests/ToolBox.Embedded.Tests/ToolBox.Embedded.Tests.csproj`.
- 1.3 Wire both into `ToolBox.slnx`; `ProjectReference` from `ToolBox.Host`.
- **Checkpoint:** `dotnet build` succeeds; nothing runtime-visible changes yet.

### Step 2 — Bundled reference firmware (no C# yet)
- 2.1 `firmware/nucleo-blink/`: hand-written `startup.s` (vector table, reset handler), `linker.ld` (flash/RAM regions, sections — this is deliberately *not* CubeMX-generated, so it doubles as teaching material on the ARM Cortex-M boot sequence, matching the project's existing Learning-doc pattern), `main.c` (blink the onboard LED, poll the onboard button, echo received UART bytes), `Makefile` invoking `arm-none-eabi-gcc`/`objcopy` directly (no HAL dependency beyond the vendor's CMSIS device header, which is a header-only, separately-licensed include, not a build dependency).
- 2.2 By hand, no C# involved: build it, flash it with a bare `openocd` command, confirm the LED blinks and a terminal (`screen`/`minicom`/`picocom` against the VCP) shows echoed characters. **This *is* spike #3 from §2.8** — folded into this step rather than run separately, since building the real firmware is the most honest way to run it.
- **Checkpoint [HW]:** a human, not an agent, sees the LED blink and gets an echo back over serial, entirely from the command line.

### Step 3 — Build tools (Area 1 — no `physical` tag, mostly CI-testable)
- 3.1 `BuildTools`: `build_firmware()` shells to `make` inside `firmware/nucleo-blink/`, captures stdout/stderr; `get_build_log()` (bounded, errors/warnings summarized not full transcript); `clean_build()`.
- 3.2 Unit tests against a deliberately broken `main.c` fixture (compile error) and a clean one — the same "prove the primitive with no server running" discipline as every prior toolset's Step 2.
- **Checkpoint:** `dotnet test` green; no board needed for this step at all — this is the one piece of the toolset actual CI (`ci.yml`) can exercise end to end, once the CI image gets an `apt-get install gcc-arm-none-eabi` line (same shape as SPICE's planned `ngspice` line).

### Step 4 — Physical-action gating (cross-cutting, no board needed)
- 4.1 `PhysicalActionGate`: reads `TOOLBOX_ALLOW_HARDWARE_ACTIONS` once at composition time; `AddEmbeddedToolset()` registers `physical`-tagged tool types only if it's `true` — absent from the catalog otherwise, per §2.3's "stronger than refusing at call time" argument.
- 4.2 Confirm-token flow for `flash_firmware` specifically: `request_flash(image_path)` returns a short-lived opaque token plus a human-readable summary of what's about to happen; `flash_firmware(token)` executes only if the token is valid and unexpired.
- 4.3 Spike (§2.8 #4): confirm whether `ModelContextProtocol` 1.4.x exposes `destructiveHint` on `[McpServerTool]`, and whether Claude Desktop/Code visibly act on it. If yes, apply it to every `physical` tool in addition to the token flow; if no, note the SDK gap and rely on 4.1/4.2 alone for v1.
- 4.4 Unit tests: env var unset → tools absent from a test `IMcpServerBuilder`; set → present; token flow → valid/expired/wrong-token all produce distinct, correct outcomes.
- **Checkpoint:** all green without any hardware attached — this step is the proof that the safety design from §2.3 actually behaves as specified, before it's ever pointed at a real board.

### Step 5 — Flash tools (Area 2, `physical`) **[HW]**
- 5.1 `list_connected_boards()` — `openocd -f interface/stlink.cfg -f target/stm32f4x.cfg -c "init; exit"` (or a lighter probe-only invocation), parsed for "found/not found"; read-only, no gate needed.
- 5.2 `flash_firmware`, wired through Step 4's confirm-token gate: `openocd ... -c "program <elf> verify reset exit"`.
- **Checkpoint [HW]:** through the Inspector (not yet Claude), `list_connected_boards()` detects the Nucleo; the confirm-token flow correctly flashes `firmware/nucleo-blink`'s build output.

### Step 6 — Debug session (Area 3, GDB/MI) **[HW, biggest single step]**
- 6.1 `DebugSession`: starts OpenOCD as a long-lived child process (GDB-server mode, default port 3333) on `start_debug_session()`; on `end_debug_session()`, terminates both OpenOCD and any attached GDB cleanly.
- 6.2 `GdbMiParser`: hand-rolled parser for GDB/MI's record grammar (spike §2.8 #1 must land here first, or this sub-step is where its findings get consumed) — turns `^done,bkpt={...}` style lines into typed C# results.
- 6.3 `DebugTools`: `set_breakpoint`/`list_breakpoints`/`remove_breakpoint` (plain `write`, no `physical` tag — matches §2.5's reasoning that these are software bookkeeping until something resumes); `continue_execution`/`step`/`halt` (`physical`, gated same as flashing but without the confirm-token — arguably lower-stakes than an irreversible flash, revisit if that reasoning doesn't hold once it's real); `read_registers`/`read_memory`/`get_backtrace` (`read`, parsed via `GdbMiParser` not returned as raw text); `evaluate_expression` (`read`, restricted per §2.5's function-call caveat — reject or flag anything that looks like a call).
- 6.4 Every text/structured return routed through `OutputLimiter`, per standing platform convention — register dumps and backtraces are exactly the kind of output that can blow past the budget.
- **Checkpoint [HW]:** through the Inspector, a full sequence — `start_debug_session` → `set_breakpoint(main)` → `continue_execution` → confirm it actually halted at the breakpoint (not just that the call returned) → `read_registers` returns parsed values → `get_backtrace` shows `main` → `continue_execution` again → `end_debug_session`.

### Step 7 — Serial tools (Area 4) **[HW]**
- 7.1 `SerialReaderService : BackgroundService` — opens the VCP (`System.IO.Ports.SerialPort`), continuously reads into a bounded ring buffer (fixed capacity, oldest-evicted — same "bounded, not unbounded" discipline as `OutputLimiter`, applied to a different shape of data). **Spike §2.8 #2 must land before this sub-step is trusted** — confirm `SerialPort` actually behaves on this Mac before designing around it further.
- 7.2 `SerialTools`: `list_serial_ports` (read), `open_serial`/`close_serial` (write), `send_serial` (`write, physical`, per §2.4), `read_serial_window(...)` (read, from the ring buffer, not the live port).
- **Checkpoint [HW]:** with `firmware/nucleo-blink` running and echoing, `open_serial` + `send_serial("hi")` + `read_serial_window()` round-trips the echo back through the agent.

### Step 8 — Host wiring + docs
- 8.1 One gated line in `ToolBoxServerComposition.cs`: `.AddEmbeddedToolset()` — the gating itself lives inside the extension method (Step 4.1), not in the Host, keeping ADR-005's "Host contains zero tool-type-specific logic" invariant intact.
- 8.2 `docs/TOOL_CATALOG.md`: new "Embedded" section, tools table with a `physical` column alongside the existing read/write classification.
- 8.3 New ADRs: the `physical` tool tier and its gating mechanism (§2.3); the one-board-family v1 scope (STM32 Nucleo, §2.10); the structural CI gap (§2.7 — no hardware-in-the-loop CI, ever, by construction, not a temporary gap); the Docker/USB-passthrough limitation (§3.1), once actually confirmed rather than assumed.
- 8.4 README: mention the Embedded toolset, the env-var opt-in, and — plainly, matching the project's existing "known, documented limitations" section — that this is the first toolset with a permanent verification gap CI cannot close.
- **Checkpoint [HW]:** fresh clone, `TOOLBOX_ALLOW_HARDWARE_ACTIONS=true`, Nucleo attached, Claude Code drives the full build→flash→breakpoint→continue→serial-read sequence end to end.

### Step 9 — Demo pass
- 9.1 Run a real "compile this, flash it, set a breakpoint at `main`, tell me what the registers look like when it hits, then let it run and show me what it prints over serial" prompt through Claude Code with the board attached; capture a screen recording (this is a much better demo medium than the Voxel viewer's screenshot, since half the payoff — GDB actually halting on real silicon — has no visual to photograph otherwise).
- **Checkpoint:** none code-based — the payoff step, same as plan 003's Step 8.

Steps 1, 3, and 4 have no hardware dependency at all and could be fully built, tested, and demoed via Inspector (build-log output, gating behavior) before the Nucleo is even ordered — worth sequencing first for the same reason plan 003 called out steps 1–3: real, checkpointed progress that doesn't stall on a shipping address.

---

*Stage 3 drafted 2026-08-03. Awaiting Timothy's review before Stage 4 (step-by-step permissioned implementation) begins — in particular, confirm the NUCLEO-F401RE/F411RE recommendation before it's ordered, since Steps 2 and 5–9 all assume it.*

**[2026-08-03, Timothy]** Confirmed no unresolved issues; permission granted for Step 1.

---

# Stage 4 (Implementation)

**[2026-08-03, AI — Step 1: Project scaffolding]**

Two items were technically still open going into this (the exact Nucleo model, unconfirmed; the Docker/USB-passthrough spike, unrun) — neither blocks Step 1, since scaffolding is board- and Docker-independent: plain project files with no firmware or container content yet. Proceeded on that basis; both remain live before Step 2/5+ and before §3.1 is written down as a permanent ADR, respectively.

Done, matching §Steps 1.1–1.3 exactly:

- `src/ToolSets/ToolBox.Embedded/ToolBox.Embedded.csproj` — same shape as `ToolBox.Voxel.csproj` (`ProjectReference` to Core, `ModelContextProtocol` 1.4.1, `Microsoft.Extensions.Hosting.Abstractions` 10.0.7 for the future `SerialReaderService`), plus `System.IO.Ports` 10.0.10 (queried against the live NuGet feed rather than guessed — latest stable, matches the `net10.0` release train). A comment on the `System.IO.Ports` reference flags it unverified on macOS per §2.8 spike #2, so that caveat travels with the code, not just the plan doc.
- `tests/ToolBox.Embedded.Tests/ToolBox.Embedded.Tests.csproj` — identical shape to `ToolBox.Voxel.Tests.csproj`.
- `ToolBox.slnx` — both new projects added to the existing `/src/ToolSets/` and `/tests/` solution folders.
- `ToolBox.Host.csproj` — `ProjectReference` to `ToolBox.Embedded` added now; the actual `.AddEmbeddedToolset()` composition line (gated behind `TOOLBOX_ALLOW_HARDWARE_ACTIONS`, per §2.3/Step 4) is deliberately deferred to Step 8, same sequencing plan 003 used for Voxel.

No `[McpServerTool]`, no domain types, no tools registered — nothing runtime-visible changes yet, matching the step's own definition of done.

Verified, not assumed:

- `dotnet build` — succeeds, 0 warnings, 0 errors, all 11 projects including the two new ones.
- `dotnet test` — all pre-existing suites still green (12+50+10+5 = 77, unchanged from before this step, as expected since nothing in Embedded has logic yet); the new `ToolBox.Embedded.Tests` project runs cleanly with zero tests (no failure, nothing to report yet).

State: scaffolding complete and verified on this machine (no separate confirmation needed this time — unlike plan 001 Step 1, this sandbox has the .NET 10 SDK). **Awaiting: permission for Step 2 (bundled reference firmware) — note this is the first `[HW]`-marked step and requires the Nucleo physically in hand.**

**[2026-08-04, Timothy]** Permission granted for Step 2.

**[2026-08-04, AI — Step 2: Bundled reference firmware]**

Facts load-bearing enough to get wrong on real silicon — peripheral base addresses, pin assignments, register bit positions — were checked against RM0368 and community sources via web search rather than pulled from memory alone, matching plan 004's "labeled verified vs. assumed" discipline. Sources: [ST RM0368 reference manual](https://www.st.com/resource/en/reference_manual/dm00096844-stm32f401xb-c-and-stm32f401xd-e-advanced-arm-based-32-bit-mcus-stmicroelectronics.pdf) (memory map, register layout), [Cirkit Designer's Nucleo-F401RE pinout](https://docs.cirkitdesigner.com/component/4a1d82a1-8187-4a51-85a6-9ed4333090e7/stm-32-nucleo-f401re) and [components101's pinout](https://components101.com/microcontrollers/stm32-nucleo-f401re-pinout-datasheet) (LD2=PA5, B1=PC13, confirmed independently), [controllerstech.com's register-level UART walkthrough](https://controllerstech.com/how-to-setup-uart-using-registers-in-stm32/) and a second targeted search cross-checking it (the first fetch conflated `TC` and `TXE` as both bit 6 — caught and corrected against a second source to `TXE=bit7, TC=bit6, RXNE=bit5`, RM0090/RM0368's actual layout). [Homebrew's `arm-none-eabi-gcc` formula page](https://formulae.brew.sh/formula/arm-none-eabi-gcc) confirmed it's mainline `homebrew-core`, no tap needed.

Created, all under `firmware/nucleo-blink/` (outside `ToolBox.slnx`, same "not part of the .NET solution" pattern as `viewer/`):

- `regs.h` — hand-written register structs/bit definitions for exactly the four peripherals this program touches (RCC, GPIOA, GPIOC, USART2). **Deviation from §Steps' wording**, flagged rather than silently substituted: the step originally allowed "the vendor's CMSIS device header" as a dependency; hand-rolling instead keeps this directory buildable from a fresh clone with nothing beyond the compiler, and is the more honest teaching artifact per `persona.md`'s documentation preferences — every address is traceable to a specific RM0368 section in a comment, not inherited opaquely from a 25,000-line vendor header.
- `main.c` — blinks LD2 (PA5), reads B1 (PC13, active-low) to double the blink rate while held, configures USART2 (PA2/PA3, AF7) at 115200 baud from the 16 MHz HSI reset-default clock, and echoes received bytes. The baud-rate divisor is computed from `HSI_HZ`/`BAUD_RATE` as a named compile-time expression with the RM0368 formula in a comment, not a bare magic number.
- `startup.s` — hand-written vector table (16 Cortex-M core-exception entries only, no peripheral IRQs — nothing here unmasks an NVIC line) and `Reset_Handler` (copies `.data` flash→RAM, zeroes `.bss`, calls `main`). Structurally the same shape as ST's own CMSIS startup file, just hand-written for the same reproducibility reason as `regs.h`.
- `linker.ld` — `FLASH` (512K @ `0x08000000`) / `RAM` (96K @ `0x20000000`), both confirmed against ST's datasheet.
- `Makefile` — `make` builds; `make flash` runs the exact `openocd ... program ... verify reset exit` command Step 2.2 (and later, Step 5) needs.
- `README.md` — the by-hand build/flash/verify walkthrough for Step 2.2's checkpoint, written for Timothy to run, not for an agent to run.

Verified, not assumed — as far as verification can go without the board attached:

- Installed `arm-none-eabi-gcc` (Homebrew, mainline formula, not previously present on this machine) specifically to catch mistakes before handing physical verification off, rather than asking Timothy to be the first person to compile this.
- `make` — builds clean, **zero warnings** even under `-Wall -Wextra`, `text=536 bytes, data=0, bss=0`.
- Structural ELF inspection (`objdump`/`readelf`/`nm`) — the linked vector table matches the ARM Cortex-M architecture spec **byte-for-byte**: word 0 = `0x20018000` (`_estack`, matching `RAM origin + 96K` exactly), word 1 = `Reset_Handler+1` (Thumb bit correctly set, and it matches the ELF's own entry point), words 2–6 and the SVC/DebugMon/PendSV/SysTick slots all resolve to `Default_Handler`'s address as expected from the weak-alias pattern, reserved slots are exactly zero in exactly the positions RM0368's table specifies. `.isr_vector` and `.text` both land in `FLASH`; `.data`/`.bss` both land in `RAM`; `_sidata` correctly points at `.data`'s flash load address.

This is real evidence the assembly, linker script, and C are structurally sound — but it is not Step 2.2's checkpoint, and doesn't substitute for it. Nothing here proves the chip's silicon does what the reference manual says, that this exact board's ST-Link enumerates correctly, or that 115200 baud at the computed `BRR` value is actually clean on a scope — only a human, watching a real LED and a real terminal, closes that loop.

State: firmware authored and compile-verified; **hardware verification (README's three checkpoints — LED blinks, button speeds it up, serial echoes) is Timothy's step, not done yet.** Awaiting: confirmation the by-hand checkpoint passed, or a report of what didn't, before Step 2.2 can be marked complete.

**[2026-08-04, Timothy]** No hardware yet — the Nucleo hasn't been ordered/arrived. Reordering per the Steps section's own closing note ("Steps 1, 3, and 4 have no hardware dependency at all"): proceeding to Step 3, skipping Step 2.2's physical checkpoint for now. Step 2's authored firmware stands as written, unverified on real silicon until the board is in hand.

**[2026-08-04, AI — Step 3: Build tools]**

Created, all under `src/ToolSets/ToolBox.Embedded/`:

- `EmbeddedSession.cs` — the singleton state object named in the Stage 3 target structure, scoped to build-only state for now (`BuildResult? LastBuild`) rather than pre-building placeholder fields for flash/debug/serial — same "separate early, abstract late" instinct ADR-003 established, applied to this toolset's own state object instead of a platform-wide abstraction.
- `FirmwareDirectory.cs` — a one-field wrapper around the path `BuildTools` runs `make` in. Added specifically so `BuildTools` never hardcodes `firmware/nucleo-blink/` — production resolves it once in `EmbeddedToolsetExtensions`, tests inject a small fixture directory instead. This is what makes Step 3.2's fixture-based tests possible without needing the real (much larger) reference firmware to compile as part of every test run.
- `BuildTools.cs` — `build_firmware()`, `get_build_log()`, `clean_build()`, exactly as scoped. Shells to `make` via `Process`, capturing stdout/stderr separately then combining them (stderr labeled, so a compiler error is never silently interleaved into unrelated stdout noise). `get_build_log()` routes through `OutputLimiter`, matching the platform's standing discipline.
- `EmbeddedToolsetExtensions.cs` — `AddEmbeddedToolset()` registers `EmbeddedSession`, resolves the default `FirmwareDirectory` by walking up from the running assembly to `ToolBox.slnx` (documented as assuming a repo checkout is present, consistent with §3.1's native/dev-machine-only scoping), and calls `WithTools<BuildTools>()`. **Not yet wired into `ToolBoxServerComposition`** — that's still Step 8, and Step 4's gating logic still needs to wrap whatever `physical`-tagged tools future steps add here.

Created, under `tests/ToolBox.Embedded.Tests/`:

- `Fixtures/valid-firmware/` and `Fixtures/broken-firmware/` — minimal standalone `main.c`/`Makefile` pairs, deliberately not the real `firmware/nucleo-blink/` project (no linking, no board-specific complexity needed to prove the shell-out logic works). The broken fixture references an undeclared identifier — a real compiler error, not a contrived string.
- `BuildToolsTests.cs` — five tests, all against the **real** `arm-none-eabi-gcc`, not a mock: valid source reports success, broken source reports failure, `get_build_log()` before any build says so rather than returning empty, `get_build_log()` after a failed build contains the actual compiler error text, `clean_build()` removes the `build/` directory it created. Same "prove it against the real tool, not a mock" instinct as the Voxel WebSocket close-handshake story (plan 003) — a mocked "compiler succeeded" would hide exactly the process-plumbing bugs (wrong working directory, one stream captured but not the other, exit code misread) this class is actually at risk of.
- `DescriptionConventionTests.cs` — copy of the Basics/Voxel pattern, `typeof(BuildTools)`, 3 tools, full `[Description]` coverage asserted.
- `ToolBox.Embedded.Tests.csproj` — added `<None Update="Fixtures\**" CopyToOutputDirectory="PreserveNewest" />` so the fixtures exist next to the test DLL, not just in source.

Also updated `.github/workflows/ci.yml`: an `apt-get install -y gcc-arm-none-eabi make` step before `Restore`, exactly the line the Stage 3 plan's own Step 3 checkpoint text called for. This is deliberately the **only** piece of the Embedded toolset CI will ever exercise end-to-end (§2.7) — flash/GDB/serial need real hardware no hosted runner has, permanently, not just until some future CI upgrade.

Verified, not assumed:

- `dotnet build` — 0 warnings, 0 errors, all 13 projects.
- `dotnet test` — **85 total** (was 77; +8 new: 5 `BuildToolsTests` + 3 `DescriptionConventionTests`), all passing, including the real-compiler round trips against both fixtures. `GetBuildLog_AfterFailedBuild_ContainsTheRealCompilerError` specifically asserts the literal `undefined_symbol_that_does_not_exist` string is present in the captured output — not just "exit code nonzero," but that the actual diagnostic reaches the agent.
- Confirmed via `git add --dry-run` that fixture `build/` output stays untracked (the repo's existing generic `build/` `.gitignore` pattern already covers it — no new ignore rule needed).

State: Build area complete and verified, including a real CI-path check (the toolchain-install line), on this machine. **Not yet verified: that this same CI step actually passes on a hosted GitHub Actions runner** — worth confirming on the next push rather than assuming apt's `gcc-arm-none-eabi` package resolves identically there. Awaiting: permission for Step 4 (physical-action gating) — still hardware-independent, next in the reordered sequence.

**[2026-08-04, Timothy]** "next" — permission granted for Step 4.

**[2026-08-04, AI — Step 4: Physical-action gating]**

**Scope note, flagged rather than silently narrowed:** 4.4 as written asks for tests proving "env var unset → tools absent from a test `IMcpServerBuilder`; set → present." There is no `physical`-tagged tool type to gate yet — `BuildTools` has none, and `FlashTools` doesn't exist until Step 5 — so that specific assertion would be exercising an empty set against nothing, a vacuous test rather than a real one. Deferred that half of 4.4 to Step 5, where `FlashTools` gives it something real to assert presence/absence of. Everything else in 4.1–4.4 is built and tested now.

Created, under `src/ToolSets/ToolBox.Embedded/`:

- `PhysicalActionGate.cs` — two responsibilities in one class, matching §2.3's two mechanisms exactly:
  1. `HardwareActionsAllowed`, parsed once from `TOOLBOX_ALLOW_HARDWARE_ACTIONS` (`"true"`/`"1"`, case-insensitive on `true`; deliberately no synonym-guessing like `"yes"`/`"on"` — one spelling, unambiguous).
  2. A single-use, short-lived (2-minute) confirm-token flow: `IssueToken(summary)` / `TryConsumeToken(token, out summary)` returning `Valid`/`Unknown`/`Expired`. A token is removed the moment it's looked up, valid or not — so neither a stale token nor a legitimate one can ever be replayed.
  - Takes `TimeProvider` via constructor injection, same convention as `ServerInfoProvider` (persona.md's plan 001 note: "tests control the clock, no `Thread.Sleep`-based expiry tests"). Two public constructors — `(TimeProvider)` for production (reads the real env var) and `(string? rawEnvironmentValue, TimeProvider)` for tests (no env-var mutation, which would be unsafe under parallel test execution anyway). DI only considers the first: a bare `string?` isn't resolvable from the container, so `ActivatorUtilities` correctly skips the second constructor without any `[ActivatorUtilitiesConstructor]` attribute needed — confirmed by the successful build, not assumed.

Updated `EmbeddedToolsetExtensions.cs`: constructs `PhysicalActionGate` (using the already-registered `TimeProvider.System` from `AddToolBoxCore()`, same clock `ServerInfoProvider` resolves) and registers it as a singleton immediately after `EmbeddedSession`/`FirmwareDirectory`. The toolset descriptor's text now differs based on `HardwareActionsAllowed` — hardware-enabled vs. compile-only-with-instructions-to-enable — a small, real, observable proof the gate is actually consulted rather than inert plumbing, visible to any `server_info` caller even before Step 5 gives it a tool type to gate. Left an explicit comment marking exactly where Steps 5–7 attach their `if (gate.HardwareActionsAllowed) { builder.WithTools<...>(); }` conditionals.

Created `tests/ToolBox.Embedded.Tests/PhysicalActionGateTests.cs` — 14 tests: 8 theory cases covering env-value parsing (`null`/`""`/`"false"`/`"0"`/`"yes"` → false; `"true"`/`"TRUE"`/`"1"` → true — `"yes"` deliberately included as a *rejected* case, proving the "no synonym-guessing" decision is enforced, not just described), plus issue→consume round-trip, unknown-token, single-use/replay, post-expiry (`Expired`, using the `TestClock` pattern), and a token-issuance validation guard.

Verified, not assumed:

- `dotnet build` — 0 warnings, 0 errors, all 13 projects.
- `dotnet test` — **99 total** (was 85; +14, all `PhysicalActionGateTests`), all passing.
- `git status` — only the intended files touched; no stray fixture/build artifacts.

State: gating mechanism built and fully tested with zero hardware attached, per this step's own checkpoint. **Not yet proven:** that the gate actually controls a real tool's presence — that's Step 5's job, and it's the first thing Step 5 should verify before anything else in it. Steps 5, 6, and 7 are all `[HW]`-marked in full, and Timothy doesn't have the board yet — but two sub-pieces inside them don't actually need it: `GdbMiParser` (Step 6.2 — a hand-rolled parser that only needs canned MI text, not a live GDB session) and the serial ring buffer's bounding logic (inside Step 7's `SerialReaderService`, not the actual port I/O). §2.7 named both of these explicitly as the toolset's hardware-independent test surface. Awaiting: direction on which of those two to pull forward, versus Step 8's non-hardware sub-parts (catalog/ADR docs), versus pausing here until the board arrives.
