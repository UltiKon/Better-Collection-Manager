# Third-party components

The MIT license in the package's `LICENSE` applies to the mod, installer and
original project code. It does not replace the licenses of the third-party
components described below.

The five managed libraries supplied in `payload/CustomCorlib-Unity6000/` are the
user-provided Unity/Mono libraries from the source repository. Their exact hashes
are recorded in `payload.json`. The included `Mono-LICENSE.txt` is the license
notice published by Unity's Mono source repository:
https://github.com/Unity-Technologies/mono/blob/unity-main/LICENSE

MelonLoader 0.7.3 is downloaded unchanged from the official LavaGang release and
verified against the published SHA-256 asset digest. Its own license and third-party
notices remain in the installed `MelonLoader/Documentation/` directory:
https://github.com/LavaGang/MelonLoader/releases/tag/v0.7.3

This setup is a separate project and is not endorsed by Blizzard, Unity or LavaGang.

Two original Mono libraries retain CodeView/PDB references to the Unity build
worker. These are upstream compiler records, not the sender's local paths. The
original DLLs are preserved to avoid changing their signatures and identities.
