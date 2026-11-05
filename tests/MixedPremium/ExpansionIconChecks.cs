using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using BetterCollectionManager.Patches;
using PegasusShared;
using UnityEngine;
using Vector2 = UnityEngine.Vector2;

internal static partial class Program
{
    private static void CheckExpansionIcons(KeywordSetTray.GridFixture fixture)
    {
        var native = fixture.Native;
        DebugLog.ExpansionStarMessages.Clear();
        var toggle = (CollectionSetFilterDropdownToggle)typeof(CollectionFilterMenu)
            .GetField("s_toggle", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        var originalLogo = toggle.m_currentIconQuad;
        var decoration = new GameObject("Large remote decorative quad");
        decoration.transform.SetParent(toggle.transform, false);
        decoration.transform.localPosition = new Vector3(20f, 0f, 10f);
        var frameRenderer = decoration.AddComponent<MeshRenderer>();
        frameRenderer.UseLocalBounds = true;
        frameRenderer.LocalBounds = new Bounds(Vector3.zero, new Vector3(10f, 0.01f, 8f));
        var iconObject = new GameObject("Native logo artwork");
        iconObject.transform.SetParent(toggle.transform, false);
        iconObject.layer = 21;
        var iconRenderer = iconObject.AddComponent<MeshRenderer>();
        var nativeIconScale = new Vector3(0.8f, 1.1f, 0.6f);
        iconObject.transform.localScale = nativeIconScale;
        toggle.m_currentIconQuad = iconRenderer;
        iconRenderer.UseLocalBounds = true;
        iconRenderer.LocalBounds = new Bounds(Vector3.zero, new Vector3(0.65f, 0.01f, 0.3f));
        var original = native.m_selected;
        var callbacks = native.m_items.Select(item => item.Callback).ToArray();
        var savedSets = native.m_items.Select(item => item.CardSets).ToArray();
        var behavior = CollectionQualitySettings.FilterModeBehavior;
        var mode = FilterModes.Get(FilterTrayKind.Expansions);
        bool hide = CollectionQualitySettings.HideEmptyFilters;
        var standardObject = new GameObject("Native Standard fixture");
        standardObject.transform.SetParent(native.m_contents.transform, false);
        var standard = standardObject.AddComponent<SetFilterItem>();
        standard.IsAllStandard = true; standard.FormatType = FormatType.FT_STANDARD;
        standard.IconTexture = new Texture { name = "Standard logo" };
        KeywordSetTray.AddRowArtwork(standard);
        standard.m_uberText.GameStringLookup = true;
        standard.m_uberText.RenderToTexture = true;
        native.m_items.Add(standard);
        int nativeApplications = 0;
        foreach (var row in native.m_items) row.Callback = (sets, cards, format, source, transition) => nativeApplications++;
        var manager = new CollectionManager();
        FormatType Theme(FormatType nativeTheme = FormatType.FT_WILD, object explicitDeck = null)
        {
            CollectionThemePatches.Postfix(manager, new[] { explicitDeck }, ref nativeTheme);
            return nativeTheme;
        }
        var wild = native.m_items[1];
        var first = native.m_items[2]; var second = native.m_items[4]; var old = native.m_items[5];
        first.CardSets = second.CardSets = new List<TAG_CARD_SET> { TAG_CARD_SET.CORE };
        old.CardSets = new List<TAG_CARD_SET> { TAG_CARD_SET.TEST_EXPANSION };
        CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Category;
        FilterModes.Set(FilterTrayKind.Expansions, FilterMode.Single);
        CollectionQualitySettings.HideEmptyFilters = true;
        ExpansionFilterState.Clear(); native.m_selected = first;
        CollectionFilterMenu.InvalidateNativeExpansionPool();
        CollectionFilterMenu.EnsureWideExpansionPool();
        CollectionFilterMenu.SelectExpansion(second);
        Check(Theme() == FormatType.FT_STANDARD && nativeApplications == 1,
            "A Standard selection uses the light native page theme while reusing the warm Wild pool");
        Check(toggle.CurrentIcon == second.IconTexture,
            "A warm broad-pool single expansion still updates its own logo");
        CollectionFilterMenu.ChangeExpansionMode(FilterMode.Or);
        CollectionFilterMenu.SelectExpansion(first);
        var markedStandard = toggle.CurrentIcon as Texture2D;
        Check(markedStandard != null && markedStandard != standard.IconTexture,
            "Multiple Standard expansions use a marked copy through the native logo renderer");
        Check(iconObject.transform.localScale.Equals(nativeIconScale),
            "The centered asterisk uses the native icon scale");
        Check(Theme() == FormatType.FT_STANDARD && nativeApplications == 1,
            "Multiple Standard expansions keep the light theme without refiltering the native pool");
        Check(!toggle.GetComponentsInChildren<UberText>(true).Any(text => text.gameObject.name == "BCM_ExpansionSelectionStar"),
            "The footnote marker needs no separate font, camera-facing text or glyph renderer");
        int copied = Graphics.Blits;
        CollectionFilterMenu.RefreshExpansionIcon();
        CollectionFilterMenu.UpdateExpansionMarker();
        Check(toggle.CurrentIcon == markedStandard && Graphics.Blits == copied,
            "Refreshing and late updates reuse the marked icon without per-frame GPU readback");
        Check(iconObject.transform.localScale.Equals(nativeIconScale),
            "Refreshing the asterisk preserves the native icon scale");
        Check(DebugLog.ExpansionStarMessages.Any(message => message.Contains("requested=True"))
            && DebugLog.ExpansionStarMessages.Any(message => message.Contains("texture-ready")),
            "Diagnostics confirm that the marked texture was assigned to the native renderer");
        CheckMarkerPixels(iconRenderer);
        CollectionFilterMenu.SelectExpansion(old);
        Check(Theme() == FormatType.FT_WILD && nativeApplications == 1,
            "Adding a Wild expansion restores the dark native page theme without rebuilding the pool");
        Check(toggle.CurrentIcon is Texture2D && toggle.CurrentIcon != wild.IconTexture,
            "Adding an older expansion switches the multiple-selection logo to a standalone star using Wild ink");
        CollectionFilterMenu.SelectExpansion(old);
        Check(toggle.CurrentIcon == markedStandard && Theme() == FormatType.FT_STANDARD,
            "Removing the last older expansion restores Standard ink, its light theme and the standalone star");
        manager.Editing = true;
        Check(Theme() == FormatType.FT_WILD, "Editing a Wild deck preserves the deck's native theme");
        manager.Editing = false;
        Check(Theme(explicitDeck: new object()) == FormatType.FT_WILD,
            "An explicit deck theme is unaffected by collection expansion choices");
        manager.Display.ViewMode = CollectionUtils.ViewMode.HERO_SKINS;
        Check(Theme() == FormatType.FT_WILD, "Hero skin display themes remain native");
        manager.Display.ViewMode = CollectionUtils.ViewMode.CARDS;
        Check(Theme(FormatType.FT_CLASSIC) == FormatType.FT_CLASSIC && Theme(FormatType.FT_STANDARD) == FormatType.FT_STANDARD,
            "Classic and native special-mode Standard themes remain unchanged");
        CollectionFilterMenu.ChangeExpansionMode(FilterMode.Single);
        Check(toggle.CurrentIcon == first.IconTexture,
            "Single preview restores the latest expansion logo and removes the star");
        Check(iconObject.transform.localScale.Equals(nativeIconScale),
            "Single selection restores the native icon scale");
        CollectionFilterMenu.ChangeExpansionMode(FilterMode.Or);
        Check(toggle.CurrentIcon == markedStandard,
            "Cycling out of Single restores the multiple-selection standalone star");
        CollectionFilterMenu.ClearExpansions();
        Check(Theme() == FormatType.FT_WILD, "An empty expansion selection keeps the unfiltered Wild theme");
        Check(toggle.CurrentIcon == wild.IconTexture,
            "Clearing expansions restores the unfiltered Wild logo without a star");
        Check(iconObject.transform.localScale.Equals(nativeIconScale),
            "Clearing the selection restores the native icon scale");
        ExpansionFilterState.Clear(); native.m_selected = original;
        CollectionQualitySettings.HideEmptyFilters = hide;
        CollectionQualitySettings.FilterModeBehavior = behavior;
        FilterModes.Set(FilterTrayKind.Expansions, mode);
        native.m_items.Remove(standard); UnityEngine.Object.Destroy(standardObject);
        toggle.m_currentIconQuad = originalLogo;
        UnityEngine.Object.Destroy(iconObject);
        UnityEngine.Object.Destroy(decoration);
        for (int i = 0; i < callbacks.Length; i++) { native.m_items[i].Callback = callbacks[i]; native.m_items[i].CardSets = savedSets[i]; }
        CollectionFilterMenu.InvalidateNativeExpansionPool();
        CollectionFilterMenu.RefreshExpansionIcon();
    }
    private static void CheckMarkerPixels(Renderer logo)
    {
        var source = new Texture2D(128, 128, TextureFormat.RGBA32, false) { name = "Native book ink" };
        Color ink = new Color(0.31f, 0.24f, 0.13f, 1f);
        var originalPixels = new Color[128 * 128];
        for (int y = 25; y < 96; y++)
            for (int x = 25; x < 89; x++) originalPixels[y * 128 + x] = ink;
        source.SetPixels(originalPixels);
        source.isReadable = false;
        var savedTarget = RenderTexture.active;
        var previousTarget = new RenderTexture();
        RenderTexture.active = previousTarget;
        var marker = new ExpansionIconMarker();
        var marked = marker.Get(source, logo);
        Check(RenderTexture.active == previousTarget && Graphics.LastReadWrite == RenderTextureReadWrite.sRGB
            && RenderTexture.LiveTemporaries == 0,
            "Copying with the available four-argument API uses an sRGB target, restores the active target and releases temporaries");
        Color[] result = marked.GetPixels();
        bool hasSoftEdge = false;
        for (int i = 0; i < result.Length; i++)
        {
            if (result[i].a <= 0f) continue;
            Check(Math.Abs(result[i].r - ink.r) < 0.001f && Math.Abs(result[i].g - ink.g) < 0.001f
                && Math.Abs(result[i].b - ink.b) < 0.001f,
                "The standalone asterisk retains the native ink color");
            if (result[i].a < 1f) hasSoftEdge = true;
        }
        var components = MarkerInkComponents(result, 128, 128);
        Check(components.Count == 1 && hasSoftEdge,
            "The output contains one antialiased asterisk and no native format logo");
        Rect star = components.Single();
        Check(Math.Abs(star.center.x - 64f) < 1f && Math.Abs(star.center.y - 64f) < 1f
            && star.width > 64f && star.height > 48f,
            "The standalone asterisk is centered and readable in the native icon cell");
        CheckCrowdedMarkerPixels(ink);
        Check(source.Pixels.SequenceEqual(originalPixels), "Stamping never changes the native texture's pixels");
        int blits = Graphics.Blits;
        Check(marker.Get(source, logo) == marked && Graphics.Blits == blits,
            "Repeated requests use the existing private texture");
        marker.Clear();
        Check(marked.Destroyed && !source.Destroyed, "Teardown releases private copies and preserves native textures");
        Graphics.FailNextBlit = true;
        bool failed = false;
        try { marker.Get(source, logo); } catch (InvalidOperationException) { failed = true; }
        Check(failed && RenderTexture.active == previousTarget && RenderTexture.LiveTemporaries == 0,
            "A GPU copy failure restores render state and releases its temporary target");
        Check(marker.Get(source, logo) != null, "A failed texture copy does not poison the cache or prevent retry");
        marker.Clear();
        foreach (Type api in new[] { typeof(NativeBlitBinding), typeof(MaterialBlitBinding), typeof(SlicedBlitBinding), typeof(ScaledSlicedBlitBinding) })
        {
            var compatible = new ExpansionIconMarker(api);
            Check(compatible.Get(source, logo).GetPixels().SequenceEqual(result),
                "Runtime-selected native copy bindings preserve the same pixels: " + api.Name);
            compatible.Clear();
        }
        Check(MaterialBlitBinding.LastMaterial != null && MaterialBlitBinding.LastMaterial.Destroyed,
            "The material-only native API receives an owned copy material which is released on teardown");
        var materialCopy = new ExpansionIconMarker(typeof(MaterialBlitBinding));
        Shader.MissingCopyShader = true;
        bool missingShader = false;
        try { materialCopy.Get(source, logo); } catch (InvalidOperationException) { missingShader = true; }
        Shader.MissingCopyShader = false;
        Check(missingShader && RenderTexture.active == previousTarget && RenderTexture.LiveTemporaries == 0,
            "An unavailable copy shader reports a real failure and restores native render state");
        Check(materialCopy.Get(source, logo) != null, "Copy material creation can retry once the native shader is available");
        materialCopy.Clear();
        bool unsupportedTarget = false;
        try { ExpansionIconMarker.ResolveBlit(typeof(WrongBlitTarget), out _); }
        catch (MissingMethodException ex) { unsupportedTarget = ex.Message.Contains("Available:"); }
        Check(unsupportedTarget, "Overload discovery rejects a non-render-texture destination and reports available signatures");
        MeshFilter filter = logo.gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = new Mesh();
        filter.sharedMesh.SetUVs(0, new List<Vector4> { new Vector4(0f, 0f, 0f, 0f), new Vector4(1f, 1f, 0f, 0f) });
        logo.sharedMaterial = new Material { mainTextureScale = new Vector2(0.5f, 0.5f), mainTextureOffset = new Vector2(0.5f, 0f) };
        Color[] atlasPixels = marker.Get(source, logo).GetPixels();
        int atlasChanges = 0;
        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
                if (!atlasPixels[y * 128 + x].Equals(originalPixels[y * 128 + x]))
                {
                    atlasChanges++;
                    Check(x >= 64 && y < 64, "Atlas stamping stays inside the native mesh UV and material offset region");
                }
        Check(atlasChanges > 0, "Atlas-backed icons receive visible asterisk pixels in their selected cell");
        marker.Clear();
        UnityEngine.Object.Destroy(filter); logo.sharedMaterial = null;
        RenderTexture.active = savedTarget;
    }

    private static void CheckCrowdedMarkerPixels(Color ink)
    {
        var pixels = new Color[128 * 128];
        for (int y = 12; y < 122; y++)
            for (int x = 12; x < 122; x++) pixels[y * 128 + x] = ink;
        ExpansionIconMarker.Stamp(pixels, 128, 128, new Rect(0f, 0f, 1f, 1f));
        var components = MarkerInkComponents(pixels, 128, 128);
        Check(components.Count == 1 && components[0].width < 100f && pixels[20 * 128 + 20].a == 0f,
            "Even a large native logo is completely removed before drawing the standalone star");
    }

    private static List<Rect> MarkerInkComponents(Color[] pixels, int width, int height)
    {
        var visited = new bool[pixels.Length];
        var result = new List<Rect>();
        for (int index = 0; index < pixels.Length; index++)
        {
            if (visited[index] || pixels[index].a <= 0.1f) continue;
            var queue = new Queue<int>();
            queue.Enqueue(index); visited[index] = true;
            int left = width, right = 0, bottom = height, top = 0;
            while (queue.Count > 0)
            {
                int next = queue.Dequeue(), x = next % width, y = next / width;
                left = Math.Min(left, x); right = Math.Max(right, x + 1);
                bottom = Math.Min(bottom, y); top = Math.Max(top, y + 1);
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                        int neighbor = ny * width + nx;
                        if (visited[neighbor] || pixels[neighbor].a <= 0.1f) continue;
                        visited[neighbor] = true; queue.Enqueue(neighbor);
                    }
            }
            result.Add(new Rect(left, bottom, right - left, top - bottom));
        }
        return result;
    }

