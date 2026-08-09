module Logger;

const STD_INPUT_HANDLE: DWORD = -10;
const STD_OUTPUT_HANDLE: DWORD = -11;
const NULL: LPVOID = 0;

import kernel32 GetStdHandle function (DWORD nStdHandle) HANDLE, stdcall;
import kernel32 WriteConsoleA function (HANDLE hConsoleOutput, LPVOID lpBuffer, DWORD nNumberOfCharsToWrite, LPDWORD lpNumberOfCharsWritten, LPVOID lpReserved) BOOL, stdcall;
import kernel32 ReadConsoleA function (HANDLE hConsoleOutput, LPVOID lpBuffer, DWORD nNumberOfCharsToRead, LPDWORD lpNumberOfCharsRead, LPVOID pInputControl) BOOL, stdcall;

const hStdOut: HANDLE = GetStdHandle(STD_OUTPUT_HANDLE);
const hStdIn: HANDLE = GetStdHandle(STD_INPUT_HANDLE);

function Write(string value) {
    WriteConsoleA(hStdOut, value.Address, value.Length, NULL, NULL);
}

function WriteLn(string value) {
    WriteConsoleA(hStdOut, value.Address, value.Length, NULL, NULL);
    WriteConsoleA(hStdOut, "^n".Address, 1, NULL, NULL);
}

function ReadLine() string {
    var buffer = Heap.Alloc(16); // TODO Replace with u8 [...16][..(.Capacity)]; ?
    //var length: DWORD;
    var length = Heap.Alloc(8); // TODO Replace with lens

    ReadConsoleA(hStdIn, buffer.Address, buffer.Length - 1, length.Address, NULL);
    
    return buffer;//[..16];
}
