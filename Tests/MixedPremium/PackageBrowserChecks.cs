using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterCollectionManager;
using PegasusShared;

internal static partial class Program
{
    private static void CheckPackageBrowser()
    {
        var previousManager = CollectionManager.Instance;
        var previousRefresh = KeywordFilterApplier.OnRefresh;
        bool previousReady = ComboCollectionCards.Ready;
        var previousMatcher = ExtraFilterState.OtherFilterMatch;
        string directory = Path.Combine(Path.GetTempPath(), "bcm-package-browser-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "categories.json");
        try
        {
            PackageBrowser.Reset(); CollectionFilterMenu.Detach();
            var manager = new CollectionManager(); CollectionManager.Instance = manager;
            manager.Display.ViewMode = CollectionUtils.ViewMode.CARDS;
            ComboCollectionCards.Ready = PreferredQualityMode.IsEnabled = true;
            CategoryCatalog.LoadUserFile(path);
            var standard = Add("BROWSER_STANDARD", TAG_CARD_SET.CORE);
            var standardTwo = Add("BROWSER_STANDARD_2", TAG_CARD_SET.CORE);
            var wild = Add("BROWSER_WILD", TAG_CARD_SET.TEST_EXPANSION);
            var legacy = Add("BROWSER_REPRINT", TAG_CARD_SET.TEST_EXPANSION);
            var reprint = Add("CORE_BROWSER_REPRINT", TAG_CARD_SET.CORE);
            Class(standard, TAG_CLASS.MAGE); Class(standardTwo, TAG_CLASS.MAGE, TAG_CLASS.ROGUE);
            Class(wild, TAG_CLASS.WARRIOR); Class(legacy, TAG_CLASS.ROGUE); Class(reprint, TAG_CLASS.ROGUE);
            GameUtils.CounterpartCardIds[legacy.CardId] = new List<string> { reprint.CardId };
            GameUtils.CounterpartCardIds[reprint.CardId] = new List<string> { legacy.CardId };
            var complete = Save("All Standard", "Custom", standard, standardTwo);
            var mixed = Save("Mixed expansions", "Custom", standard, wild);
            var wildOnly = Save("All Wild", "Factions", wild);
            var reprinted = Save("Legal reprint", "Custom", legacy);
            var pages = new FolderPages(); pages.Pages.Add(new List<CollectibleCard> { standard }); pages.Pages.Add(new List<CollectibleCard> { standardTwo });
            manager.Display.m_cardsCollection = pages; manager.Display.CurrentPageNum = 2;

            // Simulate a card query that has no native results and whose custom
            // predicates reject every card. These must not restrict Packages.
            var native = new CollectibleCardClassFilter { m_filterText = "no matches", m_filterCardSets = new[] { TAG_CARD_SET.TEST_EXPANSION } };
            native.m_currentResultsByClass[TAG_CLASS.NEUTRAL] = new List<CollectibleCard>();
            foreach (var value in new[] { TAG_CLASS.MAGE, TAG_CLASS.ROGUE, TAG_CLASS.WARRIOR })
                native.m_currentResultsByClass[value] = new List<CollectibleCard>();
            ExtraFilterState.ClearAll(); KeywordFilterState.Clear(); ExpansionFilterState.Clear();
            ExtraFilterState.ToggleCategory(CuratedCategory.Value); ExtraFilterState.ToggleType(TAG_CARDTYPE.SPELL);
            ExtraFilterState.OtherFilterMatch = card => false;
            KeywordFilterState.SetSelected(GAME_TAG.ATK, true);
            FilterModes.Set(FilterTrayKind.Expansions, FilterMode.Or);
            var cardsExpansion = new SetFilterItem { CardSets = new List<TAG_CARD_SET> { TAG_CARD_SET.CORE } };
            ExpansionFilterState.Toggle(cardsExpansion);
            var cardTerms = ExpansionFilterState.Capture(); var keywords = KeywordFilterState.Capture();
            KeywordFilterApplier.OnRefresh = Rebuild;
            PackageBrowser.Enter();
            Check(PackageViewState.Format == FormatType.FT_STANDARD && !ExpansionFilterState.IsActive && !PackageViewState.Group.HasValue
                && Names().SetEquals(new[] { complete.Name, reprinted.Name }),
                "Entering Packages inherits Standard and shows all groups independently of native searches, card categories, types and keywords");
            Check(ComboCollectionCards.ComponentCards(reprinted, legacy.CardId).All(card => card.CardId == reprint.CardId)
                && reprinted.Cards[0].Id == legacy.CardId,
                "A legal counterpart supplies package artwork/input while the saved member edition remains intact");
            Check(native.m_filterText == "no matches" && native.m_filterCardSets.SequenceEqual(new[] { TAG_CARD_SET.TEST_EXPANSION }),
                "Package queries leave the native card query unchanged");
            Check(Bucket(TAG_CLASS.MAGE).SequenceEqual(new[] { complete.Name }) && Bucket(TAG_CLASS.ROGUE).SequenceEqual(new[] { reprinted.Name })
                && Bucket(TAG_CLASS.NEUTRAL).Count == 0,
                "Packages use native class buckets, intersecting dual-class members and resolving reprint class definitions");

            PackageBrowser.ToggleFormat();
            Check(PackageViewState.Format == FormatType.FT_WILD && Names().SetEquals(new[] { complete.Name, mixed.Name, wildOnly.Name, reprinted.Name }),
                "The direct format toggle switches to every Wild-compatible package without opening expansions");
            CheckFormatPageAnchors();
            var onlyCore = new SetFilterItem { CardSets = new List<TAG_CARD_SET> { TAG_CARD_SET.CORE } };
            ExpansionFilterState.Toggle(onlyCore); PackageBrowser.QueryChanged();
            Check(Names().SetEquals(new[] { complete.Name, reprinted.Name }),
                "One member outside the selected expansion excludes the whole package");
            FilterModes.Set(FilterTrayKind.Expansions, FilterMode.Or);
            var onlyWild = new SetFilterItem { CardSets = new List<TAG_CARD_SET> { TAG_CARD_SET.TEST_EXPANSION } };
            ExpansionFilterState.Toggle(onlyWild); PackageBrowser.QueryChanged();
            Check(Names().Contains(mixed.Name), "Multiple selected expansions permit a package combining their cards when every member matches");
            FilterModes.Set(FilterTrayKind.Expansions, FilterMode.Not);
            ExpansionFilterState.Clear(); ExpansionFilterState.Toggle(onlyWild); PackageBrowser.QueryChanged();
            Check(Names().SetEquals(new[] { complete.Name, reprinted.Name }),
                "Package expansion NOT is checked for every component, including legal reprint alternatives");
            ExpansionFilterState.Clear(); PackageBrowser.QueryChanged();
            manager.Editing = true; manager.EditedDeck = new CollectionDeck { FormatType = FormatType.FT_STANDARD };
            Rebuild();
            Check(!Names().Contains(mixed.Name) && !Names().Contains(wildOnly.Name) && Names().Contains(reprinted.Name),
                "Wild browsing never exposes a package incompatible with the edited Standard deck");
            manager.EditedDeck.FormatType = FormatType.FT_WILD; Rebuild();
            Check(Names().Contains(mixed.Name), "Changing to a Wild deck re-evaluates the complete package");
            manager.Rules.ClassAllowed = definition => definition.GetCardId() != wild.CardId; Rebuild();
            Check(!Names().Contains(mixed.Name) && !Names().Contains(wildOnly.Name),
                "A single class-incompatible member hides the complete package");
            manager.Rules.ClassAllowed = null; manager.Editing = false; manager.EditedDeck = null; Rebuild();

            PackageBrowser.SelectGroup(PackageKind.Factions);
            Check(Names().SetEquals(new[] { wildOnly.Name }) && ExtraFilterState.HasCategory(CuratedCategory.Value),
                "Package group selection is independent of the suspended card categories");
            PackageBrowser.SelectGroup(null);
            ComboCollectionCards.OpenPackage(complete);
            Check(PackageViewState.IsActive && ComboCollectionCards.OpenedPackage?.Id == complete.Id
                && native.m_currentResultsByClass.Values.SelectMany(cards => cards).Skip(1).Select(card => card.CardId).ToHashSet()
                    .SetEquals(new[] { standard.CardId, standardTwo.CardId }),
                "Opening a package shows its complete members despite the suspended card filters");
            ComboCollectionCards.BackFromPackage();
            Check(PackageViewState.IsActive && ComboCollectionCards.OpenedPackage == null && Names().Contains(mixed.Name),
                "Back inside a package returns to its package overview without activating card filters");
            PackageBrowser.Leave();
            Check(!PackageViewState.IsActive && ExpansionFilterState.IsActive && FilterModes.Get(FilterTrayKind.Expansions) == FilterMode.Or
                && ExpansionFilterState.Capture().Select(term => (term.Value, term.Operator)).SequenceEqual(cardTerms.Select(term => (term.Value, term.Operator)))
                && KeywordFilterState.Capture().Select(term => (term.Value, term.Operator)).SequenceEqual(keywords.Select(term => (term.Value, term.Operator)))
                && ExtraFilterState.HasCategory(CuratedCategory.Value) && ExtraFilterState.HasType(TAG_CARDTYPE.SPELL),
                "Leaving Packages restores exact card expansion terms, operators, keywords, roles and card types");
            Check(KeywordFilterApplier.LastAnchor != null && KeywordFilterApplier.LastAnchor.Resolve(pages) == 2,
                "Returning to Cards refreshes with the original card-page anchor");
            Check(KeywordFilterApplier.LastTransitionFrom != null && !ReferenceEquals(KeywordFilterApplier.LastTransitionFrom, KeywordFilterApplier.LastAnchor),
                "Returning to Cards compares the departing package view independently of the saved card destination");
            ExpansionFilterState.Clear(); manager.Theme = FormatType.FT_WILD;
            PackageBrowser.Enter();
            Check(PackageViewState.Format == FormatType.FT_WILD && !ExpansionFilterState.IsActive && !PackageViewState.Group.HasValue,
                "Reopening Packages inherits the Cards Wild format and starts with every compatible package");
            var neutral = Add("BROWSER_NEUTRAL", TAG_CARD_SET.CORE); Class(neutral, TAG_CLASS.NEUTRAL);
            var secondMage = Save("A Mage", "Custom", neutral, standardTwo, standard);
            Rebuild();
            Check(Bucket(TAG_CLASS.MAGE).SequenceEqual(new[] { secondMage.Name, complete.Name })
                && Bucket(TAG_CLASS.WARRIOR).SequenceEqual(new[] { wildOnly.Name })
                && Bucket(TAG_CLASS.NEUTRAL).SequenceEqual(new[] { mixed.Name }),
                "Packages sort alphabetically inside native class groups; neutral carriers do not hide class membership and cross-class packages stay discoverable");
            var dual = Save("Dual", "Custom", standardTwo);
            manager.Editing = true; manager.EditedDeck = new CollectionDeck { Classes = new List<TAG_CLASS> { TAG_CLASS.ROGUE } };
            Check(ComboCollectionCards.ClassForPackage(dual) == TAG_CLASS.ROGUE, "A package of dual-class cards uses the edited deck's eligible class for its group and banner");
            manager.Editing = false; manager.EditedDeck = null;
            secondMage.PackageClass = "WARRIOR";
            Check(ComboCollectionCards.ClassForPackage(secondMage) == TAG_CLASS.WARRIOR, "Declared built-in package classes take precedence over member inference");

            CollectibleCard Add(string id, TAG_CARD_SET set)
            {
                var card = Card(id, TAG_PREMIUM.NORMAL, 2); card.Set = set; card.Name = id;
                manager.Cards[(id, TAG_PREMIUM.NORMAL)] = card; manager.Counts[(id, TAG_PREMIUM.NORMAL)] = 2;
                return card;
            }
            CategoryCatalog.Combo Save(string name, string group, params CollectibleCard[] members)
            {
                var deck = new CategoryDeckImport.Deck();
                foreach (var card in members) { deck.Cards.Add(card.CardId); deck.RawCounts[card.CardId] = 1; }
                CategoryCatalog.AddCombo(group, name, "", deck, packageArtwork: members[0].CardId);
                return CategoryCatalog.Combos.Single(combo => combo.Name == name);
            }
            void Class(CollectibleCard card, params TAG_CLASS[] classes)
            {
                card.Definition = new EntityDef { CardId = card.CardId };
                card.Definition.SetTag(GAME_TAG.CLASS, (int)classes[0]);
                if (classes.Length > 1) card.Definition.SetTag(GAME_TAG.MULTIPLE_CLASSES, classes.Sum(value => 1 << ((int)value - 1)));
            }
            List<string> Bucket(TAG_CLASS value) => native.m_currentResultsByClass[value].Select(ComboCollectionCards.CollectionName).ToList();
            void Rebuild()
            {
                foreach (var list in native.m_currentResultsByClass.Values) list.Clear();
                BetterCollectionManager.Patches.CollectibleCardClassFilter_UpdateResults_Patch.Postfix(native);
            }
            HashSet<string> Names() => native.m_currentResultsByClass.Values.SelectMany(cards => cards)
                .Select(ComboCollectionCards.CollectionName).ToHashSet();

            void CheckFormatPageAnchors()
            {
                var savedKey = CollectionPageAnchor.CardKey;
                CollectionPageAnchor.CardKey = ComboCollectionCards.StableId;
                try
                {
                    Verify(false, 2, new[] { new[] { complete }, new[] { reprinted } },
                        new[] { new[] { mixed }, new[] { wildOnly }, new[] { complete }, new[] { reprinted } }, 4);
                    Verify(true, 2, new[] { new[] { complete }, new[] { mixed, reprinted }, new[] { wildOnly } },
                        new[] { new[] { complete }, new[] { reprinted } }, 2);
                    Verify(true, 2, new[] { new[] { complete }, new[] { wildOnly, mixed }, new[] { reprinted } },
                        new[] { new[] { complete }, new[] { reprinted } }, 2, expansionChoice: true);
                    Verify(true, 3, new[] { new[] { complete }, new[] { reprinted }, new[] { wildOnly } },
                        new[] { new[] { complete }, new[] { reprinted } }, 2);
                    Verify(true, 1, new[] { new[] { wildOnly } },
                        Array.Empty<CategoryCatalog.Combo[]>(), 1);
                    Verify(false, 2, new[] { new[] { complete }, new[] { reprinted } },
                        new[] { new[] { mixed }, new[] { wildOnly }, new[] { complete }, new[] { reprinted } }, 4, openFolder: true);
                }
                finally
                {
                    ComboCollectionCards.CloseBrowserFolder();
                    CollectionPageAnchor.CardKey = savedKey;
                    KeywordFilterApplier.OnRefresh = Rebuild;
                    manager.Display.m_cardsCollection = pages; manager.Display.CurrentPageNum = 2;
                    PackageViewState.SetFormat(FormatType.FT_WILD); Rebuild();
                }

                void Verify(bool wasWild, int current, CategoryCatalog.Combo[][] before, CategoryCatalog.Combo[][] after,
                    int expected, bool expansionChoice = false, bool openFolder = false)
                {
                    PackageViewState.SetFormat(wasWild ? FormatType.FT_WILD : FormatType.FT_STANDARD); Rebuild();
                    manager.Display.m_cardsCollection = MakePages(before); manager.Display.CurrentPageNum = current;
                    if (openFolder)
                    {
                        KeywordFilterApplier.OnRefresh = () => {
                            Rebuild(); var contents = new FolderPages(); contents.Pages.Add(new List<CollectibleCard> { standard });
                            manager.Display.m_cardsCollection = contents; manager.Display.CurrentPageNum = 1;
                        };
                        ComboCollectionCards.OpenPackage(reprinted);
                    }
                    KeywordFilterApplier.OnRefresh = () => { Rebuild(); manager.Display.m_cardsCollection = MakePages(after); };
                    if (expansionChoice) PackageBrowser.SelectExpansion(new SetFilterItem { IsAllStandard = true });
                    else PackageBrowser.ToggleFormat();
                    Check(KeywordFilterApplier.LastRequestedPage == null && KeywordFilterApplier.LastAnchor != null
                        && KeywordFilterApplier.LastAnchor.Resolve(manager.Display.m_cardsCollection) == expected,
                        "Package format switches follow the first surviving visible folder or nearest old neighbor, including expansion choices and open folders");
                    Check(ComboCollectionCards.OpenedPackage == null && KeywordFilterApplier.LastTransitionFrom != null
                        && (!openFolder || !ReferenceEquals(KeywordFilterApplier.LastAnchor, KeywordFilterApplier.LastTransitionFrom)),
                        "Package format switches close member views and compare the departing artwork separately from the destination anchor");
                }
                FolderPages MakePages(CategoryCatalog.Combo[][] order)
                {
                    var result = new FolderPages();
                    foreach (var row in order)
                        result.Pages.Add(row.Select(package => native.m_currentResultsByClass.Values.SelectMany(cards => cards)
                            .Single(card => ComboCollectionCards.StableId(card) == "BCM_COMBO_" + package.Id)).ToList());
                    return result;
                }
            }
        }
        finally
        {
            PackageBrowser.Reset(); KeywordFilterApplier.OnRefresh = previousRefresh;
            KeywordFilterState.Clear(); ExtraFilterState.ClearAll(); ExpansionFilterState.Clear();
            FilterModes.Set(FilterTrayKind.Expansions, FilterMode.Single);
            ExtraFilterState.OtherFilterMatch = previousMatcher;
            GameUtils.CounterpartCardIds.Clear(); CollectionManager.Instance = previousManager; ComboCollectionCards.Ready = previousReady;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
