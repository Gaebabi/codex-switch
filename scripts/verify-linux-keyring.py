"""Exercise an actual declared Secret Service on a private file-socket D-Bus."""
import argparse
import ctypes
import importlib.util
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
p.add_argument('--provider',choices=['gnome','kwallet5','kwallet6','ksecrets','ksecrets-current'],default='gnome')
p.add_argument('--owned-xvfb',action='store_true',help=argparse.SUPPRESS)
p.add_argument('--storage-only',action='store_true',help='Exclude the separate installed-client account-policy contracts')
p.add_argument('--inside',action='store_true')
p.add_argument('--terminal-assembly',type=Path,help='Run the dedicated headless terminal contracts in the owned GNOME session')
p.add_argument('--terminal-app',type=Path)
p.add_argument('--terminal-capture',action='store_true')
p.add_argument('--terminal-lifecycle',action='store_true',help='Additional real CLI exec/resume verification; requires an actually network-disabled owned container')
a=p.parse_args()
if sys.platform!='linux':raise SystemExit('Native Linux is required')
assembly=a.assembly.resolve();report=a.report.resolve()
if not assembly.is_file():raise SystemExit('Build the native test assembly first')
if (a.terminal_assembly is None)!=(a.terminal_app is None) or a.terminal_assembly is not None and a.provider!='gnome':
    raise SystemExit('Terminal contracts require both the built assembly/app and the owned GNOME provider')
if a.terminal_assembly is not None:
    a.terminal_assembly=a.terminal_assembly.resolve(strict=True);a.terminal_app=a.terminal_app.resolve(strict=True)
elif a.terminal_capture or a.terminal_lifecycle:raise SystemExit('Terminal lifecycle/capture requires the complete headless contract selection')

if a.provider=='ksecrets-current' and not a.inside and not a.owned_xvfb:
    env=dict(os.environ)
    for key in ('DISPLAY','XAUTHORITY','WAYLAND_DISPLAY'):env.pop(key,None)
    subprocess.run(['xvfb-run','-a','-s','-screen 0 1280x800x24 -nolisten tcp',sys.executable,str(Path(__file__).resolve()),*sys.argv[1:],'--owned-xvfb'],env=env,check=True,timeout=280)
    raise SystemExit(0)

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
        if a.provider!='gnome':
            env['QT_QPA_PLATFORM']='xcb' if a.provider=='ksecrets-current' else 'offscreen'
            env['KDE_SESSION_VERSION']='5' if a.provider=='kwallet5' else '6'
            settings=root/'config/kwalletrc'
            settings.write_text('[Wallet]\nEnabled=true\nFirst Use=false\nLaunch Manager=false\nClose When Idle=false\nClose on Screensaver=false\nLeave Open=true\nUse One Wallet=true\nDefault Wallet=codex-switch-native-test\nLocal Wallet=codex-switch-native-test\napiEnabled=true\n[org.freedesktop.secrets]\napiEnabled=true\n',encoding='utf-8')
            settings.chmod(0o600)
            if a.provider=='ksecrets-current':
                wallet=root/'share/kwalletd';wallet.mkdir(mode=0o700)
                salt=wallet/'codex-switch-native-test.salt';salt.write_bytes(secrets.token_bytes(56));salt.chmod(0o600)
        for key in ('G_DBUS_DEBUG','G_MESSAGES_DEBUG','GNOME_KEYRING_DEBUG','GNOME_KEYRING_TEST_PATH','LIBSECRET_DEBUG'):env.pop(key,None)
        command=['dbus-run-session','--config-file',str(config),'--',sys.executable,str(Path(__file__).resolve()),'--inside','--assembly',str(assembly),'--architecture',a.architecture,'--report',str(report),'--provider',a.provider]
        if a.storage_only:command.append('--storage-only')
        if a.terminal_assembly is not None:command+=['--terminal-assembly',str(a.terminal_assembly),'--terminal-app',str(a.terminal_app)]
        if a.terminal_capture:command.append('--terminal-capture')
        if a.terminal_lifecycle:command.append('--terminal-lifecycle')
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
if not dotnet and (a.terminal_assembly is None or a.terminal_assembly.suffix=='.dll'):raise SystemExit('The reviewed .NET SDK/runtime is required')
results=[];daemon=None;password=secrets.token_urlsafe(32).encode()
kwallet_hash=bytearray(secrets.token_bytes(56))
private_log=(root/'keyring-private.log').open('wb')
kwallet_path={'kwallet5':'/modules/kwalletd5','kwallet6':'/modules/kwalletd6','ksecrets':'/ksecretd'}.get(a.provider)
kwallet_command={'kwallet5':'kwalletd5','kwallet6':'kwalletd6','ksecrets':'ksecretd','ksecrets-current':'ksecretd'}.get(a.provider)
if a.provider=='ksecrets-current':
    spec=importlib.util.spec_from_file_location('ksecrets_control',Path(__file__).with_name('ksecrets-test-control.py'))
    current_control=importlib.util.module_from_spec(spec);spec.loader.exec_module(current_control)
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

