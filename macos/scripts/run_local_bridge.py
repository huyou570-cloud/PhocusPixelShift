#!/usr/bin/env python3
"""Run explicitly configured, hash-pinned local bridges and preserve partial records."""
from __future__ import annotations

import argparse
import json
import math
import os
import platform
import plistlib
import re
import signal
import subprocess
import time
from datetime import datetime, timezone
from pathlib import Path
from scan_3fr_sequences import sha256, VALID
from inspect_output import inspect_tiff

NATIVE_FLAGS = {'PHOCUS_CALIBRATED_PROBE', 'PHOCUS_RETAIN_NATIVE_TIFF',
                'PHOCUS_USE_NATIVE_FILE_WRITER', 'PHOCUS_ALLOW_NATIVE_64BIT_REOPEN'}
NATIVE_PATHS = {'PHOCUS_ROUNDTRIP_FILE', 'PHOCUS_NATIVE_TIFF_FILE'}
ALLOWED_ENV = NATIVE_FLAGS | NATIVE_PATHS | {'PHOCUS_PROBE_LABEL'}


def now():
    return datetime.now(timezone.utc).isoformat()


def save(path, data):
    temporary = path.with_suffix('.json.tmp')
    temporary.write_text(json.dumps(data, indent=2, ensure_ascii=False) + '\n')
    temporary.replace(path)


def resolve_bridge(value):
    value = value or os.environ.get('PHOCUS_NATIVE_BRIDGE')
    if not value:
        raise ValueError('Set --bridge or PHOCUS_NATIVE_BRIDGE; automatic discovery is disabled')
    path = Path(value).expanduser().resolve()
    if not path.is_file() or not os.access(path, os.X_OK):
        raise ValueError(f'Bridge is not an executable file: {path}')
    return path


def check_digest(value):
    if not isinstance(value, str) or not re.fullmatch('[a-fA-F0-9]{64}', value):
        raise ValueError('Expected SHA-256 must be 64 hexadecimal characters')
    return value.lower()


def validate_manifest(data):
    if not isinstance(data, dict):
        raise ValueError('Manifest must be a JSON object')
    if data.get('schema') != 'phocus-pixel-shift/2':
        raise ValueError('Rescan with schema /2 and confirm grouping before execution')
    root = Path(data['sourceDirectory']).resolve(strict=True)
    if not root.is_dir():
        raise ValueError('Source directory does not exist')
    groups = data.get('groups')
    if not isinstance(groups, list):
        raise ValueError('Manifest groups must be a list')
    selected, ids, files = [], set(), set()
    for group in groups:
        if not isinstance(group, dict):
            raise ValueError('Each group must be an object')
        ident = group['id']
        if not isinstance(ident, str) or not re.fullmatch(r'group-[0-9]{3,}', ident) or ident in ids:
            raise ValueError('Unsafe or duplicate group ID')
        ids.add(ident)
        if type(group.get('selected')) is not bool:
            raise ValueError('selected must be a boolean')
        if not group['selected']:
            continue
        count = group.get('frames')
        if type(count) is not int or count not in VALID or group.get('groupingConfirmed') is not True:
            raise ValueError(f'{ident}: confirm exact 4/6/16-frame grouping first')
        if not isinstance(group.get('files'), list) or len(group['files']) != count:
            raise ValueError(f'{ident}: frame count does not match file list')
        for row in group['files']:
            if not isinstance(row, dict) or not isinstance(row.get('path'), str) or not Path(row['path']).is_absolute():
                raise ValueError('Input entries require absolute paths')
            path = Path(row['path']).resolve()
            if not path.is_relative_to(root) or path.suffix.lower() != '.3fr' or path in files:
                raise ValueError(f'{ident}: duplicate or out-of-root input')
            if type(row.get('bytes')) is not int or row['bytes'] < 0:
                raise ValueError('Invalid input byte count')
            check_digest(row['sha256'])
            files.add(path)
        selected.append(group)
    if not selected:
        raise ValueError('Manifest has no confirmed selected groups')
    return selected


