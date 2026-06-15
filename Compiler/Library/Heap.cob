// https://github.com/microsoft/Windows-classic-samples/blob/main/Samples/EapHostSupplicant/cpp/memory.cpp

//module Standard.Heap;
module Heap;

error {
    OutOfBounds
}

import kernel32 HeapCreate  func(flOptions: DWORD, dwInitialSize: SIZE_T, dwMaximumSize: SIZE_T) HANDLE stdcall;
import kernel32 HeapAlloc   func(hHeap: HANDLE, dwFlags: DWORD, dwBytes: SIZE_T) LPVOID stdcall;
import kernel32 HeapReAlloc func(hHeap: HANDLE, dwFlags: DWORD, lpMem: LPVOID, dwBytes: SIZE_T) LPVOID stdcall;
import kernel32 HeapFree    func(hHeap: HANDLE, dwFlags: DWORD, lpMem: LPVOID) BOOL stdcall;

const HEAP_ZERO_MEMORY: DWORD = 8_u32;

const heap: HANDLE = HeapCreate(0, 0, 0);

func Alloc(size: DWORD) Lens`u8 {
    const addr = HeapAlloc(heap, HEAP_ZERO_MEMORY, size);
    return Lens`u8( addr, size );
    // return lens u8 addr..+size;
}

func ReAlloc(ptr: Lens`u8, size: DWORD) Lens`u8 {
    const addr = HeapReAlloc(heap, HEAP_ZERO_MEMORY, ptr.Address, size);
    return Lens`u8( addr, size );
}

func Free(ptr: Lens`u8) {
    HeapFree(heap, 0, ptr.Address);
}
