using BetterCollectionManager.Patches;
using UnityEngine;

internal static partial class Program
{
    private static void CheckDropdownReadability()
    {
        var popup = new GameObject("NativeDropdownPopup");
        popup.AddComponent<BetterCollectionManager.DropdownScrollbar>();
        DropdownMenuItem Row(string name)
        {
            var row = new GameObject(name).AddComponent<DropdownMenuItem>();
            row.transform.SetParent(popup.transform, false);
            row.m_text = new GameObject(name + "Text").AddComponent<UberText>();
            row.m_text.transform.SetParent(row.transform, false);
            row.m_selected = new GameObject(name + "Selection");
            row.m_selected.transform.SetParent(row.transform, false);
            return row;
        }
        void Refresh(DropdownMenuItem row)
        {
            // Native layoutMenu and hover-out call UpdateTextColor. Execute
            // that native color assignment, then the actual production hook.
            row.UpdateTextColor();
            DropdownReadabilityPatches.Postfix(row);
        }
        bool White(DropdownMenuItem row) => row.m_text.TextColor.Equals(Color.white);

        var selected = Row("SelectedRow");
        var ordinary = Row("OrdinaryRow");
        ordinary.m_selected.SetActive(false);
        foreach (bool open in new[] { false, true, false, true })
        {
            popup.SetActive(open);
            Refresh(selected);
            Refresh(ordinary);
            Check(White(selected) == open, "Only a row with a visible selected background must use white text");
            Check(!White(ordinary), "Unselected native dropdown rows must keep their normal brown text");
        }
        selected.m_text.TextColor = ordinary.m_text.TextColor = Color.white; // Native hover.
        DropdownReadabilityPatches.Postfix(ordinary);
        Check(White(ordinary), "The global color correction must preserve native hover highlighting");
        Refresh(selected); Refresh(ordinary); // Native hover-out.
        Check(White(selected) && !White(ordinary), "Hover-out must keep the selected row white and restore ordinary row colors");

        selected.m_selected.SetActive(false);
        ordinary.m_selected.SetActive(true);
        Refresh(selected); Refresh(ordinary);
        Check(!White(selected) && White(ordinary), "Refreshing a changed selection must move the white text to the new selected row");

        var field = new GameObject("ClosedDropdownField").AddComponent<DropdownMenuItem>();
        field.m_text = new GameObject("FieldText").AddComponent<UberText>();
        Refresh(field);
        Check(!White(field), "A closed native value field without a selection background must retain its original text color");
        field.m_text = null;
        DropdownReadabilityPatches.Postfix(field);

        var native = Row("UnrelatedNativeRow");
        native.transform.SetParent(null, true);
        Refresh(native);
        Check(!White(native), "Dropdowns outside the mod and custom FPS control must retain their native colors");
    }
}
