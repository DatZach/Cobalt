;
;	stage1.asm
;	X86 CoFS Stage 1 Bootloader
;
;   NASM Syntax, I forgot I like FASM more Q_Q
;
;	Bootloader Memory Map:
;		0x00000000 - 0x000003FF - Real Mode Interrupt Vector Table
;		0x00000400 - 0x000004FF - BIOS Data Area
;		0x00000500 - 0x00007BFF - Stage 2 (29 KiB)
;		0x00007C00 - 0x0000DFFF - Stage 1
;		0x0000E000 - 0x0000EFFF - Free (27 KiB)
;		0x0000F000 - 0x0000FBFF - Memory map (127 entries)
;		0x0000FC00 - 0x0000FFFF - Stage 1/2 Stack
;		0x00010000 - 0x0006FFFF - Relocation Buffer (383 KiB)
;		0x0007E200 - 0x00095000 - Paging Tables
;		0x00095000 - 0x0009FFFF - Free (32 KiB)
;		0x000A0000 - 0x000BFFFF - Video RAM (VRAM) Memory
;		0x000B0000 - 0x000B7777 - Monochrome Video Memory
;		0x000B8000 - 0x000BFFFF - Color Video Memory
;		0x000C0000 - 0x000C7FFF - Video ROM BIOS
;		0x000C8000 - 0x000EFFFF - BIOS Shadow Area
;		0x000F0000 - 0x000FFFFF - System BIOS
;

org     0x7C00
bits    16

%define MdtBuffer 0x7E00

jmp     Main

times 8 - ($-$$) db 0

FSCB:
.Magic                  dd  0
.Version                db  0
.BytesPerSector         dw  0
.SectorsPerCluster      db  0
.LogClustersPerBlock    db  0
.TotalClusters          dd  0
.MdtCluster             dd  0
.Checksum               dd  0

;
;   Panic
;   Registers Clobbered
;       None
;   Arguments
;       AL      Error Code
;
Panic:
    mov     si, errorMsg
    add     al, '0'
    mov     byte [si + 6], al

    mov     ah, 0x0E
.NextChar:
    lodsb
    or      al, al
    jz      .PanicDone
    int     0x10
    jmp     .NextChar

.PanicDone:
    cli
    hlt

;
;	ReadClusters
;	Registers Clobbered
;		None
;	Arguments
;		AX			Cluster
;		BX			Buffer
;	Returns
;		EBX			Populated
;		CF			Set on error
;
ReadClusters:
	pushad

    mul     byte [FSCB.SectorsPerCluster]
    inc     ax

	; Populate packet
	mov	    dword [DiskPacket.Buffer], ebx
	mov	    dword [DiskPacket.Sector], eax

    movzx   ax, byte [FSCB.SectorsPerCluster]
	mov		word [DiskPacket.Blocks], ax
    mul     cx

	; Call interrupt
	mov		ah, 0x42
	mov		dl, byte [bootDrive]
	xor		bx, bx
	mov		ds, bx
	mov		si, DiskPacket
	int		0x13
	
	popad
	ret

Main:
    ; Prepare stack
	xor		ax, ax
	mov		ds, ax
	mov		sp, 0x7C00
	
	; Save boot device
	mov		byte [bootDrive], dl

    ; 1 - Load MDT
    mov     eax, dword [FSCB.MdtCluster]
    mov     ebx, MdtBuffer
    mov     ecx, 1
    call    ReadClusters
    jnc     .FindEntryInit
    mov     al, 1
    call    Panic

    ; 2 - Find BOOT Entry
.FindEntryInit:
    mov     ebx, MdtBuffer
.FindEntry:
    mov     edx, dword [ebx]
    cmp     edx, 0x544F4F42
    je      .FoundEntry
    cmp     edx, 0
    je      .FindEntryError
    add     ebx, 32
    jmp     .FindEntry
.FindEntryError:
    mov     al, 2
    call    Panic
.FoundEntry:

    ; TODO Either spec needs to specify that BOOT appears before MDXT 
    ;      or we need to implement support for encountering MDXT 

    ; 3 - Read BOOT Clusters
    mov     eax, dword [ebx + 4]
    mov     ecx, dword [ebx + 8]
    mov     ebx, 0x500
    call    ReadClusters
    jnc     .JumpStage2
    mov     al, 3
    call    Panic

    ; 4 - Jump
.JumpStage2:
	mov		dl, byte [bootDrive]
	jmp		0x0000:0x0500
    cli
    hlt

errorMsg                db "Error 0", 0
bootDrive:				dd	0
sectorData:				dd	0
entrySector:			dw	0
entryCluster:			dw	0

DiskPacket:
	.Size               db  0
    .Reserved0          db  0
    .Blocks             dw  0
    .Buffer             dd  0
    .Sector             dq  0

times 510 - ($-$$) db 0
dw 0xAA55
