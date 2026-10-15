"""Exercise an actual declared Secret Service on a private file-socket D-Bus."""
import argparse
import ctypes
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
p.add_argument('--provider',choices=['gnome','kwallet5'],default='gnome')
p.add_argument('--storage-only',action='store_true',help='Exclude the separate installed-client account-policy contracts')
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
        env=dict(os.environ,CODEX_SWITCH_LINUX_TEST_ROOT=str(root),HOME=str(root/'home'),XDG_DATA_HOME=str(root/'share'),XDG_CONFIG_HOME=str(root/'config'),XDG_CACHE_HOME=str(root/'cache'),
            XDG_RUNTIME_DIR=str(root/'runtime'),GNOME_KEYRING_CONTROL=str(root/'control'))
        for folder in ('home','share','config','runtime','control','cache'):(root/folder).mkdir(mode=0o700)
        (root/'share/keyrings').mkdir(mode=0o700)
        if a.provider=='kwallet5':
            env['QT_QPA_PLATFORM']='offscreen'
            env['KDE_SESSION_VERSION']='5'
            settings=root/'config/kwalletrc'
            settings.write_text('[Wallet]\nEnabled=true\nFirst Use=false\nLaunch Manager=false\nClose When Idle=false\nClose on Screensaver=false\nLeave Open=true\nUse One Wallet=true\nDefault Wallet=codex-switch-native-test\nLocal Wallet=codex-switch-native-test\napiEnabled=true\n[org.freedesktop.secrets]\napiEnabled=true\n',encoding='utf-8')
            settings.chmod(0o600)
        for key in ('G_DBUS_DEBUG','G_MESSAGES_DEBUG','GNOME_KEYRING_DEBUG','GNOME_KEYRING_TEST_PATH','LIBSECRET_DEBUG'):env.pop(key,None)
        command=['dbus-run-session','--config-file',str(config),'--',sys.executable,str(Path(__file__).resolve()),'--inside','--assembly',str(assembly),'--architecture',a.architecture,'--report',str(report),'--provider',a.provider]
        if a.storage_only:command.append('--storage-only')
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
kwallet_hash=bytearray(secrets.token_bytes(56))
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
    return match[1]

def unlock_kwallet():
    # Test-only KDE PAM control. No key is passed on argv or exported to a receipt.
    owner=verify_owner().encode('ascii')
    gio=ctypes.CDLL('libgio-2.0.so.0');glib=ctypes.CDLL('libglib-2.0.so.0');objects=ctypes.CDLL('libgobject-2.0.so.0')
    pointer=ctypes.c_void_p;error=pointer()
    gio.g_bus_get_sync.argtypes=[ctypes.c_int,pointer,ctypes.POINTER(pointer)];gio.g_bus_get_sync.restype=pointer
    glib.g_variant_parse.argtypes=[pointer,ctypes.c_char_p,pointer,pointer,ctypes.POINTER(pointer)];glib.g_variant_parse.restype=pointer
    gio.g_dbus_connection_call_sync.argtypes=[pointer,ctypes.c_char_p,ctypes.c_char_p,ctypes.c_char_p,ctypes.c_char_p,pointer,pointer,ctypes.c_int,ctypes.c_int,pointer,ctypes.POINTER(pointer)];gio.g_dbus_connection_call_sync.restype=pointer
    glib.g_variant_unref.argtypes=[pointer];glib.g_error_free.argtypes=[pointer];objects.g_object_unref.argtypes=[pointer]
    glib.g_variant_take_ref.argtypes=[pointer];glib.g_variant_take_ref.restype=pointer
    connection=parameters=reply=None
    try:
        connection=gio.g_bus_get_sync(2,None,ctypes.byref(error))
        if error.value or not connection:raise RuntimeError('Owned KDE test bus unavailable')
        encoded=("('codex-switch-native-test', @ay ["+','.join(str(b) for b in kwallet_hash)+"], 0)").encode('ascii')
        parameters=glib.g_variant_parse(None,encoded,None,None,ctypes.byref(error))
        if error.value or not parameters:raise RuntimeError('Owned KDE test control arguments invalid')
        parameters=glib.g_variant_take_ref(parameters)
        reply=gio.g_dbus_connection_call_sync(connection,owner,b'/modules/kwalletd5',b'org.kde.KWallet',b'pamOpen',parameters,None,0,10000,None,ctypes.byref(error))
        if error.value or not reply:raise RuntimeError('Owned KDE PAM test control failed')
    finally:
        if reply:glib.g_variant_unref(reply)
        if parameters:glib.g_variant_unref(parameters)
        if connection:objects.g_object_unref(connection)
        if error.value:glib.g_error_free(error)
    opened=dbus(owner.decode('ascii'),'/modules/kwalletd5','org.kde.KWallet.isOpen','codex-switch-native-test').stdout
    if opened.strip()!='(true,)':raise RuntimeError('The owned KDE wallet did not open')
    phase('kwallet-unlock')
    verify_owner()
def start():
    global daemon
    if a.provider=='gnome':
        daemon=subprocess.Popen(['gnome-keyring-daemon','--foreground','--unlock','--components=secrets','--control-directory',str(root/'control')],stdin=subprocess.PIPE,stdout=private_log,stderr=private_log)
        daemon.stdin.write(password);daemon.stdin.close();ready(True);verify_owner()
    else:
        daemon=subprocess.Popen(['kwalletd5'],stdin=subprocess.DEVNULL,stdout=private_log,stderr=private_log)
        ready(True);verify_owner();unlock_kwallet()
    if daemon.poll() is not None:raise RuntimeError('The owned foreground provider is no longer live')
