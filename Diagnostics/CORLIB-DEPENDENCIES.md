# Dependencies of the five CustomCorlib DLLs

Inspected on 2026-11-05: the actual files in `CustomCorlib-Unity6000/`, the available
Hearthstone references (Unity 6000.3.11f1), and MelonLoader 0.7.3 including Harmony.
The DLLs were read as PE/.NET metadata; selected TLS methods were also decompiled.
Hearthstone was not launched here.

## Result

The five files total **8,650,752 bytes = 8.65 MB = 8.25 MiB**. Their static assembly
references contain no UnityEditor library, so a full Unity Editor installation
appears unnecessary for this workaround. The existing game/Mono runtime and a
complete, compatible MelonLoader installation remain prerequisites.

This is static dependency analysis. It does not establish successful startup on
a fresh installation or compatibility of every later call with stripped game
libraries.

## Managed dependencies

| File | Direct assembly references |
| --- | --- |
| `mscorlib.dll` | No other managed assemblies; uses the native Mono runtime |
| `System.dll` | mscorlib, Mono.Security, System.Core, System.Configuration, System.Xml, System.Numerics |
| `System.Core.dll` | mscorlib, System |
| `Mono.Security.dll` | mscorlib, System |
| `Mono.Btls.Interface.dll` | mscorlib, System, Mono.Security |

Four additional assembly names appear outside this five-file set. All four
already exist in the inspected `Hearthstone_Data/Managed/`:

| Additional library | Existing game copy, bytes | Referenced by |
| --- | ---: | --- |
| `System.Configuration.dll` | 124416 | System |
| `System.Xml.dll` | 2159616 | System; also System.Configuration and System.Security |
| `System.Numerics.dll` | 38912 | System; also System.Security |
| `System.Security.dll` | 81920 | System.Configuration, indirectly |

The references of these four game libraries resolve within these nine assemblies.
This static pass introduces no further unresolved managed assembly names. Matching
names and version numbers do not prove that every later required method survives
stripping. Do not replace these four game files based on this report.

MelonLoader documents that the override prioritizes base libraries while game
and loader directories remain additional search sources:
[MelonLoader 0.7.3 launch options](https://github.com/LavaGang/MelonLoader/blob/v0.7.3/README.md#launch-options).

## GetFrames confirmed

`System.Diagnostics.StackTrace.GetFrames()` is missing from the original game
`mscorlib.dll` (2,790,912 bytes) and present as a public instance method with a body
in the full copy (4,632,064 bytes). Both inspected MelonLoader.dll and 0Harmony.dll
reference it. This identifies the missing API supplied by the replacement;
without the original exception it does not reconstruct the exact earlier failure.

## Native libraries and TLS

The files also contain native calls. Windows APIs such as kernel32, advapi32,
and crypt32 are supplied by Windows/Wine. Full System.dll also contains alternative
platform-specific backends for macOS, libc, MonoPosixHelper, and libmono-btls-shared.
A P/Invoke import alone does not establish that a backend is used on a particular
system. These names are not a blanket list of extra downloads.

The inspected `Mono.Net.Security.MonoTlsProviderFactory` defaults to UnityTLS when
`Mono.Unity.UnityTls.IsSupported` is true. Its interface comes from the native
Unity/Mono runtime through the `GetUnityTlsInterface()` internal call. BTLS is an
alternative path; an explicit `MONO_TLS_PROVIDER` can change selection. Whether
additional native TLS files are loaded requires actual startup and network testing.

## Next practical test

Test Hearthstone, compatible MelonLoader, all five override DLLs, the existing
search override, and the mod DLL without installing Unity Editor. Check login,
collection, mod features, and restarting. This tests whether the five-file set
is sufficient; initially there is no need to remove files from it.

## Inspected file hashes

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| `Mono.Btls.Interface.dll` | 22,528 | `93ee11f2e0a9e2364b67b4e70a4974409494254c53e278f394e2c6dffa10e9c5` |
| `Mono.Security.dll` | 241,152 | `618009319dbf3ae374f9b5dc0d0522e1e1bdd172200c120a5471747b7991a584` |
| `System.Core.dll` | 1,113,088 | `a5e7d47313da0eca0b92a901a30498f5e970916faf63b70b74dd62b09760ed72` |
| `System.dll` | 2,641,920 | `7019d6b6dbfa07cf30f62e7d3ccb969f70cbf001fd72112938ffb535c7f75d81` |
| `mscorlib.dll` | 4,632,064 | `6c76e06e6d44cf0f39bf59e6a90770968b70093ad4c1572dfbc5e46e431c5fbf` |
