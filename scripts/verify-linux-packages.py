"""Verify native preview candidates in pinned fresh images with no SDK or personal mounts."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import sys
import uuid

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--packages',type=Path,required=True)
p.add_argument('--previous',type=Path,required=True)
p.add_argument('--rid',choices=['linux-x64','linux-arm64'],required=True)
p.add_argument('--output',type=Path,required=True)
a=p.parse_args();repository=Path(__file__).resolve().parents[1];packages=a.packages.resolve();previous=a.previous.resolve();output=a.output.resolve()
if sys.platform!='linux' or os.geteuid()==0 or os.environ.get('CODEX_SWITCH_DISPOSABLE_RUNNER')!='true' or {'x86_64':'linux-x64','aarch64':'linux-arm64'}.get(platform.machine())!=a.rid:raise SystemExit('Use the explicitly disposable normal-user native package worker')
if output.exists() or not output.is_relative_to(repository/'artifacts') or output==repository/'artifacts':raise SystemExit('Use a fresh dedicated ignored result directory')
provenance=json.loads((packages/'PROVENANCE.json').read_text(encoding='utf-8'));head=subprocess.run(['git','rev-parse','HEAD'],cwd=repository,capture_output=True,text=True,check=True).stdout.strip()
if provenance['sourceRevision']!=head or provenance['rid']!=a.rid or provenance['verificationOnlyVersionOverride'] or not provenance['unsignedPreview'] or provenance['supportedAccountHandoff']:raise SystemExit('Package source/capability provenance mismatch')
artifacts={}
for entry in provenance['artifacts']:
 file=packages/entry['file']
 if file.parent!=packages or hashlib.sha256(file.read_bytes()).hexdigest()!=entry['sha256']:raise SystemExit('Candidate package SHA mismatch')
 artifacts['tar' if file.name.endswith('.tar.gz') else file.suffix[1:]]=file
if set(artifacts)!={'tar','deb','rpm'}:raise SystemExit('The complete three-format candidate set is required')
old=json.loads((previous/'PROVENANCE.json').read_text(encoding='utf-8'));old_file=previous/old['artifacts'][0]['file']
if old['sourceRevision']!=head or old['rid']!=a.rid or old['version']==provenance['version'] or not old['verificationOnlyVersionOverride'] or hashlib.sha256(old_file.read_bytes()).hexdigest()!=old['artifacts'][0]['sha256']:raise SystemExit('An actual same-source distinct compiled upgrade fixture is required')
manifest=json.loads((repository/'scripts/linux-package-images.json').read_text(encoding='utf-8'))
output.mkdir(mode=0o700,parents=True);public=output/'public';public.mkdir();summaries=[]
for case in manifest['targets']:
 if case['id'] not in ('ubuntu24','debian13','fedora44') or not re.fullmatch(r'(ubuntu|debian|registry\.fedoraproject\.org/fedora)@sha256:[0-9a-f]{64}',case['image']):raise SystemExit('Only declared pinned fresh package images are allowed')
 own=output/case['id'];own.mkdir(mode=0o700);context=own/'context';context.mkdir(mode=0o700);result=own/'results';result.mkdir(mode=0o700)
 shutil.copyfile(artifacts[case['family']],context/('candidate.'+case['family']))
 for script in ('verify-package-system.py','capture-linux-app.py','verify-linux-package-case.py','verify-linux-install.py'):shutil.copyfile(repository/'scripts'/script,context/script)
 if case['id']=='debian13':shutil.copyfile(artifacts['tar'],context/'current.tar.gz');shutil.copyfile(old_file,context/'previous.tar.gz')
 expected={**case,'sourceRevision':head,'rid':a.rid};(context/'expected.json').write_text(json.dumps(expected),encoding='utf-8')
 image='codex-switch-package-'+case['id']+':'+head[:12]+'-'+a.rid
 subprocess.run(['docker','build','--file',str(repository/'scripts/linux-package.Dockerfile'),'--build-arg','BASE_IMAGE='+case['image'],'--build-arg','PACKAGE_FAMILY='+case['family'],'--build-arg','TEST_UID='+str(os.getuid()),'--tag',image,str(context)],check=True,timeout=600)
 name='codex-switch-package-'+uuid.uuid4().hex
 try:
  subprocess.run(['docker','run','--init','--name',name,'--label','codex-switch-package-verification='+head,'--network','none','--pids-limit','256','--mount','type=bind,source='+str(result)+',target=/results',image,'python3','/input/verify-linux-package-case.py'],check=True,timeout=450)
 finally:
  found=subprocess.run(['docker','inspect',name],capture_output=True,text=True,timeout=15)
  if found.returncode==0:
   item=json.loads(found.stdout)[0]
   if item['Name']!='/'+name or item['Config']['Labels'].get('codex-switch-package-verification')!=head:raise RuntimeError('Unexpected package container cleanup identity')
   subprocess.run(['docker','rm','--force',item['Id']],check=True,capture_output=True,timeout=15)
  elif 'No such object' not in found.stderr:raise RuntimeError('Package container inspection failed')
 summary=json.loads((result/'system.json').read_text(encoding='utf-8'))
 if summary['sourceRevision']!=head or summary['rid']!=a.rid or summary['failed'] or not summary['globalDotnetAbsent'] or summary['accountsUsed']:raise SystemExit('Fresh installed native program acceptance mismatch')
 target=public/case['id'];subprocess.run([sys.executable,str(repository/'scripts/verify-ui-evidence.py'),str(result/'raw'),str(target),'--os','Linux','--revision',head],check=True,timeout=45)
 shutil.copyfile(result/'system.json',target/'system.json');summaries.append(summary)
 if case['id']=='debian13':
  per_user=json.loads((result/'per-user.json').read_text(encoding='utf-8'))
  if per_user['failed'] or per_user['sourceRevision']!=head or not per_user['upgradeIncluded'] or not per_user['actualInstalledUi']:raise SystemExit('Per-user lifecycle/update acceptance failed')
  subprocess.run([sys.executable,str(repository/'scripts/verify-ui-evidence.py'),str(result/'per-user-raw'),str(public/'per-user'),'--os','Linux','--revision',head],check=True,timeout=45)
  shutil.copyfile(result/'per-user.json',public/'per-user/lifecycle.json')
report={'schema':1,'sourceRevision':head,'rid':a.rid,'version':provenance['version'],'freshTargets':summaries,'perUser':per_user,'packageProvenance':provenance,'scope':'Complete native unsigned preview candidates and actual fresh minimal-container/per-user install/update/removal/bootstrap UI. No accounts, full GNOME/KDE/XWayland/accessibility, authenticated handoff or supported Release promotion.'}
(public/'package-results.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8');print(json.dumps({'rid':a.rid,'freshTargets':len(summaries),'perUserPassed':per_user['passed'],'actualInstalledScreenshots':48,'supportedAccountHandoff':False}))
