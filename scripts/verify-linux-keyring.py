"""Exercise actual GNOME Keyring on a private file-socket D-Bus, never the user's bus."""
import argparse
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import signal
import stat
import subprocess
import sys
import tempfile
import time
from xml.sax.saxutils import escape

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--assembly',type=Path,required=True)
p.add_argument('--architecture',choices=['X64','Arm64'],required=True)
p.add_argument('--report',type=Path,required=True)
p.add_argument('--inside',action='store_true')
a=p.parse_args()
if sys.platform!='linux':raise SystemExit('Native Linux is required')
assembly=a.assembly.resolve();report=a.report.resolve()
if not assembly.is_file():raise SystemExit('Build the native test assembly first')

if not a.inside:
    root=Path(tempfile.mkdtemp(prefix='codex-linux-keyring-test-')).resolve()
    try:
        (root/'bus').mkdir(mode=0o700)
        config=root/'bus.conf'
        config.write_text('<busconfig><type>session</type><listen>unix:tmpdir='+escape(str(root/'bus'))+'</listen><auth>EXTERNAL</auth><policy context="default"><allow user="'+str(os.geteuid())+'"/><allow send_destination="*"/><allow receive_sender="*"/><allow own="*"/></policy></busconfig>',encoding='utf-8')
        config.chmod(0o600)
        env=dict(os.environ,CODEX_SWITCH_LINUX_TEST_ROOT=str(root),XDG_DATA_HOME=str(root/'share'),XDG_CONFIG_HOME=str(root/'config'),XDG_CACHE_HOME=str(root/'cache'),
            XDG_RUNTIME_DIR=str(root/'runtime'),GNOME_KEYRING_CONTROL=str(root/'control'))
        for folder in ('share','config','runtime','control','cache'):(root/folder).mkdir(mode=0o700)
        (root/'share/keyrings').mkdir(mode=0o700)
        for key in ('G_DBUS_DEBUG','G_MESSAGES_DEBUG','GNOME_KEYRING_DEBUG','GNOME_KEYRING_TEST_PATH','LIBSECRET_DEBUG'):env.pop(key,None)
        command=['dbus-run-session','--config-file',str(config),'--',sys.executable,str(Path(__file__).resolve()),'--inside','--assembly',str(assembly),'--architecture',a.architecture,'--report',str(report)]
        subprocess.run(command,env=env,check=True,timeout=240)
    finally:
        if root.parent!=Path(tempfile.gettempdir()).resolve() or not root.name.startswith('codex-linux-keyring-test-') or root.is_symlink() or root.stat().st_uid!=os.geteuid():raise SystemExit('Unexpected diagnostic cleanup root')
        shutil.rmtree(root)
    raise SystemExit(0)

root=Path(os.environ.get('CODEX_SWITCH_LINUX_TEST_ROOT','')).resolve()
address=os.environ.get('DBUS_SESSION_BUS_ADDRESS','')
if not root.name.startswith('codex-linux-keyring-test-') or root.is_symlink() or root.stat().st_uid!=os.geteuid() or stat.S_IMODE(root.stat().st_mode)!=0o700 or not address.startswith('unix:path='+str(root/'bus')+'/'):
    raise SystemExit('Only the owned diagnostic D-Bus is allowed')
dotnet=shutil.which('dotnet')
if not dotnet:raise SystemExit('The reviewed .NET SDK/runtime is required')
results=[];daemon=None;password=secrets.token_urlsafe(32).encode()
private_log=(root/'keyring-private.log').open('wb')
def redact(value):
    return value.replace(str(root),'<isolated-test-root>').replace(str(Path.cwd()),'<repository>')[-6000:]
def phase(name,secret_input=None):
    result=subprocess.run([dotnet,str(assembly),name],input=None if secret_input is None else secret_input.decode('ascii'),capture_output=True,text=True,timeout=70)
    if result.returncode:
        print(redact(result.stderr),file=sys.stderr)
        raise RuntimeError('Native keyring phase failed: '+name)
    value=json.loads(result.stdout)
    if value['failed']!=0 or value['passed']<=0 or value['architecture']!=a.architecture:raise RuntimeError('Native phase/architecture check failed')
    results.append(value);print(json.dumps({'phase':name,'architecture':value['architecture'],'passed':value['passed'],'failed':0}),flush=True)
