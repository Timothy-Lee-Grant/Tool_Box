2026_08_04_00_42-(The-Translator-The-Courier-The-Puppeteer-And-The-Scribe)

# Lecture 010 — The Translator, The Courier, The Puppeteer, and The Scribe: Everything Behind the Embedded Toolset

Plans 001–003 taught the platform's skeleton and its first real capability. Plan 004 (SPICE) taught you that a wrong number in a simulation is just a wrong number. Plan 006 — this one — is different in kind, and Steps 1–4 have already touched more genuinely new territory than any prior plan: a cross-compiler with no operating system underneath it, a linker script that hand-places bytes in physical memory, a chip that boots by reading a table nobody runs, a debugger protocol that talks in a grammar instead of English, and a safety design built specifically because this toolset can make a real LED blink or, eventually, make a real motor turn.

This lecture exists for one purpose: **you're about to keep building this toolset — Steps 5 through 9 are all still ahead — and every one of them assumes you already have the concepts below.** It's also written so that when the Nucleo finally arrives and Step 2.2's checkpoint either passes or doesn't, you'll know *why*, not just *whether*.

## How to use this document

Your day job already gives you a real head start here: I2C/SPI, Raspberry Pi work, hardware/software integration. Use that — this lecture leans on it rather than re-teaching it. But it also names, plainly, where this toolset goes past typical day-to-day embedded work: most Pi work runs under Linux, with an OS, a filesystem, and `libc` underneath it; most STM32 work (yours very possibly included) goes through ST's HAL, which hides the exact registers `regs.h` exposes on purpose. The four "characters" from plan 006 §2.2 (the Translator, the Courier, the Puppeteer, the Scribe) structure this document because they're the actual seams in the system — each one is a different program, talking a different protocol, with a different failure mode, and reviewing this code means being able to say which of the four a given bug belongs to before you go looking for it.

Read it in order once, then use Part 19's concept index to jump back in later. Every code excerpt below is copied from a real file in this repo, not reconstructed from memory — where a fact needed checking against ARM/ST documentation rather than recalled, that's marked `[verified]` inline, and Part 20's Sources section lists what was actually checked.

---

# PART 0 — WHY THIS IS A DIFFERENT KIND OF LECTURE

## Part 0.1 — What's actually new here, named honestly

Four things this plan introduces that nothing before it in this repo touched:

| New territory | Why your day job doesn't automatically cover it |
|---|---|
| **Bare-metal boot** (linker script, vector table, `Reset_Handler`) | Pi work boots through U-Boot/Linux, which already did this for you. STM32 work through CubeMX/HAL generates this file *for* you, correctly, without ever showing you why it's correct. |
| **A debugger's own wire protocol** (GDB/MI, not interactive GDB) | You've almost certainly typed `break main` at a `(gdb)` prompt. Nobody who hasn't built debugger tooling has parsed what GDB says back to a *program*, not a human. |
| **A hardware debug probe's protocol** (SWD) | I2C/SPI are protocols *you* speak to a peripheral chip you chose. SWD is a protocol something else speaks *into* the CPU core itself, from outside, while it runs. |
| **A safety design built for physical consequence** | Nothing before this toolset could hurt anything. This is the first "physical" tool tier in the platform (plan 006 §2.3), and it borrows a general software-security pattern (fail-closed defaults, confirm tokens) you'll see again in contexts that have nothing to do with hardware. |

Two smaller things worth flagging up front because Part 15 spends real time on them: a genuine, if currently harmless, bug shipped in Step 3's code, and a subtlety in how .NET's dependency injection container picks a constructor that Step 4's code depends on working correctly. Both are in this lecture because they're real, not hypothetical — the same standard Lecture 009's audit held itself to.

---

# PART I — THE MACHINE ITSELF

## Part 1 — What "bare metal" actually means

Every program you've written that prints to a terminal, opens a file, or allocates memory with `malloc` is standing on an enormous pile of software you don't see: a C runtime library, system calls, a kernel, device drivers, a scheduler. `main.c` in `firmware/nucleo-blink/` stands on none of that. There is no operating system on this chip. There is no `printf` (nothing to print *to* — you write your own `usart2_write_byte`). There is no `malloc` (no heap, no OS to manage one — see the `-nostdlib` build flag below). There is no process, because there's no OS to have a concept of "a process" — there is exactly one program, and it owns the entire chip from the instant it starts.

This is called **freestanding** execution, and the C standard actually has a formal name for it (C11 §4/5.1.2.1): a *freestanding implementation* is one where the execution environment provides no guarantee of a standard library, and `main`'s signature and behavior at startup are implementation-defined rather than standard — which is exactly why your `main.c` gets to define what happens when it returns (in this case: `Reset_Handler`'s `Infinite_Loop`, plan 006 Step 2's `startup.s`) instead of the C standard's usual "return to the OS" contract.

The compiler flags in `firmware/nucleo-blink/Makefile` say this directly:

```make
CFLAGS  := $(MCU_FLAGS) -std=c11 -Wall -Wextra -Og -g3 \
           -ffreestanding -fno-builtin -fdata-sections -ffunction-sections
LDFLAGS := $(MCU_FLAGS) -T linker.ld -nostdlib -nostartfiles \
           -Wl,--gc-sections -Wl,-Map=$(BUILD)/$(TARGET).map
```

