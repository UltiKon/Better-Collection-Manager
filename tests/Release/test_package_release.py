import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]


def module(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'tools' / (name + '.py'))
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


release = module('package_release')
docs = module('check_docs')


class Packaging(unittest.TestCase):
    def test_root_readme_links_resolve_for_both_loaders(self):
        sources = {'Source/' + p.relative_to(ROOT).as_posix(): p.read_bytes() for p in release.source_files()}
        for readme in ('docs/README-BEPINEX.md', 'docs/DISTRIBUTION.md'):
            with self.subTest(readme=readme):
                contents = dict(sources, **{'README.md': release.distribution_readme(readme)})
                self.assertEqual(docs.check_links(contents), [])
                headings = docs.anchors(contents['README.md'].decode())
                self.assertIn('abstract', headings)

    def test_relocation_preserves_external_links_and_anchors(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'README.md').write_text('[local](USER-GUIDE.md#search) [anchor](#here) '
                                          '[web](https://example.org/guide)')
            with patch.object(release, 'ROOT', root):
                text = release.distribution_readme('README.md').decode()
            self.assertIn('(Source/USER-GUIDE.md#search)', text)
            self.assertIn('(#here)', text)
            self.assertIn('(https://example.org/guide)', text)

    def test_source_selection_keeps_docs_and_excludes_local_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name in ('README.md', 'src/BetterCollectionManager/ModMetadata.cs', 'config/BetterCollectionManager.cfg',
                         'tests/Release/test_fixture.py', 'tests/Sample/bin/private.cs',
                         'tools/__pycache__/private.py', 'local.dll', 'private.log', '.git/config'):
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text('fixture')
            with patch.object(release, 'ROOT', root):
                names = {p.relative_to(root).as_posix() for p in release.source_files()}
            self.assertEqual(names, {'README.md', 'src/BetterCollectionManager/ModMetadata.cs', 'config/BetterCollectionManager.cfg',
                                     'tests/Release/test_fixture.py'})

    def test_zip_entries_are_reproducible_and_reject_parent_paths(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'fixture.zip'
            with zipfile.ZipFile(path, 'w') as archive:
                release.add_entry(archive, 'Source/README.md', b'fixture')
                with self.assertRaises(ValueError):
                    release.add_entry(archive, '../private', b'fixture')
            with zipfile.ZipFile(path) as archive:
                self.assertEqual(archive.getinfo('Source/README.md').date_time, (2026, 1, 1, 0, 0, 0))
                self.assertIsNone(archive.testzip())


if __name__ == '__main__':
    unittest.main()
