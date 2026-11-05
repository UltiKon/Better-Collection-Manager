using System;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using BetterCollectionManager.Patches;
using UnityEngine;

// Attribute doubles let the fixtures execute the actual production hook bodies.
// Native game integration remains a separate in-game check.
namespace HarmonyLib
{
    public sealed class Harmony
    {
        public void PatchAll(Type type) { }
        public readonly System.Collections.Generic.List<MethodBase> Patched = new System.Collections.Generic.List<MethodBase>();
        public readonly System.Collections.Generic.Dictionary<MethodBase, (HarmonyMethod Prefix, HarmonyMethod Finalizer)> Hooks = new System.Collections.Generic.Dictionary<MethodBase, (HarmonyMethod, HarmonyMethod)>();
        public void Patch(MethodBase method, HarmonyMethod prefix = null, HarmonyMethod finalizer = null, HarmonyMethod postfix = null)
        { Patched.Add(method); Hooks[method] = (prefix, finalizer); }
    }
    public sealed class HarmonyMethod { public int priority; public readonly MethodInfo Method; public HarmonyMethod(MethodInfo method) { Method = method; } }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type, string method) { }
        public HarmonyPatch(Type type, string method, Type[] arguments) { }
    }
    public sealed class HarmonyPrefix : Attribute { }
    public sealed class HarmonyPostfix : Attribute { }
    public sealed class HarmonyFinalizer : Attribute { }
    public sealed class HarmonyPriority : Attribute { public HarmonyPriority(int priority) { } }
    public static class Priority { public const int Low = 200, First = 800; }
    public static class AccessTools
    {
        public delegate ref TValue FieldRef<T, TValue>(T instance);
        private static class Slot<T, TValue> { public static TValue Value; }
        public static FieldRef<T, TValue> FieldRefAccess<T, TValue>(string name)
        {
            var field = Field(typeof(T), name) ?? throw new MissingFieldException(name);
            return (T instance) => { Slot<T, TValue>.Value = (TValue)field.GetValue(instance); return ref Slot<T, TValue>.Value; };
        }
        public static Type TypeByName(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).FirstOrDefault(type => type != null);
        public static MethodInfo Method(Type type, string name) => type.GetMethod(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        public static FieldInfo Field(Type type, string name) => type.GetField(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        public static MethodInfo PropertyGetter(Type type, string name) => Property(type, name)?.GetGetMethod(true);
        public static PropertyInfo Property(Type type, string name) => type.GetProperty(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }
}

internal static partial class Program
{
    private static object InvokeQualityHook(string type, string method, params object[] args) =>
        typeof(QualityDeckInputPatches).GetNestedType(type, BindingFlags.NonPublic)
            .GetMethod(method, BindingFlags.Static | BindingFlags.Public).Invoke(null, args);

    private static void CheckDragHooks()
    {
        foreach (ClickQualityMode clickMode in Enum.GetValues(typeof(ClickQualityMode)))
        foreach (DragQualityMode mode in Enum.GetValues(typeof(DragQualityMode)))
        {
            bool split = clickMode == ClickQualityMode.ClickedHalf || mode == DragQualityMode.DraggedHalf;
            var fixture = QualityCase(split ? -1 : 1, split ? 1 : 2, 2,
                split ? QualityDisplayMode.Split : QualityDisplayMode.HighestOnly);
            CollectionQualitySettings.Click = clickMode;
            CollectionQualitySettings.Drag = mode;
            fixture.Actor.Premium = mode == DragQualityMode.DraggedHalf ? TAG_PREMIUM.NORMAL : TAG_PREMIUM.GOLDEN;
            var input = CollectionInputMgr.Get();
            // Only the real native pickup event runs: there is no GrabCardVisual
            // or AddCard Harmony hook in this integration fixture.
            fixture.Visual.Shown = false;
            input.NativeGrab(fixture.Visual);
            input.OverDeck = true;
            Input.LeftReleased = true;
            bool runNative = (bool)InvokeQualityHook("HeldReleasePatch", "Prefix", input);
            Check(!runNative && input.DropCalls == 1 && input.LastDropCanceled && !input.HasHeldCard(),
                "Release must cancel the native drag before the original held-card update can insert anything");
            Check(fixture.Tray.Added.Count == 0, "The mouse release itself must not insert a quality before native cleanup");
            Time.frameCount++;
            QualityDeckInput.OnUpdate();
            if (mode == DragQualityMode.ChooseOnDrop)
            {
                Check(fixture.Tray.Added.Count == 0 && ButtonListMenu.Visible is QualityChoiceMenu,
                    "Release in chooser mode must display variants without adding a card");
                ButtonListMenu.Visible.Buttons.First(b => b.m_ButtonText.Text.StartsWith("Normal")).Click();
            }
            TAG_PREMIUM expected = mode == DragQualityMode.ChooseOnDrop || mode == DragQualityMode.DraggedHalf ? TAG_PREMIUM.NORMAL
                : clickMode == ClickQualityMode.PreferredOrder ? TAG_PREMIUM.SIGNATURE : TAG_PREMIUM.GOLDEN;
            Check(fixture.Tray.Added.SequenceEqual(new[] { expected }),
                "Release must honor the selected variant or automatic drag priority");
            QualityDeckInput.OnUpdate();
            Check(input.DropCalls == 1 && fixture.Tray.Added.Count == 1,
                "A second observer of the same release must not cancel or insert twice");
            Check(fixture.Tray.Suggestions == 1 && !UniversalInputManager.Get().DialogActive,
                "Release routing must finalize once and leave the collection interactive");
        }

        var fallback = QualityCase(1, 1, 1);
        CollectionQualitySettings.Drag = DragQualityMode.ChooseOnDrop;
        var current = CollectionInputMgr.Get();
        current.NativeGrab(fallback.Visual);
        current.OverDeck = true;
        Input.LeftReleased = true;
        QualityDeckInput.OnUpdate(); // No Harmony release or drop hook runs.
        Check(current.DropCalls == 1 && current.LastDropCanceled && fallback.Tray.Added.Count == 0,
            "The mod update must intercept release independently of Harmony pickup/drop hooks");
        Time.frameCount++;
        QualityDeckInput.OnUpdate();
        Check(ButtonListMenu.Visible is QualityChoiceMenu && fallback.Tray.Added.Count == 0,
            "The independent release observer must open the quality selector");
        Check(QualityDeckInput.CancelChoice() && fallback.Tray.Added.Count == 0 && !UniversalInputManager.Get().DialogActive,
            "Canceling the release selector must add nothing and restore native input");

        foreach (bool deckTile in new[] { false, true })
        {
            var outside = QualityCase(1, 1, 1);
            current = CollectionInputMgr.Get();
            current.NativeGrab(outside.Visual);
            current.OverDeck = deckTile;
            if (deckTile) current.Held.Slot = new object();
            Input.LeftReleased = true;
            Check((bool)InvokeQualityHook("HeldReleasePatch", "Prefix", current) && current.DropCalls == 0,
                "Drops outside the deck and dragging an existing deck slot must retain native handling");
            QualityDeckInput.ClearDrag();
        }
        var stale = QualityCase(1, 1, 1);
        current = CollectionInputMgr.Get();
        current.NativeGrab(stale.Visual);
        current.OverDeck = true;
        stale.Manager.EditedDeck = new CollectionDeck();
        Input.LeftReleased = true;
        Check(!(bool)InvokeQualityHook("HeldReleasePatch", "Prefix", current) && current.LastDropCanceled,
            "A release after changing deck must cancel the old drag rather than inserting into another deck");
        Time.frameCount++;
        QualityDeckInput.OnUpdate();
        Check(stale.Tray.Added.Count == 0 && ButtonListMenu.Visible == null,
            "A canceled stale drag must never open a selector or insert later");
        Input.LeftReleased = false;
    }
}
