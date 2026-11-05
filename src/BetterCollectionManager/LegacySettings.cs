using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BetterCollectionManager
{
    // These identifiers are used only to import installations from before the rename.
    internal static class LegacySettings
    {
        internal const string OldName = "CollectionPreferredQuality";
        internal const string OldPluginGuid = "collectionpreferredquality";
        private const string NewName = "BetterCollectionManager";

        internal static void ImportDirectory(string parent)
        {
            string source = Path.Combine(parent, OldName);
            string destination = Path.Combine(parent, NewName);
            if (!Directory.Exists(source) || Directory.Exists(destination)) return;
            CopyDirectory(source, destination);
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string path in Directory.GetFiles(source))
                File.Copy(path, Path.Combine(destination, Path.GetFileName(path)), false);
            foreach (string path in Directory.GetDirectories(source))
                CopyDirectory(path, Path.Combine(destination, Path.GetFileName(path)));
        }

        internal static bool ImportStandaloneConfig(string destination)
        {
            if (File.Exists(destination)) return false;
            string parent = Path.GetDirectoryName(destination);
            // The first BCM release used a lowercase GUID and CFG filename.
            // Prefer that newer configuration over the pre-rename settings.
            string source = Path.Combine(parent, "bettercollectionmanager.cfg");
            if (!File.Exists(source)) source = Path.Combine(parent, OldPluginGuid + ".cfg");
            if (!File.Exists(source)) return false;
            string contents = File.ReadAllText(source);
            foreach (string suffix in new[] { "", "Behavior", "Hub" })
                contents = contents.Replace("[" + OldName + suffix + "]", "[" + NewName + suffix + "]");
            contents = contents.Replace("plugin Collection Preferred Quality ", "plugin Better Collection Manager ");
            File.WriteAllText(destination, contents);
            return true;
        }

        // MelonLoader shares one CFG with other mods. Append only missing renamed
        // sections, retaining all old sections and a backup of the original file.
        internal static bool ImportSharedConfig(string path)
        {
            if (!File.Exists(path)) return false;
            string original = File.ReadAllText(path);
            var blocks = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
            StringBuilder current = null;
            using (var reader = new StringReader(original))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        string section = trimmed.Substring(1, trimmed.Length - 2).Trim('"');
                        blocks[section] = current = new StringBuilder();
                    }
                    else current?.AppendLine(line);
                }
            }
            var appended = new StringBuilder();
            foreach (string suffix in new[] { "", "Behavior", "Hub" })
                if (!blocks.ContainsKey(NewName + suffix) && blocks.TryGetValue(OldName + suffix, out var block))
                    appended.AppendLine().Append('[').Append(NewName).Append(suffix).AppendLine("]").Append(block);
            if (appended.Length == 0) return false;
            string backup = path + ".before-bcm.bak";
            if (!File.Exists(backup)) File.Copy(path, backup, false);
            File.WriteAllText(path, original + Environment.NewLine + appended);
            return true;
        }
    }
}
