struct Array `T {
    Length: uint; // , const
    data: Lens`T;

    Capacity: uint => data.Length;
    //Capacity: uint { get { return data.Length; } }

    [uint]: T! {
        get { return data[key]; }
        set { data[key] = value; }
    }

    factory New(capacity: uint) {
        var _capacity = capacity; // TODO capacity
        if (_capacity == 0)
            _capacity = 16;
        
        return This {
            Length = 0,
            data = Alloc(_capacity) // TODO * T.Size)
        };
    }

    func Add(value: T) {
        if (Length >= data.Length) {
            data = ReAlloc(data, data.Length << 1);
        }

        data[Length] = value;
        Length += 1;
    }

    func AddRange(value: T[]) {
        var i: int;
        for (i in ..value.Length)
            Add(value[i]);
    }

    func Reverse() {
        if (Length < 2)
            return;
        
        var i: int;
        for (i in ..(Length / 2)) {
            const j = Length - i - 1;
            const a = this[i];
            const b = this[j];
            this[i] = b;
            this[j] = a;

            // (this[i], this[j]) = (this[j], this[i]);
        }
    }

    func Slice(range: Range) Lens`T! {
        var length = (range.End < 0) :: { true => Length - ~range.End, false => range.Length };
        if (range.Start < 0 || length < 0 || length > Length) return error.OutOfBounds;
        return Lens`T ( data.Address + range.Start, length );
    }

    func GetEnumerator() ArrayEnumerator`T => ArrayEnumerator`T ( this[0], this, 0 );
}

tuple ArrayEnumerator `T (
    Current: T;
    parent: Array`T;
    current: u64;

    func MoveNext() bool {
        if (current < parent.Length) {
            Current = parent[current];
            current += 1;
            
            return true;
        }

        return false;
    }
)