- `-ffreestanding` tells GCC: don't assume `main` gets called by a C runtime, don't assume the standard library is present, and don't "optimize" `main.c`'s code into calls to library functions it thinks it recognizes (a hosted-mode GCC will sometimes rewrite a hand-written loop into a `memcpy` call — fatal here, because there's no `memcpy` to call).
- `-nostdlib -nostartfiles` at link time: don't link the C standard library, and don't link the usual `crt0.o`/`_start` startup object every hosted C program gets for free. **You are `crt0`** — `startup.s`'s `Reset_Handler` is doing, by hand, exactly the job a hosted platform's startup code does invisibly (Part 5 walks through it).

**What this replaces**, and why it's worth seeing once: if you'd written this as a normal hosted program, `int main(void)` would get called by `__libc_start_main` (Linux) or an equivalent, which itself was called by the OS loader after `mmap`-ing your ELF into a running process's address space, setting up a stack, and jumping to `_start`. None of that machinery exists here. **Something has to exist in its place**, and Parts 4–5 are that something.

## Part 2 — Meet the chip: the STM32F401RE's memory map

`regs.h` opens with four addresses:

```c
#define RCC_BASE    0x40023800UL
#define GPIOA_BASE  0x40020000UL
#define GPIOC_BASE  0x40020800UL
#define USART2_BASE 0x40004400UL
```

The single most important idea in this entire lecture, if you only take one thing from it: **on this chip, there is no difference between "a variable" and "a hardware register" from the CPU's point of view.** Both are just addresses in a 32-bit address space that `LDR`/`STR` instructions read and write. `GPIOA->ODR = 0x20` and `some_array[8] = 0x20` compile to *the same instruction shape* — a store to a memory address. The only thing that makes `0x40020014` (GPIOA's `ODR` register) special is that the silicon wired that specific address, instead of to a row of SRAM cells, to a set of flip-flops that directly drive physical pins. This is called **memory-mapped I/O**, and it's the entire reason `regs.h` can define a `GPIO_TypeDef` struct and have `GPIOA->MODER` "just work" — the struct's field offsets are made to match the silicon's own register layout, and the struct pointer is made to *equal* the peripheral's base address:

```c
#define GPIOA ((GPIO_TypeDef *)GPIOA_BASE)
```

That's not really a cast in the usual sense — it's telling the compiler "trust me, treat this raw number as if a `GPIO_TypeDef` already lives there," because one effectively does, in silicon.

The address ranges themselves come from the chip's **bus architecture** — three buses matter here (RM0368 §2.3, `[verified]` against ST's own reference manual during Step 2):

```
0x0000 0000 ┌─────────────────────────┐
            │  (aliased to Flash/RAM   │   depends on BOOT pins at reset
            │   depending on boot mode)│
0x0800 0000 ├─────────────────────────┤ ◄── FLASH (512K on the F401RE)
            │  Your program lives here │      your code starts executing
            │                          │      from HERE, always
0x2000 0000 ├─────────────────────────┤ ◄── SRAM (96K on the F401RE)
            │  Stack, .data, .bss      │      volatile — gone at power-off
0x4000 0000 ├─────────────────────────┤ ◄── APB1 peripherals (USART2 lives at 0x4000 4400)
0x4001 0000 ├─────────────────────────┤ ◄── APB2 peripherals
0x4002 0000 ├─────────────────────────┤ ◄── AHB1 peripherals (GPIOA/C, RCC live here)
            └─────────────────────────┘
```

Two buses, not one, because **speed and purpose differ**: AHB (Advanced High-performance Bus) connects things the CPU core touches constantly at full clock speed (GPIO, the clock controller itself); APB (Advanced Peripheral Bus) is a simpler, slower bus for things that don't need to keep up (USART2, timers) — this is the same "not everything needs the fast path" design idea you'll meet again in Part 14 when the serial reader gets its own background service instead of running on the tool-call thread. `RCC` (**R**eset and **C**lock **C**ontrol) sits on AHB1 because it's the thing that turns the clock *to* every other peripheral on or off — which is exactly why every one of `gpio_init()`/`usart2_init()` starts by touching `RCC->AHB1ENR`/`RCC->APB1ENR` first: **an unclocked peripheral's registers usually just don't respond at all** — the single most common "why doesn't this work" bug in bare-metal STM32 code, and the register write ordering in your own `main.c` avoids it by construction, not by luck.

---

# PART II — THE TRANSLATOR

*(plan 006 §2.2: "Turns source into a binary. Never touches the board. What it can undo: everything — it's a local file.")*

## Part 3 — The compile pipeline, and what "cross" means

A normal `gcc` on your Mac compiles code *for* the machine it's *running on* (ARM64 macOS → ARM64 macOS binary). `arm-none-eabi-gcc` is a **cross-compiler**: it runs on your Mac's architecture but emits code for a *different* one — ARM Cortex-M, which your Mac cannot execute directly. The name itself is a standard **target triple** convention (`arch-vendor-os-abi`, though here it's compressed to `arch-vendor-abi`): `arm` (the instruction set family), `none` (no vendor-specific OS assumptions), `eabi` (Embedded ABI — the calling-convention/data-layout rules this toolchain follows, distinct from Linux's `eabihf`/glibc ABI). You'll see this exact naming pattern again anywhere cross-compilation shows up: `x86_64-unknown-linux-gnu`, `wasm32-unknown-unknown`, `aarch64-apple-darwin` — it's worth recognizing on sight for the rest of your career, not just for this toolset.

The pipeline itself is the same four stages any C toolchain runs, just aimed at a different target:

```
main.c ──(preprocess+compile)──► main.o     ┐
                                              ├──(link)──► nucleo-blink.elf
startup.s ──(assemble)────────► startup.o   ┘
```

