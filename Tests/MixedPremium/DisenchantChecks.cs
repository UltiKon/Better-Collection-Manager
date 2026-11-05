using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static DisenchantChoices.Request DisenchantCase(int normal = 5, int golden = 1, int signature = 1, int diamond = 0)
    {
        CollectionQualitySettings.DisenchantPopup = true;
        CollectionQualitySettings.NeverDustSignaturesDiamonds = false;
        PreferredQualityMode.IsEnabled = true; Network.LoggedIn = true;
        PreferredPremiumPrefs.Order = new[] { TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.DIAMOND, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.NORMAL };
        CollectionManager.Instance = new CollectionManager();
        foreach (var item in new[] { (TAG_PREMIUM.NORMAL, normal), (TAG_PREMIUM.GOLDEN, golden), (TAG_PREMIUM.SIGNATURE, signature), (TAG_PREMIUM.DIAMOND, diamond) })
            CollectionManager.Instance.Cards[("chosen-card", item.Item1)] = Card("chosen-card", item.Item1, item.Item2);
        CollectionManager.Instance.Cards[("other-card", TAG_PREMIUM.NORMAL)] = Card("other-card", TAG_PREMIUM.NORMAL, 100);
        var actor = new GameObject("Inspected card").AddComponent<Actor>();
        actor.Def = new EntityDef { CardId = "chosen-card", Name = "Chosen card" }; actor.Premium = TAG_PREMIUM.NORMAL;
        CraftingManager.Instance = new CraftingManager { Shown = actor };
        CraftingManager.Instance.Transaction.CardID = "chosen-card";
        Navigation.Clear();
        CraftingManager.Instance.EnterCraftMode(actor);
        var button = new GameObject("Disenchant").AddComponent<DisenchantButton>();
        DialogManager.Get().Popup = null;
        return DisenchantChoices.Request.Capture(CraftingManager.Instance, button);
    }

    private static void CheckDisenchantChoices()
    {
        var harmony = new HarmonyLib.Harmony();
        DisenchantChoices.Install(harmony); DisenchantChoices.Install(harmony);
        Check(harmony.Patched.Count == 2 && harmony.Patched[0].DeclaringType == typeof(DisenchantButton)
            && harmony.Patched[1].DeclaringType == typeof(CraftingManager), "Install only inspected-card release and execution hooks, once");
        var request = DisenchantCase();
        var release = typeof(DisenchantChoices).GetMethod("ReleasePrefix", BindingFlags.NonPublic | BindingFlags.Static);
        Check(!(bool)release.Invoke(null, new object[] { request.Button }) && ButtonListMenu.Visible is DisenchantChoiceMenu
            && request.Transaction.Operations.Count == 0, "The inspected-card click opens the picker without selling a copy");
        DisenchantChoiceMenu.DestroyMenu();
        var single = DisenchantCase(normal: 1, golden: 0, signature: 0);
        Check(single.Choices().SequenceEqual(new[] { DisenchantMode.One }), "One owned copy has one distinct action, using native single-copy behavior");
        release.Invoke(null, new object[] { single.Button });
        Check(single.Button.NativeReleases == 1 && ButtonListMenu.Visible == null && DialogManager.Get().Popup == null
            && single.Transaction.Operations.Count == 0, "One distinct single-copy action skips the picker and keeps native warnings");
        single = DisenchantCase(normal: 3, golden: 0, signature: 0);
        Check(single.Choices().SequenceEqual(new[] { DisenchantMode.All, DisenchantMode.One }),
            "Keep-two is redundant when it removes the same quality and copy count as Only-one");
        single = DisenchantCase(normal: 2, golden: 1, signature: 0);
        single.Manager.Shown.Premium = TAG_PREMIUM.GOLDEN;
        single = DisenchantChoices.Request.Capture(single.Manager, single.Button);
        Check(single.Plan(DisenchantMode.KeepTwo).Count == single.Plan(DisenchantMode.One).Count
            && single.Choices().Count == 3, "Equal amounts that remove different qualities remain distinct choices");
        single = DisenchantCase(normal: 0, golden: 0, signature: 1);
        CollectionQualitySettings.NeverDustSignaturesDiamonds = true;
        single = DisenchantChoices.Request.Capture(single.Manager, single.Button);
        DisenchantChoiceMenu.Open(single);
        Check(single.Choices().Count == 0 && ButtonListMenu.Visible == null && DialogManager.Get().Popup == null,
            "No eligible copies opens no amount picker or sale confirmation");
        request = DisenchantCase();
        request.Transaction.Add(CraftingPendingTransaction.Operation.NormalCreate);
        Check((bool)release.Invoke(null, new object[] { request.Button }), "Crafting Undo bypasses the picker and retains native behavior");
        request = DisenchantCase();
        Check(request.IsCurrent(), "Fresh inspected-card request is valid");
        var all = request.Plan(DisenchantMode.All);
        var keep = request.Plan(DisenchantMode.KeepTwo);
        var one = request.Plan(DisenchantMode.One);
        Check(all.Count == 7 && all.Dust == 175, "All copies counts all owned qualities of the inspected card");
        Check(keep.Count == 5 && keep.Dust == 25 && keep.Counts.Count == 1 && keep.Counts[TAG_PREMIUM.NORMAL] == 5,
            "Keep-two retains one signature and one golden copy, selling only the five normal copies");
        Check(one.Count == 1 && one.Dust == 5, "One copy uses the currently inspected quality");
        request.Choose(DisenchantMode.One);
        Check(request.Button.NativeReleases == 1 && request.Transaction.Operations.Count == 0 && CraftingManager.Get().CloseCalls == 0,
            "One copy delegates to native warnings and animation without staging a batch");

        request.Choose(DisenchantMode.KeepTwo);
        Check(DialogManager.Get().Popup.m_text.Contains("5 × Normal") && DialogManager.Get().Popup.m_text.Contains("25 Arcane Dust"),
            "Batch confirmation includes exact quantities and dust");
        Check(CraftingManager.Get().CloseCalls == 0 && request.Transaction.Operations.Count == 0, "Opening the confirmation does not sell anything");
        DialogManager.Get().Popup.m_responseCallback(AlertPopup.Response.CANCEL, null);
        Check(request.Transaction.Operations.Count == 0 && request.Manager.CloseCalls == 0, "Cancel does not mutate transactions or close inspection");
        request.Choose(DisenchantMode.KeepTwo);
        var confirmation = DialogManager.Get().Popup;
        confirmation.m_responseCallback(AlertPopup.Response.CONFIRM, null);
        Check(request.Manager.CloseCalls == 1 && request.Manager.SubmittedCardId == "chosen-card" && request.Manager.Dust == 25,
            "Confirmed batch submits only the inspected card through native inspect-close");
        Check(Navigation.Count == 0, "Confirmed batch removes the native preview's Back handler");
        Check(request.Manager.Submitted.GetTransactionAmount(TAG_PREMIUM.NORMAL) == -5
            && request.Manager.Submitted.GetTransactionAmount(TAG_PREMIUM.GOLDEN) == 0
            && request.Manager.Submitted.GetTransactionAmount(TAG_PREMIUM.SIGNATURE) == 0
            && CollectionManager.Get().GetCard("other-card", TAG_PREMIUM.NORMAL).OwnedCount == 100,
            "Keep-two excludes retained qualities and unrelated cards");
        confirmation.m_responseCallback(AlertPopup.Response.CONFIRM, null);
        Check(request.Manager.CloseCalls == 1, "Duplicate confirmation cannot submit twice");

        request = DisenchantCase();
        request.Choose(DisenchantMode.All);
        DialogManager.Get().Popup.m_responseCallback(AlertPopup.Response.CONFIRM, null);
        Check(request.Manager.SubmittedCardId == "chosen-card" && request.Manager.Submitted.Operations.Count == 7 && request.Manager.Dust == 175,
            "All means all copies of exactly one card, never all extra cards in the collection");

        request = DisenchantCase(normal: 1, golden: 0, signature: 0);
        var lastRefund = CollectionManager.Get().GetCard(request.CardId, TAG_PREMIUM.NORMAL);
        lastRefund.Refundable = true;
        int leaves = 0, previewClosed = 0;
        Navigation.Clear();
        Navigation.Push(() => { leaves++; return true; });
        request.Manager.EnterCraftMode(request.Manager.Shown, () => previewClosed++);
        request.Choose(DisenchantMode.All);
        DialogManager.Get().Popup.m_responseCallback(AlertPopup.Response.CONFIRM, null);
        lastRefund.OwnedCount = 0;
        request.Manager.CompleteClose();
        Check(previewClosed == 1 && Navigation.Count == 1 && !request.Manager.IsCancelling(),
            "Dusting the final Refund copy runs the native preview callback and releases its navigation entry");
        Check(Navigation.GoBack() && leaves == 1 && request.Manager.CloseCalls == 1 && Navigation.Count == 0,
            "Leaving an empty Refund collection takes one Back without closing a dead preview again");
        request.Manager.Transaction = new CraftingPendingTransaction { CardID = "other-card" };
        var nextActor = new GameObject("Next inspected card").AddComponent<Actor>();
        nextActor.Def = new EntityDef { CardId = "other-card" }; nextActor.Premium = TAG_PREMIUM.NORMAL;
        request.Manager.EnterCraftMode(nextActor);
        Check(request.Manager.IsCardShowing() && !request.Manager.IsCancelling() && Navigation.Count == 1,
            "A later card can open its own native preview after the final Refund was disenchanted");
        Check(Navigation.GoBack() && request.Manager.CloseCalls == 2 && Navigation.Count == 0,
            "The next preview closes exactly once through its own handler");

        request = DisenchantCase(); request.Choose(DisenchantMode.All);
        Navigation.Enabled = false;
        DialogManager.Get().Popup.m_responseCallback(AlertPopup.Response.CONFIRM, null);
        Check(request.Transaction.Operations.Count == 0 && request.Manager.Dust == 0 && request.Manager.CloseCalls == 0,
            "A native navigation lock cannot leave a staged batch without closing the preview");
        Navigation.Clear();

        foreach (Action<DisenchantChoices.Request> invalidate in new Action<DisenchantChoices.Request>[] {
            r => CollectionManager.Get().GetCard(r.CardId, TAG_PREMIUM.NORMAL).OwnedCount--,
            r => r.Manager.SellValues[TAG_PREMIUM.NORMAL]++,
            r => r.Manager.Shown.Premium = TAG_PREMIUM.GOLDEN,
            r => r.Manager.Transaction.CardID = "other-card",
            r => r.Manager.Transaction = new CraftingPendingTransaction { CardID = r.CardId },
            r => r.Manager.Transaction.Add(CraftingPendingTransaction.Operation.NormalDisenchant),
            r => r.Manager.ServerTransaction = new CraftingPendingTransaction(),
            r => CraftingManager.Instance = new CraftingManager(),
            r => CollectionManager.Instance = new CollectionManager(),
            r => r.Manager.Shown = null,
            r => r.Manager.Cancelling = true,
            r => Network.LoggedIn = false,
            r => PreferredPremiumPrefs.Order = new[] { TAG_PREMIUM.NORMAL, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.DIAMOND, TAG_PREMIUM.SIGNATURE },
            r => CollectionManager.Get().GetCard(r.CardId, TAG_PREMIUM.NORMAL).IsCraftable = false })
        {
            request = DisenchantCase(); request.Choose(DisenchantMode.All);
            var popup = DialogManager.Get().Popup;
            invalidate(request);
            Check(!request.IsCurrent(), "Changed context invalidates reviewed amounts");
            popup.m_responseCallback(AlertPopup.Response.CONFIRM, null);
            Check(request.Manager.CloseCalls == 0 && request.Manager.Submitted == null, "Stale confirmation cannot sell cards");
        }

        request = DisenchantCase(normal: 3, golden: 0, signature: 0);
        CollectionManager.Get().GetCard(request.CardId, TAG_PREMIUM.NORMAL).TrialCount = 10;
        request = DisenchantChoices.Request.Capture(request.Manager, request.Button);
        Check(request.Plan(DisenchantMode.All).Count == 3 && request.Plan(DisenchantMode.KeepTwo).Count == 1,
            "Trial copies are neither sold nor counted as retained owned copies");
        request.Manager.Transaction.Add(CraftingPendingTransaction.Operation.NormalDisenchant);
        request = DisenchantChoices.Request.Capture(request.Manager, request.Button);
        Check(request.Plan(DisenchantMode.All).Count == 2 && request.Plan(DisenchantMode.KeepTwo).Count == 0,
            "Previously pending removals cannot be sold again");

        request = DisenchantCase(normal: 5, golden: 0, signature: 2);
        CollectionManager.Get().GetCard(request.CardId, TAG_PREMIUM.SIGNATURE).IsCraftable = false;
        request = DisenchantChoices.Request.Capture(request.Manager, request.Button);
        Check(request.Plan(DisenchantMode.All).Count == 5 && request.Plan(DisenchantMode.KeepTwo).Count == 5,
            "Protected top-quality owned copies count toward retained two but cannot be sold");

        // Cover every mixture of 0..3 copies per quality against independent retention invariants.
        for (int n = 0; n < 4; n++) for (int g = 0; g < 4; g++) for (int d = 0; d < 4; d++) for (int s = 0; s < 4; s++)
        {
            request = DisenchantCase(n, g, s, d);
            keep = request.Plan(DisenchantMode.KeepTwo);
            int[] owned = { s, d, g, n };
            int[] sold = request.Variants.Select(v => keep.Counts.TryGetValue(v.Premium, out int c) ? c : 0).ToArray();
            Check(keep.Count == Math.Max(0, n + g + d + s - 2) && sold.Zip(owned, (a, b) => a >= 0 && a <= b).All(v => v),
                "Keep-two conserves ownership for every quality mixture");
            for (int i = 0; i < owned.Length; i++)
                if (sold[i] > 0) Check(Enumerable.Range(i + 1, owned.Length - i - 1).All(j => owned[j] == sold[j]),
                    "No higher quality is sold while a lower one is retained");
        }

        request = DisenchantCase(normal: 2, golden: 0, signature: 0);
        DisenchantChoiceMenu.Open(request);
        Check(ButtonListMenu.Visible.Buttons.Count == 3 && ButtonListMenu.Visible.Header == "Disenchant this card", "Picker offers only two distinct actions and cancel");
        Check(ButtonListMenu.Visible.Buttons.All(button => button.gameObject.name != "BCM_DisenchantSecond"), "Keep-two is hidden when no extra copies of this card remain");
        ButtonListMenu.Visible.Hide();
        Check(request.Manager.CloseCalls == 0 && request.Transaction.Operations.Count == 0, "Closing the picker does not sell anything");
        DisenchantChoiceMenu.DestroyMenu();

        var nativeExecute = typeof(DisenchantChoices).GetMethod("NativeDisenchantPrefix", BindingFlags.NonPublic | BindingFlags.Static);
        foreach (TAG_PREMIUM quality in new[] { TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.DIAMOND })
        {
            request = DisenchantCase(normal: 5, golden: 1, signature: 3, diamond: 2);
            request.Manager.Shown.Premium = quality;
            CollectionQualitySettings.NeverDustSignaturesDiamonds = true;
            request = DisenchantChoices.Request.Capture(request.Manager, request.Button);
            foreach (DisenchantMode mode in Enum.GetValues(typeof(DisenchantMode)))
            {
                var protectedPlan = request.Plan(mode);
                Check(!protectedPlan.Counts.ContainsKey(TAG_PREMIUM.SIGNATURE) && !protectedPlan.Counts.ContainsKey(TAG_PREMIUM.DIAMOND),
                    "Enabled premium protection excludes Signature and Diamond from every amount option");
            }
            Check(request.Plan(DisenchantMode.All).Count == 6 && request.Plan(DisenchantMode.KeepTwo).Count == 6,
                "Protected premium copies remain owned and count toward the retained two");
            request.Choose(DisenchantMode.One);
            Check(request.Button.NativeReleases == 0 && DialogManager.Get().Popup == null,
                "Only-one cannot sell a protected Signature or Diamond");
            Check(!(bool)nativeExecute.Invoke(null, new object[] { request.Manager }), "Native execution also refuses a protected quality");
            PreferredQualityMode.IsEnabled = false;
            Check(!(bool)nativeExecute.Invoke(null, new object[] { request.Manager }), "Premium protection also applies when Preferred Quality is inactive");
            PreferredQualityMode.IsEnabled = true;
            request.Manager.Transaction.Add(CraftingPendingTransaction.Operation.NormalCreate);
            Check((bool)nativeExecute.Invoke(null, new object[] { request.Manager }), "Premium protection preserves native crafting Undo");
        }
        foreach (TAG_PREMIUM quality in new[] { TAG_PREMIUM.NORMAL, TAG_PREMIUM.GOLDEN })
        {
            request = DisenchantCase(); request.Manager.Shown.Premium = quality;
            CollectionQualitySettings.NeverDustSignaturesDiamonds = true;
            Check((bool)nativeExecute.Invoke(null, new object[] { request.Manager }), "Premium protection allows normal and golden native sales");
        }
        request = DisenchantCase(); request.Choose(DisenchantMode.All);
        var stalePopup = DialogManager.Get().Popup;
        CollectionQualitySettings.NeverDustSignaturesDiamonds = true;
        stalePopup.m_responseCallback(AlertPopup.Response.CONFIRM, null);
        Check(!request.IsCurrent() && request.Manager.CloseCalls == 0 && request.Transaction.Operations.Count == 0,
            "Enabling protection invalidates already-open confirmations without staging sales");
        request = DisenchantCase(normal: 4, golden: 0, signature: 0);
        CollectionQualitySettings.NeverDustSignaturesDiamonds = true;
        Check(!request.IsCurrent(), "Changing protection invalidates even a request with no premium copies");
        request = DisenchantCase(); request.Manager.Shown.Premium = TAG_PREMIUM.SIGNATURE;
        Check((bool)nativeExecute.Invoke(null, new object[] { request.Manager }), "Explicitly disabling protection restores native Signature sales");
        request = DisenchantCase();
        DisenchantChoiceMenu.Open(request);
        Check(ButtonListMenu.Visible != null, "Enabled disenchant popup opens the custom amount picker");
        CollectionQualitySettings.NeverDustSignaturesDiamonds = true;
        CollectionQualitySettings.DisenchantPopup = false;
        Check(ButtonListMenu.Visible == null && !request.IsCurrent(),
            "Disabling the popup immediately closes the custom picker and invalidates its request");
        request.Manager.Shown.Premium = TAG_PREMIUM.SIGNATURE;
        Check((bool)release.Invoke(null, new object[] { request.Button })
            && (bool)nativeExecute.Invoke(null, new object[] { request.Manager }),
            "A disabled popup restores native release and execution even when saved Never dust protection is on");
        Check(DisenchantChoices.Request.Capture(request.Manager, request.Button) == null,
            "A disabled popup cannot capture new custom disenchant requests");
        DisenchantChoiceMenu.Open(request);
        Check(ButtonListMenu.Visible == null && request.Transaction.Operations.Count == 0,
            "A stale request cannot reopen the popup or stage a sale after disabling it");
        request = DisenchantCase();
        request.Choose(DisenchantMode.All);
        var disabledConfirmation = DialogManager.Get().Popup;
        CollectionQualitySettings.DisenchantPopup = false;
        disabledConfirmation.m_responseCallback(AlertPopup.Response.CONFIRM, null);
        Check(request.Transaction.Operations.Count == 0 && request.Manager.CloseCalls == 0,
            "Disabling the popup invalidates an already-open batch confirmation");
        CollectionQualitySettings.DisenchantPopup = true;
        disabledConfirmation.m_responseCallback(AlertPopup.Response.CONFIRM, null);
        Check(!request.IsCurrent() && request.Transaction.Operations.Count == 0,
            "Re-enabling the popup cannot revive a confirmation from before it was disabled");
        CollectionQualitySettings.NeverDustSignaturesDiamonds = true;
    }
}
