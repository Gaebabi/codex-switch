"""Test a complete reviewed x64 candidate in an owned offline Docker desktop session."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import uuid

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--packages', type=Path, required=True)
p.add_argument('--upstream-package', type=Path, required=True)
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
repository = Path(__file__).resolve().parents[1]

def run(command, **kwargs):
    return subprocess.run(command, check=True, timeout=kwargs.pop('timeout', 30), **kwargs)

def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()

def git(*arguments):
    return run(['git', *arguments], cwd=repository, capture_output=True, text=True).stdout.strip()

head = git('rev-parse', 'HEAD')
if git('status', '--porcelain'):
    raise SystemExit('Commit the scoped test recipe before source-bound verification')
packages = a.packages.resolve(strict=True)
provenance = json.loads((packages / 'PROVENANCE.json').read_text(encoding='utf-8'))
if provenance['rid'] != 'linux-x64' or provenance['verificationOnlyVersionOverride'] or not provenance['unsignedPreview'] or provenance['supportedAccountHandoff']:
    raise SystemExit('Use the complete native x64 unsigned preview candidate')
git('diff', '--exit-code', provenance['sourceRevision'], head, '--', 'src', 'tests', 'Directory.Build.props')
artifacts = {entry['file']: entry for entry in provenance['artifacts']}
for name, entry in artifacts.items():
    file = packages / name
    if file.parent != packages or digest(file) != entry['sha256']:
        raise SystemExit('Candidate artifact identity mismatch')
deb = [packages / name for name in artifacts if name.endswith('.deb')]
tar = [packages / name for name in artifacts if name.endswith('.tar.gz')]
if len(deb) != 1 or len(tar) != 1:
    raise SystemExit('Exactly one complete tar and deb candidate is required')
client = json.loads((repository / 'src/platforms/client/LinuxClients.json').read_text(encoding='utf-8'))
upstream = a.upstream_package.resolve(strict=True)
if digest(upstream) != client['architectures']['X64']['packageSha256']:
    raise SystemExit('The actual upstream package must match the reviewed checksum')
engine = json.loads(run(['docker', 'info', '--format', '{{json .}}'], capture_output=True, text=True).stdout)
if engine['OSType'] != 'linux' or engine['Architecture'] not in ('x86_64', 'amd64'):
    raise SystemExit('An actual native x64 Linux Docker engine is required')
output = a.output.resolve()
if output.exists() or not output.is_relative_to(repository / 'artifacts') or output == repository / 'artifacts':
    raise SystemExit('Use a fresh result directory below ignored repository artifacts')
output.mkdir(parents=True, mode=0o700)
context = output / 'context'
context.mkdir(mode=0o700)
with (context / 'source.tar').open('wb') as archive:
    run(['git', 'archive', provenance['sourceRevision'], 'src', 'tests', 'Directory.Build.props', 'global.json', 'localization', 'resources', 'assets', 'licenses'], cwd=repository, stdout=archive)
shutil.copyfile(deb[0], context / 'candidate.deb')
shutil.copyfile(tar[0], context / 'candidate.tar.gz')
shutil.copyfile(upstream, context / 'upstream.deb')
recipe = repository / 'scripts/linux-desktop-session'
shutil.copyfile(recipe / 'verify-session.py', context / 'verify-session.py')
expected = {'schema': 1, 'testRevision': head, 'candidateRevision': provenance['sourceRevision'], 'candidateVersion': provenance['version'], 'candidateSha256': digest(deb[0]), 'upstreamSha256': digest(upstream), 'architecture': 'X64'}
(context / 'expected.json').write_text(json.dumps(expected), encoding='utf-8')
image = 'codex-switch-desktop-session:' + head[:12]
with (output / 'build-private.log').open('wb') as log:
    build = subprocess.run(['docker', 'build', '--file', str(recipe / 'Dockerfile'), '--tag', image, str(context)], stdout=log, stderr=subprocess.STDOUT, timeout=1200)
if build.returncode:
    print((output / 'build-private.log').read_text(encoding='utf-8', errors='replace')[-5000:])
    raise SystemExit(build.returncode)
name = 'codex-switch-desktop-session-' + uuid.uuid4().hex
identifier = run(['docker', 'create', '--init', '--name', name, '--label', 'codex-switch-desktop-session=' + head, '--network', 'none', '--cap-drop', 'ALL', '--security-opt', 'seccomp=unconfined', '--memory', '2g', '--cpus', '2', '--pids-limit', '512', '--shm-size', '256m', '--tmpfs', '/tmp:rw,nosuid,nodev,mode=1777', image], capture_output=True, text=True).stdout.strip()
(output / 'owned-container.json').write_text(json.dumps({'id': identifier, 'name': name, 'testRevision': head, 'image': image}), encoding='utf-8')
try:
    item = json.loads(run(['docker', 'inspect', identifier], capture_output=True, text=True).stdout)[0]
    if item['Name'] != '/' + name or item['Config']['Labels'].get('codex-switch-desktop-session') != head or item['Mounts'] or item['HostConfig']['PortBindings'] or item['HostConfig']['Privileged'] or item['HostConfig']['NetworkMode'] != 'none' or item['Config']['User'] != '1000:1000':
        raise RuntimeError('Owned desktop container isolation contract mismatch')
    with (output / 'session-private.log').open('wb') as log:
        session = subprocess.run(['docker', 'start', '--attach', identifier], stdout=log, stderr=subprocess.STDOUT, timeout=600)
    run(['docker', 'cp', identifier + ':/results/.', str(output / 'results')])
    if session.returncode:
        print((output / 'session-private.log').read_text(encoding='utf-8', errors='replace')[-5000:])
        raise SystemExit(session.returncode)
    result = json.loads((output / 'results/session.json').read_text(encoding='utf-8'))
    if result['failed'] or result['expected'] != expected or not result['ordinaryUi'] or result['accountsUsed']:
        raise RuntimeError('Native desktop result identity/scope mismatch')
    result['isolation'] = {'user': item['Config']['User'], 'hostMounts': False, 'publishedPorts': False, 'runtimeNetwork': 'none', 'privileged': False, 'capabilities': 'all dropped', 'seccomp': 'unconfined for actual Chromium user-namespace sandbox; client sandbox remains enabled', 'memoryBytes': item['HostConfig']['Memory'], 'cpus': 2, 'pidsLimit': 512}
    (output / 'results/session.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({key: result[key] for key in ('passed', 'failed', 'ordinaryUi', 'accountsUsed', 'scope')}))
finally:
    item = json.loads(run(['docker', 'inspect', identifier], capture_output=True, text=True).stdout)[0]
    if item['Name'] != '/' + name or item['Config']['Labels'].get('codex-switch-desktop-session') != head or item['Mounts']:
        raise RuntimeError('Refuse cleanup of an unexpected container')
    run(['docker', 'rm', '--force', identifier], capture_output=True)
