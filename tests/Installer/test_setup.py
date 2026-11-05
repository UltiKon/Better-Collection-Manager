import argparse
import contextlib
import importlib.util
import io
import hashlib
import json
from pathlib import Path
import plistlib
import struct
import tempfile
import unittest
from unittest.mock import patch

REPO = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('bcm_setup', REPO / 'installer/setup.py')
setup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(setup)


def parameters(game, **kwargs):
    values = dict(game_dir=str(game), wine_prefix=None, battle_config=None, game_key=None,
                  loader_archive=str(REPO / 'artifacts/startup-investigation/MelonLoader-0.7.3-x64.zip'), dry_run=False)
    values.update(kwargs)
    return argparse.Namespace(**values)


def fixture_game(base):
    prefix = base / 'prefix'
    game = prefix / 'drive_c/Games/Hearthstone'
    game.mkdir(parents=True)
    (prefix / 'dosdevices').mkdir()
    (prefix / 'dosdevices/c:').symlink_to(prefix / 'drive_c', target_is_directory=True)
    (prefix / 'dosdevices/z:').symlink_to(base.parent, target_is_directory=True)
    (prefix / 'system.reg').write_text('WINE REGISTRY Version 2\n')
    (prefix / 'user.reg').write_text('WINE REGISTRY Version 2\n\n[Software\\\\Other] 1\n"enabled"="yes"\n')
    raw = bytearray(256)
    raw[:2] = b'MZ'
    struct.pack_into('<I', raw, 0x3c, 0x80)
    raw[0x80:0x84] = b'PE\0\0'
    struct.pack_into('<H', raw, 0x84, 0x8664)
    (game / 'Hearthstone.exe').write_bytes(raw)
    (game / 'Hearthstone_Data/Managed').mkdir(parents=True)
    (game / 'Hearthstone_Data/Managed/Assembly-CSharp.dll').write_bytes(b'fixture')
    config = prefix / 'drive_c' / 'users' / 'player' / 'AppData/Roaming/Battle.net/Battle.net.config'
    config.parent.mkdir(parents=True)
    config.write_text(json.dumps({'Client': {'Language': 'deDE', 'Unrelated': {'Setting': 'keep'}},
                                  'Games': {'hs_beta': {'AdditionalLaunchArguments': '-force-d3d11', 'Region': 'EU'},
                                            'wow': {'AdditionalLaunchArguments': '-other'}}}))
    return prefix, game, config


class Arguments(unittest.TestCase):
    def test_preserves_other_arguments_and_idempotent(self):
        target = str(Path('MelonLoader') / 'Custom Corlib')
        value = setup.merge_launch_arguments('-force-d3d11 --label="some text"', target)
        self.assertTrue(value.startswith('-force-d3d11 --label="some text" '))
        self.assertEqual(value, setup.merge_launch_arguments(value, target))
        self.assertEqual(value.count(setup.OPTION), 1)

    def test_replaces_duplicate_and_space_form(self):
        old = f'{setup.OPTION}="old path" -keep {setup.OPTION} "another path"'
        result = setup.merge_launch_arguments(old, 'replacement')
        self.assertEqual(result, '-keep ' + setup.OPTION + '="replacement"')

    def test_refuses_malformed_existing_option(self):
        with self.assertRaises(setup.SetupError):
            setup.merge_launch_arguments(setup.OPTION + '="unclosed', 'new')

    def test_refuses_unsafe_path_characters(self):
        for value in ('bad"path', 'bad\npath', 'bad\0path'):
            with self.assertRaises(setup.SetupError):
                setup.merge_launch_arguments('', value)

    def test_merges_config_and_keeps_foreign_settings(self):
        raw = b'\xef\xbb\xbf' + json.dumps({'Client': {'PrivateSetting': 'keep'}, 'Games': {'hs_beta': {'Region': 'EU'}, 'wow': {'keep': 123}}}).encode()
        result, _ = setup.update_battle_config(raw, 'hs_beta', 'corlib')
        new = setup.checked_json(result, 'fixture')
        self.assertTrue(result.startswith(b'\xef\xbb\xbf'))
        self.assertEqual(new['Client'], {'PrivateSetting': 'keep'})
        self.assertEqual(new['Games']['wow'], {'keep': 123})
        self.assertEqual(new['Games']['hs_beta']['Region'], 'EU')
        self.assertEqual(setup.update_battle_config(result, 'hs_beta', 'corlib')[0], result)

    def test_refuses_duplicate_json_keys(self):
        with self.assertRaises(setup.SetupError):
            setup.checked_json(b'{"Games":{},"Games":{}}', 'fixture')

    def test_game_selection_does_not_guess(self):
        self.assertEqual(setup.choose_game_key({'Games': {'hs_beta': {}, 'wow': {}}}), 'hs_beta')
        for config in ({'Games': {'wow': {}}}, {'Games': {'hs': {}, 'hs_beta': {}}}, {'Client': {}}):
            with self.assertRaises(setup.SetupError):
                setup.choose_game_key(config)
        self.assertEqual(setup.choose_game_key({'Games': {}}, 'hs_beta'), 'hs_beta')