`-mcpu=cortex-m4 -mthumb -mfloat-abi=soft` (both `Makefile`s) tell the compiler *which* ARM variant and instruction encoding to emit: `-mthumb` selects the 16-bit-dense Thumb-2 instruction encoding Cortex-M cores actually execute (they don't support full 32-bit ARM instructions at all — Thumb-2 isn't an option here, it's the *only* encoding); `-mfloat-abi=soft` says "don't emit hardware floating-point instructions, do float math in software," a deliberate simplification since `main.c` never uses `float`/`double` — there's nothing here for the F401's actual FPU to do, so there's no reason to add the ABI complexity of hardware-float calling conventions.

## Part 4 — The linker script: placing bytes by hand

Once `main.o`/`startup.o` exist, the **linker**'s job is deciding *where in the final address space* every function and variable actually lands — and on a hosted platform, you never think about this, because the OS loader picks addresses for you at load time. Here, *you* pick, in `linker.ld`:

```
MEMORY
{
    FLASH (rx)  : ORIGIN = 0x08000000, LENGTH = 512K
    RAM   (rwx) : ORIGIN = 0x20000000, LENGTH = 96K
}
```

This literally repeats Part 2's memory map, on purpose — the linker has no independent way to know where this chip's Flash and RAM live; it only knows what you tell it.

The subtlest idea in the whole file is the difference between a section's **LMA** (Load Memory Address — where it physically *sits in Flash* when the chip powers on) and its **VMA** (Virtual/run-time Memory Address — where code expects to *find* it while running):

```
    _sidata = LOADADDR(.data);

    .data :
    {
        _sdata = .;
        *(.data)
        *(.data*)
        _edata = .;
    } > RAM AT> FLASH
```

`> RAM AT> FLASH` says exactly this: "this section's *addresses*, as far as the code is concerned, are in RAM (`> RAM`) — but *physically store its initial bytes* in Flash (`AT> FLASH`)." Why would you ever want that split? Because **RAM forgets everything at power-off, and Flash can't be written to at the speed a running program needs** (writing Flash means an erase-then-program cycle, milliseconds per operation, not nanoseconds) — so any *initialized, writable* global variable has to physically live in RAM to be fast and writable, but its *initial value* has to survive a power cycle, which only Flash does. Something, therefore, has to **copy** that initial value from its Flash home (`_sidata`) to its RAM home (`_sdata`..`_edata`) exactly once, at boot, before `main` runs and might read it. Part 5 is that copy, in `Reset_Handler`.

`.bss` (uninitialized/zero-initialized globals) skips this problem entirely — the C standard already guarantees these start at zero, so instead of *storing* a Flash image of "a million zero bytes," the linker just records *how many* zero bytes are needed (`_sbss`..`_ebss`) and `Reset_Handler` zeroes that RAM range directly. Nothing here has any initialized-or-uninitialized globals yet (`data=0, bss=0` in Step 2's verified `size` output) — but the mechanism has to exist correctly *before* the first global variable is added, or that variable will silently start containing garbage flash contents instead of its intended initial value, a bug that's brutal to diagnose because the C source looks completely correct.

## Part 5 — The startup file: how a chip boots, mechanically

Two things happen automatically, unconditionally, the instant this chip's `NRST` pin releases or it powers on — and both are hard-wired in silicon, not configurable:

1. The core loads a 32-bit value from address `0x00000000` into the **stack pointer**.
2. The core loads a 32-bit value from address `0x00000004` and **jumps** to it.

That's it. That's the entire hardware-guaranteed boot contract for every ARM Cortex-M chip — the first two words at address 0 are not code, they're *data*: an initial stack-pointer value and a reset-handler address. `startup.s`'s `g_pfnVectors` **is** that data, placed at address 0 (well, `0x08000000` — Flash is aliased to address 0 at boot on this chip, `[verified]` via ST documentation conventions, standard across the STM32F4 family):

```
    .word _estack        ; word 0 — NOT a code address, the initial SP value
    .word Reset_Handler   ; word 1 — where execution actually begins
    .word NMI_Handler
    .word HardFault_Handler
    ...
```

Confirmed structurally, not assumed, in Step 2's own verification (the plan doc's Stage 4 log): `objdump` on the linked ELF showed word 0 as the literal bytes for `0x20018000` (`_estack` = `RAM origin + 96K`, exactly) and word 1 as `Reset_Handler`'s address *with its low bit set* (`0x0800018d`, not `...18c`). That low bit isn't a typo or an off-by-one — Cortex-M cores execute **Thumb-only** code (Part 3), and the architecture uses that address bit 0 specifically to signal "this is a Thumb code address" to the core's instruction fetch logic. The GNU assembler sets it automatically for any symbol marked `%function`, which is exactly why `startup.s` declares `.type Reset_Handler, %function` — leave that off, and the vector table would contain an address one bit short of correct, and the chip would hard-fault on the very first instruction it tried to fetch.

`Reset_Handler` itself does exactly three things, in order, matching Part 4's setup precisely:

```asm
Reset_Handler:
    /* 1. Copy .data's initial values, Flash -> RAM */
    ldr r0, =_sidata
    ldr r1, =_sdata
    ldr r2, =_edata
CopyData:
    cmp r1, r2
    bcs CopyDataDone
    ldr r3, [r0], #4
    str r3, [r1], #4
    b CopyData
CopyDataDone:

    /* 2. Zero .bss */
    ...

    /* 3. Only now is C code safe to run */
    bl main
```

Order matters and isn't arbitrary: `main` cannot run *before* step 1, because any global that has an initializer would read Flash-garbage-that-happens-to-be-in-RAM instead of its real value; it cannot run before step 2 for the identical reason applied to zero-initialized globals. This is, mechanically, **the same job `crt0`/`__libc_start_main` does on a hosted system** — you're just watching it happen in eleven lines of assembly instead of it happening invisibly inside `glibc`.

**The vector table's remaining fourteen entries** are the ARM core's built-in exception vector — fault handlers, the supervisor call, the systick timer — and every one of them here is wired to `Default_Handler` via a `.weak` + `.thumb_set` alias:

```asm
    .weak NMI_Handler
    .thumb_set NMI_Handler, Default_Handler
```

`.weak` means "this symbol can be silently overridden by a strong definition elsewhere" — the exact mechanism that would let a future `SysTick_Handler` function in `main.c` simply *replace* this alias by existing, with zero changes needed to `startup.s`. Nothing overrides any of them yet, because `main.c` never unmasks an NVIC interrupt line (everything is polled — Part 12), so if one of these ever fires in practice, it means something genuinely went wrong (a bad pointer dereference, a stack overflow into unmapped memory), and `Default_Handler` deliberately just loops in place rather than silently continuing into undefined behavior — the embedded equivalent of "fail loud, in a place you can find with a debugger attached," which is exactly what Part 10's Puppeteer is for.

## Part 6 — ELF: the file format holding all of this together

`nucleo-blink.elf` (and every `.dll`/`.so`/`.exe` you've ever produced with any compiler) is an **ELF** (Executable and Linkable Format) file — a container format with two different, overlapping views of the same bytes, and Step 2's own verification walked both:

- **Sections** — the *linker's* view: named, typed regions (`.text`, `.isr_vector`, `.data`, `.debug_info`, ...), useful for *building* the binary and for tools like `objdump -h`.
- **Symbols** — a table mapping names (`Reset_Handler`, `main`, `_estack`, ...) to addresses, useful for *debugging* — this is the table GDB (Part 10) consults every time you type a function name instead of a raw address.

The `objdump`/`readelf`/`nm` output already captured in plan 006's Stage 4 log is worth re-reading now with this frame:

```
Idx Name          Size      VMA       LMA
  0 .isr_vector   00000040  08000000  08000000
  1 .text         000001d8  08000040  08000040
  2 .data         00000000  20000000  20000000
```

`.isr_vector` at exactly `0x08000000` — that's Part 5's vector table, confirmed to actually be *where the hardware needs it to be*, not just where the source code says it should be. `.text`'s VMA starting at `0x08000040` — precisely `0x40` bytes (16 words × 4 bytes) after the vector table, i.e. immediately following it, exactly as `linker.ld`'s section ordering demands. `.data`'s VMA and LMA both shown as `20000000` in this particular build only because the section is *empty* (`Size 00000000` — there's nothing to actually load-address-vs-run-address split when there's no data) — Part 4's LMA/VMA distinction would show up as two *different* addresses the moment a real initialized global exists. None of this was asserted from memory in Step 2 — it was read directly out of the built artifact, which is the actual habit worth keeping: **when you can inspect the real output instead of trusting the plan that produced it, do that.**

## Part 7 — `volatile`, and the one atomic trick in this file

Every register field in `regs.h` is declared `volatile uint32_t`. This keyword exists for exactly one purpose: telling the compiler **"do not assume this value only changes because of code you can see."** Without it, an optimizing compiler is fully entitled to notice that `main.c` never *writes* `GPIOC->IDR` (Part 8's button-poll register) in between two reads of it inside a loop, conclude the value "can't have changed," and cache the first read in a register — silently turning `while (button_pressed()) { ... }` into an infinite loop that never re-samples the actual pin, because from the compiler's pure-C-semantics point of view, nothing in the visible code changed it. `volatile` disables exactly that optimization for exactly that variable: every read and write in source becomes a real read/write instruction, every time, because the *hardware* can change this value out from under the program at any moment — a button press, an incoming UART byte — and the compiler has no way to know that unless told.

The one genuinely clever trick worth internalizing, because it's a real answer to a real category of your stated weak area (race conditions), is `led_set`'s use of `BSRR` instead of `ODR`:

```c
GPIOA->BSRR = on ? (1UL << LED_PIN) : (1UL << (LED_PIN + 16U));
```

The "obvious" way to set one bit of an output register is a **read-modify-write**: `GPIOA->ODR |= (1 << LED_PIN)` — read the current value, OR in the bit, write it back. That's *three* bus operations, and if anything else touches `ODR` between the read and the write (another interrupt handler, in a program that has any — this one doesn't, but the pattern generalizes), the write silently clobbers whatever that other write did, a textbook **lost update** race. `BSRR` (Bit **S**et/**R**eset **R**egister) is a hardware trick that turns this into a *single* write with no read at all: writing a 1 to `BSRR`'s low 16 bits sets the corresponding `ODR` bit, writing a 1 to its high 16 bits (bits 16–31) *resets* the corresponding bit — and both halves are implemented as **separate hardware set/reset logic**, not as "write, then let the chip do a read-modify-write internally." One atomic bus transaction, no possible interleaving, no lost update — the silicon gives you an atomic primitive for exactly this one operation, and using it instead of `ODR |=` is the difference between "this code has a race condition on paper that never happens to bite because nothing else touches this pin yet" and "this code cannot have that race condition, structurally."

## Part 8 — UART/USART: framing and the baud-rate arithmetic

You already know UART conceptually from I2C/SPI-adjacent day-job work; the STM32-specific piece worth walking through is `usart2_init()`'s one nontrivial line:

```c
const uint32_t div16 = HSI_HZ / BAUD_RATE;
USART2->BRR = ((div16 / 16UL) << 4) | (div16 % 16UL);
```

USART hardware needs to turn a clock (16 MHz here) into a specific bit rate (115200 baud) by dividing it down, and RM0368 §19.6.3 (`[verified]`) specifies that division as a **fixed-point number with 4 fractional bits**: a 12-bit integer "mantissa" (how many *whole* clock-divisor steps) plus a 4-bit "fraction" (sixteenths of one more step), packed into one 16-bit register — because a *pure* integer divisor almost never lands on a clean baud rate (16,000,000 / 115,200 = 138.88..., not a whole number), and the fractional bits let the hardware get within a fraction of a percent instead of being stuck rounding to the nearest whole divisor. The code above computes `16 × USARTDIV` as a single integer division (`16,000,000 / 115,200 = 138`, floor), then reconstructs the mantissa (`138 / 16 = 8`) and fraction (`138 % 16 = 10`) from it — the resulting real baud rate lands at ≈115,942 Hz, about 0.6% off nominal, comfortably inside UART's typical ~2–3% tolerance for reliable framing. This exact scenario (16 MHz HSI → 115200 baud on a NUCLEO's USART2) is close to the single most common configuration in STM32 tutorials in existence, which is part of why it was safe to trust after cross-checking the formula rather than needing to spike it on hardware first.

**Framing** is the other half of "how does the receiver know where one byte starts and stops" without a shared clock line (unlike SPI): UART agrees on a *bit rate* in advance, then wraps each byte in a start bit (a guaranteed 0→1 transition the receiver synchronizes on) and one or more stop bits (guaranteed idle-high), sampling the line at the agreed rate in between. This is why baud-rate *mismatch* between two devices doesn't fail cleanly — it doesn't refuse to connect, it just samples at slightly the wrong moments and produces garbled bytes, which is exactly the failure mode Step 2.2's by-hand serial checkpoint (`screen /dev/tty.usbmodem* 115200`) is designed to catch: if the banner prints as readable text, the whole chain (BRR math, GPIO AF wiring, ST-Link's VCP bridge) is correct end to end; if it prints as noise, baud rate mismatch is the first thing to suspect.

`usart2_write_byte`/`usart2_try_read_byte` are **polled**, not interrupt-driven — the loop just busy-checks `USART_SR_TXE`/`USART_SR_RXNE` before touching `DR`. That's a deliberate simplicity choice appropriate for a single-threaded demo program with nothing else to do while waiting; Part 13 covers why the *host*-side serial reader (Step 7, still ahead) can't get away with the equivalent shortcut.

---

# PART III — THE COURIER AND THE PUPPETEER

*(plan 006 §2.2: the Courier "makes a one-way trip"; the Puppeteer "halts the chip mid-instruction... the most powerful of the four.")*

## Part 9 — SWD: a protocol that reaches into a running CPU from outside

I2C and SPI are protocols *you* designed a conversation around — you chose the sensor, you wrote the driver, the peripheral chip has no idea it's being debugged. **SWD (Serial Wire Debug)** is categorically different: it's a protocol built into the Cortex-M core's own silicon specifically so an *external* device can pause, inspect, and resume the core *without the running program's cooperation or even its awareness*. Two wires do this (`[verified]`, and it's why the ST-Link needs only one small connector, not a wide parallel bus): **SWCLK** (a clock the debug probe drives) and **SWDIO** (a single bidirectional data line — the probe and the chip take turns driving it, unlike SPI's separate MOSI/MISO). This is ARM's newer, pin-frugal alternative to the older, still-common **JTAG** (4–5 wires: TCK/TMS/TDI/TDO/optional TRST) — both protocols ultimately talk to the same on-chip **Debug Access Port**, just with different physical-layer wire counts and framing.

What SWD actually grants the probe, once connected, is direct read/write access to the core's internal debug registers and, through them, the entire memory-mapped address space from Part 2 — which is the mechanism, not magic, behind "GDB can read a variable's value": the debug probe (ST-Link) reads the memory location that variable lives at, over SWD, while the CPU is halted, exactly the same memory-mapped-I/O address space `regs.h` uses, just accessed from *outside* the chip instead of by code running *on* it.

**Hardware breakpoints**, the mechanism `set_breakpoint` (Step 6, still ahead) will eventually rely on, are a specific, separate piece of debug silicon: the **Flash Patch and Breakpoint (FPB) unit**, which on Cortex-M4 (this chip) provides **8 instruction-address comparators** (`[verified]` — Cortex-M0/M0+ only get 4; M3/M4/M7 get 8) — dedicated hardware that watches the instruction fetch address bus and signals a halt the instant it matches a programmed address, with **zero modification to the program's own code or Flash contents**. This is the deep reason hardware breakpoints exist as a *separate* mechanism from the older, simpler idea of a software breakpoint (temporarily overwriting an instruction with a trap opcode, then restoring it): software breakpoints require writable code memory to patch, which either doesn't exist (code running from ROM/Flash directly, as here) or is expensive to rewrite constantly; hardware comparators cost nothing per breakpoint hit/removal beyond programming a register, at the price of a hard ceiling — this chip cannot have a ninth simultaneous hardware breakpoint, a real, concrete limit Step 6's `DebugTools` will eventually need to surface honestly rather than silently fail past.

## Part 10 — OpenOCD: the bridge, and why it has to be a separate long-lived process

Neither your Mac nor GDB itself speaks SWD — nothing on a laptop has the electrical hardware to drive those two wires. **OpenOCD** (Open On-Chip Debugger) is the piece that does: it talks USB to the ST-Link probe on one side (which then speaks SWD to the chip), and it exposes a **GDB remote-serial-protocol server over a plain TCP socket** (conventionally port 3333) on the other side. This is *exactly* the shape plan 006's Stage 3 flagged as the one genuinely new process-management pattern in this codebase (§ "Architecture," the `DebugSession` box): every other external-tool integration so far in this platform (and SPICE's planned `ngspice` integration) is one short-lived `Process.Start` → wait → parse call. A debug session needs **two coordinated, long-lived processes** — OpenOCD has to keep running for the *entire* debug session (it's the only thing physically holding the SWD link open), while GDB attaches to it, over TCP, as a client that can connect and disconnect independently. This is architecturally much closer to `VoxelViewerBroadcastService` (a `BackgroundService` that stays up for the process's lifetime, ADR-010) than to a `Process.Start`/`WaitForExit` shell-out — worth remembering when Step 6 actually designs `DebugSession`, because reaching for the simpler shell-out pattern by habit would be a real design mistake here, not just a style choice.

```
   arm-none-eabi-gdb  ◄──TCP:3333, GDB remote protocol──►  OpenOCD  ◄──USB, vendor protocol──►  ST-Link probe  ◄──SWD (2 wires)──►  STM32F401RE core
   (short-lived,                                           (long-lived —                                                          (halted/running/
    per debug session)                                      IS the SWD link)                                                       single-stepping)
```

`make flash` (Step 2's `Makefile`) uses OpenOCD *without* GDB at all — `openocd ... -c "program firmware.elf verify reset exit"` is OpenOCD's own built-in Flash-programming command, a one-shot batch mode that connects, writes, verifies, resets the chip, and exits, with no debug session or TCP server ever started. This is worth noticing precisely because it demonstrates OpenOCD is doing **two genuinely separate jobs** behind one binary: a Flash programmer (used once, briefly, by `make flash` and Step 5's `flash_firmware`) and a GDB-server bridge (used continuously, by Step 6's `DebugSession`) — the same tool, two different modes, and the "why is this a one-way trip" mechanics of the former deserve their own short explanation.

**Why flashing is mechanically irreversible, not just "risky by policy":** NOR Flash memory (what this chip's program storage is built from) can only be **programmed** in one direction at the bit level — a programming operation can change a bit from `1` to `0`, never the reverse. Setting bits back to `1` requires a separate, coarser-grained **erase** operation that clears an entire sector at once (not a single byte), and erase is what actually costs the flash cell wear-out lifetime (typically on the order of 10,000–100,000 cycles per sector on this class of chip). So "flash new firmware" is mechanically: erase a sector back to all-`1`s, then program the new image's `0` bits in — there is no operation that just "writes the old bytes back" unless you *kept a copy of them first*, which is exactly why plan 006 §2.2 calls the Courier's trip one-way: the mechanism genuinely has no undo, only "flash something else over it."

## Part 11 — GDB/MI: a debugger's protocol for talking to a program instead of a person

You've used interactive GDB — a `(gdb)` prompt, typed commands, English-shaped output meant for your eyes. **GDB/MI** (Machine Interface) is a *second*, parallel command language GDB also understands, specifically designed for another *program* to drive — an IDE, or, here, `DebugTools`. This is precisely the same design instinct as MCP's own tool-annotation system (plan 006 §2.3) or a REST API's JSON responses versus its human-readable docs: **a machine consumer needs structure it can parse deterministically, not prose it has to guess at.**

Invoked with `gdb --interpreter=mi2` (or `mi3`), every line GDB emits is a tagged **record**, and the tag alone tells a parser what kind of thing it's looking at before parsing anything else — `[verified]` against GDB's own documentation:

| Prefix | Record type | Meaning |
|---|---|---|
| `^` | Result record | The direct response to a command you sent — `^done`, `^running`, `^error,msg="..."` |
| `*` | Exec-async record | The *target*'s execution state changed — `*stopped,reason="breakpoint-hit",...` |
| `=` | Notify-async record | Supplementary info the client should track — `=breakpoint-created,...` |
| `~` | Console stream | Human-readable text, same as what you'd see at an interactive prompt |
| `&` | Log stream | GDB's own internal/debug chatter |
| `@` | Target stream | Output the *debuggee program itself* printed (irrelevant here — this program only talks over the separate UART, Part 8, not GDB's own stdio channel) |

A worked example, the shape Step 6's `GdbMiParser` will need to handle for real:

```
-break-insert main
^done,bkpt={number="1",type="breakpoint",disp="keep",enabled="y",addr="0x0800012c",func="main",...}
```

The leading `-` line is the *command you send* (MI commands are always dash-prefixed, distinct from interactive GDB's bare `break main`); everything after is GDB's structured response — a `^done` result record carrying a `bkpt=` field whose value is itself a nested key/value record. This is precisely the correctness-boundary argument plan 006 §2.5 makes explicit, and it's worth connecting directly to something you already learned building the SPICE feasibility lecture (008): parsing this server-side and handing the agent `{"address": "0x0800012c", "function": "main"}` instead of raw text is the *identical* move as SPICE's `add_rc_lowpass` computing R/C in C# instead of asking a language model to do arithmetic — **every field extracted from a structured record instead of regex'd out of prose is a class of "the model misread a hex dump" eliminated before it can happen**, not a nicety.

---

# PART IV — THE SCRIBE AND THE GATEKEEPER

## Part 12 — Bridging a push-model peripheral into a pull-model tool call

MCP tools are fundamentally request/response: an agent calls `read_serial_window()`, gets an answer, the call ends. A UART port is fundamentally the opposite — bytes arrive *whenever the chip decides to send them*, with no relationship to when a tool call happens to be in flight. If `read_serial_window()` tried to read the port directly, it would only ever see whatever arrived in the brief window that specific call happened to be running, silently losing everything that arrived between calls.

The fix (plan 006 §2.6, still ahead as Step 7) is a **producer/consumer** pattern you've almost certainly already used in some form on the embedded side, just now appearing on the *host* process instead of the microcontroller: a `SerialReaderService : BackgroundService` runs continuously, for the entire life of the Host process, doing nothing but draining the port into a **bounded ring buffer** (fixed capacity, oldest bytes evicted once full — the same "never grow unbounded" discipline `OutputLimiter` already enforces on tool *output*, just applied to *buffered input* instead). `read_serial_window()` then just reads a snapshot of that buffer — a pure, fast, non-blocking operation completely decoupled from the port's own timing. This is architecturally the mirror image of `VoxelViewerBroadcastService` (ADR-010): that service *pushes* state out continuously to a passive browser; this one *pulls* state in continuously from a passive port — same "a toolset may bring its own long-lived companion infrastructure, independent of any single tool call" pattern, applied in the opposite data direction.

## Part 13 — Safety as architecture, not as a warning label

`PhysicalActionGate` (Step 4) is worth understanding as an instance of two general, transferable software-security patterns — not an embedded-specific quirk:

**Fail-closed defaults.** `HardwareActionsAllowed` defaults to `false` unless `TOOLBOX_ALLOW_HARDWARE_ACTIONS` is *explicitly, exactly* `"true"` or `"1"` — no synonym guessing (`PhysicalActionGateTests`' `"yes"` case is deliberately asserted `false`, proving the narrowness is enforced, not just documented). This is the same instinct behind a firewall that denies by default and allow-lists exceptions, rather than allowing by default and block-listing known-bad — **the safe state is the one that requires no correct action from anyone to reach**, and an operator who does nothing, or misspells the env var, lands on the safe side automatically.

**Defense in depth**, specifically the "absent from the catalog, not merely refusing at call time" argument (plan 006 §2.3): the plan's design goes out of its way to make `physical`-tier tools not just *reject* when disallowed, but never be *registered* at all — an agent literally cannot see `flash_firmware` exists in its tool list unless the operator opted in. This mirrors a pattern you'll meet constantly in production access-control design: a user with no permission to a resource often shouldn't even see the resource exists in a list (information-disclosure minimization), not just be denied when they try to act on it. Layered *on top* of that (registration-time gating), the confirm-token flow (`IssueToken`/`TryConsumeToken`) is a second, independent layer for the single highest-stakes action (`flash_firmware`) specifically — structurally the same two-step shape as a CSRF token, an OAuth authorization code, or a "type DELETE to confirm" destructive-action UX: **a token that proves "the caller was shown a specific summary and chose to proceed," single-use so it can't be replayed, short-lived so an old, stale intent can't execute unexpectedly late.** None of this is embedded-specific — it's exactly the shape you'd want for, say, an agent tool that can drop a production database table, and it's worth recognizing that this toolset is where the pattern first *had* to exist in this codebase, not because hardware is special, but because this is the first tool tier in the platform with a consequence that isn't a `git revert` away.

---

# PART V — THE .NET MACHINERY UNDERNEATH — AND A REAL BUG

## Part 14 — Shelling out safely, and a genuine deadlock risk in `BuildTools`

Here's the honest finding this lecture owes you, in the same spirit as Lecture 009's audit: `BuildTools.RunMake` (Step 3, shipped code, currently passing every test) has a latent bug.

```csharp
using Process process = Process.Start(startInfo)
    ?? throw new InvalidOperationException("failed to start 'make' — is it on PATH?");

string stdout = process.StandardOutput.ReadToEnd();
string stderr = process.StandardError.ReadToEnd();
process.WaitForExit();
```

This is a documented, famous .NET footgun — Microsoft's own `Process.StandardError` docs warn about it by name. The OS gives each redirected stream (stdout, stderr) a **finite pipe buffer** (commonly 64 KB). `ReadToEnd()` on `StandardOutput` **blocks until the child process closes its stdout handle** — which normally means "until the process exits." If, while that call is blocked, the child process writes enough to **stderr** to fill *its* pipe buffer, the child's write to stderr blocks too (the OS won't accept more bytes until something reads them out). Now both sides are stuck: the child is blocked writing stderr, waiting for someone to drain it; the parent is blocked reading stdout, waiting for the child to finish — and the child can never finish, because it's stuck mid-write. **Deadlock**, and it never shows up until a build produces enough combined warnings/errors on the *unread* stream to fill that buffer — which is exactly why every test in `BuildToolsTests` passed cleanly (a one-line compile error is nowhere near 64 KB) while the risk sat there unnoticed the whole time.

The fix (worth doing before Step 6 ever pipes verbose GDB output through similar code, where hitting 64 KB is far more plausible) is to **read both streams concurrently**, never one fully before starting the other — either via `Process.OutputDataReceived`/`ErrorDataReceived` event handlers with `BeginOutputReadLine()`/`BeginErrorReadLine()`, or by kicking off both `ReadToEndAsync()` calls before awaiting either:

```csharp
Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
Task<string> stderrTask = process.StandardError.ReadToEndAsync();
await Task.WhenAll(stdoutTask, stderrTask);
process.WaitForExit();
```

This is a real, concrete example of your own stated growth area (async/non-blocking I/O, "why does a seemingly-correct synchronous sequence actually deadlock") — not a hypothetical one. Worth filing as a small fix before Step 6 builds `DebugSession` on the same `Process` foundation.

## Part 15 — Constructor-based DI: how the container actually chooses

`PhysicalActionGate` deliberately ships **two public constructors**:

```csharp
public PhysicalActionGate(TimeProvider clock)
    : this(Environment.GetEnvironmentVariable(EnvironmentVariableName), clock) { }

public PhysicalActionGate(string? rawEnvironmentValue, TimeProvider clock) { ... }
```

and `EmbeddedToolsetExtensions` relies on the DI container choosing the *first* one automatically, with no attribute telling it to. This works because of a specific, documented rule in `Microsoft.Extensions.DependencyInjection`'s activation logic (`ActivatorUtilities`, the same machinery behind every `[McpServerToolType]` your tool classes rely on): given multiple public constructors, it picks the one where **every parameter type is resolvable from the service provider**, preferring the constructor with the *most* satisfiable parameters when more than one qualifies. `TimeProvider` is registered (`AddToolBoxCore()`); a bare `string?` is not, and never will be — nothing registers a naked string in this container — so the two-argument `(string?, TimeProvider)` constructor is **permanently ineligible** for DI resolution, leaving the one-argument constructor as the only candidate. This is a genuinely useful, reusable trick: **a constructor overload with an unregistrable parameter type is an automatic "test-only" seam, no `[InternalsVisibleTo]`, no factory abstraction, no test-specific DI wiring needed** — and it's worth recognizing the *general* rule (container picks the most-satisfiable public constructor) rather than memorizing this one case, because it'll explain real behavior the next time a DI container does something that looks like magic.

## Part 16 — `TimeProvider`: buying determinism for the price of one indirection

`PhysicalActionGate`'s token-expiry tests (`TryConsumeToken_AfterExpiry_ReturnsExpired`) never call `Thread.Sleep`. They do this instead:

```csharp
clock.Now += TimeSpan.FromMinutes(3);
```

The entire payoff of `PhysicalActionGate` (and `ServerInfoProvider` before it, same pattern) taking `TimeProvider` through its constructor instead of calling `DateTimeOffset.UtcNow` directly is that **time itself becomes an injectable dependency** — production gets `TimeProvider.System` (the real clock), tests get a `TestClock : TimeProvider` whose `GetUtcNow()` returns whatever the test sets, instantly, deterministically, with zero wall-clock time actually elapsing. Compare the alternative a naive test of a 2-minute expiry window would need: either `Thread.Sleep(TimeSpan.FromMinutes(2))` (a genuinely 2-minute-slow test — unacceptable in any CI budget) or a flaky race against real elapsed time. This is the general answer to "how do you unit-test anything with a timeout, an expiry, or a schedule in it" — inject the clock, never call the static one directly in code you intend to test, a rule worth carrying into every future project regardless of language.

---

# PART VI — CONNECTING IT BACK

## Part 17 — What's shipped, what's ahead, and which concepts each step will need

| Plan 006 step | Status | Concepts from this lecture it exercises |
|---|---|---|
| 1 — Scaffolding | Done | (platform conventions only — no new concepts here) |
| 2 — Reference firmware | Authored, **hardware verification still pending** | Parts 1–8, in full — this is where all of them live |
| 3 — Build tools | Done, **Part 14's bug still open** | Part 3 (the compile pipeline `build_firmware` shells out to), Part 14 |
| 4 — Physical-action gating | Done | Part 13 (safety design), Parts 15–16 (the DI/`TimeProvider` mechanics that make it testable) |
| 5 — Flash tools `[HW]` | Ahead | Part 10 (OpenOCD's programming mode), Part 10's flash-irreversibility mechanics, Part 13 (the confirm-token flow gets its first real caller) |
| 6 — Debug session `[HW]` | Ahead | Part 9 (SWD, FPB hardware breakpoints), Part 10 (the two-process OpenOCD+GDB shape), Part 11 (GDB/MI, in full) — the single largest step, and the one this lecture spent the most preparation on |
| 7 — Serial tools `[HW]` | Ahead | Part 8 (framing/baud), Part 12 (the producer/consumer bridge) |
| 8 — Host wiring + docs | Ahead | Ties every ADR this plan will need back to the precedents named throughout (ADR-005, ADR-009, ADR-010) |
| 9 — Demo pass | Ahead | Everything, end to end |

---

## Part 18 — Concept index

| Term | First introduced | One-line definition |
|---|---|---|
| Freestanding execution | Part 1 | A C environment with no guaranteed standard library or OS beneath `main` |
| Memory-mapped I/O | Part 2 | Hardware registers addressed identically to ordinary memory |
| AHB / APB | Part 2 | Fast/full-speed vs. simpler/slower internal peripheral buses |
| Target triple | Part 3 | `arch-vendor-abi` naming for what a cross-compiler emits code for |
| Thumb-2 | Part 3 | The only instruction encoding Cortex-M cores execute |
| LMA / VMA | Part 4 | A section's physical load address in Flash vs. its run-time address in RAM |
| `.bss` | Part 4 | Zero-initialized globals — not stored in the binary, just a byte count |
| Vector table | Part 5 | The fixed table at address 0 the core reads unconditionally at reset |
| Weak alias | Part 5 | A symbol silently overridable by a later strong definition |
| ELF sections vs. symbols | Part 6 | The linker's layout view vs. the debugger's name-to-address view |
| `volatile` | Part 7 | Forces every read/write to actually happen — hardware can change this without the compiler seeing why |
| Atomic set/reset (BSRR) | Part 7 | A single-write hardware primitive avoiding read-modify-write races |
| UART framing | Part 8 | Start/stop bits synchronizing an unclocked serial line |
| SWD | Part 9 | The 2-wire protocol letting an external probe halt/inspect a running core |
| FPB hardware breakpoints | Part 9 | Dedicated silicon comparators, 8 on this chip, needing no code patching |
| OpenOCD | Part 10 | The USB↔SWD↔TCP bridge translating between a probe and GDB |
| Flash erase/program asymmetry | Part 10 | Why "undo a flash write" isn't a real operation |
| GDB/MI | Part 11 | GDB's machine-readable command protocol, parallel to its human CLI |
| Producer/consumer + ring buffer | Part 12 | Decoupling a continuous stream from discrete, on-demand reads |
| Fail-closed default | Part 13 | The safe state requires no correct action to reach |
| Defense in depth | Part 13 | Independent, stacked controls — absence + hint + confirm-token |
| `Process` stdout/stderr deadlock | Part 14 | Reading one redirected stream fully can block forever if the other's OS buffer fills |
| DI constructor selection | Part 15 | The container picks the most-satisfiable public constructor automatically |
| `TimeProvider` | Part 16 | Injectable clock — deterministic, sleep-free tests for anything time-based |

## Part 19 — What to carry away

1. **A vector table is data pretending to be a program's first instruction, and its first two entries are the only hardware-mandated ones on this whole chip.** Everything else in the boot story — `.data` copying, `.bss` zeroing — is a *software convention* the C standard requires, not something the silicon itself demands.
2. **The Puppeteer is powerful precisely because it doesn't ask the running program's permission.** SWD's whole design point is external, unilateral control — which is exactly why the *safety* design in Part 13 exists on the host side, not on the chip: nothing about the chip's own debug interface has an opinion about whether it *should* be halted, only whether it *can* be, which it always can.
3. **The correctness-boundary argument keeps reappearing because it's not an embedded-specific idea.** SPICE's server-side R/C math, GDB/MI's structured records, and this toolset's parsed-not-prose design goal are the same move: push interpretation to the one place that can do it deterministically, and hand the agent a conclusion instead of raw material to misread.
4. **A real, currently-harmless bug shipped in Step 3, and finding it didn't require the board.** `Process.StandardOutput`/`StandardError` deadlock risk is a pure-.NET fact, catchable by knowing the pattern, not by running more hardware tests — a reminder that "no CI can test the hardware path" (plan 006 §2.7) doesn't mean nothing about this toolset is testable; it means knowing *which* bugs are hardware bugs and which are ordinary software bugs wearing an embedded costume.
5. **This whole plan is a portfolio-relevant argument, not a hardware detour.** Cross-compilation, linker mechanics, protocol design (GDB/MI's tagged records look a lot like any structured wire protocol you'll design for a service), and fail-closed security defaults are all things a backend/systems interviewer will recognize instantly — the chip is the excuse to build all of them for real, not the point.

## Sources

Facts marked `[verified]` above were checked against the following during this lecture's preparation, not recalled from memory alone:

- [ST RM0368 — STM32F401xB/C and STM32F401xD/E reference manual](https://www.st.com/resource/en/reference_manual/dm00096844-stm32f401xb-c-and-stm32f401xd-e-advanced-arm-based-32-bit-mcus-stmicroelectronics.pdf) — memory map, bus layout, USART register/baud-rate formula (also the primary source already cited in plan 006 Step 2's own log)
- [ARM Developer — Flash Patch and Breakpoint Unit (FPB)](https://developer.arm.com/documentation/100166/latest/Debug/Flash-Patch-and-Breakpoint-Unit--FPB-) and [Memfault — How do breakpoints even work?](https://interrupt.memfault.com/blog/cortex-m-breakpoints) — hardware breakpoint comparator counts by Cortex-M variant
- [GDB/MI Output Syntax — GNU GDB documentation](https://sourceware.org/gdb/current/onlinedocs/gdb.html/GDB_002fMI-Output-Syntax.html) and [GDB/MI Stream Records](https://sourceware.org/gdb/current/onlinedocs/gdb.html/GDB_002fMI-Stream-Records.html) — record-prefix grammar (`^`/`*`/`=`/`~`/`&`/`@`)
- Microsoft's own `Process.StandardError`/`Process.StandardOutput` API documentation — the redirected-stream deadlock warning, and `ActivatorUtilities`' documented constructor-selection behavior — general .NET platform facts, not project-specific claims, so treated as stable background knowledge rather than re-verified line by line.
