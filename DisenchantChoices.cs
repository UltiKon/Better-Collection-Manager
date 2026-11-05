using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace BetterCollectionManager
{
    /// <summary>Choose amounts before entering native single-card warnings or staging a native batch.</summary>
    internal static class DisenchantChoices
    {
        private static bool s_installed, s_nativeRelease;
        private static MethodInfo s_release;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (s_installed) return;
            try
            {
                s_release = AccessTools.Method(typeof(DisenchantButton), "OnRelease");
                if (s_release == null) throw new MissingMethodException("DisenchantButton.OnRelease");
                harmony.Patch(s_release, prefix: new HarmonyMethod(typeof(DisenchantChoices).GetMethod(nameof(ReleasePrefix), BindingFlags.NonPublic | BindingFlags.Static)));
                MethodInfo execute = AccessTools.Method(typeof(CraftingManager), "DisenchantButtonPressed");
                if (execute == null) throw new MissingMethodException("CraftingManager.DisenchantButtonPressed");
                harmony.Patch(execute, prefix: new HarmonyMethod(typeof(DisenchantChoices).GetMethod(nameof(NativeDisenchantPrefix), BindingFlags.NonPublic | BindingFlags.Static)));
                s_installed = true;
            }
            catch (Exception ex) { DebugLog.Exception("Disenchant choices unavailable", ex); }
        }

        private static bool IsProtected(TAG_PREMIUM premium)
            => CollectionQualitySettings.NeverDustSignaturesDiamonds && (premium == TAG_PREMIUM.SIGNATURE || premium == TAG_PREMIUM.DIAMOND);

        private static bool NativeDisenchantPrefix(CraftingManager __instance)
        {
            if (!CollectionQualitySettings.DisenchantPopup || !CollectionQualitySettings.NeverDustSignaturesDiamonds) return true;
            // Undoing a just-crafted/upgraded copy is a reversal, not a sale.
            if (__instance.GetPendingClientTransaction()?.GetLastTransactionWasCrafting() == true) return true;
            return __instance.GetShownCardInfo(out var _, out var premium) && !IsProtected(premium);
        }

        private static bool ReleasePrefix(DisenchantButton __instance)
        {
            if (!CollectionQualitySettings.DisenchantPopup || s_nativeRelease || !PreferredQualityMode.IsEnabled || !Network.IsLoggedIn()) return true;
            try
            {
                CraftingManager manager = CraftingManager.Get();
                CraftingPendingTransaction transaction = manager.GetPendingClientTransaction();
                // Native Undo must keep its meaning after creating or upgrading a card.
                if (transaction == null || transaction.GetLastTransactionWasCrafting()) return true;
                if (!__instance.IsButtonEnabled() || manager.GetPendingServerTransaction() != null || manager.IsCancelling()) return false;
                Request request = Request.Capture(manager, __instance);
                if (request == null) return false;
                DisenchantChoiceMenu.Open(request);
            }
            catch (Exception ex)
            {
                // A failed picker must never turn a click into an unintended sale.
                DebugLog.Exception("Could not open disenchant choices", ex);
            }
            return false;
        }

        internal sealed class Request
        {
            internal CraftingManager Manager;
            internal CollectionManager Collection;
            internal DisenchantButton Button;
            internal CraftingPendingTransaction Transaction;
            internal string CardId, TransactionCardId, CardName;
            internal TAG_PREMIUM Selected;
            internal readonly List<DisenchantVariant> Variants = new List<DisenchantVariant>();
            private bool m_protectPremiums;
            private int m_popupRevision;
            private readonly List<int> m_pending = new List<int>();

            internal static Request Capture(CraftingManager manager, DisenchantButton button)
            {
                if (!CollectionQualitySettings.DisenchantPopup || !manager.IsCardShowing() || !manager.GetShownCardInfo(out var def, out var premium)) return null;
                var request = new Request { Manager = manager, Collection = CollectionManager.Get(), Button = button,
                    Transaction = manager.GetPendingClientTransaction(), CardId = def.GetCardId(), CardName = def.GetName(), Selected = premium };
                if (!manager.IsPendingTransactionForCard(request.CardId)) return null;
                request.TransactionCardId = request.Transaction.CardID;
                request.m_popupRevision = CollectionQualitySettings.DisenchantPopupRevision;
                request.m_protectPremiums = CollectionQualitySettings.NeverDustSignaturesDiamonds;
                foreach (TAG_PREMIUM quality in PreferredPremiumPrefs.Order)
                {
                    CollectibleCard card = CollectionManager.Get().GetCard(request.CardId, quality);
                    int owned = manager.GetNumOwnedIncludePending(request.CardId, quality);
                    bool hasValue = manager.TryGetCardSellValue(request.CardId, quality, out int sell);
                    bool sellable = card != null && card.IsCraftable && hasValue && !IsProtected(quality);
                    request.Variants.Add(new DisenchantVariant { Premium = quality, Owned = owned, SellValue = sell, CanDisenchant = sellable });
                    request.m_pending.Add(request.Transaction.GetTransactionAmount(quality));
                }
                return request;
            }

            internal bool IsCurrent()
            {
                if (!CollectionQualitySettings.DisenchantPopup || m_popupRevision != CollectionQualitySettings.DisenchantPopupRevision
                    || !PreferredQualityMode.IsEnabled || !Network.IsLoggedIn() || Manager == null || Button == null
                    || m_protectPremiums != CollectionQualitySettings.NeverDustSignaturesDiamonds
                    || !ReferenceEquals(Manager, CraftingManager.Get()) || !ReferenceEquals(Collection, CollectionManager.Get())
                    || Manager.IsCancelling() || !Manager.IsCardShowing() || Manager.GetPendingServerTransaction() != null
                    || !ReferenceEquals(Transaction, Manager.GetPendingClientTransaction()) || Transaction.CardID != TransactionCardId
                    || !Manager.GetShownCardInfo(out var def, out var premium) || def.GetCardId() != CardId || premium != Selected
                    || Transaction.GetLastTransactionWasCrafting()) return false;
                Request current = Capture(Manager, Button);
                if (current == null || current.Variants.Count != Variants.Count) return false;
                for (int i = 0; i < Variants.Count; i++)
                {
                    var before = Variants[i]; var after = current.Variants[i];
                    if (before.Premium != after.Premium || before.Owned != after.Owned || before.SellValue != after.SellValue
                        || before.CanDisenchant != after.CanDisenchant || m_pending[i] != current.m_pending[i]) return false;
                }
                return true;
            }

            internal DisenchantPlan Plan(DisenchantMode mode) => DisenchantPlan.Create(Variants, mode, Selected);

            internal List<DisenchantMode> Choices()
            {
                var modes = new List<DisenchantMode>();
                var plans = new List<DisenchantPlan>();
                // Prefer the native single-copy flow whenever another option
                // removes that exact same copy. Amount alone is not enough:
                // equal counts can still remove different qualities.
                foreach (var mode in new[] { DisenchantMode.One, DisenchantMode.All, DisenchantMode.KeepTwo })
                {
                    DisenchantPlan plan = Plan(mode);
                    if (plan.Count == 0 || plans.Exists(plan.SameRemovals)) continue;
                    modes.Add(mode); plans.Add(plan);
                }
                modes.Sort();
                return modes;
            }

            internal void Choose(DisenchantMode mode)
            {
                if (!IsCurrent()) return;
                DisenchantPlan plan = Plan(mode);
                if (plan.Count == 0) return;
                if (mode == DisenchantMode.One)
                {
                    try { s_nativeRelease = true; s_release.Invoke(Button, null); }
                    finally { s_nativeRelease = false; }
                    return;
                }
                // Confirm the complete cross-quality batch, including its effect on decks.
                var lines = new List<string>();
                foreach (var variant in Variants)
                    if (plan.Counts.TryGetValue(variant.Premium, out int count))
                        lines.Add(count + " × " + PreferredPremiumHelper.PremiumName(variant.Premium));
                DialogManager.Get().ShowPopup(new AlertPopup.PopupInfo
                {
                    m_headerText = "Disenchant " + plan.Count + " copies of this card?",
                    m_text = CardName + "\n\n" + string.Join("\n", lines) + "\n\n+" + plan.Dust + " Arcane Dust\n\nThis may make decks incomplete. The card preview will close.",
                    m_showAlertIcon = true,
                    m_responseDisplay = AlertPopup.ResponseDisplay.CONFIRM_CANCEL,
                    m_responseCallback = (response, data) =>
                    {
                        if (response != AlertPopup.Response.CONFIRM || !IsCurrent()) return;
                        Apply(plan);
                    }
                });
            }

            private void Apply(DisenchantPlan plan)
            {
                if (!Navigation.CanGoBack) return;
                // Native pending transactions preserve collection counts, server validation,
                // pricing, dust and the normal inspect-close refresh path.
                foreach (var entry in plan.Counts)
                {
                    CraftingPendingTransaction.Operation operation;
                    switch (entry.Key)
                    {
                        case TAG_PREMIUM.NORMAL: operation = CraftingPendingTransaction.Operation.NormalDisenchant; break;
                        case TAG_PREMIUM.GOLDEN: operation = CraftingPendingTransaction.Operation.GoldenDisenchant; break;
                        case TAG_PREMIUM.DIAMOND: operation = CraftingPendingTransaction.Operation.DiamondDisenchant; break;
                        case TAG_PREMIUM.SIGNATURE: operation = CraftingPendingTransaction.Operation.SignatureDisenchant; break;
                        default: throw new InvalidOperationException("Unsupported disenchant quality");
                    }
                    for (int i = 0; i < entry.Value; i++) Transaction.Add(operation);
                }
                Manager.AdjustUnCommitedArcaneDustChanges(plan.Dust);
                Options.Get().SetBool(Option.HAS_DISENCHANTED, true);
                // EnterCraftMode pushes a close handler, including the caller's
                // preview callback. Go through it so a later Back cannot close
                // this already-closed preview again and strand native input.
                Navigation.GoBack();
            }
        }
    }
}
