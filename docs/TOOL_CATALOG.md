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

- `server_info.toolsets` reports the toolsets loaded *in this process* — with config-driven loading it is deployment-dependent: only toolsets enabled via configuration appear (see "Enabling toolsets" below).
- `current_time` exists because models should not guess the date; its description says so explicitly.

## Toolset: Voxel

A live-buildable voxel world (plan 003). Tools describe *form*, not coordinates — `place_sphere`/`place_cylinder`/`place_cone`/`place_tube` rasterize a shape into cells server-side rather than exposing `place_block(x,y,z)` as the only primitive. First **write**-classified toolset in this catalog (see ADR-011) and first with its own companion infrastructure, a viewer broadcast service (ADR-010).

| Tool | Class | Parameters | Returns |
|---|---|---|---|
| `world_info` | read | none | Text: coordinate system, ground rule, reference sizes in blocks |
| `list_materials` | read | none | Text: the fixed 12-material palette, comma-joined |
| `place_block` | write | `x,y,z` (int), `material` (string) | Text: `"placed <material>"` |
| `place_box` | write | `x1,y1,z1,x2,y2,z2` (int), `material` (string), `hollow` (bool, default false) | Text: `"placed N <material>"` |
| `place_cylinder` | write | `x,z` (int), `r` (1-30), `h` (int, 1-120), `material` (string), `y` (int, default 0), `hollow` (bool, default **true**) | Text |
| `place_cone` | write | `x,z` (int), `y` (int), `r` (1-30), `h` (int, 1-80), `material` (string), `r2` (0-30, default 0) | Text |
| `place_sphere` | write | `x,y,z` (int), `r` (1-30), `material` (string), `ry,rz` (0-30, default 0), `hollow` (bool, default false) | Text |
| `place_tube` | write | `path` (2-24 `{x,y,z}` points), `r_start` (0.5-24), `r_end` (0.3-24), `material` (string) | Text |
| `mirror` | write | `axis` (`X`\|`Z`), `plane` (int, default 0) | Text: `"mirrored N blocks across ..."` |
| `remove_box` | write | `x1,y1,z1,x2,y2,z2` (int) | Text: `"removed N blocks"` |
| `clear` | write | none | `"world cleared"` |
| `describe_world` | read | none | Text: block count + bounding box |

### Notes

- **State is a single global singleton** (`VoxelWorld`), shared by every connected client — a documented v1 limitation, not an oversight (ADR-009). No session scoping yet.
- **Materials are validated against a fixed palette of 12** (deliberately fewer than the reference implementation this is modeled on's 100) — an unrecognized name returns a text hint, never an exception.
- **The agent cannot see the build.** A companion `BackgroundService` broadcasts world changes over a WebSocket (one fixed port, 8090 by default, loopback-only unless `Ports:Viewer:Address` is `0.0.0.0` — ADR-012, ADR-016; if the port is taken the viewer is reported as unavailable in `/health` `components` and the tools keep working) to a browser viewer (`viewer/index.html`) — but that channel is one-directional, server → browser, for human eyes only. The only feedback a tool call itself returns is the text above; there is no image or vision loop back to the model. Reachable at `ws://localhost:8090/voxel/` (or `127.0.0.1`) when run directly on a host, or at `ws://<published-host>:8090/voxel/` once a container sets the viewer address to `0.0.0.0` and publishes that port (a separate, deliberate exposure decision from the MCP endpoint's — ADR-012, ADR-016). Ports and the `/health` shape: `docs/ARCHITECTURE.md` §4.
- The viewer works identically regardless of which MCP transport the Host is running (stdio or HTTP) — it is not part of the MCP wire at all, just infrastructure the toolset brings with it (ADR-010).

## Enabling toolsets

The Host registers every toolset but starts only the *enabled* ones; a disabled toolset's tools are hidden from `tools/list`, a call to one returns `toolset 'x' is off`, and its background services (e.g. the Voxel viewer on 8090) never run (ADR-018).

| Id | Toolset | Enabled by default |
|---|---|---|
| `basics` | Basics | yes |
| `voxel` | Voxel | yes |
| `embedded` | Embedded | **no** — opt in explicitly |

**Config keys**
- appsettings: `"Toolsets": { "Enabled": ["basics", "voxel"] }`
- env var: `TOOLBOX_TOOLSETS=basics,voxel` (comma-separated, like `TOOLBOX_TRANSPORT`)

**Precedence** (last wins): code default (`basics`, `voxel`) < appsettings `Toolsets:Enabled` < `TOOLBOX_TOOLSETS` < in-memory overrides (tests).

**Rules**
- Ids are case-insensitive; whitespace around commas is ignored.
- Nothing configured → `basics`, `voxel` (identical to before the catalog existed).
- An **empty** `TOOLBOX_TOOLSETS=` enables **nothing** — it is not "use the default".
- An unknown id fails startup with a message listing the valid ids.
- At startup the Host logs the enabled toolsets on stderr (ADR-004), e.g. `Enabled toolsets: basics, voxel`.
- The roster is applied once at startup; live switching lands in a later step of #32.

## Transport independence

This catalog is identical over stdio and streamable HTTP — deliberately. Tools are defined once (plan 002 added a transport with zero toolset diffs); the wire is a deployment decision, never a capability decision. If a tool ever behaves differently per transport, that's a bug against this document. (The Voxel viewer broadcast is not a tool and sits outside this invariant entirely — see above.)
