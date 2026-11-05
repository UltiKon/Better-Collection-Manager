#!/usr/bin/env python3
"""Install Better Collection Manager on Windows, Wine, or native macOS."""
from __future__ import annotations

import argparse
import csv
from datetime import datetime, timezone
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import platform
import plistlib
import re
import shlex
import stat
import struct
import subprocess
import sys
import tempfile
import urllib.request
import uuid
import zipfile

ROOT = Path(__file__).resolve().parent
VERSION = '0.7.3'
RELEASES = {
    'windows': ('MelonLoader.x64.zip', '5b2b2f3d1cd42b59ec886c5bdc2663edae87a0097a4f4a8f58c0965a99dda416'),
    'macos': ('MelonLoader.macOS.x64.zip', '97290f7e44670cdbfdc408796cc0136138b6bca58b75f5b98a407d24896d6607'),
}
CORLIBS = ('mscorlib.dll', 'System.dll', 'System.Core.dll', 'Mono.Security.dll', 'Mono.Btls.Interface.dll')
OPTION = '--melonloader.monosearchpathoverride'
KNOWN_GAME_KEYS = {'hs', 'hs_beta', 'hs_ptr', 'hs_test', 'wtcg', 'hearthstone'}
WINE_KEY = r'Software\\Wine\\AppDefaults\\Hearthstone.exe\\DllOverrides'


class SetupError(Exception):
    pass


def sha(data):
    return hashlib.sha256(data).hexdigest()


def checked_json(raw, label):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise SetupError(f'{label}: duplicate JSON keys; no changes made.')
            result[key] = value
        return result
    try:
        data = json.loads(raw.decode('utf-8-sig'), object_pairs_hook=unique)
    except (ValueError, UnicodeError) as error:
        raise SetupError(f'{label}: invalid UTF-8 JSON.') from error
    if not isinstance(data, dict):
        raise SetupError(f'{label}: expected a JSON object.')
    return data


def merge_launch_arguments(existing, corlib_path):
    """Replace only our option, preserving the spelling of unrelated arguments."""
    if not isinstance(existing, str):
        raise SetupError('AdditionalLaunchArguments must be a string.')
    if any(char in corlib_path for char in ('"', '\n', '\r', '\0')):
        raise SetupError('The installation path contains unsupported special characters.')
    pattern = re.compile(r'(?<!\S)' + re.escape(OPTION) + r'(?:=(?:"[^"\r\n]*"|[^\s"]+)|\s+(?:"[^"\r\n]*"|(?!-)[^\s"]+))?(?=\s|$)', re.I)
    cleaned = pattern.sub('', existing).strip()
    if OPTION.lower() in cleaned.lower():
        raise SetupError('The existing corlib argument is ambiguous. Check its quotation marks.')
    return (cleaned + ' ' if cleaned else '') + OPTION + '="' + corlib_path + '"'


def choose_game_key(config, explicit=None, interactive=False):
    games = config.get('Games')
    if not isinstance(games, dict):
        raise SetupError('Battle.net.config has no Games section. Launch Hearthstone through Battle.net once first.')
    if explicit:
        if not re.fullmatch(r'[A-Za-z0-9_-]+', explicit):
            raise SetupError('Invalid Battle.net game key.')
        if explicit in games and not isinstance(games[explicit], dict):
            raise SetupError('The selected Games entry is not an object.')
        return explicit
    matches = [key for key, value in games.items() if isinstance(value, dict) and key.lower() in KNOWN_GAME_KEYS]
    if len(matches) == 1:
        return matches[0]
    if matches and interactive:
        print('Multiple Hearthstone entries found:')
        for index, key in enumerate(matches, 1):
            print(f'  {index}. {key}')
        try:
            choice = int(input('Select an entry: '))
            if choice < 1:
                raise ValueError()
            return matches[choice - 1]
        except (ValueError, IndexError):
            raise SetupError('Invalid selection.')
    raise SetupError('The Hearthstone entry is ambiguous. Supply the matching Games key with --game-key; setup will not guess an entry.')


