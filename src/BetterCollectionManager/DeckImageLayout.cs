using System;
using System.Collections.Generic;

namespace BetterCollectionManager
{
    // Layout coordinates are top-left based. The selected format and distinct card
    // count choose the row capacity; the canvas grows around full-size cards.
    internal sealed class DeckImageLayout
    {
        internal const int CardWidth = 272, CardHeight = 380, ExportCardWidth = 544, Gap = 8, Margin = 28;
        internal const double CaptionRatio = 72.0 / CardWidth * .75;
        internal const int StatRow = 64, TitleIconSpace = 128, HeaderIconSize = 88, HeaderFontSize = 80, FramePadding = 24;
        internal const int WildVineHeight = 252, WildVineInset = 48, WildVineOverhang = 12;
        internal readonly int Columns, Width, Height, CardPixelWidth, CardPixelHeight, CaptionPixels;
        internal readonly (int X, int Y, int Width, int Height) InfoBox, TitleBox;
        internal bool HasHeader => InfoBox.Height > 0;
        internal readonly bool TitleLeftAligned;
        internal readonly int TitleTextInset;
        internal readonly double Scale, ResolutionScale;
        internal readonly DeckImageAspect Aspect;
        internal readonly DeckImageResolution Resolution;
        private readonly DeckImageLayout m_original;
        internal DeckImageLayout Original => m_original ?? this;
        internal readonly List<Cell> Cells = new List<Cell>();
        internal readonly List<Panel> Panels = new List<Panel>();
        internal int Px(double value) => (int)Math.Round(value * Scale);
        internal readonly struct Panel
        {
            internal readonly DeckImageOption Option;
            internal readonly int X, Y, Width, Height;
            internal Panel(DeckImageOption option, int x, int y, int width, int height)
            { Option = option; X = x; Y = y; Width = width; Height = height; }
        }
        internal readonly struct Cell
        {
            internal readonly int X, Y, Width, Height, Caption;
            internal Cell(int x, int y, int width, int height, int caption)
            { X = x; Y = y; Width = width; Height = height; Caption = caption; }
        }
        private static int Total(IReadOnlyList<int> sectionSizes)
        {
            int total = 0;
            foreach (int count in sectionSizes)
            {
                if (count < 0) throw new ArgumentOutOfRangeException(nameof(sectionSizes));
                total = checked(total + count);
            }
            return total;
        }
        internal static DeckImageLayout ForExport(int total, int maxTextureSize, DeckImageOption options,
            DeckImageAspect aspect, int typeCount, int rarityCount, bool wild, DeckImageResolution resolution, int? titleTextWidth = null)
        {
            // Measure only geometry at full size; reduced presets must also work
            // when the full-size deck would exceed the texture limit.
            var original = new DeckImageLayout(total, maxTextureSize, options, aspect, typeCount, rarityCount,
                titleTextWidth: titleTextWidth, wild: wild, validateTextureLimit: false);
            int target = DeckImageSettings.ResolutionPixels(resolution);
            double scale = target == 0 ? 1 : Math.Min(1, (double)target / Math.Min(original.Width, original.Height));
            // Every GPU capture and canvas is created directly at this scale.
            return new DeckImageLayout(original, scale, resolution, maxTextureSize);
        }

