using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using BepInEx.Configuration;
using BetterCollectionManager;

internal static class Program
{
    private static int s_checks;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        s_checks++;
    }

    private static void Main(string[] args)
    {
        string directory = Path.Combine(Path.GetTempPath(), "bcm-bepinex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "preferences.cfg");
            LoaderPreferences.Initialize(new ConfigFile(path, false));
            Check(LoaderPreferences.LoadJsonBackups, "A missing CFG allows the initial JSON import.");
            Check(!LoaderPreferences.Config.SaveOnConfigSet, "The adapter must preserve batched saves.");
            var category = LoaderPreferences.CreateCategory("BetterCollectionManager");
            var order = category.CreateEntry("PremiumOrder", "Signature,Diamond,Golden,Normal", "Premium order");
            var enabled = category.CreateEntry("CollapseEnabled", true, "Collapse");
            var fps = LoaderPreferences.CreateCategory("BetterCollectionManagerBehavior")
                .CreateEntry("CustomFrameRate", 144, "FPS");
            Check(!File.Exists(path), "Binding defaults must not write a partial CFG.");
            order.Value = "Golden,Normal,Signature,Diamond";
            enabled.Value = false;
            fps.Value = 240;
            Check(!File.Exists(path), "Changing several settings must defer disk writes until Save.");
            LoaderPreferences.Save();
            Check(File.Exists(path), "Saving must create the CFG.");

            // Bind in a different order after restart. Unbound sections must survive.
            LoaderPreferences.Initialize(new ConfigFile(path, false));
            Check(!LoaderPreferences.LoadJsonBackups, "An existing CFG must take precedence over old JSON backups.");
            fps = LoaderPreferences.CreateCategory("BetterCollectionManagerBehavior")
                .CreateEntry("CustomFrameRate", 144, "FPS");
            Check(fps.Value == 240, "An integer setting must survive a restart.");
            LoaderPreferences.Load();
            category = LoaderPreferences.CreateCategory("BetterCollectionManager");
            enabled = category.CreateEntry("CollapseEnabled", true, "Collapse");
            order = category.CreateEntry("PremiumOrder", "default", "Premium order");
            Check(!enabled.Value, "A boolean setting must survive a restart.");
            Check(order.Value == "Golden,Normal,Signature,Diamond", "A comma-separated preference must survive a restart.");
            var duplicate = category.CreateEntry("CollapseEnabled", true, "Collapse");
            duplicate.Value = true;
            Check(enabled.Value, "Repeated bindings must use the same live setting.");
            LoaderPreferences.Save();
            Check(new ConfigFile(path, false).Bind("BetterCollectionManager", "CollapseEnabled", false).Value,
                "Updated values must be persisted by the real BepInEx backend.");

            if (args.Length != 2) throw new ArgumentException("Pass the BepInEx DLL and MelonLoader DLL paths.");
            VerifyAssembly(args[0], true);
            VerifyAssembly(args[1], false);
            Console.WriteLine($"Passed {s_checks} real BepInEx configuration and assembly checks.");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void VerifyAssembly(string path, bool bepinex)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        Check(metadata.GetAssemblyDefinition().Version == new Version(0, 1, 0, 0), "The assembly must advertise release 0.1.0.");
        var references = metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).ToArray();
        Check(references.Contains(bepinex ? "BepInEx" : "MelonLoader"), "The selected loader must be referenced.");
        Check(!references.Contains(bepinex ? "MelonLoader" : "BepInEx"), "The other loader must not be required.");
        var types = metadata.TypeDefinitions.Select(handle => metadata.GetString(metadata.GetTypeDefinition(handle).Name)).ToArray();
        Check(types.Contains(bepinex ? "BetterCollectionManagerPlugin" : "BetterCollectionManagerMod"),
            "The selected loader entry must be present.");
        Check(!types.Contains(bepinex ? "BetterCollectionManagerMod" : "BetterCollectionManagerPlugin"),
            "The other loader entry must not be present.");
        var entryType = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition)
            .Single(type => metadata.GetString(type.Name) == (bepinex ? "BetterCollectionManagerPlugin" : "BetterCollectionManagerMod"));
        Check(metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)entryType.BaseType).Name)
            == (bepinex ? "BaseUnityPlugin" : "MelonMod"), "The entry must inherit the actual loader base class.");
        if (bepinex)
        {
            var attributes = entryType.GetCustomAttributes().Select(metadata.GetCustomAttribute).ToArray();
            var plugin = attributes.Single(attribute =>
                metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)
                    metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent).Name) == "BepInPlugin");
            var value = metadata.GetBlobReader(plugin.Value);
            Check(value.ReadUInt16() == 1 && value.ReadSerializedString() == "bettercollectionmanager"
                && value.ReadSerializedString() == "Better Collection Manager" && value.ReadSerializedString() == "0.1.0",
                "BepInEx must receive the stable plugin GUID, display name and version.");
            var callbacks = entryType.GetMethods().Select(handle => metadata.GetString(metadata.GetMethodDefinition(handle).Name)).ToArray();
            Check(new[] { "Awake", "Update", "LateUpdate", "OnApplicationQuit", "OnDestroy" }.All(callbacks.Contains),
                "Unity must be able to discover initialization, frame and shutdown callbacks.");
        }
        Check(types.Contains("BetterCollectionManagerRuntime"), "Both loaders must contain the shared runtime.");
        var resources = metadata.ManifestResources.Select(handle => metadata.GetString(metadata.GetManifestResource(handle).Name)).ToArray();
        Check(resources.Contains("BetterCollectionManager.Categories.json") && resources.Contains("BetterCollectionManager.Packages.json"),
            "Both catalogs must remain embedded in each build.");
        Check(pe.ReadDebugDirectory().All(entry => entry.Type != DebugDirectoryEntryType.CodeView
            && entry.Type != DebugDirectoryEntryType.EmbeddedPortablePdb), "Release builds must not expose debug paths.");
    }
}
