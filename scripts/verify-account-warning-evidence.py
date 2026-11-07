"""Export only source-bound fictional native Windows account-warning evidence."""
import argparse
import hashlib
import json
from pathlib import Path
import re
from PIL import Image

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('source',type=Path);parser.add_argument('destination',type=Path)
parser.add_argument('--revision',required=True);parser.add_argument('--application-source')
parser.add_argument('--baseline',action='store_true');args=parser.parse_args()
if not re.fullmatch('[0-9a-f]{40}',args.revision) or args.application_source and not re.fullmatch('[0-9a-f]{40}',args.application_source):raise SystemExit('Exact source revision required')
report=json.loads((args.source/'account-warning-receipt.json').read_text())
if report['os']!='Windows' or report['architecture']!='X64' or report['failed']!=0 or report['baseline']!=args.baseline or report['passed']!=(8 if args.baseline else 44):
    raise SystemExit('The complete native warning verification must pass; observed '+str(report.get('passed'))+' checks')
if not args.baseline:
    for check in ['Click requests browser reauthentication for the selected account without opening a real login in evidence mode',
                  'Hover retains the themed wrapped full explanation and reauthentication hint']:
        if report['checks'].count(check)!=2:raise SystemExit('Both themes require the account-specific login and independent hover checks')
names={f'en-{theme}-account-warning-{case}.png' for theme in ['dark','light'] for case in (['600x440','500x360'] if args.baseline else ['600x440','500x360','tooltip','global'])}
if set(report['images'])!=names or len(report['images'])!=len(names):raise SystemExit('Complete declared native warning images required')
args.destination.mkdir(parents=True,exist_ok=True);images=[]
for name in sorted(names):
    with Image.open(args.source/name) as raw:
        raw.load();rgb=raw.convert('RGB')
        if rgb.width<160 or rgb.height<25 or rgb.width>1200 or rgb.height>900 or rgb.entropy()<1:raise SystemExit('Invalid native app/popup image')
        clean=Image.new('RGB',rgb.size);clean.paste(rgb);target=args.destination/name;clean.save(target)
        with Image.open(target) as checked:
            if checked.info or checked.convert('RGB').tobytes()!=rgb.tobytes():raise SystemExit('Image pixels changed or metadata remained')
        images.append({'file':name,'sha256':hashlib.sha256(target.read_bytes()).hexdigest(),'width':rgb.width,'height':rgb.height,'capture':'Actual owned app/popup HWND','originalPixelsUnchanged':True})
proof={'schema':1,'revision':args.revision,'applicationSource':args.application_source or args.revision,'os':'Windows','architecture':'X64','appVersion':report['appVersion'],
       'baseline':args.baseline,'passed':report['passed'],'failed':0,'checks':report['checks'],'images':images,'personalDataUsed':False,'scope':report['scope']}
(args.destination/'evidence.json').write_text(json.dumps(proof,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'baseline':args.baseline,'passed':report['passed'],'nativeImages':len(images),'originalPixelsUnchanged':True,'personalDataUsed':False}))
