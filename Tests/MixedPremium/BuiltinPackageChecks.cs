using System.Collections.Generic;
using System.Linq;
using BetterCollectionManager;

internal static partial class Program
{
    private static void CheckBuiltinPackageResults()
    {
        var family = PackageCatalog.Packages.Single(p => p.Name == "Wildseeds");
        var faction = PackageCatalog.Packages.First(p => p.PackageGroup == PackageKind.Factions);
        var seedCard = Card(family.Cards[0].Id, TAG_PREMIUM.NORMAL, 1);
        var factionCard = Card(faction.Cards[0].Id, TAG_PREMIUM.NORMAL, 1);
        var categoryMode = FilterModes.Get(FilterTrayKind.Packages);
        try
        {
            ComboCollectionCards.BackFromPackage(); ExtraFilterState.ClearAll();
            FilterModes.Set(FilterTrayKind.Packages, FilterMode.Single);
            ComboCollectionCards.Ready = true;
            List<CollectibleCard> Results(CollectibleCard member)
            {
                var cards = new List<CollectibleCard> { member };
                var insert = ComboCollectionCards.Prepare(new Dictionary<int, List<CollectibleCard>> { [1] = cards });
                cards.Clear(); insert(); return cards;
            }
            ExtraFilterState.ClearPackages(); ExtraFilterState.TogglePackage(PackageKind.Parasitic);
            int inventoryReads = CollectionManager.Get().GetAllCardsCalls, portraits = CardDef.PortraitLoads;
            var results = Results(seedCard);
            Check(CollectionManager.Get().GetAllCardsCalls == inventoryReads && CardDef.PortraitLoads == portraits,
                "Preparing package results does not enumerate the inventory or load portraits for off-page folders");
            Check(ComboCollectionCards.CollectionManaCost(results[0]) == 0
                && ComboCollectionCards.CommonPremium(family) == TAG_PREMIUM.NORMAL
                && CollectionManager.Get().GetAllCardsCalls == inventoryReads,
                "Folder sorting and appearance do not scan all components for summed mana, quality or rune requirements");
            Check(results.Any(card => ComboCollectionCards.StableId(card) == "BCM_COMBO_" + family.Id)
                && results.All(card => ComboCollectionCards.StableId(card).StartsWith("BCM_COMBO_")),
                "A matching package member produces its bundled folder without standalone cards or requiring every class member");
            ExtraFilterState.ClearPackages(); ExtraFilterState.TogglePackage(PackageKind.Factions);
            results = Results(factionCard);
            Check(results.Any(card => ComboCollectionCards.StableId(card) == "BCM_COMBO_" + faction.Id)
                && results.All(card => ComboCollectionCards.StableId(card) != "BCM_COMBO_" + family.Id),
                "The peer Factions filter inserts faction folder proxies and excludes parasitic folders");
            var jade = PackageCatalog.Packages.Single(p => p.Name == "Jade Golems · Druid");
            var shared = Card("CFM_715", TAG_PREMIUM.NORMAL, 1);
            ExtraFilterState.ClearPackages(); ExtraFilterState.TogglePackage(PackageKind.Parasitic);
            var warriorCards = new List<CollectibleCard>();
            var neutralCards = new List<CollectibleCard> { shared };
            var insertWarrior = ComboCollectionCards.Prepare(new Dictionary<TAG_CLASS, List<CollectibleCard>> {
                [TAG_CLASS.WARRIOR] = warriorCards, [TAG_CLASS.NEUTRAL] = neutralCards });
            neutralCards.Clear(); insertWarrior();
            Check(!warriorCards.Concat(neutralCards).Any(card => ComboCollectionCards.StableId(card).Contains("jade-golems")),
                "A shared faction card cannot leak another class's package into the Warrior results");
            var druidCards = new List<CollectibleCard>(); neutralCards.Add(shared);
            var insertDruid = ComboCollectionCards.Prepare(new Dictionary<TAG_CLASS, List<CollectibleCard>> {
                [TAG_CLASS.DRUID] = druidCards, [TAG_CLASS.NEUTRAL] = neutralCards });
            neutralCards.Clear(); insertDruid();
            Check(druidCards.Any(card => ComboCollectionCards.StableId(card) == "BCM_COMBO_" + jade.Id)
                && !druidCards.Concat(neutralCards).Any(card => ComboCollectionCards.StableId(card).Contains("jade-golems-rogue")),
                "Shared legal members produce only the selected class branch, in its matching class bucket");
            ExtraFilterState.ClearPackages(); ExtraFilterState.TogglePackage(PackageKind.Factions);
            ExtraFilterState.OtherFilterMatch = card => false;
            Check(Results(factionCard).Count == 0,
                "Native and other custom filter exclusions remove a folder when none of its members remain in scope");
            ExtraFilterState.OtherFilterMatch = card => true;
            ExtraFilterState.ToggleCategory(CuratedCategory.Combos);
            Check(Results(seedCard).Count == 0,
                "Categories Combos does not insert built-in package folders");
        }
        finally
        {
            ExtraFilterState.OtherFilterMatch = card => true; ExtraFilterState.ClearAll();
            FilterModes.Set(FilterTrayKind.Packages, categoryMode);
        }
    }
}