    private static class NativeBlitBinding
    {
        // The public convenience wrapper is absent, as in a stripped game assembly.
        private static void Blit2(Texture source, RenderTexture destination)
            => Graphics.Blit(source, destination, new Vector2(1f, 1f), Vector2.zero);
    }

    private static class MaterialBlitBinding
    {
        internal static Material LastMaterial;
        public static void Blit(Texture source, RenderTexture destination, Material material, int pass)
        {
            if (material?.shader?.name != "Hidden/BlitCopy" || pass != -1)
                throw new InvalidOperationException("Native material copy needs a valid copy shader and pass");
            LastMaterial = material;
            Graphics.Blit(source, destination, new Vector2(1f, 1f), Vector2.zero);
        }
    }

    private static class SlicedBlitBinding
    {
        public static void Blit(Texture source, RenderTexture destination, int sourceDepthSlice, int destDepthSlice)
        {
            if (sourceDepthSlice != 0 || destDepthSlice != 0) throw new InvalidOperationException("Incorrect 2D texture slice");
            Graphics.Blit(source, destination, new Vector2(1f, 1f), Vector2.zero);
        }
    }

    private static class ScaledSlicedBlitBinding
    {
        public static void Blit(Texture source, RenderTexture destination, Vector2 scale, Vector2 offset, int sourceDepthSlice, int destDepthSlice)
        {
            if (sourceDepthSlice != 0 || destDepthSlice != 0) throw new InvalidOperationException("Incorrect 2D texture slice");
            Graphics.Blit(source, destination, scale, offset);
        }
    }

    private static class WrongBlitTarget
    {
        public static void Blit(Texture source, Material destination) => throw new InvalidOperationException("Wrong destination");
    }

}
