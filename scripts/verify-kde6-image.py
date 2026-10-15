"""Install only the preverified public native test packages inside a disposable image."""
import hashlib
import json
from pathlib import Path
import platform
import subprocess

root=Path('/verified-input')
manifest=json.loads((root/'context.json').read_text(encoding='utf-8'))
provider=manifest['provider'];client=manifest['client']
architecture={'x86_64':'X64','aarch64':'Arm64'}.get(platform.machine())
if architecture!=provider['architecture']:raise SystemExit('Container must execute on the declared native architecture')
files=[]
for package in [*provider['packages'],client]:
 file=root/package['file']
 if file.parent!=root or hashlib.sha256(file.read_bytes()).hexdigest()!=package['sha256']:raise SystemExit('Native package checksum mismatch')
 metadata=subprocess.run(['/usr/bin/dpkg-deb','--field',str(file),'Package','Version','Architecture'],capture_output=True,text=True,check=True).stdout
 fields=dict(line.split(': ',1) for line in metadata.splitlines())
 if fields!={'Package':package['name'],'Version':package['version'],'Architecture':package['architecture']}:raise SystemExit('Native package metadata mismatch')
 files.append(str(file))
subprocess.run(['/usr/bin/apt-get','install','--no-install-recommends','-y',*files],check=True,timeout=480)
image=Path(provider['executable'])
if image.is_symlink() or hashlib.sha256(image.read_bytes()).hexdigest()!=provider['sha256']:raise SystemExit('Installed daemon does not match the audited contract')
print(json.dumps({'provider':provider['id'],'version':provider['version'],'architecture':architecture,'nativeImageVerified':True}))
