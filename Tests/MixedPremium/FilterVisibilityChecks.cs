using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BetterCollectionManager;
using UnityEngine;

namespace MelonLoader
{
    public static class MelonCoroutines
    {
        private static readonly List<IEnumerator> Running = new List<IEnumerator>();
        public static int ActiveCount => Running.Count;
        public static object Start(IEnumerator routine) { Running.Add(routine); return routine; }
        public static void Stop(object routine)
        {
            Running.Remove((IEnumerator)routine);
            (routine as IDisposable)?.Dispose();
        }
        public static void AdvanceFrame()
        {
            Time.frameCount++;
            foreach (IEnumerator routine in Running.ToArray())
            {
                bool completed = true;
                try { completed = !routine.MoveNext(); }
                finally
                {
                    if (completed) { Running.Remove(routine); (routine as IDisposable)?.Dispose(); }
                }
            }
        }
        public static void Complete()
        {
            for (int i = 0; i < 100 && Running.Count > 0; i++) AdvanceFrame();
            if (Running.Count > 0) throw new Exception("Filter animation did not complete");
        }
    }
}

internal static partial class Program
{
    private static void CheckFilterOpeningMask(KeywordSetTray.GridFixture fixture)
    {
        var deck = new GameObject("Edited deck mask");
        var deckMask = deck.AddComponent<CameraMask>(); deckMask.enabled = true;
        try
        {
            foreach (FilterTrayKind kind in new[] { FilterTrayKind.Menu, FilterTrayKind.Keywords })
            for (int reopen = 0; reopen < 2; reopen++)
            {
                KeywordSetTray.HideImmediately();
                KeywordSetTray.SetShown(true, kind, "offscreen mask regression");
                Check(!fixture.ActiveMask.enabled,
                    "An offscreen opening must not schedule an unrestricted mask over the edited deck");
                bool entered = false;
                for (int i = 0; i < 50 && MelonLoader.MelonCoroutines.ActiveCount > 0; i++)
                {
                    var mask = fixture.ActiveMask;
                    int updates = mask.MaskUpdates;
                    Vector3 position = mask.transform.position;
                    bool enabled = mask.enabled;
                    MelonLoader.MelonCoroutines.AdvanceFrame();
                    if (mask.enabled)
                    {
                        entered = true;
                        Check((enabled && Vector3.Distance(position, mask.transform.position) < 0.00001f) || mask.MaskUpdates > updates,
                            "A moving visible mask must assign its scissor in the same frame as its new position");
                    }
                    if (mask != fixture.Root.GetComponent<CameraMask>())
                        Check(!fixture.Root.GetComponent<CameraMask>().enabled,
                            "The pinned header must leave only its list mask scheduled");
                }
                Check(entered && fixture.ActiveMask.enabled && fixture.ActiveMask.MaskUpdates > 0,
                    "Entering the screen must restore clipped menu rendering for fresh and cached rows");
                KeywordSetTray.SetShown(false, "offscreen mask regression");
                MelonLoader.MelonCoroutines.Complete();
                Check(!fixture.ActiveMask.enabled, "A fully offscreen closing mask must unschedule its last scissor");
                Check(deckMask.enabled && deckMask.MaskUpdates == 0,
                    "Menu clipping changes must leave the edited deck's own mask untouched");
            }
        }
        finally { KeywordSetTray.HideImmediately(); UnityEngine.Object.Destroy(deck); }
    }

