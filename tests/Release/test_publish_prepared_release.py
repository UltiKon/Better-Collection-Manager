import base64
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('publish', ROOT / 'tools/publish_prepared_release.py')
publish = importlib.util.module_from_spec(spec)
spec.loader.exec_module(publish)


class PreparedRelease(unittest.TestCase):
    def run_publish(self, changed_checksum=False, different_dll=False):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            metadata = root / 'src/BetterCollectionManager/ModMetadata.cs'
            metadata.parent.mkdir(parents=True)
            metadata.write_text('internal const string Version = "0.2.0";')
            manifest_path = root / '.github/release-assets/0.2.0.manifest'
            manifest_path.parent.mkdir(parents=True)
            dll = b'MZ BSJB BepInEx release fixture'
            assets = {'BetterCollectionManager.dll': dll}
            for loader, member in [('bepinex', 'BepInEx/plugins/BetterCollectionManager/BetterCollectionManager.dll'),
                                   ('shareable', 'Mods/BetterCollectionManager.dll')]:
                data = (dll + b'changed') if loader == 'bepinex' and different_dll else dll
                output = io.BytesIO()
                with zipfile.ZipFile(output, 'w') as archive:
                    archive.writestr(member, data)
                    archive.writestr('SHA256SUMS.txt', hashlib.sha256(data).hexdigest() + '  ' + member + '\n')
                assets[f'BetterCollectionManager-0.2.0-{loader}.zip'] = output.getvalue()
            manifest = {'tag': '0.2.0', 'title': 'Added PNG export', 'assets': []}
            blobs = {}
            for index, (name, data) in enumerate(assets.items()):
                sha = f'{index:040x}'
                blobs[sha] = data
                manifest['assets'].append({'name': name, 'blob': sha, 'size': len(data),
                                           'sha256': hashlib.sha256(data).hexdigest()})
            if changed_checksum:
                manifest['assets'][0]['sha256'] = '0' * 64
            manifest_path.write_text(json.dumps(manifest))

            def download(request, timeout):
                sha = request.full_url.rsplit('/', 1)[1]
                return io.BytesIO(json.dumps({'content': base64.b64encode(blobs[sha]).decode()}).encode())

            environment = root / 'github-env'
            with patch.object(publish, '__file__', str(root / 'tools/publish_prepared_release.py')), \
                    patch.dict(os.environ, GH_TOKEN='fixture', GH_REPO='fixture/repo', GITHUB_ENV=str(environment)), \
                    patch.object(publish.urllib.request, 'urlopen', side_effect=download):
                publish.main()
            self.assertEqual((root / 'release-assets/BetterCollectionManager.dll').read_bytes(), dll)
            self.assertIn('RELEASE_TITLE=Added PNG export', environment.read_text())

    def test_verified_assets_and_release_metadata(self):
        self.run_publish()

    def test_modified_download_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'checksum mismatch'):
            self.run_publish(changed_checksum=True)

    def test_standalone_dll_must_match_bepinex_package(self):
        with self.assertRaisesRegex(ValueError, 'differs'):
            self.run_publish(different_dll=True)


if __name__ == '__main__':
    unittest.main()
