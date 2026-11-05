using System;
using System.Linq;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void CheckDropdownScroll()
    {
        var resources = Resources.Objects;
        var source = MakeSettingsScrollbarTemplate();
        var control = DropdownControl.MakeTemplate();
        int width = Screen.width, height = Screen.height;
        try
        {
            Screen.width = 800; Screen.height = 600;
            control.m_menuItemTemplate.gameObject.SetActive(false);
            var rows = Enumerable.Range(0, 18).Select(i => {
                var row = Child(control.m_menuItemContainer.transform, "NativeOption" + i).AddComponent<DropdownMenuItem>();
                row.m_text = Child(row.transform, "Caption").AddComponent<UberText>();
                row.m_text.Text = "Option " + i; row.m_text.FontSize = 24;
                row.m_text.RenderToTexture = false;
                row.gameObject.AddComponent<MeshFilter>().sharedMesh = new Mesh {
                    bounds = new Bounds(Vector3.zero, new Vector3(200, .01f, 32)) };
                row.gameObject.AddComponent<MeshRenderer>().sharedMaterial = new Material { renderQueue = 4300 };
                row.m_selected = Child(row.transform, "Selection"); row.m_selected.SetActive(i == 15);
                return row;
            }).ToArray();
            control.m_menuItemContainer.OnUpdateSlices = () => {
                for (int i = 0; i < control.m_menuItemContainer.Slices.Count; i++)
                    control.m_menuItemContainer.Slices[i].transform.localPosition = new Vector3(140, 0, 400 - i * 32);
            };
            foreach (var row in rows) control.m_menuItemContainer.AddSlice(row.gameObject);
            control.m_menuItemContainer.UpdateSlices();
            int callbacks = 0; control.setMenuShownCallback(_ => callbacks++);
            DropdownScrollbar.Attach(control);
            control.onUserPressedButton();
            var scroll = control.GetComponent<DropdownScrollbar>();
            var bar = (ComboMemberScrollbar)typeof(DropdownScrollbar).GetField("m_bar",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(scroll);
            Check(rows.Count(row => row.gameObject.activeSelf) == 8 && rows[15].gameObject.activeSelf && callbacks == 1,
                "An overflowing native dropdown shows a bounded window including the current selection and retains its callback");
            Check(bar.Root.activeInHierarchy && bar.Root.GetComponentsInChildren<MeshRenderer>(false).Length == 4,
                "Dropdown overflow uses native rail, caps and a visible grip even when the source grip renderer was culled");
            Check(bar.Track.width >= 18f && bar.Thumb.width >= 18f, "The dropdown rail has a comfortably sized draggable grip");
            Input.mousePosition = bar.Track.center; Input.mouseScrollDelta = new Vector2(0, 1);
            Check(scroll.Tick() && rows[9].gameObject.activeSelf && !rows[17].gameObject.activeSelf,
                "The mouse wheel shifts the native option window by one row");
            Input.mouseScrollDelta = Vector2.zero;
            Input.mousePosition = bar.Thumb.center; Input.LeftPressed = Input.LeftHeld = true; scroll.Tick();
            Input.LeftPressed = false; Input.mousePosition = new Vector3(bar.Track.center.x, bar.Track.yMax + 100, 0); scroll.Tick();
            Check(rows[0].gameObject.activeSelf && !rows[8].gameObject.activeSelf,
                "Dragging the native grip reaches the first option");
            Input.LeftHeld = false; Input.LeftReleased = true; scroll.Tick();
            Check(scroll.ConsumeRelease(), "Releasing a captured dropdown drag outside is consumed by that dropdown");
            Input.LeftReleased = false;
            control.onUserCancelled();
            Check(!bar.Root.activeSelf && callbacks == 2 && rows.All(row => row.gameObject.activeSelf && !row.m_text.isHidden()),
                "Closing restores native row visibility and hides the scrollbar without losing close callbacks");
            control.onUserPressedButton();
            Check(rows[15].gameObject.activeSelf && rows.Count(row => row.gameObject.activeSelf) == 8,
                "Reopening restores the selected option after prior scrolling");
            control.onUserCancelled();
            var pending = Child(control.transform, "Pending native rows"); pending.SetActive(false);
            foreach (var row in rows) row.transform.SetParent(pending.transform, false);
            control.onUserPressedButton(); // The native list has not been built yet.
            foreach (var row in rows) row.transform.SetParent(control.m_menuItemContainer.transform, false);
            control.m_menuItemContainer.ClearSlices();
            foreach (var row in rows) control.m_menuItemContainer.AddSlice(row.gameObject);
            control.m_menuItemContainer.UpdateSlices(); scroll.Tick();
            Check(bar.Root.activeInHierarchy && rows.Count(row => row.gameObject.activeSelf) == 8,
                "A dropdown whose native rows appear after its shown callback still receives a scrollbar");
            control.onUserCancelled();

        }
        finally
        {
            UnityEngine.Object.Destroy(control.gameObject); UnityEngine.Object.Destroy(source.gameObject);
            Resources.Objects = resources; Screen.width = width; Screen.height = height;
            Input.LeftPressed = Input.LeftHeld = Input.LeftReleased = false; Input.mouseScrollDelta = Vector2.zero;
        }
    }
}
