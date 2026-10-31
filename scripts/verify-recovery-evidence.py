"""Export fictional recovery-state Windows screenshots from the native UI fixture."""
import hashlib
import json
from pathlib import Path
import sys
from PIL import Image

source, destination = map(Path, sys.argv[1:3])
result = json.loads((source / 'recovery-receipt.json').read_text(encoding='utf-8'))
if result.get('passed') is not True or result.get('pendingLabel') != 'Recover' or result.get('completedLabel') != 'Switch':
    raise SystemExit('Recovery UI assertions failed')
destination.mkdir(parents=True, exist_ok=True)
images = []
for name in ('recovery-before.png', 'recovery-after.png'):
    with Image.open(source / name) as raw:
        raw.load()
        rgb = raw.convert('RGB')
        if rgb.width < 400 or rgb.height < 280 or rgb.entropy() < 1:
            raise SystemExit('Recovery frame is blank or too small')
        clean = Image.new('RGB', rgb.size)
        clean.paste(rgb)
        clean.save(destination / name, format='PNG')
    with Image.open(destination / name) as checked:
        if checked.info:
            raise SystemExit('Unexpected screenshot metadata')
    images.append({'file': name, 'sha256': hashlib.sha256((destination / name).read_bytes()).hexdigest()})
(destination / 'receipt.json').write_text(json.dumps({'passed': True, 'images': images, 'scope': result['scope']}, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'passed': True, 'images': len(images)}))
