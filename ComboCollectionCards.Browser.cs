using System;
using System.Collections.Generic;
using PegasusShared;

namespace BetterCollectionManager
{
    internal static partial class ComboCollectionCards
    {
        private static readonly Dictionary<string, Dictionary<string, string>> s_browserEditions = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, List<string>> s_browserCounterparts = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        internal static void CloseBrowserFolder() => ClearPackageNavigation();
        private static bool PackageSelected(CategoryCatalog.Combo package)
            => PackageViewState.IsActive ? PackageViewState.Matches(package) && s_browserEditions.ContainsKey(package.Id)
                : ExtraFilterState.MatchesPackage(package);
        private static bool TryBrowserEdition(CategoryCatalog.Combo combo, string member, out string edition)
        {
            edition = null;
            if (!PackageViewState.IsActive || !combo.IsPackage || string.IsNullOrEmpty(combo.Id)) return false;
            string id = combo.AddAll ? combo.Id.Substring(0, combo.Id.Length - ":add-all".Length) : combo.Id;
            return s_browserEditions.TryGetValue(id, out var members) && members.TryGetValue(member, out edition);
        }
        private static List<string> BrowserEditions(string id)
        {
            if (s_browserCounterparts.TryGetValue(id, out var result)) return result;
            result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Queue<string>(); pending.Enqueue(id);
            while (pending.Count > 0)
            {
                string next = pending.Dequeue();
                if (string.IsNullOrEmpty(next) || !seen.Add(next)) continue;
                result.Add(next);
                var counterparts = GameUtils.GetCounterpartCardIds(next);
                if (counterparts != null) foreach (string counterpart in counterparts) pending.Enqueue(counterpart);
            }
            return s_browserCounterparts[id] = result;
        }
        private static string BrowserEdition(string member, CollectionManager manager, CollectionDeck deck)
        {
            string selected = null; bool owned = false;
            foreach (string id in BrowserEditions(member))
            {
                var variants = new List<CollectibleCard>();
                foreach (var premium in ComponentPremiums)
                {
                    var card = manager.GetCard(id, premium);
                    if (card == null || !GameUtils.IsCardValidForFormat(PackageViewState.Format, card.GetEntityDef())
                        || deck != null && !GameUtils.IsCardValidForFormat(deck.FormatType, card.GetEntityDef())
                        || !ExpansionFilterState.Matches(card)) continue;
                    variants.Add(card);
                }
                if (variants.Count == 0) continue;
                bool hasCopies = variants.Exists(card => PreferredPremiumHelper.GetOwnedCount(manager, card, id) > 0 || card.TrialCount > 0);
                if (selected == null || hasCopies && !owned) { selected = id; owned = hasCopies; }
            }
            return selected;
        }
        internal static TAG_CLASS ClassForPackage(CategoryCatalog.Combo package)
        {
            if (Enum.TryParse(package.PackageClass, out TAG_CLASS declared) && (int)declared > 0 && Enum.IsDefined(typeof(TAG_CLASS), declared)) return declared;
            // Neutral members do not constrain the class. Intersect native class
            // lists so a dual-class member follows the other package members.
            List<TAG_CLASS> common = null;
            foreach (var member in package.Cards)
            {
                var variants = ComponentCards(package, member.Id);
                EntityDef definition = variants.Count > 0 ? variants[0].GetEntityDef()
                    : CollectionManager.Get()?.GetCard(member.Id, TAG_PREMIUM.NORMAL)?.GetEntityDef() ?? DefLoader.Get()?.GetEntityDef(member.Id);
                if (definition == null) continue;
                var classes = new List<TAG_CLASS>(); definition.GetClasses(classes);
                if (classes.Contains(TAG_CLASS.NEUTRAL)) continue;
                classes.RemoveAll(value => (int)value <= 0 || !Enum.IsDefined(typeof(TAG_CLASS), value));
                if (classes.Count == 0) continue;
                if (common == null) common = classes;
                else common.RemoveAll(value => !classes.Contains(value));
            }
            if (common == null || common.Count == 0) return TAG_CLASS.NEUTRAL;
            var manager = CollectionManager.Get();
            var deck = manager != null && manager.IsInEditMode() ? manager.GetEditedDeck() : null;
            if (deck != null)
            {
                if (common.Contains(deck.GetClass())) return deck.GetClass();
                var deckClasses = deck.GetClasses();
                if (deckClasses != null) foreach (var value in deckClasses) if (common.Contains(value)) return value;
            }
            return common[0];
        }
        private static Action PrepareBrowser<T>(IEnumerable<KeyValuePair<T, List<CollectibleCard>>> buckets)
        {
            var lists = new List<List<CollectibleCard>>();
            var classLists = new Dictionary<string, List<CollectibleCard>>(StringComparer.Ordinal);
            List<CollectibleCard> destination = null;
            foreach (var bucket in buckets)
            {
                if (bucket.Value == null) continue;
                lists.Add(bucket.Value);
                classLists[bucket.Key.ToString()] = bucket.Value;
                if (destination == null || bucket.Key.ToString() == "NEUTRAL") destination = bucket.Value;
            }
            InvalidateComponentIndex(); s_componentScopeIds.Clear();
            s_browserEditions.Clear(); s_browserCounterparts.Clear();
            var manager = CollectionManager.Get();
            var deck = manager != null && manager.IsInEditMode() ? manager.GetEditedDeck() : null;
            var visible = new List<CategoryCatalog.Combo>();
            if (Ready && PreferredQualityMode.IsEnabled && manager != null)
                foreach (var package in PackageCatalog.Packages)
                {
                    if (!PackageViewState.Matches(package)) continue;
                    var editions = new Dictionary<string, string>(StringComparer.Ordinal);
                    bool complete = package.Cards.Count > 0;
                    foreach (var member in package.Cards)
                    {
                        string id = BrowserEdition(member.Id, manager, deck);
                        if (id == null) { complete = false; break; }
                        editions[member.Id] = id;
                    }
                    if (!complete) continue;
                    s_browserEditions[package.Id] = editions;
                    if (!PackageAllowedInDeck(package)) { s_browserEditions.Remove(package.Id); continue; }
                    visible.Add(package);
                    foreach (string id in editions.Values) s_componentScopeIds.Add(id);
                }
            if (s_openPackage != null)
            {
                var saved = CurrentPackageDefinition();
                if (saved != null && PackageSelected(saved)) { s_openPackage = saved; return PreparePackage(lists); }
                ClearPackageNavigation();
            }
            var pending = new Dictionary<List<CollectibleCard>, List<CollectibleCard>>();
            foreach (var package in visible)
            {
                var variants = ComponentCards(package, package.Cards[0].Id);
                if (variants.Count == 0) continue;
                var proxy = (CollectibleCard)s_clone.Invoke(variants[0], null);
                s_proxies.Add(proxy, package); s_proxyCosts.Add(proxy, new ProxyCost { Value = 0 });
                var target = destination;
                if (typeof(T) == typeof(TAG_CLASS) && classLists.TryGetValue(ClassForPackage(package).ToString(), out var classList)) target = classList;
                if (target == null) continue;
                if (!pending.TryGetValue(target, out var proxies)) pending[target] = proxies = new List<CollectibleCard>();
                proxies.Add(proxy);
            }
            foreach (var proxies in pending.Values)
                proxies.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(CollectionName(a), CollectionName(b)));
            return () => {
                foreach (var list in lists) list.Clear();
                foreach (var entry in pending) entry.Key.AddRange(entry.Value);
            };
        }
    }
}
