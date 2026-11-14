using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;

namespace BetterCollectionManager
{
    internal static class DeckImagePresentation
    {
        private static int Text(DeckImageRenderer renderer, string text, int x, int top, int width, int height, int font,
            DeckImageCanvas.Ink? ink = null, bool leftAligned = false, bool fitVisibleHeight = false, bool centerVisibleInk = false)
        {
            int y = renderer.Canvas.Height - top - height;
            return renderer.Caption(text, x, y, width, height, font,
                ink ?? renderer.Canvas.ContrastingInk(x + width / 2, y + height / 2), leftAligned,
                fitVisibleHeight: fitVisibleHeight, centerVisibleInk: centerVisibleInk);
        }

        internal static void Header(DeckImageRenderer renderer, DeckImageLayout layout, string name, TAG_CLASS deckClass, bool wild, DeckImageOption options)
        {
            foreach (Action step in HeaderSteps(renderer, layout, name, deckClass, wild, options)) step();
        }
        internal static IEnumerable<Action> HeaderSteps(DeckImageRenderer renderer, DeckImageLayout layout, string name, TAG_CLASS deckClass, bool wild, DeckImageOption options)
        {
            if (!layout.HasHeader) yield break;
            var frame = layout.InfoBox;
            yield return () => renderer.InfoFrame(frame.X, layout.Height - frame.Y - frame.Height, frame.Width, frame.Height);
            if (wild)
            {
                yield return () => renderer.WildFrame(frame.X, layout.Height - frame.Y - frame.Height,
                    frame.Width, frame.Height, layout.Px(DeckImageLayout.WildVineHeight), layout.Px(DeckImageLayout.WildVineInset),
                    layout.Px(DeckImageLayout.WildVineOverhang));
            }
            if (!options.HasFlag(DeckImageOption.Details)) yield break;
            var title = layout.TitleBox;
            int icons = layout.TitleTextInset;
            int titleWidth = title.Width - icons;
            int titleHeight = layout.Px(102);
            int actual = 0;
            yield return () => actual = Text(renderer, name, title.X + icons, title.Y, titleWidth, titleHeight, layout.Px(DeckImageLayout.HeaderFontSize),
                leftAligned: layout.TitleLeftAligned, centerVisibleInk: true);
            int center = title.X + icons + titleWidth / 2;
            int size = layout.Px(DeckImageLayout.HeaderIconSize), gap = layout.Px(20), top = title.Y + (titleHeight - size) / 2;
            int classX = layout.TitleLeftAligned ? title.X : center - actual / 2 - gap - size;
            yield return () => renderer.ClassIcon(deckClass, classX, layout.Height - top - size, size);

        }
        internal static string CardCaption(DeckImageSnapshot.Card card) => card.DisplayCount > 1 ? "x" + card.DisplayCount : "";

