"""Exercise real CLI exec/resume with ordinary input on an owned network-disabled test workspace."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import queue
import re
import signal
import socket
import subprocess
import threading
import time

parser=argparse.ArgumentParser(description=__doc__)
for name in ['app','home','data','cli','report','revision']:parser.add_argument('--'+name,type=Path if name not in ['revision'] else str,required=True)
args=parser.parse_args()
if os.geteuid()!=1000 or os.environ.get('CODEX_SWITCH_DISPOSABLE_RUNNER')!='true' or {name for _,name in socket.if_nameindex()}!={'lo'} or not re.fullmatch('[0-9a-f]{40}',args.revision):
    raise SystemExit('Only the declared owned normal-user network-disabled native session is allowed')
home=args.home.resolve(strict=True);data=args.data.resolve(strict=True);cli=args.cli.resolve(strict=True)
if not home.parent.name.startswith('codex-linux-terminal-test-') or home.stat().st_uid!=1000 or home.is_symlink():raise SystemExit('Only the dedicated fictional terminal workspace is allowed')
project=home.parent/'native-work';project.mkdir(mode=0o700)
subprocess.run(['git','init','--quiet',str(project)],check=True,timeout=10)
environment=dict(os.environ,CODEX_HOME=str(home))
for name in ['DISPLAY','WAYLAND_DISPLAY','XAUTHORITY','OPENAI_API_KEY','CODEX_ACCESS_TOKEN','OPENAI_IDENTITY_TOKEN_FILE','OPENAI_IDENTITY_TOKEN_AUDIENCE']:environment.pop(name,None)
config=(home/'config.toml').read_bytes();sha=hashlib.sha256(cli.read_bytes()).hexdigest()
log=args.report.parent/'terminal-lifecycle-private.log'

def reader(stream,lines):
    for line in stream:lines.put(line)
    lines.put(None)

def metadata(session):
    process=subprocess.Popen([str(cli),'app-server','--listen','stdio://'],env=environment,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=private,text=True)
    lines=queue.Queue();threading.Thread(target=reader,args=(process.stdout,lines),daemon=True).start()
    def call(number,method,params):
        process.stdin.write(json.dumps({'id':number,'method':method,'params':params})+'\n');process.stdin.flush()
        while True:
            line=lines.get(timeout=25)
            if line is None:raise RuntimeError('Actual native metadata process exited')
            value=json.loads(line)
            if value.get('id')==number:
                if 'error' in value:raise RuntimeError('Actual saved native session could not be read')
                return value['result']
    try:
        call(1,'initialize',{'clientInfo':{'name':'codex_switch_terminal_verification','version':'1.41.0'}})
        process.stdin.write('{"method":"initialized"}\n');process.stdin.flush()
        return call(2,'thread/read',{'threadId':session,'includeTurns':True})['thread']
    finally:process.stdin.close();process.wait(timeout=15)

with log.open('w',encoding='utf-8') as private:
    def interrupted_turn(command):
        process=subprocess.Popen(command,cwd=project,env=environment,stdin=subprocess.DEVNULL,stdout=subprocess.PIPE,stderr=private,text=True,start_new_session=True)
        lines=queue.Queue();threading.Thread(target=reader,args=(process.stdout,lines),daemon=True).start()
        session=None;turn=False
        try:
            deadline=time.monotonic()+60
            while time.monotonic()<deadline:
                line=lines.get(timeout=max(1,deadline-time.monotonic()))
                if line is None:break
                private.write(line);private.flush();value=json.loads(line)
                if value.get('type')=='thread.started':session=value['thread_id']
                if value.get('type')=='turn.started':
                    turn=True
                    if process.poll() is None:os.killpg(process.pid,signal.SIGINT)
                    break
            process.wait(timeout=20)
            if not session or not turn:raise RuntimeError('The real offline CLI did not create an observable interrupted turn')
            return session,process.returncode
        finally:
            if process.poll() is None:os.killpg(process.pid,signal.SIGTERM);process.wait(timeout=15)
    session,_=interrupted_turn([str(cli),'exec','--json','Owned offline resume verification. Reply with OFFLINE only; do not use tools.'])
    before=metadata(session)
    if before['id']!=session or before['source']!='exec' or before['cwd']!=str(project):raise RuntimeError('Actual CLI session identity/source/folder mismatch')
    common=['--data-dir',str(data),'--codex-home',str(home),'--client-context',str(project),'--codex-cli',str(cli),'--codex-cli-sha256',sha]
    resumed,exit_code=interrupted_turn([str(args.app),'cli','switch','Demo personal','--resume',session,'--exec','--continue',*common])
    if resumed!=session:raise RuntimeError('The official exec resume did not restore the exact original session')
    after=metadata(session)
    def continuations(thread):
        return sum(item.get('type')=='userMessage' and any(part.get('text')=='Continue' for part in item.get('content',[])) for turn in thread['turns'] for item in turn['items'])
    if continuations(after)-continuations(before)!=1:raise RuntimeError('Exactly one actual Continue input must be persisted by the official CLI')
    if after['id']!=before['id'] or after['cwd']!=before['cwd']:raise RuntimeError('Original session context was replaced')
    listing=subprocess.run([str(args.app),'cli','list',*common],env=environment,capture_output=True,text=True,check=True,timeout=35)
    accounts=json.loads(listing.stdout)['accounts']
    if len(accounts)!=2 or len([a for a in accounts if a['current'] and a['name']=='Demo personal'])!=1:raise RuntimeError('The production terminal account application was not confirmed')
    if config!=(home/'config.toml').read_bytes() or exit_code==0:raise RuntimeError('An interrupted offline turn must return failure without changing configuration')
value={'revision':args.revision,'passed':1,'failed':0,'nativeCliExecCreatedSession':True,'originalSessionIdAndFolderPreserved':True,'explicitContinuePersistedOnce':True,
       'terminalAccountApplied':True,'twoEncryptedFictionalAccountsRetained':True,'configurationBytesUnchanged':True,'runtimeNetwork':'none',
       'authenticatedInference':False,'offlineTurnInterrupted':True,'nativeNonzeroExitReturned':True,'scope':'Actual native CLI exec and production headless switch/official exec resume observed through real JSON events, each interrupted through normal SIGINT after turn start. Exactly one real Continue is persisted. Network disabled and fictional credentials: no model completion or real authenticated continuation is claimed.'}
args.report.write_text(json.dumps(value,indent=2)+'\n');print(json.dumps({'passed':1,'failed':0,'actualNativeCliResume':True,'authenticatedInference':False}))
