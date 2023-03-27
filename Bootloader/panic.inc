;
;   panic.inc
;	Panic
;

%ifndef __PANIC_INC
%define __PANIC_INC

bits    16

;
;   Panic
;   Registers Clobbered
;       None
;   Arguments
;       AL      Error Code
;
Panic:
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
;	Error Dispatcher
;
Errors:
.Unknown:
    mov		si, panicErrorUnknown
    call	Panic

.CPU:
    mov		si, panicErrorCPU
    call	Panic
    
.Memory:
    mov		si, panicErrorMemory
    call	Panic

.LowMemory:
    mov		si, panicErrorLowMemory
    call	Panic

.A20:
    mov		si, panicErrorA20
    call	Panic

.KLoadError:
    mov		si, panicErrorKernelLoad
    call	Panic

.OutOfMemory:
    mov		si, panicErrorOutOfMemory
    call	Panic

;
;	Constants
;

panicErrorUnknown			db	"An unknown error has occurred", 0
panicErrorCPU				db	"Incompatible CPU", 0
panicErrorMemory			db	"Error retrieving size of memory via BIOS interrupt AX=0xE801/0x15", 0
panicErrorLowMemory			db	"System requires 16MB or more of available RAM", 0
panicErrorA20				db	"Failed to enable the A20 line", 0
panicErrorKernelLoad		db	"Failed to load kernel64.exe", 0
panicErrorOutOfMemory		db	"Out of memory", 0

%endif
