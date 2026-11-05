# Identifying required corlib overrides

The available Hearthstone references use Unity 6000.3.11f1 and MelonLoader 0.7.3.
Static metadata inspection on 2026-11-05 established:

- The original `mscorlib.dll` (2,790,912 bytes) lacks
  `System.Diagnostics.StackTrace.GetFrames()`.
- Both the inspected `MelonLoader.dll` and `0Harmony.dll` reference that method.
  This matches the reported startup symptom; the original exception is not
  available for a complete reconstruction.
- `System.dll`, `System.Core.dll`, and `Mono.Security.dll` reference each other
  and other system assemblies. This does not establish that a full override copy
  of each library is necessary.

The five full DLLs are available under `CustomCorlib-Unity6000/` in the repository.
See [CORLIB-DEPENDENCIES.md](CORLIB-DEPENDENCIES.md) for their static dependencies
and the confirmed GetFrames difference. A fresh game installation cannot be
launched in this environment.

## Preparing separate test copies

The helper requires Python 3.11+, without Unity or a .NET SDK. With Hearthstone
closed, use your own Linux game directory:

```sh
python3 tools/prepare_corlib_tests.py --game-dir "/path/to/Hearthstone"
```

It reads `MelonLoader/CustomCorlib-Unity6000` and creates six separate candidates
under `MelonLoader/BCM-Corlib-Tests`: all five DLLs, mscorlib alone, and four variants
omitting one other DLL each. Additional regular non-DLL files are copied unchanged
into every variant. Unknown DLLs or subdirectories require separate review and
cause the script to stop.

`--all-combinations` creates all 16 combinations containing mscorlib instead.
An existing output directory is never overwritten. Use `--output` with a new
location for another series. A failure may leave incomplete test copies; the
working source directory is preserved.

The script does not change original files, Battle.net settings, or mod installation.
Generated `TESTPLAN.md` explains the launch arguments and comparisons; record
results in `results.tsv`. The `manifest.json` records filenames, sizes, and SHA-256
hashes without personal installation paths.

Test the full copy as a control first, then mscorlib alone. Check login, collection,
mod features, and restarting for each candidate. Temporarily point Battle.net's
launch argument at the candidate, then restore the original value. Success without
an override DLL can mean that Mono loaded a game/loader copy instead. Only a final
combination tested together is a candidate for a smaller installation.

## Sources

- MelonLoader documents the search override for mscorlib and base libraries:
  https://github.com/LavaGang/MelonLoader/blob/v0.7.3/README.md#launch-options
- Unity Mono documents MONO_LOG_LEVEL and MONO_LOG_MASK for supplementary assembly
  loading diagnostics; verify support in the particular runtime build:
  https://github.com/Unity-Technologies/mono/blob/unity-main/man/mono.1
