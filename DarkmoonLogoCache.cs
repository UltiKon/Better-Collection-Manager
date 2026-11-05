using System;
using System.IO;

namespace BetterCollectionManager
{
    // This client strips ImageConversion.LoadImage. Keep the exported PNG for
    // inspection and a bounded RGBA32 copy for the static native texture.
    internal sealed class DarkmoonLogoPixels
    {
        internal readonly int Width, Height;
        internal readonly byte[] Rgba;
        internal DarkmoonLogoPixels(int width, int height, byte[] rgba)
        { Width = width; Height = height; Rgba = rgba; }
    }

    internal static class DarkmoonLogoCache
    {
        private const int Magic = 0x31544644; // DFT1, little endian
        private static int PixelBytes(int width, int height)
        {
            if (width <= 0 || height <= 0 || width > 2048 || height > 2048)
                throw new InvalidDataException("DFT logo dimensions must be between 1 and 2048");
            return width * height * 4;
        }

        internal static byte[] Encode(int width, int height, byte[] rgba)
        {
            if (rgba == null || rgba.Length != PixelBytes(width, height))
                throw new InvalidDataException("DFT logo RGBA length does not match its dimensions");
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic); writer.Write(width); writer.Write(height); writer.Write(rgba);
                return stream.ToArray();
            }
        }

        internal static DarkmoonLogoPixels Decode(byte[] data)
        {
            if (data == null || data.Length < 12) throw new InvalidDataException("DFT logo cache is truncated");
            using (var stream = new MemoryStream(data, false))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadInt32() != Magic) throw new InvalidDataException("Unknown DFT logo cache version");
                int width = reader.ReadInt32(), height = reader.ReadInt32(), count = PixelBytes(width, height);
                if (data.Length != 12 + count) throw new InvalidDataException("DFT logo cache size mismatch");
                return new DarkmoonLogoPixels(width, height, reader.ReadBytes(count));
            }
        }

        internal static DarkmoonLogoPixels Read(string path)
        {
            if (new FileInfo(path).Length > 12L + 2048L * 2048L * 4L)
                throw new InvalidDataException("DFT logo cache exceeds the supported size");
            return Decode(File.ReadAllBytes(path));
        }

        internal static void WriteAtomic(string path, byte[] data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, data);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
