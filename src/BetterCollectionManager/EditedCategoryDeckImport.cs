using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace BetterCollectionManager
{
    internal static class EditedCategoryDeckImport
    {
        public static string Import(string categoryName = null)
        {
            CategoryDeckImport.Deck deck = Capture(categoryName ?? SelectedCategoryName() ?? throw new InvalidOperationException("Enter a category name."));
            return CategoryCatalog.ImportDeckCards(deck.Name, deck.Cards);
        }

        public static string RemoveFromSelectedCategory(string categoryName = null)
        {
            if (ExtraFilterState.HasCategory(CuratedCategory.Combos))
                throw new InvalidOperationException("Choose a card-role category to remove standalone cards.");
            if (ExtraFilterState.HasCategory(CuratedCategory.Uncategorized))
                throw new InvalidOperationException("Assign cards to a named category to remove them from Uncategorized.");
            CategoryDeckImport.Deck deck = Capture(categoryName ?? SelectedCategoryName() ?? throw new InvalidOperationException("Enter a category name."));
            return CategoryCatalog.RemoveDeckCards(deck.Name, deck.Cards);
        }

        internal static string SelectedCategoryName()
        {
            CategoryCatalog.Category? selected = null;
            foreach (CategoryCatalog.Category category in CategoryCatalog.Categories)
                if (CategoryCatalog.IsComboCategory(category.Value) && ExtraFilterState.HasCategory(category.Value))
                {
                    if (selected.HasValue) throw new InvalidOperationException("Select exactly one category.");
                    selected = category;
                }
            return selected?.Name;
        }

        internal static CategoryDeckImport.Deck Capture(string categoryName = null, bool allowUnnamed = false)
        {
            CollectionManager manager = CollectionManager.Get();
            if (manager == null || !manager.IsInEditMode() || manager.GetEditedDeck() == null)
                throw new InvalidOperationException("Open the deck you want to import first.");
            // The root edited deck includes sideboards; the tray context can instead
            // point at a selected sideboard and must not choose the import's identity.
            CollectionDeck deck = manager.GetEditedDeck();
            string name = categoryName ?? ReadName(deck);
            if (allowUnnamed && string.IsNullOrWhiteSpace(name)) name = "Value";
            if (name == null)
            {
                DebugLog.Warning("Category import deck name API: " + NameApi(deck.GetType()));
                throw new InvalidOperationException("Could not read the current deck name.");
            }
            name = CategoryDeckImport.CleanName(name);
            var all = manager.GetAllCards();
            if (all == null) throw new InvalidOperationException("Collection cards are still loading.");
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var cards = new HashSet<string>(StringComparer.Ordinal);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var rawCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var premiums = new[] { TAG_PREMIUM.NORMAL, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.DIAMOND, TAG_PREMIUM.SIGNATURE };
            // GetAllCards is the complete native catalog, before set/text/custom
            // filtering. Query live slots instead of exporting or validating a deck.
            foreach (CollectibleCard card in all)
            {
                string id = card?.CardId;
                if (string.IsNullOrEmpty(id) || !visited.Add(id)) continue;
                int count = 0;
                foreach (TAG_PREMIUM premium in premiums) count += Math.Max(0, deck.GetCardCountAllMatchingSlots(id, premium));
                if (count > 0)
                {
                    cards.Add(id);
                    rawCounts[id] = count;
                    string normalized = CategoryCatalog.NormalizeCardId(id);
                    counts.TryGetValue(normalized, out int previous); counts[normalized] = previous + count;
                }
            }
            if (!manager.IsInEditMode() || !ReferenceEquals(deck, manager.GetEditedDeck()))
                throw new InvalidOperationException("The edited deck changed; try importing again.");
            var snapshot = new CategoryDeckImport.Deck { Name = name };
            snapshot.Cards.UnionWith(cards);
            foreach (var pair in counts) snapshot.Counts[pair.Key] = pair.Value;
            foreach (var pair in rawCounts) snapshot.RawCounts[pair.Key] = pair.Value;
            return snapshot;
        }

        // Client versions expose the deck name through different wrappers. Keep
        // those accesses optional so missing name metadata cannot break menu startup.
        internal static string ReadName(object deck, int depth = 0)
        {
            if (deck == null || depth > 2) return null;
            foreach (string member in new[] { "GetName", "GetDeckName", "Name", "DeckName", "m_name", "m_deckName" })
                if (Read(deck, member) is string name) return name;
            foreach (string member in new[] { "GetDeck", "Deck", "m_deck", "GetDeckData", "DeckData", "m_deckData" })
            {
                object nested = Read(deck, member);
                if (nested == null || ReferenceEquals(nested, deck)) continue;
                string name = ReadName(nested, depth + 1);
                if (name != null) return name;
            }
            return null;
        }

        private static object Read(object target, string member)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                MethodInfo method = type.GetMethod(member, flags, null, Type.EmptyTypes, null);
                if (method != null && !method.ContainsGenericParameters) return method.Invoke(target, null);
                PropertyInfo property = type.GetProperty(member, flags);
                if (property != null && property.GetIndexParameters().Length == 0) return property.GetValue(target, null);
                FieldInfo field = type.GetField(member, flags);
                if (field != null) return field.GetValue(target);
            }
            return null;
        }

        private static string NameApi(Type type)
        {
            var text = new StringBuilder(type.FullName);
            foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                if (member.Name.IndexOf("Name", StringComparison.OrdinalIgnoreCase) >= 0
                    || member.Name.IndexOf("Deck", StringComparison.OrdinalIgnoreCase) >= 0)
                    text.Append("; ").Append(member);
            return text.ToString();
        }
    }
}
