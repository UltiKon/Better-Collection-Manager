using System;

namespace BetterCollectionManager
{
    // Opaque compositing surface. Decorative artwork is rendered from native assets.
    internal sealed class DeckImageCanvas
    {
        internal readonly byte[] Rgba;
        internal readonly int Width, Height;
        internal readonly struct Ink
        {
            internal readonly byte R, G, B, A;
            internal Ink(byte r, byte g, byte b, byte a = 255) { R = r; G = g; B = b; A = a; }
        }
        internal static readonly Ink Gold = new Ink(218, 177, 100), Coral = new Ink(221, 126, 112);
        internal static readonly Ink Text = new Ink(237, 231, 219), Muted = new Ink(166, 182, 201), Blue = new Ink(109, 185, 220);
        private static readonly int[] PanelBevel = { 38, 86, 100, 72, 53, 91, 43, 48, 85 };
        internal DeckImageCanvas(int width, int height, bool decorated)
        {
            Width = width; Height = height; Rgba = new byte[checked(width * height * 4)];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width + x) * 4;
                    Rgba[i] = decorated ? (byte)194 : (byte)20;
                    Rgba[i + 1] = decorated ? (byte)161 : (byte)26;
                    Rgba[i + 2] = decorated ? (byte)110 : (byte)39;
                    Rgba[i + 3] = 255;
                }
        }

        internal void Blend(int x, int y, byte r, byte g, byte b, byte alpha)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height || alpha == 0) return;
            int i = (y * Width + x) * 4, inverse = 255 - alpha;
            Rgba[i] = (byte)((r * alpha + Rgba[i] * inverse + 127) / 255);
            Rgba[i + 1] = (byte)((g * alpha + Rgba[i + 1] * inverse + 127) / 255);
            Rgba[i + 2] = (byte)((b * alpha + Rgba[i + 2] * inverse + 127) / 255);
        }
        internal Ink Pixel(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) return new Ink(194, 161, 110);
            int i = (y * Width + x) * 4;
            return new Ink(Rgba[i], Rgba[i + 1], Rgba[i + 2]);
        }
        internal Ink ContrastingInk(int x, int y)
        {
            Ink paper = Pixel(x, y);
            // This threshold chooses the higher-contrast of the export's light
            // lettering and dark parchment ink for a native badge's interior.
            return paper.R * 299 + paper.G * 587 + paper.B * 114 >= 125000
                ? new Ink(53, 34, 20) : Text;
        }
        // RGB has already been composited by DeckImageCompositor. Do not apply
        // native shader alpha again: layered golden shaders can write zero
        // alpha while leaving a complete, visible RGB portrait.
        internal void RenderedPixel(int x, int y, byte r, byte g, byte b)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) return;
            int i = (y * Width + x) * 4;
            Rgba[i] = r; Rgba[i + 1] = g; Rgba[i + 2] = b; Rgba[i + 3] = 255;
        }

        internal void DarkInfoBox(Ink[] sample, int sampleWidth, int sampleHeight, int x, int y, int width, int height)
        {
            int bevel = Math.Max(5, Math.Min(width / 85, height / 7));
            int insetX = sampleWidth / 8, insetY = sampleHeight / 8;
            int spanX = sampleWidth - 2 * insetX, spanY = sampleHeight - 2 * insetY;
            for (int row = 0; row < height; row++)
            {
                int edgeY = Math.Min(row, height - 1 - row);
                int sourceRow = (insetY + row * spanY / height) * sampleWidth;
                for (int column = 0; column < width; column++)
                {
                    int edgeX = Math.Min(column, width - 1 - column);
                    int depth = Math.Min(Math.Min(edgeX, edgeY), edgeX + edgeY - bevel);
                    if (depth < 0) continue;
                    Ink texture = sample[sourceRow + insetX + column * spanX / width];
                    int shade = (texture.R * 3 + texture.G * 6 + texture.B) / 10;
                    byte gray = (byte)(depth < bevel
                        ? PanelBevel[depth * PanelBevel.Length / bevel] + (shade - 96) / 24 - (row < height / 2 ? 9 : 0)
                        : 51 + shade / 20 - (height - row) * 5 / height);
                    RenderedPixel(x + column, y + row, gray, gray, gray);
                }
            }
        }

        internal void Recess(int x, int y, int width, int height, int rim)
        {
            for (int row = 0; row < height; row++)
                for (int column = 0; column < width; column++)
                {
                    int side = Math.Min(column, width - 1 - column), end = Math.Min(row, height - 1 - row);
                    int depth = Math.Min(side, end);
                    if (side + end < rim / 2) continue;
                    if (depth < rim)
                    {
                        byte gray = (byte)(depth == 0 ? 44 : depth < (rim + 1) / 2 ? 81 : 53);
                        RenderedPixel(x + column, y + row, gray, gray, gray);
                    }
                    else
                    {
                        int shade = Math.Min(12, (depth - rim) * 3);
                        RenderedPixel(x + column, y + row, (byte)(19 + shade), (byte)(22 + shade), (byte)(25 + shade));
                    }
                }
        }

        internal void Parchment(Ink[] sample, int width, int height)
        {
            if (sample == null || sample.Length != checked(width * height) || width < 8 || height < 8)
                throw new ArgumentException("Invalid native parchment sample.");
            // Use only the interior paper. The source dialog also contains
            // metal rails, which must never be stretched across a deck image.
            int left = width / 5, bottom = height / 6;
            int spanX = width - left * 2, spanY = height - bottom * 2;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int px = x % (spanX * 2), py = y % (spanY * 2);
                    if (px >= spanX) px = spanX * 2 - px - 1;
                    if (py >= spanY) py = spanY * 2 - py - 1;
                    Ink color = sample[(bottom + py) * width + left + px];
                    RenderedPixel(x, y, color.R, color.G, color.B);
                }
        }
        internal void BlurredArtwork(Ink[] sample, int width, int height)
        {
            if (sample == null || width < 8 || height < 8 || sample.Length != checked(width * height))
                throw new ArgumentException("Invalid background artwork sample.");
            TrimArtworkPadding(ref sample, ref width, ref height);
            // Keep broad forms and colors recognizable. A radius of 10% of the
            // portrait erased the subject, especially after darkening and crop.
            // Three small separable passes soften fine details without tiling.
            byte[] colors = new byte[sample.Length * 3], scratch = new byte[colors.Length];
            for (int i = 0; i < sample.Length; i++)
            { colors[i * 3] = sample[i].R; colors[i * 3 + 1] = sample[i].G; colors[i * 3 + 2] = sample[i].B; }
            int radius = Math.Max(1, Math.Min(width, height) / 36);
            for (int pass = 0; pass < 3; pass++)
            {
                Blur(colors, scratch, width, height, radius, true);
                Blur(scratch, colors, width, height, radius, false);
            }
            Artwork(colors, width, height, true);
        }

        private static void TrimArtworkPadding(ref Ink[] sample, ref int width, ref int height)
        {
            // Some native portraits contain flat gray/black or transparent atlas
            // margins. Exclude those before blur so they cannot bleed into the art.
            Ink[] pixels = sample;
            int sourceWidth = width, sourceHeight = height;
            bool UniformLine(int start, int stride, int count)
            {
                int minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
                for (int i = 0; i < count; i++)
                {
                    Ink color = pixels[start + i * stride];
                    if (color.A < 16) continue;
                    minR = Math.Min(minR, color.R); maxR = Math.Max(maxR, color.R);
                    minG = Math.Min(minG, color.G); maxG = Math.Max(maxG, color.G);
                    minB = Math.Min(minB, color.B); maxB = Math.Max(maxB, color.B);
                    if (maxR - minR > 10 || maxG - minG > 10 || maxB - minB > 10) return false;
                }
                return true;
            }
            int left = 0, right = width - 1, bottom = 0, top = height - 1;
            while (left < sourceWidth / 5 && UniformLine(left, sourceWidth, sourceHeight)) left++;
            while (right >= sourceWidth * 4 / 5 && UniformLine(right, sourceWidth, sourceHeight)) right--;
            while (bottom < sourceHeight / 5 && UniformLine(bottom * sourceWidth + left, 1, right - left + 1)) bottom++;
            while (top >= sourceHeight * 4 / 5 && UniformLine(top * sourceWidth + left, 1, right - left + 1)) top--;
            if (left == 0 && right == width - 1 && bottom == 0 && top == height - 1) return;
            width = right - left + 1; height = top - bottom + 1;
            sample = new Ink[width * height];
            for (int y = 0; y < height; y++) Array.Copy(pixels, (bottom + y) * sourceWidth + left, sample, y * width, width);
        }

        internal void Artwork(byte[] colors, int width, int height, bool darken = false)
        {
            double scale = Math.Max(Width / (double)width, Height / (double)height);
            double offsetX = (width - Width / scale) * .5, offsetY = (height - Height / scale) * .5;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    double sx = Math.Max(0, Math.Min(width - 1, offsetX + (x + .5) / scale - .5));
                    double sy = Math.Max(0, Math.Min(height - 1, offsetY + (y + .5) / scale - .5));
                    int x0 = (int)sx, y0 = (int)sy, x1 = Math.Min(width - 1, x0 + 1), y1 = Math.Min(height - 1, y0 + 1);
                    double fx = sx - x0, fy = sy - y0;
                    double nx = (x + .5) * 2 / Width - 1, ny = (y + .5) * 2 / Height - 1;
                    double shade = darken ? .60 - .10 * (nx * nx + ny * ny) : 1;
                    int target = (y * Width + x) * 4;
                    for (int channel = 0; channel < 3; channel++)
                    {
                        double bottom = colors[(y0 * width + x0) * 3 + channel] * (1 - fx) + colors[(y0 * width + x1) * 3 + channel] * fx;
                        double top = colors[(y1 * width + x0) * 3 + channel] * (1 - fx) + colors[(y1 * width + x1) * 3 + channel] * fx;
                        Rgba[target + channel] = (byte)Math.Round((bottom * (1 - fy) + top * fy) * shade);
                    }
                    Rgba[target + 3] = 255;
                }
        }
        internal void CardShadow(byte[] coverage, int width, int height, int x, int y, int radius)
        {
            int padding = radius * 3, paddedWidth = width + padding * 2, paddedHeight = height + padding * 2;
            var shadow = new byte[paddedWidth * paddedHeight];
            var scratch = new byte[shadow.Length];
            for (int row = 0; row < height; row++)
                for (int column = 0; column < width; column++)
                    shadow[(row + padding) * paddedWidth + column + padding] = (byte)(coverage[row * width + column] / 5);
            for (int pass = 0; pass < 3; pass++)
            {
                Blur(shadow, scratch, paddedWidth, paddedHeight, radius, true, 1);
                Blur(scratch, shadow, paddedWidth, paddedHeight, radius, false, 1);
            }
            for (int row = 0; row < paddedHeight; row++)
                for (int column = 0; column < paddedWidth; column++)
                    Blend(x + column - padding + radius / 2, y + row - padding - radius, 0, 0, 0, shadow[row * paddedWidth + column]);
        }

        private static void Blur(byte[] source, byte[] destination, int width, int height, int radius, bool horizontal, int channels = 3)
        {
            int length = horizontal ? width : height, lines = horizontal ? height : width;
            int stride = horizontal ? channels : width * channels, window = radius * 2 + 1;
            for (int line = 0; line < lines; line++)
                for (int channel = 0; channel < channels; channel++)
                {
                    int start = (horizontal ? line * width * channels : line * channels) + channel, sum = 0;
                    for (int offset = -radius; offset <= radius; offset++) sum += source[start + Math.Max(0, Math.Min(length - 1, offset)) * stride];
                    for (int position = 0; position < length; position++)
                    {
                        destination[start + position * stride] = (byte)((sum + window / 2) / window);
                        sum -= source[start + Math.Max(0, position - radius) * stride];
                        sum += source[start + Math.Min(length - 1, position + radius + 1) * stride];
                    }
                }
        }

    }
}
