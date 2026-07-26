# Integrating the ToolBox into LLM_Monitor

Walkthrough for plan 002, Step 6. This document lives in Tool_Box because it describes *how to consume this server*; the actual LLM_Monitor changes should go through that repo's own `Documentation/AI_Implementation_Plans` process, using this walkthrough as its Stage 1 input.

## What this achieves

```
OpenWebUI → dotnet gateway → langchain_service ──► pipeline registry
                                   │                    │
                                   │              LangGraph agent
                                   │              (policy → RAG → tool loop)
                                   │                    │ get_tools()
                                   ▼                    ▼
                             pgvector / ollama     toolbox:8080/mcp   ◄── THIS
```

The LangGraph agent's tool loop stops being theoretical: it discovers tools from the ToolBox at startup and calls them over the compose network. Adding a toolset to the ToolBox later (Git, Docker, diagnostics) adds agent capabilities with **zero LLM_Monitor code changes** — that's the payoff being purchased here.

## 1. Compose changes (LLM_Monitor's `docker-compose.yml`)

```yaml
services:
  toolbox:
    image: ghcr.io/timothy-lee-grant/tool_box:1.0.1   # pin a version tag, never :latest — see "Image strategy" below
    environment:
      # Host-header allowlist (DNS-rebinding defense). "toolbox" is the name
      # this service is addressed by on the compose network — it MUST be listed
      # or every request will be rejected with 400.
      AllowedHosts: "localhost;127.0.0.1;toolbox"
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 10s
      timeout: 3s
      retries: 5
      start_period: 10s
    # NOTE deliberately no `ports:` section for the MCP endpoint (8080) —
    # reachable ONLY on the internal network. This is ADR-008's lockdown
    # posture: exposure is a config change someone has to consciously make,
    # mirroring LLM_Monitor's own pattern.
    #
    # Optional, separate decision (ADR-012): if the Voxel toolset is in play
    # and you want to watch it build in a browser, publish ONLY the viewer
    # port — it's receive-only/human-eyes-only, not the tool-execution
    # endpoint, so it doesn't reopen the lockdown above:
    #   ports:
    #     - "8090:8090"

  langchain_service:
    depends_on:
      toolbox:
        condition: service_healthy   # same startup-ordering discipline as pgvector
    environment:
      TOOLBOX_URL: "http://toolbox:8080/mcp"
```

**Image strategy (updated, Release 1.0):** this originally read `build: context: ../Tool_Box` — right for a single machine with both repos checked out side by side, but it breaks the moment *this* project's own CI runs, since that runner never has a `../Tool_Box` checkout to build from. That's exactly the rung this ladder predicted would eventually wobble, and it did — the fix was publishing Tool_Box's image to GHCR (multi-arch: `linux/amd64` + `linux/arm64`, so it pulls correctly on both CI runners and Apple Silicon dev machines) rather than adding cross-repo checkout machinery. Pin an explicit version tag (`:1.0.1` above), never `:latest` — reproducibility matters more than always having the newest build, and `docker_image_release.yml` (Tool_Box's own CI) publishes both on every tagged release. One live exception worth knowing about, not a contradiction of the rule: while actively verifying a Tool_Box-side fix before committing to a new tag/publish cycle, temporarily switching back to `build: context: ../Tool_Box` is the right call — no point re-tagging until the fix is proven (this is exactly how the viewer's bind-address fix, ADR-012, was verified locally on 2026-07-26 before being folded into a future tag).

**Mock mode (Stage 2 Q2 decision):** the toolbox service is NOT behind the live profile. Tools are real and cheap in both modes; only *models* are mocked in LLM_Monitor.

## 2. Python side (`langchain_service`)

Requirements (pin it — LLM_Monitor's honest-CI lesson applies to dependencies too):

```
langchain-mcp-adapters==<current>   # resolve at implementation time, then pin
```

Tool discovery, following the service's existing factory/config patterns:

```python
import os
from langchain_mcp_adapters.client import MultiServerMCPClient

def build_toolbox_client() -> MultiServerMCPClient:
    return MultiServerMCPClient({
        "toolbox": {
            "transport": "streamable_http",
            "url": os.environ["TOOLBOX_URL"],   # fail loudly if unset — no silent defaults
        }
    })

# At pipeline construction (e.g. where graph-basic / graph-rag are registered):
tools = await client.get_tools()      # -> list[BaseTool], LangGraph-ready
```

Wiring into the agent graph (matches the roadmap's policy → RAG → tool loop):

```python
from langgraph.prebuilt import ToolNode

tool_node = ToolNode(tools)
# graph: ... -> agent -> (tool_calls?) -> tool_node -> agent -> ... -> respond
```

Two conventions to carry over from the rest of langchain_service:

- **Client lifetime:** `MultiServerMCPClient` is stateless per invocation (each tool call opens a session, executes, cleans up) — safe under gunicorn's process model, nothing to share or lock.
- **Registry integration:** expose the tool-equipped graph as a new registry entry (e.g. `graph-tools`) rather than modifying existing pipelines — additive, like every other contract change in that repo.

## 3. Proof (what LLM_Monitor's tests should assert)

Integration pytest (runs inside compose, mock mode — mark it so unit CI can skip):

```python
async def test_toolbox_tools_discovered():
    tools = await build_toolbox_client().get_tools()
    names = {t.name for t in tools}
    assert {"ping", "server_info", "current_time"} <= names

async def test_agent_can_call_ping():
    result = await graph.ainvoke(user_message("ping the toolbox with message 'e2e'"))
    assert "pong: e2e" in str(result)      # mock model must be told to emit the tool call,
                                           # or call the tool node directly — decide in that repo
```

Plus one line in `scripts/acceptance_check.sh`: curl `http://toolbox:8080/health` from inside the network (or via `docker compose exec`).

## 4. Verification checklist

1. `docker compose up` → `toolbox` reaches healthy *before* `langchain_service` starts (ordering visible in logs).
2. From the langchain container: `curl -f http://toolbox:8080/health` → ok.
3. Pytest above green in mock mode.
4. Live-mode chat through OpenWebUI: "what time is it on the server?" → the agent calls `current_time` instead of hallucinating a timestamp. (This is the demo moment — an answer the model *cannot* know without the tool.)

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| Every request 400 | `toolbox` missing from `AllowedHosts` |
| `Connection refused` from Python | Service not on same compose network, or hitting `localhost` instead of `toolbox` |
| Healthcheck never passes | curl missing (wrong base image edit) or app listening on localhost instead of `0.0.0.0` |
| Tools list empty | Wrong URL path — the endpoint is `/mcp`, not `/` |
| Works with curl, adapters fail | `transport` key not `"streamable_http"`, or adapters version drift — pin and retest |
| Voxel viewer connects but always shows an empty/stale world, even after real tool calls | A stray `ToolBox.Host` process from earlier local (stdio) testing is squatting on `127.0.0.1:8090` on the host and winning the connection ahead of Docker's published port (ADR-012's discovery, ImplementationPlans/005 §2026-07-26) — `lsof -nP -iTCP:8090` on the host to check, `kill` it, reconnect the viewer. Not a Tool_Box or LLM_Monitor bug; a leftover local process. |
