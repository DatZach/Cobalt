// https://github.com/microsoft/Windows-classic-samples/blob/main/Samples/EapHostSupplicant/cpp/memory.cpp

// TODO Uncomment
//module Heap;

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

// TODO Should be in its own file?
tuple Lens `T (
    Address: LPVOID;
    Length: uint;

    // NOTE Implemented in Compiler as an intrinsic
    [uint]: T! {
        get {
            if (key < 0 || key >= Length) return error.OutOfBounds;
            
            machine cobil "Lens_Get";
            // machine cobil "
            //     GetField    r1, a0, 0
            //     Add         r1, r1 a1
            //     Peek        r0, r1, 1
            //     Return      r0
            // "
        }

        set {
            if (key < 0 || key >= Length) return error.OutOfBounds;
            
            machine cobil "Lens_Set";

            // machine cobil "
            //     GetField    r1, a0, 0
            //     Add         r1, r1 a1
            //     Poke        r1, 1, a2
            //     Return
            // "
        }
    }

    // TODO Slices for Lens
    // TODO Enumerator
)

type string     Lens`u8;
type cstring    LPVOID;
