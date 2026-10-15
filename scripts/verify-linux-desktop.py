"""Verify genuine sandboxed Linux desktop IPC on an owned X11 display/private non-activating bus."""
import argparse
import json
import os
from pathlib import Path
import platform
import selectors
import shutil
import signal
import subprocess
import sys
import tempfile
import time
from xml.sax.saxutils import escape

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--assembly',type=Path,required=True)
p.add_argument('--architecture',choices=['X64','Arm64'],required=True)
p.add_argument('--report',type=Path,required=True)
p.add_argument('--inside',action='store_true',help=argparse.SUPPRESS)
a=p.parse_args()
actual={'x86_64':'X64','aarch64':'Arm64'}.get(platform.machine())
if sys.platform!='linux' or actual!=a.architecture or os.geteuid()==0 or os.environ.get('CODEX_SWITCH_DISPOSABLE_RUNNER')!='true':
    raise SystemExit('Use an explicitly disposable native Linux x64/ARM64 worker as a normal user')
assembly=a.assembly.resolve(strict=True);report=a.report.resolve()
if not a.inside:
    if report.exists():raise SystemExit('Use a fresh ignored report directory')
    root=Path(tempfile.mkdtemp(prefix='codex-linux-desktop-test-')).resolve()
    try:
        for name in ('home','share','config','cache','runtime','bus','codex-linux-desktop-test-peer'):(root/name).mkdir(mode=0o700)
        config=root/'bus.conf'
        config.write_text('<busconfig><type>session</type><listen>unix:tmpdir='+escape(str(root/'bus'))+'</listen><auth>EXTERNAL</auth><policy context="default"><allow user="'+str(os.geteuid())+'"/><allow send_destination="*"/><allow receive_sender="*"/><allow own="*"/></policy></busconfig>',encoding='utf-8');config.chmod(0o600)
        env={key:value for key,value in os.environ.items() if key in ('PATH','LANG','LC_ALL','DOTNET_ROOT')}
        env.update(HOME=str(root/'home'),CODEX_HOME=str(root/'codex-linux-desktop-test-peer'),XDG_DATA_HOME=str(root/'share'),XDG_CONFIG_HOME=str(root/'config'),XDG_CACHE_HOME=str(root/'cache'),XDG_RUNTIME_DIR=str(root/'runtime'),XDG_SESSION_TYPE='x11',CODEX_SWITCH_DISPOSABLE_RUNNER='true',CODEX_SWITCH_DESKTOP_TEST_ROOT=str(root),DOTNET_CLI_TELEMETRY_OPTOUT='1')
        subprocess.run(['dbus-run-session','--config-file',str(config),'--','xvfb-run','-a','-s','-screen 0 1280x800x24 -nolisten tcp',sys.executable,str(Path(__file__).resolve()),'--inside','--assembly',str(assembly),'--architecture',a.architecture,'--report',str(report)],env=env,check=True,timeout=150)
    finally:
        if root.parent!=Path(tempfile.gettempdir()).resolve() or not root.name.startswith('codex-linux-desktop-test-') or root.is_symlink() or root.stat().st_uid!=os.geteuid():raise RuntimeError('Unexpected desktop diagnostic cleanup root')
        shutil.rmtree(root)
    raise SystemExit(0)

root=Path(os.environ['CODEX_SWITCH_DESKTOP_TEST_ROOT']).resolve();home=Path(os.environ['CODEX_HOME'])
if root.is_symlink() or root.stat().st_uid!=os.geteuid() or not root.name.startswith('codex-linux-desktop-test-') or home.parent!=root or not os.environ.get('DBUS_SESSION_BUS_ADDRESS','').startswith('unix:path='+str(root/'bus')+'/'):
    raise SystemExit('Only the owned private desktop session is allowed')
report.mkdir(parents=True,mode=0o700)
def checked_json(command):
    completed=subprocess.run(command,check=True,capture_output=True,text=True,timeout=45)
    return json.loads(completed.stdout)
contracts=checked_json(['dotnet',str(assembly),'--contracts',str(root)])
desktop=None;peer=None
def stop_owned():
    if desktop is None:return
    try:os.kill(desktop,signal.SIGTERM)
    except ProcessLookupError:pass
try:
    with (root/'desktop-private.log').open('wb') as log:
        peer=subprocess.Popen(['dotnet',str(assembly),'--peer',str(home)],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=log,text=True,start_new_session=True)
        selector=selectors.DefaultSelector();selector.register(peer.stdout,selectors.EVENT_READ)
        try:
            if not selector.select(35):raise TimeoutError('Native peer verification did not reach its read-only handshake')
            phase=peer.stdout.readline()
        finally:selector.close()
        if not phase:
            output,error=peer.communicate(timeout=5)
            raise RuntimeError('Native peer exited before handshake: '+(root/'desktop-private.log').read_text(encoding='utf-8'))
        phase=json.loads(phase)
        if set(phase)!={'phase','ownedProcess'} or phase['phase']!='peer-connected' or not isinstance(phase['ownedProcess'],int) or phase['ownedProcess']<=0:raise RuntimeError('Unexpected peer verification phase')
        desktop=phase['ownedProcess']
        # Capture only a visible window whose PID is this original sandboxed native client.
        windows=subprocess.run(['xdotool','search','--onlyvisible','--pid',str(desktop)],capture_output=True,text=True,check=True,timeout=5).stdout.splitlines()
        if not windows or any(not value.isdecimal() for value in windows):raise RuntimeError('Owned official desktop window is unavailable')
        window=windows[0]
        if subprocess.run(['xdotool','getwindowpid',window],capture_output=True,text=True,check=True,timeout=5).stdout.strip()!=str(desktop):raise RuntimeError('Native client capture PID changed')
        subprocess.run(['xwd','-silent','-id',window,'-out',str(root/'client.xwd')],check=True,timeout=10)
        subprocess.run(['convert',str(root/'client.xwd'),'-strip',str(report/'official-client-native.png')],check=True,timeout=10)
        stop_owned()
        output,error=peer.communicate('owned-peer-exited\n',timeout=15)
        if peer.returncode!=0:raise RuntimeError('Native peer contract failed: '+(root/'desktop-private.log').read_text(encoding='utf-8'))
        positive=json.loads(output)
    if contracts['failed']!=0 or positive['failed']!=0 or contracts['architecture']!=a.architecture or positive['architecture']!=a.architecture:raise RuntimeError('Native desktop verification result mismatch')
    result={'schema':1,'architecture':a.architecture,'passed':contracts['passed']+positive['passed'],'failed':0,'contracts':contracts,'actualPeer':positive,'kernel':platform.release(),'display':'Owned X11/Xvfb; official Chromium sandbox enabled','officialPackage':'26.1007.71326','accountsUsed':False,
        'scope':'Actual official desktop read-only IPC and native socket/process trust. Original owned daemon termination is diagnostic cleanup, not normal application shutdown or authenticated switch/resume evidence.'}
    (report/'peer-summary.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'passed':result['passed'],'failed':0,'architecture':a.architecture,'actualSandboxedPeer':True}))
finally:
    stop_owned()
    if peer is not None:
        if peer.poll() is None:peer.kill();peer.wait(timeout=5)
        try:os.killpg(peer.pid,signal.SIGKILL)
        except ProcessLookupError:pass
