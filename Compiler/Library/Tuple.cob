/*
tuple RangeIterator (
    Value: u64;
    End: u64;

    func MoveNext() bool {
        if (Value < End) {
            Value += 1;
            return true;
        }

        return false;
    }
)
*/

tuple Range (
    Start: u64;
    End: u64;

    Length: u64 => End - Start;

    //func GetIterator() RangeIterator => RangeIterator ( Start, End );
)