        private DeckImageLayout(DeckImageLayout original, double scale, DeckImageResolution resolution, int maxTextureSize)
        {
            m_original = original; Resolution = resolution; ResolutionScale = scale;
            Columns = original.Columns; Aspect = original.Aspect; Scale = original.Scale * scale;
            int Pixel(int value) => (int)Math.Round(value * scale);
            (int X, int Y, int Width, int Height) Box(int x, int y, int width, int height)
                => (Pixel(x), Pixel(y), Pixel(x + width) - Pixel(x), Pixel(y + height) - Pixel(y));
            Width = Pixel(original.Width); Height = Pixel(original.Height);
            CardPixelWidth = Pixel(original.CardPixelWidth); CardPixelHeight = Pixel(original.CardPixelHeight);
            CaptionPixels = Pixel(original.CaptionPixels); TitleTextInset = Pixel(original.TitleTextInset);
            TitleLeftAligned = original.TitleLeftAligned;
            var info = original.InfoBox; InfoBox = Box(info.X, info.Y, info.Width, info.Height);
            var title = original.TitleBox; TitleBox = Box(title.X, title.Y, title.Width, title.Height);
            foreach (Panel panel in original.Panels)
            {
                var box = Box(panel.X, panel.Y, panel.Width, panel.Height);
                Panels.Add(new Panel(panel.Option, box.X, box.Y, box.Width, box.Height));
            }
            foreach (Cell cell in original.Cells)
            {
                var box = Box(cell.X, cell.Y, cell.Width, cell.Height);
                Cells.Add(new Cell(box.X, box.Y, box.Width, box.Height,
                    Pixel(cell.Y + cell.Height + cell.Caption) - Pixel(cell.Y + cell.Height)));
            }
            ValidateTextureSize(maxTextureSize);
        }

        private void ValidateTextureSize(int maxTextureSize)
        {
            if (Width > Math.Min(8192, maxTextureSize) || Height > Math.Min(8192, maxTextureSize))
                throw new InvalidOperationException("The deck image exceeds the GPU texture limit. Choose a lower image resolution, a different format or a smaller deck.");
        }