    private static void CheckFilterInputOwnership(KeywordSetTray.GridFixture fixture)
    {
        fixture.ClearGesture();
        CollectionQualitySettings.FilterMenuColumns = 3;
        KeywordSetTray.SetShown(true, FilterTrayKind.Menu, "input ownership");
        MelonLoader.MelonCoroutines.Complete();

        SetFilterItem row = fixture.Items.First(item => !item.IsHeader);
        BoxCollider original = row.GetComponent<BoxCollider>();
        var child = new GameObject("Native registered child hit");
        child.transform.SetParent(row.transform, false);
        BoxCollider registered = child.AddComponent<BoxCollider>();
        registered.center = new Vector3(0.1f, 0.28f, 0.1f);
        registered.size = new Vector3(0.7f, 0.04f, 0.3f);
        var disabledChild = new GameObject("Disabled native child hit");
        disabledChild.transform.SetParent(row.transform, false);
        BoxCollider disabled = disabledChild.AddComponent<BoxCollider>();
        disabled.size = new Vector3(1f, 0.05f, 0.5f);
        disabled.enabled = false;
        fixture.Relayout();
        Check(original == row.GetComponent<BoxCollider>() && original.enabled
            && Near(original.center.y, 0.12f) && Near(original.size.y, 0.06f)
            && registered.enabled && Near(registered.center.y, 0.28f) && Near(registered.size.y, 0.04f)
            && !disabled.enabled,
            "Column layout must preserve native collider identity, depth, thickness and enabled states");
        PegUIElement rowPeg = row.GetComponent<PegUIElement>();
        rowPeg.RegisteredHit = registered;
        int clicks = 0;
        rowPeg.AddEventListener(UIEventType.RELEASE, e => clicks++);
        rowPeg.ReleaseThrough(registered);
        Check(clicks == 1, "PegUI's registered child hit must still deliver button releases after column layout");

        Transform previousParent = fixture.Root.transform.parent;
        var controller = new GameObject("Shared collection input controller");
        controller.AddComponent<PegUIElement>().SetEnabled(true, true);
        BoxCollider ancestorHit = controller.AddComponent<BoxCollider>();
        fixture.Root.transform.SetParent(controller.transform, true);
        var sibling = new GameObject("Shared controller input surface");
        sibling.transform.SetParent(controller.transform, false);
        BoxCollider siblingHit = sibling.AddComponent<BoxCollider>();
        var background = new GameObject("Collection card input");
        background.transform.SetParent(controller.transform, false);
        background.AddComponent<PegUIElement>().SetEnabled(true, true);
        BoxCollider backgroundHit = background.AddComponent<BoxCollider>();
        var toggleOwner = new GameObject("Native filter toggle input parent");
        toggleOwner.transform.SetParent(controller.transform, false);
        toggleOwner.AddComponent<PegUIElement>().SetEnabled(true, true);
        BoxCollider toggleHit = toggleOwner.AddComponent<BoxCollider>();
        var toggle = new GameObject("Native filter toggle component");
        toggle.transform.SetParent(toggleOwner.transform, false);
        CollectionFilterMenu.Attach(fixture.Native, toggle.AddComponent<CollectionSetFilterDropdownToggle>());
        Physics.Hits = new[] { new RaycastHit { collider = ancestorHit }, new RaycastHit { collider = siblingHit },
            new RaycastHit { collider = backgroundHit }, new RaycastHit { collider = toggleHit },
            new RaycastHit { collider = registered } };
        KeywordSetTray.PollOutsideClick();
        Check(ancestorHit.enabled && siblingHit.enabled && toggleHit.enabled && registered.enabled && !backgroundHit.enabled,
            "Modal capture must exclude shared PegUI controllers, native toggle ancestors and every menu button while blocking card input");
        fixture.MakePinnedBack();
        fixture.BlockBehindBack();
        Check(ancestorHit.enabled && siblingHit.enabled && toggleHit.enabled && fixture.Scroller.m_ScrollBounds.enabled,
            "Back capture must also preserve shared controllers, the opening toggle and the scroll viewport");
        fixture.RestoreBlocked();
        var crowded = Enumerable.Range(0, 130).Select(i => new GameObject("Crowded input " + i)).ToArray();
        Physics.Hits = crowded.Select(go => new RaycastHit { collider = go.AddComponent<BoxCollider>() }).ToArray();
        KeywordSetTray.PollOutsideClick();
        Check(crowded.All(go => !go.GetComponent<BoxCollider>().enabled),
            "Modal raycasts grow their reusable buffer instead of dropping hits when more than 64 colliders overlap");
        fixture.Root.SetActive(false);
        KeywordSetTray.PollOutsideClick();
        Check(crowded.All(go => go.GetComponent<BoxCollider>().enabled), "Closing restores every collider captured through a resized raycast buffer");
        foreach (GameObject go in crowded) UnityEngine.Object.Destroy(go);
        Check(backgroundHit.enabled && !fixture.HideAreaActive,
            "An externally hidden menu must release modal capture on the next update");
        Physics.Hits = Array.Empty<RaycastHit>();
        KeywordSetTray.HideImmediately();
        fixture.Root.transform.SetParent(previousParent, true);
        CollectionFilterMenu.Detach();
        UnityEngine.Object.Destroy(controller);
    }