def update_battle_config(raw, key, corlib):
    config = checked_json(raw, 'Battle.net.config')
    games = config.get('Games')
    if not isinstance(games, dict):
        raise SetupError('Games is missing from Battle.net.config.')
    game = games.setdefault(key, {})
    if not isinstance(game, dict):
        raise SetupError('The Hearthstone entry is not an object.')
    before = game.get('AdditionalLaunchArguments', '')
    after = merge_launch_arguments(before, corlib)
    if before == after:
        return raw, after
    game['AdditionalLaunchArguments'] = after
    newline = '\r\n' if b'\r\n' in raw else '\n'
    encoded = (json.dumps(config, ensure_ascii=False, indent=4) + '\n').replace('\n', newline).encode('utf-8')
    if raw.startswith(b'\xef\xbb\xbf'):
        encoded = b'\xef\xbb\xbf' + encoded
    return encoded, after


def wine_registry_override(raw):
    try:
        text = raw.decode('utf-8')
    except UnicodeError as error:
        raise SetupError('user.reg is not in the expected Wine format.') from error
    if not text.startswith('WINE REGISTRY Version 2'):
        raise SetupError('user.reg is not in the expected Wine format.')
    newline = '\r\n' if '\r\n' in text else '\n'
    section = re.compile(r'^\[' + re.escape(WINE_KEY) + r'\][^\r\n]*(?:\r?\n|$)', re.M | re.I)
    matches = list(section.finditer(text))
    if len(matches) > 1:
        raise SetupError('Ambiguous Wine registry section.')
    setting = '"version"="native,builtin"'
    if matches:
        match = matches[0]
        following = re.search(r'^\[', text[match.end():], re.M)
        end = match.end() + following.start() if following else len(text)
        body = text[match.end():end]
        value = re.compile(r'^"version"[^\S\r\n]*=[^\r\n]*', re.M | re.I)
        if len(value.findall(body)) > 1:
            raise SetupError('Ambiguous version override in user.reg.')
        changed = value.sub(setting, body) if value.search(body) else body.rstrip('\r\n') + newline + setting + newline + newline
        text = text[:match.end()] + changed + text[end:]
    else:
        text = text.rstrip('\r\n') + newline + newline + '[' + WINE_KEY + '] ' + str(int(datetime.now(timezone.utc).timestamp())) + newline + setting + newline
    return text.encode('utf-8')


def infer_prefix(game):
    for parent in (game, *game.parents):
        if (parent / 'system.reg').is_file() and (parent / 'user.reg').is_file() and (parent / 'drive_c').is_dir():
            return parent
    return None


def wine_windows_path(path, prefix):
    mappings = []
    for drive in (prefix / 'dosdevices').glob('?:'):
        if not re.fullmatch('[a-zA-Z]:', drive.name) or not drive.is_dir():
            continue
        try:
            base = drive.resolve()
            relative = path.resolve().relative_to(base)
            mappings.append((len(base.parts), drive.name.upper() + '\\' + str(relative).replace('/', '\\')))
        except (ValueError, OSError):
            continue
    if not mappings:
        raise SetupError('No Wine drive maps to the game path. Check dosdevices in the selected prefix.')
    return max(mappings, key=lambda item: item[0])[1]


def find_battle_configs(system, prefix):
    if prefix:
        candidates = []
        users = prefix / 'drive_c' / 'users'
        for profile in users.iterdir() if users.is_dir() else []:
            for relative in ('AppData/Roaming/Battle.net/Battle.net.config', 'Application Data/Battle.net/Battle.net.config'):
                path = profile / relative
                if path.is_file():
                    candidates.append(path.resolve())
        return sorted(set(candidates))
    if system == 'Windows':
        base = os.environ.get('APPDATA')
        candidates = [Path(base) / 'Battle.net/Battle.net.config'] if base else []
    else:
        candidates = [Path.home() / 'Library/Application Support/Battle.net/Battle.net.config']
    return [path for path in candidates if path.is_file()]


