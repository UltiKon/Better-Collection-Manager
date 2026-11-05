using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BetterCollectionManager
{
    internal sealed class QualityAddRequest
    {
        public CollectionDeck RootDeck, Context;
        public EntityDef Definition;
        public TAG_PREMIUM Requested;
        public IReadOnlyList<CollectibleCard> Variants;
        public bool IsDrag, PlaySound, PromptAfterDrop;
        public DeckRule.RuleType[] IgnoreRules;
        public Action<TAG_PREMIUM> Confirmed;
        public Action Canceled;
        public Func<bool> ValidateContext;
        public string Title;
    }

    /// <summary>Quality choice for explicit collection-to-deck gestures only.</summary>
    internal static class QualityDeckInput
    {
        private static QualityAddRequest s_action, s_drag, s_pending;
        private static int s_showAfterFrame;
        private static bool s_committing, s_refreshPending;
        private static readonly MethodInfo s_cancelDrop = typeof(CollectionInputMgr).GetMethod("DropCard", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(bool) }, null);
        private static readonly DeckRule.RuleType[] s_lockRules = {
            DeckRule.RuleType.DECK_SIZE, DeckRule.RuleType.DEATHKNIGHT_RUNE_LIMIT,
            DeckRule.RuleType.TOURIST_LIMIT, DeckRule.RuleType.PLAYER_OWNS_EACH_COPY
        };

        private static bool IsEditing()
        {
            CollectionManager manager = CollectionManager.Get();
            CollectibleDisplay display = manager?.GetCollectibleDisplay();
            return PreferredQualityMode.IsEnabled && PreferredPremiumPrefs.CollapseEnabled
                && manager != null && manager.IsInEditMode() && manager.GetEditedDeck() != null
                && CollectionDeckTray.Get() != null && display != null
                && display.GetViewMode() == CollectionUtils.ViewMode.CARDS
                && display.GetViewSubmode() == CollectionUtils.ViewSubmode.DEFAULT
                && !CraftingManager.GetIsInCraftingMode() && !(CraftingTray.Get()?.IsShown() ?? false)
                && UniversalInputManager.Get() != null && !UniversalInputManager.Get().IsTouchMode();
        }

        public static void Initialize()
        {
            // Native events also cover pickup call sites already compiled before
            // Harmony installs its hooks. Subscribe once, independent of patches.
            CollectionInputMgr.CollectionDraggableCardGrabbed -= OnNativeGrabbed;
            CollectionInputMgr.CollectionDraggableCardGrabbed += OnNativeGrabbed;
            CollectionInputMgr.CollectionDraggableCardDropped -= ClearDrag;
            CollectionInputMgr.CollectionDraggableCardDropped += ClearDrag;
        }

        private static void OnNativeGrabbed(CollectionCardVisual visual)
        {
            try
            {
                s_drag = Capture(visual, true, false);
                DebugLog.Msg("Native collection pickup: " + visual?.CardId + ", quality capture=" + (s_drag != null));
            }
            catch (Exception ex) { s_drag = null; DebugLog.Exception("Native collection pickup capture failed", ex); }
        }

        private static QualityAddRequest Capture(CollectionCardVisual visual, bool drag, bool requireShown = true)
        {
            if (ComboCollectionCards.IsCombo(visual)) return null;
            if (visual == null || (requireShown && !visual.IsShown()) || visual.GetVisualType() != CollectionUtils.ViewMode.CARDS
                || !IsEditing() || !QualityOptions.HasAddSettings || !PreferredQualityCollapse.IsSingleTile(visual.CardId)
                || !PreferredQualityCollapse.TryGetVariants(visual.CardId, out var variants)) return null;
            Actor actor = visual.GetActor();
            if (actor?.GetEntityDef() == null) return null;
            return new QualityAddRequest { RootDeck = CollectionManager.Get().GetEditedDeck(),
                Context = CollectionDeckTray.Get().GetCurrentDeckContext(), Definition = actor.GetEntityDef(),
                Requested = actor.GetPremium(), Variants = variants, IsDrag = drag };
        }

        internal static bool IsValid(QualityAddRequest request) => request != null && (request.ValidateContext != null ? request.ValidateContext() : IsEditing()
            && ReferenceEquals(CollectionManager.Get().GetEditedDeck(), request.RootDeck)
            && ReferenceEquals(CollectionDeckTray.Get().GetCurrentDeckContext(), request.Context)
            && request.Context != null);

        public static QualityAddRequest BeginClick(CollectionCardVisual visual)
        {
            QualityAddRequest previous = s_action;
            s_action = null;
            try { s_action = Capture(visual, false); }
            catch (Exception ex) { DebugLog.Exception("Collection quality click setup failed", ex); }
            return previous;
        }
        public static void EndClick(QualityAddRequest previous) => s_action = previous;
        public static QualityAddRequest PrepareDrag(CollectionCardVisual visual) => Capture(visual, true);
        public static void CompleteDrag(QualityAddRequest request, bool grabbed) => s_drag = grabbed ? request : null;
        public static void CaptureDrag(CollectionCardVisual visual, bool grabbed) => CompleteDrag(grabbed ? PrepareDrag(visual) : null, grabbed);
        public static void ClearDrag() => s_drag = null;

        // Consume the mouse release before the native held-card update inserts
        // anything. Cancel the held visual through the game's own cleanup, then
        // show the available variants (or apply the automatic quality policy).
        public static bool TryHandleRelease(CollectionInputMgr input)
        {
            if (!Input.GetMouseButtonUp(0) || input == null || s_drag == null
                || !input.HasHeldCard() || s_cancelDrop == null) return false;
            CollectionDraggableCardVisual held = input.GetHeldCardVisual();
            if (held == null || held.GetSlot() != null || held.GetVisualType() != CollectionUtils.ViewMode.CARDS
                || held.GetCurrentlyShowingEntityDef()?.GetCardId() != s_drag.Definition.GetCardId()) return false;
            bool valid = IsValid(s_drag);
            if (valid && (Box.Get() == null || !CollectionDeckTray.Get().MouseIsOver(Box.Get().GetCamera()))) return false;
            QualityAddRequest request = s_drag;
            s_drag = null;
            try
            {
                s_cancelDrop.Invoke(input, new object[] { true });
                if (!IsValid(request)) return true;
                request.PlaySound = true;
                request.IgnoreRules = new[] { DeckRule.RuleType.DEATHKNIGHT_RUNE_LIMIT };
                request.PromptAfterDrop = QualityOptions.EffectiveDrag == DragQualityMode.ChooseOnDrop;
                s_pending = request;
                s_showAfterFrame = Time.frameCount + 1;
                DebugLog.Msg("Collection release intercepted: " + request.Definition.GetCardId()
                    + ", drag quality=" + QualityOptions.EffectiveDrag);
            }
            catch (Exception ex) { s_pending = null; DebugLog.Exception("Collection release selection failed", ex); }
            return true;
        }

        // Handle collection drops before native DropCard calls AddCard. Native
        // cleanup still runs, but must not insert a second, unselected quality.
        public static bool PrepareDrop(bool canceled, bool overDeck)
        {
            if (canceled || !overDeck || s_drag == null) return false;
            QualityAddRequest request = s_drag;
            if (IsValid(request) && s_pending == null)
            {
                request.PlaySound = true;
                request.IgnoreRules = new[] { DeckRule.RuleType.DEATHKNIGHT_RUNE_LIMIT };
                request.PromptAfterDrop = QualityOptions.EffectiveDrag == DragQualityMode.ChooseOnDrop;
                s_pending = request;
                s_showAfterFrame = Time.frameCount + 1;
            }
            return true;
        }
        public static QualityAddRequest BeginDrop(bool canceled)
        {
            QualityAddRequest previous = s_action;
            s_action = canceled ? null : s_drag;
            return previous;
        }
        public static void EndDrop(QualityAddRequest previous, bool failed)
        {
            if (failed) s_pending = null;
            s_action = previous;
            s_drag = null;
        }

        internal static int Remaining(QualityAddRequest request, TAG_PREMIUM premium)
        {
            foreach (CollectibleCard variant in request.Variants)
                if (variant.PremiumType == premium)
                {
                    string id = request.Definition.GetCardId();
                    CollectionManager manager = CollectionManager.Get();
                    int owned = manager.GetOwnedCount(id, premium) + manager.GetTrialCardCount(id, premium);
                    return Math.Max(0, owned - request.RootDeck.GetCardCountAllMatchingSlots(id, premium));
                }
            return 0; // Never expose a quality excluded by the active collection search.
        }

        private static bool Legal(QualityAddRequest request, TAG_PREMIUM premium, DeckRule.RuleType[] ignoreRules)
        {
            DeckRuleset rules = CollectionManager.Get().GetDeckRuleset();
            return rules == null || rules.CanAddToDeck(request.Definition, premium, request.Context,
                out List<RuleInvalidReason> reasons, out List<DeckRule> broken, ignoreRules ?? new DeckRule.RuleType[0]);
        }

        internal static List<TAG_PREMIUM> Choices(QualityAddRequest request, DeckRule.RuleType[] ignoreRules)
        {
            ClickQualityMode policy = QualityOptions.EffectiveClick;
            if (request.IsDrag)
            {
                if (QualityOptions.EffectiveDrag == DragQualityMode.ChooseOnDrop) policy = ClickQualityMode.PreferredOrder;
                else if (QualityOptions.EffectiveDrag == DragQualityMode.DraggedHalf) policy = ClickQualityMode.ClickedHalf;
            }
            bool useRequested = policy == ClickQualityMode.ClickedHalf;
            bool preferComplete = policy == ClickQualityMode.HighestFullyAvailable;
            int playset = (TAG_RARITY)request.Definition.GetTag(GAME_TAG.RARITY) == TAG_RARITY.LEGENDARY ? 1 : 2;
            CollectionManager manager = CollectionManager.Get();
            string id = request.Definition.GetCardId();
            return QualityOptions.Available(PreferredPremiumPrefs.Order, request.Requested, useRequested, preferComplete,
                premium => Remaining(request, premium) > 0 && Legal(request, premium, ignoreRules),
                // Count the owned playset, not just unused copies: after the
                // first addition, the second must still prefer the same pair.
                premium => manager.GetOwnedCount(id, premium) + manager.GetTrialCardCount(id, premium) >= playset);
        }

        // Return true to run the original AddCard. Native deck validation,
        // animations and limits still perform the actual insertion.
        public static bool RouteAdd(EntityDef definition, ref TAG_PREMIUM premium, bool playSound,
            ref Actor animateActor, DeckRule.RuleType[] ignoreRules, ref bool result)
        {
            QualityAddRequest request = s_action;
            if (s_committing || request == null || definition == null
                || definition.GetCardId() != request.Definition.GetCardId()) return true;
            if (!IsValid(request)) { result = false; return false; }
            if (s_pending != null) { result = false; return false; }
            List<TAG_PREMIUM> choices = Choices(request, ignoreRules);
            if (choices.Count == 0) { result = false; return false; }
            if (request.IsDrag && CollectionQualitySettings.Drag == DragQualityMode.ChooseOnDrop && choices.Count > 1)
            {
                request.IgnoreRules = ignoreRules == null ? new DeckRule.RuleType[0] : (DeckRule.RuleType[])ignoreRules.Clone();
                request.PlaySound = playSound;
                request.PromptAfterDrop = true;
                s_pending = request;
                s_showAfterFrame = Time.frameCount + 1;
                result = false;
                return false;
            }
            TAG_PREMIUM selected = choices[0];
            if (premium != selected) animateActor = null; // Do not animate the wrong quality into the deck.
            premium = selected;
            return true;
        }

        public static void Confirm(QualityAddRequest request, TAG_PREMIUM premium)
        {
            if (request?.Confirmed != null)
            { if (IsValid(request)) request.Confirmed(premium); return; }
            if (!IsValid(request) || Remaining(request, premium) <= 0 || !Legal(request, premium, request.IgnoreRules)) return;
            try
            {
                s_committing = true;
                CollectionDeckTray tray = CollectionDeckTray.Get();
                if (tray.AddCard(request.Definition, premium, request.PlaySound, null, request.IgnoreRules))
                    tray.OnCardManuallyAddedByUser_CheckSuggestions(request.Definition);
            }
            finally { s_committing = false; }
        }

        public static void AdjustLock(CollectionCardVisual visual, ref CollectionCardVisual.LockType lockType, ref string reason)
        {
            if (lockType != CollectionCardVisual.LockType.NO_MORE_INSTANCES) return;
            QualityAddRequest request = Capture(visual, false);
            if (request == null) return;
            CollectionManager manager = CollectionManager.Get();
            string id = request.Definition.GetCardId();
            int total = 0;
            foreach (CollectibleCard variant in request.Variants)
                total += manager.GetOwnedCount(id, variant.PremiumType) + manager.GetTrialCardCount(id, variant.PremiumType);
            if (total <= 1) return;
            // Only the per-quality inventory frame is suppressed. Re-evaluate
            // all other native rules so copy limits, bans and class rules stay.
            DeckRuleset rules = manager.GetDeckRuleset();
            List<DeckRule> rejected = null;
            List<RuleInvalidReason> rejectionReasons = null;
            foreach (CollectibleCard variant in request.Variants)
            {
                TAG_PREMIUM premium = variant.PremiumType;
                if (manager.GetOwnedCount(id, premium) + manager.GetTrialCardCount(id, premium) <= 0) continue;
                if (rules == null || rules.CanAddToDeck(request.Definition, premium, request.Context,
                    out rejectionReasons, out rejected, s_lockRules))
                {
                    lockType = CollectionCardVisual.LockType.NONE;
                    reason = null;
                    return;
                }
            }
            if (rejected == null || rejected.Count == 0) return;
            int index = rejected.FindIndex(rule => rule.Type == DeckRule.RuleType.COUNT_COPIES_OF_EACH_CARD);
            if (index >= 0) lockType = CollectionCardVisual.LockType.MAX_COPIES_IN_DECK;
            else
            {
                index = rejected.FindIndex(rule => rule.Type == DeckRule.RuleType.IS_CARD_PLAYABLE);
                lockType = index >= 0 ? CollectionCardVisual.LockType.NOT_PLAYABLE : CollectionCardVisual.LockType.BANNED;
                if (index < 0) index = 0;
            }
            if (rejectionReasons != null && index < rejectionReasons.Count) reason = rejectionReasons[index]?.DisplayError;
        }

        public static void SettingsChanged()
        {
            CancelChoice();
            s_refreshPending = true;
        }

        public static bool CancelChoice()
        {
            bool queued = s_pending != null;
            s_pending = null;
            return QualityChoiceMenu.Cancel() || queued;
        }

        public static void OnUpdate()
        {
            try
            {
                TryHandleRelease(CollectionInputMgr.Get());
                // Native input temporarily hides the held visual offscreen.
                // Only a completed/canceled drop, another pickup or a changed
                // deck invalidates the captured half, not temporary invisibility.
                if (s_drag != null && !IsValid(s_drag)) s_drag = null;
                QualityChoiceMenu.ValidateContext();
                if (s_pending != null && Time.frameCount >= s_showAfterFrame)
                {
                    QualityAddRequest request = s_pending;
                    s_pending = null;
                    if (IsValid(request))
                    {
                        List<TAG_PREMIUM> choices = Choices(request, request.IgnoreRules);
                        if (choices.Count > 0)
                        {
                            if (request.PromptAfterDrop) QualityChoiceMenu.Open(request);
                            else Confirm(request, choices[0]);
                        }
                    }
                }
                if (s_refreshPending && PreferredQualityMode.IsEnabled && !(CollectionInputMgr.Get()?.HasHeldCard() ?? false)
                    && !CraftingManager.GetIsInCraftingMode())
                {
                    // Refresh from the update loop, after dropdown callbacks
                    // return. Menu visibility and modal input claims are
                    // independent of rebuilding the collection underneath.
                    s_refreshPending = false;
                    MixedPremiumCard.ResetSession();
                    KeywordFilterApplier.RefreshCollectionPages();
                }
            }
            catch (Exception ex) { DebugLog.Exception("Collection quality update failed", ex); s_pending = null; }
        }

        public static void ResetSession()
        {
            s_action = s_drag = s_pending = null;
            s_refreshPending = false;
            QualityChoiceMenu.DestroyMenu();
        }
    }
}
