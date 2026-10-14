module Logger;

const STD_INPUT_HANDLE = DWORD(0xFFFF_FFF6);
const STD_OUTPUT_HANDLE = DWORD(0xFFFF_FFF5);
const NULL = LPVOID(0);

import kernel32 GetStdHandle function (DWORD nStdHandle) HANDLE, stdcall;
import kernel32 WriteConsoleA function (HANDLE hConsoleOutput, LPVOID lpBuffer, DWORD nNumberOfCharsToWrite, LPDWORD lpNumberOfCharsWritten, LPVOID lpReserved) BOOL, stdcall;
import kernel32 ReadConsoleA function (HANDLE hConsoleOutput, LPVOID lpBuffer, DWORD nNumberOfCharsToRead, LPDWORD lpNumberOfCharsRead, LPVOID pInputControl) BOOL, stdcall;

const hStdOut = GetStdHandle(STD_OUTPUT_HANDLE);
const hStdIn = GetStdHandle(STD_INPUT_HANDLE);

function Write(string value) {
    WriteConsoleA(hStdOut, value.Address, value.Length, 0, 0);
}

function WriteLn(string value) {
    WriteConsoleA(hStdOut, value.Address, value.Length, 0, 0);
    WriteConsoleA(hStdOut, "^n".Address, 1, 0, 0);
}

function ReadLine() string {
    var buffer = Heap.Alloc(16); // TODO Replace with u8 [...16][..(.Capacity)]; ?
    //var length: DWORD;
    var length = Heap.Alloc(8); // TODO Replace with lens

    ReadConsoleA(hStdIn, buffer.Address, buffer.Length - 1, length.Address, NULL);
    
    return buffer;//[..16];
}
