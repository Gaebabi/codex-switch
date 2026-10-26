"""Compare unchanged baseline/coordinator retry UI on disposable hosted Windows."""
import hashlib, io, json, os, shutil, subprocess, tarfile
from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parents[1]
if os.environ.get('GITHUB_ACTIONS') != 'true' or os.environ.get('RUNNER_OS') != 'Windows':
    raise SystemExit('Native retry evidence requires disposable hosted Windows CI')
baseline = 'b626e787493ab20ccdcc583a54b3b2eef092a6b9'
output = root / 'artifacts/switch-retries'
output.mkdir(parents=True, exist_ok=False)
source = output / 'baseline-source'

def run(*args, cwd=root):
    result = subprocess.run(args, cwd=cwd, creationflags=subprocess.CREATE_NO_WINDOW, capture_output=True, timeout=300)
    if result.returncode:
        raise RuntimeError(result.stderr.decode('utf-8', errors='replace')[-4000:] or result.stdout.decode('utf-8', errors='replace')[-4000:])
    return result.stdout

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

run('git', 'fetch', '--no-tags', '--depth=1', 'origin', baseline)
assert run('git', 'rev-parse', baseline + '^{commit}').decode().strip() == baseline
with tarfile.open(fileobj=io.BytesIO(run('git', 'archive', baseline))) as archive:
    for member in archive.getmembers():
        path = Path(member.name)
        if path.is_absolute() or '..' in path.parts or not (member.isfile() or member.isdir()):
            raise ValueError('Unsafe baseline archive')
    archive.extractall(source, filter='data')
original = {str(p.relative_to(source)): sha(p) for p in (source / 'src').rglob('*') if p.is_file()}
shutil.copytree(root / 'tests/switch-retries', source / 'tests/switch-retries', ignore=shutil.ignore_patterns('bin', 'obj'))
for name, workspace, extra in [('before', source, ['--baseline']), ('after', root, [])]:
    run('dotnet', 'run', '--project', 'tests/switch-retries/SwitchRetry.Evidence.csproj', '-c', 'Release', '--no-launch-profile', '-p:UseSharedCompilation=false', '--', str(output / name), *extra, cwd=workspace)
assert all(sha(source / path) == value for path, value in original.items())
public = output / 'public'
public.mkdir()
images = []
for folder, cases in [('before', ['before']), ('after', ['retry-1', 'retry-2', 'after', 'exhausted'])]:
    for language in ['en', 'ko']:
        for case in cases:
            filename = language + '-' + case + '.png'
            with Image.open(output / folder / filename) as image:
                pixels = image.convert('RGBA').tobytes()
                size = image.size
                image.save(public / filename, format='PNG')
            with Image.open(public / filename) as clean:
                assert not clean.info and clean.size == size and clean.convert('RGBA').tobytes() == pixels and clean.entropy() > 1
            images.append({'file': filename, 'width': size[0], 'height': size[1], 'sha256': sha(public / filename), 'originalPixelsUnchanged': True})
receipt = {'baselineSource': baseline, 'sourceRevision': os.environ.get('EVIDENCE_REVISION') or run('git', 'rev-parse', 'HEAD').decode().strip(),
           'before': json.loads((output / 'before/receipt.json').read_text('utf-8')), 'after': json.loads((output / 'after/receipt.json').read_text('utf-8')),
           'baselineProductionFilesUnchanged': True, 'personalDataUsed': False, 'images': images,
           'sourceHashes': {p: sha(root / p) for p in ['src/core/AccountSwitchCoordinator.cs', 'src/windows/MainWindow.cs', 'src/desktop/DeskWindow.cs', 'tests/switch-retries/Program.cs', 'scripts/verify-switch-retries.py']},
           'scope': 'Actual own-HWND WPF and production coordinator/progress/save callbacks with fictional accounts and offline lifecycle backend. Fixture sets busy context; no actual Switch click, authenticated account/client or live Continue.'}
assert receipt['before']['passed'] == 12 and receipt['after']['passed'] == 30 and len(images) == 10
(public / 'verification.json').write_text(json.dumps(receipt, indent=2) + '\n', 'utf-8')
print(json.dumps({'nativeBefore': 12, 'nativeAfter': 30, 'images': 10, 'sourceRevision': receipt['sourceRevision']}))
