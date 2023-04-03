@"C:\Tools\nasm-2.16.01\nasm.exe" -f bin -o Bootloader/bin/stage1.bin Bootloader/stage1.asm
@"C:\Tools\nasm-2.16.01\nasm.exe" -f bin -o Bootloader/bin/stage2.bin Bootloader/stage2.asm
@"C:\Tools\nasm-2.16.01\nasm.exe" -f bin -o Kernel/bin/kernel64.bin Kernel/stub.asm
@"Utility\DiskUtil\bin\Debug\net6.0\DiskUtil.exe" c.img Format ^
	--sectors-per-cluster=8 ^
	--clusters-per-block=1024 ^
	--stage1=Bootloader/bin/stage1.bin ^
	--stage2=Bootloader/bin/stage2.bin
@"Utility\DiskUtil\bin\Debug\net6.0\DiskUtil.exe" c.img CreateDirectory /os
@"Utility\DiskUtil\bin\Debug\net6.0\DiskUtil.exe" c.img WriteFile /os/kernel64.exe Kernel/bin/kernel64.bin

@REM 16mb disk = 4kb cluster, 4mb block
