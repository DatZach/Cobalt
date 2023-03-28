;
;   cofs.inc
;   Cobalt FS
;

%ifndef __COFS_INC
%define __COFS_INC

%define MdtBuffer       0x8000
%define FscbBuffer      0x7C08
%define TreeBuffer      0x10000

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
FindTreeNode:
    push    eax
    mov     eax, MDT_TYPE_ROOT
    call    FindMdtEntry

    mov     eax, dword [ebx + RootDescriptor.RootCluster]
    mov     edi, TreeBuffer
    mov     ecx, 1
    call    ReadClusters
    pop     eax

.SearchInternal:
    mov     ebx, TreeBuffer + 128
    xor     ecx, ecx
.SearchInternal_Loop0:
    cmp     ebx, TreeBuffer + 4096 ; TODO BytesPerCluster
    jge     .SearchInternal_Loop0_Exit
    cmp     dword [ebx], 0xFFFFFFFF
    je      .SearchInternal_Loop0_Exit

    cmp     dword [ebx], eax
    jle     .SearchInternal_Loop0_1
    inc     ecx
.SearchInternal_Loop0_1:
    add     ebx, 128
    jmp     .SearchInternal_Loop0
.SearchInternal_Loop0_Exit:

    cmp     dword [TreeBuffer + 128 + ecx * 128], eax
    jne     .SearchInternal_SearchChildren

    shl     ecx, 7
    mov     ebx, TreeBuffer + 128
    add     ebx, ecx

    clc
    ret

.SearchInternal_SearchChildren:
    ; return node.IsLeaf ? default : SearchInternal(node.Children[i], key);
    cmp     ecx, 0                      ; node.IsLeaf
    je      .SearchInternal_NotFound

    push    eax
    mov     eax, dword [TreeBuffer + ecx * 4]
    mov     edi, TreeBuffer
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
;       SI      string filename
;       DX      NodeAttributes attributes
;
FindNodeEntry:
    ret

;
;   FindMdtEntry
;   Registers Clobbered
;       ?
;   Arguments
;       EAX     uint MDT Entry Magic to find
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

DiskPacket:
	.Size               db  0
    .Reserved0          db  0
    .Blocks             dw  0
    .Buffer             dd  0
    .Sector             dq  0

%endif
