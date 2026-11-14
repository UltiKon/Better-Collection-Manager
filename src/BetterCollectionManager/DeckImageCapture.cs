using System;
using UnityEngine;
using Ink = BetterCollectionManager.DeckImageCanvas.Ink;

namespace BetterCollectionManager
{
    internal static class DeckImageCapture
    {
        internal readonly struct Pair
        {
            internal readonly Color32[] Black, White;
            internal Pair(Color32[] black, Color32[] white) { Black = black; White = white; }
        }
        internal static int CaptionVerticalShift(Color32[] black, int width, int height)
        {
            // Native font mesh bounds include uneven padding and shadow space.
            // Apply half the geometric correction for the native font's optical
            // baseline, keeping the lettering's font size and X edge.
            int bottom = height, top = -1;
            for (int row = 0; row < height; row++)
                for (int column = 0; column < width; column++)
                {
                    Color32 pixel = black[row * width + column];
                    if (Math.Min(pixel.r, Math.Min(pixel.g, pixel.b)) < 180) continue;
                    bottom = Math.Min(bottom, row); top = Math.Max(top, row);
                }
            return top < bottom ? 0 : (int)Math.Round((height - 1 - bottom - top) * .25);
        }
        internal static bool PaintSymbol(Pair pair, int sourceWidth, int sourceHeight, DeckImageCanvas canvas,
            int x, int y, int width, int height, bool linear, bool leftAligned = false)
        {
            Ink Color(Color32 value) => new Ink(value.r, value.g, value.b);
            int left = sourceWidth, right = -1, bottom = sourceHeight, top = -1;
            for (int row = 0; row < sourceHeight; row++)
                for (int column = 0; column < sourceWidth; column++)
                {
                    int i = row * sourceWidth + column;
                    if (DeckImageCompositor.Transmission(Color(pair.Black[i]), Color(pair.White[i]), linear) > .94) continue;
                    left = Math.Min(left, column); right = Math.Max(right, column);
                    bottom = Math.Min(bottom, row); top = Math.Max(top, row);
                }
            if (right < left || top < bottom) return false;
            int cropWidth = right - left + 1, cropHeight = top - bottom + 1;
            double scale = Math.Min(width / (double)cropWidth, height / (double)cropHeight);
            int paintedWidth = Math.Max(1, (int)Math.Round(cropWidth * scale));
            int paintedHeight = Math.Max(1, (int)Math.Round(cropHeight * scale));
            if (!leftAligned) x += (width - paintedWidth) / 2;
            y += (height - paintedHeight) / 2;
            Ink Sample(Color32[] pixels, double sx, double sy)
            {
                sx = Math.Max(left, Math.Min(right, sx)); sy = Math.Max(bottom, Math.Min(top, sy));
                int x0 = (int)sx, y0 = (int)sy, x1 = Math.Min(right, x0 + 1), y1 = Math.Min(top, y0 + 1);
                Color32 a = pixels[y0 * sourceWidth + x0], b = pixels[y0 * sourceWidth + x1];
                Color32 c = pixels[y1 * sourceWidth + x0], d = pixels[y1 * sourceWidth + x1];
                double fx = sx - x0, fy = sy - y0;
                byte Mix(byte aa, byte bb, byte cc, byte dd) =>
                    (byte)Math.Round((aa * (1 - fx) + bb * fx) * (1 - fy) + (cc * (1 - fx) + dd * fx) * fy);
                return new Ink(Mix(a.r, b.r, c.r, d.r), Mix(a.g, b.g, c.g, d.g), Mix(a.b, b.b, c.b, d.b));
            }
            for (int row = 0; row < paintedHeight; row++)
                for (int column = 0; column < paintedWidth; column++)
                {
                    double sx = left + (column + .5) * cropWidth / paintedWidth - .5;
                    double sy = bottom + (row + .5) * cropHeight / paintedHeight - .5;
                    Ink color = DeckImageCompositor.Over(Sample(pair.Black, sx, sy), Sample(pair.White, sx, sy),
                        canvas.Pixel(x + column, y + row), linear);
                    canvas.RenderedPixel(x + column, y + row, color.R, color.G, color.B);
                }
            return true;
        }
        internal static bool PaintRgbaSymbol(Color32[] rgba, int sourceWidth, int sourceHeight, DeckImageCanvas canvas,
            int x, int y, int maxWidth, int height, bool leftAligned = false)
        {
            int left = sourceWidth, right = -1, bottom = sourceHeight, top = -1;
            for (int row = 0; row < sourceHeight; row++)
                for (int column = 0; column < sourceWidth; column++)
                    if (rgba[row * sourceWidth + column].a >= 16)
                    {
                        left = Math.Min(left, column); right = Math.Max(right, column);
                        bottom = Math.Min(bottom, row); top = Math.Max(top, row);
                    }
            if (right < left || top < bottom) return false;
            int cropWidth = right - left + 1, cropHeight = top - bottom + 1;
            double scale = Math.Min(maxWidth / (double)cropWidth, height / (double)cropHeight);
            int width = Math.Max(1, (int)Math.Round(cropWidth * scale));
            int paintedHeight = Math.Max(1, (int)Math.Round(cropHeight * scale));
            if (!leftAligned) x += (maxWidth - width) / 2;
            y += (height - paintedHeight) / 2;
            for (int row = 0; row < paintedHeight; row++)
            {
                double sy = Math.Max(bottom, Math.Min(top, bottom + (row + .5) * cropHeight / paintedHeight - .5));
                int y0 = (int)sy, y1 = Math.Min(top, y0 + 1);
                double fy = sy - y0;
                for (int column = 0; column < width; column++)
                {
                    double sx = Math.Max(left, Math.Min(right, left + (column + .5) * cropWidth / width - .5));
                    int x0 = (int)sx, x1 = Math.Min(right, x0 + 1);
                    double fx = sx - x0;
                    Color32 a = rgba[y0 * sourceWidth + x0], b = rgba[y0 * sourceWidth + x1];
                    Color32 c = rgba[y1 * sourceWidth + x0], d = rgba[y1 * sourceWidth + x1];
                    double wa = (1 - fx) * (1 - fy) * a.a, wb = fx * (1 - fy) * b.a;
                    double wc = (1 - fx) * fy * c.a, wd = fx * fy * d.a;
                    double alpha = wa + wb + wc + wd;
                    if (alpha < .5) continue;
                    byte Channel(byte aa, byte bb, byte cc, byte dd)
                        => (byte)Math.Round((aa * wa + bb * wb + cc * wc + dd * wd) / alpha);
                    canvas.Blend(x + column, y + row, Channel(a.r,b.r,c.r,d.r), Channel(a.g,b.g,c.g,d.g),
                        Channel(a.b,b.b,c.b,d.b), (byte)Math.Round(alpha));
                }
            }
            return true;
        }

