using System;
using Ink = BetterCollectionManager.DeckImageCanvas.Ink;

namespace BetterCollectionManager
{
    // Recover coverage from RGB on two opaque backgrounds. Native layered
    // shaders do not consistently write useful alpha, and the player's stripped
    // texture-copy path cannot seed a camera target reliably before its render.
    internal static class DeckImageCompositor
    {
        private static readonly double[] Linear = CreateLinearTable();
        private static readonly byte[] Srgb = CreateSrgbTable();

        internal static double Transmission(Ink black, Ink white, bool linear)
        {
            if (black.R == white.R && black.G == white.G && black.B == white.B) return 0;
            double r = Decode(white.R, linear) - Decode(black.R, linear);
            double g = Decode(white.G, linear) - Decode(black.G, linear);
            double b = Decode(white.B, linear) - Decode(black.B, linear);
            return Math.Max(0, Math.Min(1, r + g + b - Math.Min(r, Math.Min(g, b)) - Math.Max(r, Math.Max(g, b))));
        }

        internal static Ink OverMasked(Ink black, Ink white, Ink maskBlack, Ink maskWhite, Ink background, bool linear)
        {
            double coverage = 1 - Transmission(maskBlack, maskWhite, linear);
            if (coverage <= .0001) return background;
            double effectCoverage = 1 - Transmission(black, white, linear);
            if (effectCoverage <= .0001) return Over(maskBlack, maskWhite, background, linear);
            // Constrain the native rectangular rune-invalid glow to the
            // original card, including the card's antialiased edge pixels.
            double factor = coverage / effectCoverage;
            return new Ink(Encode(Decode(black.R, linear) * factor + (1 - coverage) * Decode(background.R, linear), linear),
                Encode(Decode(black.G, linear) * factor + (1 - coverage) * Decode(background.G, linear), linear),
                Encode(Decode(black.B, linear) * factor + (1 - coverage) * Decode(background.B, linear), linear));
        }

        internal static Ink Over(Ink black, Ink white, Ink background, bool linear)
        {
            if (black.R == white.R && black.G == white.G && black.B == white.B)
                return new Ink(black.R, black.G, black.B);
            double br = Decode(black.R, linear), bg = Decode(black.G, linear), bb = Decode(black.B, linear);
            double r = Decode(white.R, linear) - br;
            double g = Decode(white.G, linear) - bg;
            double b = Decode(white.B, linear) - bb;
            // A channel clipped by a bright native glow must not set coverage
            // for the whole pixel. Ordinary alpha blending gives equal deltas.
            double transmission = Math.Max(0, Math.Min(1, r + g + b - Math.Min(r, Math.Min(g, b)) - Math.Max(r, Math.Max(g, b))));
            if (transmission <= .0001) return new Ink(black.R, black.G, black.B);
            if (transmission >= .9999 && black.R == 0 && black.G == 0 && black.B == 0) return background;
            return new Ink(Encode(br + transmission * Decode(background.R, linear), linear),
                Encode(bg + transmission * Decode(background.G, linear), linear),
                Encode(bb + transmission * Decode(background.B, linear), linear));
        }
        // Native mesh bounds include font padding. Fit the visible light glyphs
        // instead, retaining the captured black outline and antialiasing.
        internal static void PaintCaption(DeckImageCanvas canvas, Ink[] black, Ink[] white, int sourceWidth, int sourceHeight,
            int x, int y, int width, int height, int glyphHeight, bool linear)
        {
            int left = sourceWidth, right = -1, bottom = sourceHeight, top = -1;
            for (int row = 0; row < sourceHeight; row++)
                for (int column = 0; column < sourceWidth; column++)
                {
                    Ink pixel = black[row * sourceWidth + column];
                    if (Math.Min(pixel.R, Math.Min(pixel.G, pixel.B)) < 180) continue;
                    left = Math.Min(left, column); right = Math.Max(right, column);
                    bottom = Math.Min(bottom, row); top = Math.Max(top, row);
                }
            if (right < left || top < bottom) throw new InvalidOperationException("The quantity lettering is empty.");
            double scale = Math.Min((glyphHeight - 1.0) / Math.Max(1, top - bottom), (width - 5.0) / Math.Max(1, right - left));
            double centerX = (left + right) * .5, centerY = (bottom + top) * .5;
            Ink Sample(Ink[] pixels, double sx, double sy, bool light)
            {
                int x0 = (int)Math.Floor(sx), y0 = (int)Math.Floor(sy);
                Ink At(int column, int row) => column < 0 || row < 0 || column >= sourceWidth || row >= sourceHeight
                    ? (light ? new Ink(255, 255, 255) : new Ink(0, 0, 0)) : pixels[row * sourceWidth + column];
                Ink a = At(x0, y0), b = At(x0 + 1, y0), c = At(x0, y0 + 1), d = At(x0 + 1, y0 + 1);
                double fx = sx - x0, fy = sy - y0;
                byte Mix(byte av, byte bv, byte cv, byte dv) => (byte)Math.Round(
                    (av * (1 - fx) + bv * fx) * (1 - fy) + (cv * (1 - fx) + dv * fx) * fy);
                return new Ink(Mix(a.R, b.R, c.R, d.R), Mix(a.G, b.G, c.G, d.G), Mix(a.B, b.B, c.B, d.B));
            }
            for (int row = 0; row < height; row++)
                for (int column = 0; column < width; column++)
                {
                    double sx = centerX + (column - (width - 1) * .5) / scale;
                    double sy = centerY + (row - (height - 1) * .5) / scale;
                    Ink color = Over(Sample(black, sx, sy, false), Sample(white, sx, sy, true), canvas.Pixel(x + column, y + row), linear);
                    canvas.RenderedPixel(x + column, y + row, color.R, color.G, color.B);
                }
        }
        private static double Decode(byte value, bool linear) => linear ? Linear[value] : value / 255.0;
        private static byte Encode(double value, bool linear)
        {
            value = Math.Max(0, Math.Min(1, value));
            return linear ? Srgb[(int)Math.Round(value * (Srgb.Length - 1))] : (byte)Math.Round(value * 255);
        }
        private static double[] CreateLinearTable()
        {
            var values = new double[256];
            for (int i = 0; i < values.Length; i++)
            {
                double value = i / 255.0;
                values[i] = value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
            }
            return values;
        }
        private static byte[] CreateSrgbTable()
        {
            var values = new byte[16385];
            for (int i = 0; i < values.Length; i++)
            {
                double value = (double)i / (values.Length - 1);
                values[i] = (byte)Math.Round(255 * (value <= .0031308 ? 12.92 * value : 1.055 * Math.Pow(value, 1 / 2.4) - .055));
            }
            return values;
        }
    }
}
