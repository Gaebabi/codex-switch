"""Verify package-manager installation/removal and source-app startup on an owned clean container."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--prepare',type=int)
p.add_argument('--family',choices=['deb','rpm'])
p.add_argument('--report',type=Path)
p.add_argument('--raw-ui',type=Path)
a=p.parse_args()
home=Path('/home/verifier');codex=home/'.codex';vault=home/'.local/share/CodexAccountDesk'
markers={codex/'auth.json':b'Owned synthetic preserved authentication marker\x00\xff',vault/'accounts.vault':b'Owned synthetic preserved encrypted-vault marker\x00\xff'}
if a.prepare is not None:
 if os.geteuid()!=0 or a.prepare<=0 or home.is_symlink() or not home.is_dir():raise SystemExit('Only a fresh image build may prepare its declared normal-user marker data')
 for file,value in markers.items():
  file.parent.mkdir(parents=True,mode=0o700,exist_ok=True);file.write_bytes(value);file.chmod(0o600)
 for directory in [home,*[file for file in home.rglob('*') if file.is_dir()]]:os.chown(directory,a.prepare,a.prepare);directory.chmod(0o700)
 for file in markers:os.chown(file,a.prepare,a.prepare)
 raise SystemExit(0)
if os.geteuid()==0 or os.environ.get('CODEX_SWITCH_DISPOSABLE_RUNNER')!='true' or a.family is None or a.report is None or a.raw_ui is None:raise SystemExit('An explicitly disposable normal-user package worker is required')
checks=[]
def check(name,value):
 if not value:raise RuntimeError('Native system package contract failed: '+name)
 checks.append(name)
check('No global .NET host/SDK/runtime is installed',shutil.which('dotnet') is None and not Path('/usr/share/dotnet').exists())
for file,value in markers.items():check('System package installation preserves pre-existing private marker bytes/modes',file.read_bytes()==value and file.stat().st_mode&0o777==0o600)
app=Path('/opt/codex-switch/CodexSwitch')
value=json.loads(subprocess.run([str(app),'--package-info'],capture_output=True,text=True,check=True,timeout=20).stdout)
check('The installed complete native application runs without .NET installation',value['runtime']=='.NET 10.0.13')
subprocess.run(['desktop-file-validate','/usr/share/applications/codex-switch.desktop'],check=True,timeout=10)
check('Menu and canonical icon are installed through the package manager',Path('/usr/share/icons/hicolor/1024x1024/apps/codex-switch.png').is_file() and Path('/usr/bin/codex-switch').is_file())
subprocess.run([sys.executable,'/input/capture-linux-app.py','--app',str(app),'--output',str(a.raw_ui.resolve())],check=True,timeout=120)
check('The actual installed program renders12 own-window source UI confirmations',len(list(a.raw_ui.glob('*.png')))==12)
if a.family=='deb':
 native=subprocess.run(['dpkg-query','-W','-f=${Package} ${Version}\n','libc6','libicu*','libglib2.0-0t64','libgcrypt20','libsqlite3-0'],capture_output=True,text=True,check=True).stdout.splitlines()
 subprocess.run(['sudo','/usr/bin/dpkg','--purge','codex-switch'],check=True,timeout=60)
else:
 native=subprocess.run(['rpm','-q','glibc','libicu','glib2','libgcrypt','sqlite-libs'],capture_output=True,text=True,check=True).stdout.splitlines()
 subprocess.run(['sudo','/usr/bin/rpm','-e','codex-switch'],check=True,timeout=60)
check('Package-manager removal deletes program/menu/icon integration',not app.exists() and not Path('/usr/share/applications/codex-switch.desktop').exists() and not Path('/usr/share/icons/hicolor/1024x1024/apps/codex-switch.png').exists())
for file,expected in markers.items():check('Removal preserves existing private account/vault marker bytes/modes',file.read_bytes()==expected and file.stat().st_mode&0o777==0o600)
manifest=json.loads(Path('/input/expected.json').read_text(encoding='utf-8'))
result={'schema':1,'sourceRevision':manifest['sourceRevision'],'version':value['version'],'architecture':value['architecture'],'rid':manifest['rid'],'distribution':manifest['distribution'],'image':manifest['image'],'nativePackages':native,'passed':len(checks),'failed':0,'checks':checks,'globalDotnetAbsent':True,'accountsUsed':False,'kernel':platform.release(),'scope':'Actual package-manager install/removal, self-contained startup and installed own-window X11/Xvfb source UI on an owned fresh minimal container. Synthetic data only; not a full GNOME/KDE/Wayland/accessibility or authenticated switch/resume acceptance.'}
a.report.parent.mkdir(parents=True,exist_ok=True);a.report.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8');print(json.dumps({key:result[key] for key in ('distribution','architecture','passed','failed','globalDotnetAbsent')}))
