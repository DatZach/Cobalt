/*
struct Array `T {
    Size: uint; // , const
    data: Lens`T;
    capacity: uint;

    [uint]: T! {
        get { return data[key]; }
        set { data[key] = value; }
    }

    factory New(count: uint) {
        var _capacity = count;
        if (_capacity == 0)
            _capacity = 16;
        
        return This {
            Size = count,
            data = Alloc(_capacity), // * T.Size)
            capacity = _capacity
        };
    }

    func Add(value: T) {
        if (Size >= capacity) {
            capacity <<= 1;
            data = ReAlloc(data, capacity);
        }

        data[Size] = value;
        Size += 1;
    }
}
*/