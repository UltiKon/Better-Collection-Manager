using System;
using System.Collections.Generic;
using Assets;
using UnityEngine;

namespace BetterCollectionManager
{
    internal static partial class ComboCollectionCards
    {
        private static readonly HashSet<string> s_componentScopeIds = new HashSet<string>(StringComparer.Ordinal);
        private static readonly TAG_PREMIUM[] ComponentPremiums = { TAG_PREMIUM.NORMAL, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.DIAMOND, TAG_PREMIUM.SIGNATURE };
        private static Dictionary<string, List<string>> s_componentEditions;
        private static CollectionManager s_componentManager;
        private static void InvalidateComponentIndex() { s_componentEditions = null; s_componentManager = null; InvalidatePageHints(); }
        private static Dictionary<string, List<string>> ComponentEditions(CollectionManager manager)
        {
            if (s_componentEditions != null && ReferenceEquals(s_componentManager, manager)) return s_componentEditions;
            var index = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var all = manager?.GetAllCards();
            if (all != null)
                foreach (CollectibleCard card in all)
                {
                    if (card == null || string.IsNullOrEmpty(card.CardId)) continue;
                    string id = card.CardId, canonical = CategoryCatalog.NormalizeCardId(id);
                    if (!index.TryGetValue(canonical, out List<string> editions)) index[canonical] = editions = new List<string>();
                    if (!editions.Contains(id)) editions.Add(id);
                }
            s_componentManager = manager; return s_componentEditions = index;
        }
        internal static CollectibleCard PreferredComponent(CategoryCatalog.Combo combo, string id)
            => PreferredPremiumHelper.ChoosePreferred(ComponentCards(combo, id));

        internal static TAG_PREMIUM CommonPremium(CategoryCatalog.Combo combo)
        {
            if (combo.IsPackage) return TAG_PREMIUM.NORMAL;
            // A template describes the entire combo, so every component must
            // actually be owned in that quality. A catalog entry alone is not ownership.
            var components = new List<List<CollectibleCard>>();
            foreach (var requirement in combo.Cards) components.Add(ComponentCards(combo, requirement.Id));
            CollectionManager manager = CollectionManager.Get();
            foreach (TAG_PREMIUM premium in new[] { TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.DIAMOND, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.NORMAL })
            {
                bool common = components.Count > 0;
                foreach (var variants in components)
                {
                    CollectibleCard card = variants.Find(value => value.PremiumType == premium
                        && (manager != null ? manager.GetOwnedCount(value.CardId, premium) : value.OwnedCount) > 0);
                    if (card == null) { common = false; break; }
                }
                if (common) return premium;
            }
            return TAG_PREMIUM.NORMAL;
        }
        private static CollectibleCard ArtworkComponent(CategoryCatalog.Combo combo, string id, TAG_PREMIUM premium)
        {
            List<CollectibleCard> variants = ComponentCards(combo, id);
            return variants.Find(card => card.PremiumType == premium) ?? variants.Find(card => card.PremiumType == TAG_PREMIUM.NORMAL)
                ?? (variants.Count > 0 ? variants[0] : null);
        }

