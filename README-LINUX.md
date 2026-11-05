# Building on Linux

For the additional BepInEx variant, use `./build-bepinex.sh` and follow
[README-BEPINEX.md](README-BEPINEX.md). The instructions below build MelonLoader.

Install a .NET SDK, Hearthstone and MelonLoader locally. From this source directory:

```sh
./build-linux.sh
# For another installation:
# GAME_DIR="/path/to/Hearthstone" ./build-linux.sh
```

The default installation is
`$HOME/Games/battlenet/drive_c/Program Files (x86)/Hearthstone`, relative to the
current user's home directory. `GAME_DIR` overrides this default. No personal
username or absolute home path is stored. The script locates `MelonLoader.dll` and `0Harmony.dll` together
under `MelonLoader/net35`, `MelonLoader/`, or `MelonLoader/net6` and uses those local
references. Successful compilation does not establish loader/runtime compatibility.

The project targets .NET Framework 4.7.2. The SDK restores Microsoft's reference
assemblies package `Microsoft.NETFramework.ReferenceAssemblies.net472` 1.0.3 from
NuGet. Game references come only from the chosen client's `Hearthstone_Data/Managed`
directory. Loose DLLs and SDK copies in the source directory are excluded from
reference discovery.

The Release result is `linux-build/BetterCollectionManager.dll`, without debug
symbols. Close the game and copy only this DLL into its `Mods/` directory. The
script does not deploy it or change the Wine prefix. Categories and Packages are
embedded; no personal UserData is required. The prebuilt DLL in the distribution
ZIP is also a net472 assembly; there is no separate native Linux mod binary.

For a direct build with nonstandard loader locations:

```sh
dotnet build BetterCollectionManager.Linux.csproj -c Release \
  -p:GameInstallRoot="/path/to/Hearthstone" -p:MelonDllDir="<directory containing loader DLLs>"
```

For optional read-only installation diagnostics:

```sh
GAME_DIR="/path/to/Hearthstone" bash diagnose-startup-linux.sh
```

The diagnostics print paths and installation details from the machine where you
run them. Inspect that output before sharing it. Diagnostics and the optional
[StartupProbe](Diagnostics/StartupProbe/README.md) are not required to install the
mod. Loader startup failures require separate investigation; building the mod
does not fix loader initialization errors.

See [README.md](README.md) for current features and in-game validation steps.
