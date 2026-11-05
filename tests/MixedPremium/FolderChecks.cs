using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private sealed class FolderPages
    {
        internal readonly List<List<CollectibleCard>> Pages = new List<List<CollectibleCard>>();
        public int GetTotalNumPages() => Pages.Count;
        public List<CollectibleCard> GetPageContents(int page) => Pages[page - 1];
        public FolderTab GetCurrentTabInfoFromPage(int page) => new FolderTab();
    }
    private sealed class FolderTab { public TAG_CLASS tagClass = TAG_CLASS.NEUTRAL; }

    private static void CheckPackagePageTransitions(CategoryCatalog.Combo package, CollectibleCard a, CollectibleCard b)
    {
        var manager = CollectionManager.Get();
        var previousCollection = manager.Display.m_cardsCollection;
        int previousPage = manager.Display.CurrentPageNum;
        try
        {
            var pages = new FolderPages(); pages.Pages.Add(new List<CollectibleCard> { a }); pages.Pages.Add(new List<CollectibleCard> { b });
            manager.Display.m_cardsCollection = pages;
            foreach (string change in new[] { "start editing", "finish editing" })
            {
                manager.Display.CurrentPageNum = 2;
                ComboCollectionCards.BeginPackageDeckChange(out var state);
                try
                {
                    // Native JumpToCollectionClassPage may have set this to 1
                    // before requesting the automatic deck transition.
                    manager.Display.CurrentPageNum = 1;
                    var callback = new object();
                    object[] arguments = { BookPageManager.PageTransitionType.SINGLE_PAGE_LEFT, false, callback, package };
                    int refreshes = KeywordFilterApplier.Refreshes;
                    ComboCollectionCards.KeepPackagePage(manager.Display, arguments);
                    Check((BookPageManager.PageTransitionType)arguments[0] == BookPageManager.PageTransitionType.NONE
                        && (bool)arguments[1] && manager.Display.CurrentPageNum == 2
                        && ReferenceEquals(arguments[2], callback) && ReferenceEquals(arguments[3], package)
                        && KeywordFilterApplier.Refreshes == refreshes,
                        "Package " + change + " preserves its visible page, suppresses the automatic flip and retains native callbacks without another refresh");
                }
                finally { ComboCollectionCards.EndPackageDeckChange(null, state); }
            }
            object[] normal = { BookPageManager.PageTransitionType.SINGLE_PAGE_RIGHT, false };
            ComboCollectionCards.KeepPackagePage(manager.Display, normal);
            Check((BookPageManager.PageTransitionType)normal[0] == BookPageManager.PageTransitionType.SINGLE_PAGE_RIGHT && !(bool)normal[1],
                "Normal package page turns remain available outside a deck transition");

            ComboCollectionCards.BeginPackageDeckChange(out var failed);
            var error = new InvalidOperationException("Deck change failed");
            Check(ReferenceEquals(ComboCollectionCards.EndPackageDeckChange(error, failed), error),
                "The deck transition guard restores its scope and preserves native errors");
            ComboCollectionCards.KeepPackagePage(manager.Display, normal);
            Check((BookPageManager.PageTransitionType)normal[0] == BookPageManager.PageTransitionType.SINGLE_PAGE_RIGHT,
                "An unsuccessful deck change cannot suppress later manual page turns");

            foreach (Action removeFilter in new Action[] {
                ExtraFilterState.ClearPackages,
                () => { ExtraFilterState.ClearPackages(); ExtraFilterState.TogglePackage(PackageKind.Parasitic); },
                () => ExtraFilterState.ToggleCategory(CuratedCategory.Value),
                ExtraFilterState.ClearAll
            })
            {
                ExtraFilterState.ClearAll(); ExtraFilterState.TogglePackage(PackageKind.Custom);
                if (ComboCollectionCards.OpenedPackage == null) ComboCollectionCards.OpenPackage(package);
                removeFilter();
                var lists = new Dictionary<int, List<CollectibleCard>> { [1] = new List<CollectibleCard> { a, b } };
                int refreshes = KeywordFilterApplier.Refreshes;
                ComboCollectionCards.Prepare(lists)();
                Check(ComboCollectionCards.OpenedPackage == null && KeywordFilterApplier.Refreshes == refreshes
                    && lists[1].All(card => ComboCollectionCards.StableId(card) == card.CardId),
                    "Removing or replacing the package filter closes its folder in the existing result rebuild");
            }
        }
        finally
        {
            manager.Display.m_cardsCollection = previousCollection; manager.Display.CurrentPageNum = previousPage;
            ExtraFilterState.ClearAll(); ExtraFilterState.TogglePackage(PackageKind.Custom);
            ComboCollectionCards.OpenPackage(package);
        }
    }

    private static void CheckFolderEditors(CollectibleCard a, CollectibleCard b, string path)
    {
        var manager = CollectionManager.Get();
        var edited = manager.EditedDeck; bool editing = manager.Editing;
        var created = new List<string>();
        try
        {
            ExtraFilterState.ClearAll(); ComboCollectionCards.BackFromPackage();
            manager.Editing = false; manager.EditedDeck = null;
            foreach (bool package in new[] { false, true })
            {
                string name = package ? "UI package folder" : "UI category folder";
                ComboPopup.AddFolder(package);
                var popup = (ComboPopup)ButtonListMenu.Visible;
                Check(popup.Buttons.Count == 3 && popup.Buttons.All(button => button.gameObject.name == "BCM_ComboName"
                    || button.gameObject.name == "BCM_SaveCombo" || button.gameObject.name == "BCM_CloseCombo"),
                    "Creating an empty folder needs no edited deck and shows only its name, Save and Cancel");
                int files = Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length;
                popup.Buttons.Single(button => button.gameObject.name == "BCM_SaveCombo").Click();
                Check(popup.IsShown() && Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length == files,
                    "An empty folder name does not write a persistent edit");
                popup.Buttons.Single(button => button.gameObject.name == "BCM_ComboName").Click();
                UniversalInputManager.Get().CompleteNativeText(name);
                popup.Buttons.Single(button => button.gameObject.name == "BCM_SaveCombo").Click();
                CategoryCatalog.LoadUserFile(path);
                Check(!popup.IsShown() && (package ? PackageCatalog.Groups.Any(group => group.Name == name)
                    : CategoryCatalog.Categories.Any(category => category.Name == name)),
                    "Empty named category/package folders survive a catalog reload without a deck-name workaround");
            }

            manager.EditedDeck = edited; manager.Editing = editing;
            ExtraFilterState.TogglePackage(PackageKind.Parasitic);
            ComboPopup.AddPackage();
            Check((string)ButtonListMenu.Visible.GetComponentsInChildren<DropdownControl>(false)
                .Single(control => control.gameObject.name == "BCM_ComboCategory").getSelection() == "Parasitic",
                "Adding a package starts in the single selected package folder");
            ComboPopup.Close(); ExtraFilterState.ClearAll();
            var deck = new CategoryDeckImport.Deck { Name = "Custom" };
            deck.Cards.Add(a.CardId); deck.Cards.Add(b.CardId); deck.RawCounts[a.CardId] = 2; deck.RawCounts[b.CardId] = 1;
            CategoryCatalog.AddCombo("Custom", "Movable package", "", deck, packageArtwork: a.CardId);
            string id = CategoryCatalog.Combos.Single(combo => combo.Name == "Movable package").Id; created.Add(id);
            foreach (PackageKind destination in new[] { PackageKind.Parasitic, PackageKind.Factions, PackageKind.Custom })
            {
                var before = CategoryCatalog.Combos.Single(combo => combo.Id == id);
                ComboPopup.EditPackage(before);
                var popup = (ComboPopup)ButtonListMenu.Visible;
                var selector = popup.GetComponentsInChildren<DropdownControl>(false).Single(control => control.gameObject.name == "BCM_ComboCategory");
                Check(selector.IsEnabled() && selector.m_button.IsEnabled() && selector.Items.Contains("Parasitic") && selector.Items.Contains("Factions") && selector.Items.Contains("Custom"),
                    "Saved custom packages expose all peer destinations in an enabled dropdown");
                selector.SelectOption(PackageCatalog.GroupName(destination));
                popup.Buttons.Single(button => button.gameObject.name == "BCM_SaveCombo").Click();
                CategoryCatalog.LoadUserFile(path);
                var after = CategoryCatalog.Combos.Single(combo => combo.Id == id);
                Check(!popup.IsShown() && after.PackageGroup == destination && after.Artwork == before.Artwork
                    && after.ExactVersions == before.ExactVersions
                    && after.Cards.Select(card => (card.Id, card.Count)).SequenceEqual(before.Cards.Select(card => (card.Id, card.Count))),
                    "Moving a custom package to " + destination + " survives reload while preserving identity, artwork, editions and quantities");
            }
            CheckPackageMoveNavigation(id, a, b);

            foreach (bool package in new[] { true, false })
            {
                string folder = package ? "Inline package folder" : "Inline category folder";
                if (package) ComboPopup.EditPackage(CategoryCatalog.Combos.Single(combo => combo.Id == id));
                else
                {
                    CategoryCatalog.AddCombo("Value", "Folder combo", "", deck);
                    var combo = CategoryCatalog.Combos.Single(value => value.Name == "Folder combo"); created.Add(combo.Id);
                    ComboPopup.Inspect(combo); ButtonListMenu.Visible.Buttons.Single(button => button.gameObject.name == "BCM_EditCombo").Click();
                }
                var popup = (ComboPopup)ButtonListMenu.Visible;
                var selector = popup.GetComponentsInChildren<DropdownControl>(false).Single(control => control.gameObject.name == "BCM_ComboCategory");
                var add = popup.Buttons.Single(button => button.gameObject.name == "BCM_AddNewFolder");
                Check(ComboSceneGeometry.ScreenBounds(selector.gameObject, CameraUtils.Camera, out Rect selectArea)
                    && ComboSceneGeometry.ScreenBounds(add.gameObject, CameraUtils.Camera, out Rect addArea) && addArea.yMin > selectArea.yMax,
                    "New category/package folder has its own full-size action above the folder dropdown");
                int files = Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length;
                object previous = selector.getSelection(); add.Click();
                UniversalInputManager.Get().CancelNativeText();
                Check(Equals(selector.getSelection(), previous) && Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length == files,
                    "Canceling Add New preserves the selected folder and writes nothing");
                add.Click(); UniversalInputManager.Get().CompleteNativeText(folder);
                Check((string)selector.getSelection() == folder && popup.IsShown(),
                    "Add New creates and immediately selects a folder while keeping the edited entry open");
                popup.Buttons.Single(button => button.gameObject.name == "BCM_SaveCombo").Click();
                CategoryCatalog.LoadUserFile(path);
                var saved = CategoryCatalog.Combos.Single(combo => combo.Name == (package ? "Movable package" : "Folder combo"));
                Check(package ? PackageCatalog.GroupName(saved.PackageGroup) == folder
                    : CategoryCatalog.Categories.Single(category => category.Value == saved.Category).Name == folder,
                    "Saving uses the newly created package/category destination after reload");
                ComboPopup.Close();
            }
        }
        finally
        {
            ComboPopup.Close(); foreach (string id in created) CategoryCatalog.DeleteCombo(id);
            manager.EditedDeck = edited; manager.Editing = editing;
            ExtraFilterState.ClearAll();
        }
    }

    private static void CheckPackageMoveNavigation(string id, CollectibleCard a, CollectibleCard b)
    {
        var previousRefresh = KeywordFilterApplier.OnRefresh;
        var navigationField = typeof(ComboCollectionCards).GetField("s_packageNavigation", BindingFlags.Static | BindingFlags.NonPublic);
        var update = typeof(ComboCollectionCards).GetMethod("UpdatePackageNavigation", BindingFlags.Static | BindingFlags.NonPublic);
        var folder = PackageCatalog.Groups.Single(group => group.Name == "UI package folder");
        CategoryCatalog.Combo Saved() => CategoryCatalog.Combos.Single(combo => combo.Id == id);
        PackageNavigation Navigation() => (PackageNavigation)navigationField.GetValue(null);
        void Rebuild()
        {
            var lists = new Dictionary<int, List<CollectibleCard>> { [1] = new List<CollectibleCard> { a, b } };
            ComboCollectionCards.Prepare(lists)();
        }
        void Open()
        {
            ComboCollectionCards.BackFromPackage();
            ExtraFilterState.ClearAll(); ExtraFilterState.TogglePackage(folder.Kind);
            ComboCollectionCards.OpenPackage(Saved());
        }
        try
        {
            var package = Saved();
            CategoryCatalog.UpdateCombo(id, package.Name, "", categoryName: folder.Name);
            KeywordFilterApplier.OnRefresh = Rebuild;
            Open(); var navigation = Navigation();
            ComboPopup.EditPackage(Saved());
            var popup = (ComboPopup)ButtonListMenu.Visible;
            popup.GetComponentsInChildren<DropdownControl>(false).Single(control => control.gameObject.name == "BCM_ComboCategory").SelectOption("Parasitic");
            popup.Buttons.Single(button => button.gameObject.name == "BCM_SaveCombo").Click();
            Check(Saved().PackageGroup == PackageKind.Parasitic && !PackageCatalog.Packages.Any(entry => entry.PackageGroup == folder.Kind)
                && ComboCollectionCards.OpenedPackage == null && Navigation() == null && !navigation.Root.activeInHierarchy,
                "Moving the last package out of its selected folder closes the folder and disposes Back during the Save refresh");
            for (int i = 0; i < 3; i++) update.Invoke(null, null);
            Check(Navigation() == null, "Later updates cannot resurrect Back after the source folder became empty");

            // The update loop can also discover a changed catalog before a
            // native refresh has run. It must use the latest destination.
            CategoryCatalog.UpdateCombo(id, package.Name, "", categoryName: folder.Name);
            Open(); navigation = Navigation();
            CategoryCatalog.UpdateCombo(id, package.Name, "", categoryName: "Factions");
            int refreshes = KeywordFilterApplier.Refreshes;
            update.Invoke(null, null);
            Check(ComboCollectionCards.OpenedPackage == null && Navigation() == null && !navigation.Root.activeInHierarchy
                && KeywordFilterApplier.Refreshes == refreshes + 1,
                "A catalog move detected by polling closes against the saved group and removes navigation with one refresh");

            // Reproduce a synchronous refresh closing the package while its
            // navigation update is still executing, then returning to it.
            CategoryCatalog.UpdateCombo(id, package.Name, "", categoryName: folder.Name);
            Open(); navigation = Navigation();
            CategoryCatalog.UpdateCombo(id, package.Name + " updated", "");
            KeywordFilterApplier.OnRefresh = () => { ExtraFilterState.ClearPackages(); Rebuild(); };
            update.Invoke(null, null);
            Check(ComboCollectionCards.OpenedPackage == null && Navigation() == null && !navigation.Root.activeInHierarchy,
                "Navigation is not recreated when a synchronous result rebuild closes the package inside its update");
        }
        finally
        {
            ComboPopup.Close(); ComboCollectionCards.BackFromPackage();
            KeywordFilterApplier.OnRefresh = previousRefresh;
            CategoryCatalog.UpdateCombo(id, "Movable package", "", categoryName: "Custom");
            ExtraFilterState.ClearAll();
        }
    }
}
