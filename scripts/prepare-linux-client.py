"""Install the checksum-pinned official client only on an explicitly disposable native CI runner."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import subprocess
import sys
import urllib.request

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--architecture',choices=['X64','Arm64'],required=True)
p.add_argument('--package-file',type=Path,required=True)
a=p.parse_args()
if sys.platform!='linux' or os.environ.get('GITHUB_ACTIONS')!='true' or os.environ.get('CODEX_SWITCH_DISPOSABLE_RUNNER')!='true':
    raise SystemExit('Client installation is limited to the explicitly disposable native CI job')
actual={'x86_64':'X64','aarch64':'Arm64'}.get(platform.machine())
if actual!=a.architecture or os.geteuid()==0:raise SystemExit('Use the declared native architecture and normal runner user')
manifest=json.loads(Path('src/platforms/client/LinuxClients.json').read_text(encoding='utf-8'));client=manifest['architectures'][a.architecture]
file=a.package_file.resolve();allowed=(Path.cwd()/'artifacts').resolve()
if not file.is_relative_to(allowed):raise SystemExit('Client package must stay inside owned ignored artifacts')
file.parent.mkdir(parents=True,exist_ok=True)
url=client['packageUrl']
expected='https://persistent.oaistatic.com/codex-app-prod/linux/deb/latest/chatgpt_'+client['debianArchitecture']+'.deb'
if url!=expected:raise SystemExit('Unexpected client package origin')
if not file.exists():
    request=urllib.request.Request(url,headers={'User-Agent':'codex-switch-native-client-verification'})
    with urllib.request.urlopen(request,timeout=45) as response, file.open('xb') as output:
        if response.url!=url:raise SystemExit('Unexpected client package redirect')
        count=0
        while chunk:=response.read(1024*1024):
            count+=len(chunk)
            if count>600_000_000:raise SystemExit('Pinned package exceeds its inspection budget')
            output.write(chunk)
digest=hashlib.sha256()
with file.open('rb') as source:
    while chunk:=source.read(1024*1024):digest.update(chunk)
if digest.hexdigest()!=client['packageSha256']:raise SystemExit('Official download changed; review/pin the new upstream package instead of executing it')
metadata=subprocess.run(['/usr/bin/dpkg-deb','--field',str(file),'Package','Version','Architecture'],capture_output=True,text=True,check=True).stdout
fields=dict(line.split(': ',1) for line in metadata.splitlines())
if fields!={'Package':manifest['package'],'Version':manifest['packageVersion'],'Architecture':client['debianArchitecture']}:raise SystemExit('Pinned package identity mismatch')
subprocess.run(['sudo','env','DEBIAN_FRONTEND=noninteractive','apt-get','install','--no-install-recommends','-y',str(file)],check=True,timeout=480)
print(json.dumps({'package':manifest['package'],'version':manifest['packageVersion'],'architecture':a.architecture,'sha256Verified':True,
    'scope':'Actual pinned official client installed on disposable CI only; no app launch, personal account, login, inference, utility Release or clean-machine acceptance.'}))
