"""Export only the passing disposable Windows startup-recovery window evidence."""
import argparse
import hashlib
import json
from pathlib import Path
import re
from PIL import Image

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('source', type=Path)
p.add_argument('output', type=Path)
p.add_argument('--revision', required=True)
a = p.parse_args()
if re.fullmatch(r'[0-9a-f]{40}', a.revision) is None:
    raise SystemExit('Exact committed source required')
receipt = json.loads((a.source/'receipt.json').read_text(encoding='utf-8'))
if receipt['failed'] != 0 or receipt['passed'] != 12 or receipt['os'] != 'Windows' or receipt['architecture'] != 'X64' or receipt['runtime'] != '.NET 10.0.13':
    observed = {key: receipt[key] for key in ('os', 'architecture', 'runtime', 'passed', 'failed')}
    raise SystemExit('Complete native Windows manual/timer recovery checks must pass: ' + json.dumps(observed))
a.output.mkdir(parents=True, exist_ok=False)
images = []
for mode in ('manual', 'automatic'):
    for state in ('unavailable', 'recovered'):
        name = mode + '-' + state + '.png'
        with Image.open(a.source/name) as source:
            rgb = source.convert('RGB')
            if rgb.width < 500 or rgb.height < 360 or rgb.entropy() < 1 or all(low == high for low, high in rgb.getextrema()):
                raise SystemExit('Actual nonblank native app window required')
            clean = Image.new('RGB', rgb.size); clean.paste(rgb)
            clean.save(a.output/name)
        if Image.open(a.output/name).info:
            raise SystemExit('Image metadata was not stripped')
        images.append({'file':name,'width':clean.width,'height':clean.height,'sha256':hashlib.sha256((a.output/name).read_bytes()).hexdigest()})
receipt.update(revision=a.revision, images=images, metadataRemoved=True)
(a.output/'evidence.json').write_text(json.dumps(receipt, indent=2)+'\n',encoding='utf-8')
print(json.dumps({'passed':receipt['passed'],'failed':0,'nativeScreenshots':len(images),'metadataRemoved':True}))
