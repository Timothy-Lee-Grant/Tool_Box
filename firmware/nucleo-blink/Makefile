# Builds nucleo-blink for the STM32F401RE — plan 006 Step 2's reference
# firmware. Not part of ToolBox.slnx (same "outside the .NET solution"
# pattern as viewer/); plan 006's build_firmware tool (Step 3) shells out
# to this Makefile rather than reimplementing it.

TARGET  := nucleo-blink
BUILD   := build

PREFIX  := arm-none-eabi-
CC      := $(PREFIX)gcc
SIZE    := $(PREFIX)size

# Cortex-M4, soft float: this program never touches the FPU, so there's
# nothing -mfloat-abi=hard would buy it, and soft float keeps the ABI
# simplest for a from-scratch startup file.
MCU_FLAGS := -mcpu=cortex-m4 -mthumb -mfloat-abi=soft

CFLAGS  := $(MCU_FLAGS) -std=c11 -Wall -Wextra -Og -g3 \
           -ffreestanding -fno-builtin -fdata-sections -ffunction-sections
LDFLAGS := $(MCU_FLAGS) -T linker.ld -nostdlib -nostartfiles \
           -Wl,--gc-sections -Wl,-Map=$(BUILD)/$(TARGET).map

OBJS := $(BUILD)/main.o $(BUILD)/startup.o

.PHONY: all clean flash

all: $(BUILD)/$(TARGET).elf

$(BUILD):
	mkdir -p $(BUILD)

$(BUILD)/main.o: main.c regs.h | $(BUILD)
	$(CC) $(CFLAGS) -c $< -o $@

$(BUILD)/startup.o: startup.s | $(BUILD)
	$(CC) $(MCU_FLAGS) -c $< -o $@

$(BUILD)/$(TARGET).elf: $(OBJS) linker.ld
	$(CC) $(LDFLAGS) $(OBJS) -o $@
	$(SIZE) $@

# Manual, by-hand flash target — plan 006 Step 2.2's checkpoint runs this
# once from the command line, before any C# tool ever calls openocd.
flash: $(BUILD)/$(TARGET).elf
	openocd -f interface/stlink.cfg -f target/stm32f4x.cfg \
	        -c "program $(BUILD)/$(TARGET).elf verify reset exit"

clean:
	rm -rf $(BUILD)
