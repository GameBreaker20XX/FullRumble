namespace NascarRumble.Host;

/// <summary>
/// Decoder for the game's .LSC still images (e.g. CW/FEND/FELD.LSC, the title art).
/// Layout: [u32 sizeA][u32 sizeB][section A][section B]; each section is one vertical half,
/// [u16 width][u16 height] followed by a PlayStation MDEC "BS" v2 frame. Images are read from the
/// user's own disc at run time; nothing of the game is stored in the repository.
/// </summary>
public static class LscImage
{
    /// <summary>Decodes a whole .LSC file into RGBA (width x height x 4).</summary>
    public static (byte[] Rgba, int Width, int Height) Decode(byte[] lsc)
    {
        int sizeA = BitConverter.ToInt32(lsc, 0), sizeB = BitConverter.ToInt32(lsc, 4);
        if (sizeA <= 0 || sizeB <= 0 || 8L + sizeA + sizeB > lsc.Length)
            throw new InvalidDataException("not an LSC image");
        var left = DecodeSection(lsc.AsSpan(8, sizeA));
        var right = DecodeSection(lsc.AsSpan(8 + sizeA, sizeB));
        int width = left.Width + right.Width, height = Math.Max(left.Height, right.Height);
        var rgba = new byte[width * height * 4];
        Blit(left, 0); Blit(right, left.Width);
        return (rgba, width, height);

        void Blit((byte[] Rgba, int Width, int Height) half, int x0)
        {
            for (int y = 0; y < half.Height; y++)
                Array.Copy(half.Rgba, y * half.Width * 4, rgba, (y * width + x0) * 4, half.Width * 4);
        }
    }

    private static (byte[] Rgba, int Width, int Height) DecodeSection(ReadOnlySpan<byte> section)
    {
        int width = BitConverter.ToUInt16(section[..2]), height = BitConverter.ToUInt16(section[2..4]);
        var bs = section[4..];
        if (BitConverter.ToUInt16(bs[2..4]) != 0x3800) throw new InvalidDataException("bad BS magic");
        int qscale = BitConverter.ToUInt16(bs[4..6]);
        int version = BitConverter.ToUInt16(bs[6..8]);
        if (version != 2) throw new InvalidDataException($"unsupported BS version {version}");

        var reader = new BitReader(bs[8..].ToArray());
        var rgba = new byte[width * height * 4];
        var blocks = new int[6][];
        for (int i = 0; i < 6; i++) blocks[i] = new int[64];

        // Macroblocks run top to bottom, then left to right; blocks are Cr, Cb, Y0..Y3.
        for (int mx = 0; mx < (width + 15) / 16; mx++)
        for (int my = 0; my < (height + 15) / 16; my++)
        {
            for (int b = 0; b < 6; b++) DecodeBlock(ref reader, qscale, blocks[b]);
            StoreMacroblock(blocks, rgba, width, height, mx * 16, my * 16);
        }
        return (rgba, width, height);
    }

    private static readonly int[] Quant =
    [
        2, 16, 19, 22, 26, 27, 29, 34,
        16, 16, 22, 24, 27, 29, 34, 37,
        19, 22, 26, 27, 29, 34, 34, 38,
        22, 22, 26, 27, 29, 34, 37, 40,
        22, 26, 27, 29, 32, 35, 40, 48,
        26, 27, 29, 32, 35, 40, 48, 58,
        26, 27, 29, 34, 38, 46, 56, 69,
        27, 29, 35, 38, 46, 56, 69, 83,
    ];

    private static readonly int[] ZigZag =
    [
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
        12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
    ];

    private static void DecodeBlock(ref BitReader reader, int qscale, int[] block)
    {
        Array.Clear(block);
        int dc = reader.ReadSigned(10);
        block[0] = dc * Quant[0];
        int index = 0;
        while (true)
        {
            if (reader.Peek(2) == 0b10) { reader.Skip(2); break; }      // end of block
            int run, level;
            if (reader.Peek(6) == 0b000001)                            // escape
            {
                reader.Skip(6);
                run = reader.Read(6);
                level = reader.ReadSigned(10);
            }
            else
            {
                (run, level) = Vlc.Read(ref reader);
            }
            index += run + 1;
            if (index > 63) throw new InvalidDataException("AC index overflow");
            int natural = ZigZag[index];
            block[natural] = (level * Quant[natural] * qscale + 4) / 8;
        }
        Idct(block);
    }