    private static void CheckFilterCloseSound(KeywordSetTray.GridFixture fixture)
    {
        var toggle = new GameObject("Filter sound toggle");
        CollectionFilterMenu.Attach(fixture.Native, toggle.AddComponent<CollectionSetFilterDropdownToggle>());
        UIBHighlight highlight = toggle.AddComponent<UIBHighlight>();
        highlight.m_MouseDownSound = "fixture-configured-button-click";
        foreach (string reason in new[] { "outside", "filter button", "native close" })
        {
            KeywordSetTray.HideImmediately();
            KeywordSetTray.SetShown(true, FilterTrayKind.Menu, "sound regression");
            MelonLoader.MelonCoroutines.Complete();
            int calls = SoundManager.Get().Played.Count;
            if (reason == "outside")
            {
                Time.unscaledTime += 2f; Time.frameCount++;
                Input.mousePosition = new Vector3(-10000, -10000, 0);
                Input.LeftPressed = Input.LeftHeld = true; Input.LeftReleased = false;
                KeywordSetTray.PollOutsideClick();
                Check(SoundManager.Get().Played.Count == calls && KeywordSetTray.IsShown,
                    "An outside press stays silent until the menu actually closes");
                Input.LeftPressed = Input.LeftHeld = false;
            }
            // The native controller may be inactive while our cloned tray is
            // open. UI audio must not depend on either tray's lifetime.
            fixture.Native.gameObject.SetActive(false);
            KeywordSetTray.SetShown(false, reason);
            KeywordSetTray.SetShown(false, reason);
            Check(SoundManager.Get().Played.Count == calls + 1
                && SoundManager.Get().Played.Last() == "choose_opponent_panel_slide_off.prefab:3139d09eb94899d41b9bf612649f47bf",
                "Every actual close must play the native panel slide-off sound exactly once, including duplicate close requests");
            Check(SoundManager.Get().Owners.Last() == null,
                "The panel slide-off sound must remain independent of the native tray's lifetime");
            if (reason == "outside")
            {
                MelonLoader.MelonCoroutines.AdvanceFrame();
                Check(fixture.Animating && fixture.Root.activeSelf,
                    "The audio regression must reopen while the outside-close animation is still running");
                calls = SoundManager.Get().Played.Count;
                Time.frameCount++;
                CollectionFilterMenu.Toggle();
                MelonLoader.MelonCoroutines.Complete();
                Check(KeywordSetTray.IsShown && SoundManager.Get().Played.Count == calls,
                    "Reopening with the toggle must not replay or schedule any synthetic close sound");
                Time.frameCount++;
                CollectionFilterMenu.Toggle();
                MelonLoader.MelonCoroutines.Complete();
                Check(!KeywordSetTray.IsShown && SoundManager.Get().Played.Count == calls + 1
                    && SoundManager.Get().Played.Last() == "choose_opponent_panel_slide_off.prefab:3139d09eb94899d41b9bf612649f47bf",
                    "Toggle dismissal must add the native slide-off sound when the panel actually closes");
            }
            else MelonLoader.MelonCoroutines.Complete();
            fixture.Native.gameObject.SetActive(true);
        }
        KeywordSetTray.SetShown(true, FilterTrayKind.Menu, "silent lifecycle hide");
        MelonLoader.MelonCoroutines.Complete();
        int beforeImmediateHide = SoundManager.Get().Played.Count;
        KeywordSetTray.HideImmediately();
        Check(SoundManager.Get().Played.Count == beforeImmediateHide,
            "Immediate lifecycle cleanup must not play a panel dismissal sound");
        CollectionFilterMenu.Detach();
        UnityEngine.Object.Destroy(toggle);
    }

