using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BetterCollectionManager
{
    internal static partial class ComboCollectionCards
    {
        internal const string PackageClickSound = "Small_Click.prefab:2a1c5335bf08dc84eb6e04fc58160681";
        private static CategoryCatalog.Combo s_openPackage;
        private static CollectionDeck s_packageDeck;
        private static bool s_packageCanAdd;
        private static CollectionDeck s_packagePreparedDeck;
        private static CategoryCatalog.Combo s_packagePreparedDefinition;
        private static bool s_packagePreparedCanAdd;
        private static CollectionPageAnchor s_packageAnchor;
        private static PackageNavigation s_packageNavigation;
        private static bool s_packageBackPending;
        private static Rect s_packageBackArea;
        private static bool s_packageDeckChange;
        private static CollectionPageAnchor s_packageDeckAnchor;

        private static void InstallPackageNavigation(HarmonyLib.Harmony harmony)
        {
            // The native display changes filters and explicitly flips to a
            // class page on both deck entry and exit. Keep that rebuild and its
            // callbacks, but assemble the current folder page without flipping.
            Type display = typeof(CollectionManager).Assembly.GetType("CollectionManagerDisplay");
            foreach (string name in new[] { "ShowDeckHelper", "OnDoneEditingDeck" })
            {
                MethodInfo method = display == null ? null : AccessTools.Method(display, name);
                if (method != null) harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(ComboCollectionCards).GetMethod(nameof(BeginPackageDeckChange))),
                    finalizer: new HarmonyMethod(typeof(ComboCollectionCards).GetMethod(nameof(EndPackageDeckChange))));
            }
            MethodInfo transition = AccessTools.Method(typeof(BookPageManager), "TransitionPageWhenReady");
            if (transition != null) harmony.Patch(transition,
                prefix: new HarmonyMethod(typeof(ComboCollectionCards).GetMethod(nameof(KeepPackagePage))));
        }
        public sealed class PackageDeckChange
        {
            internal bool Active;
            internal CollectionPageAnchor Anchor;
        }
        public static void BeginPackageDeckChange(out PackageDeckChange __state)
        {
            __state = new PackageDeckChange { Active = s_packageDeckChange, Anchor = s_packageDeckAnchor };
            if (s_openPackage == null || CollectionManager.Get()?.GetCollectibleDisplay()?.GetViewMode() != CollectionUtils.ViewMode.CARDS) return;
            s_packageDeckChange = true;
            s_packageDeckAnchor = KeywordFilterApplier.CapturePageAnchor();
        }
        public static Exception EndPackageDeckChange(Exception __exception, PackageDeckChange __state)
        {
            s_packageDeckChange = __state.Active; s_packageDeckAnchor = __state.Anchor;
            return __exception;
        }
        public static void KeepPackagePage(object __instance, object[] __args)
        {
            if (!s_packageDeckChange || s_openPackage == null || __args == null || __args.Length < 2
                || !ReferenceEquals(__instance, CollectionManager.Get()?.GetCollectibleDisplay()?.GetPageManager())
                || CollectionManager.Get()?.GetCollectibleDisplay()?.GetViewMode() != CollectionUtils.ViewMode.CARDS) return;
            if (s_packageDeckAnchor != null)
            {
                object collection = AccessTools.Field(__instance.GetType(), "m_cardsCollection")?.GetValue(__instance);
                AccessTools.Field(__instance.GetType(), "m_currentPageNum")?.SetValue(__instance, s_packageDeckAnchor.Resolve(collection));
            }
            __args[0] = Enum.Parse(__args[0].GetType(), "NONE");
            __args[1] = true;
        }
        private static readonly DeckRule.RuleType[] s_ignorePackageCounts = PackageCountRules();
        private static DeckRule.RuleType[] PackageCountRules()
        {
            var ignored = new List<DeckRule.RuleType>();
            foreach (DeckRule.RuleType rule in Enum.GetValues(typeof(DeckRule.RuleType)))
            {
                string name = rule.ToString();
                if (name.StartsWith("COUNT_", StringComparison.Ordinal) || name.StartsWith("PLAYER_OWNS_", StringComparison.Ordinal)
                    || name == "DECK_SIZE" || name == "EDITING_DECK_EXTRA_CARD_COUNT"
                    || name == "SIDEBOARD_CARD_COUNT_LIMIT" || name == "TOURIST_LIMIT") ignored.Add(rule);
            }
            return ignored.ToArray();
        }
        private static bool PackageAllowedInDeck(CategoryCatalog.Combo package)
        {
            CollectionManager manager = CollectionManager.Get();
            CollectionDeck deck = manager != null && manager.IsInEditMode() ? manager.GetEditedDeck() : null;
            if (deck == null) return true;
            if (!string.IsNullOrEmpty(package.PackageClass) && package.PackageClass != "NEUTRAL")
            {
                bool allowed = package.PackageClass == deck.GetClass().ToString();
                var classes = deck.GetClasses(); var tourists = deck.GetTouristClasses();
                if (classes != null) foreach (TAG_CLASS value in classes) allowed |= value.ToString() == package.PackageClass;
                if (tourists != null) foreach (TAG_CLASS value in tourists) allowed |= value.ToString() == package.PackageClass;
                if (!allowed) return false;
            }
            DeckRuleset rules = manager.GetDeckRuleset();
            foreach (var member in package.Cards)
            {
                var variants = ComponentCards(package, member.Id);
                if (variants.Count == 0) return false;
                if (rules != null && !rules.CanAddToDeck(variants[0].GetEntityDef(), variants[0].PremiumType, deck,
                    out List<RuleInvalidReason> reasons, out List<DeckRule> broken, s_ignorePackageCounts)) return false;
            }
            return true;
        }
        internal static CategoryCatalog.Combo OpenedPackage => s_openPackage;
        internal static CollectionPageAnchor PackageOverviewAnchor => s_packageAnchor;
        private static CategoryCatalog.Combo CurrentPackageDefinition()
        {
            if (s_openPackage != null)
                foreach (var saved in PackageCatalog.Packages)
                    if (saved.Id == s_openPackage.Id && saved.IsPackage) return saved;
            return null;
        }
        private static int s_packagePressFrame = -1;
        internal static void PollPackageClickSounds()
        {
            if ((!Input.GetMouseButtonDown(0) && !Input.GetMouseButtonDown(1)) || s_packagePressFrame == Time.frameCount
                || (UniversalInputManager.Get()?.IsDialogActive() ?? false) || (CollectionInputMgr.Get()?.HasHeldCard() ?? false)) return;
            foreach (var pair in s_bindings)
            {
                var visual = pair.Key;
                if (visual == null || !visual.IsShown() || !pair.Value.Combo.IsPackage
                    || pair.Value.Combo.AddAll && CanAddCombo() && !Input.GetMouseButtonDown(1)) continue;
                Collider hit = visual.GetComponent<Collider>();
                if (hit != null && !hit.enabled) continue;
                Camera camera = CameraUtils.FindProjectionCameraForObject(visual.gameObject);
                if (camera == null || !ComboSceneGeometry.CardSlot(visual, camera).Contains(Input.mousePosition)) continue;
                s_packagePressFrame = Time.frameCount;
                FilterButtonFeedback.PlayClickSound(PackageClickSound); return;
            }
        }

        internal static void OpenPackage(CategoryCatalog.Combo package)
        {
            if (package == null || !package.IsPackage || package.AddAll) return;
            // Build navigation before committing the folder state. A failed
            // native arrow must leave the collection and its filters intact.
            EnsurePackageNavigation();
            s_packageAnchor = KeywordFilterApplier.CapturePageAnchor();
            s_packageDeck = CollectionManager.Get()?.GetEditedDeck();
            s_packageCanAdd = CanAddCombo();
            s_openPackage = package;
            s_packageBackArea = new Rect(Screen.width * .12f, Screen.height * .75f, Screen.width * .56f, Screen.height * .10f);
            KeywordSetTray.HideImmediately();
            KeywordFilterApplier.RefreshCollectionPages(page: 1);
        }
        internal static void BackFromPackage()
        {
            if (s_openPackage == null) return;
            var anchor = s_packageAnchor;
            ClearPackageNavigation();
            KeywordFilterApplier.RefreshCollectionPages(anchor);
        }
        private static void ClearPackageNavigation()
        {
            s_openPackage = null; s_packageDeck = null; s_packageAnchor = null;
            s_packagePreparedDeck = null; s_packagePreparedDefinition = null;
            var navigation = s_packageNavigation; s_packageNavigation = null;
            navigation?.Dispose();
            s_packageBackPending = false;
        }
        private static void UpdatePackageNavigation()
        {
            if (s_packageBackPending) { s_packageBackPending = false; BackFromPackage(); return; }
            if (s_openPackage == null)
            {
                if (s_packageNavigation != null) ClearPackageNavigation();
                return;
            }
            var saved = CurrentPackageDefinition();
            if (!PreferredQualityMode.IsEnabled || saved == null || !PackageSelected(saved))
            { BackFromPackage(); return; }
            CollectionDeck deck = CollectionManager.Get()?.GetEditedDeck();
            bool canAdd = CanAddCombo();
            bool changed = !ReferenceEquals(s_packageDeck, deck) || s_packageCanAdd != canAdd
                || !ReferenceEquals(saved, s_openPackage);
            s_packageDeck = deck; s_packageCanAdd = canAdd;
            s_openPackage = saved;
            if (changed && !PackageAllowedInDeck(s_openPackage)) { BackFromPackage(); return; }
            if (changed && (!ReferenceEquals(s_packagePreparedDeck, deck) || s_packagePreparedCanAdd != canAdd
                || !ReferenceEquals(s_packagePreparedDefinition, s_openPackage)))
            { s_packagePreparedDefinition = null; KeywordFilterApplier.RefreshCollectionPages(); }
            // The synchronous result rebuild can close the folder (for example
            // after moving its last entry). Never recreate navigation afterwards.
            if (s_openPackage == null) return;
            EnsurePackageNavigation();
            s_packageNavigation.Layout(s_packageBackArea, !(UniversalInputManager.Get()?.IsDialogActive() ?? false));
        }
        // Replace all class buckets only after custom filtering and premium
        // collapse. A package is a dedicated folder; its members are not hidden
        // by a category/search selection that selected the folder itself.
        private static Action PreparePackage(List<List<CollectibleCard>> lists)
        {
            CategoryCatalog.Combo package = s_openPackage;
            s_packagePreparedDeck = CollectionManager.Get()?.GetEditedDeck();
            s_packagePreparedCanAdd = CanAddCombo(); s_packagePreparedDefinition = package;
            var members = new List<CollectibleCard>();
            var manager = CollectionManager.Get();
            CollectionDeck edited = manager != null && manager.IsInEditMode() ? manager.GetEditedDeck() : null;
            foreach (var requirement in package.Cards)
            {
                var variants = ComponentCards(package, requirement.Id);
                if (edited != null && variants.Count > 0 && !ClassAllowedInDeck(variants[0], edited)) continue;
                foreach (CollectibleCard variant in variants) members.Add(variant);
            }
            if (members.Count == 0 || lists.Count == 0) return () => { foreach (var list in lists) list.Clear(); };
            var carrier = members[0];
            List<CollectibleCard> destination = lists.Find(list => list.Exists(card => card != null && card.CardId == carrier.CardId)) ?? lists[0];
            var action = new CategoryCatalog.Combo { Id = package.Id + ":add-all", Name = CanAddCombo() ? "Add All" : "Edit Package", IsPackage = true, AddAll = true,
                Artwork = package.Artwork, PackageClass = package.PackageClass, Category = package.Category, ExactVersions = package.ExactVersions, Cards = package.Cards };
            var proxy = (CollectibleCard)s_clone.Invoke(carrier, null);
            s_proxies.Add(proxy, action); s_proxyCosts.Add(proxy, new ProxyCost { Value = 0 });
            return () => {
                foreach (var list in lists) list.Clear();
                PreferredQualityCollapse.CollapseInPlace(members, "Package", null);
                members.Sort((a, b) => { int cost = a.ManaCost.CompareTo(b.ManaCost); return cost != 0 ? cost : StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name); });
                destination.Add(proxy); destination.AddRange(members);
            };
        }
        private static void PlacePackageBack(Rect bounds)
        {
            if (s_openPackage == null) return;
            if (bounds.width <= .001f || bounds.height <= .001f) return;
            s_packageBackArea = bounds;
            EnsurePackageNavigation();
            s_packageNavigation.Layout(bounds, !(UniversalInputManager.Get()?.IsDialogActive() ?? false));
        }
        private static void EnsurePackageNavigation()
        {
            if (s_packageNavigation == null) s_packageNavigation = new PackageNavigation(() => s_packageBackPending = true);
        }
    }
}
