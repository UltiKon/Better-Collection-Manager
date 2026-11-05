using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BetterCollectionManager
{
    /// <summary>
    /// User-ordered premium preference for collapse. Persists to LoaderPreferences + JSON backup.
    /// </summary>
    internal static class PreferredPremiumPrefs
    {
        private const string CategoryName = "BetterCollectionManager";
        private const string EntryKey = "PremiumOrder";
        private const string CollapseEntryKey = "CollapseEnabled";
        private const string DefaultOrderCsv = "Diamond,Signature,Golden,Normal";

        private static PreferenceCategory s_category;
        private static PreferenceEntry<string> s_orderEntry;
        private static PreferenceEntry<bool> s_collapseEntry;
        private static TAG_PREMIUM[] s_order;
        private static int s_selectedIndex;
        private static bool s_collapseEnabled = true;
        private static bool s_loaded;

        public static IReadOnlyList<TAG_PREMIUM> Order
        {
            get
            {
                EnsureLoaded();
                return s_order;
            }
        }

        public static int SelectedIndex
        {
            get
            {
                EnsureLoaded();
                return s_selectedIndex;
            }
            set
            {
                EnsureLoaded();
                if (s_order == null || s_order.Length == 0)
                    return;
                s_selectedIndex = Math.Max(0, Math.Min(value, s_order.Length - 1));
            }
        }

        /// <summary>When false, UpdateResults skips preferred-quality collapse (filters still work).</summary>
        public static bool CollapseEnabled
        {
            get
            {
                EnsureLoaded();
                return s_collapseEnabled;
            }
            set
            {
                EnsureLoaded();
                if (s_collapseEnabled == value)
                    return;
                s_collapseEnabled = value;
                Persist();
            }
        }

        public static void EnsureLoaded()
        {
            if (s_loaded && s_order != null && s_order.Length > 0)
                return;

            try
            {
                s_category = LoaderPreferences.CreateCategory(CategoryName);
                s_orderEntry = s_category.CreateEntry(EntryKey, DefaultOrderCsv,
                    "Preferred premium order (comma: Diamond,Signature,Golden,Normal)");
                s_collapseEntry = s_category.CreateEntry(CollapseEntryKey, true,
                    "Preferred-quality collapse ON/OFF (default ON)");
                LoaderPreferences.Load();
                ApplyCsv(s_orderEntry.Value);
                s_collapseEnabled = s_collapseEntry != null ? s_collapseEntry.Value : true;
            }
            catch (Exception ex)
            {
                DebugLog.Exception("PreferredPremiumPrefs.EnsureLoaded failed", ex);
                ApplyCsv(DefaultOrderCsv);
                s_collapseEnabled = true;
            }

            if (LoaderPreferences.LoadJsonBackups) TryLoadJsonBackup();
            if (s_order == null || s_order.Length == 0)
                ApplyCsv(DefaultOrderCsv);

            s_selectedIndex = 0;
            s_loaded = true;
#if BEPINEX
            if (LoaderPreferences.LoadJsonBackups) Persist();
#endif
            DebugLog.Msg($"PreferredPremiumPrefs loaded: {Describe()} collapse={s_collapseEnabled}");
        }

        public static string Describe()
        {
            EnsureLoaded();
            var sb = new StringBuilder();
            for (int i = 0; i < s_order.Length; i++)
            {
                if (i > 0) sb.Append('>');
                sb.Append(PreferredPremiumHelper.PremiumName(s_order[i]));
            }
            return sb.ToString();
        }

        /// <summary>True when order differs from Diamond&gt;Signature&gt;Golden&gt;Normal.</summary>
        public static bool IsNonDefaultOrder()
        {
            EnsureLoaded();
            if (s_order == null || s_order.Length < 4)
                return false;
            return s_order[0] != TAG_PREMIUM.DIAMOND
                || s_order[1] != TAG_PREMIUM.SIGNATURE
                || s_order[2] != TAG_PREMIUM.GOLDEN
                || s_order[3] != TAG_PREMIUM.NORMAL;
        }

        public static void MoveSelected(int delta)
        {
            EnsureLoaded();
            int from = s_selectedIndex;
            int to = from + delta;
            if (to < 0 || to >= s_order.Length)
                return;
            TAG_PREMIUM tmp = s_order[to];
            s_order[to] = s_order[from];
            s_order[from] = tmp;
            s_selectedIndex = to;
            Persist();
        }

        public static void SetPriority(int rank, TAG_PREMIUM premium)
        {
            EnsureLoaded();
            if (!PreferredPremiumHelper.MoveToPriority(s_order, rank, premium)) return;
            s_selectedIndex = rank;
            Persist();
        }

        public static void ResetDefault()
        {
            ApplyCsv(DefaultOrderCsv);
            s_selectedIndex = 0;
            Persist();
        }

        public static void Persist()
        {
            EnsureLoaded();
            string csv = ToCsv(s_order);
            try
            {
                if (s_orderEntry != null)
                    s_orderEntry.Value = csv;
                if (s_collapseEntry != null)
                    s_collapseEntry.Value = s_collapseEnabled;
                LoaderPreferences.Save();
            }
            catch (Exception ex)
            {
                DebugLog.Exception("PreferredPremiumPrefs LoaderPreferences save failed", ex);
            }
            TrySaveJsonBackup(csv);
            DebugLog.Msg($"PreferredPremiumPrefs saved: {Describe()} collapse={s_collapseEnabled}");
        }

        private static void ApplyCsv(string csv)
        {
            var list = new List<TAG_PREMIUM>(4);
            if (!string.IsNullOrEmpty(csv))
            {
                string[] parts = csv.Split(new[] { ',', ';', '|', '>' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                {
                    TAG_PREMIUM p;
                    if (TryParsePremium(parts[i].Trim(), out p) && !list.Contains(p))
                        list.Add(p);
                }
            }
            // Ensure all four are present.
            TAG_PREMIUM[] required =
            {
                TAG_PREMIUM.DIAMOND, TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.NORMAL
            };
            for (int i = 0; i < required.Length; i++)
            {
                if (!list.Contains(required[i]))
                    list.Add(required[i]);
            }
            s_order = list.ToArray();
        }

        private static bool TryParsePremium(string name, out TAG_PREMIUM premium)
        {
            premium = TAG_PREMIUM.NORMAL;
            if (string.IsNullOrEmpty(name))
                return false;
            if (Enum.TryParse(name, true, out premium))
                return premium == TAG_PREMIUM.NORMAL || premium == TAG_PREMIUM.GOLDEN
                    || premium == TAG_PREMIUM.SIGNATURE || premium == TAG_PREMIUM.DIAMOND;
            string lower = name.ToLowerInvariant();
            if (lower.StartsWith("sig")) { premium = TAG_PREMIUM.SIGNATURE; return true; }
            if (lower.StartsWith("dia")) { premium = TAG_PREMIUM.DIAMOND; return true; }
            if (lower.StartsWith("gol") || lower.StartsWith("gold")) { premium = TAG_PREMIUM.GOLDEN; return true; }
            if (lower.StartsWith("nor")) { premium = TAG_PREMIUM.NORMAL; return true; }
            return false;
        }

        private static string ToCsv(TAG_PREMIUM[] order)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < order.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(order[i].ToString());
            }
            return sb.ToString();
        }

        private static string JsonPath()
        {
#if BEPINEX
            string directory = Path.Combine(BepInEx.Paths.ConfigPath, "BetterCollectionManager");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "premium_order.json");
#else
            try
            {
                string modsRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "My Games", "Hearthstone");
                // Prefer game Mods folder next to the loaded DLL when possible.
                string asm = typeof(PreferredPremiumPrefs).Assembly.Location;
                if (!string.IsNullOrEmpty(asm))
                {
                    string asmDir = Path.GetDirectoryName(asm);
                    if (!string.IsNullOrEmpty(asmDir))
                        modsRoot = asmDir;
                }
                string dir = Path.Combine(modsRoot, "BetterCollectionManager");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                return Path.Combine(dir, "premium_order.json");
            }
            catch
            {
                return Path.Combine("Mods", "BetterCollectionManager", "premium_order.json");
            }
#endif
        }

        private static void TrySaveJsonBackup(string csv)
        {
            try
            {
                string path = JsonPath();
                File.WriteAllText(
                    path,
                    "{\"order\":\"" + csv + "\",\"collapse\":" + (s_collapseEnabled ? "true" : "false") + "}",
                    Encoding.UTF8);
            }
            catch (Exception ex)
            {
                DebugLog.Exception("PreferredPremiumPrefs JSON save failed", ex);
            }
        }

        private static void TryLoadJsonBackup()
        {
            try
            {
                string path = JsonPath();
                if (!File.Exists(path))
                    return;
                string text = File.ReadAllText(path);
                int idx = text.IndexOf("\"order\"", StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                    return;
                int colon = text.IndexOf(':', idx);
                int q1 = text.IndexOf('"', colon + 1);
                int q2 = text.IndexOf('"', q1 + 1);
                if (q1 < 0 || q2 < 0)
                    return;
                ApplyCsv(text.Substring(q1 + 1, q2 - q1 - 1));
                int cIdx = text.IndexOf("\"collapse\"", StringComparison.OrdinalIgnoreCase);
                if (cIdx >= 0)
                {
                    int cColon = text.IndexOf(':', cIdx);
                    if (cColon > 0)
                    {
                        string tail = text.Substring(cColon + 1).TrimStart();
                        if (tail.StartsWith("false", StringComparison.OrdinalIgnoreCase))
                            s_collapseEnabled = false;
                        else if (tail.StartsWith("true", StringComparison.OrdinalIgnoreCase))
                            s_collapseEnabled = true;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog.Exception("PreferredPremiumPrefs JSON load failed", ex);
            }
        }
    }
}
