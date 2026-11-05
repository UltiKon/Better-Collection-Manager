using System.Linq;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void CheckComboCategories(string path)
    {
        string savedId = null;
        try
        {
            ExtraFilterState.ClearAll();
            ComboPopup.Add();
            var popup = (ComboPopup)ButtonListMenu.Visible;
            var category = popup.GetComponentsInChildren<DropdownControl>(false).Single(control => control.gameObject.name == "BCM_ComboCategory");
            Check(category.Items.Contains("Value") && category.Items.Contains("Draw") && !category.Items.Contains("Uncategorized") && !category.Items.Contains("Packages") && category.Items.Contains("Combos"),
                "Combo destinations include populated role categories and Combos while excluding system card groups");
            Check(!category.Items.Contains("Unique Effects"), "Empty role categories cannot reappear in the destination selector");
            category.SelectOption("Value");
            popup.Buttons.Single(button => button.gameObject.name == "BCM_ComboName").Click();
            UniversalInputManager.Get().CompleteNativeText("Category destination regression");
            popup.Buttons.Single(button => button.gameObject.name == "BCM_SaveCombo").Click();
            var saved = CategoryCatalog.Combos.Single(combo => combo.Name == "Category destination regression"); savedId = saved.Id;
            Check(saved.Category == CuratedCategory.Value && !popup.IsShown(),
                "New combo persistence follows the chosen category rather than the source deck name");
            ExtraFilterState.ToggleCategory(CuratedCategory.Combos);
            Check(ExtraFilterState.MatchesEntry(saved), "A newly saved role combo is always included in Combos");
            ExtraFilterState.ClearAll();
            CategoryCatalog.LoadUserFile(path); saved = CategoryCatalog.Combos.Single(combo => combo.Id == savedId);
            ComboPopup.Inspect(saved);
            popup.Buttons.Single(button => button.gameObject.name == "BCM_EditCombo").Click();
            category = popup.GetComponentsInChildren<DropdownControl>(false).Single(control => control.gameObject.name == "BCM_ComboCategory");
            Check((string)category.getSelection() == "Value", "Reopening restores the saved destination in the category dropdown");
            category.SelectOption("Draw"); popup.Buttons.Single(button => button.gameObject.name == "BCM_SaveCombo").Click();
            CategoryCatalog.LoadUserFile(path);
            var moved = CategoryCatalog.Combos.Single(combo => combo.Id == savedId);
            Check(moved.Category == CuratedCategory.Draw && moved.Cards.Select(card => (card.Id,card.Count)).SequenceEqual(saved.Cards.Select(card => (card.Id,card.Count)))
                && moved.ExactVersions == saved.ExactVersions,
                "Moving a saved combo to another category survives reload with its identity, card editions and quantities intact");
        }
        finally
        {
            ComboPopup.Close();
            if (savedId != null) CategoryCatalog.DeleteCombo(savedId);
            ExtraFilterState.ClearAll();
        }
    }
}
