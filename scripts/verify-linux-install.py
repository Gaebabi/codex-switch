"""Exercise ordinary per-user package entry points with isolated owned data and no account/keyring."""
import argparse
import ctypes
import fcntl
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import socket
import subprocess
import sys
import tarfile
import tempfile
import time

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--archive',type=Path,required=True)
p.add_argument('--previous-archive',type=Path)
p.add_argument('--report',type=Path,required=True)
p.add_argument('--raw-ui',type=Path)
a=p.parse_args()
if sys.platform!='linux' or os.geteuid()==0 or os.environ.get('CODEX_SWITCH_DISPOSABLE_RUNNER')!='true':raise SystemExit('Use an explicitly disposable normal-user Linux test environment')
if not shutil.which('desktop-file-validate'):raise SystemExit('Native desktop-file-utils verification is required')
root=Path(tempfile.mkdtemp(prefix='codex-linux-install-test-')).resolve();checks=[]
def check(name,value):
 if not value:raise RuntimeError('Package lifecycle contract failed: '+name)
 checks.append(name)
def unpack(file,name):
 target=root/name;target.mkdir(mode=0o700)
 with tarfile.open(file.resolve(),mode='r:gz') as archive:
  for entry in archive:
   path=Path(entry.name)
   if entry.issym() or entry.islnk() or not(entry.isdir() or entry.isfile()) or path.is_absolute() or '..' in path.parts:raise RuntimeError('Unexpected package archive entry')
  archive.extractall(target,filter='data')
 folders=list(target.iterdir())
 if len(folders)!=1 or not folders[0].is_dir():raise RuntimeError('Package needs one complete payload root')
 manifest=json.loads((folders[0]/'package-manifest.json').read_text(encoding='utf-8'))
 for entry in manifest['Files']:
  file=folders[0]/entry['Path']
  check('Extracted package file bytes/mode: '+entry['Path'],hashlib.sha256(file.read_bytes()).hexdigest()==entry['Sha256'] and file.stat().st_mode&0o777==entry['Mode'])
 return folders[0],manifest
def run(binary,argument,env,success=True):
 result=subprocess.run([str(binary),argument],env=env,capture_output=True,text=True,timeout=45)
 if (result.returncode==0)!=success:raise RuntimeError('Unexpected package entry point result: '+argument+' '+result.stderr[-2000:])
 return result
