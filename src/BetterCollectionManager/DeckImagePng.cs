using System;
using System.IO;
using System.IO.Compression;

namespace BetterCollectionManager
{
    // Pure managed encoding: no Texture2D allocation/upload or native PNG call.
    // Canvas rows are bottom-up RGBA; PNG rows are top-down opaque RGB.
    internal static class DeckImagePng
    {
        private static readonly uint[] CrcTable = MakeCrcTable();
        internal static byte[] Encode(DeckImageCanvas canvas)
        {
            using var compressed = new MemoryStream();
            compressed.WriteByte(0x78); compressed.WriteByte(0x01); // zlib, fastest compression
            uint adlerA = 1, adlerB = 0;
            byte[] row = new byte[checked(canvas.Width * 3 + 1)];
            using (var deflate = new DeflateStream(compressed, CompressionLevel.Fastest, true))
                for (int y = canvas.Height - 1; y >= 0; y--)
                {
                    row[0] = 1; // PNG Sub filter, three bytes per pixel
                    int source = y * canvas.Width * 4;
                    for (int x = 0; x < canvas.Width; x++, source += 4)
                        for (int channel = 0; channel < 3; channel++)
                        {
                            int previous = x == 0 ? 0 : canvas.Rgba[source - 4 + channel];
                            row[1 + x * 3 + channel] = unchecked((byte)(canvas.Rgba[source + channel] - previous));
                        }
                    deflate.Write(row, 0, row.Length);
                    for (int start = 0; start < row.Length; start += 5552)
                    {
                        int end = Math.Min(row.Length, start + 5552);
                        for (int i = start; i < end; i++) { adlerA += row[i]; adlerB += adlerA; }
                        adlerA %= 65521; adlerB %= 65521;
                    }
                }
            UInt32(compressed, adlerB << 16 | adlerA);
            using var png = new MemoryStream();
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            png.Write(signature, 0, signature.Length);
            using var header = new MemoryStream();
            UInt32(header, (uint)canvas.Width); UInt32(header, (uint)canvas.Height);
            header.WriteByte(8); header.WriteByte(2); // 8-bit RGB
            header.WriteByte(0); header.WriteByte(0); header.WriteByte(0);
            Chunk(png, "IHDR", header.ToArray());
            Chunk(png, "IDAT", compressed.ToArray());
            Chunk(png, "IEND", new byte[0]);
            return png.ToArray();
        }
        private static void Chunk(Stream stream, string type, byte[] data)
        {
            UInt32(stream, (uint)data.Length);
            uint crc = 0xffffffff;
            foreach (char letter in type)
            {
                byte value = (byte)letter;
                stream.WriteByte(value);
                crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
            }
            stream.Write(data, 0, data.Length);
            foreach (byte value in data) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
            UInt32(stream, crc ^ 0xffffffff);
        }
        private static void UInt32(Stream stream, uint value)
        {
            stream.WriteByte((byte)(value >> 24)); stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8)); stream.WriteByte((byte)value);
        }
        private static uint[] MakeCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                uint value = i;
                for (int bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xedb88320 ^ (value >> 1) : value >> 1;
                table[i] = value;
            }
            return table;
        }
    }
}
