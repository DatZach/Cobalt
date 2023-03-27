;
;   cpu.inc
;	x86 CPU Initialization
;

%ifndef __X86_CPU_INC
%define __X86_CPU_INC

bits    16

;
;	Global Descriptor Tables
;

; 32 bit GDT
GDTStart32:
	dq 0x0000000000000000					; Null descriptor
	dq 0x00CF9A000000FFFF					; Code descriptor
	dq 0x00CF92000000FFFF					; Data descriptor
GDT32:
	dw GDT32 - GDTStart32 - 1				; Size of GDT
	dd GDTStart32							; Base of GDT

; 64 bit GDT
GDTStart64:
	dq 0x0000000000000000					; Null descriptor
	dq 0x00AF9A000000FFFF					; Code descriptor
	dq 0x00AF92000000FFFF					; Data descriptor
GDT64:
	dw GDT64 - GDTStart64 - 1				; Size of GDT
	dd GDTStart64							; Base of GDT

;
;	EnableA20
;	No registers are clobbered
;	Arguments:
;		None
;	Returns:
;		CF			Set on error
;
EnableA20:
	pusha
	
	; Check A20 before we do anything
	call	.CheckA20
	jnc		.Done
	
    ; METHOD 1 - BIOS
.Method1:
    mov		ax, 0x2401				; BIOS Enable A20
    int		0x15
    jc		.Method2				; Failed, try next method
    cmp		ah, 0x00				; AH == 0x00 = Success
    jne		.Method2

	call	.CheckA20               ; Check if A20 is enabled now
	jnc		.Done

	; METHOD 2 - KBC
.Method2:
    cli
    
    call	.A20WaitBit2
    mov		al, 0xAD				; Disable keyboard
    out		0x64, al
    
    call	.A20WaitBit2
    mov		al, 0xD0				; Request read on KBC
    out		0x64, al
    
    call	.A20WaitBit1
    in		al, 0x60				; Get status
    push	ax						; Preserve in CL
    
    call	.A20WaitBit2
    mov		al, 0xD1				; Request write to KBC
    out		0x64, al
    
    call	.A20WaitBit2
    pop		ax						; Restore AL
    or		al, 0x02				; Enable A20s
    out		0x60, al				; Set A20 enable bit to KBC
    
    call	.A20WaitBit2
    mov		al, 0xAE				; Enable keyboard
    out		0x64, al
    
    call	.A20WaitBit2
        
    sti
	
	call	.CheckA20               ; Check if A20 is enabled now
	jnc		.Done
	
    ; METHOD 3 - Fast A20 Gate
.Method3:
	in		al, 0x92
    test 	al, 2
    jnz 	.Method3_End
    or 		al, 2
    and 	al, 0xFE
    out 	0x92, al
.Method3_End:

    call	.CheckA20               ; Check if A20 is enabled now
	jnc		.Done

	; METHOD 4 - Port 64h
.Method4:
    call	.A20WaitBit2
    
    mov		al, 0xDD				; Enable A20
    out		0x64, al
	
	; Check if A20 is enabled now
	call	.CheckA20
	jnc		.Done
	
	; Nothing worked, might as well error
	jmp		.DoneError
	
	;
	;	.A20WaitBit2
	;	Clobbered Registers:
	;		AX, BX
	;	Arguments:
	;		None
	;	Returns:
	;		Nothing
	;
	.A20WaitBit2:
		; Save CX and set to 0xFFFF
		push	cx
		mov		cx, 0xFFFF
		
		.A20WaitBit2Loop:
			; Decrease CX and jump to error if 0
			dec		cx
			jz		.KBCError
			
			; Test
			in		al, 0x64
			test	al, 0x02
			jnz		.A20WaitBit2Loop
		
		; Done, return
		pop		cx
		ret
	
	;
	;	.A20WaitBit1
	;	Clobbered Registers:
	;		AX, BX
	;	Arguments:
	;		None
	;	Returns:
	;		Nothing
	;
	.A20WaitBit1:
		; Save CX and set to 0xFFFF
		push	cx
		mov		cx, 0xFFFF
		
		.A20WaitBit1Loop:
			; Decrease CX and jump to error if 0
			dec		cx
			jz		.KBCError
			
			; Test
			in		al, 0x64
			test	al, 0x01
			jz		.A20WaitBit1Loop
		
		; Done, return
		pop		cx
		ret
	
	; Keyboard Controller Error
	.KBCError:
		pop		cx
		jmp		.DoneError
	
	;
	;	CheckA20
	;	No registers clobbered
	;	Arguments:
	;		None
	;	Returns:
	;		CF				A20 Enabled status
	;
	.CheckA20:
		push	es
		push	ax
		
		; Test wrap around 0xFFFF:0x10
		mov		ax, 0xFFFF
		mov		es, ax
		
		mov		ax, [0x0000]
		cmp		ax, [es:0x10]
		jne		.CheckA20Success
		inc		word [0x0000]
		wbinvd
		cmp		ax, [es:0x10]
		je		.CheckA20SuccessRestore
		
		; Error, clean up and return
		dec		word [0x0000]
		stc
		pop		ax
		pop		es
		ret
		
		.CheckA20SuccessRestore:
			dec		word [0x0000]
		
		.CheckA20Success:
			pop		ax
			pop		es
			clc
			ret
	
	; Error
	.DoneError:
		popa
		stc
		ret
	
	; Success
	.Done:
		popa
		clc
		ret

