//module Standard;

// TODO Fluent API for manip functions

type Array = {
    type `T;

    uint Length; // , const
    Lens`T data;

    uint Capacity => data.Length;
    //Capacity: uint { get { return data.Length; } }

    index [uint] T! {
        get { return data[key]; }
        set { data[key] = value; }
    }

    construct New(uint capacity) {
        var _capacity = capacity; // TODO capacity
        if (_capacity == 0)
            _capacity = 16;
        
        Length = 0;
        data = Heap.Alloc(_capacity); // TODO * T.Size)
    }

    function Add(T value) { // intrinsic
        if (Length >= data.Length) {
            data = Heap.ReAlloc(data, data.Length << 1);
        }

        data[Length] = value;
        Length += 1;
    }

    function AddRange(T[] value) { // intrinsic
        for (var i in ..value.Length)
            Add(value[i]);
    }

    function AddRangeLiteral(T value, u32 count) { // intrinsic
        for (var i in ..count)
            Add(value);
    }

    function Reverse() {
        if (Length < 2)
            return;
        
        for (var i in ..(Length / 2)) {
            const j = Length - i - 1;
            const a = this[i];
            const b = this[j];
            this[i] = b;
            this[j] = a;

            // (this[i], this[j]) = (this[j], this[i]);
        }
    }

    function Where(function(T element) bool predicate) T[] {
        var result = [ u8 ];

        for (var i in ..Length) {
            const element = this[i];
            if (predicate(element))
                result.Add(element);
        }

        return result;
    }

    function Select(function(T element) T predicate) T[] {
        var result = [ u8 ];

        for (var i in ..Length) {
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

type ArrayEnumerator = (
    type `T;

    T Current;

    Array`T parent;
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
