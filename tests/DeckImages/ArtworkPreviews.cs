using BetterCollectionManager;

// Optional visual review of the production background painter. Input files are
// little-endian width/height followed by top-down RGB, without card frames.
internal static class ArtworkPreviews
{
    internal static void Run(string folder)
    {
        foreach (string path in Directory.GetFiles(folder, "*.rgb"))
        {
            using var input = new BinaryReader(File.OpenRead(path));
            int width = input.ReadInt32(), height = input.ReadInt32();
            byte[] rgb = input.ReadBytes(checked(width * height * 3));
            if (rgb.Length != width * height * 3) throw new InvalidDataException(path);
            var sample = new DeckImageCanvas.Ink[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int source = (y * width + x) * 3;
                    sample[(height - 1 - y) * width + x] = new DeckImageCanvas.Ink(rgb[source], rgb[source + 1], rgb[source + 2]);
                }
            foreach (var format in new[] { (Name: "Landscape", Width: 720, Height: 480), (Name: "Square", Width: 480, Height: 480), (Name: "Portrait", Width: 480, Height: 720) })
            {
                var canvas = new DeckImageCanvas(format.Width, format.Height, false);
                canvas.BlurredArtwork(sample, width, height);
                File.WriteAllBytes(path + "." + format.Name + ".png", DeckImagePng.Encode(canvas));
            }
        }
        Console.WriteLine("Rendered artwork previews from production code: " + folder);
    }
}
