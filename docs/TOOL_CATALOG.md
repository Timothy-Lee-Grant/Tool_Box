# Tool Catalog

Every tool the platform exposes, by toolset. Conventions: snake_case tool names; every tool and parameter carries a `[Description]` (enforced by `DescriptionConventionTests`); all string output is bounded by `OutputLimiter`; each tool is classified **read** (observes) or **write** (mutates) — the classification that will later drive read-only deployment mode.

## Toolset: Basics

Trivial connectivity and identity tools. They exist to prove the plumbing (plan 001); real capabilities arrive with later toolsets.

| Tool | Class | Parameters | Returns |
|---|---|---|---|
| `ping` | read | `message` (string, optional) — echoed back | `"pong"` or `"pong: <message>"` |
| `server_info` | read | none | `{ version, toolsets: [names], uptime }` |
| `current_time` | read | none | `{ utc, local, timeZone }` — ISO-8601 strings + zone id |

### Notes

- `server_info.toolsets` reports the toolsets loaded *in this process* — with config-driven loading (plan 003) this becomes deployment-dependent.
- `current_time` exists because models should not guess the date; its description says so explicitly.
