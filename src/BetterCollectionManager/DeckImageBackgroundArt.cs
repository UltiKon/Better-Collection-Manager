using System;
using System.IO;
using System.IO.Compression;

namespace BetterCollectionManager
{
    internal static class DeckImageBackgroundArt
    {
        internal static void Paint(DeckImageCanvas canvas, TAG_CLASS deckClass)
        {
            const string prefix = "BetterCollectionManager.DeckViewBackgrounds.";
            var assembly = typeof(DeckImageBackgroundArt).Assembly;
            using Stream resource = assembly.GetManifestResourceStream(prefix + (int)deckClass + ".rgb.gz")
                ?? assembly.GetManifestResourceStream(prefix + "0.rgb.gz")
                ?? throw new InvalidOperationException("Class background artwork is missing.");
            using var zip = new GZipStream(resource, CompressionMode.Decompress);
            using var reader = new BinaryReader(zip);
            int width = reader.ReadInt32(), height = reader.ReadInt32();
            if (width <= 0 || height <= 0 || width > 1024 || height > 1024)
                throw new InvalidDataException("Class background dimensions are invalid.");
            int count = width * height * 3;
            byte[] colors = reader.ReadBytes(count);
            if (colors.Length != count) throw new InvalidDataException("Class background artwork is truncated.");
            canvas.Artwork(colors, width, height);
        }
    }
}
