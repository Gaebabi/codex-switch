"""Export only verified owned native credit UI screenshots with fictional data."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET
from PIL import Image

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('source',type=Path)
p.add_argument('destination',type=Path)
p.add_argument('--os',choices=['Windows','macOS'],required=True)
p.add_argument('--revision',required=True)
a=p.parse_args()
if not re.fullmatch('[0-9a-f]{40}',a.revision):raise SystemExit('Exact committed revision required')
r=json.loads((a.source/'credit-receipt.json').read_text(encoding='utf-8'))
expected=41 if a.os=='Windows' else 42
if r['os']!=a.os or r['failed']!=0 or r['passed']!=expected or len(r['checks'])!=expected or len(set(r['checks']))!=expected:
    raise SystemExit('All declared native credit checks must pass')
if r['architecture'] not in (['X64'] if a.os=='Windows' else ['X64','Arm64']):raise SystemExit('Native architecture mismatch')
root=Path(__file__).resolve().parents[1]
props=ET.parse(root/'Directory.Build.props')
if r['version']!=props.findtext('./PropertyGroup/Version') or r['runtime']!='.NET '+props.findtext("./PropertyGroup/RuntimeFrameworkVersion"):
    raise SystemExit('Declared app and reviewed runtime versions required')
cases=['allowed','protected','past-expiry','paused','history','empty','minimum']
names={f'{language}-{theme}-{case}.png' for language in ['en','ko'] for theme in ['dark','light'] for case in cases}
if set(r['images'])!=names or len(r['images'])!=len(names) or {p.name for p in a.source.glob('*.png')}!=names:
    raise SystemExit('Complete credit/history/protection/past-date native confirmation set required')
a.destination.mkdir(parents=True,exist_ok=True)
images=[]
for name in sorted(names):
    with Image.open(a.source/name) as frame:
        rgb=frame.convert('RGB')
        if rgb.width<140 or rgb.height<80 or rgb.width>4000 or rgb.height>3000 or rgb.entropy()<1:
            raise SystemExit('Blank or invalid native credit capture')
        clean=Image.new('RGB',rgb.size);clean.paste(rgb);output=a.destination/name;clean.save(output)
    with Image.open(output) as exported:
        if exported.info or exported.convert('RGB').tobytes()!=rgb.tobytes():raise SystemExit('Original app pixels must be preserved without metadata')
    images.append({'file':name,'width':rgb.width,'height':rgb.height,'sha256':hashlib.sha256(output.read_bytes()).hexdigest(),
                   'capture':'Actual owned native application window or popup','originalPixelsUnchanged':True})
sources=['src/core/CreditHistory.cs','src/core/CreditPresentation.cs','src/core/Models.cs','src/core/UsageAnalysis.cs','src/core/AutomaticSwitchPolicy.cs','src/core/AccountSwitchCoordinator.cs',
         'src/application/AccountAutomation.cs','src/application/AccountService.cs','src/application/Storage.cs','src/application/VaultData.cs','src/application/DesktopSwitchBackend.cs',
         'src/windows/AccountDetailWindow.cs','src/windows/CreditTrendChart.cs','src/windows/MainWindow.cs','src/windows/CreditEvidence.cs',
         'src/desktop/HistoryWindow.cs','src/desktop/CreditChart.cs','src/desktop/DeskWindow.cs','src/macos/CreditEvidence.cs','Directory.Build.props']
receipt={**r,'schema':1,'revision':a.revision,'nativeWindowCapture':True,'personalDataUsed':False,'images':images,
         'sourceHashes':{path:hashlib.sha256((root/path).read_bytes()).hexdigest() for path in sources},
         'rendering':'Windows native WPF' if a.os=='Windows' else 'Software (CI native window)'}
(a.destination/'evidence.json').write_text(json.dumps(receipt,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'os':a.os,'architecture':r['architecture'],'nativeChecks':r['passed'],'nativeImages':len(images),'originalPixelsPreserved':True,'metadataRemoved':True}))
