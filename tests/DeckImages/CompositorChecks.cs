using BetterCollectionManager;
using UnityEngine;
using Ink = BetterCollectionManager.DeckImageCanvas.Ink;

internal static class CompositorChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var paper = new Ink(190, 160, 110);
        bool Same(Ink a, Ink b, int tolerance = 0) => Math.Abs(a.R - b.R) <= tolerance
            && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance;
        foreach (int height in new[] { 40, 41, 102 })
        foreach (var glyphs in new[] { (Bottom: 5, Top: 24), (Bottom: 12, Top: 22), (Bottom: 0, Top: 31) })
        {
            const int width = 24;
            var pixels = Enumerable.Repeat(new Color32(0, 0, 0, 255), width * height).ToArray();
            // The broad dark outline and asymmetric native mesh padding must
            // not pull the visible title away from the icon's center line.
            for (int row = 0; row < height; row++)
                for (int column = 1; column < width - 1; column++)
                    pixels[row * width + column] = new Color32(70, 70, 70, 255);
            for (int row = glyphs.Bottom; row <= glyphs.Top; row++)
                for (int column = 4; column < width - 4; column++)
                    pixels[row * width + column] = new Color32(241, 235, 220, 255);
            int shift = DeckImageCapture.CaptionVerticalShift(pixels, width, height);
            double before = (glyphs.Bottom + glyphs.Top) * .5;
            double iconCenter = (height - 1) * .5;
            double textCenter = before + shift;
            check(Math.Abs(textCenter - (before + iconCenter) * .5) <= .5,
                "The optical title adjustment applies half the geometric correction across font sizes and padding.");
        }
        check(DeckImageCapture.CaptionVerticalShift(new Color32[24 * 40], 24, 40) == 0,
            "An empty text capture has no spurious alignment offset.");
        foreach (bool linear in new[] { false, true })
        {
            var red = new Ink(210, 65, 70);
            check(Same(DeckImageCompositor.OverMasked(red, red, new Ink(0, 0, 0), new Ink(255, 255, 255), paper, linear), paper),
                "A rectangular native rune-red plane cannot tint pixels outside the original card silhouette.");
            check(Same(DeckImageCompositor.OverMasked(red, red, new Ink(0, 0, 0), new Ink(0, 0, 0), paper, linear), red),
                "Opaque sideboard pixels retain the original rune-red effect.");
            check(Same(DeckImageCompositor.Over(new Ink(0, 0, 0), new Ink(255, 255, 255), paper, linear), paper),
                "Empty capture pixels retain the exact parchment instead of a black rectangle.");
            var gold = new Ink(180, 80, 35, 0);
            check(Same(DeckImageCompositor.Over(gold, gold, paper, linear), gold),
                "Layered golden artwork is retained even when its shader writes zero alpha.");
            check(Same(DeckImageCompositor.Over(new Ink(0, 0, 0), new Ink(0, 0, 0), paper, linear), new Ink(0, 0, 0)),
                "Actual opaque black card details are preserved, rather than removed by color keying.");
        }
        check(Same(DeckImageCompositor.OverMasked(new Ink(200, 60, 80), new Ink(200, 60, 80),
            new Ink(0, 0, 0), new Ink(128, 128, 128), new Ink(100, 100, 100), false), new Ink(150, 80, 90), 1),
            "The original partial card coverage softens the edge of a fully opaque red effect plane.");
        check(Same(DeckImageCompositor.Over(new Ink(50, 10, 80), new Ink(178, 138, 208), new Ink(160, 120, 60), false), new Ink(130, 70, 110), 1),
            "Antialiased colored edges composite correctly over the paper in gamma color space.");
        check(Same(DeckImageCompositor.Over(new Ink(92, 92, 92), new Ink(205, 205, 205), new Ink(128, 128, 128), true), new Ink(128, 128, 128), 1),
            "A half-covered sRGB gray over itself retains its color in linear rendering.");

        // Reproduce padded native font output: light glyphs occupy only a
        // small fraction of the measured font rectangle, with a dark outline.
        foreach (int padding in new[] { 10, 30 })
        {
            const int sw = 100, sh = 80;
            var dark = new Ink[sw * sh];
            var light = Enumerable.Repeat(new Ink(255, 255, 255), sw * sh).ToArray();
            for (int row = padding; row < padding + 14; row++)
                for (int column = 35; column < 65; column++)
                {
                    bool face = row > padding + 1 && row < padding + 12 && column > 36 && column < 63;
                    dark[row * sw + column] = light[row * sw + column] = face ? new Ink(255, 255, 255) : new Ink(0, 0, 0);
                }
            var caption = new DeckImageCanvas(120, 60, false);
            Ink shieldBefore = caption.Pixel(0, 0);
            DeckImageCompositor.PaintCaption(caption, dark, light, sw, sh, 10, 5, 100, 50, 30, false);
            int bottomInk = caption.Height, topInk = -1;
            for (int row = 0; row < caption.Height; row++)
                for (int column = 0; column < caption.Width; column++)
                    if (caption.Pixel(column, row).R >= 180)
                    { bottomInk = Math.Min(bottomInk, row); topInk = Math.Max(topInk, row); }
            check(Math.Abs(topInk - bottomInk + 1 - 30) <= 1 && Math.Abs((topInk + bottomInk) / 2.0 - 29.5) <= 1,
                "Visible quantity glyphs reach the x2 reference height and stay centered despite native font padding.");
            check(Same(caption.Pixel(10, 5), shieldBefore), "Fitting padded quantity glyphs preserves the surrounding shield pixels.");
        }
        var target = new RenderTexture(4, 1);
        var previous = new RenderTexture(1, 1); RenderTexture.active = previous;
        var camera = new Camera { targetTexture = target, clearFlags = CameraClearFlags.Depth, backgroundColor = new Color(.2f, .4f, .6f) };
        camera.Scene = value => {
            byte light = (byte)(value.backgroundColor.r * 255);
            // Empty pixel, zero-alpha layered artwork, opaque black detail,
            // and a half-covered black glyph, respectively.
            value.targetTexture.Pixels[1] = new Color32(180, 80, 35, 0);
            value.targetTexture.Pixels[2] = new Color32(0, 0, 0, 0);
            value.targetTexture.Pixels[3] = new Color32((byte)(light / 2), (byte)(light / 2), (byte)(light / 2), 0);
        };
        var pair = DeckImageCapture.Read(camera, new Texture2D(4, 1), 4, 1);
        check(camera.Backgrounds.Count == 2 && camera.Backgrounds[0].r == 0 && camera.Backgrounds[1].r == 1,
            "The real capture helper renders both opaque backgrounds through the camera after any target discard.");
        Ink Composite(int i)
        {
            var black = pair.Black[i]; var white = pair.White[i];
            return DeckImageCompositor.Over(new Ink(black.r, black.g, black.b), new Ink(white.r, white.g, white.b), paper, false);
        }
        check(Same(Composite(0), paper) && Same(Composite(1), new Ink(180, 80, 35)) && Same(Composite(2), new Ink(0, 0, 0)),
            "A discarded pre-render seed cannot cause black tiles or remove the golden portrait.");
        check(Same(Composite(3), new Ink(95, 80, 55), 1), "Glyph edges blend into the parchment without a rectangular background.");
        check(RenderTexture.active == previous && camera.clearFlags == CameraClearFlags.Depth && camera.backgroundColor.r == .2f,
            "Capture restores the prior target, clear flags and background after both passes.");
        camera.Scene = _ => throw new InvalidOperationException("Simulated GPU error");
        try { DeckImageCapture.Read(camera, new Texture2D(4, 1), 4, 1); } catch (InvalidOperationException) { }
        check(RenderTexture.active == previous && camera.clearFlags == CameraClearFlags.Depth && camera.backgroundColor.r == .2f,
            "A rendering failure also restores camera and render-target state.");

        const int sourceWidth = 180, sourceHeight = 36;
        var slate = new Color32[sourceWidth * sourceHeight];
        for (int y = 0; y < sourceHeight; y++)
            for (int x = 0; x < sourceWidth; x++)
            {
                byte gray = x < 4 || x >= sourceWidth - 4 || y < 4 || y >= sourceHeight - 4 ? (byte)100
                    : y < sourceHeight / 2 ? (byte)12 : (byte)56;
                slate[y * sourceWidth + x] = new Color32(gray, gray, gray, 255);
            }
        var onWhite = (Color32[])slate.Clone();
        slate[0] = new Color32(0, 0, 0, 0);
        onWhite[0] = new Color32(255, 255, 255, 0);
        var frame = new DeckImageCapture.Pair(slate, onWhite);
        var panel = new DeckImageCanvas(460, 140, true);
        var untouched = panel.Pixel(7, 7);
        check(DeckImageCapture.PaintQuantityFrame(frame, sourceWidth, sourceHeight, panel, 7, 7, 425, 125, false)
            && Same(panel.Pixel(9, 60), new Ink(100, 100, 100))
            && Same(panel.Pixel(40, 25), new Ink(56, 56, 56))
            && Same(panel.Pixel(7, 7), untouched),
            "The native frame keeps its bevel and transparent corner while the slate fills the old input strip.");
        Array.Fill(slate, new Color32(0, 0, 0, 255));
        Array.Fill(onWhite, new Color32(0, 0, 0, 255));
        check(!DeckImageCapture.PaintQuantityFrame(frame, sourceWidth, sourceHeight, panel, 7, 7, 425, 125, false)
            && Same(panel.Pixel(40, 25), new Ink(56, 56, 56)),
            "An opaque black drop-shadow plate is rejected before it can overwrite the panel.");
        Array.Fill(slate, new Color32(56, 56, 56, 255));
        Array.Fill(onWhite, new Color32(56, 56, 56, 255));
        check(!DeckImageCapture.PaintQuantityFrame(frame, sourceWidth, sourceHeight, panel, 7, 7, 425, 125, false),
            "A plain gray plate is not mistaken for a frame with metal edges.");

        const int wellWidth = 28, wellHeight = 80, stride = 30;
        var track = new Color32[wellWidth * wellHeight];
        Array.Fill(track, new Color32(29, 31, 33, 255));
        var wellCanvas = new DeckImageCanvas(90, 90, true);
        check(!DeckImageCapture.PaintManaWells(new DeckImageCapture.Pair(track, track), wellCanvas,
            1, 1, wellWidth, wellHeight, stride, 2, false), "A flat dark tile cannot be selected as the native mana recess.");
        for (int row = 0; row < wellHeight; row++)
            foreach (int column in new[] { 3, wellWidth - 4 })
                track[row * wellWidth + column] = new Color32(92, 92, 92, 255);
        check(DeckImageCapture.PaintManaWells(new DeckImageCapture.Pair(track, track), wellCanvas,
            1, 1, wellWidth, wellHeight, stride, 2, false)
            && Same(wellCanvas.Pixel(4, 30), new Ink(92, 92, 92))
            && Same(wellCanvas.Pixel(1 + stride + wellWidth / 2, 30), new Ink(29, 31, 33)),
            "A native tile with two rails and a recessed center is repeated behind the mana fills.");

        var portrait = new Texture { Pixels = new[] { new Color32(180, 80, 35, 255), new Color32(20, 40, 60, 255) } };
        var portraitTarget = new RenderTexture(2, 1); var portraitPixels = new Texture2D(2, 1);
        int copies = Graphics.Copies;
        var colors = DeckImageCapture.ReadArtwork(portrait, portraitTarget, portraitPixels, 2, 1);
        check(colors[0].r == 180 && colors[1].b == 60 && Graphics.Copies == copies + 1,
            "Native portrait readback uses the material-free scale/offset overload; the player rejects a null material.");
        check(RenderTexture.active == previous, "Artwork readback restores the previous render target.");
        var iconSource = new Texture { Pixels = new[] { new Color32(180, 80, 35, 0), new Color32(20, 40, 60, 128) } };
        var icon = DeckImageCapture.ReadTexture(iconSource, portraitTarget, portraitPixels, 2, 1,
            new Vector2(.15f, .4f), new Vector2(.55f, .3f));
        check(Graphics.LastScale.x == .15f && Graphics.LastScale.y == .4f
            && Graphics.LastOffset.x == .55f && Graphics.LastOffset.y == .3f && icon[0].a == 0 && icon[1].a == 128,
            "Native class atlas crops reach the material-free copy path with their alpha intact.");
        var iconCanvas = new DeckImageCanvas(2, 1, false);
        var emptyBefore = iconCanvas.Pixel(0, 0);
        for (int i = 0; i < icon.Length; i++) iconCanvas.Blend(i, 0, icon[i].r, icon[i].g, icon[i].b, icon[i].a);
        check(Same(iconCanvas.Pixel(0, 0), emptyBefore)
            && Same(iconCanvas.Pixel(1, 0), new Ink(20, 33, 50), 1),
            "Transparent class pixels leave the background intact while partial-alpha edges blend without rectangular tiles.");
        Graphics.Fail = true;
        try { DeckImageCapture.ReadArtwork(portrait, portraitTarget, portraitPixels, 2, 1); } catch (InvalidOperationException) { }
        check(RenderTexture.active == previous, "A failed artwork copy also restores the render target.");
        Graphics.Fail = false; Texture2D.FailRead = true;
        try { DeckImageCapture.ReadArtwork(portrait, portraitTarget, portraitPixels, 2, 1); } catch (InvalidOperationException) { }
        check(RenderTexture.active == previous, "A failed portrait readback restores the render target.");
        Texture2D.FailRead = false;
        RenderTexture.active = null;
    }
}
