using BetterCollectionManager;

if (Environment.GetEnvironmentVariable("BCM_DECK_IMAGE_VINE_FIXTURE") is string vineFixture)
{
    using var input = new BinaryReader(File.OpenRead(vineFixture));
    int cw = input.ReadInt32(), ch = input.ReadInt32(), sw = input.ReadInt32(), sh = input.ReadInt32();
    int x = input.ReadInt32(), y = input.ReadInt32(), width = input.ReadInt32(), height = input.ReadInt32();
    int size = input.ReadInt32(), inset = input.ReadInt32(), overhang = input.ReadInt32();
    var originalPanel = new UnityEngine.Rect(input.ReadSingle(), input.ReadSingle(), input.ReadSingle(), input.ReadSingle());
    bool linear = input.ReadBoolean();
    var canvas = new DeckImageCanvas(cw, ch, false);
    input.ReadBytes(canvas.Rgba.Length).CopyTo(canvas.Rgba, 0);
    UnityEngine.Color32[] Pixels() => Enumerable.Range(0, sw * sh)
        .Select(_ => new UnityEngine.Color32(input.ReadByte(), input.ReadByte(), input.ReadByte(), input.ReadByte())).ToArray();
    var pair = new DeckImageCapture.Pair(Pixels(), Pixels());
    if (!DeckImageCapture.PaintWildFrame(pair, sw, sh, canvas, x, y, width, height, size, inset, overhang, originalPanel, linear))
        throw new InvalidOperationException("The native vine fixture is empty.");
    File.WriteAllBytes(vineFixture + ".png", DeckImagePng.Encode(canvas));
    Console.WriteLine("Rendered vine fixture through the production compositor: " + vineFixture + ".png");
    return;
}

if (Environment.GetEnvironmentVariable("BCM_DECK_IMAGE_BLUR_FIXTURES") is string artworkFixtures)
{
    ArtworkPreviews.Run(artworkFixtures);
    return;
}

if (Environment.GetEnvironmentVariable("BCM_DECK_IMAGE_PNG_FIXTURE") is string fixture)
{
    using var reader = new BinaryReader(File.OpenRead(fixture));
    int width = reader.ReadInt32(), height = reader.ReadInt32();
    var fixtureCanvas = new DeckImageCanvas(width, height, false);
    byte[] rgba = reader.ReadBytes(width * height * 4);
    Array.Copy(rgba, fixtureCanvas.Rgba, rgba.Length);
    var watch = System.Diagnostics.Stopwatch.StartNew();
    byte[] png = System.Threading.Tasks.Task.Run(() => DeckImagePng.Encode(fixtureCanvas)).GetAwaiter().GetResult();
    File.WriteAllBytes(fixture + ".png", png);
    Console.WriteLine($"Worker PNG: {width}x{height}, {watch.Elapsed.TotalMilliseconds:F0} ms, {png.Length} bytes");
    return;
}

if (Environment.GetEnvironmentVariable("BCM_DECK_IMAGE_BENCHMARK") == "1")
{
    PaintBenchmarks.Run();
    return;
}

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}
void Throws(Action action, string message)
{
    try { action(); } catch (ArgumentException) { checks++; return; } catch (InvalidOperationException) { checks++; return; }
    throw new Exception(message);
}
PngChecks.Run(Check);
ArtworkChecks.Run(Check);
CompositorChecks.Run(Check);
WildVineChecks.Run(Check);
CardFrameChecks.Run(Check);
var labelCanvas = new DeckImageCanvas(320, 210, false);
for (int labelCount = 2; labelCount <= 9; labelCount++)
    Check(DeckImageQuantityLabel.Paint(labelCanvas, labelCount, 10, 10, 250, 142),
        "Every DeckView quantity label from x2 through x9 decodes and renders.");
Check(DeckImageQuantityLabel.Paint(labelCanvas, 12, 10, 10, 250, 142),
    "Multi-digit counts use the same reference border without a native shield fallback.");
foreach (DeckImageAspect aspect in Enum.GetValues<DeckImageAspect>())
foreach (int total in new[] { 1, 19, 30, 40, 65 })
for (int flags = 0; flags < 64; flags++)
{
    var options = DeckImageSettings.Options.Where((option, bit) => (flags & (1 << bit)) != 0).Aggregate(DeckImageOption.None, (value, option) => value | option);
    DeckImageLayout layout;
    try { layout = new DeckImageLayout(total, 8192, options, aspect); }
    catch (InvalidOperationException error)
    {
        Check(total > 30 && error.Message.Contains("texture limit"), "Oversized decks fail at the texture limit without shrinking cards.");
        continue;
    }
    Check(layout.Cells.Count == total, "Every distinct card has a cell.");
    Check(layout.Columns >= 1 && layout.Columns <= total && layout.CardPixelWidth == 544,
        "Formats automatically choose columns while preserving large card bodies.");
    Check(layout.Cells.GroupBy(c => c.Y).All(row => row.Count() <= layout.Columns)
        && layout.Height == layout.Cells.Last().Y + layout.Cells.Last().Height + layout.CaptionPixels + (int)Math.Round(DeckImageLayout.Margin * 1.4),
        "The canvas ends after the last card row and its margin.");
    var rectangles = layout.Cells.Select(c => (c.X, c.Y, c.Width, Height: c.Height + c.Caption))
        .Concat(layout.Panels.Select(p => (p.X, p.Y, p.Width, p.Height))).Append(layout.TitleBox).ToArray();
    foreach (var box in rectangles)
        Check(box.X >= 0 && box.Y >= 0 && box.X + box.Width <= layout.Width && box.Y + box.Height <= layout.Height,
            "Every card, count and statistics panel fits within the export.");
    for (int i = 0; i < rectangles.Length; i++)
    for (int j = i + 1; j < rectangles.Length; j++)
    {
        var a = rectangles[i]; var b = rectangles[j];
        Check(a.X + a.Width <= b.X || b.X + b.Width <= a.X || a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y,
            $"Cards, captions and panels never overlap: {aspect}, {total}, {flags}: {a} / {b}.");
    }
    var lastRow = layout.Cells.Where(c => c.Y == layout.Cells.Last().Y).ToArray();
    Check(Math.Abs(lastRow.First().X + lastRow.Last().X + lastRow.Last().Width - layout.Width) <= 1,
        "Partial rows stay centered in their card area.");
    bool hasContent = (options & ~DeckImageOption.Background) != DeckImageOption.None;
    Check(layout.HasHeader == hasContent, "A shield exists only when at least one header item is enabled.");
    if (!hasContent)
        Check(layout.InfoBox.Width == 0 && layout.TitleBox.Width == 0
            && layout.Cells[0].Y == (int)Math.Round(DeckImageLayout.Margin * 1.4)
            && layout.Width == layout.Columns * layout.Cells[0].Width
                + (layout.Columns - 1) * (int)Math.Round(DeckImageLayout.Gap * 1.4)
                + 2 * (int)Math.Round(DeckImageLayout.Margin * 1.4),
            "An empty shield reserves no horizontal or vertical space around the unchanged card grid.");
    var frame = layout.InfoBox;
    var title = layout.TitleBox;
    Check(!layout.HasHeader || layout.Cells[0].Y > frame.Y + frame.Height && title.Y >= frame.Y && title.X >= frame.X
        && title.X + title.Width <= frame.X + frame.Width && title.Y + title.Height <= frame.Y + frame.Height,
        "The framed title stays above the cards.");
    Check(layout.Panels.All(p => p.Y >= frame.Y && p.Y + p.Height <= frame.Y + frame.Height
        && p.X >= frame.X && p.X + p.Width <= frame.X + frame.Width), "All statistics share the title's frame.");
    Check(!layout.HasHeader || layout.Cells[0].Y - frame.Y - frame.Height <= layout.Px(16) + layout.Px(24),
        "The card grid starts close to the info frame even when the last row leaves room below.");
}
Throws(() => new DeckImageLayout(new[] { 0 }, 8192), "Reject empty decks.");
Throws(() => new DeckImageLayout(new[] { 241 }, 8192), "Reject excessive images before allocating textures.");
Throws(() => new DeckImageLayout(new[] { 30 }, 1024), "Respect GPU texture limits.");
Throws(() => new DeckImageLayout(new[] { -1 }, 8192), "Reject invalid counts.");
var landscapeDeck = new DeckImageLayout(19, 8192, DeckImageSettings.Capture(), DeckImageAspect.Landscape);
Check(landscapeDeck.CardPixelWidth >= 275,
    "A 19-card landscape deck retains large cards with the compact header.");
Check(landscapeDeck.Cells.Select(cell => cell.Y).Distinct().Count() == 3,
    "The landscape layout reserves three rows instead of reverting to two rows.");
var tallerDeck = new DeckImageLayout(30, 8192, DeckImageSettings.Capture(), DeckImageAspect.Landscape);
Check(tallerDeck.Columns > landscapeDeck.Columns && tallerDeck.Width > landscapeDeck.Width
    && tallerDeck.Height > landscapeDeck.Height && tallerDeck.CardPixelWidth == landscapeDeck.CardPixelWidth,
    "More distinct cards automatically increase columns and canvas dimensions without shrinking cards.");
foreach (DeckImageAspect format in Enum.GetValues<DeckImageAspect>())
{
    var smaller = new DeckImageLayout(12, 8192, DeckImageSettings.Capture(), format);
    var larger = new DeckImageLayout(40, 8192, DeckImageSettings.Capture(), format);
    Check(larger.Columns > smaller.Columns && smaller.CardPixelWidth == larger.CardPixelWidth,
        "Every format adapts its columns to the number of distinct cards.");
}
var portraitDeck = new DeckImageLayout(30, 8192, DeckImageSettings.Capture(), DeckImageAspect.Portrait);
var squareDeck = new DeckImageLayout(30, 8192, DeckImageSettings.Capture(), DeckImageAspect.Square);
Check(portraitDeck.Columns < squareDeck.Columns && squareDeck.Columns < tallerDeck.Columns
    && portraitDeck.Width < portraitDeck.Height && tallerDeck.Width > tallerDeck.Height,
    "Portrait, Square and Landscape select progressively wider grids for the same card count.");
