"""Export passing fictional account-automation captures from actual app-owned HWNDs."""
import argparse
import hashlib
import json
from pathlib import Path
import re
from PIL import Image

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('source', type=Path)
p.add_argument('destination', type=Path)
p.add_argument('--revision', required=True)
a = p.parse_args()
if not re.fullmatch(r'[0-9a-f]{40}', a.revision):
    raise SystemExit('Exact committed revision required')
receipt = json.loads((a.source / 'account-automation-receipt.json').read_text(encoding='utf-8'))
if receipt['os'] != 'Windows' or receipt['architecture'] != 'X64' or receipt['passed'] != 36 or receipt['failed'] != 0:
    raise SystemExit('All 36 actual application account-automation checks must pass')
names = {f'{language}-{theme}-account-automation-500x360.png' for language in ['en', 'ko'] for theme in ['dark', 'light']}
names |= {f'en-{theme}-account-automation-{case}.png' for theme in ['dark', 'light'] for case in ['enabled', 'menu', 'order']}
names |= {f'{language}-auto-switch-next-{state}.png' for language in ['en', 'ko'] for state in ['off', 'on']}
if set(receipt['images']) != names or len(receipt['images']) != len(names):
    raise SystemExit('All declared actual app/menu confirmation images required')
a.destination.mkdir(parents=True, exist_ok=True)
images = []
for name in sorted(names):
    with Image.open(a.source / name) as original:
        original.load()
        rgb = original.convert('RGB')
        if rgb.width < 160 or rgb.height < 25 or rgb.width > 1200 or rgb.height > 900 or rgb.entropy() < 1:
            raise SystemExit('Invalid native app/menu image')
        clean = Image.new('RGB', rgb.size)
        clean.paste(rgb)
        path = a.destination / name
        clean.save(path)
        with Image.open(path) as checked:
            if checked.info or checked.convert('RGB').tobytes() != rgb.tobytes():
                raise SystemExit('Original native pixels must be retained without metadata')
        images.append({'file': name, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'width': rgb.width, 'height': rgb.height,
                       'capture': 'Actual app-owned Windows HWND or native menu popup', 'originalPixelsUnchanged': True})
receipt.update(revision=a.revision, personalDataUsed=False, images=images)
(a.destination / 'evidence.json').write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'passed': receipt['passed'], 'nativeImages': len(images), 'personalDataUsed': False}))
