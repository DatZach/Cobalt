// https://github.com/microsoft/Windows-classic-samples/blob/main/Samples/EapHostSupplicant/cpp/memory.cpp

//module Standard.Heap;
module Heap;

var heap = SysCall.HeapCreate(0, 0, 0);

function Alloc(uint size) Lens`u8 {
    const addr = SysCall.HeapAlloc(heap, SysCall.HEAP_ZERO_MEMORY, size);
    return Lens`u8( addr, size );
    // return lens u8 addr..+size;
}

function ReAlloc(Lens`u8 ptr, uint size) Lens`u8 {
    const addr = SysCall.HeapReAlloc(heap, SysCall.HEAP_ZERO_MEMORY, ptr.Address, size);
    return Lens`u8( addr, size );
}

function Free(Lens`u8 ptr) {
    SysCall.HeapFree(heap, 0, ptr.Address);
}