def require_stopped(wine=False):
    """Do not edit configuration while a launcher or Wine may write it back."""
    if platform.system() == 'Windows':
        result = subprocess.run(['tasklist', '/FO', 'CSV', '/NH'], check=True, capture_output=True, text=True)
        names = [row[0].lower() for row in csv.reader(io.StringIO(result.stdout)) if row]
    else:
        result = subprocess.run(['ps', '-axo', 'comm='], check=True, capture_output=True, text=True)
        names = [line.strip().replace('\\', '/').rsplit('/', 1)[-1].lower() for line in result.stdout.splitlines()]
    targets = {'battle.net', 'battle.net.exe', 'battle.net helper.exe', 'battle.net launcher.exe', 'battle.net helper', 'battle.net launcher', 'hearthstone', 'hearthstone.exe', 'agent.exe'}
    if wine:
        targets.update({'wineserver', 'wineserver64', 'wine', 'wine64', 'wine-preloader', 'wine64-preloader'})
    if targets.intersection(names):
        raise SetupError('Close Hearthstone and Battle.net completely' + ('; also stop all Wine/CrossOver processes before editing the Wine registry.' if wine else '.'))


def pe_machine(path):
    with path.open('rb') as stream:
        if stream.read(2) != b'MZ':
            return None
        stream.seek(0x3c)
        offset = stream.read(4)
        if len(offset) != 4:
            return None
        stream.seek(struct.unpack('<I', offset)[0])
        header = stream.read(6)
    return struct.unpack('<H', header[4:])[0] if header[:4] == b'PE\0\0' and len(header) == 6 else None


def macho_arches(path):
    raw = path.read_bytes()[:4096]
    if len(raw) < 8:
        return set()
    magic = raw[:4]
    if magic in (b'\xcf\xfa\xed\xfe', b'\xce\xfa\xed\xfe'):
        return {struct.unpack('<I', raw[4:8])[0]}
    if magic in (b'\xfe\xed\xfa\xcf', b'\xfe\xed\xfa\xce'):
        return {struct.unpack('>I', raw[4:8])[0]}
    if magic in (b'\xca\xfe\xba\xbe', b'\xca\xfe\xba\xbf'):
        stride = 32 if magic[-1] == 0xbf else 20
        count = struct.unpack('>I', raw[4:8])[0]
        if count > (len(raw) - 8) // stride:
            return set()
        return {struct.unpack('>I', raw[8 + i * stride:12 + i * stride])[0] for i in range(count)}
    return set()


def native_app(game):
    apps = [game] if game.suffix.lower() == '.app' else list(game.glob('*.app'))
    apps = [app for app in apps if 'hearthstone' in app.stem.lower()]
    if len(apps) != 1:
        raise SetupError('Select exactly one Hearthstone.app, or place it in the selected game directory.')
    app = apps[0]
    try:
        with (app / 'Contents/Info.plist').open('rb') as stream:
            name = plistlib.load(stream)['CFBundleExecutable']
    except (OSError, KeyError, plistlib.InvalidFileException) as error:
        raise SetupError('Could not read CFBundleExecutable from Hearthstone.app.') from error
    if not isinstance(name, str) or Path(name).name != name:
        raise SetupError('Invalid app executable name.')
    binary = app / 'Contents/MacOS' / name
    if 0x01000007 not in macho_arches(binary):
        raise SetupError('Hearthstone.app has no x86_64 executable. MelonLoader 0.7.3 has no native arm64 loader; this installation will not be changed.')
    managed = app / 'Contents/Resources/Data/Managed/Assembly-CSharp.dll'
    if not managed.is_file():
        raise SetupError('No Mono Assembly-CSharp.dll found in the expected macOS app directory.')
    return app, binary