    private static readonly double[,] Basis = BuildBasis();

    private static double[,] BuildBasis()
    {
        var c = new double[8, 8];
        for (int u = 0; u < 8; u++)
        for (int x = 0; x < 8; x++)
            c[u, x] = (u == 0 ? Math.Sqrt(0.125) : 0.5) * Math.Cos((2 * x + 1) * u * Math.PI / 16);
        return c;
    }

    private static void Idct(int[] block)
    {
        Span<double> tmp = stackalloc double[64];
        for (int y = 0; y < 8; y++)
        for (int u = 0; u < 8; u++)
        {
            double s = 0;
            for (int v = 0; v < 8; v++) s += Basis[v, y] * block[v * 8 + u];
            tmp[y * 8 + u] = s;
        }
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
        {
            double s = 0;
            for (int u = 0; u < 8; u++) s += Basis[u, x] * tmp[y * 8 + u];
            block[y * 8 + x] = (int)Math.Round(s);
        }
    }

    private static void StoreMacroblock(int[][] blocks, byte[] rgba, int width, int height, int x0, int y0)
    {
        int[] cr = blocks[0], cb = blocks[1];
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            int px = x0 + x, py = y0 + y;
            if (px >= width || py >= height) continue;
            int[] luma = blocks[2 + (y >> 3) * 2 + (x >> 3)];
            double l = luma[(y & 7) * 8 + (x & 7)];
            int c = (y >> 1) * 8 + (x >> 1);
            double r = l + 1.402 * cr[c];
            double g = l - 0.3437 * cb[c] - 0.7143 * cr[c];
            double b = l + 1.772 * cb[c];
            int o = (py * width + px) * 4;
            rgba[o] = Clamp(r + 128); rgba[o + 1] = Clamp(g + 128); rgba[o + 2] = Clamp(b + 128);
            rgba[o + 3] = 255;
        }
    }

    private static byte Clamp(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);

    /// <summary>Bit reader over 16-bit little-endian words, most significant bit first.</summary>
    private struct BitReader(byte[] data)
    {
        private int _bit;

        private readonly int Bit(int position)
        {
            int word = position >> 4;
            if (word * 2 + 1 >= data.Length) return 0;
            int value = data[word * 2] | (data[word * 2 + 1] << 8);
            return (value >> (15 - (position & 15))) & 1;
        }

        public readonly int Peek(int count)
        {
            int v = 0;
            for (int i = 0; i < count; i++) v = (v << 1) | Bit(_bit + i);
            return v;
        }

        public void Skip(int count) => _bit += count;

        public int Read(int count) { int v = Peek(count); _bit += count; return v; }

        public int ReadSigned(int count)
        {
            int v = Read(count);
            return v >= 1 << (count - 1) ? v - (1 << count) : v;
        }
    }

    /// <summary>MPEG-1 AC coefficient VLC table (non-first coefficients), as used by BS v2.</summary>
    private static class Vlc
    {
        private static readonly Dictionary<(int Length, int Code), (int Run, int Level)> Table = Build();

        private static Dictionary<(int, int), (int, int)> Build()
        {
            var t = new Dictionary<(int, int), (int, int)>();
            void A(string code, int run, int level) => t[(code.Length, Convert.ToInt32(code, 2))] = (run, level);
            A("11", 0, 1);
            A("011", 1, 1); A("0100", 0, 2); A("0101", 2, 1); A("00101", 0, 3); A("00111", 3, 1);
            A("00110", 4, 1); A("000110", 1, 2); A("000111", 5, 1); A("000101", 6, 1); A("000100", 7, 1);
            A("0000110", 0, 4); A("0000100", 2, 2); A("0000111", 8, 1); A("0000101", 9, 1);
            A("00100110", 0, 5); A("00100001", 0, 6); A("00100101", 1, 3); A("00100100", 3, 2);
            A("00100111", 10, 1); A("00100011", 11, 1); A("00100010", 12, 1); A("00100000", 13, 1);
            A("0000001010", 0, 7); A("0000001100", 1, 4); A("0000001011", 2, 3); A("0000001111", 4, 2);
            A("0000001001", 5, 2); A("0000001110", 14, 1); A("0000001101", 15, 1); A("0000001000", 16, 1);
            A("000000011101", 0, 8); A("000000011000", 0, 9); A("000000010011", 0, 10); A("000000010000", 0, 11);
            A("000000011011", 1, 5); A("000000010100", 2, 4); A("000000011100", 3, 3); A("000000010010", 4, 3);
            A("000000011110", 6, 2); A("000000010101", 7, 2); A("000000010001", 8, 2); A("000000011111", 17, 1);
            A("000000011010", 18, 1); A("000000011001", 19, 1); A("000000010111", 20, 1); A("000000010110", 21, 1);
            A("0000000011010", 0, 12); A("0000000011001", 0, 13); A("0000000011000", 0, 14); A("0000000010111", 0, 15);
            A("0000000010110", 1, 6); A("0000000010101", 1, 7); A("0000000010100", 2, 5); A("0000000010011", 3, 4);
            A("0000000010010", 5, 3); A("0000000010001", 9, 2); A("0000000010000", 10, 2); A("0000000011111", 22, 1);
            A("0000000011110", 23, 1); A("0000000011101", 24, 1); A("0000000011100", 25, 1); A("0000000011011", 26, 1);
            A("00000000011111", 0, 16); A("00000000011110", 0, 17); A("00000000011101", 0, 18); A("00000000011100", 0, 19);
            A("00000000011011", 0, 20); A("00000000011010", 0, 21); A("00000000011001", 0, 22); A("00000000011000", 0, 23);
            A("00000000010111", 0, 24); A("00000000010110", 0, 25); A("00000000010101", 0, 26); A("00000000010100", 0, 27);
            A("00000000010011", 0, 28); A("00000000010010", 0, 29); A("00000000010001", 0, 30); A("00000000010000", 0, 31);
            A("000000000011000", 0, 32); A("000000000010111", 0, 33); A("000000000010110", 0, 34); A("000000000010101", 0, 35);
            A("000000000010100", 0, 36); A("000000000010011", 0, 37); A("000000000010010", 0, 38); A("000000000010001", 0, 39);
            A("000000000010000", 0, 40); A("000000000011111", 1, 8); A("000000000011110", 1, 9); A("000000000011101", 1, 10);
            A("000000000011100", 1, 11); A("000000000011011", 1, 12); A("000000000011010", 1, 13); A("000000000011001", 1, 14);
            A("0000000000010011", 1, 15); A("0000000000010010", 1, 16); A("0000000000010001", 1, 17); A("0000000000010000", 1, 18);
            A("0000000000010100", 6, 3); A("0000000000011010", 11, 2); A("0000000000011001", 12, 2); A("0000000000011000", 13, 2);
            A("0000000000010111", 14, 2); A("0000000000010110", 15, 2); A("0000000000010101", 16, 2); A("0000000000011111", 27, 1);
            A("0000000000011110", 28, 1); A("0000000000011101", 29, 1); A("0000000000011100", 30, 1); A("0000000000011011", 31, 1);
            return t;
        }

        public static (int Run, int Level) Read(ref BitReader reader)
        {
            for (int length = 2; length <= 16; length++)
            {
                if (!Table.TryGetValue((length, reader.Peek(length)), out var entry)) continue;
                reader.Skip(length);
                return (entry.Run, reader.Read(1) == 1 ? -entry.Level : entry.Level);
            }
            throw new InvalidDataException("bad AC code");
        }
    }
}
