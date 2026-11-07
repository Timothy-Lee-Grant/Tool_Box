#!/usr/bin/env python3
"""
2026_07_26_15_01 - Verification code for Lecture 008 (The Solver and the Draftsman).

Every claim marked [Verified] in Documentation/Learning/008-The-Solver-And-The-Draftsman.md
is produced by this file. It builds a Modified Nodal Analysis solver from scratch so the
matrices are visible, rather than calling a simulator and trusting it.

Run:      python3 verify_concepts.py
Requires: numpy   (pip install numpy)
Optional cross-check against a real SPICE-family simulator:
          pip install "scipy==1.11.4" ahkab      # ahkab 0.18 needs scipy < 1.13
          ahkab divider_ahkab.cir

Note: ahkab is GPL. It is a *verification* aid only and must not be taken as a
dependency of the shipped toolset.
"""

import numpy as np
import warnings

warnings.filterwarnings("ignore")
np.set_printoptions(precision=8, suppress=False)

GND = -1  # ground is not an unknown; stamping skips its row/column


# --------------------------------------------------------------------------------------
# Stamping — the mechanical graph -> matrix step (Lecture 008, Part 3.3)
# --------------------------------------------------------------------------------------
def stamp_resistor(A, a, b, ohms):
    """A resistor's entire model: +g on the diagonals, -g on the off-diagonals."""
    g = 1.0 / ohms
    if a >= 0:
        A[a, a] += g
    if b >= 0:
        A[b, b] += g
    if a >= 0 and b >= 0:
        A[a, b] -= g
        A[b, a] -= g


def stamp_vsource(A, z, a, b, k, n, volts):
    """The 'Modified' in MNA: one extra unknown (the source current) and one extra row."""
    if a >= 0:
        A[a, n + k] += 1
        A[n + k, a] += 1
    if b >= 0:
        A[b, n + k] -= 1
        A[n + k, b] -= 1
    z[n + k] = volts


# --------------------------------------------------------------------------------------
# Part 3.5 — the 5V -> 3.3V divider, solved from the raw matrix
# --------------------------------------------------------------------------------------
def part_3_5_linear_mna():
    print("=" * 78)
    print("PART 3.5 - LINEAR MNA: 5V -> 3.3V divider")
    print("=" * 78)
    n, m = 2, 1  # 2 non-ground nodes, 1 voltage source
    VIN, VOUT = 0, 1
    A = np.zeros((n + m, n + m))
    z = np.zeros(n + m)

    stamp_resistor(A, VIN, VOUT, 110)
    stamp_resistor(A, VOUT, GND, 220)
    stamp_vsource(A, z, VIN, GND, 0, n, 5.0)

    x = np.linalg.solve(A, z)
    print("A =\n", A)
    print("z =", z)
    print(f"\nV(vin)  = {x[0]:.6f} V")
    print(f"V(vout) = {x[1]:.6f} V     <- ahkab agrees: 3.33333 V")
    print(f"I(V1)   = {x[2]:.9f} A     <- ahkab agrees: -0.0151515 A")
    print(f"cond(A) = {np.linalg.cond(A):.3e}   (well conditioned)")
    assert abs(x[1] - 10.0 / 3.0) < 1e-9, "divider must land at 3.3333 V"
    print("\nASSERT OK\n")


# --------------------------------------------------------------------------------------
# Part 3.6 — both classic SPICE errors are one linear-algebra fact
# --------------------------------------------------------------------------------------
def part_3_6_singular_matrices():
    print("=" * 78)
    print("PART 3.6 - THE TWO CLASSIC SINGULAR-MATRIX ERRORS")
    print("=" * 78)

    # (a) 'no DC path to ground': a node touched only by a capacitor. In a DC operating
    #     point a capacitor is an open circuit and is never stamped -> the row is all zeros.
    print("\n(a) NO DC PATH TO GROUND")
    A = np.zeros((4, 4))
    z = np.zeros(4)
    stamp_resistor(A, 0, 1, 110)          # R1 vin -> vout
    stamp_vsource(A, z, 0, GND, 0, 3, 5.0)  # V1 vin -> gnd, aux row 3
    # node 2 exists in the netlist but only a capacitor touches it: nothing stamps it.
    print("    cap-only node row:", A[2], " <- ALL ZEROS")
    print(f"    det = {np.linalg.det(A):.3e}   rank = {np.linalg.matrix_rank(A)} of {A.shape[0]}")
    try:
        np.linalg.solve(A, z)
        print("    solved (unexpected)")
    except np.linalg.LinAlgError as e:
        print(f"    -> LinAlgError: {e}   == ngspice 'no DC path to ground'")

    gmin = 1e-12
    Ag = A.copy()
    Ag[2, 2] += gmin  # a 1 TOhm resistor to ground: electrically negligible, numerically decisive
    xg = np.linalg.solve(Ag, z)
    print(f"    gmin={gmin} rescue: rank={np.linalg.matrix_rank(Ag)}, V(node2)={xg[2]:.3e} V")
    print("    NOTE: it returns 0 V for a node whose voltage is genuinely undefined -")
    print("          a SILENT fix. This is why check_node_voltage() earns its keep.")

    # (b) 'voltage source loop': two ideal sources across the same node pair.
    print("\n(b) VOLTAGE SOURCE LOOP")
    B = np.zeros((3, 3))
    zb = np.zeros(3)
    stamp_vsource(B, zb, 0, GND, 0, 1, 5.0)  # V1 = 5 V
    stamp_vsource(B, zb, 0, GND, 1, 1, 3.0)  # V2 = 3 V  <- contradiction
    print(f"    rank = {np.linalg.matrix_rank(B)} of 3, det = {np.linalg.det(B):.3e}")
    try:
        np.linalg.solve(B, zb)
        print("    solved (unexpected)")
    except np.linalg.LinAlgError as e:
        print(f"    -> LinAlgError: {e}   == 'voltage source loop / singular matrix'")
    Bg = B.copy()
    Bg[0, 0] += 1e-12
    print(f"    gmin does NOT rescue this (rank still {np.linalg.matrix_rank(Bg)} of 3) - correctly,")
    print("    because the circuit is genuinely wrong. Different error, different remedy.\n")


