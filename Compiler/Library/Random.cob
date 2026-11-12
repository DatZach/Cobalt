module Random;

// TODO Not the final API, refactor all
// TODO const should be able to be declared here without the compiler complaining

type Rng = {
    function Next() int;
};

type PCG32 = {
    mixin Rng;

    const DEFAULT_STATE  = 0x853C49E6_748FEA9B_u64;
    const DEFAULT_STREAM = 0xDA3E39CB_94B95BDB_u64;
    const FACTOR         = 0x5851F42D_4C957F2D_u64;

    u64 state;
    u64 inc;

    construct() {
        state = DEFAULT_STATE;
        inc = DEFAULT_STREAM;
    }

    function Next() int {
        var c = state;
        var a = #( u32((c >> 18) ^ c) );
        var b = #( u32(c >> 59) );
        state = #( c * FACTOR + inc );
        return  #( (a >> b) | (a << ((~b + 1) & 31)) );
    }
}

const Global = PCG32 { };