def payload_files():
    manifest = checked_json((ROOT / 'payload.json').read_bytes(), 'payload.json')
    entries = manifest.get('files')
    bundled = (ROOT / 'payload').is_dir()
    expected = {'payload/Mods/BetterCollectionManager.dll', *('payload/CustomCorlib-Unity6000/' + name for name in CORLIBS)}
    if not isinstance(entries, dict) or set(entries) != expected:
        raise SetupError('The setup package must contain exactly the mod DLL and the five reviewed corlib DLLs.')
    result = {}
    for name, expected_hash in entries.items():
        if bundled:
            path = ROOT / name
        elif name.startswith('payload/CustomCorlib-Unity6000/'):
            path = ROOT.parent / name.removeprefix('payload/')
        else:
            path = ROOT.parent / 'linux-build/BetterCollectionManager.dll'
        if path.is_symlink() or not path.is_file():
            raise SetupError('Payload file is missing or is a symlink: ' + name)
        data = path.read_bytes()
        if sha(data) != expected_hash:
            raise SetupError('Payload checksum mismatch: ' + name)
        if not data.startswith(b'MZ') or b'BSJB' not in data:
            raise SetupError('Not a managed DLL: ' + name)
        result[name.removeprefix('payload/')] = data
    return result


def loader_files(archive_path, target):
    name, expected_hash = RELEASES[target]
    raw = archive_path.read_bytes()
    if sha(raw) != expected_hash:
        raise SetupError('The MelonLoader archive does not match the reviewed official release.')
    files = {}
    total = 0
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        for item in archive.infolist():
            path = PurePosixPath(item.filename)
            if path.is_absolute() or '..' in path.parts or '\\' in item.filename or re.match(r'^[A-Za-z]:', item.filename):
                raise SetupError('Unsafe path in the loader archive.')
            if item.is_dir():
                continue
            if stat.S_ISLNK(item.external_attr >> 16):
                raise SetupError('Symlinks in the loader archive will not be installed.')
            if path.parts[0].endswith('.dSYM') or item.filename.endswith(('.pdb', '.mdb')):
                continue
            allowed_top = {'version.dll'} if target == 'windows' else {'MelonLoader.Bootstrap.dylib', 'melonloader-launch.sh'}
            if path.parts[0] != 'MelonLoader' and item.filename not in allowed_top:
                raise SetupError('Unrecognized content in the loader archive.')
            total += item.file_size
            if total > 150 * 1024 * 1024 or item.filename in files:
                raise SetupError('The loader archive is too large or contains duplicate files.')
            files[item.filename] = archive.read(item)
    required = {'MelonLoader/net35/MelonLoader.dll', 'MelonLoader/net35/0Harmony.dll', 'MelonLoader/Dependencies/SupportModules/Mono.dll'}
    required.add('version.dll' if target == 'windows' else 'MelonLoader.Bootstrap.dylib')
    if not required <= files.keys():
        raise SetupError('The MelonLoader archive is incomplete.')
    return files


def download_loader(directory, target):
    name = RELEASES[target][0]
    destination = directory / name
    url = f'https://github.com/LavaGang/MelonLoader/releases/download/v{VERSION}/{name}'
    print(f'Downloading official MelonLoader {VERSION}: {name}')
    request = urllib.request.Request(url, headers={'User-Agent': 'BetterCollectionManager-Setup/0.1.0'})
    with urllib.request.urlopen(request, timeout=60) as response, destination.open('wb') as stream:
        received = 0
        while chunk := response.read(1024 * 1024):
            received += len(chunk)
            if received > 100 * 1024 * 1024:
                raise SetupError('The loader download is unexpectedly large.')
            stream.write(chunk)
    return destination


def ensure_regular_target(path):
    if path.is_symlink():
        raise SetupError('An installation target is a symlink: ' + path.name)
    if path.exists() and not path.is_file():
        raise SetupError('An installation target is not a regular file: ' + path.name)
    for parent in path.parents:
        if parent.is_symlink():
            raise SetupError('A target directory contains a symlink. Select the actual game or configuration path.')
        if parent.exists() and not parent.is_dir():
            raise SetupError('A target directory is not a regular directory structure.')