def stop(persist=True):
    global daemon
    if daemon is not None:
        if daemon.poll() is None:
            try:
                if persist and a.provider=='kwallet5':
                    owner=verify_owner()
                    closed=dbus(owner,'/modules/kwalletd5','org.kde.KWallet.close','codex-switch-native-test','true').stdout
                    if closed.strip()!='(0,)':raise RuntimeError('The owned KDE wallet did not persist and close')
            finally:
                daemon.terminate()
                try:daemon.wait(timeout=10)
                except subprocess.TimeoutExpired:daemon.kill();daemon.wait(timeout=5)
        daemon=None
        ready(False)
def run_contracts():
    if a.provider=='kwallet5':
        phase('no-service');start()
        owner=verify_owner()
        probe=dbus(owner,'/org/freedesktop/secrets','org.freedesktop.Secret.Service.OpenSession','dh-ietf1024-sha256-aes128-cbc-pkcs7','<[byte 0x02]>',required=False)
        if probe.returncode==0 or 'org.freedesktop.DBus.Error.InvalidArgs' not in probe.stderr or 'Client public key size is invalid' not in probe.stderr:
            raise RuntimeError('The declared KDE protocol incompatibility was not reproduced; re-review provider compatibility')
        results.append({'phase':'native-protocol-incompatibility','architecture':a.architecture,'passed':1,'failed':0})
        phase('incompatible-provider')
        alias=dbus(owner,'/org/freedesktop/secrets','org.freedesktop.Secret.Service.ReadAlias','default').stdout
        match=re.search(r"objectpath '(/org/freedesktop/secrets/collection/[^']+)'",alias)
        if not match:raise RuntimeError('The owned KDE collection is unavailable')
        items=dbus(owner,match[1],'org.freedesktop.DBus.Properties.Get','org.freedesktop.Secret.Collection','Items').stdout
        if '[]' not in items:raise RuntimeError('Incompatible provider received unexpected secret items')
        dbus(owner,'/org/freedesktop/secrets','org.freedesktop.Secret.Service.Lock',"['"+match[1]+"']")
        phase('incompatible-provider-locked');unlock_kwallet();phase('incompatible-provider-unlocked')
        stop();start();phase('incompatible-provider-restarted')
        os.kill(daemon.pid,signal.SIGSTOP)
        try:phase('provider-timeout')
        finally:os.kill(daemon.pid,signal.SIGCONT)
        stop();phase('no-service')
        return
    phase('no-service');start();phase('roundtrip');phase('concurrent')
    if not a.storage_only:phase('account-policy')
    foreign=root/'data/linux-self-test-protector/foreign.txt';foreign.write_bytes(b'Synthetic ownership fixture');foreign.chmod(0o600)
    change=['chown',str(65534 if os.geteuid()!=65534 else 0),str(foreign)]
    subprocess.run(change if os.geteuid()==0 else ['sudo','-n',*change],check=True,capture_output=True)
    phase('foreign-owner')
    alias=dbus('org.freedesktop.secrets','/org/freedesktop/secrets','org.freedesktop.Secret.Service.ReadAlias','default').stdout
    match=re.search(r"objectpath '(/org/freedesktop/secrets/collection/[^']+)'",alias)
    if not match:raise RuntimeError('The owned default collection is unavailable')
    dbus('org.freedesktop.secrets','/org/freedesktop/secrets','org.freedesktop.Secret.Service.Lock',"['"+match[1]+"']")
    phase('locked')
    if a.provider=='gnome':phase('unlock',password)
    else:unlock_kwallet()
    verify_owner()
    phase('read-after-unlock');stop();start();phase('read-after-restart')
    os.kill(daemon.pid,signal.SIGSTOP)
    try:phase('timeout')
    finally:os.kill(daemon.pid,signal.SIGCONT)
    phase('read-after-timeout');phase('missing');stop();phase('offline')
try:
    run_contracts()
    package='gnome-keyring' if a.provider=='gnome' else 'libkf5wallet-bin'
    provider_version=subprocess.run(['dpkg-query','-W','-f=${Version}',package],capture_output=True,text=True,check=True,timeout=10).stdout.strip()
    value={'schema':1,'provider':a.provider,'providerVersion':provider_version,'providerCompatibility':'unavailable' if a.provider=='kwallet5' else 'verified-storage','architecture':a.architecture,'passed':sum(v['passed'] for v in results),'failed':0,'phases':results,
        'scope':'Actual declared Secret Service/Linux private-file contracts on a disposable file-socket D-Bus/XDG session. KDE uses offscreen Qt, not a full KDE/Wayland desktop. No personal bus, accounts, Codex, key material or private paths exported.'}
    report.parent.mkdir(parents=True,exist_ok=True);report.write_text(json.dumps(value,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({key:value[key] for key in ('provider','providerVersion','providerCompatibility','architecture','passed','failed')} | {'phaseCount':len(results)}),flush=True)
finally:
    try:
        if daemon is not None:
            if daemon.poll() is None:os.kill(daemon.pid,signal.SIGCONT)
            stop(persist=False)
    finally:
        private_log.close()
        kwallet_hash[:]=bytes(len(kwallet_hash))
