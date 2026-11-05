using System;
using System.Linq;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void MenuPointerFrame(Vector3 pointer, bool down, bool held, bool up)
    {
        Input.mousePosition = pointer;
        Input.LeftPressed = down; Input.LeftHeld = held; Input.LeftReleased = up;
        Time.frameCount++;
        Time.unscaledTime += Time.unscaledDeltaTime;
        KeywordSetTray.PollOutsideClick();
    }

    private static void CheckFilterInteractions(KeywordSetTray.GridFixture fixture)
    {
        Vector3 oldPointer = Input.mousePosition;
        CollectionQualitySettings.FilterMenuRows = 7;
        foreach (int columns in new[] { 1, 2, 3 })
        foreach (FilterTrayKind kind in new[] { FilterTrayKind.Menu, FilterTrayKind.Keywords, FilterTrayKind.Type,
            FilterTrayKind.Race, FilterTrayKind.School, FilterTrayKind.Rarity })
        {
            CollectionQualitySettings.FilterMenuColumns = columns;
            fixture.ClearGesture();
            KeywordSetTray.SetShown(true, kind, "native button feedback");
            MelonLoader.MelonCoroutines.Complete();
            SetFilterItem row = fixture.Items.First(item => !item.IsHeader);
            PegUIElement peg = row.GetComponent<PegUIElement>();
            Renderer shadow = row.m_pressedShadow.GetComponentsInChildren<Renderer>(true).Single();
            Bounds face = FilterButtonFace(row).GetComponent<Renderer>().bounds;
            Check(row.GetComponent<UIBHighlight>() == null && shadow.enabled
                && Near(shadow.bounds.size.x, face.size.x) && Near(shadow.bounds.size.z, face.size.z),
                "One- and multi-column rows must retain and resize their complete native SetFilterItem press artwork without needing UIBHighlight");
            BoxCollider hit = row.GetComponent<BoxCollider>();
            Bounds originalHit = hit.bounds;
            Vector3 resting = row.transform.localPosition;
            Vector3 pointer = CameraUtils.Camera.WorldToScreenPoint(originalHit.center);
            peg.Send(UIEventType.ROLLOVER);
            peg.Send(UIEventType.PRESS);
            MenuPointerFrame(pointer, true, true, false);
            for (int i = 0; i < 6; i++) MenuPointerFrame(pointer, false, true, false);
            fixture.RefreshVisible();
            Check(row.NativePressCalls == 1 && row.NativeHoverCalls == 1
                && row.m_mouseOverGlow.activeSelf && row.m_pressedShadow.activeSelf && shadow.enabled,
                "Native SetFilterItem hover/press artwork must stay visible through held frames and visibility refresh at every column count");
            Check(row.transform.localPosition.Equals(resting)
                && Near(hit.bounds.center.z, originalHit.center.z) && hit.enabled,
                "Tray polling must add no synthetic movement on top of the prefab's native feedback");
            peg.Release();
            peg.Send(UIEventType.ROLLOUT);
            MenuPointerFrame(pointer, false, false, true);
            Check(row.NativeReleaseCalls == 1 && !row.m_pressedShadow.activeSelf && !row.m_mouseOverGlow.activeSelf,
                "Native SetFilterItem release/rollout must restore unselected artwork without duplicating callbacks");
            peg.Send(UIEventType.PRESS);
            peg.Send(UIEventType.ROLLOUT);
            fixture.RefreshVisible();
            Check(!row.m_pressedShadow.activeSelf && !row.m_mouseOverGlow.activeSelf,
                "Leaving a held button must cancel the native pressed appearance without requiring a release on that button");
            SetFilterItem heading = fixture.Items.First(item => item.IsHeader);
            heading.GetComponent<PegUIElement>().Send(UIEventType.PRESS);
            Check(heading.NativePressCalls == 0,
                "Section headings must remain noninteractive even with native feedback components");
            KeywordSetTray.HideImmediately();
            Check(!fixture.HideAreaActive, "Hiding a menu must release its collection input blocker");
            fixture.ClearGesture();
        }

        KeywordSetTray.SetShown(true, FilterTrayKind.Keywords, "Back native feedback");
        MelonLoader.MelonCoroutines.Complete();
        SetFilterItem back = fixture.MakePinnedBack();
        Renderer backShadow = back.m_pressedShadow.GetComponentsInChildren<Renderer>(true).Single();
        PegUIElement backPeg = back.GetComponent<PegUIElement>();
        Bounds backHit = fixture.BackHitBounds;
        Vector3 backRest = back.transform.localPosition;
        Vector3 backPointer = CameraUtils.Camera.WorldToScreenPoint(new Vector3(backHit.center.x, backHit.center.y, backHit.max.z - 0.001f));
        backPeg.Send(UIEventType.PRESS);
        MenuPointerFrame(backPointer, true, true, false);
        fixture.RefreshVisible();
        Check(back.NativePressCalls == 1 && back.m_pressedShadow.activeSelf && backShadow.enabled
            && Near(backShadow.bounds.size.x, fixture.BackBounds.size.x)
            && fixture.PointerIsOverBack && back.transform.localPosition.Equals(backRest)
            && Near(fixture.BackHitBounds.center.z, backHit.center.z),
            "Pinned Back must retain the same native press handler without a second animation or hit-area compensation");
        Vector3 outside = new Vector3(Screen.width - 1f, Screen.height - 1f, 0f);
        MenuPointerFrame(outside, false, false, true);
        backPeg.Release();
        Check(KeywordSetTray.IsShown && back.NativeReleaseCalls == 1 && !back.m_pressedShadow.activeSelf,
            "Dragging a pressed Back outside must cancel navigation while preserving native release feedback");
        KeywordSetTray.HideImmediately();
        fixture.ClearGesture();
        CheckFilterDragReleases(fixture);
        Input.mousePosition = oldPointer;
    }

    private static void CheckFilterDragReleases(KeywordSetTray.GridFixture fixture)
    {
        bool pins = fixture.UsePinnedControls, menus = fixture.UseKeywordMenus, logic = CollectionQualitySettings.ShowLogicControls;
        var previousHits = Physics.Hits;
        var previousMode = FilterModes.Get(FilterTrayKind.Type);
        int columns = CollectionQualitySettings.FilterMenuColumns, rows = CollectionQualitySettings.FilterMenuRows;
        try
        {
            fixture.UsePinnedControls = fixture.UseKeywordMenus = true; CollectionQualitySettings.ShowLogicControls = true;
            CollectionQualitySettings.FilterMenuColumns = 2;
            FilterModes.Set(FilterTrayKind.Type, FilterMode.Single); ExtraFilterState.ClearTypes();
            KeywordSetTray.SetShown(true, FilterTrayKind.Type, "button drag releases");
            MelonLoader.MelonCoroutines.Complete(); fixture.CloseControls(); fixture.ClearGesture();
            SetFilterItem Pin(string name) => fixture.PinnedItems.Single(item => item.gameObject.name == name);
            Vector3 Pointer(SetFilterItem item) => CameraUtils.Camera.WorldToScreenPoint(item.GetComponent<BoxCollider>().bounds.center);
            var back = Pin("BCM_PinnedFilterBack"); var controls = Pin("BCM_ShowLogicControls");
            var backPeg = back.GetComponent<PegUIElement>(); var controlsPeg = controls.GetComponent<PegUIElement>();
            backPeg.Send(UIEventType.ROLLOVER); backPeg.Send(UIEventType.PRESS);
            Physics.Hits = new[] { new RaycastHit { collider = back.GetComponent<BoxCollider>() } };
            MenuPointerFrame(Pointer(back), true, true, false);
            backPeg.Send(UIEventType.ROLLOUT);
            Physics.Hits = new[] { new RaycastHit { collider = controls.GetComponent<BoxCollider>() } };
            MenuPointerFrame(Pointer(controls), false, true, false);
            Check(controls.GetComponent<BoxCollider>().enabled,
                "Dragging from Back to another pinned button must leave the hovered button available to native hover input");
            controlsPeg.Send(UIEventType.ROLLOVER);
            fixture.RefreshVisible();
            Check(controls.m_mouseOverGlow.activeSelf && !back.m_mouseOverGlow.activeSelf,
                "Pinned hover follows the pointer during a held press without reviving the previous button's glow");
            // Return and move again: a prior target may have had overlapping
            // hits blocked, and must become interactive on this same frame.
            Physics.Hits = new[] { new RaycastHit { collider = back.GetComponent<BoxCollider>() } };
            MenuPointerFrame(Pointer(back), false, true, false);
            Physics.Hits = new[] { new RaycastHit { collider = controls.GetComponent<BoxCollider>() } };
            MenuPointerFrame(Pointer(controls), false, false, true);
            controlsPeg.Release(); backPeg.Release();
            MenuPointerFrame(Pointer(controls), false, false, false);
            Check(KeywordSetTray.ActiveKind == FilterTrayKind.Type
                && !fixture.PinnedItems.Any(item => item.gameObject.name == "BCM_PinnedFilterMode"),
                "Releasing on another pinned button cancels both actions, even when both buttons receive RELEASE");
            fixture.ClearGesture();
            MenuPointerFrame(Pointer(controls), true, true, false);
            MenuPointerFrame(Pointer(back), false, true, false);
            MenuPointerFrame(Pointer(controls), false, false, true); controlsPeg.Release();
            MenuPointerFrame(Pointer(controls), false, false, false);
            Check(fixture.PinnedItems.Any(item => item.gameObject.name == "BCM_PinnedFilterMode"),
                "Dragging away and returning to the pressed pinned button still activates that button exactly once");

            fixture.ClearGesture();
            controls = Pin("BCM_ShowLogicControls"); back = Pin("BCM_PinnedFilterBack");
            MenuPointerFrame(Pointer(controls), true, true, false);
            Input.mousePosition = Pointer(back); Input.LeftPressed = Input.LeftHeld = false; Input.LeftReleased = true;
            Time.frameCount++; back.GetComponent<PegUIElement>().Release(); controls.GetComponent<PegUIElement>().Release();
            KeywordSetTray.PollOutsideClick(); MenuPointerFrame(Pointer(back), false, false, false);
            Check(KeywordSetTray.ActiveKind == FilterTrayKind.Type && fixture.PinnedItems.Any(item => item.gameObject.name == "BCM_PinnedFilterMode"),
                "A different pinned release target is rejected even when native RELEASE arrives before menu polling");

            fixture.ClearGesture(); Physics.Hits = Array.Empty<RaycastHit>();
            var row = fixture.Items.First(item => !item.IsHeader);
            var rowPeg = row.GetComponent<PegUIElement>();
            Vector3 origin = Pointer(row), moved = origin + new Vector3(12f, 0f, 0f);
            var type = ExtraFilterCatalog.Types.Single(value => value.Name == row.Text).Type;
            rowPeg.Send(UIEventType.PRESS); MenuPointerFrame(origin, true, true, false);
            MenuPointerFrame(moved, false, true, false);
            MenuPointerFrame(origin, false, false, true); rowPeg.Release();
            Check(ExtraFilterState.HasType(type), "Dragging away and releasing back on a filter still selects it");

            fixture.ClearGesture();
            controls = Pin("BCM_ShowLogicControls");
            MenuPointerFrame(Pointer(row), true, true, false);
            MenuPointerFrame(Pointer(controls), false, false, true); controls.GetComponent<PegUIElement>().Release();
            MenuPointerFrame(Pointer(controls), false, false, false);
            Check(fixture.PinnedItems.Any(item => item.gameObject.name == "BCM_PinnedFilterMode"),
                "A filter press released on Show Controls does not toggle the pinned controls");

            fixture.ClearGesture();
            CollectionQualitySettings.FilterMenuRows = 4; CollectionQualitySettings.FilterMenuColumns = 1;
            fixture.UseKeywordMenus = false; fixture.RebuildCount(105);
            MenuPointerFrame(fixture.ScreenRect.center, false, false, false);
            Check(fixture.ScrollbarRects(out Rect rail, out _), "Drag-release regression fixture has a native scrollbar");
            int clicks = 0;
            var scrollTarget = KeywordSetTray.CreateNavigationItem(fixture.Root.transform, "Scroll release target", PegasusShared.FormatType.FT_STANDARD, () => clicks++);
            try
            {
                scrollTarget.gameObject.SetActive(true);
                origin = fixture.ScreenRect.center;
                MenuPointerFrame(rail.center, true, true, false);
                Check(fixture.ScrollbarGesture, "The native scrollbar captures the regression gesture");
                MenuPointerFrame(origin, false, true, false);
                MenuPointerFrame(origin, false, false, true); scrollTarget.GetComponent<PegUIElement>().Release();
                Check(clicks == 0, "A real scrollbar drag released over a filter remains a scroll gesture");
            }
            finally { UnityEngine.Object.Destroy(scrollTarget.gameObject); }
        }
        finally
        {
            KeywordSetTray.HideImmediately(); fixture.ClearGesture(); Physics.Hits = previousHits;
            fixture.UsePinnedControls = pins; fixture.UseKeywordMenus = menus; CollectionQualitySettings.ShowLogicControls = logic;
            CollectionQualitySettings.FilterMenuColumns = columns; ExtraFilterState.ClearTypes();
            CollectionQualitySettings.FilterMenuRows = rows;
            FilterModes.Set(FilterTrayKind.Type, previousMode);
        }
    }

    private static void CheckScrollbarTrackClicks(KeywordSetTray.GridFixture fixture)
    {
        Vector3 oldPointer = Input.mousePosition;
        float oldSkew = CameraUtils.Camera.DepthSkewZ;
        foreach (float skew in new[] { 0f, -0.4f })
        foreach (int columns in new[] { 1, 2, 3 })
        {
            CameraUtils.Camera.DepthSkewZ = skew;
            CollectionQualitySettings.FilterMenuRows = 4;
            CollectionQualitySettings.FilterMenuColumns = columns;
            fixture.ClearGesture();
            KeywordSetTray.SetShown(true, FilterTrayKind.Keywords, "scrollbar track click");
            MelonLoader.MelonCoroutines.Complete();
            fixture.RebuildCount(105);
            fixture.MakePinnedBack();
            Check(fixture.ScrollbarRects(out Rect rail, out Rect grip) && fixture.ScrollRange > 0f,
                "The common interaction adapter must resolve the live native rail and thumb at every column count");
            Vector3 middle = rail.center;
            fixture.Scroller.InputBlocked = true;
            MenuPointerFrame(middle, true, true, false);
            Check(!fixture.ScrollbarGesture && !fixture.Scroller.m_ScrollThumb.IsDragging(),
                "A scrollbar press must respect the native scroller's input block");
            MenuPointerFrame(middle, false, false, true);
            fixture.Scroller.InputBlocked = false;
            MenuPointerFrame(middle, true, true, false);
            Check(Near(fixture.Scroller.ScrollValue, 0.5f)
                && fixture.ScrollbarGesture && fixture.Scroller.m_ScrollThumb.IsDragging() && KeywordSetTray.IsShown,
                "A click halfway down the rail must move the thumb halfway through the actual content range immediately");
            fixture.ScrollbarRects(out rail, out grip);
            Check(Math.Abs(grip.center.y - middle.y) < 0.02f,
                "The native thumb's center must reach the clicked screen position");
            Vector3 bottom = new Vector3(rail.center.x, rail.yMin, 0f);
            MenuPointerFrame(bottom, false, true, false);
            Check(Near(fixture.Scroller.ScrollValue, 1f),
                "Continuing a track press to the bottom must reach the final content row, including multi-column layouts");
            Vector3 top = new Vector3(rail.center.x, rail.yMax, 0f);
            MenuPointerFrame(top, false, true, false);
            Check(Near(fixture.Scroller.ScrollValue, 0f), "Dragging a captured track press to the top must reach the list start");
            Vector3 outside = new Vector3(rail.center.x, rail.yMin - 100f, 0f);
            MenuPointerFrame(outside, false, false, true);
            Check(KeywordSetTray.IsShown && KeywordSetTray.ShouldKeepOpenForScrollRelease()
                && Near(fixture.Scroller.ScrollValue, 1f),
                "Releasing a captured track press outside must clamp to the endpoint and never close the menu");
            MenuPointerFrame(middle, false, false, false);
            Check(!fixture.ScrollbarGesture && !fixture.Scroller.m_ScrollThumb.IsDragging(),
                "Capture and the native thumb's drag state must both end after release");

            fixture.ScrollbarRects(out rail, out grip);
            float scroll = fixture.Scroller.ScrollValue;
            MenuPointerFrame(grip.center, true, true, false);
            Check(fixture.ScrollbarGesture && fixture.Scroller.m_ScrollThumb.IsDragging() && Near(fixture.Scroller.ScrollValue, scroll),
                "A press at the thumb center must immediately enter native dragging and retain its position");
            MenuPointerFrame(grip.center, false, false, true);
            fixture.ScrollbarRects(out rail, out grip);
            Vector3 gripEdge = new Vector3(grip.center.x, grip.yMax - 0.1f, 0f);
            MenuPointerFrame(gripEdge, true, true, false);
            fixture.ScrollbarRects(out rail, out grip);
            Check(fixture.ScrollbarGesture && fixture.Scroller.m_ScrollThumb.IsDragging()
                && Math.Abs(grip.center.y - gripEdge.y) < 0.02f,
                "Pressing away from the thumb center must jump its center to that point and start dragging immediately");
            MenuPointerFrame(gripEdge, false, false, true);
            BoxCollider nativeRail = fixture.Scroller.m_ScrollTrack;
            Vector3 savedSize = nativeRail.size;
            nativeRail.size = new Vector3(savedSize.x * 3f, savedSize.y, savedSize.z);
            fixture.ScrollbarRects(out rail, out grip);
            Vector3 railEdge = new Vector3(rail.xMax - 0.01f, rail.center.y, 0f);
            MenuPointerFrame(railEdge, true, true, false);
            Check(fixture.ScrollbarGesture && fixture.Scroller.m_ScrollThumb.IsDragging()
                && Near(fixture.Scroller.ScrollValue, 0.5f),
                "The full configured rail hit area must jump and drag even outside the narrower visible track mesh");
            nativeRail.size = savedSize;
            KeywordSetTray.HideImmediately();
            Check(!fixture.ScrollbarGesture && !fixture.Scroller.m_ScrollThumb.IsDragging(),
                "Hiding during a scrollbar press must stop native dragging as well as release menu capture");
            fixture.ClearGesture();
        }
        Input.mousePosition = oldPointer;
        CameraUtils.Camera.DepthSkewZ = oldSkew;
    }

    private static void CheckAutomaticColumnPreference()
    {
        foreach (int capacity in new[] { 4, 8, 12 })
        {
            var medium = Enumerable.Range(0, 65).Select(i => i % 5 == 0).ToArray();
            Check(FilterGridLayout.Choose(medium, 0, 0, capacity, 3).Columns == 2,
                "A medium list with many section headings must stay at two automatic columns at every screen height");
            Check(FilterGridLayout.Choose(new bool[99], 0, 0, capacity, 3).Columns == 2,
                "99 selectable entries must stay at two automatic columns");
            var large = new bool[100];
            Check(FilterGridLayout.Choose(large, 0, 0, capacity, 3).Columns == 3,
                "100 selectable entries must allow a third column through the same policy for every menu");
            var withHeadings = Enumerable.Repeat(false, 99).Concat(Enumerable.Repeat(true, 20)).ToArray();
            Check(FilterGridLayout.Choose(withHeadings, 0, 0, capacity, 3).Columns == 2,
                "Headings must not push a menu across the 100 selectable-entry threshold");
            Check(FilterGridLayout.Choose(medium, 0, 3, capacity, 4).Columns == 3,
                "An explicitly selected column count must take precedence over automatic column preference");
        }
    }

}
