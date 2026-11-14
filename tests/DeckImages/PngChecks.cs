using System.IO.Compression;
using BetterCollectionManager;

internal static class PngChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var canvas = new DeckImageCanvas(3, 2, false);
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 3; x++)
                canvas.RenderedPixel(x, y, (byte)(x * 90), (byte)(y * 150), (byte)(x + y * 10));
        byte[] png = DeckImagePng.Encode(canvas);
        check(png.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "Managed encoding writes the PNG signature.");
        int ReadInt(int at) => (png[at] << 24) | (png[at + 1] << 16) | (png[at + 2] << 8) | png[at + 3];
        using var idat = new MemoryStream();
        for (int at = 8; at < png.Length;)
        {
            int length = ReadInt(at);
            string type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            uint crc = 0xffffffff;
            for (int i = at + 4; i < at + 8 + length; i++)
            {
                crc ^= png[i];
                for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1;
            }
            check(unchecked((uint)ReadInt(at + 8 + length)) == (crc ^ 0xffffffff), "Every PNG chunk has a valid CRC.");
            if (type == "IHDR") check(ReadInt(at + 8) == 3 && ReadInt(at + 12) == 2 && png[at + 17] == 2, "PNG declares RGB at the canvas dimensions.");
            if (type == "IDAT") idat.Write(png, at + 8, length);
            at += length + 12;
        }
        idat.Position = 0;
        using var zlib = new ZLibStream(idat, CompressionMode.Decompress);
        using var decoded = new MemoryStream(); zlib.CopyTo(decoded);
        byte[] rows = decoded.ToArray();
        check(rows.Length == 20, "A standard zlib decoder validates and expands the encoded image.");
        for (int row = 0; row < 2; row++)
        {
            int start = row * 10;
            check(rows[start] == 1, "RGB rows use the Sub filter.");
            for (int i = 3; i < 9; i++) rows[start + 1 + i] = unchecked((byte)(rows[start + 1 + i] + rows[start + 1 + i - 3]));
            for (int x = 0; x < 3; x++)
            {
                var expected = canvas.Pixel(x, 1 - row);
                check(rows[start + 1 + x * 3] == expected.R && rows[start + 2 + x * 3] == expected.G
                    && rows[start + 3 + x * 3] == expected.B, "PNG preserves colors and the correct top/bottom orientation.");
            }
        }
    }
}
