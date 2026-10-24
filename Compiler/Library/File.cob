// TODO Use W instead of A

type File = {
    final string Path; // TODO type Path
    final SysCall.HANDLE handle;

    construct OpenRead(string path) {
        Path = path;
        handle = SysCall.CreateFileA(
            path.Address,
            SysCall.FILE_ACCESS.GENERIC_READ,
            SysCall.FILE_SHARE.READ,
            0,
            SysCall.CREATION_DISPOSITION.OPEN_EXISTING,
            SysCall.FILE_ATTRIBUTE.NORMAL,
            0
        );

        if (handle == SysCall.INVALID_HANDLE_VALUE)
            return error.FileNotFound;
    }

    destruct Close() {
        SysCall.CloseHandle(handle);
    }

    function ReadAllText() string {
        // TODO Obviously a horrible implementation

        var result = [ u8 ];
        var buffer = Heap.Alloc(1 + 4); // [ 1; 0 ]
        
        while (SysCall.ReadFile(handle, buffer.Address, 1, buffer.Address + 1, 0) == SysCall.BOOL(1)
        && (buffer[1] != 0 || buffer[2] != 0 || buffer[3] != 0 || buffer[4] != 0)) {
            result.Add(buffer[0]);
        }

        Heap.Free(buffer);

        return result[..=];
    }
}