    private static void CheckFilterClosingViewport(KeywordSetTray.GridFixture fixture)
    {
        foreach (FilterTrayKind kind in new[] { FilterTrayKind.Menu, FilterTrayKind.Keywords })
        foreach (int openingFrames in new[] { 0, 1, 4, 40 })
        {
            KeywordSetTray.HideImmediately();
            KeywordSetTray.SetShown(true, kind, "native visibility regression");
            for (int i = 0; i < openingFrames; i++) MelonLoader.MelonCoroutines.AdvanceFrame();
            fixture.Scroller.UpdateAndFireVisibleAffectedObjects();
            var visible = fixture.Items.ToDictionary(row => row, row => row.gameObject.activeSelf);
            Check(visible.Values.Any(value => value) && visible.Values.Any(value => !value),
                "The closing regression must contain visible buttons and clipped overflow");
            for (int click = 0; click < 12; click++)
            {
                KeywordSetTray.SetShown(!KeywordSetTray.IsShown, kind, "native visibility reversal");
                MelonLoader.MelonCoroutines.AdvanceFrame();
                // Native tweens do this even after ReleaseMenuInput disables
                // the scroller. Row visibility must survive every reversal.
                fixture.Scroller.UpdateAndFireVisibleAffectedObjects();
                Check(fixture.Root.activeSelf && visible.All(row => row.Key.gameObject.activeSelf == row.Value),
                    "Native visibility refreshes during rapid toggling must preserve visible rows and clipped overflow");
                if (!KeywordSetTray.IsShown)
                    Check(fixture.Scroller.m_ScrollBounds.enabled && fixture.OwnInputDisabled,
                        "The closing viewport must remain valid while every button and scrolling input is disabled");
            }
            KeywordSetTray.SetShown(false, "native visibility finish");
            while (MelonLoader.MelonCoroutines.ActiveCount > 0)
            {
                MelonLoader.MelonCoroutines.AdvanceFrame();
                if (!fixture.Root.activeSelf) break;
                fixture.Scroller.UpdateAndFireVisibleAffectedObjects();
                Check(visible.All(row => row.Key.gameObject.activeSelf == row.Value),
                    "Buttons must retain their visibility until the complete panel finishes sliding out");
            }
            Check(!fixture.Root.activeSelf && !fixture.Scroller.m_ScrollBounds.enabled && fixture.OwnInputDisabled,
                "Final hiding must also release the preserved viewport collider");
        }
    }

    private static void CheckFilterRapidToggles(KeywordSetTray.GridFixture fixture)
    {
        Vector3 originalHidden = fixture.Native.m_hideBone.transform.localPosition;
        var toggle = new GameObject("Rapid filter toggle");
        CollectionFilterMenu.Attach(fixture.Native, toggle.AddComponent<CollectionSetFilterDropdownToggle>());
        fixture.Native.m_hideBone.transform.localPosition = new Vector3(-0.5f, 0f, 0f);
        foreach (FilterTrayKind kind in new[] { FilterTrayKind.Menu, FilterTrayKind.Keywords })
        foreach (int openingFrames in new[] { 0, 1, 4, 40 })
        {
            KeywordSetTray.HideImmediately();
            fixture.ClearGesture();
            KeywordSetTray.SetShown(true, kind, "rapid opening");
            for (int i = 0; i < openingFrames; i++) MelonLoader.MelonCoroutines.AdvanceFrame();
            Vector3 position = fixture.Root.transform.localPosition;
            SetFilterItem[] rows = fixture.Items.ToArray();
            int rebuilds = KeywordSetTray.FixtureRebuilds;
            Vector3 expectedHidden = fixture.HiddenPosition;
            KeywordSetTray.SetShown(false, "rapid closing");
            Check(fixture.Root.activeSelf && fixture.Root.transform.localPosition.Equals(position),
                "Closing during opening must keep the panel and buttons at their current pose");
            MelonLoader.MelonCoroutines.AdvanceFrame();
            KeywordSetTray.SetShown(false, "duplicate closing");
            Check(fixture.Root.activeSelf && MelonLoader.MelonCoroutines.ActiveCount == 1,
                "A repeated close must not run the old animation's hide cleanup or restart the animation");
            KeywordSetTray.SetShown(true, kind, "reverse closing");
            Check(KeywordSetTray.IsShown && fixture.Root.activeSelf && fixture.Root.transform.localPosition.Equals(position)
                && MelonLoader.MelonCoroutines.ActiveCount == 1 && fixture.Items.SequenceEqual(rows)
                && KeywordSetTray.FixtureRebuilds == rebuilds,
                "Reopening during closing must reverse from the current pose without replacing or hiding buttons");
            for (int click = 0; click < 12; click++)
            {
                MelonLoader.MelonCoroutines.AdvanceFrame();
                position = fixture.Root.transform.localPosition;
                bool wantOpen = !KeywordSetTray.IsShown;
                if (!wantOpen && click % 4 == 0)
                    CollectionFilterMenu.HandleNativeShow(fixture.Native, false);
                else CollectionFilterMenu.Toggle();
                Check(KeywordSetTray.IsShown == wantOpen && fixture.Root.activeSelf
                    && fixture.Root.transform.localPosition.Equals(position) && MelonLoader.MelonCoroutines.ActiveCount == 1
                    && rows.All(row => !row.Destroyed && row.gameObject.activeSelf),
                    "Rapid reversals must retain exactly one animation and preserve the visible button objects");
            }
            KeywordSetTray.SetShown(true, kind, "settle open");
            MelonLoader.MelonCoroutines.Complete();
            Check(fixture.Root.transform.localPosition.Equals(fixture.ShownPosition) && KeywordSetTray.IsShown,
                "Rapid toggling must settle at the original shown anchor");
            KeywordSetTray.SetShown(false, "settle closed");
            Vector3 previous = fixture.Root.transform.localPosition;
            while (MelonLoader.MelonCoroutines.ActiveCount > 0)
            {
                MelonLoader.MelonCoroutines.AdvanceFrame();
                Vector3 current = fixture.Root.transform.localPosition;
                Check(current.x <= previous.x + 0.0001f,
                    "The closing panel must continue toward its hidden anchor instead of reversing to the right");
                previous = current;
            }
            Check(!fixture.Root.activeSelf && fixture.Root.transform.localPosition.Equals(expectedHidden)
                && !fixture.HideAreaActive && !fixture.Animating,
                "Closing after rapid toggling must keep its original hidden anchor and release modal input");
        }
        fixture.Native.m_hideBone.transform.localPosition = originalHidden;
        KeywordSetTray.HideImmediately();
        CollectionFilterMenu.Detach();
        UnityEngine.Object.Destroy(toggle);
    }

