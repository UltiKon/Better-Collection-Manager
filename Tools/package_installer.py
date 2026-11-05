#!/usr/bin/env python3
"""Package the reviewed setup scripts, mod and five corlib libraries."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import zipfile

REPO = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location('bcm_release', REPO / 'Tools/package_release.py')
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


def check_user_paths(name, data):
    # Standard library system paths are expected in the third-party binaries.
    # Reject home/account paths and workspace locations, including UTF-16 strings.
    patterns = [re.compile(r'/(?:home|Users|root|workspace)(?:/|\\)[^\s"<>]*', re.I),
                re.compile(r'[A-Z]:[\\/]+Users[\\/]+[^\s"<>]+', re.I)]
    upstream = '/' + '/'.join(('Users', 'bokken', 'build', 'output', 'Unity-Technologies', 'mono', 'mcs', 'class', 'lib', 'unityjit-win32')) + '/'
    for text in (data.decode('utf-8', errors='ignore'), data.decode('utf-16-le', errors='ignore'),
                 data[1:].decode('utf-16-le', errors='ignore')):
        if name in {'payload/CustomCorlib-Unity6000/Mono.Security.dll', 'payload/CustomCorlib-Unity6000/Mono.Btls.Interface.dll'}:
            # Original Unity compiler CodeView records, not sender-account paths.
            # Preserve these signed, manifest-checked third-party binaries.
            text = text.replace(upstream + Path(name).stem + '.pdb', '')
        if any(pattern.search(text) for pattern in patterns):
            raise ValueError('Potential private path in ' + name)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=REPO / 'dist/BetterCollectionManager-0.1.0-installer.zip')
    args = parser.parse_args()
    contents = {'LICENSE': (REPO / 'LICENSE').read_bytes()}
    scripts = ['setup.py', 'install.cmd', 'install.sh', 'install.command', 'README.md', 'payload.json',
               'ThirdParty/Mono-LICENSE.txt', 'ThirdParty/NOTICES.md']
    for name in scripts:
        contents[name] = (REPO / 'Installer' / name).read_bytes()
    manifest = json.loads(contents['payload.json'])['files']
    for name, digest in manifest.items():
        if name.startswith('payload/CustomCorlib-Unity6000/'):
            source = REPO / name.removeprefix('payload/')
        elif name == 'payload/Mods/BetterCollectionManager.dll':
            source = REPO / 'linux-build/BetterCollectionManager.dll'
        else:
            raise ValueError('Unrecognized payload entry')
        data = source.read_bytes()
        if hashlib.sha256(data).hexdigest() != digest:
            raise ValueError('Build changed; review and update Installer/payload.json before packaging')
        contents[name] = data
    for name, data in contents.items():
        check_user_paths(name, data)
    contents['SHA256SUMS.txt'] = ''.join(hashlib.sha256(data).hexdigest() + '  ' + name + '\n'
                                        for name, data in sorted(contents.items())).encode()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    temporary = args.output.with_suffix('.zip.tmp')
    try:
        with zipfile.ZipFile(temporary, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
            for name, data in sorted(contents.items()):
                release.add_entry(archive, name, data, executable=name.endswith(('.sh', '.command')))
        with zipfile.ZipFile(temporary) as archive:
            assert archive.testzip() is None
        temporary.replace(args.output)
    finally:
        temporary.unlink(missing_ok=True)
    print(f'Created {args.output.name}: {len(contents)} files, {args.output.stat().st_size:,} bytes')
    print('SHA256: ' + hashlib.sha256(args.output.read_bytes()).hexdigest())


if __name__ == '__main__':
    main()
