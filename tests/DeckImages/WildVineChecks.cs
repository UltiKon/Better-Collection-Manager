using BetterCollectionManager;
using UnityEngine;
using Ink = BetterCollectionManager.DeckImageCanvas.Ink;

internal static class WildVineChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const int sw = 100, sh = 80, edge = 80, overhangLimit = 12;
        var black = new Color32[sw * sh];
        var white = Enumerable.Repeat(new Color32(255, 255, 255, 255), black.Length).ToArray();
        var pair = new DeckImageCapture.Pair(black, white);
        var originalPanel = new Rect(10, 10, 80, 60);
        const int shieldX = 300, shieldY = 140, shieldWidth = 400, shieldHeight = 180, inset = 20;
        var canvas = new DeckImageCanvas(1000, 460, false);
        Ink background = canvas.Pixel(0, 0);
        check(!DeckImageCapture.PaintWildFrame(pair, sw, sh, canvas, shieldX, shieldY, shieldWidth, shieldHeight, edge, inset, overhangLimit, originalPanel, false),
            "Invisible native vines are rejected.");
        var shapes = new[] {
            (X: 6, Y: 5, Width: 25, Height: 21, Color: new Color32(40, 130, 50, 255)),
            (X: 80, Y: 8, Width: 12, Height: 19, Color: new Color32(80, 80, 190, 255)),
            (X: 7, Y: 52, Width: 9, Height: 23, Color: new Color32(150, 60, 160, 255)),
            // This entire silhouette belongs at the upper right, including
            // the curl extending across the source image's midpoint.
            (X: 30, Y: 52, Width: 61, Height: 19, Color: new Color32(180, 120, 40, 255)) };
        foreach (var shape in shapes)
            for (int row = shape.Y; row < shape.Y + shape.Height; row++)
                for (int column = shape.X; column < shape.X + shape.Width; column++)
                    black[row * sw + column] = white[row * sw + column] = shape.Color;
        check(DeckImageCapture.PaintWildFrame(pair, sw, sh, canvas, shieldX, shieldY, shieldWidth, shieldHeight, edge, inset, overhangLimit, originalPanel, false),
            "The native corner silhouettes render.");
        // The shared scale is 2.4: the five native pixels below the panel
        // consume the existing twelve-pixel card gap. Every attachment offset
        // and size relationship must follow that same transform.
        var expected = new[] { (X: 290, Y: 128, Width: 60, Height: 50), (X: 676, Y: 135, Width: 29, Height: 46),
            (X: 293, Y: 277, Width: 22, Height: 55), (X: 556, Y: 277, Width: 146, Height: 46) };
        for (int corner = 0; corner < shapes.Length; corner++)
        {
            int left = canvas.Width, right = -1, bottom = canvas.Height, top = -1;
            for (int y = 0; y < canvas.Height; y++) for (int x = 0; x < canvas.Width; x++)
            {
                Ink ink = canvas.Pixel(x, y);
                if ((x >= canvas.Width / 2 ? 1 : 0) + (y >= canvas.Height / 2 ? 2 : 0) != corner
                    || ink.R == background.R && ink.G == background.G && ink.B == background.B) continue;
                left = Math.Min(left, x); right = Math.Max(right, x);
                bottom = Math.Min(bottom, y); top = Math.Max(top, y);
            }
            var box = expected[corner];
            check(Math.Abs(left - box.X) <= 1 && Math.Abs(bottom - box.Y) <= 1
                && Math.Abs(right - left + 1 - box.Width) <= 1 && Math.Abs(top - bottom + 1 - box.Height) <= 1,
                "Native panel offsets and relative vine sizes survive without per-corner anchor guesses.");
            check(bottom >= shieldY - overhangLimit && top < shieldY + shieldHeight + overhangLimit,
                "Native decoration fits the existing margin and card gap without expanding the canvas.");
        }
        bool centerIntact = true, outsideIntact = true;
        for (int y = 0; y < canvas.Height; y++) for (int x = 0; x < canvas.Width; x++)
        {
            Ink ink = canvas.Pixel(x, y);
            bool same = ink.R == background.R && ink.G == background.G && ink.B == background.B;
            if (x >= shieldX + inset && x < shieldX + shieldWidth - inset
                && y >= shieldY + inset / 2 && y < shieldY + shieldHeight - inset / 2) centerIntact &= same;
            if (y < shieldY - overhangLimit || y >= shieldY + shieldHeight + overhangLimit) outsideIntact &= same;
        }
        check(centerIntact && outsideIntact, "Vines touch the shield border while leaving its entire content area untouched.");
        check(canvas.Pixel(shieldX + edge / 2, shieldY).G == shapes[0].Color.g
            && canvas.Pixel(shieldX + shieldWidth - edge, shieldY + shieldHeight).R == shapes[3].Color.r,
            "Vines follow the bottom and top rails instead of being displaced into isolated side posts.");

        var wideCanvas = new DeckImageCanvas(1600, 680, false);
        check(DeckImageCapture.PaintWildFrame(pair, sw, sh, wideCanvas, 200, 240, 1200, 400, 200, inset, overhangLimit, originalPanel, false),
            "A wide shield retains the native upper-right branch placement.");
        int branchLeft = wideCanvas.Width, branchRight = -1, branchBottom = wideCanvas.Height, branchTop = -1;
        bool manaClear = true;
        for (int y = 0; y < wideCanvas.Height; y++) for (int x = 0; x < wideCanvas.Width; x++)
        {
            Ink ink = wideCanvas.Pixel(x, y);
            if (x >= wideCanvas.Width / 2 && y >= wideCanvas.Height / 2
                && (ink.R != background.R || ink.G != background.G || ink.B != background.B))
            {
                branchLeft = Math.Min(branchLeft, x); branchRight = Math.Max(branchRight, x);
                branchBottom = Math.Min(branchBottom, y); branchTop = Math.Max(branchTop, y);
            }
            if (x >= 1050 && x < 1380 && y >= 260 && y < 630)
                manaClear &= ink.R == background.R && ink.G == background.G && ink.B == background.B;
        }
        check(Math.Abs(branchLeft - 1256) <= 2 && Math.Abs(branchBottom - 597) <= 2
            && Math.Abs(branchRight - branchLeft + 1 - 146) <= 2 && Math.Abs(branchTop - branchBottom + 1 - 46) <= 2,
            $"Native wide-panel branch position and scale: {branchLeft},{branchBottom} {branchRight - branchLeft + 1}x{branchTop - branchBottom + 1}.");
        check(manaClear, "The enlarged upper-right branch cannot paint over the mana curve area.");

        foreach (DeckImageAspect aspect in Enum.GetValues<DeckImageAspect>())
        foreach (int total in new[] { 1, 19, 30 })
        for (int flags = 0; flags < 64; flags++)
        {
            var options = DeckImageSettings.Options.Where((option, bit) => (flags & (1 << bit)) != 0)
                .Aggregate(DeckImageOption.None, (value, option) => value | option);
            var layout = new DeckImageLayout(total, 8192, options, aspect, wild: true);
            var plain = new DeckImageLayout(total, 8192, options, aspect);
            var frame = layout.InfoBox;
            int overhang = layout.Px(DeckImageLayout.WildVineOverhang);
            int intrusion = layout.Px(DeckImageLayout.WildVineInset);
            check(layout.Width == plain.Width && layout.Height == plain.Height && layout.Columns == plain.Columns
                && layout.InfoBox.Equals(plain.InfoBox) && layout.TitleBox.Equals(plain.TitleBox)
                && layout.CardPixelWidth == plain.CardPixelWidth && layout.CardPixelHeight == plain.CardPixelHeight
                && layout.Cells.Select(cell => (cell.X, cell.Y, cell.Width, cell.Height, cell.Caption))
                    .SequenceEqual(plain.Cells.Select(cell => (cell.X, cell.Y, cell.Width, cell.Height, cell.Caption))),
                "Wild decoration leaves the complete image dimensions, card sizes and every card coordinate unchanged.");
            check(frame.Width == plain.InfoBox.Width && frame.Height == plain.InfoBox.Height
                && layout.Width == plain.Width && layout.TitleBox.Width == plain.TitleBox.Width
                && layout.TitleBox.Y - frame.Y == plain.TitleBox.Y - plain.InfoBox.Y
                && layout.Panels.Select(panel => (panel.Option, panel.X - frame.X, panel.Y - frame.Y, panel.Width, panel.Height))
                    .SequenceEqual(plain.Panels.Select(panel => (panel.Option, panel.X - plain.InfoBox.X,
                        panel.Y - plain.InfoBox.Y, panel.Width, panel.Height))),
                "Wild decoration never enlarges the shield or changes its internal title and statistics layout.");
            if (!layout.HasHeader) continue; // Empty shields have no vines or reserved border.
            var boxes = layout.Panels.Select(p => (p.X, p.Width)).Append((layout.TitleBox.X, layout.TitleBox.Width));
            check(boxes.All(box => box.X >= frame.X + intrusion && box.X + box.Width <= frame.X + frame.Width - intrusion)
                && frame.Y - overhang >= 0
                && layout.Cells[0].Y > frame.Y + frame.Height + overhang,
                "Every Wild format and statistics combination reserves vine space outside all content and cards.");
        }
    }
}