    private static void CheckFilterCloseRecovery(KeywordSetTray.GridFixture fixture)
    {
        foreach (bool throwOnCleanup in new[] { false, true })
        {
            fixture.ClearGesture();
            KeywordSetTray.SetShown(true, FilterTrayKind.Menu, "close recovery");
            MelonLoader.MelonCoroutines.Complete();
            var background = new GameObject("Collection input during interrupted closing");
            BoxCollider hit = background.AddComponent<BoxCollider>();
            Physics.Hits = new[] { new RaycastHit { collider = hit } };
            KeywordSetTray.PollOutsideClick();
            KeywordSetTray.SetShown(false, "close recovery");
            Check(!hit.enabled && fixture.HideAreaActive, "Closing must retain modality until the animation ends or is interrupted");
            MelonLoader.MelonCoroutines.AdvanceFrame();
            if (throwOnCleanup)
            {
                fixture.Root.GetComponent<PegUIElement>().ThrowOnDisable = true;
                bool threw = false;
                try { MelonLoader.MelonCoroutines.Complete(); }
                catch (InvalidOperationException) { threw = true; }
                Check(threw, "The recovery test must exercise an actual native cleanup exception");
            }
            else fixture.StopAnimation();
            Check(!fixture.Root.activeSelf && !fixture.HideAreaActive && !fixture.Animating && hit.enabled,
                "Cancelled animations and native cleanup exceptions must both release the collection and hide every modal surface");
            KeywordSetTray.PollOutsideClick();
            Check(hit.enabled, "The next update must not recapture collection input after close recovery");
            Physics.Hits = Array.Empty<RaycastHit>();
            KeywordSetTray.HideImmediately();
            UnityEngine.Object.Destroy(background);
        }
    }

