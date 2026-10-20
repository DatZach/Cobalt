//module Standard;

type Lens = (
    type `T;

    uint Address;
    uint Length;

    // NOTE Implemented in Compiler as an intrinsic
    index [uint] T! {
        get {
            if (key < 0 || key >= Length) return error.OutOfBounds;
            
            machine cobil "Lens_Get";
            // machine cobil "
            //     GetField    r1, a0, 0
            //     Add         r1, r1 a1
            //     Peek        r0, r1, 1
            //     Return      r0
            // "
        }

        set {
            if (key < 0 || key >= Length) return error.OutOfBounds;
            
            machine cobil "Lens_Set";

            // machine cobil "
            //     GetField    r1, a0, 0
            //     Add         r1, r1 a1
            //     Poke        r1, 1, a2
            //     Return
            // "
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