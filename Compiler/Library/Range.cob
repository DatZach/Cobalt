//module Standard;

tuple Range (
    Start: u64;
    End: u64;

    Length: u64 => End - Start;

    function GetEnumerator() RangeEnumerator => RangeEnumerator ( Start - 1, End );
)

tuple RangeEnumerator (
    Current: u64;
    End: u64;

    function MoveNext() bool {
        if (Current < End) {
            Current += 1;
            return true;
        }

        return false;
    }
)
