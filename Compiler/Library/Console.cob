module Console;

type HANDLE     u64;
type BOOL       u32;
type DWORD      u32;
type LPDWORD    u64;
type LPVOID     u64;

const STD_OUTPUT_HANDLE: DWORD = -11;
const NULL: LPVOID = 0;

import kernel32 GetStdHandle func (nStdHandle: DWORD) HANDLE stdcall;
import kernel32 WriteConsoleA func (hConsoleOutput: HANDLE, lpBuffer: string, nNumberOfCharsToWrite: DWORD, lpNumberOfCharsWritten: LPDWORD, lpReserved: LPVOID) BOOL stdcall;

const hStdOut = GetStdHandle(STD_OUTPUT_HANDLE);

func WriteLine(value: string) {
    WriteConsoleA(hStdOut, value, value.Length, NULL, NULL);
    WriteConsoleA(hStdOut, "^n", 1, NULL, NULL);
}
