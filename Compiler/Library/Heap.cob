// https://github.com/microsoft/Windows-classic-samples/blob/main/Samples/EapHostSupplicant/cpp/memory.cpp

//module Standard.Heap;
module Heap;

error {
    OutOfBounds
}

import kernel32 HeapCreate  function(DWORD flOptions, SIZE_T dwInitialSize, SIZE_T dwMaximumSize) HANDLE, stdcall;
import kernel32 HeapAlloc   function(HANDLE hHeap, DWORD dwFlags, SIZE_T dwBytes) LPVOID, stdcall;
import kernel32 HeapReAlloc function(HANDLE hHeap, DWORD dwFlags, LPVOID lpMem, SIZE_T dwBytes) LPVOID, stdcall;
import kernel32 HeapFree    function(HANDLE hHeap, DWORD dwFlags, LPVOID lpMem) BOOL, stdcall;

const HEAP_ZERO_MEMORY = DWORD(8);

const heap = HeapCreate(0, 0, 0);

function Alloc(DWORD size) Lens`u8 {
    const addr = HeapAlloc(heap, HEAP_ZERO_MEMORY, size);
    return Lens`u8( addr, size );
    // return lens u8 addr..+size;
}

function ReAlloc(Lens`u8 ptr, DWORD size) Lens`u8 {
    const addr = HeapReAlloc(heap, HEAP_ZERO_MEMORY, ptr.Address, size);
    return Lens`u8( addr, size );
}

function Free(Lens`u8 ptr) {
    HeapFree(heap, 0, ptr.Address);
}