def atomic_write(path, data, mode=None):
    descriptor, temporary = tempfile.mkstemp(prefix='.bcm-', dir=path.parent)
    temporary = Path(temporary)
    try:
        with os.fdopen(descriptor, 'wb') as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        if mode is not None:
            temporary.chmod(mode)
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


class Transaction:
    def __init__(self, game):
        name = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + uuid.uuid4().hex[:8]
        self.root = game / 'BCM-Setup-Backups' / name
        ensure_regular_target(self.root / 'restore.json')
        self.root.mkdir(parents=True, mode=0o700)
        self.state = {'version': 1, 'status': 'installing', 'files': [], 'directories': []}
        self.save()

    def save(self):
        atomic_write(self.root / 'restore.json', (json.dumps(self.state, indent=2) + '\n').encode(), 0o600)

    def mkdirs(self, path):
        missing = []
        while not path.exists():
            missing.append(path)
            path = path.parent
        for directory in reversed(missing):
            self.state['directories'].append(str(directory))
            self.save()
            directory.mkdir()

    def write(self, path, data, executable=False):
        ensure_regular_target(path)
        before = path.read_bytes() if path.exists() else None
        mode = stat.S_IMODE(path.stat().st_mode) if before is not None else 0o644
        if executable:
            mode |= 0o111
        if before == data and (not executable or os.access(path, os.X_OK)):
            return
        self.mkdirs(path.parent)
        backup = str(len(self.state['files'])) + '.bak' if before is not None else None
        if backup:
            atomic_write(self.root / backup, before, 0o600)
        self.state['files'].append({'target': str(path), 'backup': backup, 'before': sha(before) if before is not None else None,
                                    'after': sha(data), 'mode': stat.S_IMODE(path.stat().st_mode) if before is not None else None})
        self.save()
        atomic_write(path, data, mode)

    def finish(self):
        self.state['status'] = 'complete'
        self.save()

    def rollback(self):
        restore(self.root, verify=False)


def restore(backup, verify=True):
    state = checked_json((backup / 'restore.json').read_bytes(), 'restore.json')
    if state.get('version') != 1 or not isinstance(state.get('files'), list):
        raise SetupError('Unknown backup format.')
    if state.get('status') == 'restored':
        return
    for entry in state['files']:
        target = Path(entry['target'])
        ensure_regular_target(target)
        actual = sha(target.read_bytes()) if target.exists() else None
        if verify and actual not in (entry['before'], entry['after']):
            raise SetupError('A target file has changed since setup; automatic restore refused: ' + target.name)
        if entry['backup']:
            original = backup / entry['backup']
            if original.parent != backup or sha(original.read_bytes()) != entry['before']:
                raise SetupError('The backup is damaged.')
    for entry in reversed(state['files']):
        target = Path(entry['target'])
        current = sha(target.read_bytes()) if target.exists() else None
        if current == entry['before'] and (entry['mode'] is None or stat.S_IMODE(target.stat().st_mode) == entry['mode']):
            continue
        if entry['backup']:
            atomic_write(target, (backup / entry['backup']).read_bytes(), entry['mode'])
        else:
            target.unlink(missing_ok=True)
    for name in reversed(state.get('directories', [])):
        try:
            Path(name).rmdir()
        except OSError:
            pass
    state['status'] = 'restored'
    atomic_write(backup / 'restore.json', (json.dumps(state, indent=2) + '\n').encode(), 0o600)


def mac_launcher(game, binary, arguments):
    # Pass the inner binary directly, as in the upstream macOS launch wrapper.
    # No changes to the signed app bundle, system environment or Battle.net binary.
    tokens = shlex.split(arguments)
    command = ['/usr/bin/arch', '-x86_64', '/usr/bin/env',
               'DYLD_LIBRARY_PATH=' + str(game),
               'DYLD_INSERT_LIBRARIES=' + str(game / 'MelonLoader.Bootstrap.dylib'),
               str(binary), *tokens]
    return ('#!/bin/bash\nset -e\ncd -- ' + shlex.quote(str(game)) + '\nexec '
            + ' '.join(shlex.quote(token) for token in command) + ' "$@"\n').encode()