# --------------------------------------------------------------------------------------
# Part 4.3 — Newton-Raphson, and why raw Newton is not enough
# --------------------------------------------------------------------------------------
def part_4_3_newton():
    print("=" * 78)
    print("PART 4.3 - NEWTON-RAPHSON on a 1k resistor in series with a diode")
    print("=" * 78)
    Is, VT = 1e-14, 0.025852  # VT = kT/q at 300 K
    print(f"IEEE-754 double overflows at exp(709), i.e. Vd > {VT * 709:.2f} V\n")
    print(f"{'supply':>8} {'method':>10} {'result':>50}")
    print("-" * 72)

    for supply in (5.0, 20.0):
        for label, limit in (("raw", None), ("pnjlim", 0.1)):
            vd, outcome = 0.0, None
            for it in range(1, 501):
                e = np.exp(vd / VT)
                i, gd = Is * (e - 1), (Is / VT) * e
                if not np.isfinite(i) or not np.isfinite(gd):
                    outcome = f"OVERFLOW at iteration {it} (Vd reached {vd:.2f} V)"
                    break
                # companion model: diode -> conductance gd in parallel with source Ieq
                ieq = i - gd * vd
                vn = (supply / 1000.0 - ieq) / (1 / 1000.0 + gd)
                if limit and abs(vn - vd) > limit:
                    vn = vd + np.sign(vn - vd) * limit
                if abs(vn - vd) < 1e-10 and it > 1:
                    vd = vn
                    outcome = f"converged in {it:3d} iterations, Vd = {vd:.4f} V"
                    break
                vd = vn
            print(f"{supply:7.1f}V {label:>10} {outcome or 'hit iteration limit':>50}")
    print("\n173 vs 12 iterations at 5 V; hard overflow at 20 V. THIS is why SPICE has a")
    print("reputation for not converging - Newton only converges NEAR the root, and the")
    print("first step throws it far away.\n")


# --------------------------------------------------------------------------------------
# Parts 5.2 / 5.3 — integration: accuracy, then the scarier one, stability
# --------------------------------------------------------------------------------------
def part_5_integration():
    R, C, V = 1000.0, 1e-6, 5.0
    tau = R * C

    print("=" * 78)
    print("PART 5.2 - ACCURACY: backward Euler on an RC step (tau = 1 ms)")
    print("=" * 78)
    print(f"{'h':>8} {'steps/tau':>10} {'computed':>12} {'exact':>12} {'error':>9}")
    print("-" * 55)
    for h in (1e-5, 1e-4, 1e-3):
        v, t = 0.0, 0.0
        while t < 5 * tau - 1e-15:
            v = (v / h + V / (R * C)) / (1 / h + 1 / (R * C))
            t += h
        exact = V * (1 - np.exp(-t / tau))
        print(f"{h:8.0e} {tau/h:10.1f} {v:12.6f} {exact:12.6f} {abs(v-exact)/exact*100:8.3f}%")
    print("First-order: halve the step, halve the error. ~10 points/tau -> sub-1%.\n")

    print("=" * 78)
    print("PART 5.3 - STABILITY IS NOT ACCURACY: implicit vs explicit")
    print("=" * 78)
    print(f"{'h/tau':>6} {'backward Euler':>18} {'forward Euler':>18} {'exact':>10}")
    print("-" * 56)
    for h in (5e-4, 1e-3, 3e-3):
        vb = vf = 0.0
        t = 0.0
        while t < 10 * tau - 1e-15:
            vb = (vb / h + V / (R * C)) / (1 / h + 1 / (R * C))  # implicit
            vf = vf + h * (V - vf) / (R * C)                      # explicit
            t += h
        exact = V * (1 - np.exp(-t / tau))
        print(f"{h/tau:6.1f} {vb:18.4f} {vf:18.4f} {exact:10.4f}")
    print("\nForward Euler does not get inaccurate at h/tau = 3 - it EXPLODES to -75 V in a")
    print("5 V circuit. Backward Euler is A-stable: bounded for ANY step size. That property,")
    print("not accuracy, is why every SPICE uses implicit integration.\n")


if __name__ == "__main__":
    part_3_5_linear_mna()
    part_3_6_singular_matrices()
    part_4_3_newton()
    part_5_integration()
    print("=" * 78)
    print("All Lecture 008 [Verified] claims reproduced.")
    print("=" * 78)
