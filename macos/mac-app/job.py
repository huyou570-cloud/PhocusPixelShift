#!/usr/bin/env python3
"""Desktop job coordinator. Input paths are explicit and ordered, never guessed."""
import argparse
import json
import os
import plistlib
import signal
import subprocess
import sys
import time
from pathlib import Path
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from scan_3fr_sequences import sha256
from inspect_output import inspect_tiff

active = None
cancelled = False


def event(stage, message, progress=None):
    print(json.dumps({'stage': stage, 'message': message, 'progress': progress}, ensure_ascii=False), flush=True)


def stop(signum, frame):
    global cancelled
    cancelled = True
    # The polling loop sends SIGINT so the runner can terminate its own bridge group.


def command(cmd, log, cancel_file, timeout=3600):
    global active
    with log.open('wb') as stream:
        active = subprocess.Popen(cmd, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
        start = time.monotonic()
        while active.poll() is None:
            if cancelled or cancel_file.exists() or time.monotonic() - start > timeout:
                os.killpg(active.pid, signal.SIGINT)
                try:
                    active.wait(5)
                except subprocess.TimeoutExpired:
                    os.killpg(active.pid, signal.SIGKILL)
                    active.wait()
                raise RuntimeError('任务已停止；未完成文件已保留。' if cancelled or cancel_file.exists() else '处理超时，任务已终止。')
            time.sleep(.2)
        code = active.returncode
        active = None
        if code:
            text = log.read_text(errors='replace')[-3000:]
            raise RuntimeError(f'处理失败（退出码 {code}）：\n{text}')


def run(job_path):
    config = json.loads(job_path.read_text())
    out = Path(config['output']).resolve()
    if out.exists():
        raise ValueError('任务目录已存在，不覆盖旧结果。')
    paths = [Path(p).resolve(strict=True) for p in config['inputs']]
    if len(paths) not in (4, 6) or len(set(paths)) != len(paths):
        raise ValueError('请选择同组且不重复的 4 或 6 张原片。')
    if any(not p.is_file() or p.suffix.lower() != '.3fr' for p in paths):
        raise ValueError('输入须为可读取的 .3FR 文件。')
    if any(p.is_relative_to(out) for p in paths):
        raise ValueError('任务目录不能包含原片。')
    phocus = Path('/Applications/Phocus.app')
    out.mkdir(parents=True)
    cancel_file = out / 'cancel.request'
    record = {'startedAt': datetime.now(timezone.utc).isoformat(), 'status': 'running',
              'inputs': [], 'outputs': [], 'sourceDirectory': str(out), 'versionPolicy': 'engine-compatibility-not-version-label'}
    (out / 'job.json').write_text(json.dumps(config, indent=2))
    try:
        event('checking', '检查本机 Phocus 和运行环境…', 2)
        with (phocus / 'Contents/Info.plist').open('rb') as stream:
            info = plistlib.load(stream)
        record['phocusVersion'] = info.get('CFBundleShortVersionString')
        event('hashing', '核对原片，记录实际选择顺序…', 8)
        for path in paths:
            if cancelled or cancel_file.exists():
                raise RuntimeError('任务已停止。')
            record['inputs'].append({'path': str(path), 'bytes': path.stat().st_size, 'sha256': sha256(path)})
        event('building', '检查引擎兼容性并编译本地桥…', 16)
        build_dir = out / '.work/build'
        prepare = [sys.executable, str(ROOT / 'scripts/prepare_native_bridge.py'), '-o', str(build_dir)]
        if config.get('architecture'):
            prepare += ['--architecture', config['architecture']]
        command(prepare,
                out / 'build-console.log', cancel_file, timeout=240)
        build = json.loads((build_dir / 'build.json').read_text())
        record['build'] = build
        manifest = out / '.work/manifest.json'
        # Common path lets a job contain explicit frames from multiple folders.
        source = Path(os.path.commonpath([str(p.parent) for p in paths]))
        manifest.write_text(json.dumps({'schema': 'phocus-pixel-shift/2', 'sourceDirectory': str(source),
                                       'groups': [{'id': 'group-001', 'frames': len(paths), 'selected': True,
                                                   'groupingConfirmed': True, 'files': record['inputs']}]}, indent=2))
        event('merging', f'合成 {len(paths)} 张并由 Phocus 导出 16 位 TIFF…', 30)
        native_out = out / '.work/run'
        command([sys.executable, str(ROOT / 'scripts/run_local_bridge.py'), str(manifest),
                 '-o', str(native_out), '--bridge', build['bridge'], '--bridge-sha256', build['bridgeSha256'],
                 '--bridge-profile', build['bridgeProfile'], '--native-tiff'],
                out / 'merge-console.log', cancel_file)
        if cancelled or cancel_file.exists():
            raise RuntimeError('任务已停止。')
        event('verifying', '独立检查 TIFF 并复核原片哈希…', 90)
        group_record = json.loads((native_out / 'group-001/run.json').read_text())
        tiff = next(Path(a['path']) for a in group_record['artifacts'] if a['kind'] == 'TIFF')
        metadata = inspect_tiff(tiff)
        expected = (11656, 8742) if len(paths) == 4 else (23312, 17484)
        if (metadata['width'], metadata['height']) != expected or metadata['channels'] != 3 or any(v != 16 for v in metadata['bitsPerSample']):
            raise RuntimeError('TIFF 尺寸或 RGB16 格式不符合当前 X2D 实验路线。')
        record['originalsVerifiedUnchanged'] = all(sha256(Path(row['path'])) == row['sha256'] for row in record['inputs'])
        if cancelled or cancel_file.exists():
            raise RuntimeError('任务已停止。')
        if not record['originalsVerifiedUnchanged']:
            raise RuntimeError('原片哈希发生变化，结果未标为成功。')
        final = out / '合并结果-16位.tif'
        tiff.rename(final)
        record['outputs'].append({'path': str(final), 'sha256': sha256(final), 'metadata': metadata})
        fff = native_out / 'group-001' / f'native-{len(paths)}-roundtrip.fff'
        if fff.exists():
            raw = out / f'合并结果-{len(paths)}帧.fff'
            fff.rename(raw)
            record['outputs'].append({'path': str(raw), 'bytes': raw.stat().st_size, 'kind': 'native-intermediate'})
        record['status'] = 'succeeded'
        event('complete', f'完成：{metadata["width"]} × {metadata["height"]}，16 位 RGB TIFF。', 100)
        return 0
    except BaseException as exc:
        record['status'] = 'cancelled' if cancelled or cancel_file.exists() else 'failed'
        record['error'] = str(exc)
        event(record['status'], str(exc))
        return 130 if record['status'] == 'cancelled' else 1
    finally:
        record['finishedAt'] = datetime.now(timezone.utc).isoformat()
        (out / '验证记录.json').write_text(json.dumps(record, ensure_ascii=False, indent=2) + '\n')


if __name__ == '__main__':
    signal.signal(signal.SIGTERM, stop)
    signal.signal(signal.SIGINT, stop)
    parser = argparse.ArgumentParser()
    parser.add_argument('job', type=Path)
    args = parser.parse_args()
    try:
        raise SystemExit(run(args.job))
    except Exception as exc:
        event('failed', str(exc))
        raise SystemExit(1)