;
;	CheckCPU
;	No registers are clobbered
;	Arguments:
;		None
;	Returns:
;		Nothing
;
;	8086/8088:
;		Intel says bits 12-15 of FLAGS
;		are always set on the 8086/8088
;	80286:
;		Intel says bits 12-15 of FLAGS
;		are always clear in real mode
;	80386:
;		Intel says bit 18 was not
;		introduced in EFLAGS until the
;		486, and cannot be set on
;		the 386
;	80486:
;		Intel says the 486 is the last
;		model without the CPUID
;		instruction, so if the ID bit
;		cannot be set, it's a 486
;
CheckCPU:
	pushaw
	
	; Check 8086
	.Check8086:
		pushf
		pop		ax
		mov		cx, ax
		and		ax, 0x0FFF
		push	ax
		popf
		
		pushf
		pop		ax
		and		ax, 0xF000
		cmp		ax, 0xF000
		jne		.Check286
		
		push	sp
		pop		dx
		cmp		dx, sp
		je		.Check286
	
	.Cleanup8086:
		push	cx
		popf
		jmp		.Failure
	
	; Check 80286
	.Check286:
		push	cx
		popf
		
		pushf
		pop		ax
		mov		ax, cx
		or		ax, 0xF000
		push	ax
		popf
		
		pushf
		pop		ax
		and		ax, 0xF000
		jnz		.Check386
	
	.Cleanup286:
		push	cx
		popf
		jmp		.Failure
	
	; Check 80386
	.Check386:
		push	cx
		popf
		
		pushfd
		pop		eax
		mov		ecx, eax
		or		eax, 0x00040000
		push	eax
		popfd
		
		pushfd
		pop		eax
		and		eax, 0xFFFBFFFF
		jnz		.Check486
	
	.Cleanup386:
		push	ecx
		popfd
		jmp		.Failure
	
	; Check 80486
	.Check486:
		push	ecx
		popfd
		
		pushfd
		pop		eax
		mov		ecx, eax
		xor		eax, 0x00200000
		push	eax
		popfd
		
		pushfd
		pop		eax
		cmp		eax, ecx
		jne		.Success
	
	.Cleanup486:
		push	ecx
		popfd
	
	; Error
	.Failure:
		popaw
		
		stc
		ret
	
	; Success
	.Success:
		push	ecx
		popfd
		popaw
		
		clc
		ret

;
;	EnterUnrealMode
;	No registers are clobbered
;	Arguments:
;		None
;	Returns:
;		Nothing
;
EnterUnrealMode:
	pushad
	
	; Disable NMIs
	in		al, 0x70
	or		al, 0x80
	out		0x70, al
	
	; Install GDT & enter PMode
	cli
	lgdt	[GDT32]
	
	mov		eax, cr0
	or		eax, 0x00000001
	mov		cr0, eax
	
	; Extend all segment register limits (Excluding CS)
	mov		bx, 0x10
	mov		ds, bx
	mov		es, bx
	mov		fs, bx
	mov		gs, bx
	mov		ss,	bx
	
	; Drop into Unreal Mode
	and		eax, 0xFFFFFFFE
	mov		cr0, eax
	
	xor		ax, ax
	mov		ds, ax
	mov		es, ax
	mov		fs, ax
	mov		gs, ax
	mov		ss, ax
	
	; Enable NMIs
	in		al, 0x70
	and		al, 0x7F
	out		0x70, al
	
	sti
	
	popad
	ret

%endif
