namespace BetterCollectionManager
{
    internal static class ModMetadata
    {
        internal const string Guid = "BetterCollectionManager";
        internal const string Name = "Better Collection Manager";
        internal const string Version = "0.1.0";
        internal const string AssemblyVersion = Version + ".0";
        internal const string RepositoryUrl = "https://github.com/UltiKon/Better-Collection-Manager";
        internal const string Description = "Collection filters, quality tiles, categories, combos and packages.";

#if BEPINEX
        internal static void BindFirestoneMetadata(BepInEx.Configuration.ConfigFile config)
        {
            // Refresh release metadata on upgrades instead of retaining old defaults.
            config.Bind("General", "Name", Name, "Display name").Value = Name;
            config.Bind("General", "Guid", Guid, "Must match the DLL file name").Value = Guid;
            config.Bind("General", "Version", Version, "Must match the GitHub release tag").Value = Version;
            config.Bind("General", "DownloadLink", RepositoryUrl, "Repository root").Value = RepositoryUrl;
            config.Bind("General", "Description", Description, "Mods-list tooltip").Value = Description;
        }
#endif
    }
}
