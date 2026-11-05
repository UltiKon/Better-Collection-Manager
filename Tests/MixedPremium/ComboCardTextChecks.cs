using System;
using System.Collections.Generic;
using BetterCollectionManager;

public sealed class KeywordTextDbfRecord { public int Tag; }
public sealed class KeywordTable
{
    public readonly List<KeywordTextDbfRecord> Records = new List<KeywordTextDbfRecord>();
    public List<KeywordTextDbfRecord> GetRecords() => Records;
}
public static class GameDbf { public static KeywordTable KeywordText = new KeywordTable(); }

internal static partial class Program
{
    private static void CheckComboCardText()
    {
        KeywordTable savedTable = GameDbf.KeywordText;
        var savedNames = new Dictionary<GAME_TAG, string>(GameStrings.Names);
        try
        {
            GameDbf.KeywordText = new KeywordTable(); GameStrings.Names.Clear();
            void Keyword(int tag, string name)
            {
                GameDbf.KeywordText.Records.Add(new KeywordTextDbfRecord { Tag = tag });
                GameStrings.Names[(GAME_TAG)tag] = name;
            }
            Check(ComboCardText.Format(null) == null && ComboCardText.Format("") == ""
                && ComboCardText.Format("Taunt") == "Taunt", "Empty/unavailable keyword data leaves card text intact");
            Keyword(190, "Taunt"); Keyword(218, "Battlecry"); Keyword(9990, "Spell Damage");
            Keyword(9991, "Damage"); Keyword(9992, "Windfury"); Keyword(9993, "Mega-Windfury");
            Keyword(9994, "GLOBAL_KEYWORD_UNKNOWN"); Keyword(9995, "UNKNOWN");
            GameDbf.KeywordText.Records.Add(null);
            GameDbf.KeywordText.Records.Add(new KeywordTextDbfRecord());
            Keyword(9996, "Taunt");
            Check(ComboCardText.Format("Battlecry: Gain Taunt.\nTAUNT, taunt!")
                == "<b>Battlecry</b>: Gain <b>Taunt</b>.\n<b>TAUNT</b>, <b>taunt</b>!",
                "All keyword occurrences are bolded with original case, punctuation and newlines");
            Check(ComboCardText.Format("Taunts Taunt2 _Taunt äTaunt Taunté Taunt\u0301")
                == "Taunts Taunt2 _Taunt äTaunt Taunté Taunt\u0301", "Keywords do not match inside Unicode words or identifiers");
            Check(ComboCardText.Format("Spell Damage +2; Mega-Windfury; Windfury")
                == "<b>Spell Damage</b> +2; <b>Mega-Windfury</b>; <b>Windfury</b>",
                "Longer and multiword keywords take precedence over shorter contained names");
            const string rich = "<material=1></material><color=Taunt>Taunt</color> <b>Taunt and Battlecry</b> <B>Taunt</B>";
            string formatted = ComboCardText.Format(rich);
            Check(formatted == "<material=1></material><color=Taunt><b>Taunt</b></color> <b>Taunt and Battlecry</b> <B>Taunt</B>"
                && ComboCardText.Format(formatted) == formatted, "Formatting preserves markup, attributes and existing bold spans and is idempotent");
            Check(ComboCardText.Format("GLOBAL_KEYWORD_UNKNOWN UNKNOWN ordinary text") == "GLOBAL_KEYWORD_UNKNOWN UNKNOWN ordinary text",
                "Missing localization keys and ordinary prose remain untouched");
            GameStrings.Names[(GAME_TAG)190] = "Spott"; GameStrings.Names[(GAME_TAG)218] = "Kampfschrei";
            GameStrings.Names[(GAME_TAG)9996] = "Spott";
            Check(ComboCardText.Format("Kampfschrei: Spott. Taunt") == "<b>Kampfschrei</b>: <b>Spott</b>. Taunt",
                "A localization change rebuilds matching and discards cached formatted text");
            GameDbf.KeywordText = new KeywordTable(); Keyword(190, "Taunt");
            Check(ComboCardText.Format("Taunt") == "<b>Taunt</b>", "Replacing the native keyword table refreshes the catalog");
            Keyword(218, "Battlecry");
            Check(ComboCardText.Format("Battlecry") == "<b>Battlecry</b>", "Newly loaded keyword records are recognized");
            GameDbf.KeywordText = null;
            Check(ComboCardText.Format("Taunt") == "Taunt", "Unloaded metadata safely returns the original text");
        }
        finally
        {
            GameDbf.KeywordText = savedTable; GameStrings.Names.Clear();
            foreach (var name in savedNames) GameStrings.Names.Add(name.Key, name.Value);
        }
    }
}
