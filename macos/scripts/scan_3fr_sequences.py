#!/usr/bin/env python3
"""Propose .3FR groups; select only groups explicitly confirmed by the caller."""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import shutil
import subprocess
from datetime import datetime, timezone
from pathlib import Path

VALID = {4, 6, 16}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(8 * 1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()


def exif_times(paths: list[Path]) -> tuple[dict, list[str]]:
    tool = shutil.which('exiftool')
    if not tool:
        return {}, ['exiftool unavailable.']
    if any('\n' in str(p) or '\r' in str(p) for p in paths):
        return {}, ['EXIF lookup skipped: a filename contains a newline.']
    try:
        proc = subprocess.run(
            [tool, '-j', '-DateTimeOriginal', '-SubSecTimeOriginal',
             '-OffsetTimeOriginal', '-@', '-'],
            input='\n'.join(map(str, paths)) + '\n', capture_output=True,
            text=True, errors='replace', timeout=120,
        )
        warnings = [proc.stderr.strip()] if proc.stderr.strip() else []
        result = {}
        for row in json.loads(proc.stdout):
            try:
                raw = row['DateTimeOriginal']
                stamp = datetime.strptime(raw, '%Y:%m:%d %H:%M:%S')
                sub = str(row.get('SubSecTimeOriginal', ''))
                if sub.isdigit():
                    stamp = stamp.replace(microsecond=int((sub + '000000')[:6]))
                offset = row.get('OffsetTimeOriginal')
                if offset:
                    tz = datetime.strptime(offset, '%z').tzinfo
                    stamp = stamp.replace(tzinfo=tz)
                    axis = 'exif-offset'
                else:
                    # A common wall-clock coordinate, NOT a claimed UTC capture time.
                    stamp = stamp.replace(tzinfo=timezone.utc)
                    axis = 'exif-wall-clock'
                result[str(Path(row['SourceFile']).resolve())] = (stamp.timestamp(), axis)
            except (KeyError, ValueError, TypeError):
                continue
        if proc.returncode:
            warnings.append(f'exiftool exited {proc.returncode}; usable per-file results retained.')
        return result, warnings
    except (OSError, subprocess.TimeoutExpired, json.JSONDecodeError) as exc:
        return {}, [f'EXIF lookup failed: {exc}']


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('-o', '--output', '--manifest', type=Path)
    parser.add_argument('--gap-seconds', type=float, default=20)
    parser.add_argument('--expected-count', type=int, choices=sorted(VALID))
    parser.add_argument('--confirm-group', action='append', default=[], metavar='ID',
                        help='Confirm a known sequence, e.g. group-001; repeat as needed')
    parser.add_argument('--groups-file', type=Path,
                        help='JSON array of ordered relative-path arrays; explicit grouping')
    args = parser.parse_args()
    root = args.directory.expanduser().resolve()
    if not root.is_dir():
        parser.error(f'Not a directory: {root}')
    if not math.isfinite(args.gap_seconds) or args.gap_seconds <= 0:
        parser.error('--gap-seconds must be finite and positive')
    paths = sorted(p.resolve() for p in root.rglob('*')
                   if p.is_file() and p.suffix.lower() == '.3fr')
    if not paths or len(paths) != len(set(paths)) or any(not p.is_relative_to(root) for p in paths):
        parser.error('Need unique .3FR files contained in the source directory (no external symlinks)')
    times, warnings = exif_times(paths)
    axes = {times[str(p)][1] for p in paths if str(p) in times}
    use_exif = all(str(p) in times for p in paths) and len(axes) == 1
    source = next(iter(axes)) if use_exif else 'mtime'
    if not use_exif:
        warnings.append('Using mtime for ALL frames: EXIF is missing or mixes offset and wall-clock times. Copying files may change mtime; confirm groups from capture records.')
    elif source == 'exif-wall-clock':
        warnings.append('EXIF has no timezone; sortTime is a common wall-clock coordinate, not UTC. Sequences spanning timezone/clock changes need explicit grouping.')
    rows = []
    for path in paths:
        stat = path.stat()
        rows.append({'path': str(path), 'relativePath': str(path.relative_to(root)),
                     'bytes': stat.st_size, 'sha256': sha256(path), 'timeSource': source,
                     'sortTime': times[str(path)][0] if use_exif else stat.st_mtime})
    rows.sort(key=lambda r: (r['sortTime'], r['relativePath']))
    groups = []
    if args.groups_file:
        try:
            supplied = json.loads(args.groups_file.read_text())
            if not isinstance(supplied, list) or not supplied:
                raise ValueError('Expected a nonempty array of path arrays')
            by_path = {r['path']: r for r in rows}
            seen = set()
            for names in supplied:
                if not isinstance(names, list) or len(names) not in VALID:
                    raise ValueError('Each explicit group must contain 4, 6, or 16 paths')
                group = []
                for name in names:
                    if not isinstance(name, str) or Path(name).is_absolute():
                        raise ValueError('Use relative paths within the source directory')
                    path = str((root / name).resolve())
                    if path not in by_path or path in seen:
                        raise ValueError(f'Unknown or duplicate input: {name}')
                    seen.add(path)
                    group.append(by_path[path])
                groups.append(group)
            if len(seen) != len(rows):
                warnings.append(f'{len(rows) - len(seen)} scanned frames omitted by explicit grouping.')
        except (OSError, ValueError, TypeError) as exc:
            parser.error(f'Invalid groups file: {exc}')
    else:
        # Never merge separate source subdirectories into one candidate.
        for parent in sorted({str(Path(r['path']).parent) for r in rows}):
            current = []
            for row in (r for r in rows if str(Path(r['path']).parent) == parent):
                if current and row['sortTime'] - current[-1]['sortTime'] > args.gap_seconds:
                    groups.append(current)
                    current = []
                current.append(row)
            if current:
                groups.append(current)
    result = []
    for index, group in enumerate(groups, 1):
        ident = f'group-{index:03d}'
        valid = len(group) in VALID
        confirmed = bool(args.groups_file) or ident in args.confirm_group
        result.append({'id': ident, 'frames': len(group), 'validFrameCount': valid,
                       'groupingConfirmed': confirmed,
                       'selected': valid and confirmed and (args.expected_count is None or len(group) == args.expected_count),
                       'files': group})
        if not valid:
            warnings.append(f'{ident} has {len(group)} frames. Not split automatically; adjust --gap-seconds or provide --groups-file.')
    unknown = set(args.confirm_group) - {g['id'] for g in result}
    if unknown:
        parser.error(f'Unknown group IDs: {sorted(unknown)}')
    manifest = {'schema': 'phocus-pixel-shift/2', 'sourceDirectory': str(root),
                'createdAt': datetime.now(timezone.utc).isoformat(), 'gapSeconds': args.gap_seconds,
                'groupingMethod': 'explicit' if args.groups_file else 'time-candidates',
                'groups': result, 'warnings': warnings}
    out = (args.output or root / 'phocus-pixel-shift-manifest.json').expanduser().resolve()
    if out in paths:
        parser.error('Manifest cannot overwrite an input')
    out.parent.mkdir(parents=True, exist_ok=True)
    with out.open('x') as stream:
        stream.write(json.dumps(manifest, indent=2, ensure_ascii=False) + '\n')
    print(json.dumps({'manifest': str(out), 'files': len(rows),
                      'groups': [(g['id'], g['frames'], g['selected']) for g in result]}, ensure_ascii=False))
    return 0 if any(g['validFrameCount'] for g in result) else 3


if __name__ == '__main__':
    raise SystemExit(main())
