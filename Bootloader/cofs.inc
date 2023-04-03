;
;   cofs.inc
;   Cobalt FS
;

%ifndef __COFS_INC
%define __COFS_INC

%define MdtBuffer           0x00008000
%define FscbBuffer          0x00007C08
%define ADDR_SEGOFF_TREE    0x10000000      ; 64kb in SEG:OFF notation
%define ADDR_TREE           0x00010000      ; 64kb in canonical notation

%define NODE_ATTRIBUTE_DIRECTORY    2

%define MDT_TYPE_ROOT   0x544F4F52

struc FSCB
    .Magic:                 resd  1
    .Version:               resb  1
    .BytesPerSector:        resw  1
    .SectorsPerCluster:     resb  1
    .LogClustersPerBlock:   resb  1
    .TotalClusters:         resd  1
    .MdtCluster:            resd  1
    .Checksum:              resd  1
endstruc

struc RootDescriptor
    .Type:                  resd 1
    .RootCluster:           resd 1
    .LastNodeId:            resd 1
    .Degree:                resb 1
endstruc

struc NodeEntry
    .NodeId:				resd 1
	.ParentNodeId:			resd 1
	.ExtensionNodeId:		resd 1
	.Attributes:			resb 1
	.ExtFlags:				resb 1
	.NameSize:				resb 1
	.reserved0:				resb 1
	.DataSize:				resq 1
	.CreationTime:			resd 1
	.LastAccessTime:		resd 1
	.LastModificationTime:	resd 1
	.Clusters:   			resd 16
	.Name:					resb 28
endstruc

;
;   FindTreeNode(nodeId) -> BTreeNode*
;   FindNodeEntry(Node*, attribute, filename) -> Entry*
;   
;   Loading the kernel (60kb max without implementing extnodes)
;       rootNode = FindTreeNode(0)
;       osEntry = FindNodeEntry(rootNode, Attributes.Directory, "os")
;       osNode = FindTreeNode(osEntry->clusters[0])
;       kernelEntry = FindNodeEntry(osNode, Attributes.None, "kernel64.exe")
;       while (offset < kernelEntry->size && i <= 14):
;           offset += ReadCluster(kernelEntry->clusters[i++], base + offset)
;       if offset < kernelEntry->size:
;           error (can be extended later)
;

;
;	ReadClusters
;	Registers Clobbered
;		EAX
;	Arguments
;		EAX			Cluster
;		EDI			Buffer
;	Returns
;		EDI			Buffer + BytesWritten
;		CF			Set on error
;
ReadClusters:
	pushad

    cmp     eax, dword [LastCluster]
    jne     .Read
    cmp     edi, dword [LastClusterAddress]
    jne     .Read

    popad
    jmp     .Done

.Read:
    mul     byte [FscbBuffer + FSCB.SectorsPerCluster]
    inc     ax

	; Populate packet
	mov	    dword [DiskPacket.Buffer], edi
	mov	    dword [DiskPacket.Sector], eax

    movzx   ax, byte [FscbBuffer + FSCB.SectorsPerCluster]
	mov		word [DiskPacket.Blocks], ax
    mul     cx

	; Call interrupt
	mov		ah, 0x42
	mov		dl, byte [KernelBootInfo.BIOSBootDevice]
	xor		bx, bx
	mov		ds, bx
	mov		si, DiskPacket
	int		0x13
	
	popad

    mov     dword [LastCluster], eax
    mov     dword [LastClusterAddress], edi

.Done:
    movzx   eax, byte [FscbBuffer + FSCB.SectorsPerCluster]
    mul     word [FscbBuffer + FSCB.BytesPerSector]
    add     edi, eax

	ret

;
;   FindTreeNode
;   Registers Clobbered
;       ALL OF THEM
;   Arguments
;       EAX     Node ID
;   Returns
;       EBX     BTreeNode* node
;       Carry   SET if error
;
FindTreeNode: ; 0xa23
    push    eax
    mov     eax, MDT_TYPE_ROOT
    call    FindMdtEntry

    mov     eax, dword [ebx + RootDescriptor.RootCluster]
    mov     edi, ADDR_SEGOFF_TREE
    mov     cx, 1
    call    ReadClusters
    pop     eax

    ; 0x0a40