class WineAndMac(unittest.TestCase):
    def test_registry_preserves_other_sections_and_idempotent(self):
        raw = b'WINE REGISTRY Version 2\n\n[Software\\\\Other] 1\n"version"="builtin"\n'
        result = setup.wine_registry_override(raw)
        self.assertIn(b'"version"="builtin"', result)
        self.assertIn(b'"version"="native,builtin"', result)
        self.assertEqual(setup.wine_registry_override(result), result)

    def test_registry_updates_only_app_specific_value(self):
        raw = ('WINE REGISTRY Version 2\r\n\r\n[' + setup.WINE_KEY + '] 1\r\n"version"="builtin"\r\n"other"="keep"\r\n\r\n[Unrelated] 2\r\n"version"="builtin"\r\n').encode()
        result = setup.wine_registry_override(raw)
        self.assertIn(b'"other"="keep"\r\n', result)
        self.assertIn(b'[Unrelated] 2\r\n"version"="builtin"', result)
        self.assertEqual(result.count(b'"version"="native,builtin"'), 1)
        self.assertEqual(setup.wine_registry_override(result), result)

    def test_refuses_invalid_registry(self):
        with self.assertRaises(setup.SetupError):
            setup.wine_registry_override(b'not a registry')

    def test_prefix_and_drive_mapping(self):
        with tempfile.TemporaryDirectory() as temp:
            prefix, game, config = fixture_game(Path(temp))
            self.assertEqual(setup.infer_prefix(game), prefix)
            self.assertEqual(setup.wine_windows_path(game / 'MelonLoader/CustomCorlib-Unity6000', prefix),
                             'C:' + '\\Games\\Hearthstone\\MelonLoader\\CustomCorlib-Unity6000')
            self.assertEqual(setup.find_battle_configs('Linux', prefix), [config])

    def test_unknown_drive_mapping_refused(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp)
            (base / 'dosdevices').mkdir()
            with self.assertRaises(setup.SetupError):
                setup.wine_windows_path(base / 'elsewhere', base)

    def test_mac_launcher_quotes_paths_and_arguments(self):
        with tempfile.TemporaryDirectory() as temp:
            game = Path(temp) / 'A game $(touch NOT-EXECUTED)'
            script = setup.mac_launcher(game, game / 'Hearthstone.app/Contents/MacOS/Hearthstone', '-launch --title="some text"')
            self.assertIn(b"'--title=some text'", script)
            self.assertIn(b'/usr/bin/arch -x86_64 /usr/bin/env', script)
            self.assertNotIn(b'eval ', script)

    def test_macos_layout_and_x64_check(self):
        with tempfile.TemporaryDirectory() as temp:
            game = Path(temp)
            app = game / 'Hearthstone.app'
            binary = app / 'Contents/MacOS/Hearthstone'
            binary.parent.mkdir(parents=True)
            binary.write_bytes(b'\xcf\xfa\xed\xfe' + struct.pack('<I', 0x01000007))
            (app / 'Contents/Info.plist').write_bytes(plistlib.dumps({'CFBundleExecutable': 'Hearthstone'}))
            managed = app / 'Contents/Resources/Data/Managed/Assembly-CSharp.dll'
            managed.parent.mkdir(parents=True)
            managed.write_bytes(b'fixture')
            self.assertEqual(setup.native_app(game), (app, binary))
            binary.write_bytes(b'\xcf\xfa\xed\xfe' + struct.pack('<I', 0x0100000c))
            with self.assertRaises(setup.SetupError):
                setup.native_app(game)

    def test_running_process_guard(self):
        result = argparse.Namespace(stdout='Hearthstone.exe\n')
        with patch.object(setup.platform, 'system', return_value='Linux'), patch.object(setup.subprocess, 'run', return_value=result):
            with self.assertRaises(setup.SetupError):
                setup.require_stopped()
        result.stdout = 'wineserver\n'
        with patch.object(setup.platform, 'system', return_value='Linux'), patch.object(setup.subprocess, 'run', return_value=result):
            with self.assertRaises(setup.SetupError):
                setup.require_stopped(wine=True)