        internal DeckImageLayout(IReadOnlyList<int> sectionSizes, int maxTextureSize)
            : this(Total(sectionSizes), maxTextureSize, DeckImageOption.None) { }
        internal DeckImageLayout(int total, int maxTextureSize, DeckImageOption options,
            DeckImageAspect aspect = DeckImageAspect.Portrait, int typeCount = 5, int rarityCount = 5,
            double cardAspect = (double)CardWidth / CardHeight, int? titleTextWidth = null, bool wild = false, double resolutionScale = 1, bool validateTextureLimit = true)
        {
            if (total <= 0) throw new InvalidOperationException("The deck is empty.");
            if (total > 240) throw new InvalidOperationException("This deck is too large to export as one image.");
            if (!Enum.IsDefined(typeof(DeckImageAspect), aspect)) throw new ArgumentOutOfRangeException(nameof(aspect));
            if (resolutionScale <= 0 || resolutionScale > 2 || double.IsNaN(resolutionScale))
                throw new ArgumentOutOfRangeException(nameof(resolutionScale));
            ResolutionScale = resolutionScale; Resolution = DeckImageResolution.Max;
            int GridPx(double value) => (int)Math.Round(value * resolutionScale);
            Aspect = aspect;
            if (cardAspect <= 0 || double.IsNaN(cardAspect) || double.IsInfinity(cardAspect))
                throw new ArgumentOutOfRangeException(nameof(cardAspect));
            // Aim for the selected shape without imposing fixed image dimensions.
            // Include the quantity-label space when estimating a card's full tile.
            double targetRatio = aspect == DeckImageAspect.Landscape ? 16.0 / 9
                : aspect == DeckImageAspect.Square ? 1 : 2.0 / 3;
            double tileRatio = 1 / cardAspect + CaptionRatio;
            double columnEstimate = Math.Sqrt(total * targetRatio * tileRatio);
            // Narrow formats favor fewer cards per row and grow downward.
            Columns = Math.Min(total, Math.Max(1, (int)(aspect == DeckImageAspect.Landscape
                ? Math.Round(columnEstimate) : Math.Floor(columnEstimate))));
            CardPixelWidth = GridPx(ExportCardWidth);
            CaptionPixels = (int)Math.Ceiling(CardPixelWidth * CaptionRatio);
            // Keep the shield and card proportions consistent in every format;
            // narrow formats gain readability through fewer columns and more rows.
            Scale = 1.4 * resolutionScale;
            int margin = GridPx(Margin * 1.4), gap = GridPx(Gap * 1.4);
            bool cost = options.HasFlag(DeckImageOption.CraftingCost);
            bool types = options.HasFlag(DeckImageOption.CardTypes), rarities = options.HasFlag(DeckImageOption.Rarities);
            bool curve = options.HasFlag(DeckImageOption.ManaCurve);
            bool hasHeader = options.HasFlag(DeckImageOption.Details) || cost || types || rarities || curve;
            int inset = Px(56), verticalInset = Px(FramePadding);
            // Fix every cell before loading any native card. The extra room is
            // for protruding ornaments; unusually large frames fit within the tile.
            int tileWidth = CardPixelWidth + GridPx(96);
            CardPixelHeight = (int)Math.Ceiling(CardPixelWidth / cardAspect) + GridPx(120);
            int rowCount = (total + Columns - 1) / Columns;
            int gridWidth = Columns * tileWidth + (Columns - 1) * gap;
            int gridHeight = rowCount * (CardPixelHeight + CaptionPixels) + (rowCount - 1) * gap;
            int row = Px(StatRow), statsGap = Px(48);
            int typeRows = types ? Math.Max(1, typeCount) : 0;
            int rarityRows = rarities ? Math.Max(1, rarityCount) : 0;
            int rarityWidth = rarities ? Px(172) : 0, typeWidth = types ? Px(276) : 0;
            int compositionGap = types && rarities ? Px(22) : 0;
            int compositionWidth = rarityWidth + compositionGap + typeWidth;
            int compositionHeight = Math.Max(typeRows, rarityRows) * row;
            int costWidth = cost ? Px(512) : 0, costHeight = cost ? Px(112) : 0;
            int curveWidth = curve ? Px(544) : 0, curveHeight = curve ? Px(244) : 0;
            bool costUnderTitle = cost;
            TitleTextInset = options.HasFlag(DeckImageOption.Details) ? Px(TitleIconSpace) : cost ? Px(96) : 0;
            int titleHeight = Px(112), titleStack = titleHeight + (costUnderTitle ? Px(4) + costHeight : 0);
            int headerHeight = Math.Max(titleStack, Math.Max(compositionHeight, curveHeight));
            int rightGroups = (compositionWidth > 0 ? 1 : 0) + (curve ? 1 : 0) + (cost && !costUnderTitle ? 1 : 0);
            int minimumHeaderWidth = Math.Max(Px(480), costWidth) + compositionWidth + curveWidth + (costUnderTitle ? 0 : costWidth)
                + rightGroups * statsGap + inset * 2;
            bool splitHeader = aspect != DeckImageAspect.Landscape && rightGroups > 0 && minimumHeaderWidth > gridWidth;
            int lowerWidth = compositionWidth + curveWidth + (costUnderTitle ? 0 : costWidth) + Math.Max(0, rightGroups - 1) * statsGap;
            int lowerHeight = Math.Max(compositionHeight, curveHeight);
            int compactWidth = Math.Max(Math.Max(Px(480), costWidth), lowerWidth) + inset * 2;
            Width = (hasHeader ? Math.Max(gridWidth, splitHeader ? compactWidth : minimumHeaderWidth) : gridWidth) + margin * 2;
            int contentWidth = Width - margin * 2 - inset * 2;
            int titleWidth = splitHeader ? contentWidth
                : contentWidth - compositionWidth - curveWidth - (costUnderTitle ? 0 : costWidth) - rightGroups * statsGap;
            if (rightGroups > 0) titleWidth = Math.Min(titleWidth, Px(680));
            if (titleTextWidth.HasValue)
            {
                int icons = TitleTextInset;
                int required = icons + Math.Max(Px(12), titleTextWidth.Value + Px(12));
                if (costUnderTitle) required = Math.Max(required, costWidth);
                titleWidth = Math.Min(titleWidth, required);
            }
            int frameWidth = splitHeader ? Math.Max(titleWidth, lowerWidth) + inset * 2
                : titleWidth + compositionWidth + curveWidth + (costUnderTitle ? 0 : costWidth) + rightGroups * statsGap + inset * 2;
            if (splitHeader) headerHeight = titleStack + statsGap + lowerHeight;
            int contentX = (Width - frameWidth) / 2 + inset;
            // Decoration never changes the canvas, panel or card layout.
            int headerY = margin;
            int contentY = headerY + verticalInset;
            TitleLeftAligned = true;
            TitleBox = hasHeader ? (contentX, contentY + (splitHeader ? 0 : (headerHeight - titleStack) / 2), titleWidth, titleHeight) : default;
            InfoBox = hasHeader ? ((Width - frameWidth) / 2, headerY, frameWidth, headerHeight + verticalInset * 2) : default;
            int gridStart = hasHeader ? InfoBox.Y + InfoBox.Height + Px(16) : margin;
            Height = gridStart + gridHeight + margin;
            if (validateTextureLimit) ValidateTextureSize(maxTextureSize);
            int statX = splitHeader ? contentX : TitleBox.X + TitleBox.Width + statsGap;
            int statsY = splitHeader ? contentY + titleStack + statsGap : contentY;
            int statsHeight = splitHeader ? lowerHeight : headerHeight;
            if (cost)
            {
                int x = costUnderTitle ? TitleBox.X : statX;
                int y = costUnderTitle ? TitleBox.Y + TitleBox.Height + Px(4) : contentY + (headerHeight - costHeight) / 2;
                Panels.Add(new Panel(DeckImageOption.CraftingCost, x, y, costWidth, costHeight));
            }
            if (compositionWidth > 0)
            {
                int y = statsY + (statsHeight - compositionHeight) / 2;
                if (rarities) Panels.Add(new Panel(DeckImageOption.Rarities, statX, y, rarityWidth, rarityRows * row));
                if (types) Panels.Add(new Panel(DeckImageOption.CardTypes, statX + rarityWidth + compositionGap, y, typeWidth, typeRows * row));
                statX += compositionWidth + statsGap;
            }
            if (curve)
                Panels.Add(new Panel(DeckImageOption.ManaCurve, statX, statsY + (statsHeight - curveHeight) / 2, curveWidth, curveHeight));
            for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                int count = Math.Min(Columns, total - rowIndex * Columns);
                int rowWidth = count * tileWidth + (count - 1) * gap;
                int left = (Width - rowWidth) / 2;
                int top = gridStart + rowIndex * (CardPixelHeight + CaptionPixels + gap);
                for (int column = 0; column < count; column++)
                    Cells.Add(new Cell(left + column * (tileWidth + gap), top, tileWidth, CardPixelHeight, CaptionPixels));
            }
        }
    }

    internal static class DeckImageBitmap
    {
        // CF_DIB uses a bottom-up BGR bitmap, padded to four bytes per row.
        // The export is opaque; PNG and legacy bitmap consumers see the same pixels.
        internal static byte[] ToDib(byte[] rgba, int width, int height)
        {
            if (width <= 0 || height <= 0 || rgba == null || rgba.Length != checked(width * height * 4))
                throw new ArgumentException("Invalid image pixels.");
            int stride = checked((width * 3 + 3) & ~3);
            int pixelBytes = checked(stride * height);
            var dib = new byte[checked(40 + pixelBytes)];
            void Int32(int offset, int value)
            {
                dib[offset] = (byte)value; dib[offset + 1] = (byte)(value >> 8);
                dib[offset + 2] = (byte)(value >> 16); dib[offset + 3] = (byte)(value >> 24);
            }
            Int32(0, 40); Int32(4, width); Int32(8, height);
            dib[12] = 1; dib[14] = 24;
            Int32(20, pixelBytes);
            for (int y = 0; y < height; y++)
            {
                int source = y * width * 4, target = 40 + y * stride;
                for (int x = 0; x < width; x++, source += 4, target += 3)
                {
                    dib[target] = rgba[source + 2];
                    dib[target + 1] = rgba[source + 1];
                    dib[target + 2] = rgba[source];
                }
            }
            return dib;
        }
    }
}
