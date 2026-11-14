using BetterCollectionManager;
using UnityEngine;

internal static class CardFrameChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const int width = 48, height = 64;
        DeckImageCardFrame Card(bool legendary, bool spell = false)
        {
            var black = new Color32[width * height];
            var white = Enumerable.Repeat(new Color32(255, 255, 255, 255), width * height).ToArray();
            for (int y = 10; y < (spell ? 38 : 48); y++)
                for (int x = 10; x < (spell ? 25 : 30); x++)
                    black[y * width + x] = white[y * width + x] = new Color32(180, 90, 30, 255);
            if (legendary)
                for (int y = 46; y < 56; y++)
                    for (int x = 18; x < 38; x++)
                        black[y * width + x] = white[y * width + x] = new Color32(210, 140, 45, 255);
            var pair = new DeckImageCapture.Pair(black, white);
            return new DeckImageCardFrame(pair, pair, width, height, 20, false);
        }
        var cards = new[] { Card(false), Card(true) };
        var plainBounds = DeckImageCardFrame.Footprint(new[] { cards[0] });
        check(plainBounds.Width <= cards[0].BodyWidth * 1.2,
            "An ordinary card does not reserve a broad invisible shadow margin that shrinks its body.");
        var bounds = DeckImageCardFrame.Footprint(cards);
        var canvas = new DeckImageCanvas(200, 130, true);
        var first = cards[0].Paint(canvas, new DeckImageLayout.Cell(0, 0, 90, 120, 8), bounds);
        var second = cards[1].Paint(canvas, new DeckImageLayout.Cell(100, 0, 90, 120, 8), bounds);
        check(first.Width == second.Width && first.Height == second.Height && first.Y == second.Y
            && second.X - first.X == 100,
            "A larger legendary ornament keeps the same body size and baseline as the normal card.");
        check(cards[1].Height > cards[0].Height && cards[1].Width > cards[0].Width,
            "The legendary frame is retained in the shared footprint.");

        var spell = Card(false, true);
        var mixed = new[] { cards[0], spell, cards[1] };
        var mixedBounds = DeckImageCardFrame.Footprint(mixed);
        var mixedCanvas = new DeckImageCanvas(300, 130, true);
        var bodies = mixed.Select((card, index) => card.Paint(mixedCanvas,
            new DeckImageLayout.Cell(index * 100, 0, 90, 120, 8), mixedBounds)).ToArray();
        int VisibleWidth(int index)
        {
            int y = mixedCanvas.Height - bodies[index].Y - bodies[index].Height / 2;
            return Enumerable.Range(index * 100, 90).Count(x => mixedCanvas.Pixel(x, y).R == 180);
        }
        check(Math.Abs(VisibleWidth(0) - VisibleWidth(1)) <= 1 && VisibleWidth(0) > 25,
            "A spell with a smaller visible frame inside identical mesh bounds renders as wide as a minion.");
        check(bodies.All(body => body.Y + body.Height == bodies[0].Y + bodies[0].Height),
            "Spells, minions and legendary cards align their visible lower edges and quantity shields.");

        var deck = Enumerable.Range(0, 19).Select(i => Card(i % 5 == 0, i % 5 != 0 && i % 2 == 0)).ToArray();
        foreach (DeckImageAspect aspect in Enum.GetValues<DeckImageAspect>())
        {
            var packed = new DeckImageLayout(deck.Length, 8192, DeckImageSettings.Capture(), aspect);
            if (aspect == DeckImageAspect.Landscape)
                check(packed.Cells.GroupBy(c => c.Y).Select(row => row.Count()).SequenceEqual(new[] { 7, 7, 5 }),
                    "The 19-card landscape deck uses three rows with seven, seven and five cards.");
            check(packed.CardPixelWidth == 544, "Captured silhouettes retain full-size card bodies in every preset.");
            var largerDeck = new DeckImageLayout(deck.Length + 1, 8192, DeckImageSettings.Capture(), aspect);
            check(largerDeck.CardPixelWidth == packed.CardPixelWidth,
                "Adding a card changes the canvas instead of shrinking existing cards.");
            check(packed.Cells.Count == deck.Length && packed.Cells.All(c => c.X >= 0 && c.Y >= 0
                && c.X + c.Width <= packed.Width && c.Y + c.Height + c.Caption <= packed.Height),
                "Every packed card and its larger quantity label fit in the dynamically sized canvas.");
            for (int i = 0; i < packed.Cells.Count; i++)
                for (int j = i + 1; j < packed.Cells.Count; j++)
                {
                    var a = packed.Cells[i]; var b = packed.Cells[j];
                    check(a.X + a.Width <= b.X || b.X + b.Width <= a.X
                        || a.Y + a.Height + a.Caption <= b.Y || b.Y + b.Height + b.Caption <= a.Y,
                        "Packed silhouettes and quantity labels never overlap their neighbors.");
                }
        }

        // Large ornaments fit a predetermined tile without changing later rows.
        var fixedTile = new DeckImageLayout.Cell(10, 10, 96, 128, 16);
        foreach (var card in cards)
        {
            var tileCanvas = new DeckImageCanvas(120, 170, false);
            bool borderBehind = false;
            int underX = 0, underY = 0;
            var body = card.PaintFitted(tileCanvas, fixedTile, projected =>
            {
                borderBehind = true;
                underX = projected.X + projected.Width / 2;
                underY = tileCanvas.Height - projected.Y - projected.Height / 2;
                tileCanvas.RenderedPixel(underX, underY, 12, 34, 56);
            });
            check(borderBehind && tileCanvas.Pixel(underX, underY).R == 180
                && body.Width > 0 && body.Width <= card.BodyWidth
                && body.X >= fixedTile.X && body.X + body.Width <= fixedTile.X + fixedTile.Width,
                "Cards keep their chosen render size in a spacious tile; oversized ornaments fit and the label is painted before the card.");
        }

        var symbolBlack = new Color32[48 * 32];
        var symbolWhite = Enumerable.Repeat(new Color32(255, 255, 255, 255), 48 * 32).ToArray();
        for (int y = 12; y < 20; y++)
            for (int x = 18; x < 30; x++)
                symbolBlack[y * 48 + x] = symbolWhite[y * 48 + x] = new Color32(80, 60, 40, 255);
        var symbolCanvas = new DeckImageCanvas(100, 80, false);
        check(DeckImageCapture.PaintSymbol(new DeckImageCapture.Pair(symbolBlack, symbolWhite), 48, 32,
            symbolCanvas, 20, 20, 60, 40, false), "The native quantity border can be fitted by RGB coverage.");
        check(symbolCanvas.Pixel(20, 20).R == 80 && symbolCanvas.Pixel(79, 59).R == 80
            && symbolCanvas.Pixel(19, 20).R == 20 && symbolCanvas.Pixel(80, 59).R == 20,
            "Transparent mesh padding no longer shrinks the visible quantity shield inside its requested box.");

        var tallBlack = new Color32[24 * 40];
        var tallWhite = Enumerable.Repeat(new Color32(255, 255, 255, 255), 24 * 40).ToArray();
        for (int y = 8; y < 32; y++)
            for (int x = 6; x < 14; x++)
                tallBlack[y * 24 + x] = tallWhite[y * 24 + x] = new Color32(80, 120, 180, 255);
        var iconCanvas = new DeckImageCanvas(80, 90, false);
        check(DeckImageCapture.PaintSymbol(new DeckImageCapture.Pair(tallBlack, tallWhite), 24, 40,
            iconCanvas, 10, 10, 50, 60, false)
            && iconCanvas.Pixel(25, 40).R == 80 && iconCanvas.Pixel(24, 40).R == 20
            && iconCanvas.Pixel(45, 40).R == 20,
            "The narrow Dust silhouette is centered in the shared icon column despite transparent padding.");

        var paddedClass = new Color32[24 * 40];
        for (int y = 8; y < 32; y++)
            for (int x = 6; x < 14; x++) paddedClass[y * 24 + x] = new Color32(80,120,180,255);
        var classCanvas = new DeckImageCanvas(80, 90, false);
        check(DeckImageCapture.PaintRgbaSymbol(paddedClass, 24, 40, classCanvas, 10, 10, 50, 60)
            && classCanvas.Pixel(25, 40).R == 80 && classCanvas.Pixel(24, 40).R == 20
            && classCanvas.Pixel(25, 10).R == 80 && classCanvas.Pixel(25, 69).R == 80
            && classCanvas.Pixel(25, 70).R == 20,
            "Atlas padding is excluded so class and Dust icons share their visible center and full requested height.");
        var roundClass = new Color32[40 * 40];
        for (int y = 8; y < 32; y++)
            for (int x = 8; x < 32; x++) roundClass[y * 40 + x] = new Color32(80,120,180,255);
        var roundCanvas = new DeckImageCanvas(80, 90, false);
        DeckImageCapture.PaintRgbaSymbol(roundClass, 40, 40, roundCanvas, 10, 10, 50, 60);
        check(roundCanvas.Pixel(10, 40).R == 80 && roundCanvas.Pixel(59, 40).R == 80
            && iconCanvas.Pixel(35, 40).R == 80 && iconCanvas.Pixel(10, 40).R == 20,
            "A narrower Dust jar moves inward relative to a round class icon, preserving their shared center.");
        // Rune styling is captured independently from the original silhouette.
        // Opaque color outside the mask must never cover the export background.
        var originalBlack = new Color32[width * height];
        var originalWhite = Enumerable.Repeat(new Color32(255, 255, 255, 255), width * height).ToArray();
        for (int y = 10; y < 48; y++)
            for (int x = 10; x < 30; x++)
                originalBlack[y * width + x] = originalWhite[y * width + x] = new Color32(150, 90, 50, 255);
        var redPlane = Enumerable.Repeat(new Color32(180, 70, 60, 255), width * height).ToArray();
        foreach (bool linear in new[] { false, true })
        {
            var styledCard = new DeckImageCardFrame(new DeckImageCapture.Pair(redPlane, redPlane),
                new DeckImageCapture.Pair(originalBlack, originalWhite), width, height, 20, linear);
            var styledCanvas = new DeckImageCanvas(100, 100, false);
            var styledBounds = DeckImageCardFrame.Footprint(new[] { styledCard });
            styledCard.Paint(styledCanvas, new DeckImageLayout.Cell(20, 20, styledBounds.Width, styledBounds.Height, 8), styledBounds);
            check(styledCanvas.Pixel(30, 60).R == 180 && styledCanvas.Pixel(30, 60).G == 70
                && styledCanvas.Pixel(5, 60).R == 20,
                "Optimized sideboard painting keeps the red treatment inside its silhouette in both color spaces.");
        }

        var shadow = new DeckImageCanvas(64, 64, true);
        byte[] mask = Enumerable.Repeat((byte)255, 16 * 24).ToArray();
        shadow.CardShadow(mask, 16, 24, 20, 22, 2);
        check(shadow.Pixel(28, 21).R < 194 && shadow.Pixel(28, 21).R > 150
            && shadow.Pixel(0, 0).R == 194 && shadow.Pixel(10, 10).R == 194,
            "A subtle soft shadow follows the card and leaves distant background pixels untouched.");

        var backdrop = new DeckImageCanvas(96, 80, false);
        DeckImageBackgroundArt.Paint(backdrop, TAG_CLASS.PALADIN);
        check(backdrop.Rgba.Where((value, index) => index % 4 == 3).All(value => value == 255)
            && backdrop.Rgba.Where((value, index) => index % 4 != 3).Distinct().Count() > 25,
            "The embedded Paladin background decodes as opaque class artwork.");
        foreach (int deckClass in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 14 })
        {
            DeckImageBackgroundArt.Paint(backdrop, (TAG_CLASS)deckClass);
            check(backdrop.Rgba.Where((value, index) => index % 4 == 3).All(value => value == 255)
                && (deckClass == 0
                    ? backdrop.Rgba.Where((value, index) => index % 4 != 3).All(value => value == 0)
                    : backdrop.Rgba.Distinct().Count() > 25),
                "Every bundled class background decodes, including DeckView's plain black neutral background.");
        }
        var neutral = new DeckImageCanvas(96, 80, false);
        DeckImageBackgroundArt.Paint(neutral, (TAG_CLASS)0);
        DeckImageBackgroundArt.Paint(backdrop, (TAG_CLASS)999);
        check(neutral.Rgba.SequenceEqual(backdrop.Rgba), "Unknown classes use the neutral background.");
    }
}