.SearchInternal:
    mov     ebx, ADDR_TREE + 128
    xor     cx, cx
.SearchInternal_Loop0:
    push    eax
    call    GetBytesPerCluster
    add     eax, ADDR_TREE
    mov     edx, eax
    pop     eax
    cmp     ebx, edx
    jge     .SearchInternal_Loop0_Exit
    cmp     dword [ebx], 0xFFFFFFFF
    je      .SearchInternal_Loop0_Exit

    cmp     eax, dword [ebx]
    jle     .SearchInternal_Loop0_1
    inc     cx
.SearchInternal_Loop0_1:
    add     ebx, 128
    jmp     .SearchInternal_Loop0
.SearchInternal_Loop0_Exit:
    xor     ebx, ebx
    mov     bx, cx
    shl     bx, 7
    add     bx, 128
    add     ebx, ADDR_TREE
    cmp     dword [ebx], eax
    jne     .SearchInternal_SearchChildren

    clc
    ret

.SearchInternal_SearchChildren:
    ; return node.IsLeaf ? default : SearchInternal(node.Children[i], key);
    cmp     ecx, 0                      ; node.IsLeaf
    je      .SearchInternal_NotFound

    push    eax
    mov     eax, dword [ADDR_TREE + ecx * 4]
    mov     edi, ADDR_SEGOFF_TREE
    mov     ecx, 1
    call    ReadClusters
    pop     eax

    jmp     .SearchInternal

.SearchInternal_NotFound:
    stc
    ret

;
;   FindNodeEntry
;   Registers Clobbered
;       ?
;   Arguments
;       EBX     BTreeNode* node
;       ESI     string filename
;       DL      NodeAttributes attributes
;       Carry   SET if not found
;   Returns
;       EBX     NodeEntry* entry
;       Carry   SET If not found
;
FindNodeEntry:
    mov     ecx, dword [ebx + NodeEntry.DataSize]    ; NOTE qword but don't support more than 16 entries in root for now
    xor     edx, edx

.FindNodeEntry_Loop0:
    mov     eax, dword [ebx + NodeEntry.Clusters + edx * 4]
    pushad
    call    FindTreeNode
    jc      .FindNodeEntry_NotFound_0
    mov     dword [.tmp], ebx                       ; For shame :(
    popad
    mov     ebx, dword [.tmp]
    test     dl, byte [ebx + NodeEntry.Attributes]
    jne     .FindNodeEntry_NotFound
    mov     edi, ebx
    add     edi, NodeEntry.Name
    push    ecx
    push    esi
    movzx   ecx, word [ebx + NodeEntry.NameSize]
    db 0x67 ; HACK Address Size override
    rep cmpsb
    pop     esi
    pop     ecx
    jne     .FindNodeEntry_NotFound

    clc
    ret

.FindNodeEntry_NotFound_0:
    popad
.FindNodeEntry_NotFound:
    inc     edx
    dec     ecx
    jnz     .FindNodeEntry_Loop0

    stc
    ret
.tmp    dd  0

;
;   FindMdtEntry
;   Registers Clobbered
;       ?
;   Arguments
;       EAX     uint MDT Entry Magic to find
;   Returns
;       EBX     MdtEntry* pointer
;       Carry   SET if error
;
FindMdtEntry:
    clc
    mov     ebx, MdtBuffer
.FindEntry:
    mov     edx, dword [ebx]
    cmp     edx, eax
    je      .FoundEntry
    cmp     edx, 0
    je      .FindEntryError
    add     ebx, 32
    jmp     .FindEntry
.FindEntryError:
    stc
.FoundEntry:
    ret

;
;   GetBytesPerCluster
;   Registers Clobbered
;       None
;   Returns
;       EAX     uint Bytes per Cluster
;
GetBytesPerCluster:
    movzx   eax, word [FscbBuffer + FSCB.BytesPerSector]
    mul     word [FscbBuffer + FSCB.SectorsPerCluster]
    ret

DiskPacket:
	.Size               db  0
    .Reserved0          db  0
    .Blocks             dw  0
    .Buffer             dd  0
    .Sector             dq  0

LastCluster:            dd  0
LastClusterAddress:     dd  0

%endif
