#!/usr/bin/env python3
"""Check installed Phocus and compile the included original Mac adapter locally.

Windows returns an actionable discovery report, not a fabricated compatible bridge.
No vendor components are downloaded or copied.
"""
from __future__ import annotations
import argparse
import json
import platform
import plistlib
import shutil
import subprocess
import sys
from pathlib import Path
from datetime import datetime, timezone
from scan_3fr_sequences import sha256

REPO = Path(__file__).resolve().parents[1]


def check_mac_install(app: Path, compatibility: dict):
    if str(app.resolve()) != compatibility['phocusPath']:
        raise ValueError('This adapter currently requires /Applications/Phocus.app')
    with (app / 'Contents/Info.plist').open('rb') as stream:
        info = plistlib.load(stream)
    # Version is diagnostic only: compatibility is established from engine bytes.
    checked = {}
    for relative, expected in compatibility['frameworkSha256'].items():
        file = (app / relative).resolve(strict=True)
        if not file.is_relative_to(app.resolve()) or sha256(file) != expected:
            raise ValueError(f'Phocus interface layout has not been validated for this component: {relative}. The application version is not restricted; private ABI compatibility must be established before processing.')
        checked[relative] = expected
    return {'version': info['CFBundleShortVersionString'], 'build': info.get('CFBundleVersion'),
            'checkedDependencies': checked}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('-o', '--output', type=Path, required=True, help='New local build directory')
    parser.add_argument('--phocus-app', type=Path, default=Path('/Applications/Phocus.app'))
    parser.add_argument('--architecture', choices=['arm64', 'x86_64'], help='Default: architecture of the running Python interpreter')
    parser.add_argument('--phocus', type=Path, help='Windows executable for discovery')
    args = parser.parse_args()
    out = args.output.expanduser().resolve()
    if out.exists():
        parser.error('Build directory must not already exist')
    out.mkdir(parents=True)
    record = {'schema': 'phocus-pixel-shift/build/1', 'startedAt': datetime.now(timezone.utc).isoformat(),
              'platform': platform.system(), 'architecture': platform.machine(), 'status': 'checking',
              'imageProcessingExecuted': False}
    architecture = args.architecture or platform.machine()
    record['architecture'] = architecture
    code = 1
    try:
        if platform.system() == 'Windows':
            cmd = [sys.executable, str(REPO / 'scripts/discover_phocus_windows.py'),
                   '--no-probe', '-o', str(out / 'windows-discovery.json')]
            if args.phocus:
                cmd += ['--phocus', str(args.phocus)]
            result = subprocess.run(cmd, capture_output=True, timeout=120)
            (out / 'discovery.stdout.log').write_bytes(result.stdout)
            (out / 'discovery.stderr.log').write_bytes(result.stderr)
            record['discoveryExitCode'] = result.returncode
            record['status'] = 'windows-adapter-required'
            record['nextStep'] = 'Follow references/windows-native-development.md on this Windows host. No Windows adapter is included or verified; discovery is not a merge.'
            code = 3
        elif platform.system() != 'Darwin' or architecture not in ('arm64', 'x86_64'):
            raise ValueError('Native source requires macOS ARM64 or x86_64')
        else:
            source = REPO / 'native/macos'
            compatibility = json.loads((source / 'compatibility.json').read_text())
            record['phocus'] = check_mac_install(args.phocus_app.expanduser(), compatibility)
            compiler = shutil.which('clang++')
            if not compiler:
                raise ValueError('clang++ missing. Install Apple Command Line Tools (xcode-select --install), then retry in a new directory')
            sources = [source / n for n in ('native_fff_bridge.mm', 'native_pipeline_support.hpp', 'compatibility_guard.hpp', 'architecture.hpp', 'compatibility.json')]
            record['sourceSha256'] = {p.name: sha256(p) for p in sources}
            binary = out / 'NativeBridge.app/Contents/MacOS/native-fff-bridge'
            binary.parent.mkdir(parents=True)
            # Legacy Intel dependencies use @executable_path/../Frameworks.
            # Link to the user's installed frameworks; do not copy vendor files.
            (binary.parent.parent / 'Frameworks').symlink_to(args.phocus_app.expanduser().resolve() / 'Contents/Frameworks', target_is_directory=True)
            command = [compiler, '-std=c++17', '-O2', '-fstack-protector-all', '-arch', architecture, '-framework', 'Foundation', '-Wl,-rpath,/Applications/Phocus.app/Contents/Frameworks',
                       str(source / 'native_fff_bridge.mm'), '-o', str(binary)]
            record['command'] = command
            result = subprocess.run(command, capture_output=True, timeout=180)
            (out / 'build.stdout.log').write_bytes(result.stdout)
            (out / 'build.stderr.log').write_bytes(result.stderr)
            record['compilerExitCode'] = result.returncode
            if result.returncode:
                raise ValueError('Compilation failed; see build.stderr.log')
            check = subprocess.run([str(binary), '--check'], capture_output=True, timeout=30)
            (out / 'compatibility.stdout.log').write_bytes(check.stdout)
            (out / 'compatibility.stderr.log').write_bytes(check.stderr)
            if check.returncode or not json.loads(check.stdout).get('compatible'):
                raise ValueError('Compiled adapter failed dependency check')
            profile = {'supportedEnvironment': compatibility['supportedEnvironment'],
                       'frameworkSha256': compatibility['frameworkSha256']}
            profile_path = out / 'bridge-profile.json'
            profile_path.write_text(json.dumps(profile, indent=2) + '\n')
            record.update(status='built-dependencies-verified', bridge=str(binary),
                          bridgeSha256=sha256(binary), bridgeProfile=str(profile_path),
                          limitations=compatibility['limitations'])
            record['nextStep'] = 'Use run_local_bridge.py with the recorded bridge, hash and profile plus --native-tiff and a confirmed manifest. A real sample run is still required.'
            code = 0
    except (OSError, ValueError, KeyError, subprocess.SubprocessError) as exc:
        record.update(status='failed', error=str(exc))
    finally:
        record['finishedAt'] = datetime.now(timezone.utc).isoformat()
        (out / 'build.json').write_text(json.dumps(record, indent=2) + '\n')
    print(json.dumps(record, indent=2))
    return code


if __name__ == '__main__':
    raise SystemExit(main())
