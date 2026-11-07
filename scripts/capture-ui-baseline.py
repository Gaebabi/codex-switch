"""Capture the unmodified pre-upgrade Mac app on the same native CI worker."""
import argparse
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import platform
import re
import subprocess
import sys
import tarfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--revision', required=True)
parser.add_argument('--rid', choices=['osx-arm64', 'osx-x64'], required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
if sys.platform != 'darwin' or platform.machine() != ('arm64' if args.rid == 'osx-arm64' else 'x86_64'):
    raise SystemExit('The declared native Mac architecture is required')
if not re.fullmatch('[a-f0-9]{40}', args.revision):
    raise SystemExit('An exact original source revision is required')
output = args.output.resolve()
if output.exists() or not output.is_relative_to(root / 'artifacts'):
    raise SystemExit('Use a fresh ignored output directory')

def run(command, **kwargs):
    return subprocess.run(command, check=True, timeout=kwargs.pop('timeout', 60), **kwargs)

if subprocess.run(['git', 'cat-file', '-e', args.revision + '^{commit}'], cwd=root, capture_output=True).returncode:
    run(['git', 'fetch', '--no-tags', '--depth=1', 'origin', args.revision], cwd=root)
revision = run(['git', 'rev-parse', 'HEAD'], cwd=root, capture_output=True, text=True).stdout.strip()
tree = run(['git', 'rev-parse', args.revision + '^{tree}'], cwd=root, capture_output=True, text=True).stdout.strip()
archive = run(['git', 'archive', args.revision], cwd=root, capture_output=True).stdout
source = output / 'source'
source.mkdir(parents=True)
with tarfile.open(fileobj=io.BytesIO(archive)) as bundle:
    entries = bundle.getmembers()
    if sum(entry.size for entry in entries) > 500_000_000:
        raise SystemExit('Original source archive exceeds the declared budget')
    for entry in entries:
        path = PurePosixPath(entry.name)
        if path.is_absolute() or '..' in path.parts or not (entry.isfile() or entry.isdir()):
            raise SystemExit('Unsafe original archive entry')
    bundle.extractall(source, filter='data')
app = output / 'published'
run(['dotnet', 'publish', str(source / 'src/macos/CodexSwitch.Mac.csproj'), '-c', 'Release', '-r', args.rid,
     '--self-contained', 'true', '-p:UseSharedCompilation=false', '-o', str(app)], cwd=source, timeout=300)
raw, public = output / 'raw', output / 'public'
run([str(app / 'CodexSwitch'), '--verify-ui', str(raw)], cwd=source, timeout=90)
run([sys.executable, str(source / 'scripts/verify-ui-evidence.py'), str(raw), str(public), '--os', 'macOS', '--revision', args.revision], cwd=source)
receipt = json.loads((public / 'evidence.json').read_text(encoding='utf-8'))
if receipt['architecture'] != ('Arm64' if args.rid == 'osx-arm64' else 'X64') or receipt['revision'] != args.revision:
    raise SystemExit('Original native source or architecture mismatch')
receipt.update(applicationSource=args.revision, originalTree=tree, sourceArchiveSha256=hashlib.sha256(archive).hexdigest(),
               revision=revision, scope='Unmodified pre-upgrade published Mac app, existing isolated native UI diagnostics and fictional inputs. Actual own NSWindows only; no personal home, credentials or Codex actions.')
(public / 'evidence.json').write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'originalSource': args.revision, 'testSource': revision, 'nativeImages': len(receipt['images']), 'sourceInstrumented': False}))
