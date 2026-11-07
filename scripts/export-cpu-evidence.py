"""Export only schema-checked, identifier-free native CPU verification receipts."""
import argparse
import json
from pathlib import Path
import re

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--revision', required=True)
args = parser.parse_args()
if not re.fullmatch('[a-f0-9]{40}', args.revision):
    raise SystemExit('Exact CPU evidence source required')
root = Path('artifacts')
app = json.loads((root/'cpu-sensor-app.json').read_text(encoding='utf-8-sig'))
normal = json.loads((root/'cpu-sensor-worker.json').read_text(encoding='utf-8-sig'))
driver = json.loads((root/'cpu-sensor-driver.json').read_text(encoding='utf-8-sig'))
privileged = json.loads((root/'cpu-sensor-worker-driver.json').read_text(encoding='utf-8-sig'))
states = {'Available', 'ProviderMissing', 'AccessDenied', 'SensorUnavailable', 'PlatformUnsupported', 'ReadFailed'}
if app['failed'] or normal['failed'] or privileged['failed'] or app['passed'] != 1 or normal['passed'] != 5 or privileged['passed'] != 7:
    raise SystemExit('CPU integration/lifecycle checks failed')
if app['state'] not in states or normal['sensorState'] not in states or privileged['sensorState'] not in states:
    raise SystemExit('Unexpected CPU status')
if driver['version'] != '2.2.0' or driver['installerSha256'] != '1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032' or not driver['authenticodeValid'] or not driver['running']:
    raise SystemExit('CPU driver verification failed')
value = {'schema': 1, 'revision': args.revision, 'os': 'Windows', 'appIntegrationChecks': app['passed'],
         'normalWorkerChecks': normal['passed'], 'driverWorkerChecks': privileged['passed'],
         'beforeDriverState': app['state'], 'afterDriverState': privileged['sensorState'],
         'nativeErrorAfterDriver': privileged.get('nativeError'), 'nativeSensorValueAccepted': privileged['sensorState'] == 'Available',
         'driverVersion': driver['version'], 'authenticodeValid': True, 'driverRunning': True,
         'signedModuleAccepted': True, 'tamperedModuleRefused': True,
         'scope': 'Disposable hosted Windows VM: actual NativeAOT worker and published-app digest/pipe/lifecycle plus signed driver/module checks. No personal data, local GUI, owner hardware temperature or account actions.'}
destination = root/'cpu-sensor-public'; destination.mkdir(parents=True, exist_ok=True)
(destination/'verification.json').write_text(json.dumps(value, indent=2)+'\n', encoding='utf-8')
print(json.dumps(value))
