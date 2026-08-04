# nucleo-blink

The bundled reference firmware for the Embedded toolset (`Documentation/ImplementationPlans/006-Embedded-Firmware-Toolset.md`). Blinks the onboard LED (LD2, PA5), doubles the blink rate while the onboard button (B1, PC13) is held, and echoes anything received on the ST-Link virtual COM port back out. Nothing here drives anything beyond the board itself — deliberate v1 scope, see plan 006 §2.9.

Bare-metal: no CMSIS, no HAL, no vendor SDK. `regs.h` hand-defines the handful of registers this program actually touches, each traceable to a specific RM0368 section. Not a shortcut — a reproducibility and teaching choice (plan 006 Step 2's design note): this directory builds from a fresh clone with nothing beyond the toolchain below.

## Prerequisites (macOS, Homebrew)

```bash
brew install arm-none-eabi-gcc openocd
```

Both are mainline `homebrew-core` formulae — no tap needed.

## Build

```bash
cd firmware/nucleo-blink
make
```

Produces `build/nucleo-blink.elf` and prints a `size` summary (text/data/bss). No board needs to be attached for this step.

## Flash (board must be attached via USB)

```bash
make flash
```

Runs OpenOCD against the Nucleo's onboard ST-Link, over SWD, and resets the board into the new program. If `list_connected_boards`-equivalent detection ever fails, the first thing to check by hand is this exact command — it's the toolset's own escape hatch back to "does this even work outside any C#."

## Verify by hand (plan 006 Step 2.2's checkpoint)

This step is deliberately **not** something an agent verifies — a human needs to actually see the LED and read the terminal:

1. After `make flash` completes, confirm LD2 (the onboard green LED) is blinking roughly once per second.
2. Hold B1 (the onboard blue button) — confirm the blink speeds up noticeably.
3. Open the serial console (the Nucleo enumerates a virtual COM port over the same USB cable — `/dev/tty.usbmodem*` on macOS; check `ls /dev/tty.usbmodem*` if unsure which one):

   ```bash
   screen /dev/tty.usbmodemXXXX 115200
   ```

   (`Ctrl-A` then `k` to exit `screen`.) Confirm the startup banner (`nucleo-blink up: ...`) appears once, then type a character — confirm it echoes back.

If all three hold, this step's checkpoint is met and plan 006's spike #3 (§2.8) is resolved by construction — the harder question it was actually checking (does the whole toolchain round-trip on real hardware) is answered the moment this works.
