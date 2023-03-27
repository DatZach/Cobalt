org     0x0500
bits    16

jmp     Main16

%define ADDRESS_BUFFER				0x10000000		; This is in seg:off format (0x10000)
%define ADDRESS_BUFFER_LINEAR		0x00010000		; This is in 64bit canonical format
%define ADDRESS_MEMORY_MAP			0x0000F000
%define MEMORY_MAP_BUFFER_LENGTH	0x00000BFF
%define MEMORY_MIN_SIZE				0x00003C00
%define ADDRESS_BOOT_MODULE_LIST	0x00070000

%include 'Bootloader/panic.inc'
%include 'Bootloader/cpu.inc'
%include 'Bootloader/memory.inc'

;
;   16bit Entry Point
;
Main16:
    mov     byte [KernelBootInfo.BIOSBootDevice], dl

    pusha
    call	CheckCPU
	jc		Errors.CPU
	call	EnableA20
	jc		Errors.A20
    call    EnterUnrealMode
    popa

    call	GetMemorySize
	jc		Errors.Memory
    
	cmp		eax, MEMORY_MIN_SIZE
	jb		Errors.LowMemory
	mov		dword [KernelBootInfo.TotalMemoryKB], eax

	push	es
	xor		ax, ax
	mov		es, ax
	
	mov		eax, MEMORY_MAP_BUFFER_LENGTH
	mov		di, ADDRESS_MEMORY_MAP
	call	GetMemoryMap
	jnc		.Main16_2
    cmp		al, 0
    je		Errors.Memory
    cmp		al, 1
    je		Errors.Memory
    cmp		al, 2
    je		Errors.LowMemory
    jmp		Errors.Unknown

.Main16_2:
	mov		dword [KernelBootInfo.MemoryMapEntries], ebp
	mov		dword [KernelBootInfo.MemoryMap], ADDRESS_MEMORY_MAP
	
	pop		es
	
	call	GetMemoryLow
	jc		Errors.Memory
	mov		word [KernelBootInfo.LowerMemoryKB], ax

    call	CreatePageTables
    lgdt	[GDT64]

    ; Go long mode
    mov		eax, cr4
    or		eax, 0x00000020
    mov		cr4, eax
    
    mov		eax, ADDRESS_PLM4
    mov		cr3, eax
    
    mov		ecx, 0xC0000080
    rdmsr
    or		eax, 0x00000100
    wrmsr
    
    mov		eax, cr0
    or		eax, 0x80000001
    mov		cr0, eax
    
    jmp		0x00000008:Main64

;
;   64bit Entry Point
;
bits    64

Main64:
	mov		rax, 0x0000000000000010
	mov		ds, ax
	mov		es, ax
	mov		fs, ax
	mov		gs, ax
	mov		ss, ax

    mov rdx, 0x1234321

    cli
    hlt

KernelBootInfo:
	.Signature			dd	0xDECADE22
	.LowerMemoryKB		dw	0
	.TotalMemoryKB		dq	0
	.BIOSBootDevice		dw	0
	.MemoryMapEntries	dd	0
	.MemoryMap			dq	0
	.CommandLine		dq	0
	.LoadedModules		dq	0
