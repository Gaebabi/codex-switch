"""Verify declared KDE6 providers in disposable native containers without mounting personal state."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import sys
import urllib.request
import uuid

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--architecture',choices=['X64','Arm64'],required=True)
p.add_argument('--tests',type=Path,required=True)
p.add_argument('--desktop-tests',type=Path,required=True)
p.add_argument('--app',type=Path,required=True)
p.add_argument('--client-package',type=Path,required=True)
p.add_argument('--output',type=Path,required=True)
p.add_argument('--revision',required=True)
a=p.parse_args()
if sys.platform!='linux' or os.geteuid()==0 or {'x86_64':'X64','aarch64':'Arm64'}.get(platform.machine())!=a.architecture:raise SystemExit('Use a normal user on the declared actual Linux architecture')
if not re.fullmatch('[0-9a-f]{40}',a.revision):raise SystemExit('Exact capture source revision required')
repository=Path(__file__).resolve().parents[1];output=a.output.resolve()
if not output.is_relative_to(repository/'artifacts') or output==repository/'artifacts' or output.exists():raise SystemExit('Choose a fresh dedicated ignored output directory')
tests=a.tests.resolve();desktop_tests=a.desktop_tests.resolve();app=a.app.resolve();package=a.client_package.resolve()
if not(tests/'CodexSwitch.LinuxVault.Tests.dll').is_file() or not(desktop_tests/'CodexSwitch.LinuxDesktop.Tests.dll').is_file() or not(app/'CodexSwitch').is_file():raise SystemExit('Build the native tests and self-contained source app first')
providers=json.loads((repository/'src/platforms/security/LinuxSecretProviders.json').read_text(encoding='utf-8'))['providers']
client_manifest=json.loads((repository/'src/platforms/client/LinuxClients.json').read_text(encoding='utf-8'));client=client_manifest['architectures'][a.architecture]
if hashlib.sha256(package.read_bytes()).hexdigest()!=client['packageSha256']:raise SystemExit('The actual reviewed official client package is required')
output.mkdir(parents=True,mode=0o700)
scripts=output/'public-scripts';scripts.mkdir()
for name in ('verify-linux-keyring.py','verify-ui-evidence.py','ksecrets-test-control.py','capture-linux-app.py','verify-linux-desktop.py'):shutil.copyfile(repository/'scripts'/name,scripts/name)
for provider in (v for v in providers if v['architecture']==a.architecture):
 own=output/provider['id'];own.mkdir(mode=0o700);context=own/'image';context.mkdir(mode=0o700)
 for entry in provider['packages']:
  file=context/entry['file']
  if file.parent!=context or not re.fullmatch(r'https://snapshot\.debian\.org/file/[0-9a-f]{40}',entry['url']):raise SystemExit('Unexpected package source')
  with urllib.request.urlopen(entry['url'],timeout=45) as response:
   data=response.read(8_000_001)
  if len(data)>8_000_000 or hashlib.sha256(data).hexdigest()!=entry['sha256']:raise SystemExit('Reviewed KDE package checksum changed')
  file.write_bytes(data)
  entry['architecture']='all' if entry['file'].endswith('_all.deb') else client['debianArchitecture']
 shutil.copyfile(package,context/'client.deb')
 description={'provider':provider,'client':{'name':client_manifest['package'],'version':client_manifest['packageVersion'],'architecture':client['debianArchitecture'],'file':'client.deb','sha256':client['packageSha256']}}
 (context/'context.json').write_text(json.dumps(description),encoding='utf-8')
 shutil.copyfile(repository/'scripts/verify-kde6-image.py',context/'verify-kde6-image.py')
 image='codex-switch-native-'+provider['id']+':'+a.revision[:12]+'-'+a.architecture.lower()
 if not re.fullmatch(r'debian@sha256:[0-9a-f]{64}',provider['image']):raise SystemExit('Pinned disposable base image required')
 subprocess.run(['docker','build','--file',str(repository/'scripts/kde6.Dockerfile'),'--build-arg','BASE_IMAGE='+provider['image'],'--build-arg','TEST_UID='+str(os.getuid()),'--tag',image,str(context)],check=True,timeout=600)
 result=own/'results';result.mkdir(mode=0o700)
 def container(arguments,timeout,desktop=False):
  name='codex-switch-test-'+uuid.uuid4().hex
  command=['docker','run','--init','--name',name,'--label','codex-switch-verification='+a.revision,'--network','none','--pids-limit','512' if desktop else '256','--mount','type=bind,source='+str(tests)+',target=/tests,readonly','--mount','type=bind,source='+str(app)+',target=/app,readonly','--mount','type=bind,source='+str(scripts)+',target=/work,readonly','--mount','type=bind,source='+str(result)+',target=/results']
  if desktop:command+=['--security-opt','seccomp=unconfined','--env','CODEX_SWITCH_DISPOSABLE_RUNNER=true','--mount','type=bind,source='+str(desktop_tests)+',target=/desktop-tests,readonly']
  command.append(image)
  try:subprocess.run([*command,*arguments],check=True,timeout=timeout)
  finally:
   inspected=subprocess.run(['docker','inspect',name],capture_output=True,text=True,timeout=10)
   if inspected.returncode==0:
    owned=json.loads(inspected.stdout)
    if len(owned)!=1 or owned[0]['Config']['Labels'].get('codex-switch-verification')!=a.revision or owned[0]['Name']!='/'+name:raise RuntimeError('Unexpected container cleanup identity')
    subprocess.run(['docker','rm','--force',owned[0]['Id']],check=True,capture_output=True,timeout=15)
   elif 'No such object' not in inspected.stderr:raise RuntimeError('Owned container cleanup inspection failed')
 container(['python3','/work/verify-linux-keyring.py','--provider',provider['id'],'--assembly','/tests/CodexSwitch.LinuxVault.Tests.dll','--architecture',a.architecture,'--report','/results/provider.json'],300)
 if provider['id']=='kwallet6':container(['python3','/work/verify-linux-desktop.py','--assembly','/desktop-tests/CodexSwitch.LinuxDesktop.Tests.dll','--architecture',a.architecture,'--report','/results/desktop'],180,desktop=True)
 container(['python3','/work/capture-linux-app.py','--app','/app/CodexSwitch','--output','/results/raw'],120)
 subprocess.run([sys.executable,str(scripts/'verify-ui-evidence.py'),str(result/'raw'),str(result/'public'),'--os','Linux','--revision',a.revision],check=True,timeout=45)
 summary=json.loads((result/'provider.json').read_text(encoding='utf-8'))
 summary.update({'providerExecutableSha256':provider['sha256'],'containerImage':provider['image'],'encodingContract':provider['contract'],'sourceRevision':a.revision})
 (result/'public/provider.json').write_text(json.dumps(summary,indent=2)+'\n',encoding='utf-8')
 print(json.dumps({'provider':provider['id'],'architecture':a.architecture,'passed':summary['passed'],'phaseCount':len(summary['phases']),'actualAppScreenshots':12}),flush=True)
