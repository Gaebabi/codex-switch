"""Build complete unsigned Linux preview candidates, inventory, checksums and source provenance."""
import argparse
import datetime
import gzip
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shlex
import shutil
import stat
import subprocess
import tarfile
import xml.etree.ElementTree as ET

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--app',type=Path,required=True)
p.add_argument('--rid',choices=['linux-x64','linux-arm64'],required=True)
p.add_argument('--output',type=Path,required=True)
a=p.parse_args()
repository=Path(__file__).resolve().parents[1];app=a.app.resolve();output=a.output.resolve()
if os.name!='posix' or {'x86_64':'linux-x64','aarch64':'linux-arm64'}.get(platform.machine())!=a.rid:raise SystemExit('Build packages on the declared native Linux architecture')
if output.exists() or not output.is_relative_to(repository/'artifacts') or output==repository/'artifacts':raise SystemExit('Use a fresh dedicated ignored output directory')
revision=subprocess.run(['git','-C',str(repository),'rev-parse','HEAD'],capture_output=True,text=True,check=True).stdout.strip()
if not re.fullmatch('[0-9a-f]{40}',revision):raise SystemExit('Exact source revision is required')
if subprocess.run(['git','-C',str(repository),'status','--porcelain'],capture_output=True,text=True,check=True).stdout:raise SystemExit('Package provenance requires a clean committed source checkout')
epoch=int(subprocess.run(['git','-C',str(repository),'show','-s','--format=%ct','HEAD'],capture_output=True,text=True,check=True).stdout)
os.environ['SOURCE_DATE_EPOCH']=str(epoch)
info=json.loads(subprocess.run([str(app/'CodexSwitch'),'--package-info'],capture_output=True,text=True,check=True,timeout=15).stdout)
version=info['version'];expected='Arm64' if a.rid=='linux-arm64' else 'X64'
if not re.fullmatch(r'\d+\.\d+\.\d+',version) or info['architecture']!=expected or info['runtime']!='.NET 10.0.13':raise SystemExit('Actual application version/architecture/runtime mismatch')
output.mkdir(parents=True);payload=output/'payload';payload.mkdir()
for file in sorted(app.rglob('*')):
 if file.is_symlink() or not (file.is_dir() or file.is_file()):raise SystemExit('Published application may contain only regular package files/directories')
 if file.is_file():
  target=payload/file.relative_to(app);target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(file,target)
  target.chmod(0o755 if file.stat().st_mode & 0o111 else 0o644)
license_root=payload/'licenses';license_root.mkdir(exist_ok=True)
for file in (repository/'licenses').iterdir():
 if file.is_file():shutil.copyfile(file,license_root/file.name)
shutil.copyfile(repository/'LICENSE',license_root/'CodexSwitch.MIT.txt')
relink=license_root/'libsecret-source';relink.mkdir()
for source,name in [('scripts/build-linux-secret.py','build-linux-secret.py'),('src/platforms/security/native/libsecret-compat.patch','libsecret-compat.patch'),('src/platforms/security/native/libsecret.json','libsecret.json'),('src/platforms/security/native/README.md','README.md')]:shutil.copyfile(repository/source,relink/name)
assets=json.loads((repository/'src/linux/obj/project.assets.json').read_text(encoding='utf-8'))
packages=[];libraries=dict(assets['libraries'])
for name,value in json.loads((app/'CodexSwitch.deps.json').read_text(encoding='utf-8'))['libraries'].items():
 if value['type']=='package' and name not in libraries:libraries[name]=value
