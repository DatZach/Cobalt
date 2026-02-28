module Console;

const STD_INPUT_HANDLE: DWORD = -10;
const STD_OUTPUT_HANDLE: DWORD = -11;
const NULL: LPVOID = 0;

import kernel32 GetStdHandle func (nStdHandle: DWORD) HANDLE stdcall;
import kernel32 WriteConsoleA func (hConsoleOutput: HANDLE, lpBuffer: LPVOID, nNumberOfCharsToWrite: DWORD, lpNumberOfCharsWritten: LPDWORD, lpReserved: LPVOID) BOOL stdcall;
import kernel32 ReadConsoleA func (hConsoleOutput: HANDLE, lpBuffer: LPVOID, nNumberOfCharsToRead: DWORD, lpNumberOfCharsRead: LPDWORD, pInputControl: LPVOID) BOOL stdcall;

const hStdOut: HANDLE = GetStdHandle(STD_OUTPUT_HANDLE);
const hStdIn: HANDLE = GetStdHandle(STD_INPUT_HANDLE);

//func WriteLine(value: string) {
//    WriteConsoleA(hStdOut, value, value.Length, NULL, NULL);
//    WriteConsoleA(hStdOut, "^n", 1, NULL, NULL);
//}

func Write(value: string) {
    WriteConsoleA(hStdOut, value.Address, value.Length, NULL, NULL);
}

func WriteLine(value: string) {
    WriteConsoleA(hStdOut, value.Address, value.Length, NULL, NULL);
    WriteConsoleA(hStdOut, "^n".Address, 1, NULL, NULL);
}

func ReadLine() string {
    var buffer = Alloc(16); // TODO Replace with u8 [...16][..(.Capacity)]; ?
    //var length: DWORD;
    var length = Alloc(8); // TODO Replace with lens

    ReadConsoleA(hStdIn, buffer.Address, buffer.Length - 1, length.Address, NULL);
    
    return buffer;//[..16];
}
