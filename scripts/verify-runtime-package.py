"""Inspect a complete self-contained published folder without executing foreign binaries."""
import argparse
import json
from pathlib import Path

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('directory',type=Path)
parser.add_argument('--rid',choices=['win-x64','osx-arm64','osx-x64','linux-x64','linux-arm64'],required=True)
parser.add_argument('--runtime',default='10.0.13')
parser.add_argument('--output',type=Path,required=True)
args=parser.parse_args();root=args.directory
data=json.loads((root/'CodexSwitch.runtimeconfig.json').read_text(encoding='utf-8'))['runtimeOptions']
frameworks={value['name']:value['version'] for value in data.get('includedFrameworks',[])}
expected={'Microsoft.NETCore.App':args.runtime}
if args.rid.startswith('win'):expected['Microsoft.WindowsDesktop.App']=args.runtime
if data['tfm']!='net10.0' or frameworks!=expected:raise SystemExit('Package does not contain the reviewed self-contained runtime')
names=['CodexSwitch.dll','CodexSwitch.deps.json','System.Private.CoreLib.dll']+(['CodexSwitch.exe','coreclr.dll','hostfxr.dll'] if args.rid.startswith('win') else
    ['CodexSwitch','libcoreclr.so','libhostfxr.so'] if args.rid.startswith('linux') else ['CodexSwitch','libcoreclr.dylib','libhostfxr.dylib'])
if any(not(root/name).is_file() for name in names):raise SystemExit('Incomplete self-contained package')
deps=json.loads((root/'CodexSwitch.deps.json').read_text(encoding='utf-8'))
if not deps['runtimeTarget']['name'].endswith('/'+args.rid):raise SystemExit('Runtime identifier mismatch')
target=deps['targets'][deps['runtimeTarget']['name']]
project_assets=sorted(asset for key,value in deps['libraries'].items() if value['type']=='project' for asset in target[key].get('runtime',{}))
required={'CodexSwitch.Core.dll','CodexSwitch.PlatformSecurity.dll','CodexSwitch.DesktopClient.dll','CodexSwitch.Protocol.dll','CodexSwitch.Sqlite.dll','CodexSwitch.Hardware.dll','CodexSwitch.Application.dll'}
if not args.rid.startswith('win'):required.add('CodexSwitch.Desktop.dll')
if not required.issubset(project_assets) or any(not(root/asset).is_file() for asset in project_assets):
    raise SystemExit('Published project dependencies are incomplete')
value={'schema':1,'rid':args.rid,'tfm':data['tfm'],'frameworks':frameworks,'files':sum(path.is_file() for path in root.rglob('*')),
    'projectRuntimeAssets':project_assets,
    'scope':'Full published-folder runtime/target inspection; foreign binaries not executed, no install/account/switch acceptance.'}
args.output.parent.mkdir(parents=True,exist_ok=True);args.output.write_text(json.dumps(value,indent=2)+'\n',encoding='utf-8');print(json.dumps(value))
