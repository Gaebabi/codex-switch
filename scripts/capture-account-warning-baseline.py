"""Reproduce the original Windows footer on a disposable native worker without changing its production source."""
import argparse
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import subprocess
import sys
import tarfile

BASELINE='9d7b8ec57c1a1008e5dafff527ad76ffa29fd19f'
parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--output',type=Path,required=True);args=parser.parse_args()
root=Path(__file__).resolve().parents[1];output=args.output.resolve()
if sys.platform!='win32' or os.environ.get('GITHUB_ACTIONS')!='true' or os.environ.get('RUNNER_OS')!='Windows' or os.environ.get('CODEX_SWITCH_DISPOSABLE_RUNNER')!='true':
    raise SystemExit('Only the explicitly disposable hosted native Windows worker is allowed')
if output.exists() or not output.is_relative_to(root/'artifacts'):raise SystemExit('Use a fresh owned ignored output folder')
def run(command,**kwargs):return subprocess.run(command,check=True,timeout=kwargs.pop('timeout',60),**kwargs)
if subprocess.run(['git','cat-file','-e',BASELINE+'^{commit}'],cwd=root,capture_output=True).returncode:
    run(['git','fetch','--no-tags','--depth=1','origin',BASELINE],cwd=root)
revision=run(['git','rev-parse','HEAD'],cwd=root,capture_output=True,text=True).stdout.strip()
archive=run(['git','archive',BASELINE],cwd=root,capture_output=True).stdout
source=output/'source';source.mkdir(parents=True)
with tarfile.open(fileobj=io.BytesIO(archive)) as bundle:
    entries=bundle.getmembers()
    if sum(e.size for e in entries)>500_000_000:raise SystemExit('Baseline source exceeds the declared budget')
    for entry in entries:
        path=PurePosixPath(entry.name)
        if path.is_absolute() or '..' in path.parts or not(entry.isfile() or entry.isdir()):raise SystemExit('Unsafe baseline archive entry')
    bundle.extractall(source,filter='data')
files=['src/windows/MainWindow.cs','src/windows/AccountCardHeader.cs','src/windows/AccountTankCard.cs','src/windows/DriveUsagePanel.cs','src/windows/ToolTipTheme.cs']
digest=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
original={name:digest(source/name) for name in files}
host=source/'src/windows/NativeUiEvidence.cs';text=host.read_text(encoding='utf-8')
marker='\n            }\n            catch (Exception error) { exit = 1;'
if text.count(marker)!=1:raise SystemExit('Original diagnostic hosting changed')
text=text.replace(marker,'\n                await AccountWarningEvidence.CaptureAsync(window, service, directory, baseline: true);'+marker)
host.write_text(text,encoding='utf-8')
(source/'src/windows/AccountWarningEvidence.cs').write_bytes((root/'src/windows/AccountWarningEvidence.cs').read_bytes())
app=output/'published'
run(['dotnet','publish',str(source/'src/windows/CodexSwitch.Windows.csproj'),'-c','Release','-r','win-x64','--self-contained','true','-p:UseSharedCompilation=false','-o',str(app)],cwd=source,timeout=300)
run([str(app/'CodexSwitch.exe'),'--verify-ui',str(output/'raw')],cwd=source,timeout=60)
if original!={name:digest(source/name) for name in files}:raise SystemExit('Original production sources changed')
run([sys.executable,str(root/'scripts/verify-account-warning-evidence.py'),str(output/'raw'),str(output/'public'),'--revision',revision,'--application-source',BASELINE,'--baseline'],cwd=root)
proof=output/'public/evidence.json';value=json.loads(proof.read_text())
value.update(originalProductionSourceSha256=original,diagnosticHostingInstrumented=True,sourceArchiveSha256=hashlib.sha256(archive).hexdigest())
proof.write_text(json.dumps(value,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'originalSource':BASELINE,'testRevision':revision,'originalProductionSourcesUnchanged':True,'nativeBeforeImages':4}))
