org     0x0500
bits    16

jmp     Main

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

Main:
    mov     ax, 0x1337

    mov     si, msg
    call    Panic

    cli
    hlt

msg db "Hello, from stage2", 0
