using System.IO;
using System.Reflection;
using BetterCollectionManager;

internal static partial class Program
{
    private static void CheckCategoryStartup()
    {
        string path = Path.Combine(Path.GetDirectoryName(CollectionQualitySettings.SettingsPath), "categories.json");
        byte[] saved = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var session = typeof(KeywordFilterUi).GetField("s_cmSessionActive", BindingFlags.NonPublic | BindingFlags.Static);
        bool active = (bool)session.GetValue(null);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            foreach (string json in new[] { "{broken", "{\"version\":1,\"entries\":[]}" })
            {
                File.WriteAllText(path, json);
                session.SetValue(null, false);
                int builds = KeywordFilterUi.SessionBuilds;
                // Execute the production entry handler that previously threw
                // before it could schedule BuildWhenReady.
                KeywordFilterUi.OnCollectionManagerEnter();
                MelonLoader.MelonCoroutines.Complete();
                Check((bool)session.GetValue(null) && KeywordFilterUi.SessionBuilds == builds + 1,
                    "Collection entry still schedules the filter controls when optional category data is invalid or empty");
                Check(File.ReadAllText(path) == json, "Menu startup preserves the user's category file even on failure");
            }
        }
        finally
        {
            session.SetValue(null, active);
            if (saved != null) File.WriteAllBytes(path, saved);
            else File.Delete(path);
        }
    }
}
