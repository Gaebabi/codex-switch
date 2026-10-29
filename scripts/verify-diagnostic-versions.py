"""Check actual protocol/Mac report versions without owner accounts or client actions."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('executable', type=Path)
parser.add_argument('--mode', choices=['switch-protocol', 'macos'], required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
if args.mode == 'switch-protocol' and os.name != 'nt' or args.mode == 'macos' and sys.platform != 'darwin':
    raise SystemExit('Run each native diagnostic on its own OS.')
expected = ET.parse(Path(__file__).resolve().parents[1] / 'Directory.Build.props').findtext('./PropertyGroup/Version')
private_temp = Path(os.environ['LOCALAPPDATA']) / 'Temp' if os.name == 'nt' else None
with tempfile.TemporaryDirectory(prefix='codex-switch-diagnostic-version-', dir=private_temp) as directory:
    report = Path(directory) / 'report.json'
    process = subprocess.run([str(args.executable.resolve(strict=True)), '--verify-' + args.mode, str(report)],
                             stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=75,
                             creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
    if not report.is_file():
        raise SystemExit(f'Actual diagnostic did not produce a report (exit {process.returncode}).')
    value = json.loads(report.read_text(encoding='utf-8'))
    if value.get('version') != expected:
        raise SystemExit(f"Diagnostic version mismatch: expected {expected}, observed {value.get('version')!r}.")
    receipt = {'schema': 1, 'version': value['version'], 'expectedVersion': expected, 'mode': args.mode, 'diagnosticExitCode': process.returncode}
    if args.mode == 'switch-protocol':
        if process.returncode != 0 or value.get('passed') is not True:
            raise SystemExit('Disposable native protocol checks did not pass.')
        receipt['nativeProtocolPassed'] = True
    else:
        checks = value['checks']
        failures = [check for check in checks if check['passed'] is not True]
        if not checks or value['failures'] != len(failures) or process.returncode != (1 if failures else 0):
            raise SystemExit('Native Mac check outcomes and original exit status disagree.')
        discovery = 'Installed app bundle and CLI discovery without launch'
        if any(check['name'] != discovery for check in failures):
            raise SystemExit('Native Mac diagnostics failed outside optional installed-client discovery.')
        receipt.update(nativeChecksPassed=len(checks) - len(failures), nativeChecksFailed=len(failures),
                       installedClientDiscoveryAvailable=not failures)
    receipt['metadataVerified'] = True
    receipt['scope'] = ('Actual published native diagnostic with disposable fixtures. No owner account, inference, desktop launch/shutdown or switch. '
                        'Mac installed-client discovery can remain an explicit failed native check on a runner without Codex; metadata verification is separate from complete installed-client acceptance. No raw errors or paths exported.')
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
print(json.dumps(receipt))