    private static void CheckFilterVisibility(KeywordSetTray.GridFixture fixture)
    {
        CollectionQualitySettings.FilterMenuRows = 0;
        CollectionQualitySettings.FilterMenuColumns = 3;
        Vector3 oldPointer = Input.mousePosition;
        foreach (int closePath in new[] { 0, 1, 2, 3, 4 })
        {
            KeywordSetTray.SetShown(true, closePath == 4 ? FilterTrayKind.Menu : FilterTrayKind.Keywords, "visibility regression");
            if (closePath != 2) MelonLoader.MelonCoroutines.Complete();
            if (closePath != 4) fixture.MakePinnedBack();
            var collectionButton = new GameObject("Collection control behind menu");
            collectionButton.AddComponent<PegUIElement>();
            BoxCollider collectionHit = collectionButton.AddComponent<BoxCollider>();
            Physics.Hits = new[] { new RaycastHit { collider = collectionHit } };
            if (closePath != 4)
            {
                Input.mousePosition = CameraUtils.Camera.WorldToScreenPoint(fixture.BackBounds.center);
                fixture.CaptureBackInput();
                Check(!collectionHit.enabled && fixture.HasInputCapture && fixture.HideAreaActive,
                    "The closure regression must start with actual Back capture and a live outside-click area");
            }
            else
                Check(collectionHit.enabled && !fixture.HasInputCapture && fixture.HideAreaActive,
                    "The top-level menu closure must start with a live outside-click area and no Back button");
            if (closePath == 1) KeywordSetTray.HideImmediately();
            else KeywordSetTray.SetShown(false, "visibility regression");
            Check(!fixture.HasInputCapture && !KeywordSetTray.IsShown
                && (closePath == 1 ? collectionHit.enabled && !fixture.HideAreaActive : fixture.HideAreaActive),
                "Closing must clear Back navigation but retain the collection blocker until the closing animation finishes");
            if (closePath == 3)
            {
                MelonLoader.MelonCoroutines.AdvanceFrame();
                KeywordSetTray.HideImmediately();
            }
            MelonLoader.MelonCoroutines.Complete();
            Check(!fixture.Root.activeSelf && !fixture.Animating && !fixture.HasInputCapture
                && !fixture.HideAreaActive && collectionHit.enabled,
                "Completed or interrupted closing must leave no invisible menu or retained input capture");
            KeywordSetTray.SetShown(false, "repeated close");
            MelonLoader.MelonCoroutines.Complete();
            Check(!fixture.Root.activeSelf && !fixture.HasInputCapture && collectionHit.enabled,
                "Repeated closing must preserve the collection's restored input");
            Physics.Hits = Array.Empty<RaycastHit>();
            UnityEngine.Object.Destroy(collectionButton);
        }
        Input.mousePosition = oldPointer;
    }

