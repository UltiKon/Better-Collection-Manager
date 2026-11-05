using System;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Hearthstone.DataModels;

namespace BetterCollectionManager
{
    // Copy the live model: it can be mutated while an asynchronous load is in flight.
    internal sealed class DarkmoonLogoSelection
    {
        internal readonly int OfferId, PetVariantId;
        internal readonly LuckyDrawFeaturedType Kind;
        internal readonly string HeroId, CustomId, AssetPath, Identity;

        internal DarkmoonLogoSelection(int offerId, LuckyDrawButtonDataModel model, string assetPath = null)
        {
            OfferId = offerId; Kind = model.ButtonItemType;
            PetVariantId = model.ButtonPetVariantID;
            HeroId = model.ButtonHeroSkinID ?? "";
            CustomId = model.ButtonCustomPrefabId ?? "";
            AssetPath = assetPath ?? "";
            Identity = Join(offerId.ToString(CultureInfo.InvariantCulture), ((int)Kind).ToString(CultureInfo.InvariantCulture),
                PetVariantId.ToString(CultureInfo.InvariantCulture), HeroId, CustomId, AssetPath);
        }

        internal static string CacheKey(string build, string identity, string assetPath)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Join("static-logo-v2", build, identity, assetPath))))
                    .Replace("-", "").ToLowerInvariant();
        }

        private static string Join(params string[] values)
        {
            var text = new StringBuilder();
            foreach (string value in values)
            { string part = value ?? ""; text.Append(part.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(part); }
            return text.ToString();
        }
    }

    // Used by both the asset resolver and texture loader to reject stale completions.
    internal sealed class DarkmoonLogoRequestState
    {
        internal string Identity { get; private set; }
        internal int Revision { get; private set; }
        internal bool Select(string identity)
        {
            if (Identity == identity) return false;
            Identity = identity;
            Revision++;
            return true;
        }
        internal void Invalidate() { Identity = null; Revision++; }
        internal bool IsCurrent(int revision) => Identity != null && Revision == revision;
    }

    internal static class DarkmoonLogoSource
    {
        private static readonly MethodInfo s_devicePath = typeof(AssetReferenceLibrary).GetMethod("GetDeviceAssetPath",
            BindingFlags.Instance | BindingFlags.NonPublic);
        internal static string BuildKey => typeof(LuckyDrawManager).Assembly.ManifestModule.ModuleVersionId.ToString("N");

        internal static DarkmoonLogoSelection ReadSelection(int offerId, LuckyDrawButtonDataModel model)
        {
            string path = null;
            // These lookups do not load the entry prefab. Observe redirected
            // library/DBF references even when the logical offer ID stays equal.
            try
            {
                if (model.ButtonItemType == LuckyDrawFeaturedType.Custom) path = CustomPath(model.ButtonCustomPrefabId);
                if (model.ButtonItemType == LuckyDrawFeaturedType.Pet) path = GameDbf.PetVariant.GetRecord(model.ButtonPetVariantID)?.PetIcon;
            }
            catch { /* Resolve reports the failure once, with retry/backoff. */ }
            return new DarkmoonLogoSelection(offerId, model, path);
        }

        private static string CustomPath(string id)
        {
            var library = DarkmoonLogoLibrary.Current;
            if (library == null || s_devicePath == null
                || !library.TryGetAsset(LuckyDrawAssetCategory.ENTRY_BUTTON, id, out LibraryAssetBase asset))
                throw new InvalidOperationException("Current DFT entry asset is not present in the native library: " + id);
            return s_devicePath.Invoke(library, new object[] { asset }) as string;
        }

        internal static void Resolve(DarkmoonLogoSelection selection, Action<string> ready, Action<Exception> failed)
        {
            try
            {
                switch (selection.Kind)
                {
                    case LuckyDrawFeaturedType.Custom:
                        DarkmoonLogoLibrary.EnsureLoaded(() =>
                        {
                            try { Finish(CustomPath(selection.CustomId), ready); }
                            catch (Exception ex) { failed(ex); }
                        }, failed);
                        break;
                    case LuckyDrawFeaturedType.Pet:
                        Finish(selection.AssetPath, ready);
                        break;
                    case LuckyDrawFeaturedType.HeroSkin:
                        DefLoader.Get().LoadCardDefAsync(selection.HeroId, (id, definition, data) =>
                        {
                            using (definition)
                                try
                                {
                                    string path = definition?.CardDef?.GetPortraitTexturePath(TAG_PREMIUM.NORMAL, false, out _);
                                    Finish(path, ready);
                                }
                                catch (Exception ex) { failed(ex); }
                        });
                        break;
                    default:
                        throw new InvalidOperationException("Current DFT has no supported featured artwork: " + selection.Kind);
                }
            }
            catch (Exception ex) { failed(ex); }
        }

        private static void Finish(string path, Action<string> ready)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Current DFT artwork has no native asset address");
            ready(path);
        }
    }
}
