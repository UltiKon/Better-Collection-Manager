using System;
using System.Collections.Generic;
using UnityEngine;
using Ink = BetterCollectionManager.DeckImageCanvas.Ink;

namespace BetterCollectionManager
{
    // Native meshes include transparent geometry that differs by card type.
    // Normalize the visible lower frame before measuring the shared footprint.
    internal sealed class DeckImageCardFrame
    {
        private readonly DeckImageCapture.Pair m_colors, m_mask;
        private readonly bool m_linear;
        private readonly bool[] m_maskOpaque, m_colorOpaque;
        internal readonly double Left, Bottom;
        internal readonly int Width, Height, BodyWidth;
        private readonly double m_bodyHeight, m_referenceScale;

        internal DeckImageCardFrame(DeckImageCapture.Pair colors, DeckImageCapture.Pair mask, int width, int height,
            int bodyWidth, bool linear)
        {
            int left = width, right = -1, bottom = height, top = -1;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    if (Coverage(mask.Black[i], mask.White[i], linear) < .04) continue;
                    left = Math.Min(left, x); right = Math.Max(right, x);
                    bottom = Math.Min(bottom, y); top = Math.Max(top, y);
                }
            if (right < left || top < bottom) throw new InvalidOperationException("The native card renderer produced an empty image.");
            Width = right - left + 1; Height = top - bottom + 1;
            // The straight sides around the lower text area give a consistent
            // reference for spells, minions and weapons. Exclude top ornaments,
            // bottom stat medallions and faint shadows from this measurement.
            var leftEdges = new List<int>(); var rightEdges = new List<int>();
            for (int y = bottom + Height / 5; y < bottom + Height / 2; y++)
            {
                int rowLeft = width, rowRight = -1;
                for (int x = left; x <= right; x++)
                    if (Coverage(mask.Black[y * width + x], mask.White[y * width + x], linear) >= .6)
                    { rowLeft = Math.Min(rowLeft, x); rowRight = Math.Max(rowRight, x); }
                if (rowRight > rowLeft) { leftEdges.Add(rowLeft); rightEdges.Add(rowRight); }
            }
            if (leftEdges.Count == 0) throw new InvalidOperationException("The native card has no visible body frame.");
            leftEdges.Sort(); rightEdges.Sort();
            int bodyLeft = leftEdges[leftEdges.Count / 2], bodyRight = rightEdges[rightEdges.Count / 2];
            int bodyBottom = top;
            for (int y = bottom; y <= top; y++)
            {
                int solid = 0;
                for (int x = bodyLeft; x <= bodyRight; x++)
                    if (Coverage(mask.Black[y * width + x], mask.White[y * width + x], linear) >= .6) solid++;
                if (solid >= (bodyRight - bodyLeft + 1) * .4) { bodyBottom = y; break; }
            }
            m_referenceScale = bodyWidth / (double)(bodyRight - bodyLeft + 1);
            Left = (left - (bodyLeft + bodyRight + 1) / 2.0) * m_referenceScale;
            Bottom = (bottom - bodyBottom) * m_referenceScale;
            BodyWidth = bodyWidth;
            // This rectangle anchors the quantity label. Its top must not move
            // when a dragon extends beyond the card body.
            m_bodyHeight = bodyWidth * (double)DeckImageLayout.CardHeight / DeckImageLayout.CardWidth;
            m_linear = linear;
            Color32[] Crop(Color32[] pixels)
            {
                var result = new Color32[Width * Height];
                for (int y = 0; y < Height; y++)
                    Array.Copy(pixels, (bottom + y) * width + left, result, y * Width, Width);
                return result;
            }
            m_colors = new DeckImageCapture.Pair(Crop(colors.Black), Crop(colors.White));
            m_mask = ReferenceEquals(mask.Black, colors.Black) ? m_colors
                : new DeckImageCapture.Pair(Crop(mask.Black), Crop(mask.White));
            m_maskOpaque = OpaquePixels(m_mask);
            m_colorOpaque = ReferenceEquals(m_mask.Black, m_colors.Black) ? m_maskOpaque : OpaquePixels(m_colors);
        }
        private static bool[] OpaquePixels(DeckImageCapture.Pair pair)
        {
            var opaque = new bool[pair.Black.Length];
            for (int i = 0; i < opaque.Length; i++)
            {
                Color32 black = pair.Black[i], white = pair.White[i];
                opaque[i] = black.r == white.r && black.g == white.g && black.b == white.b;
            }
            return opaque;
        }
        private bool OpaqueSample(bool[] opaque, SampleAxis x, SampleAxis y)
            => opaque[y.First * Width + x.First] && opaque[y.First * Width + x.Second]
                && opaque[y.Second * Width + x.First] && opaque[y.Second * Width + x.Second];