def dbus(destination,path,method,*arguments,required=True):
    result=subprocess.run(['gdbus','call','--session','--dest',destination,'--object-path',path,'--method',method,*arguments],capture_output=True,text=True,timeout=12)
    if required and result.returncode:raise RuntimeError('Owned D-Bus operation failed: '+method)
    return result
def ready(expected):
    deadline=time.monotonic()+10
    while time.monotonic()<deadline:
        value=dbus('org.freedesktop.DBus','/org/freedesktop/DBus','org.freedesktop.DBus.NameHasOwner','org.freedesktop.secrets').stdout
        if ('true' in value)==expected:return
        time.sleep(0.1)
    raise RuntimeError('Owned provider name did not reach the required state')
def verify_owner():
    owner=dbus('org.freedesktop.DBus','/org/freedesktop/DBus','org.freedesktop.DBus.GetNameOwner','org.freedesktop.secrets').stdout
    match=re.search(r"'(:[0-9.]+)'",owner)
    if not match:raise RuntimeError('Owned provider identity is unavailable')
    pid=dbus('org.freedesktop.DBus','/org/freedesktop/DBus','org.freedesktop.DBus.GetConnectionUnixProcessID',match[1]).stdout
    value=re.search(r'uint32 ([0-9]+)',pid)
    if not value or daemon is None or int(value[1])!=daemon.pid:raise RuntimeError('Secret Service is not the owned foreground process')
def start():
    global daemon
    daemon=subprocess.Popen(['gnome-keyring-daemon','--foreground','--unlock','--components=secrets','--control-directory',str(root/'control')],stdin=subprocess.PIPE,stdout=private_log,stderr=private_log)
    daemon.stdin.write(password);daemon.stdin.close();ready(True);verify_owner()
    if daemon.poll() is not None:raise RuntimeError('The owned foreground provider is no longer live')
def stop():
    global daemon
    if daemon is not None:
        if daemon.poll() is None:
            daemon.terminate()
            try:daemon.wait(timeout=10)
            except subprocess.TimeoutExpired:daemon.kill();daemon.wait(timeout=5)
        daemon=None
        ready(False)
try:
    phase('no-service');start();phase('roundtrip');phase('concurrent');phase('account-policy')
    foreign=root/'data/linux-self-test-protector/foreign.txt';foreign.write_bytes(b'Synthetic ownership fixture');foreign.chmod(0o600)
    change=['chown',str(65534 if os.geteuid()!=65534 else 0),str(foreign)]
    subprocess.run(change if os.geteuid()==0 else ['sudo','-n',*change],check=True,capture_output=True)
    phase('foreign-owner')
    alias=dbus('org.freedesktop.secrets','/org/freedesktop/secrets','org.freedesktop.Secret.Service.ReadAlias','default').stdout
    match=re.search(r"objectpath '(/org/freedesktop/secrets/collection/[^']+)'",alias)
    if not match:raise RuntimeError('The owned default collection is unavailable')
    dbus('org.freedesktop.secrets','/org/freedesktop/secrets','org.freedesktop.Secret.Service.Lock',"['"+match[1]+"']")
    phase('locked')
    phase('unlock',password)
    verify_owner()
    phase('read-after-unlock');stop();start();phase('read-after-restart')
    os.kill(daemon.pid,signal.SIGSTOP)
    try:phase('timeout')
    finally:os.kill(daemon.pid,signal.SIGCONT)
    phase('read-after-timeout');phase('missing');stop();phase('offline')
    value={'schema':1,'architecture':a.architecture,'passed':sum(v['passed'] for v in results),'failed':0,'phases':results,
        'scope':'Actual GNOME Keyring/Linux private-file native contracts on a disposable file-socket D-Bus/XDG session. No personal bus, accounts, Codex, key material or private paths exported.'}
    report.parent.mkdir(parents=True,exist_ok=True);report.write_text(json.dumps(value,indent=2)+'\n',encoding='utf-8')
finally:
    if daemon is not None:
        if daemon.poll() is None:os.kill(daemon.pid,signal.SIGCONT)
        stop()
    private_log.close()
