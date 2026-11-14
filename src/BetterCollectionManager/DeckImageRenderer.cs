using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Assets;
using UnityEngine;

namespace BetterCollectionManager
{
    internal sealed class DeckImageRenderer : IDisposable
    {
        private readonly GameObject m_root;
        private readonly Camera m_camera;
        private readonly UberText m_caption;
        private readonly int m_layer;
        private GameObject m_card;
        private DeckImageNativeArt m_nativeArt;
        private DeckImageCardArt m_art;
        private int m_changedPixels, m_inkRight;
        private readonly bool m_parchment;
        private Texture2D m_readback;
        internal DeckImageCanvas.Ink TextInk => m_parchment ? new DeckImageCanvas.Ink(53, 34, 20) : DeckImageCanvas.Text;
        internal Texture2D Image { get; private set; }
        internal DeckImageCanvas Canvas { get; private set; }
        internal DeckImageRenderer(Camera source, UberText font, int width, int height, bool decorated, DeckImageBackground background = DeckImageBackground.Parchment, bool deferCanvas = false)
        {
            m_parchment = decorated && background == DeckImageBackground.Parchment;
            uint used = 0;
            foreach (Renderer renderer in Resources.FindObjectsOfTypeAll<Renderer>())
                if (renderer.gameObject.activeInHierarchy) used |= 1u << renderer.gameObject.layer;
            m_layer = 31;
            while (m_layer >= 0 && (used & (1u << m_layer)) != 0) m_layer--;
            if (m_layer < 0) throw new InvalidOperationException("No free layer for rendering the deck image.");
            m_root = new GameObject("BCM_DeckImageRenderer");
            try
            {
                // Stage outside the live scene, with an independent camera.
                Vector3 offset = new Vector3(10000, 10000, 10000);
                var cameraObject = new GameObject("BCM_DeckImageCamera");
                cameraObject.transform.SetParent(m_root.transform, false);
                m_camera = cameraObject.AddComponent<Camera>();
                m_camera.CopyFrom(source); m_camera.enabled = false;
                m_camera.transform.position = source.transform.position + offset;
                m_camera.transform.rotation = source.transform.rotation;
                m_camera.ResetWorldToCameraMatrix();
                m_camera.cullingMask = 1 << m_layer;
                m_camera.useOcclusionCulling = false;
                m_camera.clearFlags = CameraClearFlags.SolidColor;
                m_camera.backgroundColor = Color.clear;
                m_camera.allowHDR = m_camera.allowMSAA = false;
                // Reproduce native lighting at the staging position. These
                // lights affect only the otherwise unused export layer.
                foreach (Light native in Resources.FindObjectsOfTypeAll<Light>())
                {
                    if (!native.enabled || !native.gameObject.activeInHierarchy || (native.cullingMask & source.cullingMask) == 0) continue;
                    var lightObject = new GameObject("BCM_DeckImageLight");
                    lightObject.transform.SetParent(m_root.transform, false);
                    lightObject.transform.position = native.transform.position + offset;
                    lightObject.transform.rotation = native.transform.rotation;
                    Light light = lightObject.AddComponent<Light>();
                    light.type = native.type; light.color = native.color; light.intensity = native.intensity;
                    light.range = native.range; light.spotAngle = native.spotAngle; light.innerSpotAngle = native.innerSpotAngle;
                    light.cookie = native.cookie; light.cookieSize2D = native.cookieSize2D; light.renderMode = native.renderMode;
                    light.useColorTemperature = native.useColorTemperature; light.colorTemperature = native.colorTemperature;
                    light.cullingMask = 1 << m_layer; light.shadows = LightShadows.None;
                }
                m_caption = CounterTextLayout.CreateDirectCopy(font, m_root.transform, "BCM_DeckImageCaption");
                m_caption.GameStringLookup = m_caption.RichText = m_caption.RenderToTexture = false;
                m_caption.ResizeToFit = m_caption.ResizeToFitAndGrow = m_caption.WordWrap = false;
                m_caption.TextColor = m_caption.GradientUpperColor = m_caption.GradientLowerColor = Color.white;
                m_caption.Alignment = UberText.AlignmentOptions.Center;
                m_caption.Anchor = UberText.AnchorOptions.Middle;
                m_caption.gameObject.SetActive(false);
                m_nativeArt = new DeckImageNativeArt(m_root.transform);
                // Card capture and text measurement only need a small backing surface.
                // Allocate the full image once its final silhouette layout is known.
                if (deferCanvas) Canvas = new DeckImageCanvas(1, 1, m_parchment);
                else SetCanvasSize(width, height);
            }
            catch { Dispose(); throw; }
        }
        internal void SetCanvasSize(int width, int height)
        {
            if (Canvas != null && Canvas.Width == width && Canvas.Height == height) return;
            if (Image != null) UnityEngine.Object.Destroy(Image);
            Image = null;
            Image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Canvas = new DeckImageCanvas(width, height, m_parchment);
            if (!m_parchment) return;
            GameObject paper = m_nativeArt.Parchment(m_camera);
            paper.SetActive(true);
            try
            {
                const int sampleWidth = 768, sampleHeight = 512;
                Color32[] colors = Capture(sampleWidth, sampleHeight, 0, 0,
                    () => { SetLayer(paper); DeckImageNativeArt.Fit(paper, m_camera, new Rect(0, 0, sampleWidth, sampleHeight)); }, false);
                var sample = new DeckImageCanvas.Ink[colors.Length];
                for (int i = 0; i < sample.Length; i++) sample[i] = new DeckImageCanvas.Ink(colors[i].r, colors[i].g, colors[i].b);
                Canvas.Parchment(sample, sampleWidth, sampleHeight);
            }
            finally { paper.SetActive(false); }
        }
        internal Task InitializeCanvasAsync(int width, int height, TAG_CLASS? backgroundClass, Texture artwork)
        {
            // Capture native sources on Unity's thread. The worker only sees
            // private arrays and does all full-image pixel work away from the game.
            Color32[] sample = null;
            int sampleWidth = 0, sampleHeight = 0;
            if (m_parchment)
            {
                sampleWidth = 768; sampleHeight = 512;
                GameObject paper = m_nativeArt.Parchment(m_camera);
                paper.SetActive(true);
                try
                {
                    sample = ReadGraphics(sampleWidth, sampleHeight,
                        () => { SetLayer(paper); DeckImageNativeArt.Fit(paper, m_camera, new Rect(0, 0, 768, 512)); }).Black;
                }
                finally { paper.SetActive(false); }
            }
            else if (artwork != null)
            {
                sampleWidth = Math.Max(8, (int)Math.Round(256.0 * artwork.width / Math.Max(artwork.width, artwork.height)));
                sampleHeight = Math.Max(8, (int)Math.Round(256.0 * artwork.height / Math.Max(artwork.width, artwork.height)));
                RenderTexture previous = RenderTexture.active, target = null;
                Texture2D pixels = null;
                try
                {
                    target = RenderTexture.GetTemporary(sampleWidth, sampleHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    pixels = new Texture2D(sampleWidth, sampleHeight, TextureFormat.RGBA32, false);
                    sample = DeckImageCapture.ReadArtwork(artwork, target, pixels, sampleWidth, sampleHeight);
                }
                finally
                {
                    RenderTexture.active = previous;
                    if (target != null) RenderTexture.ReleaseTemporary(target);
                    if (pixels != null) UnityEngine.Object.Destroy(pixels);
                }
            }
            return Task.Run(() =>
            {
                var canvas = new DeckImageCanvas(width, height, m_parchment);
                if (sample != null)
                {
                    var colors = Array.ConvertAll(sample, color => new DeckImageCanvas.Ink(color.r, color.g, color.b, color.a));
                    if (m_parchment) canvas.Parchment(colors, sampleWidth, sampleHeight);
                    else canvas.BlurredArtwork(colors, sampleWidth, sampleHeight);
                }
                else if (backgroundClass.HasValue) DeckImageBackgroundArt.Paint(canvas, backgroundClass.Value);
                Canvas = canvas;
            });
        }
        internal bool PrepareCard(DeckImageSnapshot.Card card)
        {
            if (m_art == null) m_art = new DeckImageCardArt(card);
            return m_art.Prepare();
        }
        internal Actor CreateCard(DeckImageSnapshot.Card card)
        {
            if (m_art == null || !m_art.Prepare()) throw new InvalidOperationException("Card artwork is not ready: " + card.Id);
            m_card = AssetLoader.Get().InstantiatePrefab(ActorNames.GetHandActor(card.Definition, m_art.Premium), AssetLoadingOptions.IgnorePrefabPosition);
            if (m_card == null) throw new InvalidOperationException("Card model unavailable: " + card.Id);
            m_card.transform.SetParent(m_root.transform, false);
            float depth = (m_camera.nearClipPlane + m_camera.farClipPlane) * .5f;
            m_card.transform.position = m_camera.ViewportToWorldPoint(new Vector3(.5f, .5f, depth));
            Actor actor = m_card.GetComponent<Actor>();
            if (actor == null) throw new InvalidOperationException("Invalid card model: " + card.Id);
            actor.SetPremium(m_art.Premium); actor.SetEntityDef(card.Definition); actor.SetCardDef(m_art.Definition); actor.UpdateAllComponents();
            foreach (Collider collider in m_card.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            m_card.SetActive(true);
            SetLayer(m_card);
            actor.SetActorState(ActorStateType.CARD_IDLE); actor.Hide(); actor.Show();
            return actor;
        }
        internal bool CardArtworkReady(Actor actor) => m_art != null && m_art.Bind(actor);
        internal int DustIcon(int x, int y, int width, int height)
        {
            GameObject icon = m_nativeArt.DustIcon(m_camera);
            icon.SetActive(true);
            try
            {
                const int sample = 192;
                var pair = ReadGraphics(sample, sample, () =>
                {
                    SetLayer(icon); DeckImageNativeArt.Fit(icon, m_camera, new Rect(0, 0, sample, sample));
                });
                if (!DeckImageCapture.PaintSymbol(pair, sample, sample, Canvas, x, y, width, height,
                    QualitySettings.activeColorSpace == ColorSpace.Linear))
                    throw new InvalidOperationException("Native Arcane Dust icon rendered no visible pixels.");
                return width;
            }
            finally { icon.SetActive(false); }
        }
        internal void ClassIcon(TAG_CLASS deckClass, int x, int y, int size)
            => TextureIcon(m_nativeArt.ClassIcon(deckClass), x, y, size, size);

        private void TextureIcon(DeckImageNativeArt.TextureArt art, int x, int y, int boxWidth, int boxHeight)
        {
            float aspect = Math.Abs(art.Texture.width * art.Scale.x / (art.Texture.height * art.Scale.y));
            int width = Math.Max(8, (int)Math.Round(256 * Math.Min(1, aspect)));
            int height = Math.Max(8, (int)Math.Round(256 / Math.Max(1, aspect)));
            RenderTexture previous = RenderTexture.active, target = null;
            Texture2D pixels = null;
            try
            {
                target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                pixels = new Texture2D(width, height, TextureFormat.RGBA32, false);
                Color32[] colors = DeckImageCapture.ReadTexture(art.Texture, target, pixels, width, height, art.Scale, art.Offset);
                // Center both visible silhouettes in the same header column.
                // The narrow Dust jar must not sit left of the circular class rim.
                if (!DeckImageCapture.PaintRgbaSymbol(colors, width, height, Canvas, x, y, boxWidth * 5 / 4, boxHeight))
                    throw new InvalidOperationException("Native symbol texture contains no visible pixels.");
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (pixels != null) UnityEngine.Object.Destroy(pixels);
            }
        }
        internal bool RarityIcon(TAG_RARITY rarity, int x, int y, int width, int height)
        {
            GameObject gem = m_nativeArt.RarityGem(rarity);
            if (gem == null) return false;
            Icon(gem, x, y, width, height); return m_changedPixels >= width * height / 100;
        }
        internal void ManaBar(int count, int maximum, int x, int y, int width, int height)
            => Icon(m_nativeArt.ManaBar((float)count / Math.Max(1, maximum), m_camera), x, y, width, height, false);
        internal void ManaWells(int x, int y, int width, int height, int stride, int count)
        {
            GameObject artwork = m_nativeArt.ManaWell(m_camera, out Func<Rect> measure);
            if (artwork != null)
            {
                bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
                artwork.SetActive(true);
                try
                {
                    var tile = ReadGraphics(width, height, () =>
                    {
                        SetLayer(artwork);
                        DeckImageNativeArt.Fit(artwork, m_camera, new Rect(0, 0, width, height), measure: measure);
                    });
                    if (DeckImageCapture.PaintManaWells(tile, Canvas, x, y, width, height, stride, count, linear))
                    {
                        DebugLog.Msg("Deck image mana curve uses the native recessed track.");
                        return;
                    }
                }
                catch (InvalidOperationException ex) { DebugLog.Msg("Deck image mana track could not be fitted: " + ex.Message); }
                finally { artwork.SetActive(false); }
            }
            DebugLog.Msg("Deck image mana curve uses the recessed track fallback.");
            for (int slot = 0; slot < count; slot++)
                Canvas.Recess(x + slot * stride, y, width, height, Math.Max(3, width / 11));
        }
        internal void ManaCrystal(int x, int y, int size)
            => Icon(m_nativeArt.ManaCrystal(m_camera), x, y, size, size, required: "mana crystal");
        internal void InfoFrame(int x, int y, int width, int height)
        {
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            DeckImageCapture.Pair Sample(GameObject surface, int sampleWidth, int sampleHeight)
            {
                surface.SetActive(true);
                try
                {
                    return ReadGraphics(sampleWidth, sampleHeight, () =>
                    {
                        SetLayer(surface);
                        DeckImageNativeArt.Fit(surface, m_camera, new Rect(0, 0, sampleWidth, sampleHeight));
                    });
                }
                finally { surface.SetActive(false); }
            }

            const int frameWidth = 960, frameHeight = 192;
            GameObject frame = m_nativeArt.QuantityFrame(m_camera);
            if (frame != null && DeckImageCapture.PaintQuantityFrame(Sample(frame, frameWidth, frameHeight),
                frameWidth, frameHeight, Canvas, x, y, width, height, linear))
            {
                DebugLog.Msg("Deck image panel uses the native pack quantity frame.");
                return;
            }

            const int tileWidth = 512, tileHeight = 192;
            DebugLog.Msg("Deck image panel uses the beveled menu texture because the pack dialog did not yield a visible frame.");
            var tile = Sample(m_nativeArt.InfoSurface(m_camera), tileWidth, tileHeight);
            var colors = Array.ConvertAll(tile.Black, color => new DeckImageCanvas.Ink(color.r, color.g, color.b));
            Canvas.DarkInfoBox(colors, tileWidth, tileHeight, x, y, width, height);
        }
        internal void WildFrame(int x, int y, int width, int height, int edge, int inset, int overhang)
        {
            GameObject frame = m_nativeArt.WildFrame(m_camera, out Vector3[] panelCorners);
            const int sampleWidth = 960, sampleHeight = 384;
            Rect nativePanel = default;
            frame.SetActive(true);
            try
            {
                var pair = ReadGraphics(sampleWidth, sampleHeight, () =>
                {
                    SetLayer(frame);
                    DeckImageNativeArt.Fit(frame, m_camera, new Rect(0, 0, sampleWidth, sampleHeight), preserveAspect: true);
                    bool first = true;
                    foreach (Vector3 local in panelCorners)
                    {
                        Vector3 point = m_camera.WorldToScreenPoint(frame.transform.TransformPoint(local));
                        nativePanel = first ? new Rect(point.x, point.y, 0, 0)
                            : Rect.MinMaxRect(Math.Min(nativePanel.xMin, point.x), Math.Min(nativePanel.yMin, point.y),
                                Math.Max(nativePanel.xMax, point.x), Math.Max(nativePanel.yMax, point.y));
                        first = false;
                    }
                });
                if (!DeckImageCapture.PaintWildFrame(pair, sampleWidth, sampleHeight, Canvas, x, y, width, height, edge, inset, overhang,
                    nativePanel, QualitySettings.activeColorSpace == ColorSpace.Linear))
                    throw new InvalidOperationException("Native matchmaking Wild vines rendered no visible pixels.");
                DebugLog.Msg("Deck image Wild vines use the original panel attachment offsets and a shared scale; content plate protected.");
            }
            finally { frame.SetActive(false); }
        }
        internal void ArtworkBackground(Texture artwork)
        {
            if (artwork == null) throw new InvalidOperationException("Background artwork is not ready.");
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = null; Texture2D pixels = null;
            try
            {
                int width = Math.Max(8, (int)Math.Round(256.0 * artwork.width / Math.Max(artwork.width, artwork.height)));
                int height = Math.Max(8, (int)Math.Round(256.0 * artwork.height / Math.Max(artwork.width, artwork.height)));
                target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                // No camera render follows this copy. Read the native portrait
                // directly, without the card frame, even for non-readable textures.
                pixels = new Texture2D(width, height, TextureFormat.RGBA32, false);
                Color32[] colors = DeckImageCapture.ReadArtwork(artwork, target, pixels, width, height); var sample = new DeckImageCanvas.Ink[colors.Length];
                for (int i = 0; i < colors.Length; i++) sample[i] = new DeckImageCanvas.Ink(colors[i].r, colors[i].g, colors[i].b);
                Canvas.BlurredArtwork(sample, width, height);
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (pixels != null) UnityEngine.Object.Destroy(pixels);
            }
        }
        internal void ClassBackground(TAG_CLASS deckClass) => DeckImageBackgroundArt.Paint(Canvas, deckClass);
        private void Icon(GameObject icon, int x, int y, int width, int height, bool preserveAspect = true, string required = null)
        {
            icon.SetActive(true);
            try { Capture(width, height, x, y, () => { SetLayer(icon); DeckImageNativeArt.Fit(icon, m_camera, new Rect(0, 0, width, height), preserveAspect); }, centerInk: preserveAspect); }
            finally { icon.SetActive(false); }
            if (required != null && m_changedPixels == 0)
                throw new InvalidOperationException("Native " + required + " rendered no visible pixels.");
        }
        internal Func<DeckImageCardFrame> CaptureCardJob(Actor actor, int bodyWidth, bool sideboard)
        {
            if (!CardArtworkReady(actor)) throw new InvalidOperationException("Card artwork is not ready to capture.");
            foreach (UberText text in actor.GetComponentsInChildren<UberText>(true)) text.UpdateNow(true);
            m_nativeArt.RememberRarity(actor, m_camera);
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                int width = (int)Math.Ceiling(bodyWidth * (1.55 + attempt * .35));
                int height = (int)Math.Ceiling(bodyWidth * (1.95 + attempt * .45));
                if (Math.Max(width, height) > SystemInfo.maxTextureSize)
                    throw new InvalidOperationException("The complete card exceeds the GPU texture limit.");
                int anchorX = width / 2, anchorY = bodyWidth / 5 + attempt * bodyWidth / 10;
                var normal = ReadGraphics(width, height, () =>
                {
                    ComboSceneGeometry.FitCardBody(actor, m_camera, bodyWidth, anchorX, anchorY);
                    foreach (UberText text in actor.GetComponentsInChildren<UberText>(true)) text.UpdateNow(true);
                    SetLayer(actor.gameObject);
                });
                if (DeckImageCapture.TouchesEdge(normal, width, height, linear)) continue;
                var colors = normal;
                if (sideboard)
                    colors = ReadGraphics(width, height, () =>
                    {
                        DeckImageNativeArt.SideboardStyle(actor, m_art.Premium);
                        SetLayer(actor.gameObject);
                    });
                return () => new DeckImageCardFrame(colors, normal, width, height, bodyWidth, linear);
            }
            throw new InvalidOperationException("The complete native card could not be captured.");
        }

        internal DeckImageLayout.Cell PaintCard(DeckImageCardFrame frame, DeckImageLayout.Cell cell, Action<DeckImageLayout.Cell> beforePaint)
            => frame.PaintFitted(Canvas, cell, beforePaint);

        internal bool QuantityLabel(int count, int x, int y, int width, int height)
            => DeckImageQuantityLabel.Paint(Canvas, count, x, y, width, height);

        internal int MeasureCaption(string caption, int width, int height, int fontPixels)
            => Caption(caption, 0, 0, width, height, fontPixels, writeToCanvas: false);

        internal int Caption(string caption, int x, int y, int width, int height, int fontPixels, DeckImageCanvas.Ink? ink = null, bool leftAligned = false,
            bool writeToCanvas = true, bool fitVisibleHeight = false, bool centerVisibleInk = false)
        {
            int paintedWidth = 0;
            m_caption.gameObject.SetActive(true);
            try
            {
                Capture(width, height, x, y, () => {
                    float depth = (m_camera.nearClipPlane + m_camera.farClipPlane) * .5f;
                    m_caption.transform.position = m_camera.ViewportToWorldPoint(new Vector3(.5f, .5f, depth));
                    m_caption.transform.rotation = m_camera.transform.rotation;
                    m_caption.transform.localScale = Vector3.one;
                    DeckImageCanvas.Ink color = ink ?? TextInk;
                    m_caption.TextColor = m_caption.GradientUpperColor = m_caption.GradientLowerColor = new Color32(color.R, color.G, color.B, 255);
                    // Statistics share a reference line. Quantity labels instead
                    // fit their actual glyphs to the baked reference-art height.
                    m_caption.Text = fitVisibleHeight ? caption : "Ag0123456789";
                    m_caption.Show(); m_caption.UpdateNow(true);
                    if (!CounterTextLayout.TryScreenInkRect(m_caption, m_camera, out Rect area) || area.height <= .001f || area.width <= .001f)
                        throw new InvalidOperationException("The native font is not ready to render.");
                    m_caption.transform.localScale *= fontPixels / area.height;
                    m_caption.Text = string.IsNullOrWhiteSpace(caption) ? "Deck" : caption;
                    m_caption.UpdateNow(true);
                    if (CounterTextLayout.TryScreenInkRect(m_caption, m_camera, out area) && area.width > width - 12f)
                        m_caption.transform.localScale *= (width - 12f) / area.width;
                    m_caption.UpdateNow(true);
                    if (CounterTextLayout.TryScreenInkRect(m_caption, m_camera, out area))
                    {
                        paintedWidth = (int)Math.Ceiling(area.width);
                        m_caption.transform.position += m_camera.ScreenToWorldPoint(new Vector3(leftAligned ? area.width * .5f + 1 : width * .5f, height * .5f, depth))
                            - m_camera.ScreenToWorldPoint(new Vector3(area.center.x, area.center.y, depth));
                    }
                    SetLayer(m_caption.gameObject);
                }, writeToCanvas, glyphHeight: fitVisibleHeight ? fontPixels : 0, centerCaptionInk: centerVisibleInk);
            }
            finally { m_caption.gameObject.SetActive(false); }
            return paintedWidth;
        }
        private void SetLayer(GameObject root)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = m_layer;
        }
        private Color32[] Capture(int width, int height, int x, int y, Action prepare, bool writeToCanvas = true,
            bool centerInk = false, int glyphHeight = 0, bool centerCaptionInk = false)
        {
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            var pair = ReadGraphics(width, height, prepare);
            Color32[] black = pair.Black, colors = pair.White;
            if (glyphHeight > 0 && writeToCanvas)
            {
                DeckImageCanvas.Ink[] Convert(Color32[] pixels) => Array.ConvertAll(pixels,
                    p => new DeckImageCanvas.Ink(p.r, p.g, p.b));
                DeckImageCompositor.PaintCaption(Canvas, Convert(black), Convert(colors), width, height,
                    x, y, width, height, glyphHeight, linear);
                return colors;
            }
            int shiftX = 0, shiftY = 0;
            if (centerCaptionInk) shiftY = DeckImageCapture.CaptionVerticalShift(black, width, height);
            if (centerInk)
            {
                int left = width, right = -1, bottom = height, top = -1;
                for (int row = 0; row < height; row++)
                    for (int column = 0; column < width; column++)
                    {
                        int index = row * width + column;
                        Color32 dark = black[index], light = colors[index];
                        if (DeckImageCompositor.Transmission(new DeckImageCanvas.Ink(dark.r, dark.g, dark.b),
                            new DeckImageCanvas.Ink(light.r, light.g, light.b), linear) >= .95) continue;
                        left = Math.Min(left, column); right = Math.Max(right, column);
                        bottom = Math.Min(bottom, row); top = Math.Max(top, row);
                    }
                if (right >= left && top >= bottom)
                {
                    shiftX = (width - (right - left + 1)) / 2 - left;
                    shiftY = (height - (top - bottom + 1)) / 2 - bottom;
                }
            }
            m_changedPixels = m_inkRight = 0;
            for (int row = 0; row < height; row++)
                for (int column = 0; column < width; column++)
                {
                    int index = row * width + column;
                    Color32 dark = black[index], light = colors[index];
                    DeckImageCanvas.Ink before = Canvas.Pixel(x + column + shiftX, y + row + shiftY);
                    var blackInk = new DeckImageCanvas.Ink(dark.r, dark.g, dark.b);
                    var whiteInk = new DeckImageCanvas.Ink(light.r, light.g, light.b);
                    DeckImageCanvas.Ink color = DeckImageCompositor.Over(blackInk, whiteInk, before, linear);
                    if (Math.Abs(color.R - before.R) + Math.Abs(color.G - before.G) + Math.Abs(color.B - before.B) > 24) { m_changedPixels++; m_inkRight = Math.Max(m_inkRight, column + shiftX + 1); }
                    colors[index] = new Color32(color.R, color.G, color.B, 255);
                    if (writeToCanvas) Canvas.RenderedPixel(x + column + shiftX, y + row + shiftY, color.R, color.G, color.B);
                }
            return colors;
        }

