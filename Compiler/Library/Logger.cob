module Logger;

var hStdOut = SysCall.GetStdHandle(SysCall.STD_OUTPUT_HANDLE);
var hStdIn = SysCall.GetStdHandle(SysCall.STD_INPUT_HANDLE);

function Write(string value) {
    SysCall.WriteConsoleA(hStdOut, value.Address, value.Length, 0, 0);
}

function WriteLn(string value) {
    SysCall.WriteConsoleA(hStdOut, value.Address, value.Length, 0, 0);
    SysCall.WriteConsoleA(hStdOut, "^n".Address, 1, 0, 0);
}

function ReadLine() string {
    var buffer = Heap.Alloc(16); // TODO Replace with u8 [...16][..(.Capacity)]; ?
    //var length: DWORD;
    var length = Heap.Alloc(8); // TODO Replace with lens

    SysCall.ReadConsoleA(hStdIn, buffer.Address, buffer.Length - 1, length.Address, SysCall.NULL);
    
    return buffer;//[..16];
}
