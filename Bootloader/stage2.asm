;
;	stage2.asm
;	X86 CoFS Stage 2 Bootloader
;
;	Bootloader Memory Map:
;		0x00000000 - 0x000003FF - Real Mode Interrupt Vector Table
;		0x00000400 - 0x000004FF - BIOS Data Area
;		0x00000500 - 0x00007BFF - Stage 2 (29 KiB)
;		0x00007C00 - 0x00007FFF - Stage 1
;		0x00008000 - 0x0000EFFF - MDT Buffer (27 KiB)
;		0x0000F000 - 0x0000FBFF - Memory map (127 entries)
;		0x0000FC00 - 0x0000FFFF - Stack
;		0x00010000 - 0x0006FFFF - Tree Buffer (383 KiB)
;		0x00070000 - 0x00070800 - Boot module list
;		0x0007E200 - 0x00095000 - Paging Tables
;		0x00095000 - 0x0009FFFF - Free (32 KiB)
;		0x000A0000 - 0x000BFFFF - Video RAM (VRAM) Memory
;		0x000B0000 - 0x000B7777 - Monochrome Video Memory
;		0x000B8000 - 0x000BFFFF - Color Video Memory
;		0x000C0000 - 0x000C7FFF - Video ROM BIOS
;		0x000C8000 - 0x000EFFFF - BIOS Shadow Area
;		0x000F0000 - 0x000FFFFF - System BIOS
;		0x00100000 - 0xFFFFFFFF - Kernel
;
org     0x0500
bits    16

jmp     Main16

%define ADDRESS_KERNEL				0x00100000
%define ADDRESS_BUFFER				0x10000000		; This is in seg:off format (0x10000)
%define ADDRESS_BUFFER_LINEAR		0x00010000		; This is in 64bit canonical format
%define ADDRESS_MEMORY_MAP			0x0000F000
%define MEMORY_MAP_BUFFER_LENGTH	0x00000BFF
%define MEMORY_MIN_SIZE				0x00003C00
%define ADDRESS_BOOT_MODULE_LIST	0x00070000

%include 'Bootloader/panic.inc'
%include 'Bootloader/cpu.inc'
%include 'Bootloader/memory.inc'
%include 'Bootloader/cofs.inc'

;
;   16bit Entry Point
;
Main16:
    mov     byte [KernelBootInfo.BIOSBootDevice], dl

	; Put CPU into unreal mode for long addressing
    call	CheckCPU
	jc		Errors.CPU
	call	EnableA20
	jc		Errors.A20
    call    EnterUnrealMode

	; Get memory information
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

	; Load the kernel before we lose BIOS
	xor		eax, eax
	call	FindTreeNode						; rootNode = FindTreeNode(0)
	jc		Errors.KLoadError
	mov		si, filenameOS
	mov		dx, NODE_ATTRIBUTE_DIRECTORY
	call	FindNodeEntry						; osEntry = FindNodeEntry(rootNode, Attributes.Directory, "os")
	jc		Errors.KLoadError
	mov		eax, dword [ebx + NodeEntry.Clusters]
	call	FindTreeNode						; osNode = FindTreeNode(osEntry->clusters[0])
	jc		Errors.KLoadError
	mov		si, filenameKernel
	xor		dx, dx
	call	FindNodeEntry						; kernelEntry = FindNodeEntry(osNode, Attributes.None, "kernel64.exe")
	jc		Errors.KLoadError
	xor		cx, cx
	mov		edi, ADDRESS_KERNEL
.Main16_LoadCluster:
	mov		eax, dword [ebx + NodeEntry.Clusters + ecx * 4]
	call	ReadClusters
	inc		cx
	cmp		cx, 14
	jl		.Main16_LoadCluster
	sub		ebx, ADDRESS_KERNEL
	cmp		ebx, dword [ebx + NodeEntry.DataSize + 4]
	jl		Errors.KLoadError

	; Go to long mode
    call	CreatePageTables
    lgdt	[GDT64]
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

    ; Jump to kernel
	xor		rbp, rbp
	mov		ebp, dword [kernelEntryPoint]
	jmp		rbp

	; Should never get here, halt
    cli
    hlt

; Constants
filenameOS				db	"os", 0
filenameKernel			db	"kernel64.exe", 0

; Variables
kernelEntryPoint		dd	0x00100000

KernelBootInfo:
	.Signature			dd	0xDECADE22
	.LowerMemoryKB		dw	0
	.TotalMemoryKB		dq	0
	.BIOSBootDevice		dw	0
	.MemoryMapEntries	dd	0
	.MemoryMap			dq	0
	.CommandLine		dq	0
	.LoadedModules		dq	0
