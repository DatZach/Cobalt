// TODO These are Windows-specific, need to somehow hide from the stdlib somehow
//module Standard {
    type HANDLE     u64;
    type BOOL       u32;
    type DWORD      u32;
    type LPDWORD    u64;
    type LPVOID     uint;
    type SIZE_T     uint;
//}

import Range;
import Lens;
import Heap;
import Array;
import String;
import Logger;
