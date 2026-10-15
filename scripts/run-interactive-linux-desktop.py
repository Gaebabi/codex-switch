"""Start an owner-operated loopback desktop from a source-verified offline image."""
import argparse
import json
from pathlib import Path
import re
import shutil
import subprocess
import time
import urllib.request
import uuid

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--desktop-image', required=True, help='Image produced by run-local-linux-desktop.py')
p.add_argument('--output', required=True, type=Path, help='Fresh private directory below artifacts')
a = p.parse_args()
repository = Path(__file__).resolve().parents[1]


def run(arguments, **kwargs):
    return subprocess.run(arguments, check=True, timeout=kwargs.pop('timeout', 30), **kwargs)


def value(arguments):
    return run(arguments, capture_output=True, text=True).stdout.strip()


def inspect(identifier):
    return json.loads(value(['docker', 'inspect', identifier]))[0]


if re.fullmatch(r'codex-switch-desktop-session:[0-9a-f]{12}', a.desktop_image) is None:
    raise SystemExit('Use the local image name produced by the verified offline desktop runner')
engine = json.loads(value(['docker', 'info', '--format', '{{json .}}']))
if engine['OSType'] != 'linux' or engine['Architecture'] not in ('x86_64', 'amd64'):
    raise SystemExit('A native x64 Linux Docker engine is required')
if engine.get('MemTotal', 0) < 6 * 1024 ** 3 - 256 * 1024 ** 2:
    raise SystemExit('Reserve at least 6GiB for the Linux engine before an authenticated desktop session')
if value(['git', '-C', str(repository), 'status', '--porcelain']):
    raise SystemExit('Commit the scoped recipe before source-bound interactive verification')
output = a.output.resolve()
artifacts = repository / 'artifacts'
if output.exists() or not output.is_relative_to(artifacts) or output == artifacts:
    raise SystemExit('Use a fresh private directory below ignored artifacts')
base = inspect(a.desktop_image)
if base['Config']['User'] != '1000:1000' or base['Config'].get('Volumes'):
    raise SystemExit('Unexpected desktop image user or implicit mounts')
digest = base['Id']
if re.fullmatch(r'sha256:[0-9a-f]{64}', digest) is None:
    raise SystemExit('Expected an immutable local desktop image ID')
references = [entry for entry in base.get('RepoDigests', []) if re.fullmatch(r'codex-switch-desktop-session@sha256:[0-9a-f]{64}', entry)]
if len(references) != 1 or inspect(references[0])['Id'] != digest:
    raise SystemExit('The local image store must expose its verified content-addressed desktop reference')
base_reference = references[0]
output.mkdir(parents=True, mode=0o700)
probe_name = 'codex-switch-image-check-' + uuid.uuid4().hex
probe = value(['docker', 'create', '--name', probe_name, '--network', 'none', '--cap-drop', 'ALL',
               '--label', 'codex-switch-interactive-probe=true', '--entrypoint', '/bin/false', digest])
try:
    run(['docker', 'cp', probe + ':/work/expected.json', str(output / 'base.json')], capture_output=True)
finally:
    item = inspect(probe)
    if item['Name'] != '/' + probe_name or item['Config']['Labels'].get('codex-switch-interactive-probe') != 'true' or item['Mounts']:
        raise RuntimeError('Refuse cleanup outside the original image inspection container')
    run(['docker', 'rm', probe], capture_output=True)
manifest = output / 'base.json'
if not manifest.is_file() or manifest.stat().st_size > 128 * 1024:
    raise SystemExit('Invalid base image source receipt')
expected = json.loads(manifest.read_text(encoding='utf-8'))
revision = expected.get('candidateRevision')
if not isinstance(revision, str) or re.fullmatch(r'[0-9a-f]{40}', revision) is None or expected.get('architecture') != 'X64':
    raise SystemExit('Expected an exact native x64 candidate revision')
run(['git', 'cat-file', '-e', revision + '^{commit}'], cwd=repository, capture_output=True)
head = value(['git', '-C', str(repository), 'rev-parse', 'HEAD'])
run(['git', 'diff', '--quiet', '--no-ext-diff', '--no-textconv', '--end-of-options', revision, '--',
     'src', 'tests', 'Directory.Build.props', 'global.json', 'localization', 'resources', 'assets', 'licenses', 'LICENSE'], cwd=repository)
