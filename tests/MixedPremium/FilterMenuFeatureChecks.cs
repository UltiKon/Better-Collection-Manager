using System;
using System.Linq;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void CheckFilterMenuFeatures()
    {
        CheckFilterPointerGestures();
        CheckAutomaticColumnPreference();
        int oldWidth = Screen.width, oldHeight = Screen.height;
        float oldPixels = CameraUtils.Camera.PixelScale;
        Vector3 oldOffset = CameraUtils.Camera.Offset;
        Screen.width = 1280; Screen.height = 720;
        CameraUtils.Camera.PixelScale = 100f;
        CameraUtils.Camera.Offset = new Vector3(640f, 360f, 0f);
        CollectionQualitySettings.FilterMenuRows = 0;
        CollectionQualitySettings.FilterMenuColumns = 0;
        var fixture = KeywordSetTray.MakeGridFixture();
        fixture.RebuildCount(4);
        int smallRows = fixture.Rows, smallColumns = fixture.Columns;
        Check(smallRows == 3 && smallColumns == 1, "Automatic must keep a short submenu compact");
        fixture.RebuildCount(65);
        Check(fixture.Rows > smallRows && fixture.Columns > smallColumns,
            "Automatic must resolve separately for a large menu instead of reusing the previous menu's dimensions");
        Check(fixture.Rows <= 12 && fixture.Columns <= 3 && fixture.ScreenRect.width <= Screen.width * 0.52f + 0.01f
            && fixture.ScreenRect.height <= Screen.height * 0.78f + 0.01f && fixture.PublishedHeight > fixture.Scroller.m_ScrollBounds.bounds.size.z,
            "Large Automatic menus must reserve room for collection cards and keep excess entries scrollable");
        Screen.width = 1920; Screen.height = 1080;
        CameraUtils.Camera.Offset = new Vector3(960f, 540f, 0f);
        fixture.RebuildCount(65);
        Check(fixture.Rows == 12 && fixture.Columns == 2 && Near(fixture.Fit, 1f)
            && fixture.ScreenRect.width <= Screen.width * 0.52f + 0.01f
            && fixture.ScreenRect.height <= Screen.height * 0.78f + 0.01f,
            "Medium Automatic lists must tolerate scrolling in two columns to keep more collection cards visible");
        fixture.RebuildCount(105);
        Check(fixture.Rows == 12 && fixture.Columns == 3,
            "Very large lists such as the complete keyword menu must still use three automatic columns");
        fixture.RebuildCount(14);
        fixture.MakePinnedBack();
        Check(fixture.Rows == 12 && fixture.Columns == 1
            && fixture.PublishedHeight > fixture.Scroller.m_ScrollBounds.bounds.size.z,
            "Automatic must tolerate a few overflowing rows to save a whole column for a medium list");
        fixture.RemovePinnedBack();
        fixture.RebuildCount(25);
        Check(fixture.Columns == 2 && fixture.Rows == 12,
            "A small overflow beyond two visible columns must scroll instead of immediately adding a third column");
        Screen.width = 1280; Screen.height = 720;
        CameraUtils.Camera.Offset = new Vector3(640f, 360f, 0f);
        CollectionQualitySettings.FilterMenuColumns = 1;
        fixture.RebuildCount(8);
        Check(fixture.Rows == 7 && Near(fixture.Fit, 1f),
            "Automatic rows must show every entry when a menu exceeds the original six-row capacity but fits on screen");
        fixture.RebuildCount(31);
        Check(fixture.Rows == 8 && Near(fixture.Fit, 1f),
            "Automatic rows must use the screen height after reserving the frame, heading and Back row");
        foreach (int height in new[] { 480, 1080, 720 })
        {
            Screen.height = height;
            CameraUtils.Camera.Offset = new Vector3(640f, height * 0.5f, 0f);
            fixture.RebuildCount(31);
            int expectedRows = height == 480 ? 4 : height == 1080 ? 12 : 8;
            Check(fixture.Rows == expectedRows && Near(fixture.Fit, 1f)
                && fixture.ScreenRect.yMin >= height * 0.08f - 0.01f
                && fixture.ScreenRect.yMax <= height * 0.92f + 0.01f,
                "Automatic rows must adapt to screen height at native item size instead of preserving the original tray height");
        }
        CollectionQualitySettings.FilterMenuColumns = 0;
        CollectionQualitySettings.FilterMenuRows = 2;
        fixture.RebuildCount(4);
        Check(fixture.Rows == 2 && fixture.Columns == 2,
            "Automatic columns must use the fixed row count when choosing enough columns");
        CollectionQualitySettings.FilterMenuRows = 0;
        CollectionQualitySettings.FilterMenuColumns = 3;
        fixture.RebuildCount(4);
        Check(fixture.Rows == 1 && fixture.Columns == 3,
            "Automatic rows must shrink when the entries fit in one fixed-width grid row");
        CollectionQualitySettings.FilterMenuRows = 7;
        fixture.RebuildCount(31);
        float rowWidth = fixture.Scroller.m_ScrollBounds.bounds.size.x * 0.98f;
        foreach (SetFilterItem header in fixture.Items.Where(item => item.IsHeader)) CheckFullWidthTexture(header, rowWidth, fixture.Fit);
        SetFilterItem back = fixture.MakeBack();
        CheckFullWidthTexture(back, rowWidth, fixture.Fit);
        fixture.RemoveBack(back);
        SetFilterItem texturedBack = fixture.MakeBack(true);
        CheckFullWidthTexture(texturedBack, rowWidth, fixture.Fit);
        Check(Near(texturedBack.m_uberText.RenderOnObject.GetComponent<Renderer>().bounds.size.x, 0.8f * fixture.Fit),
            "Render-to-texture output outside the caption hierarchy must not stretch with a full-width button");
        fixture.RemoveBack(texturedBack);

        CheckFilterRowVisuals(fixture);
        CheckPinnedBackHitAreas(fixture);
        CheckFilterInteractions(fixture);
        CheckScrollbarTrackClicks(fixture);
        CheckFilterVisibility(fixture);
        CheckFilterOpeningMask(fixture);
        CheckFilterOutsideClose(fixture);
        CheckFilterInputOwnership(fixture);
        CheckFilterCloseRecovery(fixture);
        CheckFilterRapidToggles(fixture);
        CheckFilterClosingViewport(fixture);
        CheckFilterCloseSound(fixture);
        CheckFilterMenuSession(fixture);
        CheckExpansionGridAndOpeningButton(fixture);
        CheckKeywordMenus(fixture);
        CollectionFilterMenu.Detach();
        KeywordSetTray.HideImmediately();
        CollectionQualitySettings.FilterMenuRows = 0;
        CollectionQualitySettings.FilterMenuColumns = 1;
        Screen.width = oldWidth; Screen.height = oldHeight;
        CameraUtils.Camera.PixelScale = oldPixels; CameraUtils.Camera.Offset = oldOffset;
    }

    private static void CheckFullWidthTexture(SetFilterItem item, float width, float textScale)
    {
        MeshFilter texture = item.GetComponentsInChildren<MeshFilter>(false).Single(mesh => mesh.gameObject.name == "Row face");
        Check(Near(texture.GetComponent<Renderer>().bounds.size.x, width),
            "Back and section headings must extend their actual background texture across the complete row");
        Check(item.m_uberText.FontSize == 35 && Near(item.m_uberText.transform.lossyScale.x, textScale)
            && Near(item.m_uberText.transform.lossyScale.z, textScale),
            "Full-row textures must preserve the native text size and proportions");
        Check(texture.sharedMesh.vertices.Length > 4, "Wide heading textures must preserve their edge strips through mesh slicing");
    }

    private static void CheckPinnedBackHitAreas(KeywordSetTray.GridFixture fixture)
    {
        Vector3 oldPointer = Input.mousePosition;
        float oldSkewX = CameraUtils.Camera.DepthSkewX, oldSkewZ = CameraUtils.Camera.DepthSkewZ;
        foreach (int height in new[] { 720, 480 })
        foreach (int rows in new[] { 0, 16 })
        {
            Screen.height = height;
            CameraUtils.Camera.Offset = new Vector3(640f, height * 0.5f, 0f);
            CameraUtils.Camera.DepthSkewX = rows == 0 ? 0.6f : -0.35f;
            CameraUtils.Camera.DepthSkewZ = height == 720 ? 0.4f : -0.7f;
            CollectionQualitySettings.FilterMenuRows = rows;
            CollectionQualitySettings.FilterMenuColumns = rows == 0 ? 3 : 4;
            fixture.RebuildCount(70);
            SetFilterItem back = fixture.MakePinnedBack();
            Bounds button = fixture.BackBounds;
            Bounds frame = fixture.Frame.GetComponent<Renderer>().bounds;
            Check(Near(button.size.x, fixture.Scroller.m_ScrollBounds.bounds.size.x * 0.98f)
                && Near(fixture.BackHitBounds.size.x, fixture.Scroller.m_ScrollBounds.bounds.size.x * 0.952f),
                "Back must preserve its full visual width and inset only its hit area");
            Check(button.min.x >= frame.min.x && button.max.x <= frame.max.x
                && button.min.z >= frame.min.z && button.max.z <= frame.max.z
                && Near(button.size.z, 0.5f * fixture.Fit),
                "Pinned Back must stay inside a large menu and retain one row height despite huge offscreen font bounds");
            Check(!fixture.Root.GetComponent<CameraMask>().enabled
                && fixture.BackMask.enabled && fixture.BackMask.m_Height > 0f
                && fixture.Scroller.m_ScrollBounds.size.z > 0f,
                "Offscreen font meshes must not swallow the list viewport or prevent its mask from being created");
            Bounds headingFace = fixture.Items[0].GetComponentsInChildren<MeshFilter>(false)
                .Single(mesh => mesh.gameObject.name == "Row face").GetComponent<Renderer>().bounds;
            float clipTop = fixture.BackMask.transform.position.z
                + fixture.BackMask.m_Height * fixture.BackMask.transform.lossyScale.z * 0.5f;
            Check(headingFace.max.z <= clipTop + 0.001f
                && headingFace.min.z >= fixture.Scroller.m_ScrollBounds.bounds.min.z
                && fixture.PublishedHeight < 40f * fixture.Fit,
                "The first heading must remain visible below Back without a giant scroll inset from its offscreen font mesh");
            BoxCollider hit = back.GetComponent<BoxCollider>();
            Check(back.GetComponentsInChildren<Collider>(true).All(c => c == hit || !c.enabled),
                "Back must expose only one bounded hit collider; inherited child hit areas must stay disabled");
            Check(Near(hit.bounds.size.x, fixture.BackHitBounds.size.x) && Near(hit.bounds.size.z, button.size.z)
                && Near(hit.bounds.center.z, button.center.z) && Math.Abs(hit.bounds.center.y - button.center.y) < fixture.Fit * 0.1f,
                "Back's inherited oversized collider must be reset to its visible texture, including its hit depth");
            Input.mousePosition = CameraUtils.Camera.WorldToScreenPoint(button.center);
            Check(fixture.PointerIsOverBack, "The visible Back texture must capture its own pointer position");
            Input.mousePosition = CameraUtils.Camera.WorldToScreenPoint(new Vector3(
                (fixture.BackHitBounds.max.x + button.max.x) * 0.5f, button.center.y, button.center.z));
            Check(!fixture.PointerIsOverBack, "Back's visible side trim must not enlarge its interactive area");
            Input.mousePosition = CameraUtils.Camera.WorldToScreenPoint(fixture.Track.GetComponent<Renderer>().bounds.center);
            Check(!fixture.PointerIsOverBack, "Hovering the scrollbar must never be captured by Back");
            Input.mousePosition = CameraUtils.Camera.WorldToScreenPoint(fixture.Scroller.m_ScrollBounds.bounds.center);
            Check(!fixture.PointerIsOverBack, "Hovering the scrolling list must not disable it as a Back hover");
            Input.mousePosition = new Vector3(Screen.width - 1f, Screen.height - 1f, 0f);
            Check(!fixture.PointerIsOverBack, "Back must not capture input outside the visible menu");
            var behind = new GameObject("Button directly behind Back");
            behind.transform.SetParent(fixture.Root.transform, false);
            behind.AddComponent<PegUIElement>();
            BoxCollider behindHit = behind.AddComponent<BoxCollider>();
            var unrelated = new GameObject("Unrelated list input");
            unrelated.transform.SetParent(fixture.Root.transform, false);
            unrelated.AddComponent<PegUIElement>();
            BoxCollider unrelatedHit = unrelated.AddComponent<BoxCollider>();
            Physics.Hits = new[] { new RaycastHit { collider = behindHit }, new RaycastHit { collider = hit },
                new RaycastHit { collider = fixture.Scroller.m_ScrollBounds } };
            Input.mousePosition = CameraUtils.Camera.WorldToScreenPoint(button.center);
            fixture.BlockBehindBack();
            Check(!behindHit.enabled && unrelatedHit.enabled && hit.enabled && fixture.Scroller.m_ScrollBounds.enabled,
                "Back hover must only block pointer hits behind it, preserving unrelated menu controls and the scroll viewport");
            fixture.RestoreBlocked();
            Check(behindHit.enabled && unrelatedHit.enabled, "Leaving Back must restore the colliders captured behind it");
            Physics.Hits = Array.Empty<RaycastHit>();
            UnityEngine.Object.Destroy(behind); UnityEngine.Object.Destroy(unrelated);
            Vector3 previous = fixture.Root.transform.position;
            fixture.Root.transform.position += new Vector3(-20f, 0f, 0f);
            Input.mousePosition = CameraUtils.Camera.WorldToScreenPoint(fixture.BackBounds.center);
            Check(fixture.PointerIsOverBack, "The bounded Back input region must follow the tray's slide transform");
            fixture.Root.transform.position = previous;
            fixture.RemovePinnedBack();
            Check(fixture.Root.GetComponent<CameraMask>().enabled,
                "Closing Back must restore the original clipping mask for the next menu");
        }
        Input.mousePosition = oldPointer;
        CameraUtils.Camera.DepthSkewX = oldSkewX; CameraUtils.Camera.DepthSkewZ = oldSkewZ;
        Screen.height = 720;
        CameraUtils.Camera.Offset = new Vector3(640f, 360f, 0f);
    }

    private static void CheckExpansionGridAndOpeningButton(KeywordSetTray.GridFixture fixture)
    {
        SetFilterTray native = fixture.Native;
        fixture.ClearGesture();
        native.m_trayObject.SetActive(false);
        int selected = 0;
        bool closeAfterSelection = false;
        for (int i = 0; i < 14; i++)
        {
            var go = new GameObject("Native expansion " + i);
            go.transform.SetParent(native.m_contents.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, -i * 0.5f);
            var source = go.AddComponent<SetFilterItem>();
            source.IsHeader = i == 0 || i == 8;
            source.Text = source.IsHeader ? "Native heading" : "Expansion " + i;
            source.Height = 0.5f;
            source.FormatType = i == 1 ? PegasusShared.FormatType.FT_WILD : PegasusShared.FormatType.FT_STANDARD;
            source.CardSets = i == 1 ? null : new System.Collections.Generic.List<TAG_CARD_SET> { TAG_CARD_SET.TEST_EXPANSION };
            source.IconTexture = new Texture { name = "Expansion icon " + i };
            source.IconOffset = new Vector2(i * 0.01f, i * 0.02f);
            source.SpecificCards = i == 3 ? new System.Collections.Generic.List<int> { 42, 73 } : null;
            // A live selected/hovered row can be partway through a scale tween.
            go.transform.localScale = Vector3.one * (i == 1 ? 0.63f : 0.85f);
            go.AddComponent<UIBScrollableItem>().m_size = new Vector3(1f, 0.1f, 0.5f);
            source.Callback = (sets, cards, format, row, transition) => {
                Check(ReferenceEquals(sets, source.CardSets) && ReferenceEquals(cards, source.SpecificCards)
                    && format == source.FormatType && row == source && !transition && native.m_selected == source,
                    "Native expansion selection must receive the original data and already-selected row, with immediate page refresh");
                selected++;
                KeywordFilterUi.OnSetFilterChanged();
                if (closeAfterSelection) CollectionFilterMenu.HandleNativeShow(native, false);
            };
            native.m_items.Add(source);
        }
        native.m_selected = native.m_items[1];
        var button = new GameObject("Native expansion toggle");
        button.transform.position = new Vector3(0f, 0f, -1.6f);
        button.AddComponent<BoxCollider>().size = new Vector3(1f, 0.01f, 0.3f);
        var toggle = button.AddComponent<CollectionSetFilterDropdownToggle>();
        Check(CollectionFilterMenu.Attach(native, toggle), "The native expansion toggle must attach to the shared grid");
        CollectionQualitySettings.FilterMenuRows = 2;
        CollectionQualitySettings.FilterMenuColumns = 3;
        CollectionFilterMenu.ShowExpansions();
        MelonLoader.MelonCoroutines.Complete();
        Check(KeywordSetTray.IsShown && KeywordSetTray.ActiveKind == FilterTrayKind.Expansions
            && fixture.Rows == 2 && fixture.Columns == 3,
            "Expansions must use the same saved dimensions as all other submenus");
        Check(native.ShowCalls == 0 && !native.m_trayObject.activeSelf && !native.InputCaptured && fixture.Items.Count == 14,
            "Expansions must read initialized native rows without starting a native animation or acquiring a second input capture");
        Check(fixture.Items[1] != native.m_items[1] && fixture.Items[1].Selected
            && fixture.Items.Select(item => item.Text).SequenceEqual(native.m_items.Select(item => item.Text)),
            "Expansion copies must keep native order, headings and selected state");
        Check(fixture.Items.All(row => Near(row.transform.lossyScale.x, fixture.Fit)
                && Near(row.transform.lossyScale.z, fixture.Fit))
            && Near(native.m_items[1].transform.localScale.x, 0.63f),
            "The selected Wild row and other expansions must use normal template size without inheriting or modifying live row animations");
        Bounds wildFace = FilterButtonFace(fixture.Items[1]).GetComponent<Renderer>().bounds;
        Bounds regularFace = FilterButtonFace(fixture.Items[2]).GetComponent<Renderer>().bounds;
        Check(Near(wildFace.size.x, regularFace.size.x) && Near(wildFace.size.z, regularFace.size.z)
            && fixture.Items[1].m_selectedGlow.activeSelf && !fixture.Items[2].m_selectedGlow.activeSelf,
            "A large active selected-glow plane must not become Wild's layout reference or shrink its visible button");
        var background = new GameObject("Collection card behind Expansions");
        BoxCollider backgroundHit = background.AddComponent<BoxCollider>();
        Physics.Hits = new[] { new RaycastHit { collider = backgroundHit } };
        KeywordSetTray.PollOutsideClick();
        Check(!backgroundHit.enabled && fixture.HideAreaActive,
            "Opening Expansions must keep the collection modal through the shared menu's own blocker");
        foreach (SetFilterItem header in fixture.Items.Where(item => item.IsHeader))
            CheckFullWidthTexture(header, fixture.Scroller.m_ScrollBounds.bounds.size.x * 0.98f, fixture.Fit);
        PegUIElement expansionPeg = fixture.Items[2].GetComponent<PegUIElement>();
        BoxCollider expansionHit = fixture.Items[2].GetComponent<BoxCollider>();
        Vector3 expansionRest = fixture.Items[2].transform.localPosition;
        Bounds expansionBounds = expansionHit.bounds;
        Vector3 expansionPointer = CameraUtils.Camera.WorldToScreenPoint(expansionBounds.center);
        SetFilterItem expansionFeedback = fixture.Items[2];
        int pressSounds = SoundManager.Get().Played.Count;
        int beforePressRefreshes = KeywordFilterApplier.Refreshes;
        expansionPeg.Send(UIEventType.PRESS);
        Check(SoundManager.Get().Played.Count == pressSounds + 1
            && KeywordFilterApplier.Refreshes == beforePressRefreshes,
            "Filter press starts its sound before release can begin the collection refresh");
        MenuPointerFrame(expansionPointer, true, true, false);
        fixture.RefreshVisible();
        Check(expansionFeedback.NativePressCalls == 1 && expansionFeedback.m_pressedShadow.activeSelf
            && expansionFeedback.m_pressedShadow.GetComponentsInChildren<Renderer>(true).Single().enabled
            && fixture.Items[2].transform.localPosition.Equals(expansionRest) && Near(expansionHit.bounds.center.z, expansionBounds.center.z),
            "Expansion buttons must retain native press feedback without a second, synthetic animation");
        expansionPeg.RegisteredHit = expansionHit;
        Input.LeftPressed = Input.LeftHeld = false;
        Input.LeftReleased = true;
        Time.frameCount++;
        expansionPeg.ReleaseThrough(expansionHit);
        KeywordSetTray.PollOutsideClick();
        fixture.ClearGesture();
        Check(SoundManager.Get().Played.Count == pressSounds + 1,
            "The native release sound after applying an expansion is suppressed, leaving only the press sound");
        Check(expansionFeedback.NativeReleaseCalls == 1 && expansionFeedback.Selected && expansionFeedback.m_pressedShadow.activeSelf
            && Near(fixture.Items[2].transform.localPosition.z, expansionRest.z),
            "Shared expansion selection must preserve the native release listener exactly once");
        Check(selected == 1 && native.m_selected == native.m_items[2] && fixture.Items[2].Selected,
            "Choosing a copied expansion must call the original native row exactly once and refresh selected chrome");
        Check(ReferenceEquals(toggle.CurrentIcon, native.m_items[2].IconTexture)
            && toggle.CurrentOffset.Equals(native.m_items[2].IconOffset.Value),
            "Choosing an expansion must update the opening button's native icon and texture offset");
        fixture.RefreshVisible();
        Check(!fixture.Items[1].m_selectedGlow.activeSelf && fixture.Items[2].m_selectedGlow.activeSelf
            && Near(FilterButtonFace(fixture.Items[1]).GetComponent<Renderer>().bounds.size.x, wildFace.size.x),
            "Scrolling/visibility refresh must keep Wild deselected and the new row glowing at the original button width");
        CollectionFilterMenu.RestoreExpansionSelectionForBack();
        Check(native.m_selected == native.m_items[2], "Back must retain the most recently chosen expansion");
        fixture.Items[1].GetComponent<PegUIElement>().Release();
        Check(selected == 2 && native.m_selected == native.m_items[1] && fixture.Items[1].Selected,
            "Selecting Wild must forward its null card-set list and native Wild format");
        Check(ReferenceEquals(toggle.CurrentIcon, native.m_items[1].IconTexture),
            "Switching back to Wild must restore Wild's original opening-button logo");
        fixture.Items[3].GetComponent<PegUIElement>().Release();
        Check(selected == 3 && native.m_selected == native.m_items[3] && fixture.Items[3].Selected,
            "A specific-card filter must forward the actual card list rather than losing it during cloning");
        CheckExpansionRefreshCost(fixture);
        fixture.Scroller.ScrollValue = 0.6f;
        Time.frameCount++;
        CollectionFilterMenu.Toggle();
        MelonLoader.MelonCoroutines.Complete();
        Check(backgroundHit.enabled && !fixture.HideAreaActive && !native.InputCaptured,
            "Closing Expansions must restore collection input without leaving a native tray capture behind");
        Time.frameCount++;
        CollectionFilterMenu.Toggle();
        MelonLoader.MelonCoroutines.Complete();
        KeywordSetTray.PollOutsideClick();
        Check(KeywordSetTray.ActiveKind == FilterTrayKind.Expansions && Near(fixture.Scroller.ScrollValue, 0.6f)
            && native.m_selected == native.m_items[3] && fixture.Items[3].Selected
            && native.ShowCalls == 0 && !native.InputCaptured,
            "Reopening Expansions must refresh native rows while retaining scroll position and selected expansion");

        CheckExpansionModeMenu(fixture);
        foreach (int height in new[] { 720, 480 })
        {
            Screen.height = height;
            button.GetComponent<BoxCollider>().enabled = height == 720;
            CameraUtils.Camera.Offset = new Vector3(640f, height * 0.5f, 0f);
            foreach (int rows in new[] { 0, 16 })
            {
                CollectionQualitySettings.FilterMenuRows = rows;
                CollectionQualitySettings.FilterMenuColumns = rows == 0 ? 3 : 4;
                fixture.RebuildCount(70);
                Check(CollectionFilterMenu.TryGetToggleScreenRect(CameraUtils.Camera, out Rect opening)
                    && fixture.ScreenRect.yMin >= opening.yMax + 5.9f,
                    "The complete menu frame must remain above its opening button at every supported layout size");
                if (rows == 0)
                    Check(fixture.Rows == (height == 720 ? 5 : 3) && Near(fixture.Fit, 1f),
                        $"Automatic row capacity must reserve the opening button before choosing rows, without shrinking the text (height={height}, rows={fixture.Rows}, fit={fixture.Fit})");
            }
        }
        fixture.BeginScrollRelease();
        Check(CollectionFilterMenu.HandleNativeShow(native, false) && KeywordSetTray.IsShown,
            "The native hide-area close path must also retain a scroll drag released outside");
        fixture.ClearGesture();
        closeAfterSelection = true;
        SetFilterItem sourceToChoose = native.m_items[4];
        CollectionFilterMenu.SelectExpansion(sourceToChoose);
        MelonLoader.MelonCoroutines.Complete();
        native.FinishClosing();
        Check(selected == 4 && native.m_selected == sourceToChoose && !KeywordSetTray.IsShown
            && !fixture.HideAreaActive && !fixture.Animating && !native.InputCaptured && backgroundHit.enabled,
            "A native close inside expansion selection must apply the new filter and release every modal surface without a native tween capture");
        UnityEngine.Object.Destroy(button);
        Physics.Hits = Array.Empty<RaycastHit>();
        UnityEngine.Object.Destroy(background);
    }

    private static void CheckFilterPointerGestures()
    {
        var gesture = new FilterPointerGesture();
        gesture.Observe(1, true, true, false, 100f, 100f, true);
        gesture.Observe(2, false, true, false, 200f, 100f, false);
        gesture.Observe(3, false, false, true, 200f, 100f, false);
        Check(gesture.Dragged && !gesture.CloseOutside, "Releasing a scrollbar drag outside the menu must keep it open");
        gesture.Observe(3, false, false, true, 200f, 100f, false);
        Check(!gesture.CloseOutside, "Multiple release handlers in one frame must preserve the original capture");
        gesture.Observe(4, true, true, false, 300f, 100f, false);
        gesture.Observe(5, false, false, true, 301f, 100f, false);
        Check(gesture.CloseOutside && !gesture.Dragged, "A fresh ordinary outside click must still close the menu");
        gesture.Observe(6, false, false, false, 301f, 100f, false);
        Check(!gesture.CloseOutside, "Outside-close permission must not leak to another frame");
        gesture.Observe(7, true, true, false, 100f, 100f, true);
        gesture.Observe(8, false, false, true, 101f, 100f, false);
        Check(!gesture.CloseOutside, "A press inside must keep capture even if it crosses the edge by less than the drag threshold");
        gesture.Observe(9, true, true, false, 100f, 100f, false);
        gesture.Observe(10, false, false, true, 101f, 100f, true);
        Check(!gesture.CloseOutside, "Releasing inside after an outside press must not close the menu");
        gesture.Observe(11, true, true, false, 100f, 100f, false);
        gesture.Observe(12, false, false, true, 150f, 100f, false);
        Check(gesture.CloseOutside && gesture.Dragged, "A gesture started and released outside must dismiss the menu even when dragged");
        gesture.Reset();
        gesture.Observe(13, false, false, true, 150f, 100f, false);
        Check(!gesture.CloseOutside, "An orphaned release must not close a newly opened menu");
    }
}
