# Toolset Brainstorm

Ideas for toolsets Tool_Box could grow, scored against the three real goals of this project:

1. **Feed LLM_Monitor** — give its LangGraph agent loop real tools to call.
2. **Learn packaging** — build something worth packaging and consuming from another project.
3. **Impress a Microsoft hiring manager** — memorable demo + visible engineering judgment.

A good toolset idea scores on at least two of those. The scoring rubric at the bottom ranks everything.

---

## Category A — Creative & Visual (the demo stars)

These are what a hiring manager remembers ten minutes after the interview. Inspired by [blockworld](https://github.com/techleadhd/blockworld), whose core lesson is worth stealing: **design tools for call-economy**. Blockworld doesn't expose `place_block(x,y,z)` five thousand times — it exposes `place_sphere`, `place_tube`, `mirror`, and rasterizes server-side. The agent describes *form*; the tool computes coordinates.

### A1. Voxel World Builder (blockworld-style, but mine)
- Tools: `world_info`, `place_box/cylinder/cone/sphere/tube`, `mirror`, `remove_box`, `describe_world`, `clear`
- Live viewer (Three.js + WebSocket) that renders block-by-block as the agent works
- Twist to make it not a clone: connect it to LLM_Monitor so *your own* pipeline builds the castle through your own gateway — "my agent, my tools, my infrastructure, end to end"
- Learnings: WebSocket broadcasting, server-side geometry rasterization, skills-as-prompts

### A2. Scene Composer / Headless Renderer
- Tools: `create_scene`, `add_mesh`, `set_material`, `set_camera`, `set_lighting`, `render_png`, `export_glb`
- Headless Three.js (Node sidecar or Playwright screenshot) — the agent iterates: render → look at the PNG → adjust
- The feedback loop is the interesting part: a **vision-capable agent critiquing its own renders**
- Learnings: cross-language tool implementation (C# host shelling to Node), binary content in MCP results

### A3. Diagram Generator
- Tools: `create_architecture_diagram`, `add_node`, `add_edge`, `render_svg`
- Agent draws *your system's* architecture from what it observes via other toolsets (git + docker + diagram = "document this repo visually")
- Cheap to build, composes beautifully with Category C

### A4. Audio / MIDI Composer
- Tools: `create_track`, `add_notes`, `set_instrument`, `render_wav`
- Wildcard: high novelty, low career relevance. Fun weekend, not portfolio spine.

---

## Category B — LLM_Monitor-Native (goal #1 lives here)

Tools the LangGraph agent (policy check → RAG → tool loop) would actually call in production. These integrate via `langchain-mcp-adapters` over streamable HTTP inside the compose network.

### B1. Knowledge / RAG Management ⭐
- Tools: `ingest_document`, `search_corpus`, `list_sources`, `delete_source`, `corpus_stats`
- Moves RAG administration out of startup scripts and into agent-callable operations: "ingest this README and tell me what changed in retrieval quality"
- Directly extends existing pgvector + idempotent-ingestion work

### B2. Self-Observability ⭐ (the sleeper hit)
- Tools: `query_traces`, `get_latency_percentiles`, `find_failed_requests`, `summarize_errors`, `token_usage_by_pipeline`
- Wraps the planned Langfuse/Prometheus stack so the agent can **read its own telemetry**
- Demo line: "Ask my agent why it was slow yesterday — it queries its own traces and answers." That is a production-AI-operations story almost no portfolio has, and it's exactly the observe/evaluate/defend maturity the roadmap targets.

### B3. Evaluation Runner
- Tools: `run_eval_suite`, `get_eval_history`, `compare_runs`, `add_golden_example`
- The eval harness (roadmap item 006) exposed as tools — the agent can regression-test itself before you merge
- Pairs with B2 as the "AI engineering operational maturity" chapter

### B4. Model Management
- Tools: `list_models`, `pull_model`, `benchmark_model`, `model_info`
- Wraps Ollama's API; small, useful, easy first HTTP-shaped toolset

---

## Category C — Developer & Infrastructure (the original plan)

Still valid, now supporting-cast rather than flagship.

| Toolset | Tools | Note |
|---|---|---|
| C1. Diagnostics | os_info, cpu/mem, processes, sdks, ports | The original idea. Solid, but every vendor ships one — low uniqueness |
| C2. Build & Test | run_build, run_tests, parse_errors | Still the best solve for the copy-paste-errors pain |
| C3. Git | status, diff, log, blame (read-only) | Composes with A3 and C2 |
| C4. Docker | containers, logs, inspect, compose status | Useful *for developing LLM_Monitor itself* |
| C5. Database Inspector | schema, explain_plan, table_stats, vector_index_stats | pgvector-aware version is more interesting than generic |
| C6. Log Analysis | tail, search, summarize_window | Bounded-output discipline matters most here |

---

## Category D — Embedded & Hardware (the differentiator nobody else has)

You are a firmware engineer. Every AI-tools portfolio has a filesystem toolset; **none of them have a logic analyzer.** This category turns the day-job background from "not cloud experience" into a unique asset.

### D1. Raspberry Pi Control ⭐
- Tools: `gpio_read/write`, `i2c_scan`, `i2c_read`, `spi_transfer`, `read_sensor`, `capture_camera`
- Same Tool_Box binary, running on the Pi with `--toolsets raspberrypi` — this is the payoff of runtime toolset selection, demonstrated across CPU architectures
- Demo: "Claude, what I2C devices are on this bus, and turn on the fan if the temperature sensor reads above 30°C"

### D2. Embedded Debug Assistant ⭐⭐ (the boldest idea in this file)
- Tools: `list_serial_ports`, `open_serial_console`, `read_serial_window`, `send_command`, `parse_crash_dump`, `flash_firmware`
- An AI agent that watches a UART console, notices the panic, reads the stack trace, and cross-references the firmware source (via the Git toolset)
- This is *AI-assisted embedded development* — a genuinely under-served niche, a story only someone with your background can tell, and directly relevant to Microsoft (they build devices too)

### D3. Physical Output
- Tools: `set_led_matrix`, `move_servo`, `play_tone`
- Combine with A1: the agent builds something in the voxel world *and* draws it on a physical LED matrix. Pure demo theater — in a good way.

---

## Scoring

Effort: S = weekend, M = 1–3 weeks, L = month+

| Idea | LLM_Monitor fit | Packaging learning | Hiring-manager wow | Effort | Uniqueness |
|---|---|---|---|---|---|
| A1 Voxel builder | Medium (via facade) | Medium | **High** | M | Medium (exists, but twist helps) |
| A2 Scene renderer | Medium | High (multi-lang) | High | M–L | Medium |
| A3 Diagrams | Low | Low | Medium | S | Medium |
| B1 RAG mgmt | **High** | High | Medium | M | Medium |
| B2 Self-observability | **High** | High | **High** | M | **High** |
| B3 Eval runner | **High** | Medium | High | M | High |
| B4 Model mgmt | High | Medium | Low | S | Low |
| C1 Diagnostics | Low | Medium | Low | S | Low |
| C2 Build & test | Medium | Medium | Medium | S–M | Low |
| D1 Pi control | Low | **High** (cross-arch!) | High | M | High |
| D2 Embedded debug | Low | High | **High** | L | **Very high** |

---

## Recommended portfolio arc

Three chapters, one per goal:

1. **Ship B4 → B1 → B2 first.** They serve LLM_Monitor directly (goal 1), force the HTTP transport + compose integration + packaging story (goal 2), and B2 is a legitimately rare demo. C2/C4 get built incidentally along the way because you'll want them while developing.
2. **Then A1 or A2 as the showpiece.** Creative, visual, memorable (goal 3). Route it through your own gateway for the "full stack, end to end" narrative.
3. **Then D1/D2 as the differentiator.** The résumé line writes itself: *"MCP tool platform deployed from cloud containers to Raspberry Pis; AI agents that debug firmware over serial."*

What to *not* do: build C1–C6 first because they're easy. Generic dev-tools MCP servers are a crowded space; the categories above where you have unfair advantages (your own platform to instrument, your embedded background) are not.