for name,value in sorted(libraries.items()):
 if value['type']!='package':continue
 package_id,package_version=name.rsplit('/',1);relative=value.get('path',package_id.lower()+'/'+package_version);package_root=next((Path(folder)/relative for folder in assets['packageFolders'] if (Path(folder)/relative).is_dir()),None)
 if package_root is None:raise SystemExit('Restored package license inputs are missing')
 license_expression='NOASSERTION'
 for nuspec in package_root.glob('*.nuspec'):
  tree=ET.parse(nuspec);node=tree.find('.//{*}license')
  if node is not None and node.attrib.get('type')=='expression':license_expression=node.text or 'NOASSERTION'
 notices=[]
 for file in package_root.iterdir():
  if file.is_file() and re.match(r'(?i)^(license|third[ -]?party|notice)',file.name):
   target=license_root/'nuget'/package_id/package_version/file.name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(file,target);notices.append(target.relative_to(payload).as_posix())
 packages.append({'name':package_id,'version':package_version,'purl':'pkg:nuget/'+package_id+'@'+package_version,'license':license_expression,'notices':notices})
native=json.loads((repository/'src/platforms/security/native/libsecret.json').read_text(encoding='utf-8'))
readme=f'''Codex Switch {version} Linux preview candidate ({a.rid})

This is a complete self-contained source preview, not a supported account-switching release.
Ordinary Linux account/handoff actions remain gated pending authenticated acceptance.
No .NET SDK/runtime, Python or source build is needed to run/install this payload.
Distribution native dependencies and an X11/XWayland display are still required.

Run: ./CodexSwitch
Install per user (without sudo): ./CodexSwitch --install-package
Uninstall per-user package files: ~/.local/share/codex-switch/current/CodexSwitch --uninstall-package
Custom XDG_DATA_HOME is respected. Existing vault/Codex-home/keyring data is never migrated/deleted.
System .deb/.rpm packages use the distribution package manager instead.

Source: https://github.com/Gaebabi/codex-switch/tree/{revision}
Guide: https://github.com/Gaebabi/codex-switch/blob/{revision}/docs/development/LINUX.md
Checksums detect changed bytes; these PR/CI candidates are unsigned and are not publisher authentication.
'''
(payload/'RUNNING.txt').write_text(readme,encoding='utf-8')
def digest(file):return hashlib.file_digest(file.open('rb'),'sha256').hexdigest()
files=[]
for file in sorted(payload.rglob('*')):
 if file.is_file():
  mode=0o755 if file.stat().st_mode & 0o111 else 0o644;file.chmod(mode)
  files.append({'Path':file.relative_to(payload).as_posix(),'Sha256':digest(file),'Mode':mode})
