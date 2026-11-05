using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BetterCollectionManager
{
    internal static partial class ComboCollectionCards
    {
        private static int s_lockRevision;
        private static readonly MethodInfo s_showLock = typeof(CollectionCardVisual).GetMethod("ShowLock", NativeMembers, null,
            new[] { typeof(CollectionCardVisual.LockType), typeof(string), typeof(bool), typeof(CollectionDeck) }, null);
        private static readonly MethodInfo s_cancelComboDrop = typeof(CollectionInputMgr).GetMethod("DropCard", NativeMembers, null, new[] { typeof(bool) }, null);
        private sealed class ComboDrag
        {
            internal CategoryCatalog.Combo Combo;
            internal CollectionCardVisual Source;
            internal CollectionDeck Root, Context;
            internal CollectionDraggableCardVisual Held;
            internal string CardId;
            internal TAG_PREMIUM Premium;
        }
        private static CollectionCardVisual s_grabSource, s_releasedSource;
        private static ComboDrag s_comboDrag, s_pendingComboDrop;
        private static int s_releaseFrame = -1, s_dropAfterFrame;

        private static List<List<CollectibleCard>> LockVariants(CategoryCatalog.Combo combo)
        {
            var result = new List<List<CollectibleCard>>();
            foreach (var card in combo.Cards) result.Add(ComponentCards(combo, card.Id));
            return result;
        }
        internal static long CountOwnedCombos(CategoryCatalog.Combo combo) => CountOwnedCombos(combo, LockVariants(combo));
        private static long CountOwnedCombos(CategoryCatalog.Combo combo, List<List<CollectibleCard>> components)
        {
            if (combo.Cards.Count == 0) return 0;
            long count = long.MaxValue;
            CollectionManager manager = CollectionManager.Get();
            for (int i = 0; i < combo.Cards.Count; i++)
            {
                long owned = 0;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (CollectibleCard variant in components[i])
                    if (seen.Add(variant.CardId + ":" + (int)variant.PremiumType))
                        owned += Math.Max(0, manager != null ? manager.GetOwnedCount(variant.CardId, variant.PremiumType) : variant.OwnedCount);
                if (combo.Cards[i].Count <= 0) return 0;
                count = Math.Min(count, owned / combo.Cards[i].Count);
            }
            return count;
        }
        internal static CollectionCardVisual.LockType EvaluateLock(CategoryCatalog.Combo combo, out string reason)
            => EvaluateLock(LockVariants(combo), out reason, combo.IsPackage);

        private static CollectionCardVisual.LockType EvaluateLock(List<List<CollectibleCard>> components, out string reason, bool package = false)
        {
            reason = null;
            if (!CanAddCombo()) return CollectionCardVisual.LockType.NONE;
            CollectionManager manager = CollectionManager.Get();
            var result = CollectionCardVisual.LockType.NO_MORE_INSTANCES;
            reason = "Not enough copies.";
            int priority = 0;
            foreach (var variants in components)
            {
                if (variants.Count == 0) continue;
                var request = new QualityAddRequest { RootDeck = manager.GetEditedDeck(), Context = CollectionDeckTray.Get().GetCurrentDeckContext(),
                    Definition = variants[0].GetEntityDef(), Variants = variants, Requested = PreferredPremiumPrefs.Order[0] };
                if (QualityDeckInput.Choices(request, Array.Empty<DeckRule.RuleType>()).Count > 0)
                { reason = null; return CollectionCardVisual.LockType.NONE; }
                DeckRuleset rules = manager.GetDeckRuleset();
                if (rules == null) continue;
                foreach (CollectibleCard variant in variants)
                {
                    rules.CanAddToDeck(request.Definition, variant.PremiumType, request.Context,
                        out List<RuleInvalidReason> reasons, out List<DeckRule> broken, Array.Empty<DeckRule.RuleType>());
                    if (broken == null) continue;
                    for (int i = 0; i < broken.Count; i++)
                    {
                        int rank; CollectionCardVisual.LockType type; string fallback;
                        switch (broken[i].Type)
                        {
                            case DeckRule.RuleType.DECK_SIZE: rank = 5; type = CollectionCardVisual.LockType.MAX_COPIES_IN_DECK; fallback = "Deck is full."; break;
                            case DeckRule.RuleType.COUNT_COPIES_OF_EACH_CARD: rank = 4; type = CollectionCardVisual.LockType.MAX_COPIES_IN_DECK; fallback = "Maximum copies in deck."; break;
                            case DeckRule.RuleType.IS_CARD_PLAYABLE: rank = 3; type = CollectionCardVisual.LockType.NOT_PLAYABLE; fallback = "Not playable in this deck."; break;
                            case DeckRule.RuleType.PLAYER_OWNS_EACH_COPY: rank = 1; type = CollectionCardVisual.LockType.NO_MORE_INSTANCES; fallback = "Not enough copies."; break;
                            default: rank = 2; type = CollectionCardVisual.LockType.BANNED; fallback = "Cannot add to this deck."; break;
                        }
                        if (rank <= priority) continue;
                        priority = rank; result = type;
                        reason = package && type == CollectionCardVisual.LockType.MAX_COPIES_IN_DECK ? fallback
                            : reasons != null && i < reasons.Count && !string.IsNullOrEmpty(reasons[i]?.DisplayError) ? reasons[i].DisplayError : fallback;
                    }
                }
            }
            return result;
        }
        private static CollectionCardVisual.LockType GetLock(Binding binding, out string reason)
        {
            CollectionDeck root = CollectionManager.Get()?.GetEditedDeck(), context = CollectionDeckTray.Get()?.GetCurrentDeckContext();
            if (!binding.Lock.HasValue || Time.unscaledTime >= binding.NextLockAt || binding.LockRevision != s_lockRevision
                || !ReferenceEquals(root, binding.LockRoot) || !ReferenceEquals(context, binding.LockContext))
            {
                if (binding.LockVariants == null) binding.LockVariants = LockVariants(binding.Combo);
                binding.OwnedCombos = binding.Combo.IsPackage ? 0 : CountOwnedCombos(binding.Combo, binding.LockVariants);
                if (!CanAddCombo())
                { binding.Lock = CollectionCardVisual.LockType.NONE; binding.LockReason = null; }
                else
                {
                    binding.Lock = EvaluateLock(binding.LockVariants, out binding.LockReason, binding.Combo.IsPackage);
                }
                binding.LockRoot = root; binding.LockContext = context;
                binding.LockRevision = s_lockRevision; binding.NextLockAt = Time.unscaledTime + .15f;
            }
            reason = binding.LockReason; return binding.Lock.Value;
        }
        public static bool TryAdjustComboLock(CollectionCardVisual visual, ref CollectionCardVisual.LockType type, ref string reason)
        {
            Binding binding = Find(visual);
            if (binding == null) return false;
            type = GetLock(binding, out reason);
            HidePackageBan(binding, ref type, ref reason);
            // Remember native refreshes too, so a later custom refresh does not replay their sound.
            binding.AppliedLock = type; binding.AppliedLockReason = reason;
            return true;
        }
        private static void HidePackageBan(Binding binding, ref CollectionCardVisual.LockType type, ref string reason)
        {
            if (binding.Combo.IsPackage && (type == CollectionCardVisual.LockType.BANNED || type == CollectionCardVisual.LockType.NOT_PLAYABLE))
            { type = CollectionCardVisual.LockType.NONE; reason = null; }
        }
        private static bool ApplyLock(CollectionCardVisual visual, Binding binding, bool playSound = false)
        {
            if (s_showLock == null) return false;
            CollectionCardVisual.LockType type = GetLock(binding, out string reason);
            HidePackageBan(binding, ref type, ref reason);
            GameObject go = visual.m_cardLock?.gameObject;
            if (go != null && !binding.Hidden.ContainsKey(go)) binding.Hidden[go] = go.activeSelf;
            if (binding.AppliedLock == type && binding.AppliedLockReason == reason
                && (go == null || go.activeSelf == (type != CollectionCardVisual.LockType.NONE))) return false;
            bool sound = playSound && binding.AppliedLock == CollectionCardVisual.LockType.NONE && type != CollectionCardVisual.LockType.NONE;
            s_showLock.Invoke(visual, new object[] { type, reason, sound, CollectionDeckTray.Get()?.GetCurrentDeckContext() });
            binding.AppliedLock = type; binding.AppliedLockReason = reason;
            return sound;
        }
        private static void RefreshAddedCombo(CategoryCatalog.Combo combo)
        {
            s_lockRevision++;
            bool sound = true;
            foreach (var pair in new List<KeyValuePair<CollectionCardVisual, Binding>>(s_bindings))
                if (pair.Key != null && pair.Key.IsShown() && pair.Value.Combo == combo)
                {
                    if (ApplyLock(pair.Key, pair.Value, sound)) sound = false;
                    Apply(pair.Key);
                }
        }

        private static void InitializeDrag()
        {
            CollectionInputMgr.CollectionDraggableCardGrabbed -= OnComboGrabbed;
            CollectionInputMgr.CollectionDraggableCardGrabbed += OnComboGrabbed;
            CollectionInputMgr.CollectionDraggableCardDropped -= OnComboDropped;
            CollectionInputMgr.CollectionDraggableCardDropped += OnComboDropped;
        }
        private static bool BeginDragInput(CollectionCardVisual visual, CategoryCatalog.Combo combo)
        {
            if (!CanAddCombo() || s_cancelComboDrop == null || EvaluateLock(combo, out _) != CollectionCardVisual.LockType.NONE) return false;
            // Capture before native pickup hides trays or dispatches callbacks.
            // The held visual can be published after the grabbed event.
            s_comboDrag = new ComboDrag { Combo = combo, Source = visual, Root = CollectionManager.Get().GetEditedDeck(),
                Context = CollectionDeckTray.Get().GetCurrentDeckContext(),
                CardId = visual.GetActor()?.GetEntityDef()?.GetCardId(), Premium = visual.GetActor().GetPremium() };
            s_grabSource = visual; return true;
        }
        public static void EndDragInput() => s_grabSource = null;
        private static bool IsDragPickup(CollectionCardVisual visual) => s_grabSource == visual && CanAddCombo();
        private static void OnComboGrabbed(CollectionCardVisual visual)
        {
            if (s_comboDrag != null && s_comboDrag.Source == visual)
            {
                CollectionInputMgr active = CollectionInputMgr.Get();
                s_comboDrag.Held = active != null && active.HasHeldCard() ? active.GetHeldCardVisual() : null;
                DebugLog.Msg("Combo pickup: " + s_comboDrag.Combo.Name + "; held=" + (s_comboDrag.Held != null));
                return;
            }
            Binding binding = Find(visual);
            if (binding == null || !CanAddCombo()) return;
            CollectionInputMgr input = CollectionInputMgr.Get();
            s_comboDrag = new ComboDrag { Combo = binding.Combo, Source = visual, Root = CollectionManager.Get().GetEditedDeck(),
                Context = CollectionDeckTray.Get().GetCurrentDeckContext(), Held = input != null && input.HasHeldCard() ? input.GetHeldCardVisual() : null,
                CardId = visual.GetActor()?.GetEntityDef()?.GetCardId(), Premium = visual.GetActor().GetPremium() };
        }
        private static void OnComboDropped() { s_comboDrag = null; s_grabSource = null; }
        private static bool MatchesHeld(ComboDrag drag, CollectionInputMgr input)
        {
            CollectionDraggableCardVisual held = input?.GetHeldCardVisual();
            if (held == null || !input.HasHeldCard()) return false;
            // Some native pickup paths announce the grab before publishing the
            // held visual. Adopt it once its identity is available.
            if (drag.Held == null && held.GetSlot() == null && held.GetCurrentlyShowingEntityDef()?.GetCardId() == drag.CardId)
                drag.Held = held;
            return ReferenceEquals(held, drag.Held);
        }
        private static bool ValidDrag(ComboDrag drag, bool allowDialog = false) => drag != null && CanAddCombo()
            && (allowDialog || !(UniversalInputManager.Get()?.IsDialogActive() ?? false))
            && ReferenceEquals(drag.Root, CollectionManager.Get().GetEditedDeck())
            && ReferenceEquals(drag.Context, CollectionDeckTray.Get().GetCurrentDeckContext());
        private static void ConsumeRelease(ComboDrag drag)
        { s_releasedSource = drag.Source; s_releaseFrame = Time.frameCount + 1; }
        private static bool SuppressDragRelease(CollectionCardVisual visual)
            => (s_comboDrag != null && s_comboDrag.Source == visual)
                || (s_releasedSource == visual && Time.frameCount <= s_releaseFrame);
        private static void QueueComboDrop(ComboDrag drag)
        {
            if (!ValidDrag(drag) || s_pendingComboDrop != null) return;
            s_pendingComboDrop = drag; s_dropAfterFrame = Time.frameCount + 1;
            DebugLog.Msg("Combo drop queued: " + drag.Combo.Name + "; quality=" + QualityOptions.EffectiveDrag);
        }
        public static bool PrepareComboDrop(bool canceled, bool overDeck)
        {
            if (s_comboDrag == null) return false;
            ComboDrag drag = s_comboDrag; s_comboDrag = null; ConsumeRelease(drag);
            if (!canceled && overDeck) QueueComboDrop(drag);
            return true; // Native cleanup runs with cancellation: never insert the carrier.
        }
        public static bool TryHandleDragRelease(CollectionInputMgr input)
        {
            if (!Input.GetMouseButtonUp(0) || s_comboDrag == null || input == null || !input.HasHeldCard()) return false;
            ComboDrag drag = s_comboDrag;
            if (!MatchesHeld(drag, input)) { s_comboDrag = null; return false; }
            bool overDeck = ValidDrag(drag) && Box.Get() != null && CollectionDeckTray.Get().MouseIsOver(Box.Get().GetCamera());
            s_comboDrag = null; ConsumeRelease(drag);
            try { s_cancelComboDrop.Invoke(input, new object[] { true }); }
            catch { if (MatchesHeld(drag, input)) s_comboDrag = drag; throw; }
            if (overDeck) QueueComboDrop(drag);
            return true;
        }
        private static void UpdateDrag()
        {
            try
            {
                CollectionInputMgr input = CollectionInputMgr.Get();
                if (s_comboDrag != null && !MatchesHeld(s_comboDrag, input)
                    && ((input?.HasHeldCard() ?? false) || Input.GetMouseButtonUp(0)))
                { ConsumeRelease(s_comboDrag); s_comboDrag = null; }
                if (s_comboDrag != null && !ValidDrag(s_comboDrag))
                {
                    ComboDrag drag = s_comboDrag; s_comboDrag = null; ConsumeRelease(drag);
                    if (MatchesHeld(drag, input))
                        s_cancelComboDrop.Invoke(input, new object[] { true });
                }
                TryHandleDragRelease(CollectionInputMgr.Get());
                if (s_pendingComboDrop == null || Time.frameCount < s_dropAfterFrame) return;
                ComboDrag pending = s_pendingComboDrop; s_pendingComboDrop = null;
                if (ValidDrag(pending))
                {
                    if (QualityOptions.EffectiveDrag == DragQualityMode.ChooseOnDrop) BeginComboQualityChoice(pending);
                    else AddToDeck(pending.Combo, true, pending.Premium, pending.Source?.GetActor());
                }
            }
            catch (Exception ex) { s_pendingComboDrop = null; DebugLog.Exception("Combo drag failed", ex); }
        }
        private static void ResetDrag()
        {
            CancelComboQualityChoice();
            ComboDrag drag = s_comboDrag; s_comboDrag = s_pendingComboDrop = null;
            s_grabSource = s_releasedSource = null; s_releaseFrame = -1;
            CollectionInputMgr input = CollectionInputMgr.Get();
            if (drag != null && MatchesHeld(drag, input))
                s_cancelComboDrop?.Invoke(input, new object[] { true });
        }
    }
}
