using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BetterCollectionManager
{
    internal static class PackageArtwork
    {
        internal sealed class Choice
        {
            internal string Id, Name;
            internal EntityDef Definition;
        }
        // Definitions may be shared; eligibility is always scoped to the captured
        // package. Typing an unrelated card ID cannot expand that scope.
        private static readonly Dictionary<string, Choice> s_definitions = new Dictionary<string, Choice>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Catalog> s_catalogs = new Dictionary<string, Catalog>(StringComparer.Ordinal);
        private static bool s_referenceFailureLogged;
        internal const int MaxCachedCatalogs = 32;
        private static readonly Queue<string> s_catalogOrder = new Queue<string>();
        internal static void Clear() { s_catalogs.Clear(); s_catalogOrder.Clear(); s_definitions.Clear(); s_referenceFailureLogged = false; }

        private static bool HasArtwork(EntityDef definition)
        {
            TAG_CARDTYPE type = (TAG_CARDTYPE)definition.GetTag(GAME_TAG.CARDTYPE);
            return type == TAG_CARDTYPE.MINION || type == TAG_CARDTYPE.SPELL || type == TAG_CARDTYPE.WEAPON
                || type == TAG_CARDTYPE.HERO || type == TAG_CARDTYPE.LOCATION;
        }
        private static Choice Remember(EntityDef def, string name = null)
        {
            string id = def?.GetCardId();
            if (string.IsNullOrEmpty(id) || !HasArtwork(def)) return null;
            if (string.IsNullOrEmpty(name)) name = def.GetName();
            var choice = new Choice { Id = id, Name = string.IsNullOrWhiteSpace(name) ? id : name, Definition = def };
            s_definitions[id] = choice; return choice;
        }
        internal static EntityDef Resolve(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (s_definitions.TryGetValue(id, out Choice choice)) return choice.Definition;
            EntityDef definition = CollectionManager.Get()?.GetCard(id, TAG_PREMIUM.NORMAL)?.GetEntityDef()
                ?? DefLoader.Get()?.GetEntityDef(id);
            return definition?.GetCardId() == id && Remember(definition) != null ? definition : null;
        }
        private static bool IsToken(EntityDef def) => def.GetTag(GAME_TAG.COLLECTIBLE) == 0;
        private static IEnumerable<string> References(EntityDef def)
        {
            // This is the client's collection-inspect relation API. It reads
            // RelatedCards DBF records and falls back to related-card tags.
            try { return GameUtils.GetRelatedCardIds(def, out _); }
            catch (Exception ex)
            {
                if (!s_referenceFailureLogged)
                { s_referenceFailureLogged = true; DebugLog.Warning("Package related artwork: " + ex.Message); }
                return Array.Empty<string>();
            }
        }
        internal sealed class Catalog
        {
            private readonly Dictionary<string, Choice> m_choices = new Dictionary<string, Choice>(StringComparer.Ordinal);
            private readonly HashSet<string> m_seen = new HashSet<string>(StringComparer.Ordinal);
            private readonly Queue<EntityDef> m_pending = new Queue<EntityDef>();
            private readonly Queue<string> m_references = new Queue<string>();
            internal Catalog(IEnumerable<string> roots)
            {
                foreach (string id in roots)
                {
                    EntityDef def = Resolve(id);
                    if (def != null && m_seen.Add(id)) { m_choices[id] = s_definitions[id]; m_pending.Enqueue(def); }
                }
            }
            internal bool Complete => m_pending.Count == 0 && m_references.Count == 0;
            internal int Revision { get; private set; }
            internal bool Advance()
            {
                var timer = Stopwatch.StartNew(); int scanned = 0;
                while ((m_pending.Count > 0 || m_references.Count > 0) && scanned++ < 100 && timer.ElapsedMilliseconds < 4)
                {
                    if (m_pending.Count > 0)
                    {
                        foreach (string id in References(m_pending.Dequeue()))
                            if (!string.IsNullOrEmpty(id) && m_seen.Add(id)) m_references.Enqueue(id);
                        continue;
                    }
                    string reference = m_references.Dequeue();
                    EntityDef token = Resolve(reference);
                    // A referenced collectible is not package art and must
                    // not open another collectible's family of tokens.
                    if (token == null || !IsToken(token)) continue;
                    m_choices[reference] = s_definitions[reference]; m_pending.Enqueue(token); Revision++;
                }
                return m_pending.Count > 0 || m_references.Count > 0;
            }
            internal bool Contains(string id) => id != null && m_choices.ContainsKey(id);
            internal List<Choice> Search(string query)
            {
                query = (query ?? "").Trim(); var result = new List<Choice>();
                foreach (Choice choice in m_choices.Values)
                    if (query.Length == 0 || choice.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0
                        || choice.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) result.Add(choice);
                result.Sort((a, b) => { int order = StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name);
                    return order != 0 ? order : StringComparer.Ordinal.Compare(a.Id, b.Id); });
                return result;
            }
        }
        internal static Catalog For(IEnumerable<string> roots)
        {
            var ids = new List<string>(new HashSet<string>(roots, StringComparer.Ordinal)); ids.Sort(StringComparer.Ordinal);
            string key = string.Join("\n", ids);
            if (!s_catalogs.TryGetValue(key, out Catalog catalog))
            {
                while (s_catalogs.Count >= MaxCachedCatalogs) s_catalogs.Remove(s_catalogOrder.Dequeue());
                s_catalogs[key] = catalog = new Catalog(ids); s_catalogOrder.Enqueue(key);
            }
            return catalog;
        }
        internal static EntityDef ResolveFor(CategoryCatalog.Combo package)
        {
            // The common case needs one definition, not a catalog of every member/token.
            foreach (var card in package.Cards) if (card.Id == package.Artwork) return Resolve(package.Artwork);
            var ids = new List<string>(); foreach (var card in package.Cards) ids.Add(card.Id);
            Catalog catalog = For(ids);
            // A saved selection must belong to this package too. Old globally
            // chosen artwork falls back to a contained card without rewriting it.
            if (!catalog.Contains(package.Artwork)) catalog.Advance();
            if (catalog.Contains(package.Artwork)) return Resolve(package.Artwork);
            return catalog.Complete && ids.Count > 0 ? Resolve(ids[0]) : null;
        }
    }
}
