#!/usr/bin/env python3
"""Retrieve checksum-pinned build assets without committing binaries to the tree."""
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import urllib.request
import zipfile


def main():
    root = Path(__file__).resolve().parent.parent
    metadata = (root / 'src/BetterCollectionManager/ModMetadata.cs').read_text()
    version = re.search(r'const string Version = "([0-9]+\.[0-9]+\.[0-9]+)";', metadata).group(1)
    manifest = json.loads((root / '.github/release-assets' / (version + '.manifest')).read_text())
    if manifest['tag'] != version or '\n' in manifest['title']:
        raise ValueError('Release metadata does not match the source version')
    expected = {'BetterCollectionManager.dll', f'BetterCollectionManager-{version}-bepinex.zip',
                f'BetterCollectionManager-{version}-shareable.zip'}
    if {asset['name'] for asset in manifest['assets']} != expected or len(manifest['assets']) != 3:
        raise ValueError('Expected the standalone BepInEx DLL and both loader packages')
    output = root / 'release-assets'
    output.mkdir(exist_ok=True)
    for asset in manifest['assets']:
        if not re.fullmatch('[0-9a-f]{40}', asset['blob']):
            raise ValueError('Invalid release blob SHA')
        url = f"https://api.github.com/repos/{os.environ['GH_REPO']}/git/blobs/{asset['blob']}"
        request = urllib.request.Request(url, headers={
            'Authorization': 'Bearer ' + os.environ['GH_TOKEN'],
            'Accept': 'application/vnd.github+json',
            'User-Agent': 'BetterCollectionManager-release',
        })
        with urllib.request.urlopen(request, timeout=120) as response:
            blob = json.load(response)
        content = base64.b64decode(blob['content'])
        if len(content) != asset['size'] or hashlib.sha256(content).hexdigest() != asset['sha256']:
            raise ValueError('Release asset checksum mismatch: ' + asset['name'])
        (output / asset['name']).write_bytes(content)
    dll = (output / 'BetterCollectionManager.dll').read_bytes()
    if not dll.startswith(b'MZ') or b'BSJB' not in dll or b'BepInEx' not in dll or b'MelonLoader' in dll:
        raise ValueError('The standalone download must be the BepInEx build')
    for loader, member in [('bepinex', 'BepInEx/plugins/BetterCollectionManager/BetterCollectionManager.dll'),
                           ('shareable', 'Mods/BetterCollectionManager.dll')]:
        with zipfile.ZipFile(output / f'BetterCollectionManager-{version}-{loader}.zip') as archive:
            if archive.testzip() is not None:
                raise ValueError('Corrupt release package')
            checksums = dict(line.split('  ', 1)[::-1]
                             for line in archive.read('SHA256SUMS.txt').decode().splitlines())
            for name, checksum in checksums.items():
                if hashlib.sha256(archive.read(name)).hexdigest() != checksum:
                    raise ValueError('Package entry checksum mismatch: ' + name)
            if loader == 'bepinex' and archive.read(member) != dll:
                raise ValueError('Standalone BepInEx DLL differs from the verified package')
    with open(os.environ['GITHUB_ENV'], 'a') as environment:
        environment.write(f"RELEASE_TAG={version}\nRELEASE_TITLE={manifest['title']}\n")
    print('Verified the standalone BepInEx DLL and both loader packages for ' + version)


if __name__ == '__main__':
    main()
