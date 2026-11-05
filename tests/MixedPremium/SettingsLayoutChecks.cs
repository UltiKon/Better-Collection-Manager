using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    // Native text bounds accumulate float roundoff as the panel is repeatedly moved.
    private const float SettingsPixelTolerance = 0.1f;
    private static Rect ClosedFieldScreenBounds(GameObject go)
    {
        var box = go.GetComponentsInChildren<BoxCollider>(true).First(b => b.gameObject.activeInHierarchy);
        Vector3 low = CameraUtils.Camera.WorldToScreenPoint(box.transform.TransformPoint(box.center - box.size * 0.5f));
        Vector3 high = CameraUtils.Camera.WorldToScreenPoint(box.transform.TransformPoint(box.center + box.size * 0.5f));
        return Rect.MinMaxRect(Math.Min(low.x, high.x), Math.Min(low.y, high.y), Math.Max(low.x, high.x), Math.Max(low.y, high.y));
    }

    private static Rect BannerPartInk(MeshRenderer renderer)
    {
        Vector3[] points = renderer.GetComponent<MeshFilter>().sharedMesh.vertices
            .Select(p => CameraUtils.Camera.WorldToScreenPoint(renderer.transform.TransformPoint(p))).ToArray();
        return Rect.MinMaxRect(points.Min(p => p.x), points.Min(p => p.y), points.Max(p => p.x), points.Max(p => p.y));
    }

    private static Rect SettingsTextBounds(UberText text)
    {
        Check(CounterTextLayout.TryScreenInkRect(text, CameraUtils.Camera, out Rect rect),
            "Settings captions and section headings must have measurable visible text");
        return rect;
    }

    private static Rect SettingsCheckboxBounds(CheckBox box)
    {
        Rect field = ClosedFieldScreenBounds(box.gameObject), text = SettingsTextBounds(box.m_uberText);
        return Rect.MinMaxRect(Math.Min(field.xMin, text.xMin), Math.Min(field.yMin, text.yMin),
            Math.Max(field.xMax, text.xMax), Math.Max(field.yMax, text.yMax));
    }

    private static Mesh SettingsSourceMesh(MeshRenderer renderer)
    {
        var menu = renderer.GetComponentInParent<ModsSettingsMenu>();
        if (menu != null)
        {
            var parts = (System.Collections.IDictionary)typeof(ModsSettingsMenu).GetField("m_settingsClipParts", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(menu);
            if (parts.Contains(renderer))
            {
                object part = parts[renderer];
                if (part.GetType().GetField("Source", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(part) is Mesh source) return source;
            }
        }
        return renderer.GetComponent<MeshFilter>()?.sharedMesh;
    }

    private static bool SettingsRowFullyVisible(Component control)
    {
        var menu = control.GetComponentInParent<ModsSettingsMenu>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var rows = (System.Collections.IEnumerable)typeof(ModsSettingsMenu).GetField("m_scrollRows", flags).GetValue(menu);
        var viewport = (Rect)typeof(ModsSettingsMenu).GetField("m_settingsViewport", flags).GetValue(menu);
        float scroll = (float)typeof(ModsSettingsMenu).GetField("m_settingsScroll", flags).GetValue(menu);
        foreach (object row in rows)
        {
            var go = (GameObject)row.GetType().GetField("Object").GetValue(row);
            if (!control.transform.IsChildOf(go.transform)) continue;
            var bounds = (Rect)row.GetType().GetField("Bounds").GetValue(row);
            return bounds.yMin + scroll >= viewport.yMin && bounds.yMax + scroll <= viewport.yMax;
        }
        return false;
    }

    private static Rect SettingsSectionBounds(UberText title)
    {
        var menu = title.GetComponentInParent<ModsSettingsMenu>();
        if (menu != null)
        {
            var rows = (System.Collections.IEnumerable)typeof(ModsSettingsMenu).GetField("m_scrollRows", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(menu);
            foreach (object row in rows)
                if (ReferenceEquals(row.GetType().GetField("Object").GetValue(row), title.transform.parent.gameObject))
                {
                    Rect recorded = (Rect)row.GetType().GetField("Bounds").GetValue(row);
                    float scroll = (float)typeof(ModsSettingsMenu).GetField("m_settingsScroll", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(menu);
                    return new Rect(recorded.xMin, recorded.yMin + scroll, recorded.width, recorded.height);
                }
        }
        Rect rect = SettingsTextBounds(title);
        foreach (MeshRenderer renderer in title.transform.parent.GetComponentsInChildren<MeshRenderer>(false))
        {
            if (renderer.GetComponentInParent<UberText>() != null) continue;
            Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) continue;
            Bounds world = FilterTrayGeometry.TransformBounds(mesh.bounds, renderer.transform.TransformPoint);
            Vector3 low = CameraUtils.Camera.WorldToScreenPoint(world.min);
            Vector3 high = CameraUtils.Camera.WorldToScreenPoint(world.max);
            rect = Rect.MinMaxRect(Math.Min(rect.xMin, Math.Min(low.x, high.x)), Math.Min(rect.yMin, Math.Min(low.y, high.y)),
                Math.Max(rect.xMax, Math.Max(low.x, high.x)), Math.Max(rect.yMax, Math.Max(low.y, high.y)));
        }
        return rect;
    }

    private static void CheckVisibleRowsLayout(ModsSettingsMenu menu)
    {
        int oldWidth = Screen.width, oldHeight = Screen.height;
        float oldScale = CameraUtils.Camera.PixelScale, oldUp = CameraUtils.Camera.UpDirection;
        Vector3 oldOffset = CameraUtils.Camera.Offset;
        float oldDepth = CameraUtils.Camera.DepthScale;
        CameraUtils.Camera.DepthScale = 1f;
        var layout = typeof(ModsSettingsMenu).GetMethod("LayoutMenu", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (var resolution in new[] { (1920, 1080), (1280, 720), (854, 480), (1024, 768) })
        foreach (float up in new[] { 1f, -1f })
        {
            Screen.width = resolution.Item1; Screen.height = resolution.Item2;
            CameraUtils.Camera.PixelScale = 100f; CameraUtils.Camera.UpDirection = up;
            CameraUtils.Camera.Offset = new Vector3(Screen.width * 0.27f, Screen.height * 0.83f, 0f);
            layout.Invoke(menu, null);
            CheckCommonSettingsPage(menu);
        }
        Screen.width = oldWidth; Screen.height = oldHeight;
        CameraUtils.Camera.DepthScale = oldDepth;
        CameraUtils.Camera.PixelScale = oldScale; CameraUtils.Camera.UpDirection = oldUp; CameraUtils.Camera.Offset = oldOffset;
        layout.Invoke(menu, null);
    }

    private static void CheckCommonSettingsPage(ModsSettingsMenu menu)
    {
        UberText[] labels = menu.GetComponentsInChildren<UberText>(true);
        var sections = labels.Where(t => t.gameObject.name.StartsWith("BCM_ModsLabelSection", StringComparison.Ordinal)).ToArray();
        Check(sections.Length == 5, "All five section headings remain available on the scrollable settings page");
        Rect viewport = (Rect)typeof(ModsSettingsMenu).GetField("m_settingsViewport", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(menu);
        foreach (UberText title in sections.Where(t => t.gameObject.activeInHierarchy
            && SettingsSectionBounds(t).yMin >= viewport.yMin && SettingsSectionBounds(t).yMax <= viewport.yMax))
        {
            Rect ink = SettingsTextBounds(title), banner = SettingsSectionBounds(title);
            Check(Math.Abs(ink.center.x - banner.center.x) < SettingsPixelTolerance
                && Math.Abs(ink.center.y - banner.center.y) < SettingsPixelTolerance,
                $"Section text must be centered inside the complete banner, not below it ({title.Text}, ink={ink.center.x},{ink.center.y}, banner={banner.center.x},{banner.center.y}, screen={Screen.width}x{Screen.height}, up={CameraUtils.Camera.UpDirection})");
            Check(banner.width - ink.width >= banner.height * 1.8f - SettingsPixelTolerance,
                "Section banners must keep the long heading clear of both ornaments");
            var halves = title.transform.parent.GetComponentsInChildren<MeshRenderer>(false)
                .Where(r => r.GetComponentInParent<UberText>() == null).ToArray();
            foreach (MeshRenderer bar in halves.Where(r => r.gameObject.name == "NativeHeaderBanner"))
            {
                MeshRenderer tip = halves.Single(r => r.gameObject.name == "NativeHeaderOrnament" && r.transform.parent == bar.transform.parent);
                Rect middle = BannerPartInk(bar), ornament = BannerPartInk(tip);
                bool left = ornament.center.x < ink.center.x;
                Check(Math.Abs(left ? middle.xMin - ornament.xMax : middle.xMax - ornament.xMin) < SettingsPixelTolerance,
                    "Banner middle and narrow ornaments must share exactly the same edge after widening");
                Check(left ? ink.xMin >= middle.xMin + ink.height * 0.2f - SettingsPixelTolerance
                    : ink.xMax <= middle.xMax - ink.height * 0.2f + SettingsPixelTolerance,
                    "The long heading must fit inside the plain banner with space before either ornament");
            }
            Check(title.TextColor.Equals(Color.white) && title.GradientUpperColor.Equals(Color.white)
                && title.GradientLowerColor.Equals(Color.white), "Banner text and gradients must remain white");
            Check(CounterTextLayout.VisibleRenderers(title).All(r => r.sortingLayerID == halves[0].sortingLayerID
                    && r.sortingOrder > halves.Max(h => h.sortingOrder)),
                "Banner text must draw above the artwork instead of inheriting its tint");
            if (CameraUtils.Camera.DepthScale > 0f)
                Check(CameraUtils.Camera.WorldToScreenPoint(title.transform.position).z
                    < halves.Min(r => CameraUtils.Camera.WorldToScreenPoint(r.bounds.max).z),
                    "Banner glyphs must sit in front of the nearest artwork surface");
            Check(halves.Any(r => CameraUtils.Camera.WorldToScreenPoint(r.bounds.center).x < ink.center.x)
                && halves.Any(r => CameraUtils.Camera.WorldToScreenPoint(r.bounds.center).x > ink.center.x),
                "The banner must retain artwork on both sides of its centered heading");
        }
        Rect collection = SettingsSectionBounds(sections.Single(t => t.Text == "Card Display / Handling"));
        Rect quality = SettingsSectionBounds(sections.Single(t => t.Text == "Preferred Quality"));
        Rect filters = SettingsSectionBounds(sections.Single(t => t.Text == "Filter menu"));
        Check(collection.xMax < quality.xMin && Math.Abs(collection.yMax - quality.yMax) < SettingsPixelTolerance,
            "Collection and Preferred Quality must have separate columns with aligned headings");
        Check(filters.yMax < collection.yMin && Math.Abs(filters.center.x - collection.center.x) < SettingsPixelTolerance
            && filters.xMax < quality.xMin,
            "Filter settings must follow Card Display / Handling in the left column");
        Rect general = SettingsSectionBounds(sections.Single(t => t.Text == "Other"));
        Rect disenchant = SettingsSectionBounds(sections.Single(t => t.Text == "Crafting"));
        Check(disenchant.yMax < quality.yMin && Math.Abs(disenchant.center.x - quality.center.x) < SettingsPixelTolerance,
            "Crafting has a separate category below Preferred Quality in the right column");
        Check(general.yMax < disenchant.yMin && Math.Abs(general.center.x - quality.center.x) < SettingsPixelTolerance,
            "Other follows Crafting at the bottom of the right column");
        var content = new List<Rect> { collection, quality, filters, general, disenchant };
        float centerX = (collection.center.x + quality.center.x) * 0.5f;
        float top = collection.yMax;
        float width = menu.BodyWidth * CameraUtils.Camera.PixelScale;
        float bottom = top - menu.BodyHeight * CameraUtils.Camera.PixelScale;
        Rect header = SettingsSectionBounds(menu.GetComponentsInChildren<UberText>(false).Single(t => t.Text == "Collection Mod Settings"));
        Check(Math.Abs(centerX - Screen.width * 0.5f) < SettingsPixelTolerance
            && Math.Abs(header.center.x - centerX) < SettingsPixelTolerance
            && Math.Abs((Math.Max(top, header.yMax) + Math.Min(bottom, header.yMin)) * 0.5f - Screen.height * 0.5f) < SettingsPixelTolerance,
            "The complete settings panel including its header must be centered in the game viewport");
        foreach (DropdownControl dropdown in menu.GetComponentsInChildren<DropdownControl>(false))
        {
            string name = dropdown.gameObject.name;
            UberText label = labels.Single(t => t.gameObject.name == name.Replace("BCM_Mods", "BCM_ModsLabel"));
            Rect heading = SettingsTextBounds(label), field = ClosedFieldScreenBounds(dropdown.gameObject);
            bool priority = name.Contains("Priority"), filter = name == "BCM_ModsFilterrows" || name == "BCM_ModsFiltercolumns" || name == "BCM_ModsFiltermode";
            bool craftingControl = name == "BCM_ModsCollectionopensearch";
            Rect section = craftingControl ? disenchant : priority ? quality : filter ? filters : collection;
            float expectedX = priority || craftingControl ? quality.center.x : collection.center.x;
            Check(Math.Abs(field.center.x - expectedX) < SettingsPixelTolerance
                && Math.Abs(heading.xMin - (field.xMin + field.width * (0.075f / 4f))) < SettingsPixelTolerance,
                $"Native gold captions must share the checkbox left inset above centered fields ({name}, field={field.center.x}, headingLeft={heading.xMin}, expected={expectedX}, screen={Screen.width}x{Screen.height}, up={CameraUtils.Camera.UpDirection})");
            Check(section.yMin > heading.yMax && heading.yMin > field.yMax,
                "Section headings, captions and dropdowns must not overlap");
            if (SettingsRowFullyVisible(dropdown))
            {
                Check(Math.Abs(heading.yMin - field.yMax - field.height * (0.05f / 0.7f)) < SettingsPixelTolerance,
                    $"Caption spacing must retain the native Signature Appearance reference ({name}, headingMin={heading.yMin}, fieldMax={field.yMax}, height={field.height}, screen={Screen.width}x{Screen.height})");
                Check(field.height >= Screen.height * 0.03f,
                    "The larger page must retain readable controls instead of shrinking around remote text meshes");
            }
            float trim = field.width * (3.05f / 4f);
            Check(field.center.x - trim >= centerX - width * 0.5f && field.center.x + trim <= centerX + width * 0.5f,
                "The wider menu must enclose the closed fields' complete asymmetric metal trim");
            if (!filter && !priority && !craftingControl) Check(field.yMin > filters.yMax,
                "Card-handling fields must stay above the filter group");
            Check(label.Alignment == UberText.AlignmentOptions.Left && !label.WordWrap && !label.ResizeToFit && !label.ResizeToFitAndGrow && !label.RenderToTexture
                && label.GetComponentsInChildren<MeshFilter>(true).Length == 0,
                "Captions must retain native typography without stale copied glyph meshes");
            content.Add(heading); content.Add(field);
        }
        Rect rowsField = ClosedFieldScreenBounds(menu.GetComponentsInChildren<DropdownControl>(false)
            .Single(c => c.gameObject.name == "BCM_ModsFilterrows").gameObject);
        Rect columnsHeading = SettingsTextBounds(labels.Single(t => t.gameObject.name == "BCM_ModsLabelFiltercolumns"));
        Check(rowsField.yMin > columnsHeading.yMax, "Filter row and column settings must be stacked vertically");
        var checkboxes = menu.GetComponentsInChildren<CheckBox>(false);
        Check(checkboxes.Any(box => box.m_uberText.Text == "Collapse quality variants")
            && checkboxes.Any(box => box.m_uberText.Text == "Color code counter")
            && menu.GetComponentsInChildren<CheckBox>(true).Length == 11, "All checkboxes must remain available on the common page");
        foreach (CheckBox checkbox in checkboxes)
        {
            Rect rect = SettingsCheckboxBounds(checkbox);
            bool generalOption = checkbox.m_uberText.Text == "Hide redundant settings";
            bool filterOption = checkbox.m_uberText.Text == "Remember filter modes" || checkbox.m_uberText.Text == "Hide filters without results"
                || checkbox.m_uberText.Text == "Show Logic Controls" || checkbox.m_uberText.Text == "Show Edit Controls" || checkbox.m_uberText.Text == "Show Clear All";
            bool disenchantOption = checkbox.gameObject.name == "BCM_ModsDisenchantPopup" || checkbox.gameObject.name == "BCM_ModsNeverDustSignaturesDiamonds" || checkbox.gameObject.name == "BCM_ModsCollapseInCrafting";
            Rect section = disenchantOption ? disenchant : generalOption ? general : filterOption ? filters : collection;
            Check(rect.yMax < section.yMin && (generalOption || filterOption || disenchantOption || rect.yMin > filters.yMax),
                "Each checkbox must remain below its own section heading");
            Rect nativeHit = ClosedFieldScreenBounds(checkbox.gameObject);
            float column = generalOption || disenchantOption ? quality.center.x : collection.center.x;
            Check(Math.Abs(nativeHit.xMin - (column - rowsField.width * 0.5f + rowsField.width * (0.075f / 4f))) < SettingsPixelTolerance,
                "Checkbox squares must retain the native inset from the dropdown's left edge");
            content.Add(rect);
        }
        Rect lastHandling = SettingsCheckboxBounds(checkboxes.Single(c => c.m_uberText.Text == "Color code counter"));
        Check(Math.Abs(lastHandling.yMin - filters.yMax - rowsField.height * 0.75f) < SettingsPixelTolerance,
            "Filter settings must move directly after the visible handling settings when redundant fields disappear");
        {
            var clip = (Rect)typeof(ModsSettingsMenu).GetField("m_settingsViewport", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(menu);
            content = content.Select(rect => Rect.MinMaxRect(Math.Max(rect.xMin, clip.xMin), Math.Max(rect.yMin, clip.yMin),
                Math.Min(rect.xMax, clip.xMax), Math.Min(rect.yMax, clip.yMax)))
                .Where(rect => rect.width > 0 && rect.height > 0).ToList();
        }
        foreach (Rect rect in content)
            Check(rect.xMin >= centerX - width * 0.5f - SettingsPixelTolerance && rect.xMax <= centerX + width * 0.5f + SettingsPixelTolerance
                && rect.yMin >= bottom - SettingsPixelTolerance && rect.yMax <= top + SettingsPixelTolerance,
                "The shared menu border must contain every visible section, field and checkbox");
        var scrollbar = (GameObject)typeof(ModsSettingsMenu).GetField("m_settingsScrollbar", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(menu);
        if (scrollbar != null && scrollbar.activeInHierarchy)
        {
            Rect track = (Rect)typeof(ModsSettingsMenu).GetField("m_settingsTrackRect", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(menu);
            var nativeParts = scrollbar.GetComponentsInChildren<MeshRenderer>(true);
            Rect grip = BannerPartInk(nativeParts.Single(renderer => renderer.gameObject.name == "NativeFilterGrip"));
            Rect thumbHit = (Rect)typeof(ModsSettingsMenu).GetField("m_settingsThumbRect", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(menu);
            Check(Math.Abs(grip.xMin - thumbHit.xMin) < SettingsPixelTolerance
                && Math.Abs(grip.xMax - thumbHit.xMax) < SettingsPixelTolerance
                && Math.Abs(grip.yMin - thumbHit.yMin) < SettingsPixelTolerance
                && Math.Abs(grip.yMax - thumbHit.yMax) < SettingsPixelTolerance,
                "The rendered native grip must match its hit target at every tested resolution and camera direction");
            var rail = nativeParts.Where(renderer => renderer.gameObject.name != "NativeFilterGrip").Select(BannerPartInk).ToArray();
            Check(Math.Abs(rail.Min(rect => rect.yMin) - track.yMin) < SettingsPixelTolerance
                && Math.Abs(rail.Max(rect => rect.yMax) - track.yMax) < SettingsPixelTolerance,
                "The native rail and end caps must cover the full visible settings track after resizing");
            foreach (Rect rect in content)
                Check(track.xMin >= rect.xMax || track.xMax <= rect.xMin || track.yMin >= rect.yMax || track.yMax <= rect.yMin,
                    "The settings scrollbar must fit beside native captions, controls and banners without overlap");
        }
        for (int i = 0; i < content.Count; i++)
        for (int j = i + 1; j < content.Count; j++)
            Check(content[i].xMax <= content[j].xMin || content[j].xMax <= content[i].xMin
                || content[i].yMax <= content[j].yMin || content[j].yMax <= content[i].yMin,
                "Visible settings and section titles must not overlap across columns or groups");
        Check(bottom >= Screen.height * 0.1f - 0.1f && top <= Screen.height
            && centerX - width * 0.5f >= Screen.width * 0.05f - 0.1f
            && centerX + width * 0.5f <= Screen.width * 0.95f + 0.1f,
            $"The enlarged common page must fit standard, small and 4:3 viewports (screen={Screen.width}x{Screen.height}, up={CameraUtils.Camera.UpDirection}, top={top}, bottom={bottom}, centerX={centerX}, width={width}, headerTop={header.yMax}, headerBottom={header.yMin})");
    }

    private static void CheckSettingsLayout(ModsSettingsMenu menu)
    {
        typeof(ModsSettingsMenu).GetMethod("RestoreSettingsScrollRows", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(menu, null);
        // Native dropdown text can have large offscreen render geometry. Only
        // its visible field must determine the settings row's size.
        foreach (DropdownControl field in menu.GetComponentsInChildren<DropdownControl>(true))
        {
            field.m_selectedItem.m_text.RenderToTexture = true;
            field.m_selectedItem.m_text.UpdateNow(true);
            var raw = new GameObject("OffscreenFieldGlyphs");
            raw.transform.SetParent(field.m_selectedItem.m_text.transform, false);
            raw.AddComponent<MeshFilter>().sharedMesh = new Mesh { bounds = new Bounds {
                center = new Vector3(10000f, 10000f, 10000f), size = new Vector3(500f, 500f, 500f) } };
            raw.AddComponent<MeshRenderer>();
            // The complete trim is wider than the hit box, with the arrow
            // extending asymmetrically on the right. Popup/catcher meshes must
            // never contribute to the closed menu's width.
            field.m_selectedItem.GetComponent<MeshFilter>().sharedMesh.bounds =
                new Bounds(Vector3.zero, new Vector3(5.2f, 0.1f, 0.7f));
            field.m_button.gameObject.AddComponent<MeshFilter>().sharedMesh = new Mesh {
                bounds = new Bounds(new Vector3(2.8f, 0f, 0f), new Vector3(0.5f, 0.1f, 0.7f)) };
            field.m_button.gameObject.AddComponent<MeshRenderer>();
            foreach (Component excluded in new Component[] { field.m_menu, field.m_cancelCatcher })
            {
                excluded.gameObject.AddComponent<MeshFilter>().sharedMesh = new Mesh {
                    bounds = new Bounds(Vector3.zero, new Vector3(500f, 500f, 500f)) };
                excluded.gameObject.AddComponent<MeshRenderer>();
            }
        }
        CheckVisibleRowsLayout(menu);
    }
}
