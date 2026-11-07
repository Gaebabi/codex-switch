"""Capture the unchanged main history renderer on the same native Mac CI worker."""
import argparse
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import platform
import re
import shutil
import subprocess
import sys
import tarfile
from PIL import Image

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--revision',required=True)
p.add_argument('--rid',choices=['osx-arm64','osx-x64'],required=True)
p.add_argument('--output',type=Path,required=True)
a=p.parse_args()
root=Path(__file__).resolve().parents[1]
if sys.platform!='darwin' or platform.machine()!=('arm64' if a.rid=='osx-arm64' else 'x86_64'):
    raise SystemExit('Use the declared native Mac architecture')
if not re.fullmatch('[0-9a-f]{40}',a.revision):raise SystemExit('Exact original main source required')
output=a.output.resolve()
if output.exists() or not output.is_relative_to(root/'artifacts'):raise SystemExit('Use a fresh ignored output directory')
def run(args,**kwargs):return subprocess.run(args,check=True,timeout=kwargs.pop('timeout',60),**kwargs)
available=subprocess.run(['git','cat-file','-e',a.revision+'^{commit}'],cwd=root,capture_output=True)
if available.returncode:run(['git','fetch','--no-tags','--depth=1','origin',a.revision],cwd=root)
test_revision=run(['git','rev-parse','HEAD'],cwd=root,capture_output=True,text=True).stdout.strip()
output.mkdir(parents=True)
source=output/'source';source.mkdir()
archive=run(['git','archive',a.revision],cwd=root,capture_output=True).stdout
with tarfile.open(fileobj=io.BytesIO(archive)) as bundle:
    entries=bundle.getmembers()
    if sum(e.size for e in entries)>500_000_000:raise SystemExit('Original source archive exceeds the declared budget')
    for entry in entries:
        path=PurePosixPath(entry.name)
        if path.is_absolute() or '..' in path.parts or not (entry.isfile() or entry.isdir()):raise SystemExit('Unsafe original source archive entry')
    bundle.extractall(source,filter='data')
renderer_paths=['src/desktop/HistoryWindow.cs','src/desktop/TankAndCharts.cs','src/desktop/DeskTheme.cs']
original={name:hashlib.sha256((source/name).read_bytes()).hexdigest() for name in renderer_paths}
program=source/'src/macos/Program.cs'
text=program.read_text(encoding='utf-8')
needle='MacDiagnostics.RenderAsync(window, PreviewDirectory)'
if text.count(needle)!=1:raise SystemExit('Original diagnostic entry point changed; review its adapter')
program.write_text(text.replace(needle,'HistoryBeforeCapture.RenderAsync(window, PreviewDirectory)'),encoding='utf-8')
adapter=root/'scripts/history-baseline/HistoryBeforeCapture.cs'
fixture=root/'src/macos/HistorySamples.cs'
shutil.copyfile(adapter,source/'src/macos/HistoryBeforeCapture.cs')
shutil.copyfile(fixture,source/'src/macos/HistorySamples.cs')
if original!={name:hashlib.sha256((source/name).read_bytes()).hexdigest() for name in renderer_paths}:
    raise SystemExit('Capture instrumentation altered original production history sources')
app=output/'published'
run(['dotnet','publish',str(source/'src/macos/CodexSwitch.Mac.csproj'),'-c','Release','-r',a.rid,'--self-contained','true','-p:UseSharedCompilation=false','-o',str(app)],cwd=root,timeout=240)
raw=output/'raw'
run([str(app/'CodexSwitch'),'--verify-ui',str(raw)],cwd=root,timeout=60)
report=json.loads((raw/'before-report.json').read_text(encoding='utf-8'))
if report['failures'] or len(report['checks'])!=4 or report['os']!='macOS' or report['nativeWindowCapture'] is not True or report['language']!='en' or report['architecture']!=('Arm64' if a.rid=='osx-arm64' else 'X64'):
    raise SystemExit('Original native history capture failed')
expected={'en-dark-history-native.png','en-light-history-native.png'}
if {p.name for p in raw.glob('*.png')}!=expected:raise SystemExit('Both original themes are required')
public=output/'public';public.mkdir();images=[]
for name in sorted(expected):
    with Image.open(raw/name) as frame:
        rgb=frame.convert('RGB')
        if rgb.width<500 or rgb.height<300 or rgb.entropy()<1:raise SystemExit('Original native history capture is blank or too small')
        image=Image.new('RGB',rgb.size);image.paste(rgb);image.save(public/name)
    with Image.open(public/name) as checked:
        if checked.info:raise SystemExit('Original capture metadata was not removed')
    images.append({'file':name,'width':rgb.width,'height':rgb.height,'sha256':hashlib.sha256((public/name).read_bytes()).hexdigest(),'capture':'Actual app-owned original history NSWindow; capture-only host instrumentation and fictional inputs'})
receipt={'schema':1,'revision':test_revision,'applicationSource':a.revision,'os':report['os'],'architecture':report['architecture'],'appVersion':report['version'],'runtime':report['runtime'],'language':'en','nativeWindowCapture':True,'checks':report['checks'],'unchangedRendererSha256':original,'adapterSha256':hashlib.sha256(adapter.read_bytes()).hexdigest(),'fixtureSha256':hashlib.sha256(fixture.read_bytes()).hexdigest(),'images':images,'scope':report['scope']}
(public/'evidence.json').write_text(json.dumps(receipt,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'originalHistoryImages':len(images),'unchangedProductionRenderers':len(original),'nativeArchitecture':report['architecture'],'baselineSource':a.revision,'testSource':test_revision}))
