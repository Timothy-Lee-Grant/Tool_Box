/* Linker script for the STM32F401RE — 512K flash at 0x08000000, 96K RAM
 * at 0x20000000 (both verified against ST's datasheet during plan 006
 * Step 2). Hand-written rather than CubeMX-generated, same reproducibility
 * reasoning as regs.h — this is meant to be readable end to end, since it
 * doubles as teaching material on the boot process (plan 006 §2.5's Step 2
 * note).
 */
ENTRY(Reset_Handler)

MEMORY
{
    FLASH (rx)  : ORIGIN = 0x08000000, LENGTH = 512K
    RAM   (rwx) : ORIGIN = 0x20000000, LENGTH = 96K
}

/* Initial stack pointer: Cortex-M uses a full-descending stack, so it
 * starts one address past the last valid RAM byte. */
_estack = ORIGIN(RAM) + LENGTH(RAM);

SECTIONS
{
    .isr_vector :
    {
        . = ALIGN(4);
        KEEP(*(.isr_vector))
        . = ALIGN(4);
    } > FLASH

    .text :
    {
        . = ALIGN(4);
        *(.text)
        *(.text*)
        *(.rodata)
        *(.rodata*)
        . = ALIGN(4);
    } > FLASH

    /* _sidata marks where .data's initial values live in FLASH (its load
     * address); _sdata/_edata below mark where Reset_Handler copies them
     * to in RAM (its virtual address) — the "AT> FLASH" on .data is what
     * makes LOADADDR() here differ from the section's own start address. */
    _sidata = LOADADDR(.data);

    .data :
    {
        . = ALIGN(4);
        _sdata = .;
        *(.data)
        *(.data*)
        . = ALIGN(4);
        _edata = .;
    } > RAM AT> FLASH

    .bss :
    {
        . = ALIGN(4);
        _sbss = .;
        *(.bss)
        *(.bss*)
        *(COMMON)
        . = ALIGN(4);
        _ebss = .;
    } > RAM

    /DISCARD/ :
    {
        *(.ARM.exidx*)
        *(.ARM.extab*)
    }
}
