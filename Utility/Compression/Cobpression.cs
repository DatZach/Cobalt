using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Text;

namespace Compression
{
    public sealed record BitString(uint Value, int Length)
    {
        public BitString Append(bool value)
        {
            return new BitString(
                (Value << 1) | (value ? 1u : 0u),
                Length + 1
            );
        }

        public bool Equals(BitString? other)
        {
            if (other == null)
                return false;

            return Value == other.Value && Length == other.Length;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Value.GetHashCode(), Length.GetHashCode());
        }

        public override string ToString()
        {
            var sb = new StringBuilder(Length);
            for (int i = 0; i < Length; ++i)
                sb.Append((Value & (1 << i)) != 0 ? '1' : '0');

            return sb.ToString();
        }
    }

    public sealed class BitStream
    {
        public int Position { get; set; }

        public int Length => values.Count;

        private readonly List<bool> values;

        public BitStream()
        {
            Position = 0;
            values = new List<bool>();
        }

        public BitStream(byte[] buffer)
        {
            Position = 0;
            values = buffer.SelectMany<byte, bool>(
                x => [(x & 0x80) != 0, (x & 0x40) != 0, (x & 0x20) != 0, (x & 0x10) != 0,
                      (x & 0x08) != 0, (x & 0x04) != 0, (x & 0x02) != 0, (x & 0x01) != 0]
            ).ToList();
        }

        public void Insert(bool value)
        {
            values.Insert(Position++, value);
        }

        public void Write(bool value)
        {
            while (Position >= values.Count)
                values.Add(false);

            values[Position++] = value;
        }

        public void Write(int length, uint value)
        {
            for (int i = 0; i < length; ++i)
            {
                Write(
                    (value & (1 << (length - 1 - i))) != 0
                );
            }
        }

        public bool Read()
        {
            return values.ElementAtOrDefault(Position++);
        }

        public uint Read(int length)
        {
            uint result = 0;
            for (int i = 0; i < length; ++i)
                result |= (uint)(values.ElementAtOrDefault(Position + i) ? 1 : 0) << (length - i - 1);

            Position += length;
            return result;
        }

        public byte[] ToArray()
        {
            var length = (values.Count + 8 - 1) / 8;
            var result = new byte[length];

            for (int i = 0; i < values.Count; ++i)
            {
                var j = i / 8;
                var k = i % 8;

                if (values[i])
                    result[j] |= (byte)(1 << (7 - k));
            }

            return result;
        }
    }

    public struct Symbol
    {
        public SymbolType Type { get; set; }

        public byte Value { get; set; }

        public int Length { get; set; }

        public int Offset { get; set; }

        public Symbol(int value)
        {
            if (value >= 256)
                Type = (SymbolType)(value - 256 + 1);
            else
            {
                Type = SymbolType.Literal;
                Value = (byte)value;
            }
        }

        public static implicit operator Symbol(int value)
        {
            return new Symbol(value);
        }

        public override int GetHashCode()
        {
            if (Type == SymbolType.Literal)
                return HashCode.Combine(Type.GetHashCode(), Value.GetHashCode());

            return Type.GetHashCode();
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (obj is Symbol other)
            {
                if (other.Type == Type)
                {
                    if (Type == SymbolType.Literal)
                        return other.Value == Value;
                    
                    return true;
                }
                
                return false;
            }

            return false;
        }

        public override string ToString()
        {
            if (Type == SymbolType.Literal)
                return Value.ToString();

            return Type.ToString();
        }
    }

    public enum SymbolType
    {
        Literal,
        RLE,
        BackRef4,
        BackRef7,
        BackRef9,
        BackRef12,
        BackRef16,
        BackRef24
    }

    public sealed class HuffmanNode
    {
        public Symbol Value { get; set; }

        public int Frequency { get; set; }

        public HuffmanNode? Left { get; set; }

        public HuffmanNode? Right { get; set; }

        public bool IsLeaf => Left == null && Right == null;

        public IReadOnlyDictionary<Symbol, BitString> ResolveEncodingTable()
        {
            var table = new Dictionary<Symbol, BitString>();

            Traverse(this, new BitString(0, 0));

            return CanonicalizeEncodingTable(table);

            void Traverse(HuffmanNode node, BitString code)
            {
                if (node.IsLeaf)
                {
                    table[node.Value] = code;
                    return;
                }

                Traverse(node.Left!, code.Append(false));
                Traverse(node.Right!, code.Append(true));
            }
        }

        private IReadOnlyDictionary<Symbol, BitString> CanonicalizeEncodingTable(IReadOnlyDictionary<Symbol, BitString> source)
        {
            const int MaxSymbol = 320;

            var symLengths = new int[MaxSymbol];

            var blCounts = new int[MaxSymbol - 1];
            foreach (var x in source)
            {
                var i = x.Key.Type == SymbolType.Literal ? x.Key.Value : (int)x.Key.Type + 256 - 1;
                symLengths[i] = x.Value.Length;
                blCounts[symLengths[i]]++;
            }

            var code = 0;
            var nextCode = new int[MaxSymbol];
            for (int bits = 1; bits <= MaxSymbol - 1; ++bits)
            {
                code = (code + blCounts[bits - 1]) << 1;
                nextCode[bits] = code;
            }

            var codes = new (int Length, int Code)[MaxSymbol];
            for (int i = 0; i < MaxSymbol; ++i)
            {
                var len = symLengths[i];
                if (len != 0)
                {
                    codes[i].Code = nextCode[len];
                    codes[i].Length = len;
                    nextCode[len]++;
                }
            }

            var result = new Dictionary<Symbol, BitString>();
            foreach (var x in source)
            {
                var i = x.Key.Type == SymbolType.Literal ? x.Key.Value : (int)x.Key.Type + 256 - 1;
                result.Add(x.Key, new BitString((uint)codes[i].Code, codes[i].Length));
            }

            return result;
        }

        public IReadOnlyDictionary<BitString, Symbol> ResolveDecodingTable()
        {
            var table = ResolveEncodingTable();
            var result = table.ToDictionary(x => x.Value, x => x.Key);
            return result;
        }

        public Symbol Decode(BitStream stream)
        {
            var node = this;
            while (stream.Position < stream.Length)
            {
                if (node!.IsLeaf)
                    return node.Value;

                var bit = stream.Read();
                node = bit ? node.Right : node.Left;
            }

            if (node!.IsLeaf)
                return node.Value;

            throw new InvalidDataException();
        }

        public static HuffmanNode TreeFromSymbols(IReadOnlyList<Symbol> source)
        {
            var seen = new Dictionary<Symbol, int>();
            foreach (var x in source)
            {
                seen[x] = seen.GetValueOrDefault(x, 0) + 1;

                switch (x.Type)
                {
                    case SymbolType.Literal:
                        break;
                    case SymbolType.RLE:
                        var y = new Symbol { Type = SymbolType.Literal, Value = x.Value };
                        seen[y] = seen.GetValueOrDefault(y, 0) + 1;
                        break;
                    case SymbolType.BackRef4:
                    case SymbolType.BackRef7:
                    case SymbolType.BackRef9:
                    case SymbolType.BackRef12:
                    case SymbolType.BackRef16:
                    case SymbolType.BackRef24:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            var queue = new PriorityQueue<HuffmanNode, int>();
            foreach (var x in seen)
            {
                var node = new HuffmanNode { Value = x.Key, Frequency = x.Value };
                queue.Enqueue(node, x.Value);
            }

            while (queue.Count > 1)
            {
                var lhs = queue.Dequeue();
                var rhs = queue.Dequeue();

                var parent = new HuffmanNode
                {
                    Frequency = lhs.Frequency + rhs.Frequency,
                    Left = lhs,
                    Right = rhs
                };

                queue.Enqueue(parent, parent.Frequency);
            }

            return queue.Dequeue();
        }
    }

    public sealed class Cobpression
    {
        public static IReadOnlyList<Symbol> SymbolsFromBuffer(byte[] source)
        {
            var mtf = new byte[256];
            for (int i = 0; i < 256; ++i)
                mtf[i] = (byte)i;

            var result = new List<Symbol>(source.Length);

            uint brHash = 0;
            uint brHashMask = (1 << 16) - 1;
            var brPos = new int[brHashMask + 1];

            var runLength = 0;
            var lx = source[0];
            UpdateHash(source[0], 0);
            UpdateHash(source[1], 1);
            UpdateHash(source[2], 2);
            UpdateHash(source[3], 3);

            for (int i = 1; i <= source.Length; ++i)
            {
                var isTerminal = i == source.Length;
                var x = !isTerminal ? source[i] : lx;

                if (i < source.Length - 4)
                {
                    const int MinLookback = 4;
                    const int MaxLookback = (1 << 24) - 1;//255;
                    var seenIdx = UpdateHash(source[i + 3], i + 3);
                    if (seenIdx >= MinLookback
                    && (i - seenIdx) > MinLookback
                    && (i - seenIdx) < MaxLookback - MinLookback
                    && source[seenIdx - 0] == source[i + 3]
                    && source[seenIdx - 1] == source[i + 2]
                    && source[seenIdx - 2] == source[i + 1]
                    && source[seenIdx - 3] == source[i + 0]
                    && (source[i + 0] != source[i + 1] || source[i + 0] != source[i + 2] ||
                        source[i + 0] != source[i + 2] || source[i + 0] != source[i + 3]))
                    {
                        int offset = i - seenIdx + 3;
                        int length = 4;

                        ++runLength;
                        while (runLength > 0)
                        {
                            int symLength;
                            if (runLength > 0x40000) symLength = 0x40000;
                            else if (runLength > 0x20000) symLength = 0x20000;
                            else if (runLength > 0x10000) symLength = 0x10000;
                            else if (runLength > 0x08000) symLength = 0x08000;
                            else if (runLength > 0x04000) symLength = 0x04000;
                            else if (runLength > 0x02000) symLength = 0x02000;
                            else if (runLength > 0x01000) symLength = 0x01000;
                            else if (runLength > 0x00800) symLength = 0x00800;
                            else if (runLength > 0x00400) symLength = 0x00400;
                            else if (runLength > 0x00200) symLength = 0x00200;
                            else if (runLength > 0x00100) symLength = 0x00100;
                            else if (runLength > 0x00080) symLength = 0x00080;
                            else if (runLength > 0x00040) symLength = 0x00040;
                            else if (runLength > 0x00020) symLength = 0x00020;
                            else if (runLength > 0x00010) symLength = 0x00010;
                            else if (runLength > 0x00008) symLength = 0x00008;
                            else symLength = 1;

                            var mtfIdx = Array.IndexOf(mtf, lx);
                            Array.Copy(mtf, 0, mtf, 1, mtfIdx);
                            mtf[0] = lx;

                            result.Add(new Symbol
                            {
                                Type = symLength switch
                                {
                                    1 => SymbolType.Literal,
                                    <= 0x80000 => SymbolType.RLE,
                                    _ => throw new ArgumentOutOfRangeException()
                                },
                                Value = (byte)mtfIdx,
                                Length = symLength
                            });

                            runLength -= symLength;
                        }

                        lx = x;

                        ++seenIdx;
                        for (var j = i + length; seenIdx < i && j < source.Length && length < MaxLookback; ++j, ++seenIdx)
                        {
                            x = source[j];
                            UpdateHash(x, j);

                            if (x != source[seenIdx])
                                break;

                            ++length;
                        }

                        // TODO Discard if backref is more expensive
                        result.Add(new Symbol
                        {
                            Type = offset switch
                            {
                                < (1 << 4) when length < (1 << 4) => SymbolType.BackRef4,
                                < (1 << 7) when length < (1 << 7) => SymbolType.BackRef7,
                                < (1 << 9) when length < (1 << 9) => SymbolType.BackRef9,
                                < (1 << 12) when length < (1 << 12) => SymbolType.BackRef12,
                                < (1 << 16) when length < (1 << 16) => SymbolType.BackRef16,
                                < (1 << 24) when length < (1 << 24) => SymbolType.BackRef24,
                                _ => throw new ArgumentOutOfRangeException()
                            },
                            Offset = offset,
                            Length = length
                        });

                        runLength = 0;
                        i += length;

                        if (i >= source.Length)
                            break;

                        lx = source[i];

                        UpdateHash(source[i + 1], i + 1);
                        UpdateHash(source[i + 2], i + 2);
                        UpdateHash(source[i + 3], i + 3);
                        continue;
                    }
                }

                ++runLength;
                while ((isTerminal || x != lx) && runLength > 0)
                {
                    int symLength;
                         if (runLength > 0x40000) symLength = 0x40000;
                    else if (runLength > 0x20000) symLength = 0x20000;
                    else if (runLength > 0x10000) symLength = 0x10000;
                    else if (runLength > 0x08000) symLength = 0x08000;
                    else if (runLength > 0x04000) symLength = 0x04000;
                    else if (runLength > 0x02000) symLength = 0x02000;
                    else if (runLength > 0x01000) symLength = 0x01000;
                    else if (runLength > 0x00800) symLength = 0x00800;
                    else if (runLength > 0x00400) symLength = 0x00400;
                    else if (runLength > 0x00200) symLength = 0x00200;
                    else if (runLength > 0x00100) symLength = 0x00100;
                    else if (runLength > 0x00080) symLength = 0x00080;
                    else if (runLength > 0x00040) symLength = 0x00040;
                    else if (runLength > 0x00020) symLength = 0x00020;
                    else if (runLength > 0x00010) symLength = 0x00010;
                    else if (runLength > 0x00008) symLength = 0x00008;
                    else symLength = 1;

                    var mtfIdx = Array.IndexOf(mtf, lx);
                    Array.Copy(mtf, 0, mtf, 1, mtfIdx);
                    mtf[0] = lx;

                    result.Add(new Symbol
                    {
                        Type = symLength switch
                        {
                            1 => SymbolType.Literal,
                            <= 0x80000 => SymbolType.RLE,
                            _ => throw new ArgumentOutOfRangeException()
                        },
                        Value = (byte)mtfIdx,
                        Length = symLength
                    });

                    runLength -= symLength;
                }

                lx = x;
            }

            return result;

            int UpdateHash(byte b, int pos)
            {
                brHash = ((brHash << 5) ^ b) & brHashMask;
                var prev = brPos[(int)brHash];
                brPos[(int)brHash] = pos;
                return prev;
            }
        }

        public static byte[] Encode(byte[] source)
        {
            var symbols = SymbolsFromBuffer(source);
            var root = HuffmanNode.TreeFromSymbols(symbols);
            var codes = root.ResolveEncodingTable();

            var stream = new BitStream();
            
            // Encode Tree
            stream.Write(2, 0x02);

            // PRESENCE BITMAP
            int min = codes.Values.Min(x => x.Length);

            const int MaxSymbol = 320;
            for (int i = 0; i < MaxSymbol;)
            {
                int runType = -1; // 0 All Absent, 1 All Present, 2 Mixed
                for (int j = 0; runType != 2 && j < 16 && i + j < MaxSymbol; ++j)
                {
                    var isPresent = codes.ContainsKey(new Symbol(i + j));
                    if (runType == -1)
                        runType = isPresent ? 1 : 0;
                    else if (runType == 0)
                        runType = isPresent ? 2 : 0;
                    else if (runType == 1)
                        runType = isPresent ? 1 : 2;
                    else
                        break;
                }

                if (runType == 0 || runType == 1)
                {
                    uint len = 0;
                    for (int j = 16; j <= 128 && i + j < MaxSymbol; ++j)
                    {
                        var isPresent = codes.ContainsKey(new Symbol(i + j));
                        if (runType == 0 && isPresent)
                            break;
                        if (runType == 1 && !isPresent)
                            break;

                        if (j == 32 || j == 64 || j == 128)
                            ++len;
                    }

                    stream.Write(false);
                    stream.Write(runType == 1);
                    stream.Write(2, len);
                    i += len switch
                    {
                        0 => 16,
                        1 => 32,
                        2 => 64,
                        3 => 128,
                        _ => throw new ArgumentOutOfRangeException()
                    };
                }
                else if (runType == 2)
                {
                    stream.Write(true);
                    for (int j = 0; j < 16 && i + j < MaxSymbol; ++j)
                    {
                        var isPresent = codes.ContainsKey(new Symbol(i + j));
                        stream.Write(isPresent);
                    }

                    i += 16;
                }
                else
                    throw new ArgumentOutOfRangeException();
            }

            // SYMBOL LENGTH TABLE
            int offset = Math.Min(0x1F, min);
            stream.Write(5, (uint)offset);
            for (int i = 0; i < MaxSymbol; ++i)
            {
                var symbol = new Symbol(i);

                if (!codes.TryGetValue(symbol, out var code))
                    continue;

                var delta = code.Length - offset;
                if (delta == 0)
                    stream.Write(false);
                else if (delta == -1 || delta == 1)
                {
                    stream.Write(true);
                    stream.Write(delta == 1);
                    stream.Write(false);
                }
                else if (delta >= -4 && delta <= 3)
                {
                    stream.Write(true);
                    stream.Write(1, (uint)delta >> 2);
                    stream.Write(true);
                    stream.Write(2, (uint)delta & 0x3);
                    stream.Write(false);
                }
                else if (delta >= -16 && delta <= 15)
                {
                    stream.Write(true);
                    stream.Write(1, (uint)delta >> 4);
                    stream.Write(true);
                    stream.Write(2, ((uint)delta >> 2) & 0x3);
                    stream.Write(true);
                    stream.Write(2, (uint)delta & 0x3);
                    stream.Write(false);
                }
                else if (delta >= -256 && delta <= 255)
                {
                    stream.Write(true);
                    stream.Write(1, (uint)delta >> 8);
                    stream.Write(true);
                    stream.Write(2, ((uint)delta >> 6) & 0x3);
                    stream.Write(true);
                    stream.Write(2, ((uint)delta >> 4) & 0x3);
                    stream.Write(true);
                    stream.Write(4, (uint)delta & 0xF);
                    stream.Write(false);
                }

                offset = code.Length;
            }

            var padOffset = stream.Position;

            // Encode Symbols
            foreach (var x in symbols)
            {
                var code = codes[x];

                stream.Write(code.Length, code.Value);

                switch (x.Type)
                {
                    case SymbolType.Literal:
                        break;
                    case SymbolType.RLE:
                        stream.Write(4, (uint)Math.Log2(x.Length) - (uint)Math.Log2(8));
                        code = codes[x.Value];
                        stream.Write(code.Length, code.Value);
                        break;
                    case SymbolType.BackRef4:
                        stream.Write(4, (uint)x.Offset);
                        stream.Write(4, (uint)x.Length);
                        break;
                    case SymbolType.BackRef7:
                        stream.Write(7, (uint)x.Offset);
                        stream.Write(7, (uint)x.Length);
                        break;
                    case SymbolType.BackRef9:
                        stream.Write(9, (uint)x.Offset);
                        stream.Write(9, (uint)x.Length);
                        break;
                    case SymbolType.BackRef12:
                        stream.Write(12, (uint)x.Offset);
                        stream.Write(12, (uint)x.Length);
                        break;
                    case SymbolType.BackRef16:
                        stream.Write(16, (uint)x.Offset);
                        stream.Write(16, (uint)x.Length);
                        break;
                    case SymbolType.BackRef24:
                        stream.Write(24, (uint)x.Offset);
                        stream.Write(24, (uint)x.Length);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            // Terminal Padding
            int padLength = 8 - stream.Length % 8;
            stream.Position = padOffset;
            while (--padLength > 0)
                stream.Insert(true);
            stream.Insert(false);

            return stream.ToArray();
        }

        public static byte[] Decode(byte[] source)
        {
            var result = new List<byte>(source.Length);
            var stream = new BitStream(source);

            // Prefix
            if (stream.Read() != true)
                throw new InvalidDataException("Expected MSB to be 1");

            if (stream.Read() != false)
                throw new InvalidDataException("Expected MSB+1 to be 0");

            // Decode Tree
            const int MaxSymbol = 320;
            var present = new ushort[MaxSymbol / 16];
            for (int i = 0; i < MaxSymbol / 16;)
            {
                if (stream.Read())
                {
                    present[i++] = (ushort)stream.Read(16);
                }
                else
                {
                    var value = stream.Read();
                    var len = 1 << (int)stream.Read(2);
                    while (len-- > 0)
                        present[i++] = (ushort)(value ? 0xFFFF : 0x0000);
                }
            }

            var symLengths = new int[MaxSymbol];
            int symOffset = (int)stream.Read(5);
            for (int i = 0; i < MaxSymbol; ++i)
            {
                int j = i / 16;
                var k = i % 16;
                if ((present[j] & (1 << (15 - k))) == 0)
                    continue;

                int delta;
                if (stream.Read())
                {
                    var v0 = stream.Read(1);
                    if (stream.Read())
                    {
                        var v1 = stream.Read(2);
                        if (stream.Read())
                        {
                            var v2 = stream.Read(2);
                            if (stream.Read())
                            {
                                var v3 = stream.Read(4);

                                const int sh = (sizeof(int) * 8) - 9;
                                delta = ((int)((v0 << 8) | (v1 << 4) | (v2 << 2) | v3) << sh) >> sh;
                            }
                            else
                            {
                                const int sh = (sizeof(int) * 8) - 5;
                                delta = ((int)((v0 << 4) | (v1 << 2) | v2) << sh) >> sh;
                            }
                        }
                        else
                        {
                            const int sh = (sizeof(int) * 8) - 3;
                            delta = ((int)((v0 << 2) | v1) << sh) >> sh;
                        }
                    }
                    else
                        delta = v0 == 1 ? 1 : -1;
                }
                else
                    delta = 0;

                symOffset += delta;
                symLengths[i] = symOffset;
            }

            var blCounts = new int[MaxSymbol - 1];
            for (int i = 0; i < MaxSymbol; ++i)
            {
                int j = i / 16;
                var k = i % 16;
                if ((present[j] & (1 << (15 - k))) == 0)
                    continue;

                blCounts[symLengths[i]]++;
            }

            var code = 0;
            var nextCode = new int[MaxSymbol];
            for (int bits = 1; bits <= MaxSymbol - 1; ++bits)
            {
                code = (code + blCounts[bits - 1]) << 1;
                nextCode[bits] = code;
            }

            var codes = new (int Length, int Code)[MaxSymbol];
            for (int i = 0; i < MaxSymbol; ++i)
            {
                var len = symLengths[i];
                if (len != 0)
                {
                    codes[i].Code = nextCode[len];
                    codes[i].Length = len;
                    nextCode[len]++;
                }
            }

            var tree = new HuffmanNode();
            for (int i = 0; i < codes.Length; ++i)
            {
                var x = codes[i];
                if (x.Length == 0)
                    continue;

                var node = tree;
                for (int j = 0; j < x.Length; ++j)
                {
                    var b = (x.Code & (1 << (x.Length - 1 - j))) != 0;
                    if (b)
                    {
                        node.Right ??= new HuffmanNode();
                        node = node.Right;
                    }
                    else
                    {
                        node.Left ??= new HuffmanNode();
                        node = node.Left;
                    }
                }

                if (i >= 256)
                    node.Value = new Symbol { Type = (SymbolType)(i - 256 + 1) };
                else
                    node.Value = new Symbol { Type = SymbolType.Literal, Value = (byte)i, Length = x.Length };
            }

            // Terminal Padding
            while (stream.Read()) { }

            // Decode Symbols
            var mtf = new byte[256];
            for (int i = 0; i < 256; ++i)
                mtf[i] = (byte)i;

            while (stream.Position < stream.Length)
            {
                var symbol = ReadSymbol();
                switch (symbol.Type)
                {
                    case SymbolType.Literal:
                    {
                        var mtfIdx = mtf[symbol.Value];
                        Array.Copy(mtf, 0, mtf, 1, symbol.Value);
                        mtf[0] = mtfIdx;

                        result.Add(mtfIdx);
                        break;
                    }
                    case SymbolType.RLE:
                    {
                        var length = 1 << (int)(stream.Read(4) + (uint)Math.Log2(8));
                        symbol = ReadSymbol();

                        var mtfIdx = mtf[symbol.Value];
                        Array.Copy(mtf, 0, mtf, 1, symbol.Value);
                        mtf[0] = mtfIdx;

                        while (length-- > 0)
                            result.Add(mtfIdx);
                        break;
                    }
                    case SymbolType.BackRef4:
                    {
                        var offset = stream.Read(4);
                        var length = stream.Read(4);

                        while (length-- > 0)
                            result.Add(result[result.Count - (int)offset]);
                        break;
                    }
                    case SymbolType.BackRef7:
                    {
                        var offset = stream.Read(7);
                        var length = stream.Read(7);

                        //Debug.WriteLine($"REF -{offset}..{length}");

                        while (length-- > 0)
                            result.Add(result[result.Count - (int)offset]);
                        break;
                    }
                    case SymbolType.BackRef9:
                    {
                        var offset = stream.Read(9);
                        var length = stream.Read(9);

                        while (length-- > 0)
                            result.Add(result[result.Count - (int)offset]);
                        break;
                    }
                    case SymbolType.BackRef12:
                    {
                        var offset = stream.Read(12);
                        var length = stream.Read(12);

                        while (length-- > 0)
                            result.Add(result[result.Count - (int)offset]);
                        break;
                    }
                    case SymbolType.BackRef16:
                    {
                        var offset = stream.Read(16);
                        var length = stream.Read(16);

                        while (length-- > 0)
                            result.Add(result[result.Count - (int)offset]);
                        break;
                    }
                    case SymbolType.BackRef24:
                    {
                        var offset = stream.Read(24);
                        var length = stream.Read(24);

                        while (length-- > 0)
                            result.Add(result[result.Count - (int)offset]);
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return result.ToArray();

            Symbol ReadSymbol() => tree.Decode(stream);
        }
    }
}