def configure(args):
    system = platform.system()
    if system not in {'Windows', 'Linux', 'Darwin'}:
        raise SetupError('Supported systems: Windows, Linux/Wine, and macOS.')
    game_value = args.game_dir or os.environ.get('GAME_DIR')
    if not game_value and sys.stdin.isatty():
        game_value = input('Hearthstone game directory (or Hearthstone.app on Mac): ').strip().strip('"')
    if not game_value:
        raise SetupError('--game-dir is required.')
    selected = Path(game_value).expanduser().resolve()
    native_mac = system == 'Darwin' and not (selected / 'Hearthstone.exe').is_file()
    binary = None
    if native_mac:
        if args.wine_prefix:
            raise SetupError('For Wine/CrossOver, select the directory containing Hearthstone.exe rather than a native .app.')
        app, binary = native_app(selected)
        game = app.parent.resolve()
        prefix = None
        target = 'macos'
        if platform.machine().lower() in {'arm64', 'aarch64'}:
            test = subprocess.run(['/usr/bin/arch', '-x86_64', '/usr/bin/true'], capture_output=True)
            if test.returncode:
                raise SetupError('Rosetta is required for the x64 Mac loader. No files have been changed.')
    else:
        game = selected
        if pe_machine(game / 'Hearthstone.exe') != 0x8664:
            raise SetupError('No x64 Hearthstone.exe found in the selected game directory.')
        if not (game / 'Hearthstone_Data/Managed/Assembly-CSharp.dll').is_file():
            raise SetupError('The Hearthstone Managed directory is missing.')
        prefix = Path(args.wine_prefix).expanduser().resolve() if args.wine_prefix else (infer_prefix(game) if system != 'Windows' else None)
        if system != 'Windows' and prefix is None:
            raise SetupError('Wine prefix not detected. Specify --wine-prefix.')
        target = 'windows'
    if prefix and (not (prefix / 'user.reg').is_file() or not (prefix / 'system.reg').is_file()):
        raise SetupError('Not a valid Wine/CrossOver prefix.')
    if system == 'Windows' and prefix:
        raise SetupError('Use --wine-prefix only when running on Linux or macOS.')
    configs = [Path(args.battle_config).expanduser().resolve()] if args.battle_config else find_battle_configs(system, prefix)
    if len(configs) > 1 and sys.stdin.isatty():
        print('Multiple Battle.net configurations:')
        for index, path in enumerate(configs, 1):
            print(f'  {index}. {path}')
        try:
            choice = int(input('Select a profile: '))
            if choice < 1:
                raise ValueError()
            configs = [configs[choice - 1]]
        except (ValueError, IndexError):
            raise SetupError('Invalid profile selection.')
    if len(configs) != 1:
        raise SetupError('Battle.net.config could not be identified uniquely. Specify --battle-config; Battle.net must have been launched at least once.')
    config_path = configs[0]
    raw = config_path.read_bytes()
    key = choose_game_key(checked_json(raw, 'Battle.net.config'), args.game_key, sys.stdin.isatty())
    corlib = game / 'MelonLoader/CustomCorlib-Unity6000'
    corlib_argument = wine_windows_path(corlib, prefix) if prefix else str(corlib)
    updated_config, arguments = update_battle_config(raw, key, corlib_argument)
    registry = None
    if prefix:
        registry = (prefix / 'user.reg').read_bytes()
        registry_updated = wine_registry_override(registry)
    else:
        registry_updated = None
    payload = payload_files()
    writes = {game / ('MelonLoader/' + name if name.startswith('CustomCorlib-Unity6000/') else name): data for name, data in payload.items()}
    writes[config_path] = updated_config
    if prefix:
        writes[prefix / 'user.reg'] = registry_updated
    if native_mac:
        writes[game / 'Start-BCM.command'] = mac_launcher(game, binary, arguments)
    for path in writes:
        ensure_regular_target(path)
    return {'game': game, 'prefix': prefix, 'native_mac': native_mac, 'target': target, 'writes': writes,
            'config': config_path, 'original_config': raw, 'original_registry': registry, 'key': key}