    private static void CheckFilterOutsideClose(KeywordSetTray.GridFixture fixture)
    {
        Vector3 oldPointer = Input.mousePosition;
        float oldTime = Time.unscaledTime;
        foreach (FilterTrayKind kind in new[] { FilterTrayKind.Menu, FilterTrayKind.Keywords })
        foreach (bool eventFirst in new[] { true, false })
        foreach (bool dragged in new[] { false, true })
        {
            fixture.ClearGesture();
            KeywordSetTray.SetShown(true, kind, "outside click regression");
            MelonLoader.MelonCoroutines.Complete();
            if (kind != FilterTrayKind.Menu) fixture.MakePinnedBack();
            Check(fixture.Scroller.enabled && fixture.Scroller.m_OverridePegUI
                && fixture.Root.GetComponent<PegUIElement>().IsEnabled()
                && fixture.Items.Where(row => row.IsHeader).All(row => !row.GetComponent<PegUIElement>().IsEnabled()),
                "Reopening must restore native chrome input and scrolling while keeping section headings noninteractive");
            var collectionControl = new GameObject("Collection control after outside close");
            PegUIElement collectionPeg = collectionControl.AddComponent<PegUIElement>();
            collectionPeg.SetEnabled(true, true);
            BoxCollider collectionHit = collectionControl.AddComponent<BoxCollider>();
            int collectionClicks = 0;
            collectionPeg.AddEventListener(UIEventType.RELEASE, e => collectionClicks++);

            Rect frame = fixture.ScreenRect;
            SetFilterItem clipped = fixture.Items.First(row => CameraUtils.Camera.WorldToScreenPoint(row.transform.position).y < frame.yMin);
            BoxCollider hiddenHit = clipped.gameObject.AddComponent<BoxCollider>();
            hiddenHit.size = new Vector3(1f, 0.01f, 0.5f);
            Physics.Hits = new[] { new RaycastHit { collider = hiddenHit }, new RaycastHit { collider = collectionHit } };
            Input.mousePosition = new Vector3(frame.xMax + 2f, frame.center.y, 0);
            Time.unscaledTime += 2f;
            Time.frameCount++;
            Input.LeftPressed = Input.LeftHeld = true;
            Input.LeftReleased = false;
            int sounds = SoundManager.Get().Played.Count;
            KeywordSetTray.PollOutsideClick();
            Check(SoundManager.Get().Played.Count == sounds, "The outside-dismiss press stays silent before closing begins");
            Check(KeywordSetTray.IsShown && fixture.HideAreaActive && !collectionHit.enabled && hiddenHit.enabled,
                "An outside press must block collection input while preserving the menu's own controls until release");

            Time.frameCount++;
            if (dragged) Input.mousePosition += new Vector3(30f, 40f, 0);
            Input.LeftPressed = Input.LeftHeld = false;
            Input.LeftReleased = true;
            if (eventFirst) fixture.ReleaseOutside();
            KeywordSetTray.PollOutsideClick();
            if (!eventFirst) fixture.ReleaseOutside();
            Check(SoundManager.Get().Played.Count == sounds + 1
                && SoundManager.Get().Owners.Last() == null
                && SoundManager.Get().Played.Last() == "choose_opponent_panel_slide_off.prefab:3139d09eb94899d41b9bf612649f47bf",
                "An outside close must play the native panel slide-off sound exactly once in either event order: " + kind + " drag=" + dragged + " eventFirst=" + eventFirst + " sounds=" + (SoundManager.Get().Played.Count - sounds) + " shown=" + KeywordSetTray.IsShown + " pointer=" + Input.mousePosition);
            Check(!KeywordSetTray.IsShown && fixture.HideAreaActive && !fixture.HasInputCapture
                && fixture.OwnInputDisabled && !collectionHit.enabled,
                "Actual outside release must disable menu buttons but retain the background blocker throughout closing, in either event order");
            fixture.ClearGesture();
            Physics.Hits = Array.Empty<RaycastHit>();
            MelonLoader.MelonCoroutines.Complete();
            KeywordSetTray.PollOutsideClick();
            Check(!fixture.Root.activeSelf && !fixture.HideAreaActive && fixture.OwnInputDisabled && collectionHit.enabled && collectionPeg.IsEnabled(),
                "An outside-closed menu must not retain collection input after its animation finishes");
            collectionPeg.Release();
            Check(collectionClicks == 1, "The collection control must accept the next click after an outside close");
            UnityEngine.Object.Destroy(collectionControl);
        }
        Input.mousePosition = oldPointer;
        Time.unscaledTime = oldTime;
    }

