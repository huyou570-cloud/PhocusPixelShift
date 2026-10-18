#!/usr/bin/env python3
"""Build an Universal Intel/Apple Silicon AppKit app with original bridge source and Python workflow."""
import argparse
import plistlib
import shutil
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('-o', '--output', type=Path, required=True)
    args = parser.parse_args()
    app = args.output.expanduser().resolve()
    if app.exists() or app.suffix != '.app':
        parser.error('Choose a new .app path')
    contents = app / 'Contents'
    executable = contents / 'MacOS/PhocusPixelShift'
    resources = contents / 'Resources/workflow'
    executable.parent.mkdir(parents=True)
    resources.mkdir(parents=True)
    for folder in ('scripts', 'native'):
        shutil.copytree(ROOT / folder, resources / folder, ignore=shutil.ignore_patterns('__pycache__', '*.pyc'))
    shutil.copyfile(ROOT / 'LICENSE', contents / 'Resources/LICENSE')
    (resources / 'mac-app').mkdir()
    shutil.copyfile(ROOT / 'mac-app/job.py', resources / 'mac-app/job.py')
    info = {'CFBundleName': 'Phocus 像素位移合并', 'CFBundleDisplayName': 'Phocus 像素位移合并',
            'CFBundleIdentifier': 'io.radium.phocus-pixel-shift', 'CFBundleExecutable': 'PhocusPixelShift',
            'CFBundlePackageType': 'APPL', 'CFBundleShortVersionString': '0.2.0', 'CFBundleVersion': '2',
            'LSMinimumSystemVersion': '13.0', 'NSHighResolutionCapable': True}
    with (contents / 'Info.plist').open('wb') as stream:
        plistlib.dump(info, stream)
    slices = []
    for arch in ('arm64', 'x86_64'):
        target = executable.with_name(executable.name + '.' + arch)
        subprocess.run(['swiftc', '-O', '-swift-version', '5', '-target', arch + '-apple-macosx13.0',
                        '-module-cache-path', '/private/tmp/phocus-app-swift-cache',
                        '-framework', 'AppKit', '-framework', 'UniformTypeIdentifiers',
                        str(ROOT / 'mac-app/App.swift'), '-o', str(target)], check=True)
        slices.append(target)
    subprocess.run(['lipo', '-create', *map(str, slices), '-output', str(executable)], check=True)
    for target in slices:
        target.unlink()
    # Local ad-hoc signing is not Developer ID notarization.
    subprocess.run(['codesign', '--force', '--deep', '--sign', '-', str(app)], check=True)
    print(app)


if __name__ == '__main__':
    main()