        internal static (int Left, int Bottom, int Width, int Height) Footprint(IReadOnlyList<DeckImageCardFrame> cards)
        {
            int left = int.MaxValue, bottom = int.MaxValue, right = int.MinValue, top = int.MinValue;
            foreach (DeckImageCardFrame card in cards)
            {
                left = Math.Min(left, (int)Math.Floor(card.Left)); bottom = Math.Min(bottom, (int)Math.Floor(card.Bottom));
                right = Math.Max(right, (int)Math.Ceiling(card.Left + card.Width * card.m_referenceScale));
                top = Math.Max(top, (int)Math.Ceiling(card.Bottom + card.Height * card.m_referenceScale));
            }
            // The soft halo may extend into the inter-card gap. Reserving a
            // broad shadow margin in every tile unnecessarily shrinks the deck.
            int padding = Math.Max(2, cards[0].BodyWidth / 96);
            return (left - padding, bottom - padding, right - left + padding * 2, top - bottom + padding * 2);
        }

        private DeckImageLayout.Cell FittedCell(DeckImageLayout.Cell tile, out (int Left, int Bottom, int Width, int Height) footprint)
        {
            footprint = Footprint(new[] { this });
            int top = footprint.Bottom + footprint.Height;
            int bottom = Math.Min(footprint.Bottom, -Math.Max(1, (int)Math.Round(24.0 * BodyWidth / DeckImageLayout.ExportCardWidth)));
            footprint = (footprint.Left, bottom, footprint.Width, top - bottom);
            // BodyWidth is already the chosen capture resolution; retain that size when the tile has spare ornament room.
            double scale = Math.Min(1, Math.Min(tile.Width / (double)footprint.Width, tile.Height / (double)footprint.Height));
            int width = Math.Max(1, (int)Math.Floor(footprint.Width * scale));
            int height = Math.Max(1, (int)Math.Floor(footprint.Height * scale));
            return new DeckImageLayout.Cell(tile.X + (tile.Width - width) / 2,
                tile.Y + tile.Height - height, width, height, tile.Caption);
        }

        internal DeckImageLayout.Cell MeasureFitted(DeckImageCanvas canvas, DeckImageLayout.Cell tile)
        {
            var cell = FittedCell(tile, out var footprint);
            return BodyCell(canvas, cell, footprint);
        }
        internal DeckImageLayout.Cell PaintFitted(DeckImageCanvas canvas, DeckImageLayout.Cell tile, Action<DeckImageLayout.Cell> beforePaint = null)
        {
            var cell = FittedCell(tile, out var footprint);
            return Paint(canvas, cell, footprint, beforePaint);
        }
        private DeckImageLayout.Cell BodyCell(DeckImageCanvas canvas, DeckImageLayout.Cell cell,
            (int Left, int Bottom, int Width, int Height) footprint)
        {
            double scale = Math.Min(cell.Width / (double)footprint.Width, cell.Height / (double)footprint.Height);
            int baseline = canvas.Height - cell.Y - cell.Height;
            int bodyBottom = baseline + (int)Math.Round(-footprint.Bottom * scale);
            int bodyHeight = (int)Math.Round(m_bodyHeight * scale);
            return new DeckImageLayout.Cell(cell.X + (int)Math.Round((-BodyWidth / 2.0 - footprint.Left) * scale),
                canvas.Height - bodyBottom - bodyHeight, (int)Math.Round(BodyWidth * scale), bodyHeight, cell.Caption);
        }