def install(args):
    plan = configure(args)
    print(f'Target: {"native macOS (experimental)" if plan["native_mac"] else "Windows under Wine/CrossOver" if plan["prefix"] else "Windows"}')
    print(f'MelonLoader {VERSION}; Battle.net entry: {plan["key"]}; five corlib DLLs and the mod.')
    if plan['prefix']:
        print('Wine: version=native,builtin, only for Hearthstone.exe.')
    if plan['native_mac']:
        print('macOS: Start-BCM.command loads MelonLoader. The normal Battle.net Play button does not automatically load the native loader.')
    if args.dry_run:
        print('Dry run: no downloads, file changes, or registry changes.')
        return None
    require_stopped(bool(plan['prefix']))
    with tempfile.TemporaryDirectory(prefix='bcm-loader-') as temporary:
        archive = Path(args.loader_archive).expanduser().resolve() if args.loader_archive else download_loader(Path(temporary), plan['target'])
        files = loader_files(archive, plan['target'])
        writes = {plan['game'] / name: data for name, data in files.items()}
        writes.update(plan['writes'])
        # Complete preflight before creating a backup or changing a target.
        for path in writes:
            ensure_regular_target(path)
        require_stopped(bool(plan['prefix']))
        if plan['config'].read_bytes() != plan['original_config']:
            raise SetupError('Battle.net.config changed during preparation. Run setup again.')
        if plan['prefix'] and (plan['prefix'] / 'user.reg').read_bytes() != plan['original_registry']:
            raise SetupError('The Wine registry changed during preparation. Run setup again.')
        transaction = Transaction(plan['game'])
        try:
            for path, data in writes.items():
                if path == plan['config'] and path.read_bytes() != plan['original_config']:
                    raise SetupError('Battle.net.config changed during installation.')
                if plan['prefix'] and path == plan['prefix'] / 'user.reg' and path.read_bytes() != plan['original_registry']:
                    raise SetupError('The Wine registry changed during installation.')
                transaction.write(path, data, executable=path.suffix in {'.sh', '.command', '.dylib'})
            transaction.finish()
        except BaseException:
            try:
                transaction.rollback()
            except Exception as rollback_error:
                print('Automatic rollback is incomplete: ' + type(rollback_error).__name__, file=sys.stderr)
                print('Backup: ' + str(transaction.root), file=sys.stderr)
            raise
    print('Setup complete. Backup: ' + str(transaction.root))
    if plan['native_mac']:
        print('Sign in to Battle.net, then launch Hearthstone using Start-BCM.command. This Hearthstone launch path has not yet been tested on macOS.')
    else:
        print('Open Battle.net and launch Hearthstone as usual.')
    return transaction.root


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game-dir', help='Game directory; Hearthstone.app is also accepted on macOS')
    parser.add_argument('--wine-prefix', help='Wine/CrossOver prefix if it cannot be inferred from the game path')
    parser.add_argument('--battle-config', help='Battle.net.config if it cannot be identified uniquely')
    parser.add_argument('--game-key', help='Games key for the desired Hearthstone edition')
    parser.add_argument('--loader-archive', help='Official MelonLoader 0.7.3 archive for offline installation')
    parser.add_argument('--dry-run', action='store_true', help='Validate and show the plan without changes or downloads')
    parser.add_argument('--restore', type=Path, help='Restore a backup; refuses to overwrite files changed since setup')
    args = parser.parse_args()
    try:
        if sys.version_info < (3, 10):
            raise SetupError('Python 3.10 or newer is required.')
        if args.restore:
            require_stopped(wine=platform.system() != 'Windows')
            restore(args.restore.expanduser().resolve())
            print('Backup restored.')
        else:
            install(args)
    except (SetupError, OSError, ValueError, zipfile.BadZipFile, subprocess.SubprocessError) as error:
        print('Setup stopped: ' + str(error), file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
