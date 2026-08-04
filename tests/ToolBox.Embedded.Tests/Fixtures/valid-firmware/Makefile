# Minimal fixture Makefile for BuildToolsTests — deliberately not the real
# firmware's Makefile (no linking, no board-specific flags needed to prove
# BuildTools' shell-out logic works).
CC := arm-none-eabi-gcc
MCU_FLAGS := -mcpu=cortex-m4 -mthumb -mfloat-abi=soft

.PHONY: all clean

all:
	mkdir -p build
	$(CC) $(MCU_FLAGS) -c main.c -o build/main.o

clean:
	rm -rf build