        internal static List<CollectibleCard> ComponentCards(CategoryCatalog.Combo combo, string id)
        {
            var result = new List<CollectibleCard>();
            CollectionManager manager = CollectionManager.Get();
            if (manager == null) return result;
            string selected = id;
            if (TryBrowserEdition(combo, id, out string browserEdition)) selected = browserEdition;
            else if (!combo.ExactVersions)
            {
                if (!ComponentEditions(manager).TryGetValue(id, out List<string> editions)) return result;
                bool classic = manager.GetThemeShowing() == PegasusShared.FormatType.FT_CLASSIC;
                selected = null; int best = int.MinValue;
                foreach (string edition in editions)
                {
                    bool vanilla = edition.StartsWith("VAN_", StringComparison.Ordinal);
                    int rank = (classic == vanilla ? 100 : 0) + (s_componentScopeIds.Contains(edition) ? 1000 : 0)
                        + (edition.StartsWith("CORE_", StringComparison.Ordinal) ? 10 : 0);
                    if (rank > best) { selected = edition; best = rank; }
                }
            }
            if (selected != null)
                foreach (TAG_PREMIUM premium in ComponentPremiums)
                {
                    CollectibleCard card = manager.GetCard(selected, premium);
                    if (card != null) result.Add(card);
                }
            return result;
        }
        internal static long ManaCost(CategoryCatalog.Combo combo)
        {
            if (combo.IsPackage) return 0; // Folders have no summed card mana cost.
            if (combo.Appearance.ManaCost.HasValue) return combo.Appearance.ManaCost.Value;
            long total = 0;
            foreach (CategoryCatalog.CardRequirement requirement in combo.Cards)
            {
                List<CollectibleCard> cards = ComponentCards(combo, requirement.Id);
                if (cards.Count > 0) total += (long)Math.Max(0, cards[0].ManaCost) * requirement.Count;
            }
            return total;
        }
        private static readonly DeckRule.RuleType[] s_ignoreNonClassRules = IgnoreNonClassRules();
        private static DeckRule.RuleType[] IgnoreNonClassRules()
        {
            var result = new List<DeckRule.RuleType>();
            foreach (DeckRule.RuleType rule in Enum.GetValues(typeof(DeckRule.RuleType)))
                if (rule != DeckRule.RuleType.IS_CLASS_CARD_OR_NEUTRAL && rule != DeckRule.RuleType.IS_DUAL_CLASS_CARD_OR_NEUTRAL) result.Add(rule);
            return result.ToArray();
        }
        internal static bool ClassAllowedInDeck(CollectibleCard card, CollectionDeck deck)
        {
            DeckRuleset rules = CollectionManager.Get()?.GetDeckRuleset();
            return rules == null || rules.CanAddToDeck(card.GetEntityDef(), card.PremiumType, deck,
                out List<RuleInvalidReason> reasons, out List<DeckRule> broken, s_ignoreNonClassRules);
        }
        private static bool CanAddCombo()
        {
            CollectionManager manager = CollectionManager.Get();
            CollectibleDisplay display = manager?.GetCollectibleDisplay();
            return PreferredQualityMode.IsEnabled && manager != null && manager.IsInEditMode()
                && manager.GetEditedDeck() != null && CollectionDeckTray.Get()?.GetCurrentDeckContext() != null
                && display != null && display.GetViewMode() == CollectionUtils.ViewMode.CARDS
                && display.GetViewSubmode() == CollectionUtils.ViewSubmode.DEFAULT
                && !CraftingManager.GetIsInCraftingMode() && !(CraftingTray.Get()?.IsShown() ?? false);
        }
        private static Actor DeckAnimation(EntityDef definition, TAG_PREMIUM premium, Actor source)
        {
            if (source == null) return null;
            GameObject go = AssetLoader.Get().InstantiatePrefab(ActorNames.GetHandActor(definition, premium), AssetLoadingOptions.IgnorePrefabPosition);
            try
            {
                Actor actor = go.GetComponent<Actor>();
                actor.SetPremium(premium); actor.SetEntityDef(definition); actor.UpdateAllComponents();
                actor.Show(); actor.SetActorState(ActorStateType.CARD_IDLE);
                go.transform.SetParent(source.transform.parent, false);
                new ActorPose(source.transform).Apply(go.transform);
                foreach (Transform node in go.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = source.gameObject.layer;
                foreach (Collider collider in go.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                return actor;
            }
            catch
            {
                if (go != null) { go.SetActive(false); UnityEngine.Object.Destroy(go); }
                throw;
            }
        }
        internal static int AddToDeck(CategoryCatalog.Combo combo, bool isDrag = false, TAG_PREMIUM? requested = null, Actor animationSource = null,
            TAG_PREMIUM? selectedQuality = null)
        {
            var animations = new List<Actor>();
            CollectionDeck root = CollectionManager.Get()?.GetEditedDeck();
            CollectionDeckTray tray = CollectionDeckTray.Get();
            CollectionDeck context = tray?.GetCurrentDeckContext();
            int added;
            try { added = AddToDeckCore(combo, isDrag, requested, animationSource, animations, selectedQuality); }
            finally
            {
                // Finish insertion/sorting first. The native flight clones a
                // deck-row actor, so a later insertion must not rebind that row
                // while an earlier flight is waiting for its artwork to load.
                foreach (Actor animation in animations)
                {
                    try
                    {
                        if (CanAddCombo() && ReferenceEquals(CollectionManager.Get()?.GetEditedDeck(), root)
                            && ReferenceEquals(CollectionDeckTray.Get(), tray) && ReferenceEquals(tray.GetCurrentDeckContext(), context))
                            tray.GetCurrentCardListContext().UpdateCardList(animation.GetEntityDef(), false, animation);
                    }
                    catch (Exception ex) { DebugLog.Exception("Combo deck animation unavailable", ex); }
                    finally { animation.gameObject.SetActive(false); animation.Destroy(); }
                }
            }
            if (added > 0) RefreshAddedCombo(combo);
            return added;
        }
        private static int AddToDeckCore(CategoryCatalog.Combo combo, bool isDrag, TAG_PREMIUM? requested, Actor animationSource, List<Actor> animations,
            TAG_PREMIUM? selectedQuality)
        {
            if (!CanAddCombo()) return 0;
            CollectionManager manager = CollectionManager.Get();
            CollectionDeck root = manager.GetEditedDeck();
            CollectionDeckTray tray = CollectionDeckTray.Get();
            CollectionDeck context = tray.GetCurrentDeckContext();
            int added = 0;
            foreach (CategoryCatalog.CardRequirement requirement in combo.Cards)
            {
                List<CollectibleCard> cards = ComponentCards(combo, requirement.Id);
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (CollectibleCard card in cards) ids.Add(card.CardId);
                int before = added;
                for (int copy = 0; copy < requirement.Count; copy++)
                {
                    if (!CanAddCombo() || !ReferenceEquals(manager.GetEditedDeck(), root)
                        || !ReferenceEquals(tray.GetCurrentDeckContext(), context)) return added;
                    bool inserted = false;
                    foreach (string id in ids)
                    {
                        var variants = cards.FindAll(card => card.CardId == id);
                        var request = new QualityAddRequest { RootDeck = root, Context = context,
                            Definition = variants[0].GetEntityDef(), Variants = variants,
                            Requested = requested ?? PreferredPremiumPrefs.Order[0], IsDrag = isDrag };
                        List<TAG_PREMIUM> choices = QualityDeckInput.Choices(request, Array.Empty<DeckRule.RuleType>());
                        if (selectedQuality.HasValue && choices.Remove(selectedQuality.Value)) choices.Insert(0, selectedQuality.Value);
                        if (choices.Count == 0)
                            foreach (CollectibleCard variant in variants)
                            {
                                var failures = new List<string>();
                                DeckRuleset rules = manager.GetDeckRuleset();
                                if (rules != null)
                                {
                                    rules.CanAddToDeck(request.Definition, variant.PremiumType, context,
                                        out List<RuleInvalidReason> reasons, out List<DeckRule> broken, Array.Empty<DeckRule.RuleType>());
                                    if (broken != null) foreach (DeckRule rule in broken) failures.Add(rule.Type.ToString());
                                }
                                DebugLog.Msg("Combo deck rejected: " + id + "; premium=" + variant.PremiumType
                                    + "; remaining=" + QualityDeckInput.Remaining(request, variant.PremiumType)
                                    + "; rules=" + string.Join(",", failures));
                            }
                        foreach (TAG_PREMIUM premium in choices)
                        {
                            if (!tray.AddCard(request.Definition, premium, true, null, Array.Empty<DeckRule.RuleType>())) continue;
                            if (animationSource != null)
                                try
                                {
                                    Actor animation = DeckAnimation(request.Definition, premium, animationSource);
                                    animation.gameObject.SetActive(false); animations.Add(animation);
                                }
                                catch (Exception ex) { DebugLog.Exception("Combo animation source unavailable", ex); }
                            tray.OnCardManuallyAddedByUser_CheckSuggestions(request.Definition);
                            added++; inserted = true; break;
                        }
                        if (inserted) break;
                    }
                    if (!inserted) break; // Native ownership, class, format, copy and deck-size limits apply.
                }
                DebugLog.Msg("Combo deck component: " + requirement.Id + "; edition=" + string.Join(",", ids)
                    + "; requested=" + requirement.Count + "; added=" + (added - before));
            }
            DebugLog.Msg("Combo added to deck: " + combo.Name + "; cards=" + added);
            return added;
        }

        private static ComboDrag s_comboQualityChoice;
        private static void BeginComboQualityChoice(ComboDrag drag)
        {
            if (!ValidDrag(drag)) return;
            var choices = new List<TAG_PREMIUM>();
            foreach (var component in drag.Combo.Cards)
            {
                List<CollectibleCard> variants = ComponentCards(drag.Combo, component.Id);
                if (variants.Count == 0) continue;
                var member = new QualityAddRequest { RootDeck = drag.Root, Context = drag.Context,
                    Definition = variants[0].GetEntityDef(), Variants = variants, Requested = drag.Premium, IsDrag = true };
                foreach (TAG_PREMIUM quality in QualityDeckInput.Choices(member, Array.Empty<DeckRule.RuleType>()))
                    if (!choices.Contains(quality)) choices.Add(quality);
            }
            if (choices.Count == 0) return;
            s_comboQualityChoice = drag;
            var request = new QualityAddRequest { RootDeck = drag.Root, Context = drag.Context, IsDrag = true,
                Title = drag.Combo.Name + " · Choose quality",
                ValidateContext = () => ReferenceEquals(s_comboQualityChoice, drag) && ValidDrag(drag, true),
                Canceled = () => { if (ReferenceEquals(s_comboQualityChoice, drag)) s_comboQualityChoice = null; } };
            request.Confirmed = premium => {
                if (!ReferenceEquals(s_comboQualityChoice, drag) || !ValidDrag(drag)) return;
                s_comboQualityChoice = null;
                // One preference applies to every captured copy. The normal
                // add path revalidates ownership/rules and supplies fallbacks.
                AddToDeck(drag.Combo, true, drag.Premium, drag.Source?.GetActor(), selectedQuality: premium);
            };
            try { QualityChoiceMenu.Open(request, choices, PreferredPremiumHelper.PremiumName); }
            catch { if (ReferenceEquals(s_comboQualityChoice, drag)) s_comboQualityChoice = null; throw; }
        }
        private static void CancelComboQualityChoice()
        {
            if (s_comboQualityChoice == null) return;
            s_comboQualityChoice = null; QualityChoiceMenu.Cancel();
        }
    }
}
