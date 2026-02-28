struct Array `T {
    Length: uint; // , const
    data: Lens`T;

    [uint]: T! {
        get { return data[key]; }
        set { data[key] = value; }
    }

    factory New(length: uint) {
        var capacity = length;
        if (capacity == 0)
            capacity = 16;
        
        return This {
            Length = length,
            data = Alloc(capacity) // * T.Size)
        };
    }

    func Add(value: T) {
        if (Length >= data.Length) {
            data = ReAlloc(data, data.Length << 1);
        }

        data[Length] = value;
        Length += 1;
    }

    func Slice(range: Range) Lens`T! {
        if (range.Start < 0 || range.End >= Length) return error.OutOfBounds;
        return Lens`T ( data.Address + range.Start, range.Length );
    }
}
