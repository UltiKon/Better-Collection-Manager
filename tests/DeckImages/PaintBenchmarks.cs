using System.Diagnostics;
using BetterCollectionManager;
using UnityEngine;

internal static class PaintBenchmarks
{
    internal static void Run()
    {
        const int width = 600, height = 840;
        var black = new Color32[width * height];
        var white = Enumerable.Repeat(new Color32(255, 255, 255, 255), width * height).ToArray();
        for (int y = 40; y < 800; y++)
            for (int x = 28; x < 572; x++)
                black[y * width + x] = white[y * width + x] = new Color32((byte)(80 + x % 120), 100, 60, 255);
        var normal = new DeckImageCapture.Pair(black, white);
        var red = (Color32[])black.Clone();
        for (int i = 0; i < red.Length; i++) red[i] = new Color32(180, 70, 60, 255);
        var styled = new DeckImageCapture.Pair(red, (Color32[])red.Clone());
        foreach (bool masked in new[] { false, true })
        {
            var frame = new DeckImageCardFrame(masked ? styled : normal, normal, width, height, 544, true);
            var bounds = DeckImageCardFrame.Footprint(new[] { frame });
            // Fractional scale exercises bilinear sampling as well as opaque interiors.
            var cell = new DeckImageLayout.Cell(10, 10, bounds.Width + 1, bounds.Height + 1, 108);
            var canvas = new DeckImageCanvas(640, 920, false);
            frame.Paint(canvas, cell, bounds);
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 20; i++) frame.Paint(canvas, cell, bounds);
            Console.WriteLine($"20 full-size {(masked ? "sideboard" : "normal")} cards: {watch.Elapsed.TotalMilliseconds:F0} ms");
        }
        var bitmap = new DeckImageCanvas(3200, 4400, false);
        var bitmapWatch = Stopwatch.StartNew();
        byte[] dib = DeckImageBitmap.ToDib(bitmap.Rgba, bitmap.Width, bitmap.Height);
        Console.WriteLine($"3200 x 4400 clipboard bitmap: {bitmapWatch.Elapsed.TotalMilliseconds:F0} ms ({dib.Length} bytes)");
    }
}
