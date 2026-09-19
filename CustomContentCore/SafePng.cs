using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.Xna.Framework;

namespace CustomContentCore
{
    /// <summary>
    /// A strict PNG decoder and a simple PNG encoder in managed code. Images received from other players are decoded with this
    /// (never with the game's native image decoder) and re-encoded, so only plain pixels survive: no metadata, no appended data,
    /// no malformed structures that could target a decoder bug.
    /// </summary>
    internal static class SafePng
    {
        /*********
        ** Fields
        *********/
        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        private static readonly uint[] CrcTable = BuildCrcTable();

        /// <summary>The Adam7 interlace passes: x start, y start, x step, y step.</summary>
        private static readonly (int X, int Y, int DX, int DY)[] Adam7 = { (0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2) };


        /*********
        ** Public methods
        *********/
        /// <summary>Decode a PNG, rejecting anything malformed or outside the size limits. Thread-safe.</summary>
        /// <param name="bytes">The PNG file.</param>
        /// <param name="maxSide">The largest width or height allowed.</param>
        /// <param name="maxPixels">The most pixels allowed.</param>
        /// <exception cref="InvalidDataException">The file isn't a valid PNG within the limits.</exception>
        public static Pixels Decode(byte[] bytes, int maxSide, long maxPixels)
        {
            if (bytes.Length < Signature.Length + 12 || !bytes.AsSpan(0, Signature.Length).SequenceEqual(Signature))
                throw new InvalidDataException("not a PNG");

            int width = 0, height = 0, bitDepth = 0, colorType = 0, interlace = 0;
            byte[]? palette = null, transparency = null;
            using MemoryStream idat = new();
            bool seenHeader = false, seenEnd = false;

            int pos = Signature.Length;
            while (pos < bytes.Length)
            {
                // chunk: length, type, data, CRC
                if (pos + 12 > bytes.Length)
                    throw new InvalidDataException("truncated chunk");
                int length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(pos));
                if (length < 0 || length > bytes.Length - pos - 12)
                    throw new InvalidDataException("bad chunk length");
                string type = Encoding.ASCII.GetString(bytes, pos + 4, 4);
                ReadOnlySpan<byte> data = bytes.AsSpan(pos + 8, length);
                uint crc = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(pos + 8 + length));
                if (Crc(bytes.AsSpan(pos + 4, length + 4)) != crc)
                    throw new InvalidDataException($"bad checksum in {Printable(type)} chunk");
                pos += 12 + length;

                if (!seenHeader && type != "IHDR")
                    throw new InvalidDataException("IHDR must come first");
                switch (type)
                {
                    case "IHDR":
                        if (seenHeader || length != 13)
                            throw new InvalidDataException("bad IHDR");
                        seenHeader = true;
                        width = BinaryPrimitives.ReadInt32BigEndian(data);
                        height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                        bitDepth = data[8];
                        colorType = data[9];
                        interlace = data[12];
                        if (width <= 0 || height <= 0 || width > maxSide || height > maxSide || (long)width * height > maxPixels)
                            throw new InvalidDataException($"image size {width}x{height} is outside the allowed limits");
                        bool validDepth = colorType switch
                        {
                            0 => bitDepth is 1 or 2 or 4 or 8 or 16,
                            3 => bitDepth is 1 or 2 or 4 or 8,
                            2 or 4 or 6 => bitDepth is 8 or 16,
                            _ => false
                        };
                        if (!validDepth || data[10] != 0 || data[11] != 0 || interlace > 1)
                            throw new InvalidDataException("unsupported PNG format");
                        break;

                    case "PLTE":
                        if (length == 0 || length % 3 != 0 || length / 3 > 256 || palette != null)
                            throw new InvalidDataException("bad palette");
                        palette = data.ToArray();
                        break;

                    case "tRNS":
                        transparency = data.ToArray();
                        break;

                    case "IDAT":
                        if (idat.Length + length > bytes.Length)
                            throw new InvalidDataException("bad image data");
                        idat.Write(data);
                        break;

                    case "IEND":
                        seenEnd = true;
                        break;

                    default:
                        // other chunks (text, color profiles...) are skipped; critical unknown chunks (uppercase first letter) can't be ignored
                        if (char.IsUpper(type[0]))
                            throw new InvalidDataException($"unsupported chunk {Printable(type)}");
                        break;
                }
                if (seenEnd)
                    break;
            }
            if (!seenHeader || !seenEnd || idat.Length == 0)
                throw new InvalidDataException("incomplete PNG");
            if (colorType == 3 && palette == null)
                throw new InvalidDataException("missing palette");

