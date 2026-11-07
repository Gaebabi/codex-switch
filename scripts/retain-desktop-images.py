"""Retain three offline test images; never remove images referenced by containers."""
import argparse
import json
import re
import subprocess
from datetime import datetime, timezone, timedelta

def docker(*arguments):
    return subprocess.run(['docker', *arguments], check=True, capture_output=True,
                          text=True, encoding='utf-8', timeout=120).stdout

def retain(keep=3, minimum_hours=6):
    rows = [json.loads(line) for line in docker('image', 'ls', '--no-trunc', '--format', '{{json .}}').splitlines() if line]
    rows = [row for row in rows if row['Repository'] == 'codex-switch-desktop-session'
            and re.fullmatch(r'[0-9a-f]{12}', row['Tag'])]
    images = {}
    for row in rows:
        images.setdefault(row['ID'], row)
    ordered = []
    for identifier, row in images.items():
        created = json.loads(docker('image', 'inspect', '--format', '{{json .Created}}', identifier))
        # Docker emits nanosecond fractions; datetime accepts microsecond precision.
        moment = datetime.fromisoformat(created.replace('Z', '+00:00'))
        ordered.append((moment, identifier))
    ordered.sort(reverse=True)
    containers = docker('ps', '-aq').splitlines()
    referenced = set()
    for identifier in containers:
        referenced.add(docker('inspect', '--format', '{{.Image}}', identifier).strip())
    cutoff = datetime.now(timezone.utc) - timedelta(hours=minimum_hours)
    removed = []
    for created, identifier in ordered[keep:]:
        if created >= cutoff or identifier in referenced:
            continue
        # Without --force Docker also refuses a container-reference race.
        docker('image', 'rm', identifier)
        removed.append(identifier)
    return {'family': 'codex-switch-desktop-session', 'keep': keep,
            'minimumHours': minimum_hours, 'removedImages': removed,
            'containersAndVolumesRemoved': False}

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--keep', type=int, default=3)
    args = parser.parse_args()
    if args.keep < 1:
        parser.error('--keep must be positive')
    print(json.dumps(retain(args.keep)))