        internal DeckImageLayout.Cell Paint(DeckImageCanvas canvas, DeckImageLayout.Cell cell,
            (int Left, int Bottom, int Width, int Height) footprint, Action<DeckImageLayout.Cell> beforePaint = null)
        {
            double scale = Math.Min(cell.Width / (double)footprint.Width, cell.Height / (double)footprint.Height);
            int x = cell.X + (int)Math.Round((Left - footprint.Left) * scale);
            int baseline = canvas.Height - cell.Y - cell.Height;
            int y = baseline + (int)Math.Round((Bottom - footprint.Bottom) * scale);
            int width = Math.Max(1, (int)Math.Round(Width * m_referenceScale * scale));
            int height = Math.Max(1, (int)Math.Round(Height * m_referenceScale * scale));
            var body = BodyCell(canvas, cell, footprint);
            beforePaint?.Invoke(body);
            var coverage = new byte[width * height];
            var sampledBlack = new Color32[width * height];
            var sampledWhite = new Color32[width * height];
            var columns = Samples(Width, width);
            var rows = Samples(Height, height);
            bool sharedMask = ReferenceEquals(m_mask.Black, m_colors.Black);
            for (int row = 0; row < height; row++)
                for (int column = 0; column < width; column++)
                {
                    int index = row * width + column;
                    Color32 maskBlack = Sample(m_mask.Black, columns[column], rows[row]);
                    bool opaque = OpaqueSample(m_maskOpaque, columns[column], rows[row]);
                    Color32 maskWhite = opaque ? maskBlack : Sample(m_mask.White, columns[column], rows[row]);
                    sampledBlack[index] = maskBlack; sampledWhite[index] = maskWhite;
                    coverage[index] = opaque ? (byte)255 : (byte)Math.Round(255 * Coverage(maskBlack, maskWhite, m_linear));
                }
            int radius = Math.Max(2, (int)Math.Round(BodyWidth * scale / 70));
            canvas.CardShadow(coverage, width, height, x, y, radius);
            for (int row = 0; row < height; row++)
                for (int column = 0; column < width; column++)
                {
                    int index = row * width + column;
                    if (coverage[index] == 0 && Coverage(sampledBlack[index], sampledWhite[index], m_linear) <= .0001) continue;
                    Ink black = Color(sharedMask ? sampledBlack[index] : Sample(m_colors.Black, columns[column], rows[row]));
                    Ink white = sharedMask ? Color(sampledWhite[index])
                        : OpaqueSample(m_colorOpaque, columns[column], rows[row]) ? black
                        : Color(Sample(m_colors.White, columns[column], rows[row]));
                    Ink behind = canvas.Pixel(x + column, y + row);
                    Ink color = sharedMask
                        ? DeckImageCompositor.Over(black, white, behind, m_linear)
                        : DeckImageCompositor.OverMasked(black, white, Color(sampledBlack[index]),
                            Color(sampledWhite[index]), behind, m_linear);
                    canvas.RenderedPixel(x + column, y + row, color.R, color.G, color.B);
                }
            return body;
        }

        private static Ink Color(Color32 value) => new Ink(value.r, value.g, value.b);
        private static double Coverage(Color32 black, Color32 white, bool linear)
            => 1 - DeckImageCompositor.Transmission(Color(black), Color(white), linear);

        private readonly struct SampleAxis
        {
            internal readonly int First, Second;
            internal readonly double Fraction;
            internal SampleAxis(int first, int second, double fraction)
            { First = first; Second = second; Fraction = fraction; }
        }
        private static SampleAxis[] Samples(int source, int destination)
        {
            var axis = new SampleAxis[destination];
            for (int i = 0; i < destination; i++)
            {
                double position = Math.Max(0, Math.Min(source - 1, (i + .5) * source / destination - .5));
                int first = (int)position;
                axis[i] = new SampleAxis(first, Math.Min(first + 1, source - 1), position - first);
            }
            return axis;
        }
        private Color32 Sample(Color32[] pixels, SampleAxis x, SampleAxis y)
        {
            Color32 a = pixels[y.First * Width + x.First];
            if (x.Fraction == 0 && y.Fraction == 0) return a;
            Color32 b = pixels[y.First * Width + x.Second];
            Color32 c = pixels[y.Second * Width + x.First], d = pixels[y.Second * Width + x.Second];
            double fx = x.Fraction, fy = y.Fraction;
            byte Mix(byte aa, byte bb, byte cc, byte dd) =>
                (byte)Math.Round((aa * (1 - fx) + bb * fx) * (1 - fy) + (cc * (1 - fx) + dd * fx) * fy);
            return new Color32(Mix(a.r, b.r, c.r, d.r), Mix(a.g, b.g, c.g, d.g), Mix(a.b, b.b, c.b, d.b), 255);
        }
    }
}