    private static void CheckFilterMenuSession(KeywordSetTray.GridFixture fixture)
    {
        KeywordSetTray.ResetMenuSession();
        Physics.Hits = Array.Empty<RaycastHit>();
        var toggleObject = new GameObject("Session toggle");
        BoxCollider toggleHit = toggleObject.AddComponent<BoxCollider>();
        CollectionFilterMenu.Attach(fixture.Native, toggleObject.AddComponent<CollectionSetFilterDropdownToggle>());
        Time.frameCount++;
        CollectionFilterMenu.Toggle();
        MelonLoader.MelonCoroutines.Complete();
        Check(KeywordSetTray.ActiveKind == FilterTrayKind.Menu && Near(fixture.Scroller.ScrollValue, 0f),
            "A fresh session must start at the category menu's beginning");
        fixture.Scroller.ScrollValue = 0.5f;
        CollectionFilterMenu.ShowFilter(FilterTrayKind.Type);
        Check(Near(fixture.Scroller.ScrollValue, 0f), "A newly visited submenu must start at its first entry");
        fixture.Scroller.ScrollValue = 0.25f;
        Time.frameCount++;
        CollectionFilterMenu.Toggle();
        MelonLoader.MelonCoroutines.Complete();
        Time.frameCount++;
        CollectionFilterMenu.Toggle();
        MelonLoader.MelonCoroutines.Complete();
        Check(KeywordSetTray.ActiveKind == FilterTrayKind.Type && Near(fixture.Scroller.ScrollValue, 0.25f),
            "The opening button must resume the last submenu and its scroll position");

        var blocked = new GameObject("Collection hit beneath modal menu");
        Collider collectionHit = blocked.AddComponent<BoxCollider>();
        Collider ownHit = fixture.Scroller.m_ScrollBounds;
        Physics.Hits = new[] { new RaycastHit { collider = collectionHit }, new RaycastHit { collider = ownHit },
            new RaycastHit { collider = toggleHit } };
        KeywordSetTray.PollOutsideClick();
        Check(!collectionHit.enabled && ownHit.enabled && toggleHit.enabled,
            "A visible menu must block collection hits while retaining its own scrolling and the native opening toggle");
        Physics.Hits = Array.Empty<RaycastHit>();

        CollectionFilterMenu.ShowFilter(FilterTrayKind.Race);
        fixture.Scroller.ScrollValue = 0.75f;
        CollectionFilterMenu.ShowFilter(FilterTrayKind.Type);
        Check(Near(fixture.Scroller.ScrollValue, 0.25f), "Each submenu must retain its own scroll position");
        CollectionFilterMenu.ShowFilter(FilterTrayKind.Race);
        Check(Near(fixture.Scroller.ScrollValue, 0.75f), "Returning to a visited submenu must restore its independent position");
        foreach (FilterTrayKind kind in new[] { FilterTrayKind.Race, FilterTrayKind.Keywords })
        foreach (int closePath in new[] { 0, 1, 2 })
        {
            CollectionFilterMenu.ShowFilter(kind);
            MelonLoader.MelonCoroutines.Complete();
            fixture.MakePinnedBack();
            fixture.Scroller.SetScrollImmediate(0.65f);
            if (closePath == 0)
            {
                Time.frameCount++;
                CollectionFilterMenu.Toggle();
                MelonLoader.MelonCoroutines.Complete();
            }
            else if (closePath == 1) KeywordSetTray.HideImmediately();
            else CollectionFilterMenu.HandleNativeShow(fixture.Native, false);

            CollectionFilterMenu.Reopen();
            MelonLoader.MelonCoroutines.AdvanceFrame();
            // Exercise the real pinned header's reset after the initial
            // restore, like a native scroller's deferred initialization.
            fixture.MakePinnedBack();
            Check(Near(fixture.Scroller.ScrollValue, 0f), "The regression must exercise a late scroller reset while opening");
            MelonLoader.MelonCoroutines.Complete();
            Check(KeywordSetTray.ActiveKind == kind && Near(fixture.Scroller.ScrollValue, 0.65f)
                && Near(fixture.Scroller.ImmediateScrollValue, 0.65f),
                "The keyword list and ordinary submenus must restore scroll after late initialization on all close paths");
            Bounds rail = fixture.Scroller.m_ScrollTrack.bounds;
            Check(Near(fixture.Thumb.transform.position.z, rail.max.z - rail.size.z * 0.65f),
                "Restoring scroll must update the visible thumb at the final tray pose");

            KeywordSetTray.HideImmediately();
            CollectionFilterMenu.Reopen();
            MelonLoader.MelonCoroutines.AdvanceFrame();
            fixture.Scroller.SetScrollImmediate(0f);
            KeywordSetTray.HideImmediately();
            CollectionFilterMenu.Reopen();
            MelonLoader.MelonCoroutines.Complete();
            Check(Near(fixture.Scroller.ScrollValue, 0.65f),
                "Closing during opening must preserve the requested scroll rather than saving a transient native zero");
        }
        CollectionFilterMenu.ShowMenu();
        Check(Near(fixture.Scroller.ScrollValue, 0.5f), "Back must preserve the category menu's previous scroll position");
        KeywordSetTray.HideImmediately();
        Check(collectionHit.enabled, "Immediate hiding must restore collection input held by the modal menu");
        KeywordSetTray.ResetMenuSession();
        Time.frameCount++;
        CollectionFilterMenu.Toggle();
        MelonLoader.MelonCoroutines.Complete();
        Check(KeywordSetTray.ActiveKind == FilterTrayKind.Menu && Near(fixture.Scroller.ScrollValue, 0f),
            "Starting a new session must discard the previous menu and all remembered scroll positions");
        CollectionFilterMenu.ShowFilter(FilterTrayKind.Type);
        Check(Near(fixture.Scroller.ScrollValue, 0f), "A new session must not restore old submenu positions");
        KeywordSetTray.HideImmediately();
        KeywordSetTray.ResetMenuSession();
        CollectionFilterMenu.Detach();
        UnityEngine.Object.Destroy(blocked);
        UnityEngine.Object.Destroy(toggleObject);
    }
}
