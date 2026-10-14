"""Validate and export only isolated native app-window evidence, never desktop captures."""
import argparse
import hashlib
import json
from pathlib import Path
import re
from PIL import Image

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('source',type=Path)
parser.add_argument('destination',type=Path)
parser.add_argument('--os',choices=['Windows','macOS'],required=True)
parser.add_argument('--revision',required=True)
args=parser.parse_args()
if not re.fullmatch('[a-f0-9]{40}',args.revision):raise SystemExit('Exact source revision required')
report=args.source/('receipt.json' if args.os=='Windows' else 'render-report.json')
data=json.loads(report.read_text(encoding='utf-8'))
if data['os']!=args.os or data['failures']!=0:raise SystemExit('Native UI verification did not pass')
if args.os=='macOS' and data.get('nativeWindowCapture') is not True:raise SystemExit('Native Mac screenshots required')
pattern=re.compile(r'(en|ko)-(dark|light)-(500x360|600x420|800x560|800x600)'+('' if args.os=='Windows' else '-native')+r'\.png')
extra=re.compile(r'en-(dark|light)-(language|history)'+('' if args.os=='Windows' else '-native')+r'\.png')
paths=sorted(p for p in args.source.glob('*.png') if pattern.fullmatch(p.name) or extra.fullmatch(p.name))
if len(paths)!=(12 if args.os=='Windows' else 16):raise SystemExit('Complete native language/theme/size/menu/history evidence required')
args.destination.mkdir(parents=True,exist_ok=True)
images=[]
for path in paths:
    with Image.open(path) as raw:
        raw.load();rgb=raw.convert('RGB')
        if rgb.width<160 or rgb.height<160 or rgb.width>4000 or rgb.height>3000 or rgb.entropy()<1 or all(low==high for low,high in rgb.getextrema()):
            raise SystemExit('Blank or invalid native window image')
        clean=Image.new('RGB',rgb.size);clean.paste(rgb)
        output=args.destination/path.name;clean.save(output,format='PNG')
        with Image.open(output) as checked:
            if checked.info:raise SystemExit('Native image metadata was not stripped')
        images.append({'file':output.name,'width':rgb.width,'height':rgb.height,'sha256':hashlib.sha256(output.read_bytes()).hexdigest()})
runtime=data['runtime'];version=data.get('appVersion',data.get('version'));architecture=data['architecture']
if not re.fullmatch(r'\.NET \d+\.\d+\.\d+',runtime) or not re.fullmatch(r'\d+\.\d+\.\d+',version) or architecture not in ('X64','Arm64'):
    raise SystemExit('Unexpected runtime/version/architecture metadata')
rendering='Windows native WPF' if args.os=='Windows' else data.get('rendering')
if args.os=='macOS' and rendering!='Software (CI native window)':raise SystemExit('Unexpected Mac evidence rendering mode')
value={'schema':1,'revision':args.revision,'os':args.os,'architecture':architecture,'runtime':runtime,'appVersion':version,'images':images,
    'rendering':rendering,
    'scope':'Native published app on the stated OS; only own app window, synthetic accounts, no personal desktop/credentials/Codex action. No account-switch/resume acceptance.'}
(args.destination/'evidence.json').write_text(json.dumps(value,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'os':args.os,'architecture':architecture,'runtime':runtime,'nativeScreenshots':len(images),'metadataRemoved':True}))