Check(new DeckImageLayout(19, 8192, DeckImageSettings.Capture(), DeckImageAspect.Portrait).Width < landscapeDeck.Width,
    "Fewer cards per row create a narrower canvas with the same card size.");
var shortHeader = new DeckImageLayout(19, 8192, DeckImageSettings.Capture(), DeckImageAspect.Landscape, titleTextWidth: 180);
var longHeader = new DeckImageLayout(19, 8192, DeckImageSettings.Capture(), DeckImageAspect.Landscape, titleTextWidth: 620);
var titleOnly = new DeckImageLayout(19, 8192, DeckImageOption.Details, DeckImageAspect.Landscape, titleTextWidth: 180);
Check(shortHeader.InfoBox.Width < longHeader.InfoBox.Width && shortHeader.InfoBox.Height == longHeader.InfoBox.Height
    && shortHeader.Width == longHeader.Width && shortHeader.Height == longHeader.Height,
    "The frame fits the measured title width without resizing the exported image.");
Check(titleOnly.InfoBox.Width < shortHeader.InfoBox.Width && titleOnly.InfoBox.Height < shortHeader.InfoBox.Height,
    "The frame contracts in both dimensions when only the title and class symbol are enabled.");
foreach (DeckImageAspect aspect in Enum.GetValues<DeckImageAspect>())
{
    DeckImageSettings.Aspect = aspect;
    typeof(DeckImageSettings).GetField("s_loaded", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.SetValue(null, false);
    Check(DeckImageSettings.Aspect == aspect, "Format survives preference reloading.");
}
DeckImageSettings.Aspect = DeckImageAspect.Portrait;

// Resolution scales the actual render layout, including the header and cards.
var headerOptions = DeckImageSettings.Capture();
Check(DeckImageSettings.Resolution == DeckImageResolution.Medium, "The default preset uses the requested 1080 short edge.");
var baseLandscape = new DeckImageLayout(19, 8192, headerOptions, DeckImageAspect.Landscape);
foreach (DeckImageAspect format in Enum.GetValues<DeckImageAspect>())
{
    var full = new DeckImageLayout(19, 8192, headerOptions, format);
    if (format != DeckImageAspect.Landscape)
        Check(full.Px(DeckImageLayout.HeaderFontSize) == baseLandscape.Px(DeckImageLayout.HeaderFontSize)
            && full.CardPixelWidth == baseLandscape.CardPixelWidth && full.Cells[0].Width == baseLandscape.Cells[0].Width,
            "All formats keep the same shield-to-card proportions instead of making the cards look smaller beside an enlarged header.");
    foreach (DeckImageResolution resolution in Enum.GetValues<DeckImageResolution>())
    {
        var scaled = DeckImageLayout.ForExport(19, 8192, headerOptions, format, 5, 5, false, resolution);
        double scale = scaled.ResolutionScale;
        int target = DeckImageSettings.ResolutionPixels(resolution);
        Check(target == 0 ? scaled.Width == full.Width && scaled.Height == full.Height
            : Math.Abs(Math.Min(scaled.Width, scaled.Height) - Math.Min(target, Math.Min(full.Width, full.Height))) <= 12,
            "Named presets target the shorter image edge while Max preserves full dimensions.");
        Check(scaled.Columns == full.Columns && scaled.CardPixelWidth == (int)Math.Round(full.CardPixelWidth * scale)
            && Math.Abs(scaled.Width - full.Width * scale) <= 10 && Math.Abs(scaled.Height - full.Height * scale) <= 20
            && Math.Abs(scaled.InfoBox.Height - full.InfoBox.Height * scale) <= 10,
            $"Image quality changes render resolution uniformly: {format}/{resolution} full={full.Width}x{full.Height}, scaled={scaled.Width}x{scaled.Height}, header={full.InfoBox.Height}/{scaled.InfoBox.Height}.");
        DeckImageSettings.Resolution = resolution;
        typeof(DeckImageSettings).GetField("s_loaded", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.SetValue(null, false);
        Check(DeckImageSettings.Resolution == resolution, "Image resolution persists independently from card premium quality.");
    }
}
DeckImageSettings.Resolution = DeckImageResolution.Max;
var reducedLargeDeck = DeckImageLayout.ForExport(240, 2048, headerOptions, DeckImageAspect.Square, 5, 5, false, DeckImageResolution.Low);
Check(reducedLargeDeck.Width <= 2048 && reducedLargeDeck.Height <= 2048
    && Math.Abs(Math.Min(reducedLargeDeck.Width, reducedLargeDeck.Height) - 720) <= 24,
    "Low exports a deck whose original canvas would exceed the GPU limit without allocating that canvas.");
var smallOriginal = new DeckImageLayout(1, 8192, DeckImageOption.None);
var highSmall = DeckImageLayout.ForExport(1, 8192, DeckImageOption.None, DeckImageAspect.Portrait, 5, 5, false, DeckImageResolution.High);
Check(highSmall.Width == smallOriginal.Width && highSmall.Height == smallOriginal.Height,
    "A preset never upscales an original that is already smaller than its target.");
Throws(() => new DeckImageLayout(19, 8192, headerOptions, resolutionScale: double.NaN), "Reject invalid image resolution.");
Throws(() => new DeckImageLayout(19, 1024, headerOptions, resolutionScale: 1.5), "High resolution honors the GPU limit before allocating pixels.");

// Quality is a single coordinate transform of the same reference layout.
foreach (DeckImageAspect format in Enum.GetValues<DeckImageAspect>())
foreach (int count in new[] { 1, 19, 30 })
{
    var original = DeckImageLayout.ForExport(count, 8192, headerOptions, format, 3, 3, true, DeckImageResolution.Max, titleTextWidth: 390);
    foreach (DeckImageResolution resolution in Enum.GetValues<DeckImageResolution>())
    {
        var scaled = DeckImageLayout.ForExport(count, 8192, headerOptions, format, 3, 3, true, resolution, titleTextWidth: 390);
        int Pixel(int value) => (int)Math.Round(value * scaled.ResolutionScale);
        (int X, int Y, int Width, int Height) Box((int X, int Y, int Width, int Height) box)
            => (Pixel(box.X), Pixel(box.Y), Pixel(box.X + box.Width) - Pixel(box.X), Pixel(box.Y + box.Height) - Pixel(box.Y));
        Check(scaled.InfoBox == Box(original.InfoBox) && scaled.TitleBox == Box(original.TitleBox)
            && scaled.Columns == original.Columns && scaled.Cells.Count == original.Cells.Count,
            "Every quality uses the same measured title, header and card arrangement at scaled coordinates.");
        for (int i = 0; i < original.Cells.Count; i++)
        {
            var a = original.Cells[i]; var b = scaled.Cells[i];
            Check((b.X, b.Y, b.Width, b.Height) == Box((a.X, a.Y, a.Width, a.Height)),
                "Quality never independently recomputes a card's position or size.");
        }
        for (int i = 0; i < original.Panels.Count; i++)
        {
            var a = original.Panels[i]; var b = scaled.Panels[i];
            Check((b.X, b.Y, b.Width, b.Height) == Box((a.X, a.Y, a.Width, a.Height)),
                "Statistics keep identical placement relative to the shield across qualities.");
        }
    }
}
var readablePortrait = DeckImageLayout.ForExport(19, 8192, headerOptions, DeckImageAspect.Portrait, 3, 3, false, DeckImageResolution.Medium);
var readableSquare = DeckImageLayout.ForExport(19, 8192, headerOptions, DeckImageAspect.Square, 3, 3, false, DeckImageResolution.Medium);
Check(readablePortrait.Columns == 4 && readableSquare.Columns == 5
    && readablePortrait.CardPixelWidth >= 210 && readableSquare.CardPixelWidth >= 175
    && readablePortrait.Height > readablePortrait.Width && readableSquare.Height > readableSquare.Width,
    "The supplied 19-card deck keeps larger cards in Portrait/Square by using more rows instead of squeezing more columns.");

// Two one-pixel rows expose channel order, bottom-up order and row padding.
byte[] dib = DeckImageBitmap.ToDib(new byte[] { 255, 0, 0, 255, 0, 0, 255, 255 }, 1, 2);
using (var reader = new BinaryReader(new MemoryStream(dib)))
{
    Check(reader.ReadInt32() == 40 && reader.ReadInt32() == 1 && reader.ReadInt32() == 2, "CF_DIB has a BITMAPINFOHEADER and positive height.");
    Check(reader.ReadInt16() == 1 && reader.ReadInt16() == 24 && reader.ReadInt32() == 0, "Legacy bitmap is uncompressed 24-bit BGR.");
    Check(reader.ReadInt32() == 8 && dib.Length == 48, "Bitmap rows are aligned to four bytes.");
}
Check(dib.Skip(40).SequenceEqual(new byte[] { 0, 0, 255, 0, 255, 0, 0, 0 }), "Bottom-left RGBA becomes bottom-up padded BGR without flipping rows.");
Throws(() => DeckImageBitmap.ToDib(new byte[3], 1, 1), "Reject truncated pixels.");

var deck = new CollectionDeck();
var dynamicDef = new EntityDef { Name = "Dynamic Zilliax", Cost = 5 };
deck.Slots.Add(new CollectionDeckSlot("ZILLIAX", dynamicDef, TAG_PREMIUM.NORMAL, 1));
deck.Slots.Add(new CollectionDeckSlot("SPELL", new EntityDef { Name = "Spell", Cost = 1 }, TAG_PREMIUM.NORMAL, 1));
deck.Slots.Add(new CollectionDeckSlot("SPELL", new EntityDef { Name = "Spell", Cost = 1 }, TAG_PREMIUM.NORMAL, 1));
deck.Slots.Add(new CollectionDeckSlot("SPELL", new EntityDef { Name = "Spell", Cost = 1 }, TAG_PREMIUM.GOLDEN, 1));
deck.Slots.Add(new CollectionDeckSlot("REPRINT", new EntityDef { Name = "Spell", Cost = 1 }, TAG_PREMIUM.NORMAL, 1));
deck.Slots.Add(null);
deck.Slots.Add(new CollectionDeckSlot("EMPTY", null, TAG_PREMIUM.NORMAL, 0));
var sideboard = new SideboardDeck();
sideboard.Slots.Add(new CollectionDeckSlot("SPELL", new EntityDef { Name = "Spell", Cost = 1 }, TAG_PREMIUM.SIGNATURE, 2));
deck.Sideboards["OWNER"] = sideboard;
deck.Sideboards["EMPTY_OWNER"] = new SideboardDeck();
var snapshot = DeckImageSnapshot.Capture(deck);
Check(snapshot.Count == 2 && snapshot[1].Title == "Sideboard: OWNER", "Keep sideboards separate and omit empty ones.");
Check(snapshot[0].Cards.Count == 4 && snapshot[0].Cards.Sum(card => card.Count) == 5, "Aggregate slots while preserving quantities and exact editions.");
Check(snapshot[0].Cards.Single(card => card.Id == "SPELL" && card.Premium == TAG_PREMIUM.NORMAL).Count == 2, "Owned and missing slots can merge by card and quality.");
Check(snapshot[0].Cards.Last().Id == "ZILLIAX", "Sort by native mana cost.");
Check(snapshot[1].Cards.Single().Premium == TAG_PREMIUM.SIGNATURE && snapshot[1].Cards.Single().Count == 2, "Preserve sideboard quality and quantity.");
foreach (DeckImageQuality quality in Enum.GetValues<DeckImageQuality>().Where(value => value != DeckImageQuality.Deck))
{
    TAG_PREMIUM expected = quality == DeckImageQuality.Highest ? TAG_PREMIUM.NORMAL : Enum.Parse<TAG_PREMIUM>(quality.ToString().ToUpperInvariant());
    var forced = DeckImageSnapshot.Capture(deck, false, quality);
    Check(forced.SelectMany(section => section.Cards).All(card => card.Premium == expected), "Chosen export quality applies to every main-deck and sideboard card.");
    Check(forced[0].Cards.Single(card => card.Id == "SPELL").Count == 3 && forced[0].Cards.Count == 3,
        "Forcing one quality merges mixed-quality copies without merging distinct editions.");
    Check(forced[1].Cards.Single().Count == 2, "Forced sideboard qualities preserve quantities.");
    Check(forced.SelectMany(section => section.Cards).All(card => card.HighestQuality == (quality == DeckImageQuality.Highest)),
        "Highest aggregates quantities while deferring per-card variant selection to its loaded native definition.");
}
Check(deck.Slots.Single(card => card?.CardID == "SPELL" && card.GetCount(TAG_PREMIUM.GOLDEN) > 0).GetCount(TAG_PREMIUM.GOLDEN) == 1,
    "Choosing an export quality never changes the edited deck.");
dynamicDef.Cost = 99; deck.Slots.Clear(); sideboard.Slots.Clear();
Check(snapshot[0].Cards.Last().Definition.Cost == 5 && snapshot[1].Cards.Single().Count == 2, "Freeze dynamic definitions and contents at click time.");
deck.Slots.Add(new CollectionDeckSlot("LOADING", null, TAG_PREMIUM.NORMAL, 1));
Throws(() => DeckImageSnapshot.Capture(deck), "Missing definitions fail visibly instead of silently dropping cards.");

var mainStats = new DeckImageSnapshot.Section { Title = "Main deck" };
var sideStats = new DeckImageSnapshot.Section { OwnerId = "B", OwnerName = "Owner", Title = "Sideboard: Owner" };
DeckImageSnapshot.Card StatCard(string id, int cost, int count, TAG_CARDTYPE type, TAG_RARITY rarity, int? dust, bool uncraftable = false)
    => new() { Id = id, Definition = new EntityDef { Cost = cost, Type = type, Rarity = rarity, Name = id }, Count = count,
        NormalCraftCost = dust, Uncraftable = uncraftable };
mainStats.Cards.Add(StatCard("A", 0, 2, TAG_CARDTYPE.MINION, TAG_RARITY.COMMON, 37));
mainStats.Cards.Add(StatCard("B", 12, 1, TAG_CARDTYPE.SPELL, TAG_RARITY.LEGENDARY, 1000));
mainStats.Cards.Add(StatCard("C", 3, 1, TAG_CARDTYPE.WEAPON, TAG_RARITY.FREE, null, true));
mainStats.Cards.Add(StatCard("D", 5, 1, TAG_CARDTYPE.LOCATION, TAG_RARITY.EPIC, null));
sideStats.Cards.Add(StatCard("SIDE", 20, 3, TAG_CARDTYPE.HERO, TAG_RARITY.RARE, 10));
var statSections = new List<DeckImageSnapshot.Section> { mainStats, sideStats };
var stats = new DeckImageStatistics(statSections);
Check(DeckImageSnapshot.BackgroundCard(statSections).Id == "B", "Background selection prioritizes main-deck rarity and ignores a more expensive sideboard card.");
mainStats.Cards.Add(StatCard("AAA", 12, 1, TAG_CARDTYPE.MINION, TAG_RARITY.LEGENDARY, 1000));
Check(DeckImageSnapshot.BackgroundCard(statSections).Id == "AAA", "Equal rarity and cost use a deterministic card-ID tie break.");
mainStats.Cards.RemoveAt(mainStats.Cards.Count - 1);
Check(stats.MainCopies == 5 && stats.SideboardCopies == 3 && stats.AverageMana == "4.0", "Deck totals and weighted average exclude sideboards.");
Check(stats.Mana.Sum() == 5 && stats.Mana[0] == 2 && stats.Mana[10] == 1, "Mana curve uses quantities, includes zero and groups ten-plus costs.");
var nativeBins = DeckImageStatistics.NativeManaBins(new[] { 2, 0, 1, 0, 0, 0, 3, 1, 2, 3, 4 });
Check(nativeBins.SequenceEqual(new[] { 2, 0, 1, 0, 0, 0, 3, 10 }), "Native mana bars use 0 through 7+, retaining every expensive copy.");
Check(stats.Types.Values.Sum() == 5 && !stats.Types.ContainsKey(TAG_CARDTYPE.HERO) && !stats.Rarities.ContainsKey(TAG_RARITY.RARE), "Type and rarity counts exclude sideboards.");
Check(stats.CraftingDust == 1104, "Native prices are weighted by copies and include sideboards.");
Check(stats.UncraftableCopies == 1 && stats.UnknownPriceCopies == 1, "Unavailable prices and uncraftable cards are distinct from free cards.");
var ordered = DeckImageSnapshot.OrderedCards(statSections);
Check(ordered.Select(card => card.Id).SequenceEqual(new[] { "A", "B", "SIDE", "C", "D" }), "Sideboards follow their owner directly in the card grid.");
var beatrixSections = new List<DeckImageSnapshot.Section>
{
    new DeckImageSnapshot.Section(),
    new DeckImageSnapshot.Section { OwnerId = "JAIL_397", OwnerName = "Commander Beatrix" }
};
beatrixSections[0].Cards.Add(StatCard("JAIL_397", 5, 1, TAG_CARDTYPE.MINION, TAG_RARITY.LEGENDARY, 1600));
beatrixSections[1].Cards.Add(StatCard("BOT_236", 2, 1, TAG_CARDTYPE.MINION, TAG_RARITY.LEGENDARY, 1600));
var beatrixOrdered = DeckImageSnapshot.OrderedCards(beatrixSections);
Check(beatrixOrdered[1].Count == 1 && beatrixOrdered[1].DisplayCount == 10
    && DeckImagePresentation.CardCaption(beatrixOrdered[1]) == "x10",
    "Beatrix's chosen sideboard minion displays ten generated copies while its selected count remains one.");
Check(new DeckImageStatistics(beatrixSections).CraftingDust == 3200,
    "Beatrix's generated copies do not multiply the crafting cost.");
Check(DeckImageSnapshot.OrderedCards(beatrixSections)[1].DisplayCount == 10,
    "Ordering Beatrix's sideboard twice does not multiply its display count.");
Check(ordered[1].OwnedSideboardNumber == 1 && ordered[2].SideboardNumber == 1 && ordered[2].SideboardOwner == "Owner", "Shared owner numbers remain clear across row breaks.");
Check(DeckImagePresentation.CardCaption(ordered[1]) == "" && DeckImagePresentation.CardCaption(ordered[2]) == "x3",
    "Single copies and singleton sideboard owners have no quantity caption; multiple copies use xN.");
mainStats.Cards.Add(StatCard("B", 12, 1, TAG_CARDTYPE.SPELL, TAG_RARITY.LEGENDARY, 1000));
Check(DeckImageSnapshot.OrderedCards(statSections).Count(card => card.Id == "SIDE") == 1, "Multiple qualities of an owner do not duplicate its sideboard.");
sideStats.OwnerId = "ORPHAN";
Check(DeckImageSnapshot.OrderedCards(statSections).Last().Id == "SIDE", "Orphaned sideboards in incomplete decks remain visible.");

CollectionManager.Instance = new CollectionManager();
CollectionManager.Instance.Cards["PRICE"] = new CollectibleCard();
CollectionManager.Instance.Cards["FREE"] = new CollectibleCard { IsCraftable = false };
CraftingManager.Prices["PRICE"] = 123;
deck.Slots.Clear(); deck.Sideboards.Clear();
deck.Slots.Add(new CollectionDeckSlot("PRICE", new EntityDef { Name = "Priced" }, TAG_PREMIUM.GOLDEN, 2) { Owned = false });
deck.Slots.Add(new CollectionDeckSlot("FREE", new EntityDef { Name = "Free" }, TAG_PREMIUM.NORMAL, 1));
var priced = DeckImageSnapshot.Capture(deck)[0].Cards;
Check(priced.Single(card => card.Id == "PRICE").NormalCraftCost == 123,
    "The snapshot uses live normal crafting prices even for golden cards, regardless of ownership.");
Check(priced.Single(card => card.Id == "FREE").Uncraftable, "Native crafting eligibility controls uncraftable counts.");
Check(DeckImageSnapshot.Capture(deck, false)[0].Cards.All(card => !card.NormalCraftCost.HasValue), "Hidden cost options skip price lookup.");

// Reward-only editions may not have any normal catalog entry, yet their
// rarity still has a generic dust price. Native eligibility takes precedence.
foreach (string id in new[] { "NO_NORMAL", "REWARD", "NO_PRICE" })
    deck.Slots.Add(new CollectionDeckSlot(id, new EntityDef { Name = id }, TAG_PREMIUM.GOLDEN, 1));
CraftingManager.Prices["FREE"] = CraftingManager.Prices["NO_NORMAL"] = CraftingManager.Prices["REWARD"] = 1600;
CraftingUI.Uncraftable.Add("NO_NORMAL");
FixedRewardsMgr.Uncraftable.Add("REWARD");
var eligibilitySections = DeckImageSnapshot.Capture(deck);
var eligibility = new DeckImageStatistics(eligibilitySections);
Check(eligibility.CraftingDust == 246 && eligibility.UncraftableCopies == 3 && eligibility.UnknownPriceCopies == 1,
    "Native uncraftable cards contribute no dust even without normal catalog entries or despite a generic rarity price.");
Check(eligibilitySections[0].Cards.Single(card => card.Id == "NO_PRICE").NormalCraftCost == null
    && !eligibilitySections[0].Cards.Single(card => card.Id == "NO_PRICE").Uncraftable,
    "A craftable card with unavailable pricing remains unknown rather than silently becoming free.");
Check(DeckImageSettings.Label(DeckImageOption.Details) == "Deck name + class", "The export toggle names the two details it controls.");


for (int flags = 0; flags < (1 << DeckImageSettings.Options.Length); flags++)
{
    var options = DeckImageOption.None;
    for (int i = 0; i < DeckImageSettings.Options.Length; i++)
        if ((flags & (1 << i)) != 0) options |= DeckImageSettings.Options[i];
    var layout = new DeckImageLayout(30, 8192, options);
    Check(layout.Panels.Select(panel => panel.Option).All(option => options.HasFlag(option)), "Disabled statistics reserve no image panel.");
    Check(layout.Panels.Count == new[] { DeckImageOption.CraftingCost, DeckImageOption.CardTypes, DeckImageOption.Rarities, DeckImageOption.ManaCurve }.Count(option => options.HasFlag(option)),
        "Each enabled statistic has exactly one panel.");
    foreach (var panel in layout.Panels)
    {
        Check(panel.Y >= 0 && panel.Y + panel.Height <= layout.Height,
            "Statistics fit inside the image.");
        Check(panel.X >= 0 && panel.X + panel.Width <= layout.Width, "Statistics fit horizontally.");
    }
}
Check(new DeckImageLayout(1, 8192, DeckImageOption.None).Columns == 1, "A single card does not create an empty card column.");
var plainCanvas = new DeckImageCanvas(80, 60, false);
var background = new DeckImageCanvas(80, 60, true);
Check(plainCanvas.Rgba.Where((value, index) => index % 4 == 3).All(value => value == 255) && background.Rgba.Where((value, index) => index % 4 == 3).All(value => value == 255), "All export backgrounds are opaque.");
Check(!plainCanvas.Rgba.SequenceEqual(background.Rgba), "The native parchment uses a matching opaque backing color.");
int offset = (10 * 80 + 10) * 4;
plainCanvas.Blend(10, 10, 200, 100, 50, 255);
Check(plainCanvas.Rgba[offset] == 200 && plainCanvas.Rgba[offset + 1] == 100 && plainCanvas.Rgba[offset + 2] == 50, "Native opaque card pixels composite without altering their colors.");
plainCanvas.RenderedPixel(10, 10, 23, 45, 67);
Check(plainCanvas.Rgba[offset] == 23 && plainCanvas.Rgba[offset + 1] == 45 && plainCanvas.Rgba[offset + 2] == 67 && plainCanvas.Rgba[offset + 3] == 255,
    "GPU-composited golden portrait RGB is retained as opaque pixels without a second alpha blend.");
var sample = Enumerable.Range(0, 120).Select(i => i % 10 < 2 || i % 10 >= 8 || i / 10 < 2 || i / 10 >= 10
    ? new DeckImageCanvas.Ink(0, 0, 0) : new DeckImageCanvas.Ink((byte)(180 + i % 10), 160, 120)).ToArray();
background.Parchment(sample, 10, 12);
Check(background.Rgba.Where((value, index) => index % 4 == 0).All(value => value >= 180),
    "Native parchment sampling removes black metal rails from all four source borders.");
Check(background.Pixel(5, 0).R == background.Pixel(6, 0).R && background.Pixel(11, 0).R == background.Pixel(12, 0).R,
    "Mirrored paper samples join without a texture seam or stretching the paper.");
var gradient = Enumerable.Range(0, 80 * 40).Select(i => new DeckImageCanvas.Ink((byte)(i % 80 * 3), (byte)(i / 80 * 6), 100)).ToArray();
var tallArt = new DeckImageCanvas(20, 80, false);
var wideArt = new DeckImageCanvas(160, 80, false);
tallArt.BlurredArtwork(gradient, 80, 40); wideArt.BlurredArtwork(gradient, 80, 40);
Check(tallArt.Rgba.Where((value, i) => i % 4 == 3).All(value => value == 255), "Blurred artwork remains opaque.");
Check(tallArt.Pixel(19, 40).R - tallArt.Pixel(0, 40).R < 40 && wideArt.Pixel(159, 40).R - wideArt.Pixel(0, 40).R > 95,
    "Artwork covers each preset with a centered crop instead of stretching or tiling.");
Check(tallArt.Pixel(10, 40).R < 150 && wideArt.Pixel(80, 40).G < 150, "Artwork keeps its colors while being darkened for readable light captions.");
var brightArt = new DeckImageCanvas(80, 80, false);
brightArt.BlurredArtwork(Enumerable.Repeat(new DeckImageCanvas.Ink(255, 255, 255), 64).ToArray(), 8, 8);
Check(brightArt.Rgba.Where((value, i) => i % 4 < 3).Max() <= 153,
    "Even white source art stays dark enough for readable light statistics.");
var checker = Enumerable.Range(0, 64 * 64).Select(i => (i % 64 + i / 64) % 2 == 0 ? new DeckImageCanvas.Ink(255, 255, 255) : new DeckImageCanvas.Ink(0, 0, 0)).ToArray();
var blurred = new DeckImageCanvas(64, 64, false); blurred.BlurredArtwork(checker, 64, 64);
Check(Math.Abs(blurred.Pixel(31, 32).R - blurred.Pixel(32, 32).R) < 3, "Blur removes high-frequency artwork detail behind the deck.");
Check(checker[0].R == 255 && checker[1].R == 0, "Blurring never alters the native portrait sample.");
var paddedArtwork = Enumerable.Range(0, 128 * 128).Select(i =>
    i % 128 < 16 || i % 128 >= 112 ? new DeckImageCanvas.Ink(180,180,180)
        : i / 128 < 64 ? new DeckImageCanvas.Ink(200,40,20) : new DeckImageCanvas.Ink(20,40,200)).ToArray();
var trimmedArtwork = new DeckImageCanvas(128,128,false);
trimmedArtwork.BlurredArtwork(paddedArtwork,128,128);
Check(trimmedArtwork.Pixel(0,20).R > trimmedArtwork.Pixel(0,20).G * 2
    && trimmedArtwork.Pixel(127,105).B > trimmedArtwork.Pixel(127,105).G * 2,
    "Flat portrait margins are cropped before blur instead of leaving gray strips on the background.");
var subjectArtwork = Enumerable.Range(0, 128 * 128).Select(i =>
    new DeckImageCanvas.Ink((byte)(i % 128 >= 48 && i % 128 < 80 && i / 128 >= 24 && i / 128 < 104 ? 220 : 20),
        (byte)(20 + i / 128), (byte)(20 + i % 128))).ToArray();
var subjectBackground = new DeckImageCanvas(128,128,false);subjectBackground.BlurredArtwork(subjectArtwork,128,128);
Check(subjectBackground.Pixel(64,64).R - subjectBackground.Pixel(24,64).R > 90,
    "The background preserves a broad subject instead of dissolving the artwork into one dark color field.");
Throws(() => blurred.BlurredArtwork(new DeckImageCanvas.Ink[4], 64, 64), "Truncated artwork samples fail before indexing pixels.");
DeckImageSettings.Background = DeckImageBackground.Artwork;
typeof(DeckImageSettings).GetField("s_loaded", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.SetValue(null, false);
Check(DeckImageSettings.Background == DeckImageBackground.Artwork, "Artwork background style survives reloading independently of format.");
DeckImageSettings.Background = (DeckImageBackground)999;
Check(DeckImageSettings.Background == DeckImageBackground.Artwork, "Invalid background styles preserve the previous choice.");
DeckImageSettings.Background = DeckImageBackground.CardArtwork;
typeof(DeckImageSettings).GetField("s_loaded", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.SetValue(null, false);
Check(DeckImageSettings.Background == DeckImageBackground.CardArtwork, "Native card artwork remains a separate persistent background choice.");
DeckImageSettings.Background = DeckImageBackground.Parchment;

var defaults = DeckImageSettings.Capture();
Check(DeckImageSettings.Quality == DeckImageQuality.Normal, "Export quality defaults to normal cards as requested.");
DeckImageSettings.Quality = DeckImageQuality.Highest;
typeof(DeckImageSettings).GetField("s_loaded", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.SetValue(null, false);
Check(DeckImageSettings.Quality == DeckImageQuality.Highest, "Export quality survives reloading.");
DeckImageSettings.Quality = (DeckImageQuality)999;
Check(DeckImageSettings.Quality == DeckImageQuality.Highest, "Invalid export qualities do not overwrite the saved choice.");
var qualityEntry = (PreferenceEntry<string>)typeof(DeckImageSettings).GetField("s_quality", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
foreach (string previousQuality in new[] { "Diamond", "Signature" })
{
    qualityEntry.Value = previousQuality;
    typeof(DeckImageSettings).GetField("s_loaded", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.SetValue(null, false);
    Check(DeckImageSettings.Quality == DeckImageQuality.Highest && qualityEntry.Value == "Highest",
        "Legacy Diamond/Signature preferences migrate to Highest.");
}
DeckImageSettings.Quality = DeckImageQuality.Deck;
Check(DeckImageSettings.Options.Length == 6 && DeckImageSettings.Options.All(option => defaults.HasFlag(option)), "The six deck image options default on.");
foreach (DeckImageOption option in DeckImageSettings.Options)
{
    bool value = DeckImageSettings.Enabled(option);
    int saves = CollectionQualitySettings.Saves;
    DeckImageSettings.Set(option, !value);
    Check(DeckImageSettings.Enabled(option) != value && CollectionQualitySettings.Saves == saves + 1, "Image settings independently queue a persistent save.");
    typeof(DeckImageSettings).GetField("s_loaded", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.SetValue(null, false);
    Check(DeckImageSettings.Enabled(option) != value, "Image settings survive reloading.");
    DeckImageSettings.Set(option, value);
}
void BeginExport(int total = 1, int? artworkChoice = 0)
{
    deck.Slots.Clear(); deck.Sideboards.Clear();
    deck.Slots.Add(new CollectionDeckSlot("CARD", new EntityDef { Name = "Card", Cost = 1 }, TAG_PREMIUM.NORMAL, 2));
    for (int i = 1; i < total; i++)
        deck.Slots.Add(new CollectionDeckSlot("FORMAT" + i, new EntityDef { Name = "Format " + i }, TAG_PREMIUM.NORMAL, 1));
    CollectionManager.Instance = new CollectionManager { Edited = deck };
    DeckImageExport.Start(new UnityEngine.Camera(), new UberText());
    if (CatalogTransferMenu.Shown && artworkChoice.HasValue) CatalogTransferMenu.Select(artworkChoice.Value);
}
void FinishExport()
{
    var finishDeadline = System.Diagnostics.Stopwatch.StartNew();
    while (LoaderCoroutines.Current != null && finishDeadline.Elapsed.TotalSeconds < 10)
    {
        LoaderCoroutines.Tick();
        System.Threading.Thread.Sleep(1);
    }
    Check(!DeckImageExport.Busy && LoaderCoroutines.Current == null, "Export terminates and clears the busy state.");
    Check(DeckImageRenderer.Created == DeckImageRenderer.Disposed, "Export releases the renderer on every exit path.");
}
int surfacesBefore = DeckImageRenderer.Sizes.Count;
BeginExport();
Check(DeckImageRenderer.Sizes.Count == surfacesBefore, "Capture starts without allocating a provisional full-size image.");
Check(DeckImageExport.Busy, "Export becomes busy synchronously.");
DeckImageExport.Start(new UnityEngine.Camera(), new UberText());
FinishExport();
Check(DeckImageRenderer.Sizes.Count == surfacesBefore + 1, "The final full-size image is allocated exactly once.");
Check(DeckImageRenderer.Created == 1 && DeckImageClipboard.Copies == 1, "Repeated clicks produce one renderer and one clipboard write.");
Check(UIStatus.Instance.Messages.Last() == "Deck image copied to clipboard.", "Success is reported after copying.");

BeginExport(); LoaderCoroutines.Tick();
CollectionManager.Instance.Edited = new CollectionDeck();
FinishExport();
Check(DeckImageClipboard.Copies == 1 && UIStatus.Instance.Messages.Last().Contains("cancelled"), "Leaving the deck cancels without changing the clipboard.");

DeckImageRenderer.Fail = true;
BeginExport(); FinishExport();
Check(DeckImageClipboard.Copies == 1 && UIStatus.Instance.Messages.Last().Contains("GPU failure"), "Rendering errors leave the clipboard alone and show an error.");
DeckImageRenderer.Fail = false;

DeckImageRenderer.Loading = true;
BeginExport(); FinishExport();
Check(DeckImageClipboard.Copies == 1 && UIStatus.Instance.Messages.Last().Contains("still loading"), "Slow assets time out instead of exporting partial cards.");
BeginExport(); LoaderCoroutines.Tick();
DeckImageExport.Shutdown();
Check(!DeckImageExport.Busy && DeckImageRenderer.Created == DeckImageRenderer.Disposed, "Shutdown disposes the active iterator even if the loader does not.");
DeckImageRenderer.Loading = false;

DeckImageClipboard.Occupied = true;
BeginExport(); FinishExport();
Check(DeckImageClipboard.Copies == 1 && UIStatus.Instance.Messages.Last().Contains("clipboard is busy"), "An occupied clipboard is retried for a bounded duration.");
DeckImageClipboard.Occupied = false;
BeginExport(); FinishExport();
Check(DeckImageClipboard.Copies == 2, "An export can succeed after cancellation and failures.");
DeckImageRenderer.Captions.Clear();
int dustBefore = DeckImageRenderer.DustCalls, manaBefore = DeckImageRenderer.ManaCalls;
BeginExport();
foreach (DeckImageOption option in DeckImageSettings.Options) DeckImageSettings.Set(option, false);
FinishExport();
Check(DeckImageRenderer.DustCalls > dustBefore && DeckImageRenderer.ManaCalls > manaBefore,
    "Changing options during export does not change the already captured image settings.");
DeckImageRenderer.Captions.Clear();
dustBefore = DeckImageRenderer.DustCalls; manaBefore = DeckImageRenderer.ManaCalls;
int classBefore = DeckImageRenderer.ClassCalls;
BeginExport(); FinishExport();
Check(DeckImageRenderer.DustCalls == dustBefore && DeckImageRenderer.ManaCalls == manaBefore && DeckImageRenderer.ClassCalls == classBefore, "Disabling every additional option removes statistics and deck details from the next image.");
foreach (DeckImageOption option in DeckImageSettings.Options) DeckImageSettings.Set(option, defaults.HasFlag(option));
DeckImageRenderer.Premiums.Clear();
DeckImageSettings.Quality = DeckImageQuality.Golden;
BeginExport();
DeckImageSettings.Quality = DeckImageQuality.Normal;
FinishExport();
Check(DeckImageRenderer.Premiums.Single() == TAG_PREMIUM.GOLDEN, "Changing export quality mid-export takes effect only on the next image.");
DeckImageSettings.Quality = DeckImageQuality.Deck;
DeckImageSettings.Aspect = DeckImageAspect.Landscape;
int pipelineStart = DeckImageRenderer.Pipeline.Count;
BeginExport(8);
DeckImageSettings.Aspect = DeckImageAspect.Square;
FinishExport();
var inFlightSize = DeckImageRenderer.Sizes.Last();
var pipeline = DeckImageRenderer.Pipeline.Skip(pipelineStart).ToArray();
Check(pipeline.First() == "canvas" && pipeline.Count(stage => stage == "capture") == 8
    && pipeline.Count(stage => stage == "paint") == 8,
    "The final canvas is fixed before capturing; every card is captured and painted once.");
DeckImageSettings.Aspect = DeckImageAspect.Landscape;
BeginExport(8); FinishExport();
Check(DeckImageRenderer.Sizes.Last() == inFlightSize, "An in-flight export keeps its captured row capacity.");
DeckImageSettings.Aspect = DeckImageAspect.Square;
BeginExport(8); FinishExport();
Check(DeckImageRenderer.Sizes.Last().Width < inFlightSize.Width,
    "The next export uses the newly selected row capacity and final canvas dimensions.");
var fullSize = DeckImageRenderer.Sizes.Last();
var fullTitleMeasurement = DeckImageRenderer.CaptionMeasurements.Last();
var fullInfoFrame = DeckImageRenderer.InfoFrames.Last();
DeckImageSettings.Resolution = DeckImageResolution.Low;
int capturesBeforeLow = DeckImageRenderer.CaptureWidths.Count;
BeginExport(8);
DeckImageSettings.Resolution = DeckImageResolution.High;
FinishExport();
var lowSize = DeckImageRenderer.Sizes.Last();
var lowCaptures = DeckImageRenderer.CaptureWidths.Skip(capturesBeforeLow).ToArray();
Check(DeckImageRenderer.CaptionMeasurements.Last() == fullTitleMeasurement,
    "Native title fitting uses the exact same reference dimensions and font size in Low and Max.");
var lowInfoFrame = DeckImageRenderer.InfoFrames.Last();
double lowScale = 720.0 / Math.Min(fullSize.Width, fullSize.Height);
Check(Math.Abs(lowInfoFrame.X - Math.Round(fullInfoFrame.X * lowScale)) <= 1
    && Math.Abs(lowInfoFrame.Y - Math.Round(fullInfoFrame.Y * lowScale)) <= 1
    && Math.Abs(lowInfoFrame.Width - Math.Round(fullInfoFrame.Width * lowScale)) <= 1
    && Math.Abs(lowInfoFrame.Height - Math.Round(fullInfoFrame.Height * lowScale)) <= 1,
    "The real export pipeline preserves the measured native shield geometry across quality changes.");
Check(lowCaptures.Length == 8 && lowCaptures.All(width => width < DeckImageLayout.ExportCardWidth / 2),
    "Low captures every native card at reduced resolution instead of reading full-size GPU pixels.");
Check(Math.Abs(Math.Min(lowSize.Width, lowSize.Height) - 720) <= 12
    && lowSize.Width < fullSize.Width && lowSize.Height < fullSize.Height,
    "The export pipeline renders at the captured resolution, including title remeasurement, even when quality changes mid-export.");
DeckImageSettings.Resolution = DeckImageResolution.Max;
DeckImageSettings.Aspect = DeckImageAspect.Portrait;
var paperInk = new DeckImageCanvas(8, 8, true).ContrastingInk(4, 4);
var darkInk = new DeckImageCanvas(8, 8, false).ContrastingInk(4, 4);
Check(paperInk.R < 80 && darkInk.R > 220, "Badge labels use dark ink on parchment and light ink on a dark background.");
var infoCanvas = new DeckImageCanvas(88, 56, true);
var nativeSurface = Enumerable.Range(0, 64)
    .Select(i => new DeckImageCanvas.Ink(i % 8 < 4 ? (byte)100 : (byte)220, 120, 90)).ToArray();
infoCanvas.DarkInfoBox(nativeSurface, 8, 8, 4, 8, 80, 40);
Check(infoCanvas.Pixel(12, 25).R < infoCanvas.Pixel(75, 25).R
    && infoCanvas.Pixel(75, 25).R < 90 && infoCanvas.ContrastingInk(75, 25).R > 220,
    "The menu surface keeps its texture under a dark tint and supports light title and statistics text.");
Check(infoCanvas.Pixel(6, 25).R > infoCanvas.Pixel(10, 25).R
    && infoCanvas.Pixel(4, 8).R == 194 && infoCanvas.Pixel(3, 25).R == 194,
    "The slim border follows the rounded panel without changing the surrounding artwork.");
var dustLayout = new DeckImageLayout(19, 8192, DeckImageOption.CraftingCost);
using (var dustRenderer = new DeckImageRenderer(new UnityEngine.Camera(), new UberText(), dustLayout.Width, dustLayout.Height, true))
{
    DeckImageRenderer.DustInkRight = 56;
    DeckImagePresentation.Statistics(dustRenderer, dustLayout, stats);
    var costText = DeckImageRenderer.TextCalls.Last();
    Check(costText.X == dustLayout.TitleBox.X + dustLayout.TitleTextInset && costText.Left,
        "The dust number shares the deck name's text edge independently of icon dimensions.");
    Check(costText.Font >= 80 && costText.Height >= 120,
        "Dust uses a prominent icon and number next to full-size exported cards.");
    DeckImageRenderer.DustInkRight = -1;
}
var referenceBadge = new DeckImageCanvas(576, 327, false);
var multiBadge = new DeckImageCanvas(576, 327, false);
DeckImageQuantityLabel.Paint(referenceBadge, 2, 0, 0, 576, 327);
DeckImageQuantityLabel.Paint(multiBadge, 10, 0, 0, 576, 327);
for (int badgeY = 0; badgeY < 327; badgeY++)
    for (int badgeX = 0; badgeX < 576; badgeX++)
    {
        int sourceY = 326 - badgeY;
        if (badgeX >= 195 && badgeX < 400 && sourceY >= 65 && sourceY < 200) continue;
        var original = referenceBadge.Pixel(badgeX, badgeY); var multiple = multiBadge.Pixel(badgeX, badgeY);
        Check(original.R == multiple.R && original.G == multiple.G && original.B == multiple.B,
            "x10 preserves the exact same reference frame pixels as x2.");
    }
int nativeShieldsBefore = DeckImageRenderer.NativeShieldCalls;
int shields = DeckImageRenderer.ShieldCalls;
var quantityLayout = new DeckImageLayout(1, 8192, DeckImageOption.None);
using (var quantityRenderer = new DeckImageRenderer(new UnityEngine.Camera(), new UberText(), quantityLayout.Width, quantityLayout.Height, false))
{
    DeckImagePresentation.CardQuantity(quantityRenderer, quantityLayout, quantityLayout.Cells[0], new DeckImageSnapshot.Card { Count = 1 }, true);
    Check(DeckImageRenderer.ShieldCalls == shields, "A single copy does not reserve or render a quantity shield.");
    DeckImagePresentation.CardQuantity(quantityRenderer, quantityLayout, quantityLayout.Cells[0], new DeckImageSnapshot.Card { Count = 2 }, true);
    Check(DeckImageRenderer.ShieldCalls == shields + 1 && DeckImageRenderer.Captions.Last() == "x2", "Multiple copies use the native shield with x2.");
    var small = new DeckImageLayout.Cell(100, 100, 272, 380, 54);
    var large = new DeckImageLayout.Cell(100, 100, 544, 760, 108);
    DeckImagePresentation.CardQuantity(quantityRenderer, quantityLayout, small, new DeckImageSnapshot.Card { Count = 2 }, true);
    var smallShield = DeckImageRenderer.QuantityShields.Last();
    DeckImagePresentation.CardQuantity(quantityRenderer, quantityLayout, large, new DeckImageSnapshot.Card { Count = 2 }, true);
    var largeShield = DeckImageRenderer.QuantityShields.Last();
    Check(smallShield.Width >= small.Width * .45,
        "The DeckView quantity label stays prominent beside the native card frame.");
    Check(Math.Abs(largeShield.Width - smallShield.Width * 2) <= 1
        && Math.Abs(largeShield.Height - smallShield.Height * 2) <= 1,
        "Doubling a visible card doubles the reference quantity label.");
    int smallTop = quantityLayout.Height - smallShield.Y - smallShield.Height;
    Check(Math.Abs(smallShield.X + smallShield.Width / 2 - small.X - small.Width / 2) <= 1
        && smallTop < small.Y + small.Height && smallTop + smallShield.Height / 2 > small.Y + small.Height
        && smallTop + smallShield.Height <= small.Y + small.Height + small.Caption,
        "The centered shield overlaps only the lower rim, with its label below the card inside the reserved space.");
    DeckImagePresentation.CardQuantity(quantityRenderer, quantityLayout, large, new DeckImageSnapshot.Card { Count = 12 }, true);
    Check(DeckImageRenderer.Captions.Last() == "x12" && DeckImageRenderer.NativeShieldCalls == nativeShieldsBefore,
        "Quantities above nine retain their exact count on the same reference shield.");
    var quantityText = DeckImageRenderer.TextCalls.Last();
    Check(DeckImageRenderer.VisibleHeightCaptions.Last() == "x12"
        && Math.Abs(quantityText.Font - largeShield.Height * 88.0 / 327) <= 1,
        "Multi-digit lettering fits its actual visible glyphs to the baked x2 lettering height.");
}
// Cancellation must never wait for a CPU worker or let it publish later.
int copiesBeforeWorkerCancel = DeckImageClipboard.Copies;
DeckImageRenderer.PaintGate = new System.Threading.ManualResetEventSlim(false);
DeckImageRenderer.PaintEntered.Reset(); DeckImageRenderer.PaintFinished.Reset();
int capturesBeforeOverlap = DeckImageRenderer.Cards;
BeginExport(3);
var workerStartDeadline = System.Diagnostics.Stopwatch.StartNew();
while ((!DeckImageRenderer.PaintEntered.IsSet || DeckImageRenderer.Cards < capturesBeforeOverlap + 2)
    && workerStartDeadline.Elapsed.TotalSeconds < 3)
{
    LoaderCoroutines.Tick(); System.Threading.Thread.Sleep(1);
}
Check(DeckImageRenderer.PaintEntered.IsSet && DeckImageExport.Busy && DeckImageRenderer.Cards == capturesBeforeOverlap + 2,
    "The next native card is captured while the previous card's pixel worker is held.");
for (int extraTick = 0; extraTick < 5; extraTick++) LoaderCoroutines.Tick();
Check(DeckImageRenderer.Cards == capturesBeforeOverlap + 2,
    "The pipeline allows only one captured card ahead of the active pixel worker.");
DeckImageExport.Shutdown();
Check(!DeckImageExport.Busy && DeckImageClipboard.Copies == copiesBeforeWorkerCancel,
    "Shutdown returns while the worker is held and preserves the clipboard.");
DeckImageRenderer.PaintGate.Set();
Check(DeckImageRenderer.PaintFinished.Wait(3000), "A cancelled worker finishes only its private image buffers.");
DeckImageRenderer.PaintGate.Dispose(); DeckImageRenderer.PaintGate = null;
Check(DeckImageClipboard.Copies == copiesBeforeWorkerCancel,
    "A worker finishing after cancellation cannot publish its image.");

DeckImageSettings.Background = DeckImageBackground.Artwork;
int classBackgrounds = DeckImageRenderer.ClassBackgrounds.Count;
BeginExport(); DeckImageSettings.Background = DeckImageBackground.Parchment; FinishExport();
Check(DeckImageRenderer.ClassBackgrounds.Count == classBackgrounds + 1
    && DeckImageRenderer.ClassBackgrounds.Last() == TAG_CLASS.MAGE,
    "Class artwork uses the deck class and retains the background chosen at export start.");
DeckImageSettings.Background = DeckImageBackground.CardArtwork;
var backgroundDef = new CardDef(); backgroundDef.Portraits[TAG_PREMIUM.NORMAL] = new UnityEngine.Texture();
DefLoader.Instance.Definitions["CARD"] = backgroundDef;
DeckImageSettings.AskArtwork = false;
int automaticPopups = CatalogTransferMenu.Opened;
BeginExport(2, artworkChoice: null); FinishExport();
Check(!CatalogTransferMenu.Shown && CatalogTransferMenu.Opened == automaticPopups
    && DeckImageRenderer.ArtworkTextures.Last() == backgroundDef.Portraits[TAG_PREMIUM.NORMAL],
    "Disabling the artwork prompt exports immediately using the automatically chosen portrait.");
DeckImageSettings.AskArtwork = true;
int artworkCalls = DeckImageRenderer.ArtworkCalls;
int beforeArtworkPopup = DeckImageRenderer.Created, beforeArtworkCopy = DeckImageClipboard.Copies;
BeginExport(2, artworkChoice: null);
Check(DeckImageExport.Busy && CatalogTransferMenu.Shown && LoaderCoroutines.Current == null
    && DeckImageRenderer.Created == beforeArtworkPopup && DeckImageClipboard.Copies == beforeArtworkCopy,
    "Blurred card art asks before rendering or changing the clipboard.");
Check(CatalogTransferMenu.Choices.SequenceEqual(new[] { "Card", "Format 1" }),
    "The export popup offers each localized deck-card name once without an Automatic duplicate.");
CatalogTransferMenu.Chosen(-1); CatalogTransferMenu.Chosen(2);
Check(DeckImageExport.Busy && CatalogTransferMenu.Shown && DeckImageRenderer.Created == beforeArtworkPopup,
    "Out-of-range artwork callbacks do not start an export or replace the current choice.");
int openedArtworkPopups = CatalogTransferMenu.Opened;
DeckImageExport.Start(new UnityEngine.Camera(), new UberText());
Check(CatalogTransferMenu.Opened == openedArtworkPopups, "Repeated export clicks cannot open a second artwork request.");
CatalogTransferMenu.Cancel();
Check(!DeckImageExport.Busy && DeckImageClipboard.Copies == beforeArtworkCopy,
    "Canceling the artwork popup leaves the clipboard unchanged and releases the export button.");
BeginExport(2, artworkChoice: null);
Action<int> abandonedArtworkChoice = CatalogTransferMenu.Chosen;
DeckImageExport.Shutdown();
abandonedArtworkChoice(0);
Check(!CatalogTransferMenu.Shown && !DeckImageExport.Busy && DeckImageRenderer.Created == beforeArtworkPopup,
    "Shutdown closes a pending picker and rejects its late callbacks.");
BeginExport(2, artworkChoice: null);
CollectionManager.Instance.Edited = new CollectionDeck();
CatalogTransferMenu.Select(0);
Check(!DeckImageExport.Busy && DeckImageRenderer.Created == beforeArtworkPopup,
    "A popup selection cannot export a deck that was closed while choosing artwork.");
BeginExport(); DeckImageSettings.Background = DeckImageBackground.Parchment; FinishExport();
Check(DeckImageRenderer.ArtworkCalls == artworkCalls + 1, "Background style is captured before rendering and uses normal native artwork independently of card quality.");
BeginExport(); FinishExport();
Check(DeckImageRenderer.ArtworkCalls == artworkCalls + 1, "Changing the background style applies to the next export.");
DeckImageSettings.Background = DeckImageBackground.CardArtwork;
var alternativeBackground = new CardDef(); alternativeBackground.Portraits[TAG_PREMIUM.NORMAL] = new UnityEngine.Texture();
DefLoader.Instance.Definitions["FORMAT1"] = alternativeBackground;
BeginExport(2, artworkChoice: 1); FinishExport();
Check(DeckImageRenderer.ArtworkTextures.Last() == alternativeBackground.Portraits[TAG_PREMIUM.NORMAL],
    "Explicit card selection supplies that card's portrait and is frozen when export starts.");
BeginExport(2, artworkChoice: null);
deck.Sideboards["CARD"] = new SideboardDeck();
deck.Sideboards["CARD"].Slots.Add(new CollectionDeckSlot("LATE", new EntityDef { Name = "Late card" }, TAG_PREMIUM.NORMAL, 1));
DeckImageSettings.Background = DeckImageBackground.Parchment;
CatalogTransferMenu.Select(1); FinishExport();
Check(DeckImageRenderer.ArtworkTextures.Last() == alternativeBackground.Portraits[TAG_PREMIUM.NORMAL],
    "The picker uses the captured card choices and background settings, even if settings change before selection.");
DeckImageSettings.Background = DeckImageBackground.CardArtwork;
BeginExport(2); FinishExport();
Check(DeckImageRenderer.ArtworkTextures.Last() == backgroundDef.Portraits[TAG_PREMIUM.NORMAL],
    "The next export reads the newly selected portrait instead of reusing a cached background.");
Check(!UIStatus.Instance.Messages.Any(message => message.StartsWith("Background artwork:"))
    && !DebugLog.Messages.Any(message => message.StartsWith("Deck image background artwork:")),
    "The chosen background is neither announced in the UI nor logged separately.");
DeckImageSettings.Background = DeckImageBackground.CardArtwork;
var unloadedBackground = new CardDef(); unloadedBackground.Portraits[TAG_PREMIUM.NORMAL] = null;
DefLoader.Instance.Definitions["CARD"] = unloadedBackground;
int created = DeckImageRenderer.Created;
BeginExport(); LoaderCoroutines.Tick(); CollectionManager.Instance.Edited = new CollectionDeck(); FinishExport();
Check(DeckImageRenderer.Created == created && unloadedBackground.Requests.Single().Disposed,
    "Cancelling while background artwork loads releases the handle before allocating a renderer.");
DeckImageSettings.Background = DeckImageBackground.Parchment;
foreach (DeckImageAspect aspect in Enum.GetValues<DeckImageAspect>())
{
    var groupLayout = new DeckImageLayout(19, 8192,
        DeckImageOption.CraftingCost | DeckImageOption.Rarities | DeckImageOption.CardTypes | DeckImageOption.ManaCurve | DeckImageOption.Details,
        aspect, stats.Types.Count, stats.Rarities.Count);
    var dustPanel = groupLayout.Panels.Single(p => p.Option == DeckImageOption.CraftingCost);
    var rarityPanel = groupLayout.Panels.Single(p => p.Option == DeckImageOption.Rarities);
    var typePanel = groupLayout.Panels.Single(p => p.Option == DeckImageOption.CardTypes);
    var curvePanel = groupLayout.Panels.Single(p => p.Option == DeckImageOption.ManaCurve);
    var title = groupLayout.TitleBox;
    bool statsBelowTitle = rarityPanel.Y >= dustPanel.Y + dustPanel.Height;
    Check(dustPanel.X == title.X && dustPanel.Y > title.Y + title.Height
        && (rarityPanel.X > title.X + title.Width || statsBelowTitle) && rarityPanel.Y == typePanel.Y
        && rarityPanel.X + rarityPanel.Width < typePanel.X && typePanel.X + typePanel.Width < curvePanel.X,
        "Statistics sit beside the title or move together below it on narrow formats; rarity, types and curve retain their order.");
    Check(groupLayout.InfoBox.Height >= Math.Max(groupLayout.Px(244), Math.Max(stats.Types.Count, stats.Rarities.Count) * groupLayout.Px(DeckImageLayout.StatRow))
        + 2 * groupLayout.Px(DeckImageLayout.FramePadding),
        "The common header fits the enlarged composition rows and mana curve.");
    if (aspect == DeckImageAspect.Square)
        Check(groupLayout.CardPixelWidth >= 285,
            "A 19-card square export keeps readable cards with proportional space for quantity shields.");
    using var groupRenderer = new DeckImageRenderer(new UnityEngine.Camera(), new UberText(), groupLayout.Width, groupLayout.Height, true);
    DeckImageRenderer.ManaCrystals.Clear();
    DeckImageRenderer.TextCalls.Clear();
    DeckImagePresentation.Header(groupRenderer, groupLayout, new string('W', 50), TAG_CLASS.PALADIN, true, DeckImageOption.Details);
    var titleText = DeckImageRenderer.TextCalls.Single();
    Check(titleText.Left && titleText.X == dustPanel.X + groupLayout.Px(DeckImageLayout.TitleIconSpace)
        && (titleText.X + titleText.Width < rarityPanel.X || statsBelowTitle),
        "Deck details and dust start at their column's left edge while long names stay inside the column.");
    var beforeStatistics = groupRenderer.Canvas.Pixel(rarityPanel.X - 4, groupLayout.Height - rarityPanel.Y - 4);
    DeckImagePresentation.Statistics(groupRenderer, groupLayout, stats);
    var betweenStatistics = groupRenderer.Canvas.Pixel(rarityPanel.X - 4, groupLayout.Height - rarityPanel.Y - 4);
    Check(betweenStatistics.R == beforeStatistics.R && betweenStatistics.G == beforeStatistics.G && betweenStatistics.B == beforeStatistics.B,
        "Composition counts are drawn directly on the common header background.");
    var wells = DeckImageRenderer.Wells;
    Check(wells.Count == 8 && wells.X >= curvePanel.X && wells.X + 7 * wells.Stride + wells.Width <= curvePanel.X + curvePanel.Width,
        "All eight mana wells fit in the curve, including empty costs.");
    Check(DeckImageRenderer.TextCalls.Count(t => t.X == typePanel.X && t.Left) == stats.Types.Count
        && DeckImageRenderer.TextCalls.Count(t => t.X == rarityPanel.X + groupLayout.Px(50) + groupLayout.Px(8) && t.Left) == stats.Rarities.Count,
        "Every card type and rarity uses its own row.");
    var alignedDust = DeckImageRenderer.TextCalls.Single(t => t.X == titleText.X
        && t.Y == groupLayout.Height - dustPanel.Y - groupLayout.Px(102));
    Check(alignedDust.X == titleText.X && alignedDust.Left && alignedDust.Font == titleText.Font
        && alignedDust.Height == titleText.Height,
        "Dust's numeric value and the deck name share their text edge, font size and line height.");
    var dustIcon = DeckImageRenderer.DustIcons.Last();
    var classIcon = DeckImageRenderer.HeaderArtwork.Last(artwork => !artwork.Vines);
    Check(dustIcon.X == classIcon.X && dustIcon.Width == classIcon.Size * 5 / 4 && dustIcon.Height == classIcon.Size,
        "Dust and class icons share one centered icon column and the same visible height.");
    Check(DeckImageRenderer.TextCalls.Where(t => t.X == typePanel.X).All(t => t.Font >= 64),
        "Card type text is enlarged to match the larger header.");
    Check(DeckImageRenderer.ManaCrystals.Count == 8, "Every mana cost bin, including empty bins, has a native crystal.");
    for (int i = 0; i < 8; i++)
    {
        var crystal = DeckImageRenderer.ManaCrystals[i];
        Check(DeckImageRenderer.TextCalls.Any(t => t.Text == i.ToString() && t.X == crystal.X && t.Y == crystal.Y
            && t.Width == crystal.Size && t.Height == crystal.Size), "Mana costs are centered inside their crystals.");
        Check(crystal.X >= curvePanel.X && crystal.X + crystal.Size <= curvePanel.X + curvePanel.Width
            && groupLayout.Height - crystal.Y <= curvePanel.Y + curvePanel.Height,
            "Each crystal fits within the native statistics frame.");
    }
    var finalCrystal = DeckImageRenderer.ManaCrystals.Last();
    var plus = DeckImageRenderer.TextCalls.Single(t => t.Text == "+");
    Check(plus.X == finalCrystal.X + finalCrystal.Size && plus.Y == finalCrystal.Y
        && plus.X + plus.Width <= curvePanel.X + curvePanel.Width,
        "The plus is separate and aligned beside the centered seven.");
}
foreach (DeckImageAspect aspect in Enum.GetValues<DeckImageAspect>())
{
    foreach (DeckImageResolution resolution in Enum.GetValues<DeckImageResolution>())
    {
        var aligned = DeckImageLayout.ForExport(19, 8192, DeckImageOption.Details,
            aspect, 5, 5, true, resolution);
        using var alignedRenderer = new DeckImageRenderer(new UnityEngine.Camera(), new UberText(), aligned.Width, aligned.Height, true);
        DeckImageRenderer.TextCalls.Clear(); DeckImageRenderer.HeaderArtwork.Clear();
        DeckImageRenderer.VerticallyCenteredCaptions.Clear();
        DeckImagePresentation.Header(alignedRenderer, aligned, "Lightcaller", TAG_CLASS.PALADIN, true, DeckImageOption.Details);
        var nameText = DeckImageRenderer.TextCalls.Single();
        var classArt = DeckImageRenderer.HeaderArtwork.Single();
        Check(DeckImageRenderer.VerticallyCenteredCaptions.SequenceEqual(new[] { "Lightcaller" })
            && Math.Abs(nameText.Y + nameText.Height * .5 - classArt.Y - classArt.Size * .5) <= .5,
            "The deck name's visible lettering and class icon use one vertical center in every format and resolution.");
    }
    var formatLayout = new DeckImageLayout(19, 8192, DeckImageOption.Details, aspect, wild: true);
    using var formatRenderer = new DeckImageRenderer(new UnityEngine.Camera(), new UberText(), formatLayout.Width, formatLayout.Height, true);
    string name = new string('W', 50);
    DeckImageRenderer.HeaderArtwork.Clear();
    DeckImageRenderer.InfoFrames.Clear();
    DeckImageRenderer.WildFrames.Clear();
    DeckImagePresentation.Header(formatRenderer, formatLayout, name, TAG_CLASS.PALADIN, true, DeckImageOption.Details);
    Check(DeckImageRenderer.HeaderArtwork.Count == 1 && !DeckImageRenderer.HeaderArtwork[0].Vines
        && DeckImageRenderer.Captions.Last() == name,
        "Wild headers retain the class icon and title independently of the frame ornament.");
    var badge = DeckImageRenderer.HeaderArtwork[0];
    var frame = formatLayout.InfoBox;
    var vines = DeckImageRenderer.WildFrames.Single();
    Check(vines.X == frame.X && vines.Width == frame.Width && vines.Height == frame.Height
        && vines.Y == formatLayout.Height - frame.Y - frame.Height
        && vines.Inset < formatLayout.TitleBox.X - frame.X
        && vines.Overhang < formatLayout.Px(16),
        "Wild vines attach to the actual compact shield corners and stay outside its contents.");
    DeckImageRenderer.HeaderArtwork.Clear();
    DeckImageRenderer.WildFrames.Clear();
    DeckImagePresentation.Header(formatRenderer, formatLayout, name, TAG_CLASS.PALADIN, false, DeckImageOption.Details);
    Check(DeckImageRenderer.HeaderArtwork.SequenceEqual(new[] { badge }) && DeckImageRenderer.WildFrames.Count == 0,
        "Standard keeps its class-icon placement and receives no Wild vines.");
    DeckImageRenderer.HeaderArtwork.Clear();
    int captionsBefore = DeckImageRenderer.Captions.Count;
    DeckImagePresentation.Header(formatRenderer, formatLayout, name, TAG_CLASS.PALADIN, true, DeckImageOption.None);
    Check(DeckImageRenderer.HeaderArtwork.Count == 0 && DeckImageRenderer.WildFrames.Count == 1
        && DeckImageRenderer.Captions.Count == captionsBefore,
        "Disabling deck name + class hides both while retaining Wild's frame vines.");
}
foreach (DeckImageAspect aspect in Enum.GetValues<DeckImageAspect>())
{
    DeckImageSettings.Aspect = aspect;
    foreach (DeckImageOption option in DeckImageSettings.Options) DeckImageSettings.Set(option, false);
    deck.FormatType = "FT_WILD";
    int framesBefore = DeckImageRenderer.InfoFrames.Count, vinesBefore = DeckImageRenderer.WildFrames.Count;
    int measuredBefore = DeckImageRenderer.CaptionMeasurements.Count, copiesBefore = DeckImageClipboard.Copies;
    BeginExport(); FinishExport();
    Check(DeckImageClipboard.Copies == copiesBefore + 1
        && DeckImageRenderer.InfoFrames.Count == framesBefore && DeckImageRenderer.WildFrames.Count == vinesBefore
        && DeckImageRenderer.CaptionMeasurements.Count == measuredBefore,
        "An export without header contents copies successfully, with no shield, vines or hidden title measurement.");
    DeckImageSettings.Set(DeckImageOption.Background, true);
    BeginExport(); FinishExport();
    Check(DeckImageRenderer.InfoFrames.Count == framesBefore && DeckImageRenderer.WildFrames.Count == vinesBefore,
        "Keeping the decorative background does not bring back an empty shield.");
}
foreach (DeckImageOption option in DeckImageSettings.Options) DeckImageSettings.Set(option, defaults.HasFlag(option));
// Existing local debug configuration must no longer cause any export writes.
string debugSettings = Path.Combine(Path.GetDirectoryName(typeof(DeckImageExport).Assembly.Location)!,
    "BetterCollectionManager.deck-image-debug.local.txt");
string previousDebugSettings = File.Exists(debugSettings) ? File.ReadAllText(debugSettings) : null;
string debugFolder = Path.Combine(Path.GetTempPath(), "BCM former image destination " + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(debugFolder);
    string earlierExport = Path.Combine(debugFolder, "earlier.png");
    File.WriteAllBytes(earlierExport, new byte[] { 1, 2, 3 });
    File.WriteAllText(debugSettings, debugFolder);
    int copiesBefore = DeckImageClipboard.Copies;
    BeginExport(); FinishExport();
    Check(DeckImageClipboard.Copies == copiesBefore + 1 && DeckImageClipboard.LastPng.Length > 0,
        "Export still copies the image to the clipboard with an old local destination configured.");
    Check(Directory.GetFiles(debugFolder, "*", SearchOption.AllDirectories).SequenceEqual(new[] { earlierExport })
        && File.ReadAllBytes(earlierExport).SequenceEqual(new byte[] { 1, 2, 3 }),
        "Export writes no PNGs or diagnostics to the former destination and preserves existing images.");
}
finally
{
    if (previousDebugSettings != null) File.WriteAllText(debugSettings, previousDebugSettings);
    else if (File.Exists(debugSettings)) File.Delete(debugSettings);
    if (Directory.Exists(debugFolder)) Directory.Delete(debugFolder, true);
}
Console.WriteLine($"Deck image checks passed: {checks}");
