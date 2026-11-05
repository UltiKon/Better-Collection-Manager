using System.Reflection;
using System.Runtime.InteropServices;
#if !BEPINEX
using MelonLoader;
#endif
using BetterCollectionManager;

[assembly: AssemblyTitle("Better Collection Manager")]
[assembly: AssemblyDescription("Collection quality, filters, categories, combos and deck tools for Hearthstone")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("UltiKon, Imik and contributors")]
[assembly: AssemblyProduct("Better Collection Manager")]
[assembly: AssemblyCopyright("Copyright © 2026 UltiKon, Imik and contributors")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
[assembly: Guid("a7c3e91f-2b54-4d18-9e6a-1f8c0d4b5e72")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]

#if !BEPINEX
[assembly: MelonInfo(typeof(BetterCollectionManagerMod), "Better Collection Manager", "0.1.0", "UltiKon, Imik and contributors")]
[assembly: HarmonyDontPatchAll]
#endif
