using UnityEngine;

namespace BetterCollectionManager
{
    /// <summary>Stores which filter tray a HUD plaque opens — survives PegUI rewires.</summary>
    internal class FilterPlaqueTag : MonoBehaviour
    {
        public FilterTrayKind Kind = FilterTrayKind.Keywords;
    }
}
