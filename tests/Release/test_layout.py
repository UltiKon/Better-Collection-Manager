from pathlib import Path
import os
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]


class RepositoryLayout(unittest.TestCase):
    def test_projects_reference_existing_sources_and_resources(self):
        for project in ROOT.rglob('*.csproj'):
            if any(part in {'obj', 'bin'} for part in project.relative_to(ROOT).parts):
                continue
            for entry in ET.parse(project).iter():
                tag = entry.tag.rsplit('}', 1)[-1]
                attribute = 'Project' if tag == 'Import' else 'Include'
                if tag not in {'Compile', 'EmbeddedResource', 'Import'}:
                    continue
                value = entry.get(attribute, '')
                if not value or '$' in value or '*' in value or value in {'Sdk.props', 'Sdk.targets'}:
                    continue
                with self.subTest(project=project.relative_to(ROOT), reference=value):
                    self.assertTrue((project.parent / value.replace('\\', '/')).is_file())

    def test_production_build_outputs_are_under_artifacts(self):
        for project in (ROOT / 'src/BetterCollectionManager').glob('*.csproj'):
            for entry in ET.parse(project).iter():
                if entry.tag.rsplit('}', 1)[-1] == 'OutputPath':
                    value = entry.text.replace('\\', '/').replace('$(Configuration)', 'Release')
                    with self.subTest(project=project.name):
                        self.assertTrue((project.parent / value).resolve().is_relative_to(ROOT / 'artifacts'))

    def test_root_contains_no_loose_source_or_catalog_files(self):
        self.assertEqual([p.name for p in ROOT.iterdir() if p.is_file()
                          and p.suffix in {'.cs', '.csproj', '.props', '.json', '.cfg', '.sh'}], [])

    @unittest.skipUnless(os.name == 'posix', 'Bash build wrappers require a Unix shell')
    def test_build_wrappers_resolve_projects_from_another_directory(self):
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory)
            game = base / 'game with spaces'
            managed = game / 'Hearthstone_Data/Managed'
            managed.mkdir(parents=True)
            for name in ('Assembly-CSharp', 'Blizzard.T5.Game', 'Blizzard.T5.AssetManager',
                         'Blizzard.T5.AssetLoader', 'Blizzard.T5.ServiceLocator', 'Blizzard.T5.Core',
                         'Blizzard.T5.UberText.Runtime', 'blizzard.bgsclient', 'UnityEngine', 'UnityEngine.UI',
                         'UnityEngine.CoreModule', 'UnityEngine.IMGUIModule', 'UnityEngine.AnimationModule',
                         'UnityEngine.ImageConversionModule', 'UnityEngine.TextRenderingModule',
                         'UnityEngine.PhysicsModule', 'UnityEngine.InputLegacyModule'):
                (managed / (name + '.dll')).touch()
            for folder, names in [('BepInEx/core', ['BepInEx.dll', '0Harmony.dll']),
                                  ('MelonLoader/net35', ['MelonLoader.dll', '0Harmony.dll'])]:
                target = game / folder
                target.mkdir(parents=True)
                for name in names:
                    (target / name).touch()
            binary = base / 'dotnet'
            binary.write_text('#!/usr/bin/env bash\nset -eu\n'
                              'if [[ "$1" == "--version" ]]; then echo fixture; exit 0; fi\n'
                              '[[ "$1" == "build" && -f "$2" && -f README.md ]]\n'
                              'printf "%s\\n" "$2"\n')
            binary.chmod(0o755)
            env = dict(os.environ, GAME_DIR=str(game), PATH=str(base) + os.pathsep + os.environ['PATH'])
            for loader, project in [('bepinex', 'BepInEx'), ('linux', 'Linux')]:
                with self.subTest(loader=loader):
                    result = subprocess.run(['bash', str(ROOT / 'tools' / ('build-' + loader + '.sh'))],
                                            cwd=base, env=env, text=True, capture_output=True)
                    self.assertEqual(result.returncode, 0, result.stderr)
                    self.assertIn('src/BetterCollectionManager/BetterCollectionManager.' + project + '.csproj', result.stdout)


if __name__ == '__main__':
    unittest.main()