def unlock_kwallet(require_dialog=False):
    if a.provider=='ksecrets-current':
        verify_owner()
        used=current_control.unlock_dialog(daemon.pid,password,phase,require_dialog)
        results[-1]['ownedPasswordDialog']=used
        verify_owner()
        return
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
        reply=gio.g_dbus_connection_call_sync(connection,owner,kwallet_path.encode('ascii'),b'org.kde.KWallet',b'pamOpen',parameters,None,0,10000,None,ctypes.byref(error))
        if error.value or not reply:raise RuntimeError('Owned KDE PAM test control failed')
    finally:
        if reply:glib.g_variant_unref(reply)
        if parameters:glib.g_variant_unref(parameters)
        if connection:objects.g_object_unref(connection)
        if error.value:glib.g_error_free(error)
    opened=dbus(owner.decode('ascii'),kwallet_path,'org.kde.KWallet.isOpen','codex-switch-native-test').stdout
    if opened.strip()!='(true,)':raise RuntimeError('The owned KDE wallet did not open')
    phase('kwallet-unlock')
    verify_owner()
def start():
    global daemon
    if a.provider=='gnome':
        daemon=subprocess.Popen(['gnome-keyring-daemon','--foreground','--unlock','--components=secrets','--control-directory',str(root/'control')],stdin=subprocess.PIPE,stdout=private_log,stderr=private_log)
        daemon.stdin.write(password);daemon.stdin.close();ready(True);verify_owner()
    elif a.provider=='ksecrets-current':
        with current_control.pam_login(root,password) as (arguments,descriptors,deliver):
            daemon=subprocess.Popen([kwallet_command,*arguments],pass_fds=descriptors,env=dict(os.environ,PAM_KWALLET5_LOGIN='1'),stdin=subprocess.DEVNULL,stdout=private_log,stderr=private_log)
            deliver()
        ready(True);verify_owner();unlock_kwallet()
    else:
        daemon=subprocess.Popen([kwallet_command],stdin=subprocess.DEVNULL,stdout=private_log,stderr=private_log)
        ready(True);verify_owner();unlock_kwallet()
    if daemon.poll() is not None:raise RuntimeError('The owned foreground provider is no longer live')
def stop(persist=True):
    global daemon
    if daemon is not None:
        if daemon.poll() is None:
            try:
                if persist and a.provider=='ksecrets-current':
                    owner=verify_owner()
                    alias=dbus(owner,'/org/freedesktop/secrets','org.freedesktop.Secret.Service.ReadAlias','default').stdout
                    match=re.search(r"objectpath '(/org/freedesktop/secrets/collection/[^']+)'",alias)
                    if not match:raise RuntimeError('Owned KSecrets collection is unavailable during persistence')
                    dbus(owner,'/org/freedesktop/secrets','org.freedesktop.Secret.Service.Lock',"['"+match[1]+"']")
                elif persist and a.provider!='gnome':
                    owner=verify_owner()
                    closed=dbus(owner,kwallet_path,'org.kde.KWallet.close','codex-switch-native-test','true').stdout
                    if closed.strip()!='(0,)':raise RuntimeError('The owned KDE wallet did not persist and close')
            finally:
                daemon.terminate()
                try:daemon.wait(timeout=10)
                except subprocess.TimeoutExpired:daemon.kill();daemon.wait(timeout=5)
        daemon=None
        ready(False)
def run_contracts():
    phase('no-service');start();phase('roundtrip');phase('concurrent')
    if not a.storage_only:phase('account-policy')
    if a.provider in ('kwallet6','ksecrets','ksecrets-current'):
        phase('provider-identity')
        owner=verify_owner()
        version=dbus(owner,'/MainApplication','org.freedesktop.DBus.Properties.Get','org.qtproject.Qt.QCoreApplication','applicationVersion').stdout
        expected={'kwallet6':'6.13.0','ksecrets':'6.26.0','ksecrets-current':'6.30.0'}[a.provider]
        if version.strip()!="(<'"+expected+"'>,)":raise RuntimeError('Unexpected actual provider version')
        try:
            dbus(owner,'/MainApplication','org.freedesktop.DBus.Properties.Set','org.qtproject.Qt.QCoreApplication','applicationVersion',"<'0.0.0'>")
            phase('provider-refused')
        finally:dbus(owner,'/MainApplication','org.freedesktop.DBus.Properties.Set','org.qtproject.Qt.QCoreApplication','applicationVersion',"<'"+expected+"'>")
        verify_owner();phase('read-after-provider-check')
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
    else:unlock_kwallet(require_dialog=a.provider=='ksecrets-current')
    verify_owner()
    phase('read-after-unlock');stop();start();phase('read-after-restart')
    os.kill(daemon.pid,signal.SIGSTOP)
    try:phase('timeout')
    finally:os.kill(daemon.pid,signal.SIGCONT)
    phase('read-after-timeout');phase('missing');stop();phase('offline')
