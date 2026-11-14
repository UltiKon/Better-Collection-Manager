using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace BetterCollectionManager
{
    internal static class DeckImageQuantityLabel
    {
        private sealed class Label
        {
            internal int Width, Height;
            internal byte[] Rgba;
        }
        private static readonly Dictionary<int, Label> Labels = new Dictionary<int, Label>();
        internal static bool Paint(DeckImageCanvas canvas, int count, int x, int y, int width, int height)
        {
            if (count < 2) return false;
            int key = count <= 9 ? count : 0;
            Label label;
            lock (Labels)
            {
                if (!Labels.TryGetValue(key, out label))
                {
                    using Stream resource = typeof(DeckImageQuantityLabel).Assembly.GetManifestResourceStream(
                        "BetterCollectionManager.DeckViewLabels.x" + (key == 0 ? 2 : key) + ".rgba.gz")
                        ?? throw new InvalidOperationException("The quantity label is missing.");
                    using var zip = new GZipStream(resource, CompressionMode.Decompress);
                    using var reader = new BinaryReader(zip);
                    int sourceWidth = reader.ReadInt32(), sourceHeight = reader.ReadInt32();
                    if (sourceWidth <= 0 || sourceHeight <= 0 || sourceWidth > 1024 || sourceHeight > 1024)
                        throw new InvalidDataException("Invalid quantity label dimensions.");
                    byte[] rgba = reader.ReadBytes(checked(sourceWidth * sourceHeight * 4));
                    if (rgba.Length != sourceWidth * sourceHeight * 4) throw new InvalidDataException("Truncated quantity label.");
                    label = new Label { Width = sourceWidth, Height = sourceHeight, Rgba = rgba };
                    if (key == 0)
                    {
                        // Replace only the baked numeral with the adjacent velvet
                        // panel pixels. The original reference border is unchanged.
                        for (int numeralRow = 65; numeralRow < 200; numeralRow++)
                            for (int numeralColumn = 195; numeralColumn < 400; numeralColumn++)
                                Array.Copy(rgba, (numeralRow * sourceWidth + 180) * 4, rgba, (numeralRow * sourceWidth + numeralColumn) * 4, 4);
                    }
                    Labels.Add(key, label);
                }
            }
            // Interpolate premultiplied colors so transparent edge pixels do not
            // introduce a dark fringe. Source assets use top-down RGBA rows.
            for (int row = 0; row < height; row++)
            {
                double sy = Math.Max(0, Math.Min(label.Height - 1, (row + .5) * label.Height / height - .5));
                int y0 = (int)sy, y1 = Math.Min(y0 + 1, label.Height - 1);
                double fy = sy - y0;
                for (int column = 0; column < width; column++)
                {
                    double sx = Math.Max(0, Math.Min(label.Width - 1, (column + .5) * label.Width / width - .5));
                    int x0 = (int)sx, x1 = Math.Min(x0 + 1, label.Width - 1);
                    double fx = sx - x0;
                    int a = (y0 * label.Width + x0) * 4, b = (y0 * label.Width + x1) * 4;
                    int c = (y1 * label.Width + x0) * 4, d = (y1 * label.Width + x1) * 4;
                    byte[] rgba = label.Rgba;
                    double wa = (1 - fx) * (1 - fy) * rgba[a + 3], wb = fx * (1 - fy) * rgba[b + 3];
                    double wc = (1 - fx) * fy * rgba[c + 3], wd = fx * fy * rgba[d + 3];
                    double alpha = wa + wb + wc + wd;
                    if (alpha < .5) continue;
                    byte Channel(int offset) => (byte)Math.Round((rgba[a + offset] * wa + rgba[b + offset] * wb
                        + rgba[c + offset] * wc + rgba[d + offset] * wd) / alpha);
                    canvas.Blend(x + column, y + height - 1 - row, Channel(0), Channel(1), Channel(2), (byte)Math.Round(alpha));
                }
            }
            return true;
        }
    }
}