        internal static Color32[] ReadArtwork(Texture source, RenderTexture target, Texture2D pixels, int width, int height)
            => ReadTexture(source, target, pixels, width, height, new Vector2(1, 1), new Vector2(0, 0));

        internal static Color32[] ReadTexture(Texture source, RenderTexture target, Texture2D pixels, int width, int height,
            Vector2 scale, Vector2 offset)
        {
            RenderTexture previous = RenderTexture.active;
            try
            {
                // The stripped player rejects a null Material in Blit. Its
                // scale/offset overload uses the native texture-copy path.
                Graphics.Blit(source, target, scale, offset);
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                return pixels.GetPixels32();
            }
            finally { RenderTexture.active = previous; }
        }
        internal static Pair Read(Camera camera, Texture2D pixels, int width, int height)
        {
            CameraClearFlags clear = camera.clearFlags;
            Color background = camera.backgroundColor;
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.Render();
                RenderTexture.active = camera.targetTexture;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                Color32[] black = pixels.GetPixels32();
                camera.backgroundColor = Color.white;
                camera.Render();
                RenderTexture.active = camera.targetTexture;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                return new Pair(black, pixels.GetPixels32());
            }
            finally
            {
                camera.clearFlags = clear; camera.backgroundColor = background;
                RenderTexture.active = previous;
            }
        }

