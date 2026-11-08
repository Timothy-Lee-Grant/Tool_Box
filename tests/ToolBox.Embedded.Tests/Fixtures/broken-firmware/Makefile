# See Fixtures/valid-firmware/Makefile — identical shape, deliberately broken source.
CC := arm-none-eabi-gcc
MCU_FLAGS := -mcpu=cortex-m4 -mthumb -mfloat-abi=soft

.PHONY: all clean

all:
	mkdir -p build
	$(CC) $(MCU_FLAGS) -c main.c -o build/main.o

clean:
	rm -rf build