        internal static void CardQuantity(DeckImageRenderer renderer, DeckImageLayout layout, DeckImageLayout.Cell cell, DeckImageSnapshot.Card card, bool wild)
        {
            if (card.DisplayCount <= 1) return;
            int labelWidth = Math.Max(1, (int)Math.Round(cell.Width * .46));
            int labelHeight = Math.Max(1, (int)Math.Round(labelWidth * 327.0 / 576));
            int labelTop = cell.Y + cell.Height - labelHeight / 4;
            int labelX = cell.X + (cell.Width - labelWidth) / 2;
            renderer.QuantityLabel(card.DisplayCount, labelX, layout.Height - labelTop - labelHeight, labelWidth, labelHeight);
            if (card.DisplayCount <= 9) return;
            // Multi-digit values use the same reference frame, with a blank
            // numeral area. Match the actual glyph height to the baked x2
            // lettering, rather than sizing against a taller reference line.
            int inset = Math.Max(1, (int)Math.Round(labelWidth * 120.0 / 576));
            int lineTop = labelTop + (int)Math.Round(labelHeight * 70.0 / 327);
            int lineHeight = Math.Max(1, (int)Math.Round(labelHeight * 122.0 / 327));
            int font = Math.Max(1, (int)Math.Round(labelHeight * 88.0 / 327));
            Text(renderer, CardCaption(card), labelX + inset, lineTop, Math.Max(1, labelWidth - inset * 2), lineHeight, font,
                new DeckImageCanvas.Ink(255, 255, 255), fitVisibleHeight: true);
        }
        internal static void Statistics(DeckImageRenderer renderer, DeckImageLayout layout, DeckImageStatistics stats)
        {
            foreach (Action step in StatisticsSteps(renderer, layout, stats)) step();
        }
        internal static IEnumerable<Action> StatisticsSteps(DeckImageRenderer renderer, DeckImageLayout layout, DeckImageStatistics stats)
        {
            foreach (DeckImageLayout.Panel panel in layout.Panels)
                switch (panel.Option)
                {
                    case DeckImageOption.CraftingCost:
                        yield return () => Dust(renderer, layout, panel, stats); break;
                    case DeckImageOption.CardTypes:
                        int index = 0, row = layout.Px(DeckImageLayout.StatRow);
                        foreach (var pair in stats.Types.OrderBy(pair => (int)pair.Key))
                        {
                            int top = panel.Y + index * row;
                            yield return () => Text(renderer, pair.Value + " " + DeckImageStatistics.TypeName(pair.Key),
                                panel.X, top, panel.Width, row, layout.Px(48), leftAligned: true);
                            index++;
                        }
                        break;
                    case DeckImageOption.Rarities:
                        foreach (Action step in RaritySteps(renderer, layout, panel, stats)) yield return step;
                        break;
                    case DeckImageOption.ManaCurve:
                        foreach (Action step in CurveSteps(renderer, layout, panel, stats)) yield return step;
                        break;
                }
        }
        private static void Dust(DeckImageRenderer renderer, DeckImageLayout layout, DeckImageLayout.Panel panel, DeckImageStatistics stats)
        {
            int icon = layout.Px(DeckImageLayout.HeaderIconSize);
            int textX = layout.TitleBox.X + layout.TitleTextInset;
            int iconX = layout.TitleBox.X;
            int iconTop = panel.Y + layout.Px(7);
            renderer.DustIcon(iconX, renderer.Canvas.Height - iconTop - icon, icon * 5 / 4, icon);
            Text(renderer, stats.CraftingDust.ToString("N0", CultureInfo.InvariantCulture) + (stats.UnknownPriceCopies > 0 ? "+" : ""),
                textX, panel.Y, panel.X + panel.Width - textX, layout.Px(102), layout.Px(DeckImageLayout.HeaderFontSize), leftAligned: true);
        }
        private static IEnumerable<Action> RaritySteps(DeckImageRenderer renderer, DeckImageLayout layout, DeckImageLayout.Panel panel, DeckImageStatistics stats)
        {
            int index = 0, row = layout.Px(DeckImageLayout.StatRow);
            int gem = layout.Px(50), gap = layout.Px(8);
            foreach (var pair in stats.Rarities.OrderBy(pair => (int)pair.Key))
            {
                int top = panel.Y + index * row;
                int gemY = layout.Height - top - (row + gem) / 2;
                yield return () =>
                {
                    if (pair.Key != TAG_RARITY.FREE && renderer.RarityIcon(pair.Key, panel.X, gemY, gem, gem))
                        Text(renderer, pair.Value.ToString(), panel.X + gem + gap, top, panel.Width - gem - gap, row, layout.Px(48), leftAligned: true);
                    else
                        Text(renderer, pair.Value + " " + DeckImageStatistics.RarityName(pair.Key), panel.X + gem + gap, top, panel.Width - gem - gap, row,
                            layout.Px(44), leftAligned: true);
                };
                index++;
            }
        }
        private static IEnumerable<Action> CurveSteps(DeckImageRenderer renderer, DeckImageLayout layout, DeckImageLayout.Panel panel, DeckImageStatistics stats)
        {
            int[] bins = DeckImageStatistics.NativeManaBins(stats.Mana);
            int plusWidth = layout.Px(30), step = (panel.Width - plusWidth) / bins.Length;
            int left = panel.X;
            int wellTop = panel.Y + layout.Px(4), wellHeight = layout.Px(176), wellWidth = step - layout.Px(4);
            int baseline = wellTop + wellHeight - layout.Px(5), maxHeight = layout.Px(133), maximum = Math.Max(1, bins.Max());
            int barWidth = wellWidth * 3 / 5, crystalSize = layout.Px(50), crystalTop = wellTop + wellHeight + layout.Px(6);
            int countHeight = layout.Px(28);
            yield return () => renderer.ManaWells(left + (step - wellWidth) / 2, layout.Height - wellTop - wellHeight, wellWidth, wellHeight, step, bins.Length);
            for (int i = 0; i < bins.Length; i++)
            {
                int bin = i;
                int x = left + step * bin, bar = bins[bin] * maxHeight / maximum;
                yield return () =>
                {
                    if (bins[bin] > 0)
                        renderer.ManaBar(bins[bin], maximum, x + (step - barWidth) / 2,
                            renderer.Canvas.Height - baseline, barWidth, maxHeight);
                    Text(renderer, bins[bin].ToString(), x, baseline - bar - countHeight - layout.Px(2), step, countHeight,
                        layout.Px(27), DeckImageCanvas.Text);
                    int crystalX = x + (step - crystalSize) / 2;
                    renderer.ManaCrystal(crystalX, layout.Height - crystalTop - crystalSize, crystalSize);
                    Text(renderer, bin.ToString(), crystalX, crystalTop, crystalSize, crystalSize, layout.Px(34), DeckImageCanvas.Text);
                    if (bin == bins.Length - 1)
                        Text(renderer, "+", crystalX + crystalSize, crystalTop, plusWidth, crystalSize, layout.Px(30), DeckImageCanvas.Text);
                };
            }
        }
    }
}