        internal static bool PaintQuantityFrame(Pair frame, int sourceWidth, int sourceHeight, DeckImageCanvas canvas,
            int x, int y, int width, int height, bool linear)
        {
            Ink Black(int index) => new Ink(frame.Black[index].r, frame.Black[index].g, frame.Black[index].b);
            Ink White(int index) => new Ink(frame.White[index].r, frame.White[index].g, frame.White[index].b);
            int RowShade(int row)
            {
                int total = 0, count = 0, darkest = 255, brightest = 0;
                for (int quarter = 1; quarter <= 3; quarter++)
                {
                    int index = row * sourceWidth + sourceWidth * quarter / 4;
                    Ink ink = Black(index);
                    if (DeckImageCompositor.Transmission(ink, White(index), linear) > .25) continue;
                    int shade = (ink.R + ink.G + ink.B) / 3;
                    total += shade;
                    darkest = Math.Min(darkest, shade);
                    brightest = Math.Max(brightest, shade);
                    count++;
                }
                return count == 3 ? total - darkest - brightest : count == 2 ? total / count : -1;
            }

            int centerShade = RowShade(sourceHeight * 7 / 10);
            int sourceEdge = sourceHeight * 3 / 20;
            bool HasRim(int start, int end)
            {
                for (int row = start; row < end; row++)
                    if (RowShade(row) >= centerShade + 12) return true;
                return false;
            }
            if (centerShade < 30 || centerShade > 130 || !HasRim(0, sourceEdge) || !HasRim(sourceHeight - sourceEdge, sourceHeight))
                return false;
            int edge = Math.Min(width / 85, height / 4);
            for (int row = 0; row < height; row++)
            {
                int sourceY = FrameCoordinate(row, sourceHeight, height, sourceEdge, edge);
                for (int column = 0; column < width; column++)
                {
                    int sourceX = FrameCoordinate(column, sourceWidth, width, sourceEdge, edge);
                    // The region above the input contains uninterrupted slate.
                    // Use it to fill the entire interior under the deck stats.
                    int textureY = column >= edge && column < width - edge && row >= edge && row < height - edge
                        ? sourceHeight * 3 / 5 + (row - edge) * (sourceHeight / 5) / (height - 2 * edge) : sourceY;
                    int index = textureY * sourceWidth + sourceX;
                    Ink color = DeckImageCompositor.Over(Black(index), White(index), canvas.Pixel(x + column, y + row), linear);
                    canvas.RenderedPixel(x + column, y + row, color.R, color.G, color.B);
                }
            }
            return true;
        }

        internal static bool PaintWildFrame(Pair frame, int sourceWidth, int sourceHeight, DeckImageCanvas canvas,
            int x, int y, int width, int height, int cornerHeight, int inset, int overhang, Rect originalPanel, bool linear)
        {
            Ink Black(int i) => new Ink(frame.Black[i].r, frame.Black[i].g, frame.Black[i].b);
            Ink White(int i) => new Ink(frame.White[i].r, frame.White[i].g, frame.White[i].b);
            if (originalPanel.width <= 0 || originalPanel.height <= 0)
                throw new ArgumentException("Native vine panel reference is empty.");
            int count = sourceWidth * sourceHeight;
            var visible = new bool[count];
            int left = sourceWidth, right = -1, bottom = sourceHeight, top = -1;
            for (int row = 0; row < sourceHeight; row++) for (int column = 0; column < sourceWidth; column++)
            {
                int index = row * sourceWidth + column;
                if (DeckImageCompositor.Transmission(Black(index), White(index), linear) > .94) continue;
                visible[index] = true;
                left = Math.Min(left, column); right = Math.Max(right, column);
                bottom = Math.Min(bottom, row); top = Math.Max(top, row);
            }
            if (right < left || top < bottom) return false;

            // Matchmaking vines are separate organic silhouettes, not a tiled
            // frame. Assign each connected silhouette to its nearest corner;
            // this keeps long curls intact even when they cross the midpoint.
            var labels = new int[count];
            var queue = new int[count];
            var minX = new[] { sourceWidth, sourceWidth, sourceWidth, sourceWidth };
            var minY = new[] { sourceHeight, sourceHeight, sourceHeight, sourceHeight };
            var maxX = new[] { -1, -1, -1, -1 };
            var maxY = new[] { -1, -1, -1, -1 };
            for (int seed = 0; seed < count; seed++)
            {
                if (!visible[seed] || labels[seed] != 0) continue;
                int head = 0, tail = 1;
                long sumX = 0, sumY = 0;
                queue[0] = seed; labels[seed] = -1;
                while (head < tail)
                {
                    int index = queue[head++], sx = index % sourceWidth, sy = index / sourceWidth;
                    sumX += sx; sumY += sy;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = sx + dx, ny = sy + dy;
                        if (nx < 0 || nx >= sourceWidth || ny < 0 || ny >= sourceHeight) continue;
                        int next = ny * sourceWidth + nx;
                        if (!visible[next] || labels[next] != 0) continue;
                        labels[next] = -1; queue[tail++] = next;
                    }
                }
                int corner = (sumX * 2 >= originalPanel.center.x * 2 * tail ? 1 : 0)
                    + (sumY * 2 >= originalPanel.center.y * 2 * tail ? 2 : 0);
                for (int i = 0; i < tail; i++)
                {
                    int index = queue[i], sx = index % sourceWidth, sy = index / sourceWidth;
                    labels[index] = corner + 1;
                    minX[corner] = Math.Min(minX[corner], sx); maxX[corner] = Math.Max(maxX[corner], sx);
                    minY[corner] = Math.Min(minY[corner], sy); maxY[corner] = Math.Max(maxY[corner], sy);
                }
            }

