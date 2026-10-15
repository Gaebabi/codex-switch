"""Exercise the actual pinned client and policy on an explicitly disposable native CI runner."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shlex
import shutil
import stat
import subprocess
import sys
import tempfile

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--architecture',choices=['X64','Arm64'],required=True)
p.add_argument('--executable',type=Path,required=True)
p.add_argument('--tests',type=Path,required=True)
p.add_argument('--report',type=Path,required=True)
a=p.parse_args()
if sys.platform!='linux' or os.environ.get('GITHUB_ACTIONS')!='true' or os.environ.get('CODEX_SWITCH_DISPOSABLE_RUNNER')!='true' or os.geteuid()==0:
    raise SystemExit('Run native client verification only as the normal user of the explicitly disposable CI job')
if {'x86_64':'X64','aarch64':'Arm64'}.get(platform.machine())!=a.architecture:raise SystemExit('Actual native architecture mismatch')
executable=a.executable.resolve();tests=a.tests.resolve();report=a.report.resolve()
if not executable.is_file() or not tests.is_file():raise SystemExit('Build/publish the actual native app and tests first')
policy_directory=Path('/etc/codex');policy_paths=[policy_directory/'config.toml',policy_directory/'requirements.toml']
if policy_directory.is_symlink() or any(os.path.lexists(path) for path in policy_paths):raise SystemExit('Never modify an existing system configuration or managed policy')
created_policy_directory=not policy_directory.exists()
root=Path(tempfile.mkdtemp(prefix='codex-linux-client-test-')).resolve();root.chmod(0o700)
checks=[];owned_policies={}
def check(name,condition):
    if not condition:raise AssertionError(name)
    checks.append(name)
def run(command,**kwargs):return subprocess.run(command,capture_output=True,text=True,check=True,timeout=70,**kwargs)
def write(path,text):path.write_text(text,encoding='utf-8');path.chmod(0o600)
def policies(values):
    if not policy_directory.exists():run(['sudo','install','-d','-m','0755',str(policy_directory)])
    for name,text in values.items():
        target=policy_directory/name
        if target not in policy_paths or os.path.lexists(target):raise RuntimeError('Unexpected policy target')
        source=root/('system-'+name);write(source,text)
        run(['sudo','install','-m','0644','-o','root','-g','root',str(source),str(target)])
        owned_policies[target]=hashlib.sha256(text.encode()).digest()
def clear_policies():
    for path,digest in list(owned_policies.items()):
        value=path.lstat()
        if not stat.S_ISREG(value.st_mode) or value.st_uid!=0 or hashlib.sha256(path.read_bytes()).digest()!=digest:raise RuntimeError('Owned test policy changed; refusing removal')
        run(['sudo','rm','--',str(path)]);del owned_policies[path]
def snapshot(home,context=None):
    paths=list(home.rglob('*.toml'))+[home/'auth.json']+list(owned_policies)
    if context:paths+=list(context.rglob('*.toml'))
    return {str(path):hashlib.sha256(path.read_bytes()).digest() for path in paths if path.is_file()}
def inspect(name,config,expected_store,allowed=True,system=None,selection=None,expected_success=True,workspace=False,api=False,context=None):
    home=root/('home-'+name);home.mkdir(mode=0o700);write(home/'config.toml',config);write(home/'auth.json','{}\n')
    output=root/'reports';output.mkdir(mode=0o700,exist_ok=True);destination=output/(name+'.json')
    if system:policies(system)
    before=snapshot(home,context);env=dict(os.environ,XDG_CONFIG_HOME=str(root/'xdg-config'),XDG_DATA_HOME=str(root/'xdg-data'),XDG_CACHE_HOME=str(root/'xdg-cache'),XDG_RUNTIME_DIR=str(root/'xdg-runtime'))
    env.pop('DBUS_SESSION_BUS_ADDRESS',None)
    command=[str(executable),'--codex-home',str(home),'--verify-client',str(destination)]
    if selection:command+=['--codex-cli',selection[0],'--codex-cli-sha256',selection[1]]
    if context:command+=['--client-context',str(context)]
    try:
        result=subprocess.run(command,env=env,capture_output=True,text=True,timeout=45)
        if not destination.is_file():raise RuntimeError('Native client probe did not produce its sanitized report: '+name)
        value=json.loads(destination.read_text(encoding='utf-8'))
        check(name+' preserves configuration/auth/system-policy input bytes',snapshot(home,context)==before)
        check(name+' reports its actual policy capability',value['inspectionAvailable']==expected_success and result.returncode==(0 if expected_success else 1))
        check(name+' keeps desktop handoff unavailable',value['desktopHandoffAvailable'] is False)
        if expected_success:
            check(name+' names the selected configuration scope',value['configurationScope']==('SelectedDirectoryContext' if context else 'SelectedHomeContext'))
            check(name+' observes effective backend/login/workspace',value['credentialStore']==expected_store and value['fileCredentialsAllowed']==allowed and value['workspaceRestricted']==workspace and value['requiresApiLogin']==api)
            check(name+' preserves explicit-versus-system provenance',value['Provenance']==('ExplicitChecksum' if selection else 'ApprovedSystemPackage') and value['DesktopBinaryApproved']==(selection is None))
        check(name+' report is private and excludes source paths/config/workspace IDs',stat.S_IMODE(destination.stat().st_mode)==0o600 and str(root) not in destination.read_text(encoding='utf-8') and 'synthetic-workspace' not in destination.read_text(encoding='utf-8'))
    finally:clear_policies()
try:
    for name in ('xdg-config','xdg-data','xdg-cache','xdg-runtime'):(root/name).mkdir(mode=0o700)
    native=Path('/usr/bin/true').read_bytes()
    foreign=root/'foreign-client';foreign.write_bytes(native);foreign.chmod(0o555);run(['sudo','chown','65534',str(foreign)])
    capability=root/'capability-client';capability.write_bytes(native);capability.chmod(0o500);run(['sudo','setcap','cap_net_raw=ep',str(capability)])
    env=dict(os.environ,CODEX_SWITCH_LINUX_CLIENT_TEST_ROOT=str(root))
    result=run(['dotnet',str(tests)],env=env);native_result=json.loads(result.stdout)
    check('Native binary trust suite passes on the actual architecture',native_result['failed']==0 and native_result['passed']==15 and native_result['architecture']==a.architecture)
    for store in ('file','keyring','auto','ephemeral'):inspect(store,'cli_auth_credentials_store = "'+store+'"\n',store,store=='file')
    inspect('system-layer','cli_auth_credentials_store = "file"\n','file',system={'config.toml':'cli_auth_credentials_store = "keyring"\n'})
    inspect('managed-backend','cli_auth_credentials_store = "file"\n','keyring',False,system={'requirements.toml':'cli_auth_credentials_store = "keyring"\n'})
    inspect('forced-api','cli_auth_credentials_store = "file"\nforced_login_method = "api"\n','file',False,api=True)
    inspect('managed-login','cli_auth_credentials_store = "file"\n','file',False,system={'requirements.toml':'allowed_login_methods = ["api"]\n'})
    inspect('workspace','cli_auth_credentials_store = "file"\nforced_chatgpt_workspace_id = "synthetic-workspace"\n','file',workspace=True)
    for trusted in (True,False):
        context=root/('project-trusted' if trusted else 'project-untrusted');context.mkdir(mode=0o700);(context/'.git').mkdir(mode=0o700);(context/'.codex').mkdir(mode=0o700)
        write(context/'.codex/config.toml','cli_auth_credentials_store = "keyring"\nforced_login_method = "api"\n')
        config='cli_auth_credentials_store = "file"\n'
        if trusted:config+='[projects.'+json.dumps(str(context))+']\ntrust_level = "trusted"\n'
        inspect('trusted-project' if trusted else 'untrusted-project',config,'keyring' if trusted else 'file',not trusted,api=trusted,context=context)
    manifest=json.loads(Path('src/platforms/client/LinuxClients.json').read_text(encoding='utf-8'))['architectures'][a.architecture]
    inspect('explicit-checksum','cli_auth_credentials_store = "file"\n','file',selection=('/usr/lib/chatgpt/resources/codex',manifest['cliSha256']))
    standalone=Path(os.environ['CODEX_SWITCH_STANDALONE_CLI']).resolve(strict=True)
    inspect('standalone-cli','cli_auth_credentials_store = "file"\n','file',selection=(str(standalone),hashlib.sha256(standalone.read_bytes()).hexdigest()))
    inspect('legacy-profile-refusal','cli_auth_credentials_store = "file"\nprofile = "work"\n[profiles.work]\ncli_auth_credentials_store = "keyring"\n',None,expected_success=False)
    inspect('wrong-checksum','cli_auth_credentials_store = "file"\n',None,selection=('/usr/lib/chatgpt/resources/codex','0'*64),expected_success=False)
    marker=root/'script-executed';script=root/'untrusted-script';write(script,'#!/bin/sh\ntouch '+shlex.quote(str(marker))+'\n');script.chmod(0o500)
    inspect('script-refusal','cli_auth_credentials_store = "file"\n',None,selection=(str(script),hashlib.sha256(script.read_bytes()).hexdigest()),expected_success=False)
    check('An explicitly pinned shell script never executes',not marker.exists())
    value={'schema':1,'architecture':a.architecture,'passed':native_result['passed']+len(checks),'failed':0,'nativeTrust':native_result['passed'],'checks':checks,
        'scope':'Actual pinned official package/CLI and normal config/managed-policy RPC on an owned disposable native runner. Inputs unchanged; no real login, inference, personal home/keyring, installed-client lifecycle or handoff. Source UI remains gated.'}
    report.parent.mkdir(parents=True,exist_ok=True);report.write_text(json.dumps(value,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({key:value[key] for key in ('schema','architecture','passed','failed','nativeTrust','scope')}))
finally:
    clear_policies()
    if created_policy_directory and policy_directory.exists():run(['sudo','rmdir','--',str(policy_directory)])
    if root.parent!=Path(tempfile.gettempdir()).resolve() or root.is_symlink() or root.stat().st_uid!=os.geteuid():raise SystemExit('Unexpected diagnostic cleanup root')
    shutil.rmtree(root)
