"""Verify the exact reviewed signed modules and their corresponding source payload."""
import hashlib
from pathlib import Path

root = Path(__file__).resolve().parents[1]
modules = root / 'src/sensors/modules'
expected = {'AMDFamily17.bin': 'cd59598344b54a23178ccf9223239f60a3111bd86ff2c7bea2c9d1cef89e095b',
            'IntelMSR.bin': 'f3ab69f0a2686813de2f47efcda6271e176c4ee50fab5f107254f7d6a4e77361'}
if {p.name for p in modules.glob('*.bin')} != set(expected):
    raise SystemExit('Only the two reviewed CPU modules may be embedded')
for name, digest in expected.items():
    if hashlib.sha256((modules/name).read_bytes()).hexdigest() != digest:
        raise SystemExit('Signed CPU module digest changed; upstream/security review required')
for name in ['AMDFamily17.p', 'IntelMSR.p', 'pawnio.inc', 'native.inc', 'ntstatus.inc', 'core.inc', 'COPYING', 'BUILD.txt']:
    if not (modules/'source'/name).is_file():
        raise SystemExit('Corresponding CPU module source/build/license is missing')
if (root/'licenses/PawnIO-Modules.LGPL-2.1.txt').read_bytes() != (modules/'source/COPYING').read_bytes():
    raise SystemExit('CPU module license mismatch')
print('Two pinned official signed CPU modules and corresponding source/license files verified.')
