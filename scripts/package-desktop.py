"""Package an already natively verified Windows/Mac publish without credentials."""
import argparse
import hashlib
import json
from pathlib import Path
import platform
import plistlib
import re
import shutil
import subprocess
import sys
import zipfile
import xml.etree.ElementTree as ET

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--app', type=Path, required=True)
p.add_argument('--rid', choices=['win-x64', 'osx-arm64', 'osx-x64'], required=True)
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
root = Path(__file__).resolve().parents[1]
source = a.app.resolve(strict=True)
output = a.output.resolve()
if not source.is_dir() or output.exists() or not output.is_relative_to(root / 'artifacts'):
    raise SystemExit('Use an actual published folder and fresh ignored artifacts output')
expected = {'win-x64': ('win32', 'AMD64'), 'osx-arm64': ('darwin', 'arm64'), 'osx-x64': ('darwin', 'x86_64')}
if (sys.platform, platform.machine()) != expected[a.rid]:
    raise SystemExit('Package on the declared native OS and architecture')
head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip()
version = ET.parse(root / 'Directory.Build.props').findtext('./PropertyGroup/Version')
if re.fullmatch(r'[0-9a-f]{40}', head) is None or not version or re.fullmatch(r'\d+\.\d+\.\d+', version) is None:
    raise SystemExit('Expected an exact source revision and shared semantic version')
output.mkdir(parents=True)
subprocess.run([sys.executable, str(root / 'scripts/verify-runtime-package.py'), str(source), '--rid', a.rid,
                '--output', str(output / 'RUNTIME.json')], check=True)
if any(file.is_symlink() for file in source.rglob('*')):
    raise SystemExit('Unexpected symbolic link in published payload')
for file in source.rglob('*'):
    if file.is_file() and (file.name.lower() in ('auth.json', 'accounts.json', '.env') or file.suffix.lower() in ('.vault', '.log', '.pfx', '.p12', '.pdb')):
        raise SystemExit('Private or diagnostic file in published payload')
stage = output / 'stage'
stage.mkdir()
archive = output / f'CodexSwitch-v{version}-{a.rid}.zip'
signing = 'Unsigned Windows executable'
if sys.platform == 'win32':
    folder = stage / f'CodexSwitch-v{version}-{a.rid}'
    shutil.copytree(source, folder)
    shutil.copyfile(root / 'LICENSE', folder / 'LICENSE')
    with zipfile.ZipFile(archive, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as bundle:
        for file in sorted(stage.rglob('*')):
            if file.is_file():
                bundle.write(file, file.relative_to(stage).as_posix())
else:
    app = stage / 'Codex Switch.app'
    binary = app / 'Contents/MacOS'
    resources = app / 'Contents/Resources'
    shutil.copytree(source, binary)
    resources.mkdir()
    # Code signing treats nested files in Contents/MacOS as code. License text
    # is bundle data and must remain present under Contents/Resources instead.
    published_licenses = binary / 'licenses'
    if published_licenses.exists():
        shutil.move(str(published_licenses), str(resources / 'licenses'))
    shutil.copyfile(root / 'src/macos/Info.plist', app / 'Contents/Info.plist')
    shutil.copyfile(root / 'assets/macos/app-icon.icns', resources / 'app-icon.icns')
    shutil.copyfile(root / 'licenses/Burn0Assets.LICENSE.txt', resources / 'Burn0Assets.LICENSE.txt')
    shutil.copyfile(root / 'src/macos/entitlements.plist', resources / 'entitlements.plist')
    shutil.copyfile(root / 'LICENSE', resources / 'LICENSE')
    shutil.copyfile(root / 'scripts/install-macos.sh', stage / 'Install.command')
    (stage / 'Install.command').chmod(0o755)
    (binary / 'CodexSwitch').chmod(0o755)
    with (app / 'Contents/Info.plist').open('rb') as file:
        metadata = plistlib.load(file)
    if metadata['CFBundleVersion'] != version or metadata['CFBundleShortVersionString'] != version:
        raise SystemExit('Native bundle version must match source')
    architecture = subprocess.check_output(['lipo', '-archs', str(binary / 'CodexSwitch')], text=True).strip()
    if architecture != ('arm64' if a.rid == 'osx-arm64' else 'x86_64'):
        raise SystemExit('Unexpected Mach-O entry-point architecture')
    for file in sorted(binary.iterdir()):
        if file.is_file() and file.name != 'CodexSwitch':
            subprocess.run(['codesign', '--force', '--sign', '-', '--timestamp=none', '--options', 'runtime',
                            '--entitlements', str(resources / 'entitlements.plist'), str(file)], check=True)
    subprocess.run(['codesign', '--force', '--sign', '-', '--timestamp=none', '--options', 'runtime',
                    '--entitlements', str(resources / 'entitlements.plist'), str(app)], check=True)
    subprocess.run(['codesign', '--verify', '--deep', '--strict', str(app)], check=True)
    for name in ('OpenAI-Codex.LICENSE.txt', 'OpenAI-Codex.NOTICE.txt'):
        if (resources / 'licenses' / name).read_bytes() != (root / 'licenses' / name).read_bytes():
            raise SystemExit('Bundled model instruction license or notice is missing or changed')
    subprocess.run(['ditto', '-c', '-k', '--sequesterRsrc', '--keepParent', str(app), str(archive)], check=True)
    subprocess.run(['/usr/bin/zip', '-q', str(archive), 'Install.command'], cwd=stage, check=True)
    signing = 'Native ad-hoc signature verified; no Developer ID or Apple notarization'
with archive.open('rb') as stream:
    digest = hashlib.file_digest(stream, 'sha256').hexdigest()
receipt = {'schema': 1, 'sourceRevision': head, 'version': version, 'rid': a.rid,
           'nativeOs': platform.system(), 'nativeArchitecture': platform.machine(), 'signing': signing,
           'artifact': {'file': archive.name, 'bytes': archive.stat().st_size, 'sha256': digest},
           'scope': 'Native published app packaged with complete runtime; account switching acceptance is separate.'}
(output / 'PROVENANCE.json').write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
(output / 'SHA256SUMS').write_text(digest + '  ' + archive.name + '\n', encoding='utf-8')
print(json.dumps(receipt))
