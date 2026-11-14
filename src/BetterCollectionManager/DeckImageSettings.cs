using System;
using System.Collections.Generic;

namespace BetterCollectionManager
{
    internal enum DeckImageQuality { Deck, Normal, Golden, Highest }
    internal enum DeckImageResolution { Low, Medium, High, Max }
    internal enum DeckImageAspect { Portrait, Landscape, Square }
    internal enum DeckImageBackground { Parchment, Artwork, CardArtwork }

    [Flags]
    internal enum DeckImageOption
    {
        None = 0, Details = 1, CraftingCost = 2, ManaCurve = 8,
        CardTypes = 16, Rarities = 32, Background = 64
    }
    internal static class DeckImageSettings
    {
        internal static readonly DeckImageOption[] Options = {
            DeckImageOption.Details, DeckImageOption.CraftingCost,
            DeckImageOption.ManaCurve, DeckImageOption.CardTypes, DeckImageOption.Rarities, DeckImageOption.Background
        };
        private static readonly Dictionary<DeckImageOption, PreferenceEntry<bool>> Entries = new Dictionary<DeckImageOption, PreferenceEntry<bool>>();
        private static bool s_loaded;
        private static PreferenceEntry<string> s_quality;
        private static PreferenceEntry<bool> s_askArtwork;
        internal static bool AskArtwork
        {
            get { EnsureLoaded(); return s_askArtwork.Value; }
            set { EnsureLoaded(); if (s_askArtwork.Value == value) return; s_askArtwork.Value = value; CollectionQualitySettings.QueuePreferencesSave(); }
        }
        private static PreferenceEntry<string> s_aspect, s_background, s_resolution;
        internal static string BackgroundLabel(DeckImageBackground background) => background switch {
            DeckImageBackground.Artwork => "Class artwork (DeckView)",
            DeckImageBackground.CardArtwork => "Blurred card art",
            _ => "Native parchment"
        };
        internal static DeckImageBackground Background
        {
            get { EnsureLoaded(); return Enum.TryParse(s_background.Value, out DeckImageBackground value) && Enum.IsDefined(typeof(DeckImageBackground), value) ? value : DeckImageBackground.Parchment; }
            set
            {
                EnsureLoaded();
                if (!Enum.IsDefined(typeof(DeckImageBackground), value) || Background == value) return;
                s_background.Value = value.ToString(); CollectionQualitySettings.QueuePreferencesSave();
            }
        }
        internal static string ResolutionLabel(DeckImageResolution resolution) => resolution.ToString();
        internal static int ResolutionPixels(DeckImageResolution resolution) => resolution switch {
            DeckImageResolution.Low => 720,
            DeckImageResolution.Medium => 1080,
            DeckImageResolution.High => 1440,
            _ => 0
        };
        internal static DeckImageResolution Resolution
        {
            get { EnsureLoaded(); return Enum.TryParse(s_resolution.Value, out DeckImageResolution value) && Enum.IsDefined(typeof(DeckImageResolution), value) ? value : DeckImageResolution.Medium; }
            set
            {
                EnsureLoaded();
                if (!Enum.IsDefined(typeof(DeckImageResolution), value) || Resolution == value) return;
                s_resolution.Value = value.ToString(); CollectionQualitySettings.QueuePreferencesSave();
            }
        }
        internal static string AspectLabel(DeckImageAspect aspect) => aspect.ToString();
        internal static DeckImageAspect Aspect
        {
            get { EnsureLoaded(); return Enum.TryParse(s_aspect.Value, out DeckImageAspect value) && Enum.IsDefined(typeof(DeckImageAspect), value) ? value : DeckImageAspect.Landscape; }
            set
            {
                EnsureLoaded();
                if (!Enum.IsDefined(typeof(DeckImageAspect), value) || Aspect == value) return;
                s_aspect.Value = value.ToString(); CollectionQualitySettings.QueuePreferencesSave();
            }
        }
        internal static string QualityLabel(DeckImageQuality quality) => quality == DeckImageQuality.Deck ? "As in deck" : quality.ToString();
        internal static DeckImageQuality Quality
        {
            get { EnsureLoaded(); return Enum.TryParse(s_quality.Value, out DeckImageQuality value) && Enum.IsDefined(typeof(DeckImageQuality), value) ? value : DeckImageQuality.Normal; }
            set
            {
                EnsureLoaded();
                if (!Enum.IsDefined(typeof(DeckImageQuality), value) || Quality == value) return;
                s_quality.Value = value.ToString();
                CollectionQualitySettings.QueuePreferencesSave();
            }
        }
        internal static string Label(DeckImageOption option) => option switch {
            DeckImageOption.Details => "Deck name + class",
            DeckImageOption.CraftingCost => "Crafting cost (normal)",
            DeckImageOption.ManaCurve => "Mana curve",
            DeckImageOption.CardTypes => "Card type counts",
            DeckImageOption.Rarities => "Rarity counts",
            DeckImageOption.Background => "Remove background",
            _ => throw new ArgumentOutOfRangeException(nameof(option))
        };
        internal static void EnsureLoaded()
        {
            if (s_loaded) return;
            CollectionQualitySettings.EnsureLoaded();
            CollectionQualitySettings.FlushPreferences(true);
            var category = LoaderPreferences.CreateCategory("BetterCollectionManagerDeckImage");
            s_quality = category.CreateEntry("Quality", "Normal", "Card quality: Deck, Normal, Golden or Highest (Diamond, Signature, Golden, Normal)");
            // A new key avoids interpreting the previous High (100%) as the new 1440 preset.
            s_resolution = category.CreateEntry("ResolutionPreset", "Medium", "Image resolution: Low (720), Medium (1080), High (1440) on the short edge, or Max (original); no upscaling");
            s_aspect = category.CreateEntry("Aspect", "Landscape", "Deck image format: Portrait, Square or Landscape; automatic columns based on distinct card count");
            s_background = category.CreateEntry("BackgroundStyle", "Parchment", "Deck image background: Parchment, class Artwork or blurred CardArtwork");
            s_askArtwork = category.CreateEntry("AskArtwork", true, "Ask for background artwork before exporting blurred card art; otherwise choose automatically");
            foreach (DeckImageOption option in Options)
                Entries[option] = category.CreateEntry(option.ToString(), true,
                    (option == DeckImageOption.Background ? "Decorative background" : Label(option)) + " in copied deck images");
            LoaderPreferences.Load();
            s_loaded = true;
            if (s_quality.Value == "Diamond" || s_quality.Value == "Signature")
            {
                s_quality.Value = "Highest";
                CollectionQualitySettings.QueuePreferencesSave();
            }
        }
        internal static bool Enabled(DeckImageOption option)
        {
            EnsureLoaded(); return Entries[option].Value;
        }
        internal static void Set(DeckImageOption option, bool value)
        {
            EnsureLoaded();
            if (Entries[option].Value == value) return;
            Entries[option].Value = value;
            CollectionQualitySettings.QueuePreferencesSave();
        }
        internal static DeckImageOption Capture()
        {
            DeckImageOption result = DeckImageOption.None;
            foreach (DeckImageOption option in Options) if (Enabled(option)) result |= option;
            return result;
        }
    }
}
