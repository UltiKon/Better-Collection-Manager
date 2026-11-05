from pathlib import Path
import os
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


@unittest.skipUnless(os.name == 'posix', 'Bash release wrapper requires a Unix shell')
class CoordinatedRelease(unittest.TestCase):
    def run_release(self, fail=''):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            tools = root / 'tools'
            tools.mkdir()
            shutil.copyfile(ROOT / 'tools/build-release.sh', tools / 'build-release.sh')
            for name, stage in [('build-bepinex.sh', 'bepinex'), ('build-linux.sh', 'melonloader')]:
                (tools / name).write_text('#!/usr/bin/env bash\n'
                                         'echo ' + stage + ' >> "$TRACE"\n'
                                         '[[ "$FAIL" != "' + stage + '" ]]\n')
            binaries = root / 'fake-bin'
            binaries.mkdir()
            for name, stage in [('dotnet', 'verify'), ('python3', 'package')]:
                script = binaries / name
                script.write_text('#!/usr/bin/env bash\n'
                                  'echo "' + stage + ' $*" >> "$TRACE"\n'
                                  '[[ "$FAIL" != "' + stage + '" ]]\n')
                script.chmod(0o755)
            trace = root / 'trace'
            env = dict(os.environ, TRACE=str(trace), FAIL=fail,
                       GAME_DIR=str(root / 'game with spaces'),
                       PATH=str(binaries) + os.pathsep + os.environ['PATH'])
            # Run outside the fixture repository to exercise path resolution.
            result = subprocess.run(['bash', str(tools / 'build-release.sh')],
                                    cwd=tempfile.gettempdir(), env=env, capture_output=True, text=True)
            return result.returncode, trace.read_text().splitlines()

    def test_both_builds_and_verification_precede_packaging(self):
        code, steps = self.run_release()
        self.assertEqual(code, 0)
        self.assertEqual(steps[:2], ['bepinex', 'melonloader'])
        self.assertTrue(steps[2].startswith('verify run --project tests/BepInEx/BepInEx.csproj'))
        self.assertIn('artifacts/bepinex/BetterCollectionManager.dll', steps[2])
        self.assertIn('artifacts/melonloader/BetterCollectionManager.dll', steps[2])
        self.assertIn('--loader bepinex', steps[3])
        self.assertIn('--loader melonloader', steps[4])
        self.assertEqual(len(steps), 5)

    def test_failed_build_never_packages_a_previous_dll(self):
        for stage in ('bepinex', 'melonloader'):
            with self.subTest(stage=stage):
                code, steps = self.run_release(stage)
                self.assertNotEqual(code, 0)
                self.assertFalse(any(step.startswith(('verify ', 'package ')) for step in steps))

    def test_failed_metadata_verification_never_packages(self):
        code, steps = self.run_release('verify')
        self.assertNotEqual(code, 0)
        self.assertFalse(any(step.startswith('package ') for step in steps))


if __name__ == '__main__':
    unittest.main()
