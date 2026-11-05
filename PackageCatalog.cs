using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BetterCollectionManager
{
    internal enum PackageKind { Parasitic = 1, Factions = 2, Custom = 3 }

    // Packages are a separate collection-entry type and source catalog. Their
    // component cards never become a standalone Categories → Packages result.
    internal static class PackageCatalog
    {
        internal readonly struct Group
        {
            internal readonly PackageKind Kind;
            internal readonly string Name;
            internal Group(PackageKind kind, string name) { Kind = kind; Name = name; }
        }
        private static readonly List<Group> s_groups = new List<Group>();
        static PackageCatalog() { ResetGroups(); }
        internal static IReadOnlyList<Group> Groups => s_groups.AsReadOnly();
        internal static void ResetGroups()
        {
            s_groups.Clear();
            foreach (PackageKind kind in new[] { PackageKind.Parasitic, PackageKind.Factions, PackageKind.Custom })
                s_groups.Add(new Group(kind, kind.ToString()));
        }
        internal static string GroupName(PackageKind kind)
        {
            foreach (Group group in s_groups) if (group.Kind == kind) return group.Name;
            throw new InvalidOperationException("The package folder no longer exists.");
        }
        internal static PackageKind EnsureGroup(string name)
        {
            name = CategoryDeckImport.CleanName(name);
            string key = CategoryDeckImport.NameKey(name);
            if (key == "PACKAGES") return PackageKind.Custom; // Legacy version 6 destination.
            foreach (Group group in s_groups) if (CategoryDeckImport.NameKey(group.Name) == key) return group.Kind;
            uint hash = 2166136261;
            foreach (char c in key) unchecked { hash = (hash ^ c) * 16777619; }
            var kind = (PackageKind)(1000 + (int)(hash & 0x3fffffff));
            foreach (Group group in s_groups) if (group.Kind == kind) throw new FormatException("Folder name collision; choose another name.");
            s_groups.Add(new Group(kind, name));
            return kind;
        }
        internal sealed class Package : CategoryCatalog.Combo
        {
            internal Package(PackageKind kind = PackageKind.Custom) { IsPackage = true; PackageGroup = kind; Category = CuratedCategory.Packages; }
        }
        private static readonly Dictionary<string, string> PreviousGenerated = new Dictionary<string, string>(StringComparer.Ordinal) {
            { "package-wildseeds", "Wildseeds" },
            { "package-rituals", "Rituals" },
            { "package-jade-golems", "Jade Golems" },
            { "package-relics", "Relics" },
            { "package-soul-fragments", "Soul Fragments" },
            { "package-librams", "Librams" },
            { "package-galakrond", "Galakrond" },
            { "package-cthun", "C'Thun" },
            { "package-lackeys", "Lackeys" },
            { "package-abyssal-curses", "Abyssal Curses" },
            { "package-plagues", "Plagues" },
            { "package-bombs", "Bombs" },
            { "package-spellstones", "Spellstones" },
            { "package-treants", "Treants" },
            { "package-starships", "Starships" },
            { "package-incindius", "Eruptions" },
            { "package-concoctions", "Concoctions" },
            { "package-treasure-excavation", "Excavate" },
            { "package-pogo-hoppers", "Pogo-Hoppers" },
            { "package-silver-hand", "Silver Hand Recruits" },
            { "package-skeletons", "Volatile Skeletons" },
            { "package-shuffle-bears", "Ambushes" },
            { "package-grim-patrons", "Grim Patrons" },
            { "package-sigil", "Sigils" },
            { "package-rafam", "Rafaams" },
            { "package-primes", "Prime Legendaries" },
            { "package-zerg", "Zerg" },
            { "package-terran", "Terran" },
            { "package-protoss", "Protoss" },
            { "package-cthun-empire", "C'Thun Abilities" },
            { "package-earthen", "Earthen" },
            { "package-chaotic-tendrils", "Chaotic Tendrils" },
            { "package-riffs", "Riffs" },
            { "package-asteroids", "Asteroids" },
            { "package-imbue", "Imbue" },
            { "package-dark-gifts", "Dark Gifts" },
            { "package-animal-companions", "Animal Companions" },
            { "package-leylines", "Leylines" },
            { "package-ogre-gang", "Ogre-Gang" },
            { "package-dream-cards", "Dream Cards" },
            { "package-wisps", "Wisps" },
            { "package-sparkbots", "Sparkbots" },
            { "package-tickets", "Tickets" },
            { "package-jade-displays", "Jade Displays" },
            { "package-astral-automatons", "Astral Automatons" },
            { "package-cthun-shattered", "C'Thun, the Shattered" },
            { "faction-grimy-goons", "Grimy Goons" },
            { "faction-jade-lotus", "Jade Lotus" },
            { "faction-kabal", "Kabal" },
            { "faction-alliance", "Alliance" },
            { "faction-horde", "Horde" },
            { "package-fabled-time_009", "Gelbin of Tomorrow" },
            { "package-fabled-time_020", "Broxigar" },
            { "package-fabled-time_209", "Muradin, High King" },
            { "package-fabled-time_211", "Lady Azshara" },
            { "package-fabled-time_609", "Ranger General Sylvanas" },
            { "package-fabled-time_619", "Talanji of the Graves" },
            { "package-fabled-time_850", "Lo'Gosh, Blood Fighter" },
            { "package-fabled-time_852", "Azure Queen Sindragosa" },
            { "package-fabled-time_875", "Garona Halforcen" },
            { "package-fabled-time_890", "Medivh the Hallowed" },
        };
        private static IReadOnlyList<CategoryCatalog.Entry> s_bundled;
        private static IReadOnlyList<CategoryCatalog.Combo> s_packages = Array.Empty<CategoryCatalog.Combo>();
        internal static IReadOnlyList<CategoryCatalog.Combo> Packages => s_packages;
        internal static void SetActive(List<CategoryCatalog.Combo> entries)
        {
            var packages = new List<CategoryCatalog.Combo>();
            foreach (var entry in entries) if (entry.IsPackage) packages.Add(entry);
            s_packages = packages.AsReadOnly();
        }
        internal static PackageKind LegacyKind(string id)
            => id == "package-zerg" || id == "package-terran" || id == "package-protoss" || id.StartsWith("faction-", StringComparison.Ordinal)
                ? PackageKind.Factions : PackageKind.Parasitic;
        internal static List<CategoryCatalog.Combo> BuiltIns(IReadOnlyList<CategoryCatalog.Entry> legacy)
        {
            if (s_bundled == null)
            {
                using (Stream stream = typeof(PackageCatalog).Assembly.GetManifestResourceStream("BetterCollectionManager.Packages.json"))
                using (var reader = new StreamReader(stream, Encoding.UTF8)) s_bundled = CategoryCatalog.Parse(reader.ReadToEnd());
            }
            var result = new List<CategoryCatalog.Combo>(); var seen = new HashSet<string>(StringComparer.Ordinal);
            void Add(CategoryCatalog.Entry entry, bool custom = false)
            {
                string id = custom ? "legacy:" + entry.Id : entry.Id;
                if (!entry.Categories.ContainsCategory(CuratedCategory.Packages) || !seen.Add(id)) return;
                var package = new Package(custom ? PackageKind.Custom : entry.PackageGroup) { Id = "builtin:" + id, Name = entry.Name,
                    Description = custom ? "Custom package" : entry.PackageGroup == PackageKind.Factions ? "Faction package" : "Parasitic archetype package",
                    Cards = entry.Cards, PackageClass = entry.PackageClass, ExactVersions = false, Artwork = entry.Cards[0].Id };
                result.Add(package);
            }
            foreach (CategoryCatalog.Entry entry in s_bundled) Add(entry);
            // Old generated keyword/faction buckets must not revive retired definitions.
            foreach (CategoryCatalog.Entry entry in legacy)
                if (!PreviousGenerated.TryGetValue(entry.Id, out string originalName) || entry.Name != originalName) Add(entry, custom: true);
            return result;
        }
        private static bool ContainsCategory(this IReadOnlyList<CuratedCategory> values, CuratedCategory value)
        { foreach (CuratedCategory item in values) if (item == value) return true; return false; }
        internal static bool Matches(CategoryCatalog.Combo entry, PackageKind kind) => entry.IsPackage && entry.PackageGroup == kind;
    }
}