def execute(cmd, env, timeout, stdout_path, stderr_path):
    # Stream raw bytes to disk instead of buffering potentially huge/binary logs.
    with stdout_path.open('wb') as stdout, stderr_path.open('wb') as stderr:
        proc = subprocess.Popen(cmd, stdout=stdout, stderr=stderr, env=env, start_new_session=True)
        try:
            return proc.wait(timeout=timeout), False
        except subprocess.TimeoutExpired:
            os.killpg(proc.pid, signal.SIGKILL)
            return proc.wait(), True
        except BaseException:
            os.killpg(proc.pid, signal.SIGKILL)
            proc.wait()
            raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('manifest', type=Path)
    parser.add_argument('-o', '--output', '--output-root', type=Path, required=True)
    parser.add_argument('--bridge')
    parser.add_argument('--bridge-sha256', required=True, help='Expected digest from a trusted build record')
    parser.add_argument('--bridge-profile', type=Path, help='JSON supportedEnvironment and optional frameworkSha256')
    parser.add_argument('--native-tiff', action='store_true')
    parser.add_argument('--timeout-seconds', type=float, default=3600)
    parser.add_argument('--phocus-app', type=Path, default=Path('/Applications/Phocus.app'))
    args = parser.parse_args()
    try:
        if platform.system() != 'Darwin':
            raise ValueError('Native processing requires macOS; scanning works cross-platform')
        if not math.isfinite(args.timeout_seconds) or args.timeout_seconds <= 0:
            raise ValueError('Timeout must be finite and positive')
        manifest = args.manifest.expanduser().resolve(strict=True)
        manifest_hash = sha256(manifest)
        data = json.loads(manifest.read_text())
        selected = validate_manifest(data)
        bridge = resolve_bridge(args.bridge)
        expected = check_digest(args.bridge_sha256)
        if sha256(bridge) != expected:
            raise ValueError('Bridge SHA-256 mismatch; refusing execution')
        phocus = args.phocus_app.expanduser().resolve(strict=True)
        if not phocus.is_dir():
            raise ValueError('Phocus application path must be a directory')
        with (phocus / 'Contents/Info.plist').open('rb') as stream:
            info = plistlib.load(stream)
        profile = json.loads(args.bridge_profile.read_text()) if args.bridge_profile else {}
        if not isinstance(profile, dict):
            raise ValueError('Bridge profile must be a JSON object')
        supported = profile.get('supportedEnvironment', [])
        if not isinstance(supported, list) or any(v not in ALLOWED_ENV for v in supported):
            raise ValueError('Unsupported bridge profile environment variable')
        if args.native_tiff and not NATIVE_PATHS <= set(supported):
            raise ValueError('--native-tiff requires a profile declaring both native output paths')
        framework_hashes = profile.get('frameworkSha256', {})
        if not isinstance(framework_hashes, dict):
            raise ValueError('frameworkSha256 must be a relative-path/digest object')
        for relative, digest in framework_hashes.items():
            target = (phocus / relative).resolve(strict=True)
            if Path(relative).is_absolute() or not target.is_relative_to(phocus):
                raise ValueError('Framework path must stay inside Phocus.app')
            if sha256(target) != check_digest(digest):
                raise ValueError(f'Phocus component SHA-256 mismatch: {relative}')
        out = args.output.expanduser().resolve()
        source = Path(data['sourceDirectory']).resolve()
        if out == source or source.is_relative_to(out):
            raise ValueError('Output directory cannot contain the source directory')
        out.mkdir(parents=True, exist_ok=False)
    except (ValueError, OSError, KeyError, TypeError) as exc:
        parser.error(str(exc))

    run = {'schema': 'phocus-pixel-shift/run/2', 'startedAt': now(), 'status': 'running',
           'manifest': str(manifest), 'manifestSha256': manifest_hash,
           'bridge': str(bridge), 'bridgeSha256': expected, 'bridgeProfile': profile,
           'phocusPath': str(phocus), 'phocusPresent': True,
           'phocusVersion': info.get('CFBundleShortVersionString'),
           'phocusBuild': info.get('CFBundleVersion'),
           'frameworkHashChecks': framework_hashes, 'timeoutSeconds': args.timeout_seconds,
           'clearedInheritedEnvironment': sorted(k for k in os.environ if k.startswith('PHOCUS_')),
           'groups': []}
    save(out / 'run.json', run)
    interrupted = False
    try:
        for group in selected:
            record = {'id': group['id'], 'frames': group['frames'], 'startedAt': now(),
                      'status': 'running', 'inputs': [], 'environment': {}}
            run['groups'].append(record)
            gd = out / group['id']
            started = time.monotonic()
            try:
                gd.mkdir()
                inputs = [Path(row['path']).resolve() for row in group['files']]
                for path, row in zip(inputs, group['files']):
                    actual = {'path': str(path), 'bytes': path.stat().st_size, 'sha256': sha256(path)}
                    record['inputs'].append(actual)
                    if actual['bytes'] != row['bytes'] or actual['sha256'] != row['sha256'].lower():
                        raise ValueError(f'Input changed since scan: {path}')
                if sha256(bridge) != expected:
                    raise ValueError('Bridge changed during run')
                for relative, digest in framework_hashes.items():
                    if sha256(phocus / relative) != digest.lower():
                        raise ValueError(f'Phocus component changed: {relative}')
                frames = group['frames']
                merged = gd / f'native-{frames}{".fff" if frames in (4, 6) else ".rgb16"}'
                tiff = gd / f'native-{frames}.tif'
                roundtrip = gd / f'native-{frames}-roundtrip.fff'
                flags = {}
                if 'PHOCUS_PROBE_LABEL' in supported:
                    flags['PHOCUS_PROBE_LABEL'] = group['id']
                if args.native_tiff:
                    flags.update({key: '1' for key in NATIVE_FLAGS if key in supported})
                    flags.update(PHOCUS_ROUNDTRIP_FILE=str(roundtrip), PHOCUS_NATIVE_TIFF_FILE=str(tiff))
                env = {k: v for k, v in os.environ.items() if not k.startswith('PHOCUS_')}
                env.update(flags)
                record['environment'] = flags
                cmd = [str(bridge), str(frames), str(merged), *map(str, inputs)]
                record['command'] = cmd
                save(gd / 'run.json', record)
                save(out / 'run.json', run)
                code, timeout = execute(cmd, env, args.timeout_seconds, gd / 'stdout.log', gd / 'stderr.log')
                record.update(exitCode=code, timedOut=timeout, artifacts=[])
                # Record partial outputs before classifying bridge failure.
                for path, kind in ((merged, 'intermediate-FFF' if frames in (4, 6) else 'intermediate-RGB-buffer'),
                                   (tiff, 'TIFF'), (roundtrip, 'roundtrip-FFF')):
                    if path.is_file():
                        item = {'path': str(path), 'kind': kind, 'bytes': path.stat().st_size,
                                'sha256': sha256(path)}
                        if kind == 'TIFF':
                            try:
                                item['metadata'] = inspect_tiff(path)
                            except (OSError, ValueError) as exc:
                                item['inspectionError'] = str(exc)
                        record['artifacts'].append(item)
                if timeout:
                    raise ValueError('Bridge timed out; process group terminated')
                if code:
                    raise ValueError(f'Bridge exited {code}; see stderr.log')
                if not args.native_tiff and (not merged.is_file() or merged.stat().st_size == 0):
                    raise ValueError('Bridge returned success without a nonempty intermediate output')
                if args.native_tiff:
                    inspected = next((a for a in record['artifacts'] if a['kind'] == 'TIFF'), None)
                    if not inspected or 'metadata' not in inspected:
                        raise ValueError('Native TIFF missing or metadata inspection failed')
                record['status'] = 'succeeded'
            except KeyboardInterrupt:
                record.update(status='interrupted', error='Interrupted by user')
                interrupted = True
            except (OSError, ValueError, subprocess.SubprocessError) as exc:
                record.update(status='failed', error=str(exc))
            finally:
                record.update(finishedAt=now(), elapsedSeconds=time.monotonic() - started)
                record['cleanup'] = {'policy': 'retain-for-audit', 'deleted': [],
                                     'retained': sorted(str(p) for p in gd.iterdir()) if gd.is_dir() else []}
                if gd.is_dir():
                    save(gd / 'run.json', record)
                save(out / 'run.json', run)
            if interrupted:
                break
    finally:
        failed = interrupted or any(g['status'] != 'succeeded' for g in run['groups'])
        run.update(finishedAt=now(), status='interrupted' if interrupted else 'failed' if failed else 'succeeded',
                   unattemptedGroups=[g['id'] for g in selected[len(run['groups']):]])
        save(out / 'run.json', run)
    print(json.dumps({'output': str(out), 'status': run['status']}))
    return 130 if interrupted else 1 if failed else 0


if __name__ == '__main__':
    raise SystemExit(main())
