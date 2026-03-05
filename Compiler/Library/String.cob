func string(value: int, radix: int = 10) string {
    var result = [];
    
    const sign = radix == 10 && value < 0;
    var v = (sign) :: { true => -value, false => value };

    for (v > 0) {
        const i = v % radix;
        v /= radix;

        result.Add((i < 10) :: {
            true  => i + '0',
            false => i + 'A' - 10
        });
    }

    if (sign)
        result.Add('-');
    
    result.Reverse();

    return result[..=];
}

// TODO Move to another file
// TODO int
func int_(value: string, radix: int = 10) int {
    if (value.Length == 0)
        return 0;
    
    var result = 0;

    //const sign = value[0] :: { '-' => -1, default => 1 };

    var i: int;
    var length: int = 0;
    for (i in ..value.Length) {
        const ch = value[i];
        if (ch == 13 || ch == 10 || ch == 0) {
            break;
        }
        
        length += 1;
    }
    
    for (i in ..length) {
        const ch = value[i];
        result *= radix;
        result += ch - '0';
    }

    return result;
}