class Installation(unittest.TestCase):
    def setUp(self):
        # Installation/rollback tests must also work in a clean source checkout.
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.payload_root = Path(temporary.name)
        self.payload = {'Mods/BetterCollectionManager.dll': b'MZ\0BSJB mod fixture'}
        self.payload.update({'CustomCorlib-Unity6000/' + name: b'MZ\0BSJB ' + name.encode()
                             for name in setup.CORLIBS})
        manifest = {}
        for name, data in self.payload.items():
            path = self.payload_root / 'payload' / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
            manifest['payload/' + name] = hashlib.sha256(data).hexdigest()
        (self.payload_root / 'payload.json').write_text(json.dumps({'files': manifest}))
        root_patch = patch.object(setup, 'ROOT', self.payload_root)
        root_patch.start()
        self.addCleanup(root_patch.stop)

    def test_payload_and_pinned_archives(self):
        payload = setup.payload_files()
        self.assertEqual(len(payload), 6)
        archives = [('windows', REPO / 'artifacts/startup-investigation/MelonLoader-0.7.3-x64.zip'),
                    ('macos', REPO / 'artifacts/corlib-audit/MelonLoader.macOS.x64.zip')]
        for kind, archive in archives:
            if archive.exists():
                files = setup.loader_files(archive, kind)
                self.assertIn('MelonLoader/net35/MelonLoader.dll', files)
                self.assertFalse(any(name.endswith(('.pdb', '.mdb')) for name in files))

    def test_tampered_payload_refused(self):
        (self.payload_root / 'payload/Mods/BetterCollectionManager.dll').write_bytes(b'tampered')
        with self.assertRaisesRegex(setup.SetupError, 'checksum mismatch'):
            setup.payload_files()

    def test_tampered_archive_refused(self):
        with tempfile.TemporaryDirectory() as temp:
            archive = Path(temp) / 'invalid.zip'
            archive.write_bytes(b'not the official release')
            with self.assertRaises(setup.SetupError):
                setup.loader_files(archive, 'windows')

    def test_transaction_restores_originals_and_removes_only_our_files(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            original = root / 'original.txt'
            original.write_bytes(b'original')
            transaction = setup.Transaction(root)
            transaction.write(original, b'changed')
            transaction.write(root / 'new/subdir/mod.dll', b'new')
            transaction.finish()
            (root / 'unrelated.txt').write_bytes(b'keep')
            setup.restore(transaction.root)
            self.assertEqual(original.read_bytes(), b'original')
            self.assertFalse((root / 'new').exists())
            self.assertEqual((root / 'unrelated.txt').read_bytes(), b'keep')

    def test_restore_refuses_subsequent_changes(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            original = root / 'original.txt'
            original.write_bytes(b'original')
            transaction = setup.Transaction(root)
            transaction.write(original, b'installed')
            transaction.finish()
            original.write_bytes(b'later user changes')
            with self.assertRaises(setup.SetupError):
                setup.restore(transaction.root)
            self.assertEqual(original.read_bytes(), b'later user changes')

    def test_installer_end_to_end_and_restore(self):
        archive = REPO / 'artifacts/startup-investigation/MelonLoader-0.7.3-x64.zip'
        if not archive.exists():
            self.skipTest('Official Windows archive not present')
        with tempfile.TemporaryDirectory() as temp:
            prefix, game, config = fixture_game(Path(temp))
            before_config, before_registry = config.read_bytes(), (prefix / 'user.reg').read_bytes()
            (game / 'version.dll').write_bytes(b'previous proxy')
            with patch.object(setup.platform, 'system', return_value='Linux'), patch.object(setup, 'require_stopped'), contextlib.redirect_stdout(io.StringIO()):
                backup = setup.install(parameters(game))
            self.assertEqual((game / 'Mods/BetterCollectionManager.dll').read_bytes(), self.payload['Mods/BetterCollectionManager.dll'])
            for name in setup.CORLIBS:
                self.assertEqual((game / 'MelonLoader/CustomCorlib-Unity6000' / name).read_bytes(), self.payload['CustomCorlib-Unity6000/' + name])
            installed = setup.checked_json(config.read_bytes(), 'fixture')
            self.assertEqual(installed['Games']['wow']['AdditionalLaunchArguments'], '-other')
            self.assertEqual(installed['Client']['Language'], 'deDE')
            self.assertTrue(installed['Games']['hs_beta']['AdditionalLaunchArguments'].startswith('-force-d3d11 '))
            self.assertIn(b'"version"="native,builtin"', (prefix / 'user.reg').read_bytes())
            setup.restore(backup)
            self.assertEqual(config.read_bytes(), before_config)
            self.assertEqual((prefix / 'user.reg').read_bytes(), before_registry)
            self.assertEqual((game / 'version.dll').read_bytes(), b'previous proxy')
            self.assertFalse((game / 'Mods/BetterCollectionManager.dll').exists())

    def test_install_failure_rolls_back_files_and_configuration(self):
        with tempfile.TemporaryDirectory() as temp:
            prefix, game, config = fixture_game(Path(temp))
            before = config.read_bytes()
            original_write = setup.Transaction.write
            counter = []
            def fail_on_config(transaction, path, data, executable=False):
                original_write(transaction, path, data, executable)
                counter.append(path)
                if path == config:
                    raise OSError('simulated failure')
            minimal = {'version.dll': b'loader', 'MelonLoader/net35/MelonLoader.dll': b'fixture'}
            with patch.object(setup.platform, 'system', return_value='Linux'), patch.object(setup, 'require_stopped'), patch.object(setup, 'loader_files', return_value=minimal), patch.object(setup.Transaction, 'write', fail_on_config), contextlib.redirect_stdout(io.StringIO()):
                with self.assertRaises(OSError):
                    setup.install(parameters(game))
            self.assertGreater(len(counter), 2)
            self.assertEqual(config.read_bytes(), before)
            self.assertFalse((game / 'version.dll').exists())
            self.assertFalse((game / 'Mods').exists())

    def test_dry_run_never_downloads_or_writes(self):
        with tempfile.TemporaryDirectory() as temp:
            prefix, game, config = fixture_game(Path(temp))
            before = {p: p.read_bytes() for p in Path(temp).rglob('*') if p.is_file()}
            with patch.object(setup.platform, 'system', return_value='Linux'), patch.object(setup, 'download_loader', side_effect=AssertionError('download')), contextlib.redirect_stdout(io.StringIO()):
                setup.install(parameters(game, dry_run=True))
            after = {p: p.read_bytes() for p in Path(temp).rglob('*') if p.is_file()}
            self.assertEqual(before, after)
            self.assertFalse((game / 'BCM-Setup-Backups').exists())

    def test_native_windows_branch_preserves_settings_without_registry_changes(self):
        with tempfile.TemporaryDirectory() as temp:
            prefix, game, config = fixture_game(Path(temp))
            registry_before = (prefix / 'user.reg').read_bytes()
            # Use the actual APPDATA parent of Battle.net in this fixture.
            appdata = config.parent.parent
            minimal = {'version.dll': b'loader'}
            with patch.object(setup.platform, 'system', return_value='Windows'), patch.dict(setup.os.environ, {'APPDATA': str(appdata)}), patch.object(setup, 'require_stopped'), patch.object(setup, 'loader_files', return_value=minimal), contextlib.redirect_stdout(io.StringIO()):
                backup = setup.install(parameters(game))
            self.assertEqual((prefix / 'user.reg').read_bytes(), registry_before)
            self.assertEqual(setup.checked_json(config.read_bytes(), 'fixture')['Games']['wow']['AdditionalLaunchArguments'], '-other')
            self.assertTrue((game / 'Mods/BetterCollectionManager.dll').is_file())
            setup.restore(backup)

    def test_native_macos_installation_and_restore(self):
        archive = REPO / 'artifacts/corlib-audit/MelonLoader.macOS.x64.zip'
        if not archive.exists():
            self.skipTest('Official macOS archive not present')
        with tempfile.TemporaryDirectory() as temp:
            game = Path(temp) / 'Native Hearthstone'
            app = game / 'Hearthstone.app'
            binary = app / 'Contents/MacOS/Hearthstone'
            binary.parent.mkdir(parents=True)
            binary.write_bytes(b'\xcf\xfa\xed\xfe' + struct.pack('<I', 0x01000007))
            binary.chmod(0o755)
            (app / 'Contents/Info.plist').write_bytes(plistlib.dumps({'CFBundleExecutable': 'Hearthstone'}))
            managed = app / 'Contents/Resources/Data/Managed/Assembly-CSharp.dll'
            managed.parent.mkdir(parents=True)
            managed.write_bytes(b'fixture')
            config = Path(temp) / 'Battle.net.config'
            config.write_text(json.dumps({'Games': {'hs_beta': {'AdditionalLaunchArguments': '-launch'}}}))
            before = {p: p.read_bytes() for p in app.rglob('*') if p.is_file()}
            with patch.object(setup.platform, 'system', return_value='Darwin'), patch.object(setup.platform, 'machine', return_value='x86_64'), patch.object(setup, 'require_stopped'), contextlib.redirect_stdout(io.StringIO()):
                backup = setup.install(parameters(app, loader_archive=str(archive), battle_config=str(config)))
            self.assertTrue((game / 'MelonLoader.Bootstrap.dylib').is_file())
            self.assertTrue((game / 'Start-BCM.command').stat().st_mode & 0o111)
            self.assertFalse((game / 'version.dll').exists())
            self.assertEqual(before, {p: p.read_bytes() for p in app.rglob('*') if p.is_file()})
            setup.restore(backup)
            self.assertFalse((game / 'Start-BCM.command').exists())
            self.assertEqual(before, {p: p.read_bytes() for p in app.rglob('*') if p.is_file()})

    def test_macos_crossover_uses_windows_loader(self):
        with tempfile.TemporaryDirectory() as temp:
            prefix, game, config = fixture_game(Path(temp))
            with patch.object(setup.platform, 'system', return_value='Darwin'):
                plan = setup.configure(parameters(game))
            self.assertEqual(plan['target'], 'windows')
            self.assertEqual(plan['prefix'], prefix)
            self.assertFalse(plan['native_mac'])
            self.assertNotIn(game / 'Start-BCM.command', plan['writes'])

    def test_rollback_skips_unmodified_original_when_writer_failed(self):
        with tempfile.TemporaryDirectory() as temp:
            game = Path(temp)
            original = game / 'existing'
            original.write_bytes(b'original')
            transaction = setup.Transaction(game)
            transaction.write(game / 'new.dll', b'installed')
            writer = setup.atomic_write
            def fail_target(path, data, mode=None):
                if path == original:
                    raise PermissionError('fixture')
                writer(path, data, mode)
            with patch.object(setup, 'atomic_write', fail_target):
                with self.assertRaises(PermissionError):
                    transaction.write(original, b'new')
                transaction.rollback()
            self.assertFalse((game / 'new.dll').exists())
            self.assertEqual(original.read_bytes(), b'original')

    def test_symlink_targets_refused(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            outside = root / 'outside'
            outside.mkdir()
            (root / 'link').symlink_to(outside, target_is_directory=True)
            with self.assertRaises(setup.SetupError):
                setup.ensure_regular_target(root / 'link/newfile')
            (root / 'target').symlink_to(outside / 'file')
            with self.assertRaises(setup.SetupError):
                setup.ensure_regular_target(root / 'target')


if __name__ == '__main__':
    unittest.main()
