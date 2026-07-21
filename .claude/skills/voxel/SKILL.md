---
name: voxel
description: Conventions for the Tool_Box voxel toolset — scale, materials, primitives, and build order. Read this before any voxel build.
---

# Voxel

## First: call world_info

**Call `world_info` before you place anything.**

You cannot infer the block scale from a tool signature like `place_block(x, y, z, material)` — it looks identical whether a sensible build is 10 blocks across or 200. `world_info` gives you reference sizes (a person, a doorway, a wall, a tower) already expressed in blocks. Build to that table.

This is the single most common failure in this world, and it is invisible until you look at the viewer: a structure sized wrong doesn't error, it just reads as a dollhouse or as too big to fit on screen.

## Coordinates

`x`/`z` are the ground plane; `y` is up. `y = 0` is ground — **never go below it**. Every placement tool here rejects a negative `y` outright, so treat it as a hard floor, not a soft guideline.

Centre builds near the origin — the viewer frames whatever you've built automatically, so there's no need to hunt for a "good" location first.

## Materials

Call `list_materials` once before you start — there are 12:

`stone` `brick` `wood` `glass` `gold` `iron` `grass` `sand` `water` `snow` `lava` `obsidian`

That's deliberately fewer than you might expect from a voxel toolset. **Discipline beats variety** — pick 3–5 materials for a build and hold them. A structure in every available material reads as noise, not richness. If you name an unknown material, the tool tells you the near-miss; don't loop retries, just use the suggested name or call `list_materials` again.

## Primitives — never place a large form block by block

| Tool | For | Notes |
|---|---|---|
| `world_info` | **call this first** — the grid scale | |
| `list_materials` | the palette | call once |
| `place_box` | walls, floors, rooms | `hollow: true` for a shell you can stand inside |
| `place_cylinder` | towers, columns | defaults to hollow (a tower you walk inside) — pass `hollow: false` for a solid pillar |
| `place_cone` | spires, roof caps | set `r2` for a truncated cone (a frustum) instead of a point |
| `place_sphere` | domes, orbs, rounded roofs | `ry`/`rz` stretch it into an ellipsoid; defaults solid |
| `place_tube` | **curved, organic forms** — arches, necks, cables | swept through a path of 2–24 points; `r_start`/`r_end` taper it |
| `mirror` | build one half, mirror it | never hand-build symmetry across `x` or `z` |
| `remove_box` | carve gates, windows, doorways | fast — don't loop `remove_block` for an opening |
| `place_block` | **detail only** — a single accent or corner stone | if you're calling this more than ~20–30 times for one surface, you reached for the wrong primitive |
| `clear` | reset before a new, unrelated build | |
| `describe_world` | block count + bounding box | the closest thing to "seeing" the world from here — there is no image, only this summary |

`place_sphere`, `place_cylinder`, `place_cone`, and `place_tube` don't place spheres or tubes — they rasterize a shape into cubes server-side. Describe the *form* (a radius, a taper, a path) and let the tool work out which cells that means. Computing cube-by-cube coordinates yourself defeats the entire point of these tools.

## Build order

Work bottom-up, big-to-small:

1. `world_info`, then `clear` if there's an old build in the way.
2. **Mass** — the big primitives: box floors, cylinder towers, sphere domes.
3. **Structure** — secondary forms: `place_tube` for anything organic or curved.
4. **Carve** — `remove_box` for gates, windows, doorways.
5. **Detail** — `place_block` for the handful of single-cube accents that actually need it.

## Budget

Aim for roughly **50–300 tool calls** per build. Most of that should be primitive calls, not `place_block` loops — a dome from `place_sphere` is one call; the same dome built cube-by-cube is hundreds. If you notice yourself writing a loop of individual `place_block` calls to approximate a shape a primitive already covers, stop and use the primitive instead.

## There is no viewer feedback loop

The build has a live 3D viewer, but you cannot see it — it's a one-way broadcast to a human's browser tab, with no image or vision channel back to you. `describe_world` (block count + bounding box) is the only feedback you get. Build deliberately from the reference sizes and the primitives above rather than iterating by "looking" at the result, because you can't.