            // One scale retains the artist's size relationships: the large
            // upper-right branch remains larger than the upper-left curl.
            int tallest = 1;
            for (int corner = 0; corner < 4; corner++)
                if (maxX[corner] >= minX[corner]) tallest = Math.Max(tallest, maxY[corner] - minY[corner] + 1);
            double scale = Math.Min(cornerHeight / (double)tallest, height / (double)originalPanel.height);
            void FitOutside(double extent, double room)
            {
                if (extent > 0) scale = Math.Min(scale, Math.Max(0, room) / extent);
            }
            FitOutside(originalPanel.xMin - left, x - 1);
            FitOutside(right + 1 - originalPanel.xMax, canvas.Width - x - width - 1);
            FitOutside(originalPanel.yMin - bottom, Math.Min(overhang, y - 1));
            FitOutside(top + 1 - originalPanel.yMax, canvas.Height - y - height - 1);
            if (scale <= 0) return false;
            inset = Math.Max(1, Math.Min(inset, width / 2));
            int verticalInset = Math.Max(1, inset / 2);
            for (int corner = 0; corner < 4; corner++)
            {
                if (maxX[corner] < minX[corner]) continue;
                int cropWidth = maxX[corner] - minX[corner] + 1, cropHeight = maxY[corner] - minY[corner] + 1;
                int paintedWidth = Math.Max(1, (int)Math.Round(cropWidth * scale));
                int paintedHeight = Math.Max(1, (int)Math.Round(cropHeight * scale));
                bool rightCorner = (corner & 1) != 0;
                bool upperCorner = corner >= 2;
                double sourceX = rightCorner ? originalPanel.xMax : originalPanel.xMin;
                double sourceY = upperCorner ? originalPanel.yMax : originalPanel.yMin;
                int targetX = (rightCorner ? x + width : x) + (int)Math.Round((minX[corner] - sourceX) * scale);
                int targetY = (upperCorner ? y + height : y) + (int)Math.Round((minY[corner] - sourceY) * scale);
                Ink Sample(Color32[] pixels, double sx, double sy, bool white)
                {
                    int x0 = (int)Math.Floor(sx), y0 = (int)Math.Floor(sy);
                    Color32 Pixel(int px, int py) => px >= 0 && px < sourceWidth && py >= 0 && py < sourceHeight
                        && labels[py * sourceWidth + px] == corner + 1 ? pixels[py * sourceWidth + px]
                        : white ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255);
                    Color32 a = Pixel(x0, y0), b = Pixel(x0 + 1, y0), c = Pixel(x0, y0 + 1), d = Pixel(x0 + 1, y0 + 1);
                    double fx = sx - x0, fy = sy - y0;
                    byte Mix(byte aa, byte bb, byte cc, byte dd) => (byte)Math.Round(
                        (aa * (1 - fx) + bb * fx) * (1 - fy) + (cc * (1 - fx) + dd * fx) * fy);
                    return new Ink(Mix(a.r, b.r, c.r, d.r), Mix(a.g, b.g, c.g, d.g), Mix(a.b, b.b, c.b, d.b));
                }
                for (int row = 0; row < paintedHeight; row++) for (int column = 0; column < paintedWidth; column++)
                {
                    int px = targetX + column, py = targetY + row;
                    // The inner plate covers vines passing behind the content.
                    // Only the native rim and the surrounding area are exposed.
                    if (px >= x + inset && px < x + width - inset
                        && py >= y + verticalInset && py < y + height - verticalInset) continue;
                    double sx = minX[corner] + (column + .5) / scale - .5;
                    double sy = minY[corner] + (row + .5) / scale - .5;
                    Ink color = DeckImageCompositor.Over(Sample(frame.Black, sx, sy, false), Sample(frame.White, sx, sy, true),
                        canvas.Pixel(targetX + column, targetY + row), linear);
                    canvas.RenderedPixel(targetX + column, targetY + row, color.R, color.G, color.B);
                }
            }
            return true;
        }

        private static int FrameCoordinate(int value, int sourceSize, int size, int sourceEdge, int edge)
        {
            if (value < edge) return value * sourceEdge / edge;
            if (value >= size - edge) return sourceSize - 1 - (size - 1 - value) * sourceEdge / edge;
            return sourceEdge + (value - edge) * (sourceSize - 2 * sourceEdge) / (size - 2 * edge);
        }

        internal static bool PaintManaWells(Pair tile, DeckImageCanvas canvas, int x, int y, int width, int height,
            int stride, int count, bool linear)
        {
            Ink Black(int index) => new Ink(tile.Black[index].r, tile.Black[index].g, tile.Black[index].b);
            Ink White(int index) => new Ink(tile.White[index].r, tile.White[index].g, tile.White[index].b);
            int Shade(int index)
            {
                Ink ink = Black(index);
                return DeckImageCompositor.Transmission(ink, White(index), linear) > .35
                    ? -1 : (ink.R + ink.G + ink.B) / 3;
            }

            // A flat texture behind the chart is not the recess artwork.
            // The native slot has a dark center and bright rails on both sides.
            int recessedRows = 0;
            for (int row = height / 4; row < height * 3 / 4; row++)
            {
                int offset = row * width;
                int center = Shade(offset + width / 2);
                if (center < 8 || center > 75) continue;
                int left = -1, right = -1;
                for (int column = 0; column < width / 4; column++)
                {
                    left = Math.Max(left, Shade(offset + column));
                    right = Math.Max(right, Shade(offset + width - 1 - column));
                }
                if (Math.Min(left, right) >= center + 12) recessedRows++;
            }
            if (recessedRows < height / 4) return false;

            for (int slot = 0; slot < count; slot++)
                for (int row = 0; row < height; row++)
                    for (int column = 0; column < width; column++)
                    {
                        int index = row * width + column;
                        int px = x + slot * stride + column, py = y + row;
                        Ink result = DeckImageCompositor.Over(Black(index), White(index), canvas.Pixel(px, py), linear);
                        canvas.RenderedPixel(px, py, result.R, result.G, result.B);
                    }
            return true;
        }

        internal static bool TouchesEdge(Pair pair, int width, int height, bool linear)
        {
            // Ignore faint shadows, but catch visible ornaments that extend
            // beyond the face mesh even when a layered shader writes zero alpha.
            bool Visible(int i)
            {
                Color32 black = pair.Black[i], white = pair.White[i];
                return DeckImageCompositor.Transmission(new Ink(black.r, black.g, black.b),
                    new Ink(white.r, white.g, white.b), linear) < .85;
            }

            int lastRow = (height - 1) * width;
            for (int x = 0; x < width; x++)
                if (Visible(x) || Visible(lastRow + x)) return true;
            for (int y = 1; y < height - 1; y++)
                if (Visible(y * width) || Visible(y * width + width - 1)) return true;
            return false;
        }
    }
}
