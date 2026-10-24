// TODO Need to make an internal SysCall wrapper that's generic and hides all platforms
// TODO These are Windows-specific, need to somehow hide from the stdlib somehow

error {
    // Intrinsics
    NaN,
    Infinity,
    Panic,
    RefinementViolation,
    DivideByZero,
    Overflow,
    OutOfBounds,

    // Standard Library
    FileNotFound
}

import SysCall;
import Range;
import Lens;
import Heap;
import Array;
import String;
import Logger;
import File;
