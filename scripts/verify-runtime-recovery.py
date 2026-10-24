"""Capture only fictional app windows on disposable hosted Windows CI."""
import hashlib,json,os,shutil,subprocess,tarfile,io
from pathlib import Path
from PIL import Image

root=Path(__file__).resolve().parents[1]
if os.environ.get('GITHUB_ACTIONS')!='true' or os.environ.get('RUNNER_OS')!='Windows':
    raise SystemExit('Native recovery verification requires the disposable hosted Windows workflow')
baseline='dd7fe742d01a15740784c13c953165b8f2999137'
output=root/'artifacts/runtime-recovery';output.mkdir(parents=True,exist_ok=False)
source=output/'baseline-source'
def run(*args,cwd=root):
    result=subprocess.run(args,cwd=cwd,creationflags=subprocess.CREATE_NO_WINDOW,capture_output=True,timeout=300)
    if result.returncode: raise RuntimeError(result.stderr.decode('utf-8',errors='replace')[-2500:] or result.stdout.decode('utf-8',errors='replace')[-2500:])
    return result.stdout
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
run('git','fetch','--no-tags','--depth=1','origin',baseline)
assert run('git','rev-parse',baseline+'^{commit}').decode().strip()==baseline
with tarfile.open(fileobj=io.BytesIO(run('git','archive',baseline))) as archive:
    for member in archive.getmembers():
        path=Path(member.name)
        if path.is_absolute() or '..' in path.parts or not (member.isfile() or member.isdir()):
            raise ValueError('Unsafe baseline source archive')
    archive.extractall(source,filter='data')
original={str(p.relative_to(source)).replace('\\','/'):sha(p) for p in (source/'src').rglob('*') if p.is_file()}
shutil.copytree(root/'tests/runtime-recovery',source/'tests/runtime-recovery',ignore=shutil.ignore_patterns('bin','obj'))
service=json.loads(run('dotnet','run','--project','tests/runtime-recovery/RuntimeRecovery.Tests.csproj','-c','Release','--no-launch-profile','-p:UseSharedCompilation=false').decode().strip().splitlines()[-1])
assert service['passed']==9
for name,workspace,extra in [('before',source,['--baseline']),('after',root,[])]:
    run('dotnet','run','--project','tests/runtime-recovery/RuntimeRecovery.Tests.csproj','-c','Release','--no-launch-profile','-p:UseSharedCompilation=false','--','--evidence',str(output/name),*extra,cwd=workspace)
assert all(sha(source/path)==value for path,value in original.items())
public=output/'public';public.mkdir()
images=[]
for folder,filename in [('before','before.png'),('after','after.png'),('after','update-gap.png'),('after','automatic-recovery.png')]:
    path=output/folder/filename
    with Image.open(path) as image:
        pixels=image.convert('RGBA').tobytes();size=image.size
        image.save(public/filename,format='PNG')
    with Image.open(public/filename) as clean:
        assert not clean.info and clean.size==size and clean.convert('RGBA').tobytes()==pixels
    images.append({'file':filename,'width':size[0],'height':size[1],'sha256':sha(public/filename),'originalPixelsUnchanged':True})
receipt={'baselineSource':baseline,'sourceRevision':os.environ.get('EVIDENCE_REVISION') or run('git','rev-parse','HEAD').decode().strip(),'service':service,
    'before':json.loads((output/'before/receipt.json').read_text('utf-8')),'after':json.loads((output/'after/receipt.json').read_text('utf-8')),
    'baselineProductionFilesUnchanged':True,'personalDataUsed':False,'images':images,
    'sourceHashes':{p:sha(root/p) for p in ['src/application/AccountService.cs','tests/runtime-recovery/Program.cs','tests/runtime-recovery/WindowEvidence.cs','scripts/verify-runtime-recovery.py']},
    'scope':'Actual hosted Windows WPF button/timer and unchanged baseline with fictional authentication and offline disposable RPC. No live Store update, authenticated refresh, real account/client or owner input.'}
assert receipt['before']['passed']==4 and receipt['after']['passed']==6
(public/'verification.json').write_text(json.dumps(receipt,indent=2)+'\n','utf-8')
print(json.dumps({'service':9,'nativeBefore':4,'nativeAfter':6,'originalProductionBaselineUnchanged':True,'sourceRevision':receipt['sourceRevision']}))
