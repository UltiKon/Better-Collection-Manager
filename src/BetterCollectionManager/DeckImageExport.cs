using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using ImageCard = BetterCollectionManager.DeckImageSnapshot.Card;
using ImageSection = BetterCollectionManager.DeckImageSnapshot.Section;

namespace BetterCollectionManager
{
    internal static class DeckImageExport
    {
        private static object s_coroutine;
        private static IEnumerator s_routine;
        private static Task s_work;
        private static object s_artworkRequest;
        private static Action s_cancelArtwork;
        internal static bool Busy { get; private set; }
        internal static void Start(Camera source, UberText font)
        {
            if (Busy) return;
            try
            {
                if (!DeckImageClipboard.Supported) throw new PlatformNotSupportedException("Image clipboard export is unavailable on this platform.");
                CollectionManager manager = CollectionManager.Get();
                CollectionDeck deck = manager != null && manager.IsInEditMode() ? manager.GetEditedDeck() : null;
                if (deck == null || source == null || font == null) throw new InvalidOperationException("Open a deck first.");
                DeckImageOption options = DeckImageSettings.Capture();
                List<ImageSection> sections = DeckImageSnapshot.Capture(deck, options.HasFlag(DeckImageOption.CraftingCost), DeckImageSettings.Quality);
                List<ImageCard> cards = DeckImageSnapshot.OrderedCards(sections);
                var statistics = new DeckImageStatistics(sections);
                bool wild = deck.FormatType.ToString() == "FT_WILD";
                var layout = DeckImageLayout.ForExport(cards.Count, SystemInfo.maxTextureSize, options, DeckImageSettings.Aspect,
                    statistics.Types.Count, statistics.Rarities.Count, wild, DeckImageSettings.Resolution);
                DeckImageBackground background = DeckImageSettings.Background;
                object request = new object();
                s_artworkRequest = request;
                Busy = true;
                void Render(ImageCard artwork)
                {
                    if (!ReferenceEquals(s_artworkRequest, request)) return;
                    s_artworkRequest = null; s_cancelArtwork = null;
                    try
                    {
                        CheckCurrent(deck);
                        if (source == null || font == null) throw new InvalidOperationException("The export menu was closed.");
                        s_routine = Guard(Export(deck, cards, statistics, layout, options, deck.GetClass(), wild, background, artwork, source, font));
                        s_coroutine = LoaderCoroutines.Start(s_routine);
                        UIStatus.Get()?.AddInfo("Rendering deck image...");
                    }
                    catch (Exception ex) { Shutdown(); Report(ex); }
                }
                if (options.HasFlag(DeckImageOption.Background) && background == DeckImageBackground.CardArtwork && DeckImageSettings.AskArtwork)
                {
                    var choices = cards.GroupBy(card => card.Id, StringComparer.Ordinal).Select(group => group.First())
                        .OrderBy(card => card.Definition.GetName() ?? card.Id, StringComparer.CurrentCultureIgnoreCase).ToList();
                    var labels = choices.Select(card => card.Definition.GetName() ?? card.Id).ToList();
                    Action cancel = CatalogTransferMenu.OpenChoices("Choose background artwork", labels,
                        index => { if (index >= 0 && index < choices.Count) Render(choices[index]); },
                        () => {
                            if (!ReferenceEquals(s_artworkRequest, request)) return;
                            s_artworkRequest = null; s_cancelArtwork = null; Busy = false;
                        });
                    if (ReferenceEquals(s_artworkRequest, request)) s_cancelArtwork = cancel;
                }
                else Render(background == DeckImageBackground.CardArtwork ? DeckImageSnapshot.BackgroundCard(sections) : null);
            }
            catch (Exception ex) { Shutdown(); Report(ex); }
        }
        private static IEnumerator Guard(IEnumerator routine)
        {
            try
            {
                while (true)
                {
                    object current;
                    try { if (!routine.MoveNext()) break; current = routine.Current; }
                    catch (Exception ex) { Report(ex); break; }
                    yield return current;
                }
            }
            finally
            {
                (routine as IDisposable)?.Dispose();
                ObservePendingWork();
                Busy = false; s_coroutine = null; s_routine = null;
            }
        }
        private static IEnumerator Export(CollectionDeck deck, List<ImageCard> cards, DeckImageStatistics statistics,
            DeckImageLayout layout, DeckImageOption options, TAG_CLASS deckClass, bool wild, DeckImageBackground background, ImageCard backgroundCard, Camera source, UberText font)
        {
            string title = deck.Name;
            float started = Time.realtimeSinceStartup;
            var timing = System.Diagnostics.Stopwatch.StartNew();
            using var art = options.HasFlag(DeckImageOption.Background) && background == DeckImageBackground.CardArtwork
                ? new DeckImageCardArt(new ImageCard { Id = backgroundCard.Id, Definition = backgroundCard.Definition, Premium = TAG_PREMIUM.NORMAL }) : null;
            if (art != null)
            {
                float deadline = started + 15f;
                while (!art.Prepare())
                {
                    CheckCurrent(deck); CheckDeadline(deadline, started); yield return null;
                }
            }
            CheckCurrent(deck);
            using var renderer = new DeckImageRenderer(source, font, layout.Width, layout.Height, options.HasFlag(DeckImageOption.Background), background, deferCanvas: true);
            // Measure at the common reference size so font fitting cannot move
            // the frame between quality presets. Only the final geometry scales.
            var original = layout.Original;
            int? titleWidth = null;
            if (options.HasFlag(DeckImageOption.Details))
            {
                int measureWidth = Math.Min(original.TitleBox.Width - original.TitleTextInset, Math.Min(8192, SystemInfo.maxTextureSize));
                titleWidth = renderer.MeasureCaption(title, measureWidth, original.Px(102), original.Px(80));
            }
            layout = DeckImageLayout.ForExport(cards.Count, SystemInfo.maxTextureSize, options, layout.Aspect,
                statistics.Types.Count, statistics.Rarities.Count, wild, layout.Resolution, titleTextWidth: titleWidth);
            TAG_CLASS? backgroundClass = options.HasFlag(DeckImageOption.Background) && background == DeckImageBackground.Artwork
                ? (TAG_CLASS?)deckClass : null;
            if (art != null && art.Portrait == null)
                throw new InvalidOperationException("Background portrait is unavailable: " + backgroundCard.Id);
            Task canvasWork = renderer.InitializeCanvasAsync(layout.Width, layout.Height, backgroundClass, art?.Portrait);
            s_work = canvasWork;
            // Start the first native artwork request while the background pixels are built.
            renderer.PrepareCard(cards[0]);
            while (!canvasWork.IsCompleted) { CheckCurrent(deck); yield return null; }
            canvasWork.GetAwaiter().GetResult();
            CheckCurrent(deck);
            double drawBudgetStarted = timing.Elapsed.TotalSeconds;
            foreach (Action step in DeckImagePresentation.HeaderSteps(renderer, layout, title, deckClass, wild, options))
            {
                step();
                if (timing.Elapsed.TotalSeconds - drawBudgetStarted >= .004)
                {
                    yield return null; CheckCurrent(deck);
                    drawBudgetStarted = timing.Elapsed.TotalSeconds;
                }
            }
            double setupSeconds = timing.Elapsed.TotalSeconds;
            double captureSeconds = 0, composeSeconds = 0;
            int index = 0;
            Task<double> pendingPaint = null;
            foreach (ImageCard card in cards)
            {
                CheckCurrent(deck);
                double cardStarted = timing.Elapsed.TotalSeconds;
                float deadline = Time.realtimeSinceStartup + 15f;
                while (!renderer.PrepareCard(card))
                {
                    CheckCurrent(deck);
                    CheckDeadline(deadline, started);
                    yield return null;
                }
                Actor actor = renderer.CreateCard(card);
                // Give Actor.Start, native text generation and portrait loading time
                // to finish. Never export a partially loaded card as a success.
                yield return null;
                while (!renderer.CardArtworkReady(actor))
                {
                    CheckCurrent(deck);
                    CheckDeadline(deadline, started);
                    yield return null;
                }
                actor.UpdateAllComponents();
                if (!renderer.CardArtworkReady(actor)) throw new InvalidOperationException("Card artwork changed while rendering: " + card.Id);
                yield return new WaitForEndOfFrame();
                CheckCurrent(deck);
                Func<DeckImageCardFrame> createFrame = renderer.CaptureCardJob(actor, layout.CardPixelWidth, card.SideboardNumber > 0);
                renderer.ReleaseCard();
                captureSeconds += timing.Elapsed.TotalSeconds - cardStarted;
                // Native loading/capture of this card overlaps the previous
                // card's CPU composition. Canvas writes remain strictly serial.
                if (pendingPaint != null)
                {
                    while (!pendingPaint.IsCompleted) { CheckCurrent(deck); yield return null; }
                    composeSeconds += pendingPaint.GetAwaiter().GetResult();
                    pendingPaint = null;
                }
                var tile = layout.Cells[index];
                if (card.DisplayCount > 9)
                {
                    var frameWork = Task.Run(() =>
                    {
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        var frame = createFrame();
                        return (Frame: frame, Seconds: watch.Elapsed.TotalSeconds);
                    });
                    s_work = frameWork;
                    while (!frameWork.IsCompleted) { CheckCurrent(deck); yield return null; }
                    var framed = frameWork.GetAwaiter().GetResult();
                    composeSeconds += framed.Seconds;
                    var body = framed.Frame.MeasureFitted(renderer.Canvas, tile);
                    DeckImagePresentation.CardQuantity(renderer, layout, body, card, wild);
                    pendingPaint = Task.Run(() =>
                    {
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        framed.Frame.PaintFitted(renderer.Canvas, tile);
                        return watch.Elapsed.TotalSeconds;
                    });
                }
                else
                {
                    pendingPaint = Task.Run(() =>
                    {
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        renderer.PaintCard(createFrame(), tile,
                            body => DeckImagePresentation.CardQuantity(renderer, layout, body, card, wild));
                        return watch.Elapsed.TotalSeconds;
                    });
                }
                s_work = pendingPaint;
                index++;
            }
            if (pendingPaint != null)
            {
                while (!pendingPaint.IsCompleted) { CheckCurrent(deck); yield return null; }
                composeSeconds += pendingPaint.GetAwaiter().GetResult();
            }
            double finishStarted = timing.Elapsed.TotalSeconds;
            drawBudgetStarted = timing.Elapsed.TotalSeconds;
            foreach (Action step in DeckImagePresentation.StatisticsSteps(renderer, layout, statistics))
            {
                step();
                if (timing.Elapsed.TotalSeconds - drawBudgetStarted >= .004)
                {
                    yield return null; CheckCurrent(deck);
                    drawBudgetStarted = timing.Elapsed.TotalSeconds;
                }
            }
            composeSeconds += timing.Elapsed.TotalSeconds - finishStarted;
            double painted = timing.Elapsed.TotalSeconds;
            var encodingWork = Task.Run(() =>
            {
                byte[] pngBytes = DeckImagePng.Encode(renderer.Canvas);
                double pngFinished = timing.Elapsed.TotalSeconds;
                byte[] dibBytes = DeckImageBitmap.ToDib(renderer.Canvas.Rgba, layout.Width, layout.Height);
                return (Png: pngBytes, Dib: dibBytes, PngFinished: pngFinished);
            });
            s_work = encodingWork;
            while (!encodingWork.IsCompleted) { CheckCurrent(deck); yield return null; }
            var encodedImage = encodingWork.GetAwaiter().GetResult();
            CheckCurrent(deck);
            byte[] png = encodedImage.Png, dib = encodedImage.Dib;
            double encoded = encodedImage.PngFinished;
            DebugLog.Msg(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Deck image export timings ({0} cards, {1}x{2}): setup {3:F2}s, capture {4:F2}s, compose {5:F2}s, PNG {6:F2}s, bitmap {7:F2}s, total {8:F2}s.",
                cards.Count, layout.Width, layout.Height, setupSeconds, captureSeconds, composeSeconds, encoded - painted, timing.Elapsed.TotalSeconds - encoded, timing.Elapsed.TotalSeconds));
            float clipboardDeadline = Time.realtimeSinceStartup + 1.5f;
            while (!DeckImageClipboard.TryCopy(png, dib))
            {
                if (Time.realtimeSinceStartup > clipboardDeadline) throw new InvalidOperationException("The clipboard is busy. Try again.");
                yield return null;
                CheckCurrent(deck);
            }
            UIStatus.Get()?.AddInfo("Deck image copied to clipboard.");
        }
        private static void CheckDeadline(float deadline, float started)
        {
            if (Time.realtimeSinceStartup > deadline || Time.realtimeSinceStartup - started > 120f)
                throw new TimeoutException("Card artwork is still loading. Try again shortly.");
        }
        private static void CheckCurrent(CollectionDeck deck)
        {
            CollectionManager manager = CollectionManager.Get();
            if (manager == null || !manager.IsInEditMode() || !ReferenceEquals(manager.GetEditedDeck(), deck))
                throw new InvalidOperationException("Deck image export cancelled because the deck was closed.");
        }
        private static void Report(Exception ex)
        {
            DebugLog.Exception("Deck image export failed", ex);
            UIStatus.Get()?.AddInfo("Could not copy deck image: " + ex.Message);
        }
        private static void ObservePendingWork()
        {
            Task work = s_work; s_work = null;
            if (work != null && work.IsFaulted) { var ignored = work.Exception; }
            else if (work != null && !work.IsCompleted)
                work.ContinueWith(failed => { var ignored = failed.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }
        internal static void Shutdown()
        {
            Action cancelArtwork = s_cancelArtwork;
            s_artworkRequest = null; s_cancelArtwork = null;
            cancelArtwork?.Invoke();
            IEnumerator routine = s_routine;
            LoaderCoroutines.Stop(s_coroutine);
            (routine as IDisposable)?.Dispose();
            ObservePendingWork();
            s_coroutine = null; s_routine = null; Busy = false;
        }
    }
}
