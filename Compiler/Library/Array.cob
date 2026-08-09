//module Standard;

// TODO Fluent API for manip functions

struct Array `T {
    Length: uint; // , const
    data: Lens`T;

    Capacity: uint => data.Length;
    //Capacity: uint { get { return data.Length; } }

    [uint]: T! {
        get { return data[key]; }
        set { data[key] = value; }
    }

    factory New(uint capacity) {
        var _capacity = capacity; // TODO capacity
        if (_capacity == 0)
            _capacity = 16;
        
        return This {
            Length = 0,
            data = Heap.Alloc(_capacity) // TODO * T.Size)
        };
    }

    function Add(T value) {
        if (Length >= data.Length) {
            data = Heap.ReAlloc(data, data.Length << 1);
        }

        data[Length] = value;
        Length += 1;
    }

    function AddRange(T[] value) {
        var i: int;
        for (i in ..value.Length)
            Add(value[i]);
    }

    function Reverse() {
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

    function Where(function(T element) bool predicate) T[] {
        var result = [];

        var i: int;
        for (i in ..Length) {
            const element = this[i];
            if (predicate(element))
                result.Add(element);
        }

        return result;
    }

    function Select(function(T element) T predicate) T[] {
        var result = [];

        var i: int;
        for (i in ..Length) {
            var element = this[i];
            element = predicate(element);
            result.Add(element);
        }

        return result;
    }

    function Slice(Range range) Lens`T! {
        var length = (range.End < 0) :: { true => Length - ~range.End, false => range.Length };
        if (range.Start < 0 || length < 0 || length > Length) return error.OutOfBounds;
        return Lens`T ( data.Address + range.Start, length );
    }

    function GetEnumerator() ArrayEnumerator`T => ArrayEnumerator`T ( this[0], this, 0 );
}

tuple ArrayEnumerator `T (
    Current: T;
    parent: Array`T;
    current: u64;

    function MoveNext() bool {
        if (current < parent.Length) {
            Current = parent[current];
            current += 1;
            
            return true;
        }

        return false;
    }
)
