"""Run one owned clean system-package case and optional per-user lifecycle."""
import json
from pathlib import Path
import subprocess
import sys

case=json.loads(Path('/input/expected.json').read_text(encoding='utf-8'))
subprocess.run([sys.executable,'/input/verify-package-system.py','--family',case['family'],'--report','/results/system.json','--raw-ui','/results/raw'],check=True,timeout=200)
if case['id']=='debian13':
 subprocess.run([sys.executable,'/input/verify-linux-install.py','--archive','/input/current.tar.gz','--previous-archive','/input/previous.tar.gz','--report','/results/per-user.json','--raw-ui','/results/per-user-raw'],check=True,timeout=240)
