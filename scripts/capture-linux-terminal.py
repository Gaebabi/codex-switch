"""Capture only an owned real terminal executing actual headless account commands with fictional inputs."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import struct
import subprocess
import sys
import time

parser=argparse.ArgumentParser(description=__doc__)
for name in ['app','home','data','client','sha','output','revision']:parser.add_argument('--'+name,required=True)
parser.add_argument('--inside',action='store_true');parser.add_argument('--render',action='store_true')
args=parser.parse_args()
if sys.platform!='linux' or os.geteuid()==0 or os.environ.get('CODEX_SWITCH_DISPOSABLE_RUNNER')!='true' or not re.fullmatch('[0-9a-f]{40}',args.revision):
    raise SystemExit('Only the declared owned normal-user native test session is allowed')
output=Path(args.output).resolve();private=output.parent/'terminal-capture-private'
if not args.inside:
    if private.exists():raise SystemExit('Use a fresh owned terminal capture folder')
    output.mkdir(parents=True,exist_ok=True);private.mkdir(mode=0o700)
    subprocess.run(['xvfb-run','-a','-s','-screen 0 1600x1000x24 -nolisten tcp',sys.executable,str(Path(__file__).resolve()),*sys.argv[1:],'--inside'],check=True,timeout=90)
    raise SystemExit(0)
if args.render:
    env=dict(os.environ)
    for name in ['DISPLAY','WAYLAND_DISPLAY','XAUTHORITY']:env.pop(name,None)
    common=['--data-dir',args.data,'--codex-home',args.home,'--client-context',args.home,'--codex-cli',args.client,'--codex-cli-sha256',args.sha]
    try:
        print('Codex Switch | Linux terminal account verification',flush=True)
        print('Actual native executable; fictional accounts; commands run without DISPLAY.\n',flush=True)
        version=subprocess.run([args.client,'--version'],env=env,capture_output=True,text=True,check=True,timeout=10).stdout.strip()
        expected=os.environ.get('CODEX_SWITCH_TERMINAL_EXPECTED_CLI_VERSION')
        if not expected or version!='codex-cli '+expected:raise RuntimeError('Observed terminal CLI version differs from the independently declared reviewed pin')
        print('$ codex --version\n'+version+'\n',flush=True)
        for command in [['switch','Demo personal'],['list']]:
            print('$ codex-switch cli '+('switch "Demo personal"' if command[0]=='switch' else 'list'),flush=True)
            result=subprocess.run([args.app,'cli',*command,*common],env=env,capture_output=True,text=True,timeout=35,check=True)
            value=json.loads(result.stdout)
            if 'synthetic-access' in result.stdout or 'example.invalid' in result.stdout:raise RuntimeError('Account identifiers must not be exposed')
            if command[0]=='switch' and value.get('applied') is not True:raise RuntimeError('Actual account application was not verified')
            if command[0]=='list' and (len(value['accounts'])!=2 or len([a for a in value['accounts'] if a['current'] and a['name']=='Demo personal'])!=1):raise RuntimeError('Actual current account was not confirmed')
            print(result.stdout.rstrip()+'\n',flush=True)
        print('PASS: selected account applied; both encrypted accounts retained.\nNo real login, inference or active-task continuation is claimed.',flush=True)
        (private/'render-ready').write_text('verified\n')
        while not (private/'captured').exists():time.sleep(.1)
    except Exception:
        (private/'render-failed').write_text('Actual terminal verification failed.\n');raise
    raise SystemExit(0)

def command(*values):return subprocess.run(values,check=True,capture_output=True,text=True,timeout=10).stdout
def wait(probe,timeout=35):
    deadline=time.monotonic()+timeout
    while True:
        value=probe()
        if value:return value
        if time.monotonic()>deadline:raise RuntimeError('Owned terminal readiness failed')
        time.sleep(.1)
with (private/'native-private.log').open('wb') as log:
    manager=subprocess.Popen(['xfwm4','--compositor=off'],stdout=log,stderr=log)
    terminal=None
    try:
        wait(lambda:re.search(r'window id # (0x[0-9a-f]+)',command('xprop','-root','_NET_SUPPORTING_WM_CHECK')))
        terminal=subprocess.Popen(['xterm','-title','Codex Switch - actual Linux CLI verification','-fa','DejaVu Sans Mono','-fs','12','-geometry','112x32','-bg','#17191e','-fg','#e8eaf0','-e',sys.executable,str(Path(__file__).resolve()),*sys.argv[1:],'--render'],stdout=log,stderr=log)
        def ready():
            if terminal.poll() is not None or (private/'render-failed').exists():raise RuntimeError('Actual terminal command failed')
            return (private/'render-ready').exists()
        wait(ready,60)
        windows=command('xdotool','search','--onlyvisible','--pid',str(terminal.pid)).splitlines()
        if len(windows)!=1:raise RuntimeError('Exactly one owned visible terminal is required')
        xid=windows[0];pid=command('xprop','-id',xid,'_NET_WM_PID')
        if not re.search(r'= '+str(terminal.pid)+r'\s*$',pid):raise RuntimeError('The visible terminal has a different owner')
        tree=command('xwininfo','-id',xid,'-tree');parent=re.search(r'Parent window id: (0x[0-9a-f]+)',tree);root=re.search(r'Root window id: (0x[0-9a-f]+)',tree)
        if not parent or not root or parent[1]==root[1]:raise RuntimeError('The original OS frame is required; desktop-root capture is refused')
        children=command('xwininfo','-id',parent[1],'-children')
        if not re.search(r'^\s+0x'+format(int(xid),'x')+r'\s',children,re.M):raise RuntimeError('Original terminal is not a direct child of the native frame')
        wm=re.search(r'window id # (0x[0-9a-f]+)',command('xprop','-root','_NET_SUPPORTING_WM_CHECK'))
        if not wm or not command('xprop','-id',wm[1],'_NET_WM_NAME').rstrip().endswith('"Xfwm4"'):raise RuntimeError('Actual Xfwm4 is required')
        time.sleep(.3)
        raw=private/'terminal.xwd';picture=output/'terminal-accounts-native-frame.png'
        subprocess.run(['xwd','-silent','-id',parent[1],'-out',str(raw)],check=True,timeout=10)
        subprocess.run(['convert',str(raw),'-strip',str(picture)],check=True,timeout=10)
        if 'Parent window id: '+parent[1]+' ' not in command('xwininfo','-id',xid,'-tree'):raise RuntimeError('Terminal frame changed during capture')
        image=picture.read_bytes();width,height=struct.unpack('>II',image[16:24]);position=8
        while position<len(image):
            length=struct.unpack('>I',image[position:position+4])[0];kind=image[position+4:position+8]
            if kind in (b'tEXt',b'zTXt',b'iTXt',b'eXIf',b'iCCP'):raise RuntimeError('Private image metadata is forbidden')
            position+=12+length
        if width>1500 or height>950 or width<800 or height<400:raise RuntimeError('Unexpected native terminal frame size')
        proof={'file':picture.name,'sha256':hashlib.sha256(image).hexdigest(),'width':width,'height':height,'originalTerminalPidVerified':True,'directParentFrameVerified':True,'desktopRootExcluded':True,'actualCommandsWithoutDisplay':True,'fictionalAccounts':2,'titlebarComposition':False}
        (output/'terminal-capture.json').write_text(json.dumps({'revision':args.revision,'os':'Linux','windowManager':'Xfwm4','images':[proof],'scope':'Actual native terminal executing the real published headless commands with fictional local account data. No personal account, real authentication, inference or active-task continuation.'},indent=2)+'\n')
        (private/'captured').write_text('done\n');terminal.wait(timeout=10)
    finally:
        if terminal is not None and terminal.poll() is None:terminal.terminate();terminal.wait(timeout=10)
        if manager.poll() is None:manager.terminate();manager.wait(timeout=10)
print(json.dumps({'actualTerminalImages':1,'headlessCommandsVerified':True,'personalDataUsed':False}))
