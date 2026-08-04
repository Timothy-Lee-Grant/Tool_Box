/* Minimal Cortex-M4 startup for the STM32F401RE.
 *
 * Reset_Handler copies .data from flash to RAM, zeroes .bss, then calls
 * main(). The vector table below lists only the ARM core exceptions
 * (RM0368's Cortex-M4 table, positions 0-15) — no peripheral IRQ entries,
 * because main.c polls every peripheral and never unmasks an NVIC line.
 * Standard pattern (this is conceptually the same shape as ST's own
 * CMSIS startup_stm32f401xe.s), hand-written here rather than pulled in,
 * for the same reproducibility reason as regs.h.
 */
    .syntax unified
    .cpu cortex-m4
    .thumb

    .global Reset_Handler
    .global g_pfnVectors

    .section .text.Reset_Handler
    .type Reset_Handler, %function
Reset_Handler:
    /* Copy .data's initial values from flash (_sidata, the LMA) to
     * RAM (_sdata.._edata, the VMA) — see linker.ld for how these
     * symbols are defined. */
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

    /* Zero .bss. */
    ldr r1, =_sbss
    ldr r2, =_ebss
    movs r3, #0
ZeroBss:
    cmp r1, r2
    bcs ZeroBssDone
    str r3, [r1], #4
    b ZeroBss
ZeroBssDone:

    bl main

Infinite_Loop:
    b Infinite_Loop
    .size Reset_Handler, .-Reset_Handler

/* Catch-all for any exception this program doesn't distinguish. In
 * normal operation none of these should ever fire — nothing here
 * enables an NVIC interrupt — so landing here means something (a bad
 * pointer, a fault) went wrong, and looping in place beats running off
 * into undefined memory. */
    .section .text.Default_Handler
    .type Default_Handler, %function
Default_Handler:
    b Default_Handler
    .size Default_Handler, .-Default_Handler

    .weak NMI_Handler
    .thumb_set NMI_Handler, Default_Handler
    .weak HardFault_Handler
    .thumb_set HardFault_Handler, Default_Handler
    .weak MemManage_Handler
    .thumb_set MemManage_Handler, Default_Handler
    .weak BusFault_Handler
    .thumb_set BusFault_Handler, Default_Handler
    .weak UsageFault_Handler
    .thumb_set UsageFault_Handler, Default_Handler
    .weak SVC_Handler
    .thumb_set SVC_Handler, Default_Handler
    .weak DebugMon_Handler
    .thumb_set DebugMon_Handler, Default_Handler
    .weak PendSV_Handler
    .thumb_set PendSV_Handler, Default_Handler
    .weak SysTick_Handler
    .thumb_set SysTick_Handler, Default_Handler

    /* Entry 0 is the initial main-stack-pointer value (a data word, not
     * a code address — the one entry that isn't a handler symbol).
     * Every entry from 1 onward gets its Thumb bit set automatically by
     * the assembler/linker because each symbol is Thumb-mode code. */
    .section .isr_vector,"a",%progbits
    .type g_pfnVectors, %object
g_pfnVectors:
    .word _estack
    .word Reset_Handler
    .word NMI_Handler
    .word HardFault_Handler
    .word MemManage_Handler
    .word BusFault_Handler
    .word UsageFault_Handler
    .word 0 /* Reserved */
    .word 0 /* Reserved */
    .word 0 /* Reserved */
    .word 0 /* Reserved */
    .word SVC_Handler
    .word DebugMon_Handler
    .word 0 /* Reserved */
    .word PendSV_Handler
    .word SysTick_Handler
    .size g_pfnVectors, .-g_pfnVectors
