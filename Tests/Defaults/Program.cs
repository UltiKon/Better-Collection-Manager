using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using MelonLoader;

internal static class Program
{
    private static int s_checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        s_checks++;
    }

    private static void Reload(Type type) => type.GetField("s_loaded", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, false);

    private static void Main()
    {
        string directory = Path.GetDirectoryName(CollectionQualitySettings.SettingsPath);
        bool existed = Directory.Exists(directory);
        string[] paths = { CollectionQualitySettings.SettingsPath, Path.Combine(directory, "premium_order.json") };
        var originals = paths.ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllBytes(path) : null);
        try
        {
            CheckLegacyImports();
            Directory.CreateDirectory(directory);
            foreach (string path in paths) File.Delete(path);
            PreferredPremiumPrefs.EnsureLoaded();
            CollectionQualitySettings.EnsureLoaded();
            var expected = ReadRequestedDefaults();
            int entries = 0;
            foreach (var category in MelonPreferences.Categories)
                foreach (var entry in category.Value.Entries)
                {
                    string key = category.Key + "/" + entry.Key;
                    object value = entry.Value.GetType().GetField("Value").GetValue(entry.Value);
                    Check(expected.TryGetValue(key, out string wanted)
                        && string.Equals(wanted, Convert.ToString(value, CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase),
                        "The shipped default must match the user's CFG: " + key);
                    entries++;
                }
            Check(entries == expected.Count, "Every requested preference must be registered.");
            Check(PreferredPremiumPrefs.Order.SequenceEqual(new[] { TAG_PREMIUM.DIAMOND, TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.NORMAL })
                && !PreferredPremiumPrefs.IsNonDefaultOrder(), "Fresh installs must use Diamond before Signature.");
            Check(QualityOptions.EffectiveClick == ClickQualityMode.PreferredOrder && QualityOptions.EffectiveDrag == DragQualityMode.ChooseOnDrop,
                "The requested input defaults must also be the effective policies.");

            PreferredPremiumPrefs.SetPriority(0, TAG_PREMIUM.SIGNATURE);
            Reload(typeof(PreferredPremiumPrefs));
            PreferredPremiumPrefs.EnsureLoaded();
            Check(PreferredPremiumPrefs.Order[0] == TAG_PREMIUM.SIGNATURE && PreferredPremiumPrefs.IsNonDefaultOrder(),
                "A saved custom order must take precedence over the new default.");
            PreferredPremiumPrefs.ResetDefault();
            Check(PreferredPremiumPrefs.Order[0] == TAG_PREMIUM.DIAMOND && !PreferredPremiumPrefs.IsNonDefaultOrder(),
                "Reset must restore the new order.");
            PreferredPremiumPrefs.CollapseEnabled = false;
            MelonPreferences.Categories.Clear();
            Reload(typeof(PreferredPremiumPrefs));
            PreferredPremiumPrefs.EnsureLoaded();
            Check(!PreferredPremiumPrefs.CollapseEnabled, "The JSON backup must retain an existing collapse preference.");

            CollectionQualitySettings.Click = ClickQualityMode.ClickedHalf;
            CollectionQualitySettings.Drag = DragQualityMode.DraggedHalf;
            CollectionQualitySettings.DisenchantPopup = true;
            CollectionQualitySettings.OpenSearch = CollectionOpenSearchMode.Off;
            CollectionQualitySettings.FilterMenuColumns = 3;
            CollectionQualitySettings.HideEmptyFilters = false;
            MelonPreferences.Categories.Clear();
            Reload(typeof(CollectionQualitySettings));
            CollectionQualitySettings.EnsureLoaded();
            Check(CollectionQualitySettings.Click == ClickQualityMode.ClickedHalf && CollectionQualitySettings.Drag == DragQualityMode.DraggedHalf
                && CollectionQualitySettings.DisenchantPopup && CollectionQualitySettings.OpenSearch == CollectionOpenSearchMode.Off
                && CollectionQualitySettings.FilterMenuColumns == 3 && !CollectionQualitySettings.HideEmptyFilters,
                "Existing JSON preferences must retain all overrides of the new defaults.");

            File.Delete(CollectionQualitySettings.SettingsPath);
            MelonPreferences.Categories.Clear();
            var native = MelonPreferences.CreateCategory("BetterCollectionManagerBehavior");
            native.CreateEntry("ClickQuality", "ClickedHalf", "");
            native.CreateEntry("DragQuality", "SameAsClick", "");
            native.CreateEntry("DisenchantPopup", true, "");
            native.CreateEntry("CollectionOpenSearch", "Off", "");
            native.CreateEntry("FilterMenuColumns", 1, "");
            native.CreateEntry("HideEmptyFilters", false, "");
            Reload(typeof(CollectionQualitySettings));
            CollectionQualitySettings.EnsureLoaded();
            Check(CollectionQualitySettings.Click == ClickQualityMode.ClickedHalf && CollectionQualitySettings.Drag == DragQualityMode.SameAsClick
                && CollectionQualitySettings.DisenchantPopup && CollectionQualitySettings.OpenSearch == CollectionOpenSearchMode.Off
                && CollectionQualitySettings.FilterMenuColumns == 1 && !CollectionQualitySettings.HideEmptyFilters,
                "Existing loader preferences must retain all overrides of the new defaults.");
            Console.WriteLine($"Passed {s_checks} requested-default, reset and saved-preference checks.");
        }
        finally
        {
            foreach (var original in originals)
                if (original.Value != null) File.WriteAllBytes(original.Key, original.Value);
                else File.Delete(original.Key);
            if (!existed && Directory.Exists(directory)) Directory.Delete(directory);
        }
    }

    private static void CheckLegacyImports()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bcm-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string legacy = "[" + LegacySettings.OldName + "]\nPremiumOrder = SIGNATURE,DIAMOND,GOLDEN,NORMAL\n\n["
                + LegacySettings.OldName + "Behavior]\nDisenchantPopup = true\nFilterMenuColumns = 3\n";
            string oldPath = Path.Combine(temporary, LegacySettings.OldPluginGuid + ".cfg");
            File.WriteAllText(oldPath, legacy);
            string newPath = Path.Combine(temporary, "bettercollectionmanager.cfg");
            Check(LegacySettings.ImportStandaloneConfig(newPath), "An old standalone CFG must be imported.");
            Check(File.ReadAllText(newPath).Contains("[BetterCollectionManagerBehavior]\nDisenchantPopup = true")
                && File.ReadAllText(newPath).Contains("FilterMenuColumns = 3"), "Imported sections must use the new name and retain saved values.");
            File.WriteAllText(newPath, "[BetterCollectionManager]\nCollapseEnabled = false\n");
            Check(!LegacySettings.ImportStandaloneConfig(newPath) && File.ReadAllText(newPath).Contains("CollapseEnabled = false"),
                "An existing new CFG must never be replaced.");
            Check(File.ReadAllText(oldPath) == legacy, "The original standalone CFG must stay intact.");

            string shared = Path.Combine(temporary, "MelonPreferences.cfg");
            string original = "[OtherMod]\nKeep = 42\n\n" + legacy + "\n[BetterCollectionManager]\nCollapseEnabled = false\n";
            File.WriteAllText(shared, original);
            Check(LegacySettings.ImportSharedConfig(shared), "Missing renamed sections must be appended to the shared CFG.");
            string migrated = File.ReadAllText(shared);
            Check(migrated.StartsWith(original) && migrated.Contains("[BetterCollectionManagerBehavior]")
                && migrated.Contains("CollapseEnabled = false"), "Foreign, legacy and current sections must retain their original values.");
            Check(File.ReadAllText(shared + ".before-bcm.bak") == original, "The shared CFG must have an exact pre-migration backup.");
            Check(!LegacySettings.ImportSharedConfig(shared) && File.ReadAllText(shared) == migrated,
                "A second launch must not append duplicate sections or modify the shared CFG.");

            string oldDirectory = Path.Combine(temporary, LegacySettings.OldName);
            Directory.CreateDirectory(Path.Combine(oldDirectory, "auto_search"));
            File.WriteAllText(Path.Combine(oldDirectory, "quality_settings.json"), "{\"click\":\"ClickedHalf\"}");
            File.WriteAllText(Path.Combine(oldDirectory, "auto_search", "account.tsv"), "already-seen");
            LegacySettings.ImportDirectory(temporary);
            string newDirectory = Path.Combine(temporary, "BetterCollectionManager");
            Check(File.ReadAllText(Path.Combine(newDirectory, "quality_settings.json")).Contains("ClickedHalf")
                && File.ReadAllText(Path.Combine(newDirectory, "auto_search", "account.tsv")) == "already-seen",
                "Settings backups and search history must be copied under the new directory name.");
            File.WriteAllText(Path.Combine(newDirectory, "quality_settings.json"), "current");
            LegacySettings.ImportDirectory(temporary);
            Check(File.ReadAllText(Path.Combine(newDirectory, "quality_settings.json")) == "current"
                && File.Exists(Path.Combine(oldDirectory, "quality_settings.json")), "Current settings and legacy originals must survive repeated imports.");
        }
        finally { Directory.Delete(temporary, true); }
    }

    private static Dictionary<string, string> ReadRequestedDefaults()
    {
        var values = new Dictionary<string, string>();
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("RequestedDefaults.cfg");
        using var reader = new StreamReader(stream);
        string section = "", line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
            int separator = line.IndexOf('=');
            if (separator > 0) values.Add(section + "/" + line.Substring(0, separator).Trim(), line.Substring(separator + 1).Trim());
        }
        return values;
    }
}
