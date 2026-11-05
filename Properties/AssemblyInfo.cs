using System.Reflection;
using System.Runtime.InteropServices;
#if !BEPINEX
using MelonLoader;
#endif
using BetterCollectionManager;

[assembly: AssemblyTitle(ModMetadata.Name)]
[assembly: AssemblyDescription("Collection quality, filters, categories, combos and deck tools for Hearthstone")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("UltiKon, Imik and contributors")]
[assembly: AssemblyProduct(ModMetadata.Name)]
[assembly: AssemblyCopyright("Copyright © 2026 UltiKon, Imik and contributors")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
[assembly: Guid("a7c3e91f-2b54-4d18-9e6a-1f8c0d4b5e72")]
[assembly: AssemblyVersion(ModMetadata.AssemblyVersion)]
[assembly: AssemblyFileVersion(ModMetadata.AssemblyVersion)]

#if !BEPINEX
[assembly: MelonInfo(typeof(BetterCollectionManagerMod), ModMetadata.Name, ModMetadata.Version, "UltiKon, Imik and contributors")]
[assembly: HarmonyDontPatchAll]
#endif