context = output / 'context'
context.mkdir(mode=0o700)
recipe = repository / 'scripts/linux-desktop-session'
for name in ('interactive.Dockerfile', 'interactive-session.py', 'prepare-keyring.py'):
    shutil.copyfile(recipe / name, context / name)
image = 'codex-switch-interactive:' + uuid.uuid4().hex
with (output / 'build-private.log').open('wb') as log:
    run(['docker', 'build', '--file', str(context / 'interactive.Dockerfile'), '--build-arg', 'DESKTOP_IMAGE=' + base_reference,
         '--tag', image, str(context)], stdout=log, stderr=subprocess.STDOUT, timeout=600)
image_id = inspect(image)['Id']
name = 'codex-switch-owner-desktop-' + uuid.uuid4().hex
network = 'codex-switch-owner-' + uuid.uuid4().hex
value(['docker', 'network', 'create', '--label', 'codex-switch-owner-session=true', network])
identifier = value(['docker', 'create', '--init', '--name', name, '--label', 'codex-switch-owner-session=true',
                    '--network', network, '--publish', '127.0.0.1::6080', '--cap-drop', 'ALL', '--security-opt', 'seccomp=unconfined',
                    '--memory', '4g', '--memory-swap', '4g', '--cpus', '3', '--pids-limit', '1024', '--shm-size', '512m',
                    '--tmpfs', '/tmp:rw,nosuid,nodev,mode=1777', '--env', 'CODEX_SWITCH_INTERACTIVE=owner-operated', image_id])
receipt = {'id': identifier, 'name': name, 'network': network, 'image': image_id, 'baseImage': digest,
           'candidateRevision': revision, 'testRevision': head, 'automaticCapture': False,
           'hostHomesMounted': False, 'ownerAuthenticationPending': True}
(output / 'owned-container.json').write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
item = inspect(identifier)
host = item['HostConfig']
if item['Config']['User'] != '1000:1000' or item['Mounts'] or host['Privileged'] or host['CapDrop'] != ['ALL'] or host['CapAdd'] or host['Devices'] or host['PidMode'] or host['IpcMode'] != 'private' or host['NetworkMode'] != network or host['PortBindings'] != {'6080/tcp': [{'HostIp': '127.0.0.1', 'HostPort': ''}]}:
    raise RuntimeError('Private interactive desktop isolation mismatch; inspect the owned receipt')
run(['docker', 'start', identifier], capture_output=True)
deadline = time.monotonic() + 60
while time.monotonic() < deadline:
    ready = subprocess.run(['docker', 'exec', identifier, 'test', '-f', '/home/verifier/interactive/ready'], capture_output=True, timeout=5)
    if ready.returncode == 0:
        break
    if not inspect(identifier)['State']['Running']:
        raise RuntimeError('Desktop startup failed; private diagnostics remain inside the owned container')
    time.sleep(0.5)
else:
    raise TimeoutError('Interactive desktop initialization did not finish')
ports = inspect(identifier)['NetworkSettings']['Ports']
if set(ports) != {'6080/tcp'} or len(ports['6080/tcp']) != 1 or ports['6080/tcp'][0]['HostIp'] != '127.0.0.1':
    raise RuntimeError('Only one loopback viewer port may be published')
port = int(ports['6080/tcp'][0]['HostPort'])
access = json.loads(value(['docker', 'exec', identifier, 'cat', '/home/verifier/interactive/viewer-private.json']))
url = f'http://127.0.0.1:{port}/vnc.html#autoconnect=1&resize=scale&quality=9&compression=2&logging=error&password=' + access['password']
with urllib.request.urlopen(f'http://127.0.0.1:{port}/vnc.html', timeout=5) as response:
    if response.status != 200 or b'noVNC' not in response.read(100000):
        raise RuntimeError('Loopback viewer is not ready')
(output / 'viewer-private.json').write_text(json.dumps({'url': url, 'port': port}), encoding='utf-8')
receipt.update({'port': port, 'httpReady': True, 'loopbackOnly': True})
(output / 'owned-container.json').write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'ready': True, 'name': name, 'port': port, 'viewerFile': str(output / 'viewer-private.json'),
                  'scope': 'Owner-operated Docker/Xfce/X11 userspace; authentication and desktop acceptance remain pending'}))
# Deliberately retain this owned container, including its private encrypted data.
# Closing the terminal or browser must not delete the owner's new Linux login.
