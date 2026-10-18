"""Offline behavioral tests; no vendor framework or real image processing."""
import contextlib
import io
import json
import os
import plistlib
import struct
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
import scan_3fr_sequences as scan
import run_local_bridge as runner
from inspect_output import inspect_tiff


def call(module, arguments):
    with patch.object(sys, 'argv', ['script', *map(str, arguments)]), contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
        return module.main()


class WorkflowTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.base = Path(self.tmp.name)
        self.raw = self.base / 'raw'
        self.raw.mkdir()
        self.frames = []
        for i in range(8):
            p = self.raw / f'{i}.3FR'
            p.write_bytes(f'frame {i}'.encode())
            os.utime(p, (1000 + i, 1000 + i))
            self.frames.append(p)
        self.app = self.base / 'Phocus.app'
        (self.app / 'Contents').mkdir(parents=True)
        with (self.app / 'Contents/Info.plist').open('wb') as f:
            plistlib.dump({'CFBundleShortVersionString': 'fixture'}, f)
        self.bridge = self.base / 'fixture-bridge'
        self.bridge.write_text(f'#!{sys.executable}\nimport sys\nfrom pathlib import Path\nPath(sys.argv[2]).write_bytes(b"intermediate")\n')
        self.bridge.chmod(0o700)

    def manifest(self):
        grouping = self.base / 'groups.json'
        grouping.write_text(json.dumps([[p.name for p in self.frames[:4]], [p.name for p in self.frames[4:]]]))
        path = self.base / 'manifest.json'
        with patch.object(scan, 'exif_times', return_value=({}, [])):
            self.assertEqual(call(scan, [self.raw, '--manifest', path, '--groups-file', grouping]), 0)
        return path

    def run_bridge(self, manifest, *extra):
        args = [manifest, '--output-root', self.base / 'out', '--bridge', self.bridge,
                '--bridge-sha256', scan.sha256(self.bridge), '--phocus-app', self.app, *extra]
        with patch.object(runner.platform, 'system', return_value='Darwin'):
            return call(runner, args)

    def record(self):
        return json.loads((self.base / 'out/run.json').read_text())

    def test_ambiguous_eight_not_split(self):
        with patch.object(scan, 'exif_times', return_value=({}, [])):
            self.assertEqual(call(scan, [self.raw, '-o', self.base / 'm.json']), 3)
        data = json.loads((self.base / 'm.json').read_text())
        self.assertEqual([g['frames'] for g in data['groups']], [8])
        self.assertFalse(data['groups'][0]['selected'])

    def test_exact_candidate_requires_confirmation(self):
        for p in self.frames[4:]:
            p.unlink()
        with patch.object(scan, 'exif_times', return_value=({}, [])):
            call(scan, [self.raw, '-o', self.base / 'm.json'])
            call(scan, [self.raw, '-o', self.base / 'yes.json', '--confirm-group', 'group-001'])
        self.assertFalse(json.loads((self.base / 'm.json').read_text())['groups'][0]['selected'])
        self.assertTrue(json.loads((self.base / 'yes.json').read_text())['groups'][0]['selected'])

    def test_partial_exif_uses_mtime_for_all(self):
        partial = {str(self.frames[0]): (900000, 'exif-wall-clock')}
        with patch.object(scan, 'exif_times', return_value=(partial, [])):
            call(scan, [self.raw, '-o', self.base / 'm.json'])
        data = json.loads((self.base / 'm.json').read_text())
        self.assertEqual({r['timeSource'] for g in data['groups'] for r in g['files']}, {'mtime'})

    def test_exif_partial_failure_and_subseconds(self):
        rows = [{'SourceFile': str(self.frames[0]), 'DateTimeOriginal': '2026:10:17 12:00:00', 'SubSecTimeOriginal': '123', 'OffsetTimeOriginal': '+08:00'}, {'SourceFile': str(self.frames[1]), 'Error': 'bad file'}]
        result = type('Result', (), {'stdout': json.dumps(rows), 'stderr': 'one bad file', 'returncode': 1})()
        with patch.object(scan.shutil, 'which', return_value='/fixture/exiftool'), patch.object(scan.subprocess, 'run', return_value=result) as invoked:
            times, warnings = scan.exif_times(self.frames)
        self.assertEqual(len(times), 1)
        self.assertAlmostEqual(times[str(self.frames[0].resolve())][0] % 1, .123, places=5)
        self.assertTrue(warnings)
        self.assertIn('-@', invoked.call_args.args[0])

    def test_duplicate_explicit_input_rejected(self):
        p = self.base / 'bad.json'
        p.write_text(json.dumps([['0.3FR'] * 4]))
        with patch.object(scan, 'exif_times', return_value=({}, [])), self.assertRaises(SystemExit):
            call(scan, [self.raw, '--groups-file', p])

    def test_success_and_environment_isolation(self):
        with patch.dict(os.environ, {'PHOCUS_UNKNOWN': 'unsafe'}):
            self.assertEqual(self.run_bridge(self.manifest()), 0)
        data = self.record()
        self.assertEqual(data['phocusVersion'], 'fixture')
        self.assertEqual(len(data['groups']), 2)
        self.assertIn('PHOCUS_UNKNOWN', data['clearedInheritedEnvironment'])
        self.assertEqual(data['groups'][0]['environment'], {})
        self.assertEqual(data['status'], 'succeeded')

    def test_failure_continues_and_binary_logs_survive(self):
        self.bridge.write_text(f'#!{sys.executable}\nimport os, sys\nos.write(1,b"\\xff\\xfe")\nsys.exit(7)\n')
        self.assertEqual(self.run_bridge(self.manifest()), 1)
        data = self.record()
        self.assertEqual([g['exitCode'] for g in data['groups']], [7, 7])
        self.assertEqual((self.base / 'out/group-001/stdout.log').read_bytes(), b'\xff\xfe')
        self.assertIn('finishedAt', data)

    def test_changed_input_not_executed_but_next_group_runs(self):
        manifest = self.manifest()
        self.frames[0].write_bytes(b'changed')
        self.assertEqual(self.run_bridge(manifest), 1)
        groups = self.record()['groups']
        self.assertIn('Input changed', groups[0]['error'])
        self.assertNotIn('command', groups[0])
        self.assertEqual(groups[1]['status'], 'succeeded')

    def test_timeout_leaves_record(self):
        self.bridge.write_text(f'#!{sys.executable}\nimport time\ntime.sleep(30)\n')
        self.assertEqual(self.run_bridge(self.manifest(), '--timeout-seconds', '.1'), 1)
        self.assertTrue(all(g['timedOut'] for g in self.record()['groups']))

    def test_wrong_bridge_hash_refused(self):
        with self.assertRaises(SystemExit):
            self.run_bridge(self.manifest(), '--bridge-sha256', '0' * 64)
        self.assertFalse((self.base / 'out').exists())

    def test_non_mac_refused(self):
        with patch.object(runner.platform, 'system', return_value='Linux'), self.assertRaises(SystemExit):
            call(runner, [self.manifest(), '-o', self.base / 'out', '--bridge-sha256', '0' * 64])

    def test_no_discovery(self):
        with patch.dict(os.environ, {}, clear=True), self.assertRaises(ValueError):
            runner.resolve_bridge(None)

    def test_missing_output_is_failure(self):
        self.bridge.write_text(f'#!{sys.executable}\n')
        self.assertEqual(self.run_bridge(self.manifest()), 1)
        self.assertEqual(self.record()['status'], 'failed')

    def test_unsafe_group_id_refused(self):
        path = self.manifest()
        data = json.loads(path.read_text())
        data['groups'][0]['id'] = '../escape'
        path.write_text(json.dumps(data))
        with self.assertRaises(SystemExit):
            self.run_bridge(path)
        self.assertFalse((self.base / 'out').exists())

    def test_native_tiff_requires_declared_capability(self):
        with self.assertRaises(SystemExit):
            self.run_bridge(self.manifest(), '--native-tiff')

    def test_framework_mismatch_refused(self):
        profile = self.base / 'profile.json'
        profile.write_text(json.dumps({'frameworkSha256': {'Contents/Info.plist': '0' * 64}}))
        with self.assertRaises(SystemExit):
            self.run_bridge(self.manifest(), '--bridge-profile', profile)

    def test_native_tiff_profile_and_inspection(self):
        self.bridge.write_text(f"#!{sys.executable}\nimport os, sys, struct\nfrom pathlib import Path\ntags=[(256,4,1,12),(257,4,1,8),(258,3,1,16),(277,3,1,3)]\nb=b'II'+struct.pack('<HIH',42,8,len(tags))\nfor t,k,n,v in tags:\n b+=struct.pack('<HHI',t,k,n)+(struct.pack('<H',v)+b'\\0\\0' if k==3 else struct.pack('<I',v))\nPath(os.environ['PHOCUS_NATIVE_TIFF_FILE']).write_bytes(b+b'\\0'*4)\n")
        profile = self.base / 'profile.json'
        profile.write_text(json.dumps({'supportedEnvironment': ['PHOCUS_NATIVE_TIFF_FILE', 'PHOCUS_ROUNDTRIP_FILE', 'PHOCUS_RETAIN_NATIVE_TIFF']}))
        self.assertEqual(self.run_bridge(self.manifest(), '--native-tiff', '--bridge-profile', profile), 0)
        group = self.record()['groups'][0]
        self.assertNotIn('PHOCUS_CALIBRATED_PROBE', group['environment'])
        self.assertEqual(group['environment']['PHOCUS_RETAIN_NATIVE_TIFF'], '1')
        self.assertEqual(next(a for a in group['artifacts'] if a['kind'] == 'TIFF')['metadata']['width'], 12)

    def test_interruption_keeps_top_level_record(self):
        with patch.object(runner, 'execute', side_effect=KeyboardInterrupt):
            self.assertEqual(self.run_bridge(self.manifest()), 130)
        data = self.record()
        self.assertEqual(data['status'], 'interrupted')
        self.assertEqual(data['unattemptedGroups'], ['group-002'])
        self.assertEqual(data['groups'][0]['status'], 'interrupted')

    def test_all_supported_counts_and_intermediate_types(self):
        for p in self.frames:
            p.unlink()
        names = []
        for count in (4, 6, 16):
            group = []
            for i in range(count):
                name = f'{count}-{i}.3FR'
                (self.raw / name).write_bytes(name.encode())
                group.append(name)
            names.append(group)
        grouping = self.base / 'groups.json'
        grouping.write_text(json.dumps(names))
        manifest = self.base / 'm.json'
        with patch.object(scan, 'exif_times', return_value=({}, [])):
            call(scan, [self.raw, '-o', manifest, '--groups-file', grouping])
        self.assertEqual(self.run_bridge(manifest), 0)
        groups = self.record()['groups']
        self.assertEqual([g['frames'] for g in groups], [4, 6, 16])
        self.assertEqual(groups[2]['artifacts'][0]['kind'], 'intermediate-RGB-buffer')

    def test_malformed_manifest_refused(self):
        with self.assertRaises(ValueError):
            runner.validate_manifest([])
        with self.assertRaises(ValueError):
            runner.validate_manifest({'schema': 'phocus-pixel-shift/1'})

    def test_existing_manifest_preserved(self):
        path = self.base / 'm.json'
        path.write_text('keep me')
        with patch.object(scan, 'exif_times', return_value=({}, [])), self.assertRaises(FileExistsError):
            call(scan, [self.raw, '-o', path])
        self.assertEqual(path.read_text(), 'keep me')

    def test_tiff_metadata(self):
        path = self.base / 'test.tif'
        tags = [(256, 4, 1, 12), (257, 4, 1, 8), (258, 3, 1, 16), (277, 3, 1, 3)]
        blob = b'II' + struct.pack('<HIH', 42, 8, len(tags))
        for tag, kind, count, value in tags:
            raw = struct.pack('<H', value) + b'\0\0' if kind == 3 else struct.pack('<I', value)
            blob += struct.pack('<HHI', tag, kind, count) + raw
        path.write_bytes(blob + b'\0' * 4)
        info = inspect_tiff(path)
        self.assertEqual((info['width'], info['height'], info['channels']), (12, 8, 3))
        self.assertEqual(info['bitsPerSample'], [16])
        path.write_bytes(b'not a TIFF')
        with self.assertRaises(ValueError):
            inspect_tiff(path)



class WindowsDiscoveryTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.exe = self.root / 'Hasselblad' / 'Phocus' / 'Phocus.exe'
        self.exe.parent.mkdir(parents=True)
        self.exe.write_bytes(b'fixture phocus executable')
        self.exe.chmod(0o700)
        (self.exe.parent / 'Plugins').mkdir()
        (self.exe.parent / 'Plugins' / 'PixelShiftExport.plugin').write_bytes(b'fixture')

    def test_bounded_discovery_and_no_overwrite(self):
        import discover_phocus_windows as discovery
        self.assertEqual([self.exe.resolve()], discovery.candidates([self.root]))
        out = self.root / 'report.json'
        with patch.object(sys, 'argv', ['script', '--root', str(self.root), '--no-probe', '-o', str(out)]), contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(discovery.main(), 0)
        report = json.loads(out.read_text())
        self.assertEqual(report['phocusExecutables'][0]['sha256'], discovery.digest(self.exe))
        self.assertEqual(report['phocusExecutables'][0]['path'], str(self.exe.resolve()))
        with patch.object(sys, 'argv', ['script', '--root', str(self.root), '--no-probe', '-o', str(out)]), contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            discovery.main()

    def test_versioned_phocus64_install_layout(self):
        import discover_phocus_windows as discovery
        executable = self.root / 'Hasselblad' / 'Phocus 4.1.1' / 'Phocus64.exe'
        executable.parent.mkdir()
        executable.write_bytes(b'fixture')
        self.assertIn(executable.resolve(), discovery.candidates([self.root]))
        too_deep = self.root / 'a/b/c/d/e/Phocus.exe'
        too_deep.parent.mkdir(parents=True)
        too_deep.write_bytes(b'fixture')
        self.assertNotIn(too_deep.resolve(), discovery.candidates([self.root]))

    def test_probe_timeout_and_output_are_recorded(self):
        import discover_phocus_windows as discovery
        result = type('Result', (), {'stdout': 'help', 'stderr': 'version', 'returncode': 0})()
        with patch.object(discovery.subprocess, 'run', return_value=result) as run:
            report = discovery.probe(self.exe, 1)
        self.assertEqual(len(report['probes']), 2)
        self.assertEqual(run.call_count, 2)
        self.assertEqual(report['adjacentLayouts'][0]['nameHints'], ['PixelShiftExport.plugin'])


