using System;
using System.Reflection;

namespace BetterCollectionManager
{
    /// <summary>Use the native preview action for mouse clicks while browsing the collection.</summary>
    internal static class CollectionClickInspect
    {
        private const BindingFlags NativeMethods = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly MethodInfo s_rightClick = typeof(CollectionCardVisual).GetMethod("OnRightClick", NativeMethods);
        private static readonly MethodInfo s_ignoreInput = typeof(CollectionCardVisual).GetMethod("ShouldIgnoreAllInput", NativeMethods);

        // The release prefix has already chosen the pressed half of a mixed
        // tile. Its right-click prefix must not replace that with pointer hover.
        public static bool IsRoutingLeftClick { get; private set; }

        public static bool TryInspect(CollectionCardVisual visual)
        {
            bool previousRouting = IsRoutingLeftClick;
            try
            {
                CollectionManager manager = CollectionManager.Get();
                if (visual == null || manager == null || ComboCollectionCards.IsCombo(visual) || IsRoutingLeftClick
                    || s_rightClick == null || s_ignoreInput == null
                    || !visual.IsShown() || visual.m_disableCraftingMode
                    || manager.IsInEditMode() || manager.GetEditedDeck() != null
                    || UniversalInputManager.Get() == null || UniversalInputManager.Get().IsTouchMode()
                    || (CollectionInputMgr.Get()?.HasHeldCard() ?? false)
                    || (CraftingTray.Get()?.IsShown() ?? false)) return false;

                CollectibleDisplay display = manager.GetCollectibleDisplay();
                CollectionUtils.ViewMode mode = visual.GetVisualType();
                if (display == null || display.GetViewMode() != mode
                    || display.GetViewSubmode() != CollectionUtils.ViewSubmode.DEFAULT
                    || !HasNativePreview(mode)
                    // Card backs use a separate cosmetic actor; the native
                    // preview does not require a card actor/entity definition.
                    || (mode != CollectionUtils.ViewMode.CARD_BACKS && visual.GetActor()?.GetEntityDef() == null)
                    || (bool)s_ignoreInput.Invoke(visual, null)) return false;

                IsRoutingLeftClick = true;
                // Reuse native transaction guards, actor setup, preview/favorite
                // flows and the appropriate card/skin/pet details implementation.
                s_rightClick.Invoke(visual, null);
                return true;
            }
            catch (Exception ex)
            {
                DebugLog.Exception("Collection left-click inspect failed; keeping native release", ex);
                return false;
            }
            finally { IsRoutingLeftClick = previousRouting; }
        }

        private static bool HasNativePreview(CollectionUtils.ViewMode mode)
        {
            switch (mode)
            {
                case CollectionUtils.ViewMode.CARDS:
                case CollectionUtils.ViewMode.HERO_SKINS:
                case CollectionUtils.ViewMode.CARD_BACKS:
                case CollectionUtils.ViewMode.COINS:
                case CollectionUtils.ViewMode.BATTLEGROUNDS_GUIDE_SKINS:
                case CollectionUtils.ViewMode.BATTLEGROUNDS_HERO_SKINS:
                case CollectionUtils.ViewMode.PETS:
                    return true;
                default:
                    return false;
            }
        }
    }
}
