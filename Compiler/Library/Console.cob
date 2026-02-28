module Console;

const STD_OUTPUT_HANDLE: DWORD = -11;
const NULL: LPVOID = 0;

import kernel32 GetStdHandle func (nStdHandle: DWORD) HANDLE stdcall;
//import kernel32 WriteConsoleA func (hConsoleOutput: HANDLE, lpBuffer: string, nNumberOfCharsToWrite: DWORD, lpNumberOfCharsWritten: LPDWORD, lpReserved: LPVOID) BOOL stdcall;
import kernel32 WriteConsoleA func (hConsoleOutput: HANDLE, lpBuffer: LPVOID, nNumberOfCharsToWrite: DWORD, lpNumberOfCharsWritten: LPDWORD, lpReserved: LPVOID) BOOL stdcall;

const hStdOut: HANDLE = GetStdHandle(STD_OUTPUT_HANDLE);

//func WriteLine(value: string) {
//    WriteConsoleA(hStdOut, value, value.Length, NULL, NULL);
//    WriteConsoleA(hStdOut, "^n", 1, NULL, NULL);
//}

func WriteLine(value: Lens`u8) {
    WriteConsoleA(hStdOut, value.Address, value.Length, NULL, NULL);
    //WriteConsoleA(hStdOut, "^n", 1, NULL, NULL);
}