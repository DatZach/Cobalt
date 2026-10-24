// TODO Refactor this to be Cobalt-first; avoid bias from other platforms' architecture

module SysCall;

// function GetEnvironmentVariables() string[] {
//     return [
//         "what",
//         "neat"
//     ];
// }


/*
 *  Windows, internal
 */

type HANDLE  = u64;
type BOOL    = u32;
type DWORD   = u32;
type LPDWORD = u64;
type LPVOID  = uint;
type SIZE_T  = uint;

type FILE_ACCESS = DWORD : {
    GENERIC_WRITE = 0x40000000,
    GENERIC_READ = 0x80000000,
    GENERIC_ALL = 0x10000000
}

type FILE_SHARE = DWORD : {
    NONE = 0x00,
    READ,
    WRITE,
    DELETE
}

type CREATION_DISPOSITION = DWORD : {
    NONE = 0,
    CREATE_NEW,
    CREATE_ALWAYS,
    OPEN_EXISTING,
    OPEN_ALWAYS,
    TRUNCATE_EXISTING
}

type FILE_ATTRIBUTE = DWORD : {
    NONE = 0x00,
    READONLY,
    HIDDEN,
    SYSTEM,
    _,
    _,
    ARCHIVE,
    _,
    NORMAL,
    TEMPORARY
}

const HEAP_ZERO_MEMORY = DWORD(8);

const STD_INPUT_HANDLE = DWORD(0xFFFF_FFF6);
const STD_OUTPUT_HANDLE = DWORD(0xFFFF_FFF5);
const NULL = LPVOID(0);

const INVALID_HANDLE_VALUE = HANDLE(0xFFFF_FFFF_FFFF_FFFF);

import kernel32 HeapCreate  function(DWORD flOptions, SIZE_T dwInitialSize, SIZE_T dwMaximumSize) HANDLE, stdcall;
import kernel32 HeapAlloc   function(HANDLE hHeap, DWORD dwFlags, SIZE_T dwBytes) LPVOID, stdcall;
import kernel32 HeapReAlloc function(HANDLE hHeap, DWORD dwFlags, LPVOID lpMem, SIZE_T dwBytes) LPVOID, stdcall;
import kernel32 HeapFree    function(HANDLE hHeap, DWORD dwFlags, LPVOID lpMem) BOOL, stdcall;

import kernel32 GetStdHandle function (DWORD nStdHandle) HANDLE, stdcall;
import kernel32 WriteConsoleA function (HANDLE hConsoleOutput, LPVOID lpBuffer, DWORD nNumberOfCharsToWrite, LPDWORD lpNumberOfCharsWritten, LPVOID lpReserved) BOOL, stdcall;
import kernel32 ReadConsoleA function (HANDLE hConsoleOutput, LPVOID lpBuffer, DWORD nNumberOfCharsToRead, LPDWORD lpNumberOfCharsRead, LPVOID pInputControl) BOOL, stdcall;

import kernel32 CreateFileA function(LPVOID lpFileName, FILE_ACCESS dwDesiredAccess, FILE_SHARE dwShareMode, LPVOID lpSecurityAttributes, CREATION_DISPOSITION dwCreationDisposition, FILE_ATTRIBUTE dwFlagsAndAttributes, HANDLE hTemplateFile) HANDLE, stdcall;
import kernel32 CloseHandle function(HANDLE hObject) BOOL, stdcall;
import kernel32 ReadFile function(HANDLE hFile, LPVOID lpBuffer, DWORD nNumberOfBytesToRead, LPDWORD lpNumberOfBytesRead, LPVOID lpOverlapped) BOOL, stdcall;
