using System;
using System.IO;
using System.Linq;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void CheckCategoryDeckMenu(KeywordSetTray.GridFixture fixture)
    {
        string directory = Path.Combine(Path.GetTempPath(), "bcm-category-menu-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "categories.json");
        var manager = CollectionManager.Get();
        var previousDeck = manager.EditedDeck;
        bool previousEditing = manager.Editing;
        var previousContext = CollectionDeckTray.Get()?.Context;
        var sourceCards = new[] { "dbf-1", "dbf-2", "dbf-3", "dbf-4" };
        var savedCards = manager.Cards.ToArray();
        var previousKind = KeywordSetTray.ActiveKind;
        void ClickCategoryAction(string name)
        {
            var listAction = fixture.Items.FirstOrDefault(item => item.gameObject.name == name);
            if (listAction != null)
            {
                fixture.ClearGesture(); Time.frameCount++;
                listAction.GetComponent<PegUIElement>().Release();
            }
            else ClickFilterTool(fixture, name);
        }
        void ConfirmCategoryName(string name)
        {
            var popup = (ComboPopup)ButtonListMenu.Visible;
            popup.Buttons.Single(button => button.gameObject.name == "BCM_ComboName").Click();
            UniversalInputManager.Get().CompleteNativeText(name);
            popup.Buttons.Single(button => button.gameObject.name == "BCM_SaveCombo").Click();
            Check(!popup.IsShown(), "Confirming a category name completes the pending membership action and closes its popup");
            CollectionFilterMenu.ShowFilter(FilterTrayKind.Categories);
            MelonLoader.MelonCoroutines.Complete(); fixture.ClearGesture();
        }
        try
        {
            CategoryCatalog.LoadUserFile(path);
            CollectionFilterMenu.ShowFilter(FilterTrayKind.Categories);
            MelonLoader.MelonCoroutines.Complete();
            fixture.ClearGesture();
            var edited = new CollectionDeck { Name = "Unused deck name" };
            edited.Add("dbf-1", TAG_PREMIUM.NORMAL);
            edited.Add("dbf-1", TAG_PREMIUM.GOLDEN);
            edited.Add("dbf-2", TAG_PREMIUM.SIGNATURE);
            var sideboard = new CollectionDeck { Name = "Sideboard must not name the category" };
            sideboard.Add("dbf-3", TAG_PREMIUM.DIAMOND);
            edited.Sideboards.Add(sideboard);
            manager.EditedDeck = edited; manager.Editing = true;
            if (CollectionDeckTray.Get() != null) CollectionDeckTray.Get().Context = sideboard;
            foreach (string id in sourceCards) manager.Cards[(id, TAG_PREMIUM.NORMAL)] = new CollectibleCard { CardId = id };
            int refreshes = KeywordFilterApplier.Refreshes;
            ClickCategoryAction("BCM_ImportCategoryDeck");
            var prompt = (ComboPopup)ButtonListMenu.Visible;
            Check(prompt.IsShown() && prompt.Buttons.Count == 3 && KeywordFilterApplier.Refreshes == refreshes
                && !CategoryCatalog.Categories.Any(c => c.Name == edited.Name)
                && (!Directory.Exists(CategoryCatalog.DeckDirectory(path)) || Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length == 0),
                "Without a selected category the button asks for a name without using the deck name or writing membership");
            prompt.Buttons.Single(button => button.gameObject.name == "BCM_CloseCombo").Click();
            Check(!prompt.IsShown() && KeywordFilterApplier.Refreshes == refreshes,
                "Canceling category naming leaves membership and the collection unchanged");
            CollectionFilterMenu.ShowFilter(FilterTrayKind.Categories);
            MelonLoader.MelonCoroutines.Complete(); fixture.ClearGesture();
            ClickCategoryAction("BCM_ImportCategoryDeck");
            prompt.Buttons.Single(button => button.gameObject.name == "BCM_SaveCombo").Click();
            Check(prompt.IsShown() && !CategoryCatalog.Categories.Any(c => c.Name == edited.Name),
                "An empty category name keeps the prompt open without falling back to the deck name");
            ConfirmCategoryName("My Counters");
            var category = CategoryCatalog.Categories.Single(c => c.Name == "My Counters");
            Check(fixture.Items.Any(item => item.Text == "My Counters")
                && fixture.Items.Concat(fixture.PinnedItems).Single(item => item.gameObject.name == "BCM_ImportCategoryDeck").Text.Contains("+3")
                && KeywordFilterApplier.Refreshes == refreshes + 1
                && CategoryCatalog.Matches(new CollectibleCard { CardId = "dbf-3" }, category.Value)
                && !CategoryCatalog.Matches(new CollectibleCard { CardId = "dbf-4" }, category.Value)
                && edited.Slots.Count == 3 && sideboard.Slots.Count == 1,
                "The current deck button reads an incomplete root deck and sideboard, ignores premium duplicates and leaves the deck unchanged");
            Check(Directory.GetFiles(CategoryCatalog.DeckDirectory(path), "*.cards").Length == 1
                && CategoryCatalog.Categories.All(c => c.Name != sideboard.Name),
                "Live imports persist a card-ID snapshot under the entered category name without requiring an export or clipboard");
            fixture.Items.Single(item => item.Text == "My Counters").GetComponent<PegUIElement>().Release();
            Check(ExtraFilterState.HasCategory(category.Value), "An imported category row uses the normal filter selection callback");
            edited.Add("dbf-4", TAG_PREMIUM.NORMAL);
            edited.Name = "Selected category takes priority";
            ClickCategoryAction("BCM_ImportCategoryDeck");
            Check(CategoryCatalog.Matches(new CollectibleCard { CardId = "dbf-4" }, category.Value)
                && ExtraFilterState.HasCategory(category.Value)
                && fixture.Items.Concat(fixture.PinnedItems).Single(item => item.gameObject.name == "BCM_ImportCategoryDeck").Text.Contains("+1"),
                "A second click reads the latest unsaved deck contents and merges additions without losing the selection");
            ClickCategoryAction("BCM_ImportCategoryDeck");
            Check(Directory.GetFiles(CategoryCatalog.DeckDirectory(path), "*.cards").Length == 2,
                "Repeated imports of the live deck do not duplicate saved snapshots");
            edited.Name = "A different deck name";
            ClickCategoryAction("BCM_RemoveCategoryDeck");
            Check(sourceCards.All(id => !CategoryCatalog.Matches(new CollectibleCard { CardId = id }, category.Value))
                && !ExtraFilterState.HasCategory(category.Value) && CategoryCatalog.Categories.Length == 10
                && fixture.Items.Concat(fixture.PinnedItems).Any(item => item.gameObject.name == "BCM_RemoveCategoryDeck" && item.Text.Contains("-4"))
                && edited.Slots.Count == 4 && sideboard.Slots.Count == 1,
                "The explicit removal button targets the selected category, removes the overlap including sideboards, and never edits the deck");
            ClickCategoryAction("BCM_RemoveCategoryDeck");
            ConfirmCategoryName("My Counters");
            Check(Directory.GetFiles(CategoryCatalog.DeckDirectory(path), "*.remove").Length == 1,
                "Removing the same deck again is a no-op rather than toggling its cards back on");
            edited.Name = "";
            ClickCategoryAction("BCM_ImportCategoryDeck");
            ConfirmCategoryName("My Counters");
            Check(sourceCards.All(id => CategoryCatalog.Matches(new CollectibleCard { CardId = id }, category.Value)),
                "An unnamed deck can restore cards additively using the category name entered in the popup");
            ExtraFilterState.ClearCategories();
            ClickCategoryAction("BCM_RemoveCategoryDeck");
            Check(sourceCards.All(id => CategoryCatalog.Matches(new CollectibleCard { CardId = id }, category.Value)),
                "Removal without a selected category waits for the entered target before changing membership");
            ConfirmCategoryName("My Counters");
            Check(CategoryCatalog.Categories.Length == 10 && !CategoryCatalog.Matches(new CollectibleCard { CardId = "dbf-1" }, category.Value),
                "Removal uses the entered category name and deletes the emptied category");
            ClickCategoryAction("BCM_ImportCategoryDeck");
            ConfirmCategoryName("My Counters");
            int editFiles = Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length;
            FilterModes.Set(FilterTrayKind.Categories, FilterMode.Or);
            ExtraFilterState.ToggleCategory(category.Value); ExtraFilterState.ToggleCategory(CuratedCategory.AoE);
            ClickCategoryAction("BCM_RemoveCategoryDeck");
            Check(Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length == editFiles
                && sourceCards.All(id => CategoryCatalog.Matches(new CollectibleCard { CardId = id }, category.Value)),
                "Multiple selected categories never cause an ambiguous bulk deletion");
            ExtraFilterState.ToggleCategory(CuratedCategory.AoE);
            manager.Editing = false;
            refreshes = KeywordFilterApplier.Refreshes;
            ClickCategoryAction("BCM_ImportCategoryDeck");
            Check(CategoryCatalog.Categories.Length == 11 && ExtraFilterState.HasCategory(category.Value)
                && KeywordFilterApplier.Refreshes == refreshes
                && fixture.Items.Concat(fixture.PinnedItems).Any(item => item.gameObject.name == "BCM_ImportCategoryDeck" && item.Text.Contains("Import failed"))
                && fixture.Items.Any(item => item.Text == "My Counters"),
                "Outside edit mode a stale edited-deck reference cannot be imported or change membership, selection or menu availability");
            manager.Editing = true; manager.EditedDeck = new CollectionDeck { Name = "Empty" };
            ClickCategoryAction("BCM_ImportCategoryDeck");
            Check(CategoryCatalog.Categories.Length == 11 && Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length == editFiles,
                "An empty deck reports failure and creates no empty category or saved snapshot");
            ExtraFilterState.ClearCategories(); manager.EditedDeck = edited;
            ClickCategoryAction("BCM_ImportCategoryDeck");
            prompt = (ComboPopup)ButtonListMenu.Visible;
            manager.EditedDeck = new CollectionDeck { Name = "Other deck" };
            prompt.Buttons.Single(button => button.gameObject.name == "BCM_ComboName").Click();
            UniversalInputManager.Get().CompleteNativeText("Wrong deck category");
            prompt.Buttons.Single(button => button.gameObject.name == "BCM_SaveCombo").Click();
            Check(prompt.IsShown() && prompt.Header.Contains("deck changed")
                && !CategoryCatalog.Categories.Any(c => c.Name == "Wrong deck category")
                && Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length == editFiles,
                "A deck switch while naming cannot import another deck or create a category");
            ComboPopup.Close();
            Check(EditedCategoryDeckImport.ReadName(new PrivateDeckName()) == "Private"
                && EditedCategoryDeckImport.ReadName(new WrappedDeckName()) == "Wrapped"
                && EditedCategoryDeckImport.ReadName(new object()) == null,
                "Name access supports private native getters and wrapped deck data without guessing a category when unavailable");
        }
        finally
        {
            ComboPopup.Close();
            manager.EditedDeck = previousDeck; manager.Editing = previousEditing;
            if (CollectionDeckTray.Get() != null) CollectionDeckTray.Get().Context = previousContext;
            manager.Cards.Clear(); foreach (var pair in savedCards) manager.Cards.Add(pair.Key, pair.Value);
            ExtraFilterState.ClearAll();
            foreach (string file in Directory.GetFiles(CategoryCatalog.DeckDirectory(path))) File.Delete(file);
            CategoryCatalog.LoadUserFile(path);
            Directory.Delete(directory, true);
            typeof(KeywordSetTray).GetField("s_categoryImportStatus", System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static).SetValue(null, null);
            typeof(KeywordSetTray).GetField("s_categoryRemoveStatus", System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static).SetValue(null, null);
            CollectionFilterMenu.ShowFilter(previousKind);
            MelonLoader.MelonCoroutines.Complete();
        }
    }
    private sealed class PrivateDeckName { private string GetName() => "Private"; }
    private sealed class WrappedDeckName { private readonly object m_deckData = new CollectionDeck { Name = "Wrapped" }; }
}