class NativeBuildTests(unittest.TestCase):
    def setUp(self):
        import prepare_native_bridge as build
        self.build = build
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name).resolve()

    def test_mac_dependency_validation(self):
        app = self.root / 'Phocus.app'
        (app / 'Contents').mkdir(parents=True)
        with (app / 'Contents/Info.plist').open('wb') as f:
            plistlib.dump({'CFBundleShortVersionString': '4.1.1'}, f)
        dependency = app / 'Contents/fixture'
        dependency.write_bytes(b'fixture')
        spec = {'phocusPath': str(app), 'phocusVersion': '4.1.1',
                'frameworkSha256': {'Contents/fixture': scan.sha256(dependency)}}
        result = self.build.check_mac_install(app, spec)
        self.assertEqual(result['version'], '4.1.1')
        dependency.write_bytes(b'changed')
        with self.assertRaises(ValueError):
            self.build.check_mac_install(app, spec)
        dependency.write_bytes(b'fixture')
        # A changed UI version label does not reject an otherwise compatible engine.
        with (app / 'Contents/Info.plist').open('wb') as f:
            plistlib.dump({'CFBundleShortVersionString': '9.9'}, f)
        self.assertEqual(self.build.check_mac_install(app, spec)['version'], '9.9')

    def test_windows_reports_missing_adapter_without_compilation(self):
        completed = type('Completed', (), {'stdout': b'fixture', 'stderr': b'', 'returncode': 0})()
        out = self.root / 'build'
        with patch.object(self.build.platform, 'system', return_value='Windows'), patch.object(self.build.subprocess, 'run', return_value=completed) as run:
            self.assertEqual(call(self.build, ['-o', out]), 3)
        self.assertEqual(run.call_count, 1)
        data = json.loads((out / 'build.json').read_text())
        self.assertEqual(data['status'], 'windows-adapter-required')
        self.assertNotIn('bridge', data)

    def test_guard_hashes_match_committed_profile(self):
        directory = Path(__file__).resolve().parents[1] / 'native/macos'
        spec = json.loads((directory / 'compatibility.json').read_text())
        guard = (directory / 'compatibility_guard.hpp').read_text()
        for relative, expected in spec['frameworkSha256'].items():
            self.assertIn('{"' + relative + '", "' + expected + '"}', guard)

    def test_unsupported_host_records_failure(self):
        out = self.root / 'build'
        with patch.object(self.build.platform, 'system', return_value='Linux'):
            self.assertEqual(call(self.build, ['-o', out]), 1)
        self.assertEqual(json.loads((out / 'build.json').read_text())['status'], 'failed')


if __name__ == '__main__':
    unittest.main()