try:
    terminal=None
    if a.terminal_assembly is not None:
        start();verify_owner()
        terminal_environment=dict(os.environ)
        for name in ('DISPLAY','WAYLAND_DISPLAY','XAUTHORITY'):terminal_environment.pop(name,None)
        native=Path(terminal_environment['CODEX_SWITCH_STANDALONE_CLI']).resolve(strict=True)
        import hashlib
        terminal_environment['CODEX_SWITCH_STANDALONE_CLI']=str(native)
        terminal_environment['CODEX_SWITCH_STANDALONE_CLI_SHA']=hashlib.sha256(native.read_bytes()).hexdigest()
        command=(['dotnet',str(a.terminal_assembly)] if a.terminal_assembly.suffix=='.dll' else [str(a.terminal_assembly)])
        task=subprocess.run([*command,str(root/'codex-linux-terminal-test-contracts'),str(a.terminal_app)],env=terminal_environment,capture_output=True,timeout=240)
        if task.returncode:
            private_log.write(task.stdout);private_log.write(task.stderr);private_log.flush()
            # This executable enforces an owned disposable home and fictional credentials.
            # Keep stdout private; expose only bounded stderr after its credential-output guard.
            diagnostic=task.stderr.decode('utf-8',errors='replace')
            if any(marker in diagnostic for marker in ('synthetic-access','synthetic-refresh','e30.')):raise RuntimeError('Terminal fixture refused unsafe credential diagnostics')
            print('Disposable headless terminal diagnostics:\n'+diagnostic[-4000:],file=sys.stderr)
            raise RuntimeError('Native headless terminal contracts failed; inspect private diagnostics')
        terminal=json.loads(task.stdout)
        if terminal['failed'] or terminal['passed']!=16 or terminal['architecture']!=a.architecture or terminal['standaloneCliVersion']!='0.163.0':raise RuntimeError('Complete native headless terminal and exact standalone0.163.0 verification is required')
        verify_owner()
        if a.terminal_lifecycle:
            sandbox=root/'codex-linux-terminal-test-contracts';report.parent.mkdir(parents=True,exist_ok=True)
            subprocess.run([sys.executable,str(Path(__file__).with_name('verify-linux-terminal-lifecycle.py')),'--app',str(a.terminal_app),'--home',str(sandbox/'home'),'--data',str(sandbox/'vault'),'--cli',str(native),'--report',str(report.parent/'terminal-lifecycle.json'),'--revision',os.environ['EVIDENCE_REVISION']],env=terminal_environment,check=True,timeout=150)
        if a.terminal_capture:
            sandbox=root/'codex-linux-terminal-test-contracts'
            subprocess.run([sys.executable,str(Path(__file__).with_name('capture-linux-terminal.py')),'--app',str(a.terminal_app),'--home',str(sandbox/'home'),'--data',str(sandbox/'vault'),'--client',str(native),'--sha',terminal_environment['CODEX_SWITCH_STANDALONE_CLI_SHA'],'--output',str(report.parent),'--revision',os.environ['EVIDENCE_REVISION']],env=terminal_environment,check=True,timeout=120)
    else:run_contracts()
    package='gnome-keyring' if a.provider=='gnome' else 'libkf5wallet-bin' if a.provider=='kwallet5' else 'kwallet6'
    provider_version=subprocess.run(['dpkg-query','-W','-f=${Version}',package],capture_output=True,text=True,check=True,timeout=10).stdout.strip()
    environment={}
    if a.provider in ('kwallet6','ksecrets','ksecrets-current'):
        versions=subprocess.run(['dpkg-query','-W','-f=${binary:Package} ${Version}\n','libqt6core6t64','libqca-qt6-2','libglib2.0-0t64','libgcrypt20'],capture_output=True,text=True,check=True,timeout=10).stdout
        environment=dict(line.split(' ',1) for line in versions.strip().splitlines())
    value={'schema':1,'provider':a.provider,'providerVersion':provider_version,'providerCompatibility':'verified-storage','architecture':a.architecture,'passed':sum(v['passed'] for v in results),'failed':0,'phases':results,'nativePackages':environment,'accountPolicyIncluded':not a.storage_only,
        'display':'Qt/X11 on owned Xvfb; actual password dialog' if a.provider=='ksecrets-current' else 'Qt offscreen' if a.provider!='gnome' else 'Headless GNOME Keyring',
        'scope':'Actual declared Secret Service/Linux private-file contracts on a disposable file-socket D-Bus/HOME/XDG session; no full KDE/Wayland or authenticated Codex handoff acceptance. No personal bus, accounts, Codex, key material or private paths exported.'}
    if terminal is not None:
        value.update(revision=os.environ['EVIDENCE_REVISION'],terminal=terminal,standaloneCliSha256=terminal_environment['CODEX_SWITCH_STANDALONE_CLI_SHA'],headless=True)
        value['passed']=terminal['passed'];value['accountPolicyIncluded']=False
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