manifest={'Schema':1,'Version':version,'Rid':a.rid,'Revision':revision,'Files':files}
(payload/'package-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
spdx={'spdxVersion':'SPDX-2.3','dataLicense':'CC0-1.0','SPDXID':'SPDXRef-DOCUMENT','name':'CodexSwitch-'+version+'-'+a.rid,'documentNamespace':'https://github.com/Gaebabi/codex-switch/sbom/'+revision+'/'+a.rid+'/'+version,
 'creationInfo':{'created':datetime.datetime.fromtimestamp(epoch,datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ'),'creators':['Tool: codex-switch-package']},
 'packages':[{'SPDXID':'SPDXRef-NuGet-'+str(index),'name':item['name'],'versionInfo':item['version'],'downloadLocation':'NOASSERTION','filesAnalyzed':False,'licenseConcluded':'NOASSERTION','licenseDeclared':item['license'],'copyrightText':'NOASSERTION','externalRefs':[{'referenceCategory':'PACKAGE-MANAGER','referenceType':'purl','referenceLocator':item['purl']}]} for index,item in enumerate(packages)],
 'files':[{'SPDXID':'SPDXRef-File-'+str(index),'fileName':'./'+entry['Path'],'checksums':[{'algorithm':'SHA256','checksumValue':entry['Sha256']}],'licenseConcluded':'NOASSERTION','copyrightText':'NOASSERTION'} for index,entry in enumerate(files)],
 'annotations':[{'annotationType':'OTHER','annotator':'Tool: codex-switch-package','annotationDate':datetime.datetime.fromtimestamp(epoch,datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ'),'comment':'Application/native/runtime bytes are inventoried. Bundled libsecret source/patch/relinking and NuGet license notices accompany the payload. No official Codex client or account data is redistributed.'}]}
spdx['packages'].extend([
 {'SPDXID':'SPDXRef-Application','name':'Codex Switch','versionInfo':version,'downloadLocation':'https://github.com/Gaebabi/codex-switch/tree/'+revision,'filesAnalyzed':False,'licenseConcluded':'MIT','licenseDeclared':'MIT','copyrightText':'NOASSERTION'},
 {'SPDXID':'SPDXRef-libsecret','name':'libsecret (private compatible build)','versionInfo':'0.21.8.2','downloadLocation':'https://github.com/Gaebabi/codex-switch/tree/'+revision+'/src/platforms/security/native','filesAnalyzed':False,'licenseConcluded':'LGPL-2.1-or-later','licenseDeclared':'LGPL-2.1-or-later','copyrightText':'NOASSERTION'}])
spdx['relationships']=[{'spdxElementId':'SPDXRef-DOCUMENT','relationshipType':'DESCRIBES','relatedSpdxElement':'SPDXRef-Application'},*({'spdxElementId':'SPDXRef-Application','relationshipType':'DEPENDS_ON','relatedSpdxElement':item['SPDXID']} for item in spdx['packages'] if item['SPDXID']!='SPDXRef-Application')]
(output/'SBOM.spdx.json').write_text(json.dumps(spdx,indent=2)+'\n',encoding='utf-8')
(output/'DEPENDENCIES.json').write_text(json.dumps({'schema':1,'rid':a.rid,'nuget':packages,'nativeSecret':native,'systemDependencies':'Distribution C/C++/ICU/OpenSSL/Kerberos/zlib/GLib/libgcrypt, X11/XCB/ICE/SM/fontconfig, read-only SQLite and a configured Secret Service for future account capabilities. Full desktop/account acceptance remains separate.'},indent=2)+'\n',encoding='utf-8')
stem='CodexSwitch-v'+version+'-preview-'+a.rid
with (output/(stem+'.tar.gz')).open('wb') as raw,gzip.GzipFile(filename='',mode='wb',fileobj=raw,mtime=epoch) as compressed,tarfile.open(fileobj=compressed,mode='w',format=tarfile.PAX_FORMAT) as archive:
 for file in sorted(payload.rglob('*')):
  entry=archive.gettarinfo(str(file),stem+'/'+file.relative_to(payload).as_posix());entry.uid=entry.gid=0;entry.uname=entry.gname='';entry.mtime=epoch
  if file.is_file():
   with file.open('rb') as source:archive.addfile(entry,source)
  else:archive.addfile(entry)
stage=output/'system-root';destination=stage/'opt/codex-switch';shutil.copytree(payload,destination)
desktop=stage/'usr/share/applications/codex-switch.desktop';desktop.parent.mkdir(parents=True)
desktop.write_text('[Desktop Entry]\nType=Application\nName=Codex Switch\nComment=Linux source preview; account handoff is under native acceptance\nExec=/opt/codex-switch/CodexSwitch\nIcon=codex-switch\nTerminal=false\nCategories=Utility;Development;\nStartupNotify=true\n',encoding='utf-8')
icon=stage/'usr/share/icons/hicolor/1024x1024/apps/codex-switch.png';icon.parent.mkdir(parents=True);shutil.copyfile(payload/'icons/codex-switch.png',icon)
launcher=stage/'usr/bin/codex-switch';launcher.parent.mkdir(parents=True);launcher.write_text('#!/bin/sh\nexec /opt/codex-switch/CodexSwitch "$@"\n',encoding='utf-8');launcher.chmod(0o755)
for directory in [stage,*[file for file in stage.rglob('*') if file.is_dir()]]:directory.chmod(0o755)
deb=output/(stem+'.deb');control=stage/'DEBIAN';control.mkdir(mode=0o755)
(control/'control').write_text('Package: codex-switch\nVersion: '+version+'~preview.1\nSection: utils\nPriority: optional\nArchitecture: '+('arm64' if a.rid=='linux-arm64' else 'amd64')+'\nMaintainer: Codex Switch contributors <45709904+Gaebabi@users.noreply.github.com>\nHomepage: https://github.com/Gaebabi/codex-switch\nDepends: ca-certificates, tzdata, libc6 (>= 2.39), libgcc-s1, libstdc++6, zlib1g, libgssapi-krb5-2, libssl3t64, libicu74 | libicu76 | libicu78, libglib2.0-0t64 (>= 2.80), libgcrypt20, libsqlite3-0, libx11-6, libxcb1, libice6, libsm6, libfontconfig1\nDescription: Codex Switch Linux source preview\n Complete self-contained preview. Account/handoff actions remain gated.\n No account data, service configuration or credential migration is performed.\n',encoding='utf-8')
subprocess.run(['dpkg-deb','--root-owner-group','--build',str(stage),str(deb)],check=True,timeout=120)
shutil.rmtree(control)
rpmroot=output/'rpmbuild';(rpmroot/'SPECS').mkdir(parents=True)
spec=rpmroot/'SPECS/codex-switch.spec'
spec.write_text(f'''Name: codex-switch
Version: {version}
Release: 0.1.preview
Summary: Codex Switch Linux source preview
License: MIT AND LGPL-2.1-or-later
URL: https://github.com/Gaebabi/codex-switch
BuildArch: {'aarch64' if a.rid=='linux-arm64' else 'x86_64'}
AutoReqProv: no
Requires: glibc >= 2.39, libgcc, libstdc++, zlib, krb5-libs, openssl-libs, libicu, glib2, libgcrypt, sqlite-libs, libX11, libxcb, libICE, libSM, fontconfig, ca-certificates, tzdata
%description
Complete self-contained preview. Account/handoff actions remain gated.
No account data, service configuration or credential migration is performed.
%install
mkdir -p "%{{buildroot}}"
cp -a {shlex.quote(str(stage) + '/.').replace('%','%%')} "%{{buildroot}}/"
%files
/opt/codex-switch
/usr/bin/codex-switch
/usr/share/applications/codex-switch.desktop
/usr/share/icons/hicolor/1024x1024/apps/codex-switch.png
''',encoding='utf-8')
subprocess.run(['rpmbuild','--define','_topdir '+str(rpmroot),'--define','_build_id_links none','--define','_buildhost reproducible.invalid','--define','__os_install_post %{nil}','-bb',str(spec)],check=True,timeout=180)
rpms=list((rpmroot/'RPMS').rglob('*.rpm'))
if len(rpms)!=1:raise SystemExit('Expected exactly one architecture-specific RPM')
shutil.copyfile(rpms[0],output/(stem+'.rpm'))
artifacts=[output/(stem+suffix) for suffix in ('.tar.gz','.deb','.rpm')]
provenance={'schema':1,'sourceRevision':revision,'version':version,'rid':a.rid,'runtime':info['runtime'],'nativeArchitecture':info['architecture'],'unsignedPreview':True,'supportedAccountHandoff':False,'payloadFiles':len(files),'artifacts':[{'file':file.name,'sha256':digest(file),'bytes':file.stat().st_size} for file in artifacts],'scope':'Native source-built unsigned candidate packages only. Checksums/inventory do not establish publisher authentication, actual account handoff or clean-desktop support.'}
(output/'PROVENANCE.json').write_text(json.dumps(provenance,indent=2)+'\n',encoding='utf-8')
(output/'SHA256SUMS').write_text(''.join(digest(file)+'  '+file.name+'\n' for file in [*artifacts,output/'SBOM.spdx.json',output/'DEPENDENCIES.json',output/'PROVENANCE.json']),encoding='utf-8')
print(json.dumps(provenance))
