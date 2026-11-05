#!/usr/bin/env python3
"""Create a shareable mod ZIP from a Release DLL and reviewed source files."""
import argparse
import hashlib
from pathlib import Path, PurePosixPath
import re
import stat
import zipfile

ROOT = Path(__file__).resolve().parent.parent
SOURCE_DIRS = ('Data', 'Patches', 'Properties', 'Tests', 'Tools', 'Diagnostics', 'Installer')
SOURCE_SUFFIXES = {'.cs', '.csproj', '.props', '.sln', '.md', '.json', '.tsv', '.py', '.sh', '.cmd', '.command'}
EXCLUDED_PARTS = {'bin', 'obj', '__pycache__', '.git', '.vs'}
TOP_LEVEL_FILES = {'.gitignore', 'LICENSE', 'build-linux.sh', 'diagnose-startup-linux.sh', 'bettercollectionmanager.cfg'}
# Match actual paths, including UTF-16 assembly strings, without embedding any
# particular builder's account or machine name in this program.
PRIVATE_PATTERNS = (
    re.compile(r'/(?:home|Users|root|workspace)(?:/|\\)[^\s"<>]*', re.I),
    re.compile(r'(?<![\w])\b[A-Z]:[\\/](?![\\/]*["\'])[^\s"<>]+', re.I),
    re.compile(r'\\\\[A-Za-z0-9_.-]+\\[A-Za-z0-9_$.-]+\\'),
    re.compile(r'[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}'),
    re.compile(r'-----BEGIN (?:[A-Z]+ )?PRIVATE KEY-----'),
    re.compile(r'\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|sk-[A-Za-z0-9_-]{24,})\b'),
)


def check_privacy(name, data):
    # Published upstream license attribution is public, not sender metadata.
    if name.removeprefix('Source/') == 'Installer/ThirdParty/Mono-LICENSE.txt' and hashlib.sha256(data).hexdigest() == '844242559dbdb96cb9cbe103b06c2bc8a349f19ad0b89d5c3cf830a8914333f6':
        return
    for text in (data.decode('utf-8', errors='ignore'), data.decode('utf-16-le', errors='ignore'),
                 data[1:].decode('utf-16-le', errors='ignore')):
        if any(pattern.search(text) for pattern in PRIVATE_PATTERNS):
            raise ValueError(f'Potential private path or credential in {name}; review before packaging')


def source_files():
    files = {p for p in ROOT.iterdir() if p.is_file()
             and (p.name in TOP_LEVEL_FILES or p.suffix in SOURCE_SUFFIXES)}
    for directory in SOURCE_DIRS:
        files.update(p for p in (ROOT / directory).rglob('*') if p.is_file()
                     and (p.suffix in SOURCE_SUFFIXES or p.relative_to(ROOT).as_posix() == 'Installer/ThirdParty/Mono-LICENSE.txt')
                     and not EXCLUDED_PARTS.intersection(p.relative_to(ROOT).parts))
    for path in sorted(files):
        if path.is_symlink():
            raise ValueError(f'Symlinks are not allowed: {path.name}')
        yield path


def add_entry(archive, name, data, executable=False):
    path = PurePosixPath(name)
    if path.is_absolute() or '..' in path.parts:
        raise ValueError(f'Unsafe archive entry: {name}')
    entry = zipfile.ZipInfo(name, date_time=(2026, 1, 1, 0, 0, 0))
    entry.create_system = 3
    entry.external_attr = (stat.S_IFREG | (0o755 if executable else 0o644)) << 16
    entry.compress_type = zipfile.ZIP_DEFLATED
    archive.writestr(entry, data)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dll', type=Path, required=True, help='Fresh Release mod DLL (no symbols)')
    parser.add_argument('--loader', choices=('melonloader', 'bepinex'), default='melonloader')
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    if args.output is None:
        suffix = 'bepinex' if args.loader == 'bepinex' else 'shareable'
        args.output = ROOT / f'dist/BetterCollectionManager-0.1.0-{suffix}.zip'
    if args.dll.name != 'BetterCollectionManager.dll' or not args.dll.is_file():
        parser.error('--dll must point to BetterCollectionManager.dll')
    dll = args.dll.read_bytes()
    if not dll.startswith(b'MZ') or b'BSJB' not in dll:
        parser.error('--dll must be a managed PE assembly')
    # A Release produced by the supplied project should have neither embedded
    # Portable PDBs nor a CodeView/PDB reference left in the DLL.
    if b'.pdb' in dll.lower() or b'MPDB' in dll:
        parser.error('DLL contains debug information; rebuild with DebugType=none and DebugSymbols=false')
    if args.loader == 'bepinex':
        if b'BepInEx' not in dll or b'MelonLoader' in dll:
            parser.error('--loader bepinex requires the BepInEx build, without a MelonLoader dependency')
        readme = 'README-BEPINEX.md'
        dll_path = 'BepInEx/plugins/BetterCollectionManager/BetterCollectionManager.dll'
    else:
        if b'MelonLoader' not in dll or b'BepInEx' in dll:
            parser.error('--loader melonloader requires the MelonLoader build')
        readme = 'DISTRIBUTION.md'
        dll_path = 'Mods/BetterCollectionManager.dll'
    contents = {'README.md': (ROOT / readme).read_bytes(),
                'LICENSE': (ROOT / 'LICENSE').read_bytes(),
                dll_path: dll}
    for path in source_files():
        contents['Source/' + path.relative_to(ROOT).as_posix()] = path.read_bytes()
    for name, data in contents.items():
        check_privacy(name, data)
    sums = ''.join(f'{hashlib.sha256(data).hexdigest()}  {name}\n'
                   for name, data in sorted(contents.items()))
    contents['SHA256SUMS.txt'] = sums.encode('utf-8')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    temporary = args.output.with_suffix('.zip.tmp')
    try:
        with zipfile.ZipFile(temporary, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
            for name, data in sorted(contents.items()):
                add_entry(archive, name, data, executable=name.endswith('.sh'))
        with zipfile.ZipFile(temporary) as archive:
            if archive.testzip() is not None:
                raise ValueError('ZIP integrity check failed')
        temporary.replace(args.output)
    finally:
        temporary.unlink(missing_ok=True)
    print(f'Created {args.output.name}: {len(contents)} files, {args.output.stat().st_size:,} bytes')
    print(f'SHA256: {hashlib.sha256(args.output.read_bytes()).hexdigest()}')


if __name__ == '__main__':
    main()
