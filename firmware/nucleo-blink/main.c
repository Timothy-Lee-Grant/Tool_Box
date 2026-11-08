/* nucleo-blink — plan 006's Step 2 reference firmware for the NUCLEO-F401RE.
 *
 * On boot: blinks the onboard LED (LD2, PA5), doubles the blink rate while
 * the onboard button (B1, PC13) is held, and echoes any byte received on
 * the ST-Link virtual COM port (USART2, PA2/PA3) straight back out.
 *
 * This is the whole v1 demo circuit (plan 006 §2.2/§2.9): nothing here
 * drives anything beyond the board itself, by deliberate scope decision.
 * No CMSIS, no HAL — see regs.h for why, and for exactly which registers
 * this program touches.
 */
#include "regs.h"

#define LED_PIN    5U  /* PA5 — LD2 */
#define BUTTON_PIN 13U /* PC13 — B1, active low (external pull-up on the board) */

#define HSI_HZ    16000000UL /* reset-default clock source; nothing here touches RCC_CFGR */
#define BAUD_RATE 115200UL

static void delay_cycles(volatile uint32_t count)
{
    while (count--)
    {
        __asm__ volatile("nop");
    }
}

static void gpio_init(void)
{
    RCC->AHB1ENR |= RCC_AHB1ENR_GPIOAEN | RCC_AHB1ENR_GPIOCEN;

    /* PA5 as general-purpose output (MODER = 01) for LD2. */
    GPIOA->MODER &= ~(0x3UL << (LED_PIN * 2U));
    GPIOA->MODER |= (0x1UL << (LED_PIN * 2U));

    /* PC13 stays an input (MODER = 00, the reset value) — B1 already has
     * an external pull-up on the board, so no PUPDR change is needed. */

    /* PA2 (TX) / PA3 (RX) as alternate function (MODER = 10), AF7 = USART2. */
    GPIOA->MODER &= ~((0x3UL << (2U * 2U)) | (0x3UL << (3U * 2U)));
    GPIOA->MODER |= ((0x2UL << (2U * 2U)) | (0x2UL << (3U * 2U)));
    GPIOA->AFR[0] &= ~((0xFUL << (2U * 4U)) | (0xFUL << (3U * 4U)));
    GPIOA->AFR[0] |= ((0x7UL << (2U * 4U)) | (0x7UL << (3U * 4U)));
}

static void usart2_init(void)
{
    RCC->APB1ENR |= RCC_APB1ENR_USART2EN;

    /* RM0368 §19.6.3: USARTDIV = fCK / (16 * baud); BRR stores it as
     * mantissa (bits 15:4) + 1/16-fraction (bits 3:0). Computing
     * 16*USARTDIV directly as an integer division and splitting it via
     * /16 and %16 is algebraically the same thing and avoids a separate
     * rounding step — both are compile-time constants here. At 16 MHz
     * HSI / 115200 baud this lands at ~0.6% error, well inside UART's
     * usual tolerance. */
    const uint32_t div16 = HSI_HZ / BAUD_RATE;
    USART2->BRR = ((div16 / 16UL) << 4) | (div16 % 16UL);

    USART2->CR1 = USART_CR1_TE | USART_CR1_RE | USART_CR1_UE;
}

static void led_set(int on)
{
    /* BSRR: bits 0-15 set, bits 16-31 reset the same ODR bit — one
     * atomic write, so this can never race a read-modify-write on ODR
     * (RM0368 §8.4.7). */
    GPIOA->BSRR = on ? (1UL << LED_PIN) : (1UL << (LED_PIN + 16U));
}

static int button_pressed(void)
{
    return (GPIOC->IDR & (1UL << BUTTON_PIN)) == 0U; /* active low */
}

static void usart2_write_byte(uint8_t b)
{
    while ((USART2->SR & USART_SR_TXE) == 0U)
    {
    }
    USART2->DR = b;
}

static int usart2_try_read_byte(uint8_t *out)
{
    if ((USART2->SR & USART_SR_RXNE) == 0U)
    {
        return 0;
    }
    *out = (uint8_t)(USART2->DR & 0xFFU);
    return 1;
}

static void usart2_write_string(const char *s)
{
    while (*s)
    {
        usart2_write_byte((uint8_t)*s++);
    }
}

int main(void)
{
    gpio_init();
    usart2_init();

    usart2_write_string("nucleo-blink up: LD2 blinking, B1 speeds it up, echoing serial input\r\n");

    const uint32_t blink_delay = 800000U;

    while (1)
    {
        led_set(1);
        delay_cycles(button_pressed() ? blink_delay / 4U : blink_delay);
        led_set(0);
        delay_cycles(button_pressed() ? blink_delay / 4U : blink_delay);

        uint8_t rx;
        if (usart2_try_read_byte(&rx))
        {
            usart2_write_byte(rx); /* the Scribe's other half — plan 006's serial
                                       tools poll this echo to prove the round trip */
        }
    }
}