try:
 payload,manifest=unpack(a.archive,'current');home=root/'home-데모 space%$`"\\=';home.mkdir(mode=0o700)
 data=home/'share';data.mkdir(mode=0o700);codex=home/'.codex';codex.mkdir(mode=0o700);vault=data/'CodexAccountDesk';vault.mkdir(mode=0o700)
 markers={codex/'auth.json':b'Owned synthetic existing authentication bytes\x00\xff',vault/'accounts.vault':b'Owned synthetic existing encrypted-vault bytes\x00\xff'}
 for file,value in markers.items():file.write_bytes(value);file.chmod(0o600)
 bus=socket.socket(socket.AF_UNIX,socket.SOCK_STREAM);bus.bind(str(root/'unused-bus.sock'));bus.listen(2);bus.setblocking(False)
 env=dict(os.environ,HOME=str(home),CODEX_HOME=str(codex),XDG_DATA_HOME=str(data),XDG_CONFIG_HOME=str(home/'config'),XDG_CACHE_HOME=str(home/'cache'),XDG_RUNTIME_DIR=str(home/'runtime'),DBUS_SESSION_BUS_ADDRESS='unix:path='+str(root/'unused-bus.sock'),DOTNET_ROOT=str(root/'no-dotnet'),DOTNET_MULTILEVEL_LOOKUP='0')
 for key in ('XDG_CONFIG_HOME','XDG_CACHE_HOME','XDG_RUNTIME_DIR'):Path(env[key]).mkdir(mode=0o700)
 prefix=data/'codex-switch';installed=prefix/'current/CodexSwitch';binary=payload/'CodexSwitch'
 data.chmod(0o777)
 run(binary,'--install-package',env,False);check('A writable share ancestor refuses without repairing its mode',data.stat().st_mode&0o777==0o777);data.chmod(0o700)
 prefix.symlink_to(codex,target_is_directory=True);run(binary,'--install-package',env,False)
 check('A program-root symlink cannot redirect installation into the selected credential home',prefix.is_symlink() and all(file.read_bytes()==value for file,value in markers.items()));prefix.unlink()
 run(binary,'--install-package',dict(env,CODEX_HOME=str(prefix)),False);check('An overlapping program/Codex home refuses before activation',not installed.exists())
 run(binary,'--install-package',dict(env,XDG_DATA_HOME='relative-share'),False);check('Relative XDG configuration refuses instead of selecting the current directory',not installed.exists())
 menu=data/'applications';menu.mkdir(mode=0o700);custom=menu/'codex-switch.desktop';custom.write_text('Owned unrelated desktop file',encoding='utf-8');custom.chmod(0o600)
 run(binary,'--install-package',env,False);check('An unmanaged existing desktop file is not overwritten',custom.read_text(encoding='utf-8')=='Owned unrelated desktop file');custom.unlink()
 if a.previous_archive:
  previous,old_manifest=unpack(a.previous_archive,'previous')
  check('Upgrade fixture is an actual distinct compiled package version',old_manifest['Version']!=manifest['Version'])
  run(previous/'CodexSwitch','--install-package',env)
  old=prefix/'versions'/old_manifest['Version']
  check('Previous ordinary package installs before update',old.is_dir())
 run(binary,'--install-package',env)
 check('Normal per-user installer activates a complete native payload',installed.is_file() and (prefix/'current').is_symlink())
 info=json.loads(run(installed,'--package-info',env).stdout)
 check('Installed self-contained app starts with global .NET unavailable',info['version']==manifest['Version'] and info['runtime']=='.NET 10.0.13')
 desktop=data/'applications/codex-switch.desktop';icon=data/'icons/hicolor/1024x1024/apps/codex-switch.png'
 check('Menu and canonical high-resolution icon integrate per user',desktop.is_file() and icon.read_bytes()==(payload/'icons/codex-switch.png').read_bytes())
 subprocess.run(['desktop-file-validate',str(desktop)],check=True,capture_output=True,text=True,timeout=10)
 gio=ctypes.CDLL('libgio-2.0.so.0');objects=ctypes.CDLL('libgobject-2.0.so.0')
 gio.g_desktop_app_info_new_from_filename.argtypes=[ctypes.c_char_p];gio.g_desktop_app_info_new_from_filename.restype=ctypes.c_void_p
 gio.g_app_info_get_executable.argtypes=[ctypes.c_void_p];gio.g_app_info_get_executable.restype=ctypes.c_char_p
 objects.g_object_unref.argtypes=[ctypes.c_void_p]
 native=gio.g_desktop_app_info_new_from_filename(os.fsencode(desktop));check('The native desktop parser accepts Unicode/special-character installation paths',bool(native))
 try:check('The native desktop parser resolves the root-managed shell-free launcher',gio.g_app_info_get_executable(native).decode()=='/usr/bin/env')
 finally:objects.g_object_unref(native)
 diagnostic=root/'native-launch.desktop';text=desktop.read_text(encoding='utf-8').replace(' ./CodexSwitch\n',' ./CodexSwitch --package-info\n').replace('StartupNotify=true','StartupNotify=false');diagnostic.write_text(text,encoding='utf-8')
 launch_env=dict(env,DBUS_SESSION_BUS_ADDRESS='unix:path='+str(root/'absent-launch-bus'))
 launched=subprocess.run(['gio','launch',str(diagnostic)],env=launch_env,capture_output=True,text=True,check=True,timeout=20)
 responses=[]
 for line in launched.stdout.splitlines():
  try:value=json.loads(line)
  except json.JSONDecodeError:continue
  if isinstance(value,dict):responses.append(value)
 check('Native desktop launch executes the actual installed app through special-character paths',any(value.get('version')==manifest['Version'] and value.get('runtime')=='.NET 10.0.13' for value in responses))
 if a.previous_archive:check('Update retains the previous complete version',old.is_dir())
 run(binary,'--install-package',env);check('Repeated installation verifies and preserves the same package',json.loads(run(installed,'--package-info',env).stdout)['version']==manifest['Version'])
 with (prefix/'.installation.lock').open('r+b') as lock:
  fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB);run(binary,'--install-package',env,False)
  check('A held cross-process install lock refuses a concurrent mutation',installed.is_file())
 if a.raw_ui:
  raw=a.raw_ui.resolve()
  if raw.exists():raise RuntimeError('Use fresh own-window output')
  # A separate owned non-activating bus is required for actual Avalonia UI startup.
  capture=subprocess.Popen([sys.executable,str(Path(__file__).with_name('capture-linux-app.py')),'--app',str(installed.resolve()),'--output',str(raw)],env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,start_new_session=True)
  try:
   deadline=time.monotonic()+20;active=False;actual=str(installed.resolve())
   while time.monotonic()<deadline and capture.poll() is None:
    for process in Path('/proc').glob('[0-9]*/exe'):
     try:target=os.readlink(process)
     except (FileNotFoundError,ProcessLookupError,PermissionError):continue
     if target==actual:active=True;break
    if active:break
    time.sleep(.025)
   check('The actual installed utility is alive before removal is requested',active)
   run(binary,'--uninstall-package',env,False);check('Uninstall refuses while another installed utility process is running',installed.is_file())
   output,error=capture.communicate(timeout=120)
   if capture.returncode:raise RuntimeError('Installed native capture failed: '+error[-2000:])
  finally:
   if capture.poll() is None:
    os.killpg(capture.pid,signal.SIGKILL);capture.wait(timeout=10)
  check('The installed payload renders its actual own-window UI',len(list(raw.glob('*.png')))==12)
 target=(prefix/'current').resolve();unmanaged=target/'keep-user-file.txt';unmanaged.write_text('Owned unrecorded user bytes',encoding='utf-8');unmanaged.chmod(0o600)
 recorded=target/'RUNNING.txt';original_recorded=recorded.read_bytes();recorded.write_bytes(original_recorded+b'Owned user modification\n')
 before_menu=desktop.read_bytes();before_icon=icon.read_bytes();run(binary,'--uninstall-package',env,False)
 check('Modified recorded payload refuses removal before active/menu/icon mutation',installed.exists() and desktop.read_bytes()==before_menu and icon.read_bytes()==before_icon and recorded.read_bytes()==original_recorded+b'Owned user modification\n');recorded.write_bytes(original_recorded)
 original_desktop=desktop.read_bytes();desktop.write_bytes(original_desktop+b'# Owned user modification\n')
 run(binary,'--install-package',env,False);check('User-modified desktop integration is not overwritten',desktop.read_bytes()==original_desktop+b'# Owned user modification\n')
 result=json.loads(run(installed,'--uninstall-package',env).stdout)
 check('The installed executable removes its own runtime and returns successful JSON without a lazy console assembly load',result['uninstalled'] is True)
 check('Uninstall removes only recorded payload and active integration',not installed.exists() and not icon.exists())
 check('User-added version files and modified desktop entries survive uninstall',unmanaged.read_text(encoding='utf-8')=='Owned unrecorded user bytes' and desktop.exists() and result['retainedUnmanagedItems']>=2)
 for file,value in markers.items():check('Existing account/vault bytes and private modes survive install/update/uninstall',file.read_bytes()==value and file.stat().st_mode&0o777==0o600)
 try:connection,_=bus.accept()
 except BlockingIOError:pass
 else:connection.close();raise RuntimeError('A package entry point unexpectedly contacted the keyring/session bus')
 check('Headless package operations never contact the selected bus',True);bus.close()
 report={'schema':1,'sourceRevision':manifest['Revision'],'version':manifest['Version'],'rid':manifest['Rid'],'passed':len(checks),'failed':0,'checks':checks,'upgradeIncluded':bool(a.previous_archive),'actualInstalledUi':bool(a.raw_ui),'scope':'Actual native per-user package entry points/self-contained startup/installed own-window UI with owned synthetic existing data. No real accounts, live client, keyring change, authenticated switch/resume or full desktop acceptance.'}
 a.report.parent.mkdir(parents=True,exist_ok=True);a.report.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8');print(json.dumps({key:report[key] for key in ('passed','failed','version','rid','upgradeIncluded','actualInstalledUi')}))
finally:
 if root.parent!=Path(tempfile.gettempdir()).resolve() or root.is_symlink() or root.stat().st_uid!=os.geteuid() or not root.name.startswith('codex-linux-install-test-'):raise RuntimeError('Unexpected install test cleanup root')
 shutil.rmtree(root)
