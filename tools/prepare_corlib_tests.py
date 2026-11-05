#!/usr/bin/env python3
"""Prepare separate Wine/MelonLoader corlib test folders without changing originals."""
import argparse
import hashlib
import itertools
import json
import os
from pathlib import Path
import shutil
import sys

CORE = 'mscorlib.dll'
OPTIONAL = ('System.dll', 'System.Core.dll', 'Mono.Security.dll', 'Mono.Btls.Interface.dll')
EXPECTED = {CORE, *OPTIONAL}


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def variants(exhaustive):
    yield '00-full-baseline', (CORE, *OPTIONAL)
    yield '01-mscorlib-only', (CORE,)
    if exhaustive:
        for count in range(1, len(OPTIONAL)):
            for selected in itertools.combinations(OPTIONAL, count):
                label = '-'.join(str(OPTIONAL.index(name) + 1) for name in selected)
                yield 'subset-' + label, (CORE, *selected)
    else:
        for index, omitted in enumerate(OPTIONAL, 2):
            yield f'{index:02d}-without-{omitted[:-4]}', (CORE, *(name for name in OPTIONAL if name != omitted))


def prepare(game, output, exhaustive=False):
    source = game / 'MelonLoader' / 'CustomCorlib-Unity6000'
    if not source.is_dir():
        raise ValueError('CustomCorlib-Unity6000 is missing from the selected game directory.')
    entries = sorted(source.iterdir())
    if any(path.is_symlink() or not path.is_file() for path in entries):
        raise ValueError('The corlib directory must contain regular files without subdirectories or symlinks.')
    found = {path.name for path in entries if path.suffix.lower() == '.dll'}
    if found != EXPECTED:
        missing, extra = sorted(EXPECTED - found), sorted(found - EXPECTED)
        raise ValueError(f'The DLL selection differs from the reviewed five-file set. Missing: {missing}; extra: {extra}')
    # Never create tests within the source, and never reuse an existing destination.
    if output.resolve() == source.resolve() or source.resolve() in output.resolve().parents:
        raise ValueError('The output directory must not be inside the original corlib directory.')
    before = {path.name: digest(path) for path in entries}
    auxiliary = tuple(path.name for path in entries if path.suffix.lower() != '.dll')
    rows = []
    output.mkdir(parents=False, exist_ok=False)
    for label, libraries in variants(exhaustive):
        destination = output / label
        destination.mkdir()
        names = (*libraries, *auxiliary)
        for name in names:
            shutil.copyfile(source / name, destination / name)
            if digest(destination / name) != before[name]:
                raise ValueError('A copy does not match its source file.')
        rows.append({'test': label, 'dlls': list(libraries)})
    after = {path.name: digest(path) for path in source.iterdir() if path.is_file()}
    if before != after:
        raise ValueError('The source files changed externally while copying. Do not use these results.')
    manifest = {
        'description': 'Corlib override candidates; no game execution or runtime validation performed.',
        'files': [{'name': path.name, 'bytes': path.stat().st_size, 'sha256': before[path.name]} for path in entries],
        'auxiliary_files_in_every_test': list(auxiliary),
        'tests': rows,
    }
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    heading = 'Test\tDLLs\tLoader\tLogin\tCollection\tModFeatures\tRestart\tError\n'
    lines = ''.join(row['test'] + '\t' + ','.join(row['dlls']) + '\t\t\t\t\t\t\n' for row in rows)
    (output / 'results.tsv').write_text(heading + lines, encoding='utf-8')
    (output / 'TESTPLAN.md').write_text('''# Corlib tests

These directories are copies. The script does not change the working
CustomCorlib-Unity6000 directory, game files, mods, or Battle.net settings.
It does not launch the game. The manifest contains filenames, sizes, and hashes,
without personal absolute paths. Ordinary game/loader logs can contain private
paths or account details: review them before including them in a bug report.

1. Close Hearthstone completely. Save the working Battle.net launch argument
   locally. Leave MelonLoader, mods, and all other settings unchanged.
2. Test `00-full-baseline` first. In the existing launch argument, replace only
   the final `CustomCorlib-Unity6000` directory with `BCM-Corlib-Tests\\00-full-baseline`.
   If using a custom output directory, use its actual path instead. Keep the
   quotation marks around the complete Windows/Wine path.
3. Launch the game, sign in, open the collection, and test filters, quality display,
   deck editing, and other regularly used features. Restart and repeat. Record
   the result in `results.tsv`. If even the full copy fails, stop and restore the
   original argument: there is not yet a valid comparison.
4. Close the game completely and test `01-mscorlib-only` the same way. If successful,
   that single override DLL is a candidate for the minimal selection.
5. Test other variants if needed. The default set omits one extra DLL at a time;
   `--all-combinations` creates all 16 combinations containing mscorlib. Separate
   successful omissions do not prove the same files can be omitted together.
   Always test the final combined selection separately.
6. Restore the original launch argument after testing. Remove test directories
   manually only after the game has been completely closed.

Startup alone is not sufficient. TLS/networking, login, and later-opened views can
load additional libraries on demand. Even extended success does not establish
compatibility with all future game and loader versions.

If an override DLL is absent, Mono may fall back to the game or loader copy.
Success therefore means only that this full override copy was unnecessary for
the tested workflows; the library itself may still be required. For additional
loading diagnostics, launch Battle.net from an environment with MONO_LOG_LEVEL=debug
and MONO_LOG_MASK=asm. Exact output depends on the Unity Mono build, and Battle.net
must inherit that environment. These logs can be large and contain private paths.
''', encoding='utf-8')
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game-dir', type=Path, default=os.environ.get('GAME_DIR'), help='Hearthstone directory; alternatively set GAME_DIR')
    parser.add_argument('--output', type=Path, help='New output directory; default: MelonLoader/BCM-Corlib-Tests')
    parser.add_argument('--all-combinations', action='store_true', help='All 16 combinations instead of six initial comparisons')
    args = parser.parse_args()
    if args.game_dir is None:
        parser.error('--game-dir or GAME_DIR is required.')
    game = args.game_dir.expanduser().resolve()
    output = args.output.expanduser().resolve() if args.output else game / 'MelonLoader' / 'BCM-Corlib-Tests'
    try:
        rows = prepare(game, output, args.all_combinations)
    except (OSError, ValueError) as error:
        # Report exception kind rather than absolute local paths from OSError.
        detail = str(error) if isinstance(error, ValueError) else type(error).__name__
        parser.exit(1, f'No tests started. Preparation failed: {detail}\n')
    print(f'Prepared {len(rows)} test directories. Original files are unchanged.')
    print('Instructions: TESTPLAN.md; results: results.tsv; file hashes: manifest.json in the output directory.')
    print('The game was not launched. Test 00-full-baseline first, then 01-mscorlib-only.')


if __name__ == '__main__':
    main()
