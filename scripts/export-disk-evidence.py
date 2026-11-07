"""Export identifier-free native disk counter verification, not benchmark claims."""
import argparse
import json
from pathlib import Path
import re

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--revision',required=True)
args=parser.parse_args()
if not re.fullmatch('[a-f0-9]{40}',args.revision):raise SystemExit('Exact disk evidence revision required')
data=json.loads(Path('artifacts/disk-io-native.json').read_text(encoding='utf-8'))
if data['failed'] or data['passed']!=22:raise SystemExit('Hardware/disk contracts did not pass')
native=data['nativeDiskIo']
if native['state']!='Available' or not native['readAvailable'] or not native['writeAvailable']:raise SystemExit('Native disk counters were not actually available')
if native['profile'] not in {'Unknown','SeekPenalty','NoSeekPenalty','Mixed'}:raise SystemExit('Invalid disk capability profile')
value={'schema':1,'revision':args.revision,'os':'Windows','hardwareChecks':data['passed'],'nativeCounterState':native['state'],
       'profile':native['profile'],'readAvailable':True,'writeAvailable':True,'busyAvailable':native['busyAvailable'],
       'scope':'Actual read-only volume counters on disposable hosted Windows. No generated disk load, filesystem contents, device names/serials, benchmark, personal data or account actions. Unknown capabilities never imply a manufacturer rating.'}
root=Path('artifacts/disk-io-public');root.mkdir(parents=True,exist_ok=True)
(root/'verification.json').write_text(json.dumps(value,indent=2)+'\n',encoding='utf-8')
print(json.dumps(value))
