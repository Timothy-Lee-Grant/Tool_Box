#!/usr/bin/env python3
"""
2026_07_26_15_01 - Rendering spike for Lecture 008, Part 8 (The Draftsman).

Two renders that together make Part 8's argument:

  1. divider  - a SERIES chain. Renders beautifully in 58 ms. Proves the C#-emits-JSON /
                Python-emits-SVG sidecar contract works.
  2. bridge   - a Wheatstone bridge, five resistors, NOT a series chain. Requires explicit
                push/pop stack manipulation, named anchors and endpoints() calls - and even
                hand-placed by a human who can SEE the output, three labels collide and one
                resistor's diagonal cuts through the middle of the figure.

The finding is (2), and it is the point of the spike: schemdraw is a TURTLE-GRAPHICS
library. Each element starts where the last one ended and goes the direction you tell it.
It draws symbols excellently and solves NONE of the layout problem. The layout problem is
exactly "given only 'R1 connects VIN and VOUT', produce that direction list" - and that is
the open research problem Part 8 says to route around rather than solve.

Run:      python3 verify_rendering.py
Requires: pip install schemdraw          (MIT)
Optional: pip install cairosvg           (only to also emit PNGs)
"""

import json
import sys
import time

import schemdraw
import schemdraw.elements as elm


# --------------------------------------------------------------------------------------
# 1. SERIES CHAIN - the sidecar contract, and the case that works
# --------------------------------------------------------------------------------------
# This dict is the proposed wire format between C# and this script: C# emits it as JSON on
# argv, this script writes SVG. Note what C# has to supply: an ORDER and a DIRECTION for
# every element. Producing those two things from a netlist graph is the whole problem.
DIVIDER_SPEC = {
    "title": "divider_5v_to_3v3",
    "elements": [
        {"type": "SourceV", "label": "V1\n5V", "dir": "up"},
        {"type": "Line", "dir": "right"},
        {"type": "Resistor", "label": "R1\n110$\\Omega$", "dir": "right"},
        {"type": "Dot", "label": "VOUT"},
        {"type": "Resistor", "label": "R2\n220$\\Omega$", "dir": "down"},
        {"type": "Line", "dir": "left"},
        {"type": "Line", "dir": "left"},
        {"type": "Ground"},
    ],
}


def render_from_spec(spec, out="schematic.svg"):
    t0 = time.time()
    with schemdraw.Drawing(show=False) as d:
        d.config(unit=2.5)
        for e in spec["elements"]:
            el = getattr(elm, e["type"])()
            if "dir" in e:
                el = getattr(el, e["dir"])()
            if "label" in e:
                el = el.label(e["label"])
            d += el
        svg = d.get_imagedata("svg")
    with open(out, "wb") as f:
        f.write(svg)
    print(f"  {out}: {len(svg)} bytes in {(time.time() - t0) * 1000:.0f} ms")
    return svg


# --------------------------------------------------------------------------------------
# 2. WHEATSTONE BRIDGE - the case that does not work
# --------------------------------------------------------------------------------------
def render_bridge(out="bridge.svg"):
    """Not expressible as a linear spec at all. Needs a stack and named anchors."""
    t0 = time.time()
    with schemdraw.Drawing(show=False) as d:
        d.config(unit=2.6)
        V = d.add(elm.SourceV().up().label("V1\n5V"))
        d.add(elm.Line().right().length(1.3))
        d.add(elm.Dot().label("A", "left"))
        d.push()                                     # <- stack manipulation
        d.add(elm.Resistor().right().label("R1\n1k"))
        B = d.add(elm.Dot().label("B", "right"))
        d.add(elm.Resistor().down().label("R2\n1k"))
        C = d.add(elm.Dot())
        d.pop()                                      # <- back to the branch point
        d.add(elm.Resistor().down().label("R3\n1k"))
        D = d.add(elm.Dot().label("D", "left"))
        d.add(elm.Resistor().right().label("R4\n2k"))
        d.add(elm.Line().at(C.center).down().toy(D.center))
        d.add(elm.Line().right().tox(V.start).at(D.center))
        d.add(elm.Resistor().endpoints(B.center, D.center).label("R5\n10k"))  # the diagonal
        d.add(elm.Ground().at(V.start))
        svg = d.get_imagedata("svg")
    with open(out, "wb") as f:
        f.write(svg)
    print(f"  {out}: {len(svg)} bytes in {(time.time() - t0) * 1000:.0f} ms")
    return svg


def to_png(svg_path):
    try:
        import cairosvg
    except ImportError:
        return
    cairosvg.svg2png(url=svg_path, write_to=svg_path.replace(".svg", ".png"), scale=2)
    print(f"  -> {svg_path.replace('.svg', '.png')}")


if __name__ == "__main__":
    spec = json.loads(sys.argv[1]) if len(sys.argv) > 1 else DIVIDER_SPEC

    print("1. SERIES CHAIN (the case that works):")
    render_from_spec(spec)
    to_png("schematic.svg")

    print("\n2. WHEATSTONE BRIDGE (the case that does not):")
    render_bridge()
    to_png("bridge.svg")

    print("\nOpen both. The divider is publication quality. The bridge - hand-placed by a")
    print("human with full visibility - has three colliding labels and a diagonal cutting")
    print("through the figure. An automatic layout algorithm working from the graph alone")
    print("will not do better. See Lecture 008, Part 8.5 for the three-tier response.")
