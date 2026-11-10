using System.Buffers.Binary;
using System.Text;

namespace ComplexTweaks.Tweaks;

internal readonly record struct EncodedTicket(string Text, int Length);

internal static class TicketEncoding {
    private const string BlowfishKey = "#un@e=x>";
    private const string FUCKED_GARBAGE_ALPHABET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-_";
    private const int SPLIT_SIZE = 300;

    // https://github.com/goatcorp/FFXIVQuickLauncher/blob/c57b1fb86443d1f4b784b85c07f73b1225da3d3c/src/XIVLauncher.Common/Encryption/Ticket.cs#L38-L115
    // https://github.com/goatcorp/FFXIVQuickLauncher/blob/c57b1fb86443d1f4b784b85c07f73b1225da3d3c/src/XIVLauncher.Common/Encryption/CrtRand.cs#L12-L16
    // https://github.com/goatcorp/FFXIVQuickLauncher/blob/c57b1fb86443d1f4b784b85c07f73b1225da3d3c/src/XIVLauncher.Common/Util/GameHelpers.cs#L97-L103
    public static EncodedTicket Encode(ReadOnlySpan<byte> ticket, uint steamTime) {
        var minute = steamTime - 5;
        minute -= minute % 60;

        // layout: u16 sum | hex text | NUL | padding, sized to a multiple of the 8-byte cipher block.
        var text = Encoding.ASCII.GetBytes(Convert.ToHexStringLower(ticket) + '\0');
        var sum = (ushort)text.Sum(b => b);
        var block = new byte[(text.Length + 9) & ~7];
        BinaryPrimitives.WriteUInt16LittleEndian(block, sum);
        text.CopyTo(block.AsSpan(2));

        // the first 4 bytes become a running total over the padding, which comes from the MSVC rand() sequence.
        var random = minute ^ (uint)(short)sum;
        var total = BinaryPrimitives.ReadUInt32LittleEndian(block);
        for (var i = 2 + text.Length; i < block.Length; i++) {
            random = unchecked(random * 0x343FD + 0x269EC3);
            var next = (random >> 16) & 0x7FFF;
            var character = FUCKED_GARBAGE_ALPHABET[(int)((total + next) & 0x3F)];
            block[i] = (byte)character;
            total += character;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(block, total);
        (block[0], block[1]) = (block[1], block[0]);

        new Blowfish(Encoding.ASCII.GetBytes($"{minute:x8}{BlowfishKey}")).EncryptEcb(block);

        var encoded = Convert.ToBase64String(block).Replace('+', '-').Replace('/', '_').Replace('=', '*');
        return new EncodedTicket(string.Join(',', encoded.Chunk(SPLIT_SIZE).Select(c => new string(c))), encoded.Length);
    }
}

internal sealed class Blowfish {
    private const int Rounds = 16;
    private const int SBoxEntries = 256;
    private static readonly Lazy<uint[]> PiWords = new(ComputePiWords);

    private readonly uint[] _p = new uint[Rounds + 2];
    private readonly uint[] _s = new uint[4 * SBoxEntries];

    public Blowfish(ReadOnlySpan<byte> key) {
        var pi = PiWords.Value;
        pi.AsSpan(0, _p.Length).CopyTo(_p);
        pi.AsSpan(_p.Length).CopyTo(_s);

        var position = 0;
        for (var i = 0; i < _p.Length; i++) {
            var word = 0u;
            for (var b = 0; b < 4; b++, position++)
                word = (word << 8) | key[position % key.Length];
            _p[i] ^= word;
        }

        uint left = 0, right = 0;
        for (var i = 0; i < _p.Length; i += 2) {
            (left, right) = EncryptBlock(left, right);
            _p[i] = left;
            _p[i + 1] = right;
        }
        for (var i = 0; i < _s.Length; i += 2) {
            (left, right) = EncryptBlock(left, right);
            _s[i] = left;
            _s[i + 1] = right;
        }
    }

    public void EncryptEcb(Span<byte> data) {
        for (var offset = 0; offset + 8 <= data.Length; offset += 8) {
            var block = data.Slice(offset, 8);
            var (left, right) = EncryptBlock(BinaryPrimitives.ReadUInt32BigEndian(block), BinaryPrimitives.ReadUInt32BigEndian(block[4..]));
            BinaryPrimitives.WriteUInt32BigEndian(block, left);
            BinaryPrimitives.WriteUInt32BigEndian(block[4..], right);
        }
    }

    private (uint Left, uint Right) EncryptBlock(uint left, uint right) {
        for (var round = 0; round < Rounds; round++) {
            left ^= _p[round];
            right ^= Feistel(left);
            (left, right) = (right, left);
        }
        return (right ^ _p[Rounds + 1], left ^ _p[Rounds]);
    }

    private uint Feistel(uint x) => unchecked(
        ((_s[x >> 24] + _s[SBoxEntries + (byte)(x >> 16)]) ^ _s[2 * SBoxEntries + (byte)(x >> 8)]) + _s[3 * SBoxEntries + (byte)x]);

    private static uint[] ComputePiWords() {
        var words = new uint[Rounds + 2 + 4 * SBoxEntries];
        var bits = words.Length * 32;
        const int guardBits = 64;

        // machin: pi = 16 atan(1/5) - 4 atan(1/239)
        var one = BigInteger.One << (bits + guardBits);
        var fraction = (16 * InverseArcTan(5, one) - 4 * InverseArcTan(239, one) - 3 * one) >> guardBits;

        for (var i = 0; i < words.Length; i++)
            words[i] = (uint)((fraction >> (bits - 32 * (i + 1))) & uint.MaxValue);
        return words;
    }

    private static BigInteger InverseArcTan(int x, BigInteger one) {
        var power = one / x;
        var sum = power;
        var square = x * x;
        for (var n = 3; !power.IsZero; n += 2) {
            power /= square;
            sum += (n & 2) == 0 ? power / n : -(power / n);
        }
        return sum;
    }
}
