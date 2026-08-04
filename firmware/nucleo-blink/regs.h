/* Minimal, hand-written register definitions for the four STM32F401RE
 * peripherals this program touches (RCC, GPIOA, GPIOC, USART2).
 *
 * Deliberately not ST's CMSIS device header: plan 006 (Documentation/
 * ImplementationPlans/006-Embedded-Firmware-Toolset.md) originally scoped
 * this as "no HAL beyond the vendor's CMSIS device header," but that header
 * is ~25,000 lines to define four peripherals. Hand-rolling just what's
 * used keeps this directory buildable from a fresh clone with nothing
 * beyond arm-none-eabi-gcc, and doubles as the more honest teaching
 * artifact — every address and bit position below is traceable to a
 * specific RM0368 section rather than inherited from an opaque header.
 * Recorded as a deliberate Stage 3 deviation, not silently substituted.
 *
 * Addresses and bit positions cross-checked against RM0368 (STM32F401xB/C
 * and STM32F401xD/E reference manual) via web search during plan 006
 * Step 2 — see that step's Stage 4 log entry for sources. The only
 * verification that actually matters, though, is Step 2.2's checkpoint:
 * a human confirming this runs on real silicon.
 */
#ifndef NUCLEO_BLINK_REGS_H
#define NUCLEO_BLINK_REGS_H

#include <stdint.h>

/* RM0368 §2.3 (Memory map) — AHB1 domain */
#define RCC_BASE    0x40023800UL
#define GPIOA_BASE  0x40020000UL
#define GPIOC_BASE  0x40020800UL
/* APB1 domain */
#define USART2_BASE 0x40004400UL

typedef struct
{
    volatile uint32_t CR;
    volatile uint32_t PLLCFGR;
    volatile uint32_t CFGR;
    volatile uint32_t CIR;
    volatile uint32_t AHB1RSTR;
    volatile uint32_t AHB2RSTR;
    volatile uint32_t RESERVED0[2];
    volatile uint32_t APB1RSTR;
    volatile uint32_t APB2RSTR;
    volatile uint32_t RESERVED1[2];
    volatile uint32_t AHB1ENR;
    volatile uint32_t AHB2ENR;
    /* Fields past this point (APB1ENR onward is what we actually need,
     * kept explicit below rather than padding further) are unused by
     * this program and intentionally omitted. */
    volatile uint32_t RESERVED2[2];
    volatile uint32_t APB1ENR;
    volatile uint32_t APB2ENR;
} RCC_TypeDef;

typedef struct
{
    volatile uint32_t MODER;
    volatile uint32_t OTYPER;
    volatile uint32_t OSPEEDR;
    volatile uint32_t PUPDR;
    volatile uint32_t IDR;
    volatile uint32_t ODR;
    volatile uint32_t BSRR;
    volatile uint32_t LCKR;
    volatile uint32_t AFR[2]; /* AFR[0] = AFRL, pins 0-7; AFR[1] = AFRH, pins 8-15 */
} GPIO_TypeDef;

typedef struct
{
    volatile uint32_t SR;
    volatile uint32_t DR;
    volatile uint32_t BRR;
    volatile uint32_t CR1;
    volatile uint32_t CR2;
    volatile uint32_t CR3;
    volatile uint32_t GTPR;
} USART_TypeDef;

#define RCC    ((RCC_TypeDef *)RCC_BASE)
#define GPIOA  ((GPIO_TypeDef *)GPIOA_BASE)
#define GPIOC  ((GPIO_TypeDef *)GPIOC_BASE)
#define USART2 ((USART_TypeDef *)USART2_BASE)

/* RM0368 §6.3.10 (RCC_AHB1ENR) / §6.3.12 (RCC_APB1ENR) — only the bits
 * this program sets. */
#define RCC_AHB1ENR_GPIOAEN  (1UL << 0)
#define RCC_AHB1ENR_GPIOCEN  (1UL << 2)
#define RCC_APB1ENR_USART2EN (1UL << 17)

/* RM0368 §19.6.1 (USART_SR) */
#define USART_SR_RXNE (1UL << 5)
#define USART_SR_TXE  (1UL << 7)

/* RM0368 §19.6.4 (USART_CR1) */
#define USART_CR1_RE (1UL << 2)
#define USART_CR1_TE (1UL << 3)
#define USART_CR1_UE (1UL << 13)

#endif /* NUCLEO_BLINK_REGS_H */