            // decompress exactly the expected amount (stops decompression bombs)
            int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
            int bitsPerPixel = channels * bitDepth;
            long expected = 0;
            foreach ((int w, int h) in PassSizes(width, height, interlace == 1))
                expected += (long)h * (1 + ((long)w * bitsPerPixel + 7) / 8);
            if (expected > int.MaxValue)
                throw new InvalidDataException("image too large");
            byte[] raw = new byte[expected];
            idat.Position = 0;
            using (ZLibStream inflate = new(idat, CompressionMode.Decompress))
            {
                int read = 0;
                while (read < raw.Length)
                {
                    int n = inflate.Read(raw, read, raw.Length - read);
                    if (n == 0)
                        throw new InvalidDataException("image data is too short");
                    read += n;
                }
            }

            // unfilter and convert to colors
            Color[] pixels = new Color[width * height];
            int offset = 0;
            if (interlace == 0)
                offset = DecodePass(raw, offset, width, height, bitDepth, colorType, channels, palette, transparency, pixels, width, 0, 0, 1, 1);
            else
            {
                int pass = 0;
                foreach ((int w, int h) in PassSizes(width, height, true))
                {
                    (int x0, int y0, int dx, int dy) = Adam7[pass++];
                    if (w > 0 && h > 0)
                        offset = DecodePass(raw, offset, w, h, bitDepth, colorType, channels, palette, transparency, pixels, width, x0, y0, dx, dy);
                }
            }
            return new Pixels(pixels, width, height);
        }

        /// <summary>Encode pixels as a plain RGBA PNG (no metadata). Thread-safe.</summary>
        public static byte[] Encode(Pixels image)
        {
            int width = image.Width, height = image.Height, stride = width * 4;
            byte[] previous = new byte[stride], current = new byte[stride], best = new byte[stride], candidate = new byte[stride];

            using MemoryStream compressed = new();
            using (ZLibStream deflate = new(compressed, CompressionLevel.Fastest, leaveOpen: true))
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Color c = image.Data[y * width + x];
                        int i = x * 4;
                        current[i] = c.R;
                        current[i + 1] = c.G;
                        current[i + 2] = c.B;
                        current[i + 3] = c.A;
                    }

                    // pick the filter with the smallest output (the usual heuristic)
                    byte bestFilter = 0;
                    long bestScore = long.MaxValue;
                    for (byte filter = 0; filter <= 4; filter++)
                    {
                        long score = 0;
                        for (int i = 0; i < stride; i++)
                        {
                            int a = i >= 4 ? current[i - 4] : 0, b = previous[i], c = i >= 4 ? previous[i - 4] : 0;
                            byte value = (byte)(current[i] - filter switch { 1 => a, 2 => b, 3 => (a + b) / 2, 4 => Paeth(a, b, c), _ => 0 });
                            candidate[i] = value;
                            score += (sbyte)value < 0 ? -(sbyte)value : value;
                        }
                        if (score < bestScore)
                        {
                            bestScore = score;
                            bestFilter = filter;
                            (best, candidate) = (candidate, best);
                        }
                    }
                    deflate.WriteByte(bestFilter);
                    deflate.Write(best, 0, stride);
                    (previous, current) = (current, previous);
                }
            }

            using MemoryStream output = new();
            output.Write(Signature);
            byte[] header = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(header, width);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
            header[8] = 8; // bit depth
            header[9] = 6; // RGBA
            WriteChunk(output, "IHDR", header);
            WriteChunk(output, "IDAT", compressed.ToArray());
            WriteChunk(output, "IEND", Array.Empty<byte>());
            return output.ToArray();
        }


        /*********
        ** Private methods
        *********/
        /// <summary>Unfilter one (sub)image and write its pixels into the output.</summary>
        /// <returns>The offset after this pass's data.</returns>
        private static int DecodePass(byte[] raw, int offset, int w, int h, int bitDepth, int colorType, int channels, byte[]? palette, byte[]? transparency, Color[] output, int outWidth, int x0, int y0, int dx, int dy)
        {
            int bitsPerPixel = channels * bitDepth;
            int stride = (int)(((long)w * bitsPerPixel + 7) / 8);
            int bpp = Math.Max(1, bitsPerPixel / 8); // bytes per pixel for filtering
            byte[] previous = new byte[stride], line = new byte[stride];

            for (int y = 0; y < h; y++)
            {
                byte filter = raw[offset++];
                if (filter > 4)
                    throw new InvalidDataException("bad row filter");
                Buffer.BlockCopy(raw, offset, line, 0, stride);
                offset += stride;
                for (int i = 0; i < stride; i++)
                {
                    int a = i >= bpp ? line[i - bpp] : 0, b = previous[i], c = i >= bpp ? previous[i - bpp] : 0;
                    line[i] = (byte)(line[i] + filter switch { 1 => a, 2 => b, 3 => (a + b) / 2, 4 => Paeth(a, b, c), _ => 0 });
                }

                int outY = y0 + y * dy;
                for (int x = 0; x < w; x++)
                    output[outY * outWidth + x0 + x * dx] = ReadPixel(line, x, bitDepth, colorType, channels, palette, transparency);
                (previous, line) = (line, previous);
            }
            return offset;
        }

        private static Color ReadPixel(byte[] line, int x, int bitDepth, int colorType, int channels, byte[]? palette, byte[]? transparency)
        {
            // read one sample, scaled to 0-255 (palette indexes are returned as-is)
            int Sample(int index)
            {
                if (bitDepth == 8)
                    return line[x * channels + index];
                if (bitDepth == 16)
                    return line[(x * channels + index) * 2]; // high byte
                int bit = x * bitDepth;
                int value = (line[bit / 8] >> (8 - bitDepth - bit % 8)) & ((1 << bitDepth) - 1);
                return colorType == 3 ? value : value * 255 / ((1 << bitDepth) - 1);
            }

            int Raw16(int index) => (line[(x * channels + index) * 2] << 8) | line[(x * channels + index) * 2 + 1];

            switch (colorType)
            {
                case 0: // gray
                {
                    int g = Sample(0);
                    bool clear = transparency is { Length: >= 2 } && (bitDepth == 16 ? Raw16(0) : RawSmall(line, x, bitDepth)) == ((transparency[0] << 8) | transparency[1]);
                    return new Color(g, g, g, clear ? 0 : 255);
                }
                case 2: // RGB
                {
                    bool clear = transparency is { Length: >= 6 } && (bitDepth == 16
                        ? Raw16(0) == ((transparency[0] << 8) | transparency[1]) && Raw16(1) == ((transparency[2] << 8) | transparency[3]) && Raw16(2) == ((transparency[4] << 8) | transparency[5])
                        : Sample(0) == transparency[1] && Sample(1) == transparency[3] && Sample(2) == transparency[5]);
                    return new Color(Sample(0), Sample(1), Sample(2), clear ? 0 : 255);
                }
                case 3: // palette
                {
                    int index = Sample(0);
                    if (index * 3 + 2 >= palette!.Length)
                        throw new InvalidDataException("palette index out of range");
                    int alpha = transparency != null && index < transparency.Length ? transparency[index] : 255;
                    return new Color(palette[index * 3], palette[index * 3 + 1], palette[index * 3 + 2], alpha);
                }
                case 4: // gray + alpha
                    return new Color(Sample(0), Sample(0), Sample(0), Sample(1));
                default: // RGBA
                    return new Color(Sample(0), Sample(1), Sample(2), Sample(3));
            }
        }

        /// <summary>Read an unscaled 1/2/4/8-bit gray sample (for comparing with the transparent color).</summary>
        private static int RawSmall(byte[] line, int x, int bitDepth)
        {
            if (bitDepth == 8)
                return line[x];
            int bit = x * bitDepth;
            return (line[bit / 8] >> (8 - bitDepth - bit % 8)) & ((1 << bitDepth) - 1);
        }

        /// <summary>The size of each pass (one for normal images, seven for interlaced ones).</summary>
        private static (int W, int H)[] PassSizes(int width, int height, bool interlaced)
        {
            if (!interlaced)
                return new[] { (width, height) };
            (int, int)[] sizes = new (int, int)[7];
            for (int i = 0; i < 7; i++)
            {
                (int x0, int y0, int dx, int dy) = Adam7[i];
                int w = width > x0 ? (width - x0 + dx - 1) / dx : 0;
                int h = height > y0 ? (height - y0 + dy - 1) / dy : 0;
                sizes[i] = w > 0 && h > 0 ? (w, h) : (0, 0);
            }
            return sizes;
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        private static void WriteChunk(Stream output, string type, byte[] data)
        {
            Span<byte> number = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
            output.Write(number);
            byte[] typeAndData = new byte[4 + data.Length];
            Encoding.ASCII.GetBytes(type, 0, 4, typeAndData, 0);
            data.CopyTo(typeAndData, 4);
            output.Write(typeAndData);
            BinaryPrimitives.WriteUInt32BigEndian(number, Crc(typeAndData));
            output.Write(number);
        }

        private static uint Crc(ReadOnlySpan<byte> data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
                crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFF;
        }

        private static uint[] BuildCrcTable()
        {
            uint[] table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        private static string Printable(string type)
        {
            return new string(Array.ConvertAll(type.ToCharArray(), ch => char.IsLetterOrDigit(ch) ? ch : '?'));
        }
    }
}