        private DeckImageCapture.Pair ReadGraphics(int width, int height, Action prepare)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = null;
            Texture2D pixels = null;
            try
            {
                target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                m_camera.targetTexture = target;
                m_camera.rect = new Rect(0, 0, 1, 1);
                m_camera.aspect = (float)width / height;
                m_camera.ResetProjectionMatrix();
                if (m_readback == null || m_readback.width != width || m_readback.height != height)
                {
                    if (m_readback != null) UnityEngine.Object.Destroy(m_readback);
                    m_readback = new Texture2D(width, height, TextureFormat.RGBA32, false);
                }
                pixels = m_readback;
                prepare();
                // Keep native layered RGB, but do not interpret shader alpha
                // as coverage. Clear both opaque backgrounds through the camera
                // itself; copying a seed before rendering produced black tiles
                // in the client, despite correct opaque native card artwork.
                return DeckImageCapture.Read(m_camera, pixels, width, height);
            }
            finally
            {
                m_camera.targetTexture = null;
                RenderTexture.active = previous;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                // m_readback is reused until the next capture size or disposal.
            }
        }
        internal void Finish()
        {
            byte[] rgba = Canvas.Rgba;
            var pixels = new Color32[rgba.Length / 4];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], 255);
            Image.SetPixels32(pixels); Image.Apply(false, false);
        }
        internal void ReleaseCard()
        {
            if (m_card != null)
            {
                m_card.SetActive(false);
                Actor actor = m_card.GetComponent<Actor>();
                if (actor != null) actor.Destroy(); else UnityEngine.Object.Destroy(m_card);
                m_card = null;
            }
            m_art?.Dispose(); m_art = null;
        }
        public void Dispose()
        {
            ReleaseCard();
            m_nativeArt?.Dispose();
            if (m_readback != null) UnityEngine.Object.Destroy(m_readback);
            m_readback = null;
            if (m_root != null) { m_root.SetActive(false); UnityEngine.Object.Destroy(m_root); }
            if (Image != null) UnityEngine.Object.Destroy(Image);
        }
    }
}
