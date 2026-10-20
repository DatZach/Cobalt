//module Standard;

type Range = (
    u64 Start;
    u64 End;

    u64 Length => End - Start;

    function GetEnumerator() RangeEnumerator => RangeEnumerator ( Start - 1, End );
)

type RangeEnumerator = (
    u64 Current;
    u64 End;

    function MoveNext() bool {
        if (Current < End) {
            Current += 1;
            return true;
        }

        return false;
    }
)
