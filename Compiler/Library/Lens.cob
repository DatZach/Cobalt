//module Standard;

type Lens = (
    type `T;

    uint Address;
    uint Length;

    // NOTE Implemented in Compiler as an intrinsic
    index [uint key] T! {
        get {
            if (key < 0 || key >= Length) return error.OutOfBounds;
            
            machine cvm "
                getfield    r1, a0, 0
                add         r1, r1, a1
                peek        r0, r1, 1
                return      r0
            ";
        }

        set {
            if (key < 0 || key >= Length) return error.OutOfBounds;
            
            machine cvm "
                getfield    r1, a0, 0
                add         r1, r1, a1
                poke        r1, 1, a2
                return      0
            ";
        }
    }

    // TODO Slices for Lens
    // TODO Enumerator
    // TODO Slice
    // TODO GetEnumerator

    function GetEnumerator() LensEnumerator`T => LensEnumerator`T ( this[0], this, 0 );
)

type LensEnumerator = (
    type `T;

    T Current;

    Lens`T parent;
    u64 current;

    function MoveNext() bool {
        if (current < parent.Length) {
            Current = parent[current];
            current += 1;
            return true;
        }

        return false;
    }
)