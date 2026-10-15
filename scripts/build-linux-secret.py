"""Build the app-private encrypted Secret Service library from pinned, reviewable sources."""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import platform
import shlex
import shutil
import subprocess
import sys
import tarfile
import urllib.request

p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
if sys.platform!='linux' or platform.machine() not in ('x86_64','aarch64'):raise SystemExit('Build on a declared native Linux x64/ARM64 environment')
repository=Path(__file__).resolve().parents[1];output=a.output.resolve();artifacts=(repository/'artifacts').resolve()
if not output.is_relative_to(artifacts) or output==artifacts:raise SystemExit('Native output must stay in a dedicated ignored artifacts directory')
source_directory=repository/'src/platforms/security/native';manifest=json.loads((source_directory/'libsecret.json').read_text(encoding='utf-8'));patch=source_directory/'libsecret-compat.patch'
expected_url='https://download.gnome.org/sources/libsecret/0.21/libsecret-0.21.8.2.tar.xz'
if manifest['url']!=expected_url or manifest['version']!='0.21.8.2' or manifest['library']!='libcodex-switch-secret-1.so.0':raise SystemExit('Unexpected reviewed native source contract')
output.mkdir(parents=True,exist_ok=True);archive=output/'libsecret.tar.xz'
receipt_file=output/'native-receipt.json'
if receipt_file.exists():
 existing=json.loads(receipt_file.read_text(encoding='utf-8'));library=output/manifest['library']
 if existing.get('upstreamSha256')!=manifest['sha256'] or existing.get('patchSha256')!=hashlib.sha256(patch.read_bytes()).hexdigest() or existing.get('architecture')!=platform.machine() or not library.is_file() or hashlib.sha256(library.read_bytes()).hexdigest()!=existing.get('sha256'):
  raise SystemExit('Existing native output differs from reviewed inputs; choose a fresh --output and matching CodexSecretDir')
 print(json.dumps(existing));raise SystemExit(0)
if not archive.exists():
 with urllib.request.urlopen(manifest['url'],timeout=40) as response:
  if response.url!=manifest['url']:raise SystemExit('Unexpected native-source redirect')
  data=response.read(2_000_001)
 if len(data)>2_000_000 or hashlib.sha256(data).hexdigest()!=manifest['sha256']:raise SystemExit('Native source size/checksum mismatch')
 archive.write_bytes(data)
if hashlib.sha256(archive.read_bytes()).hexdigest()!=manifest['sha256']:raise SystemExit('Cached native source checksum mismatch')
build=output/'build';source=output/'source'
if source.exists() or build.exists():raise SystemExit('Use a fresh native build output directory')
source.mkdir()
with tarfile.open(archive,mode='r:xz') as bundle:
 members=bundle.getmembers()
 if len(members)>1500 or sum(m.size for m in members)>8_000_000:raise SystemExit('Native source archive limits exceeded')
 for member in members:
  target=(source/member.name).resolve()
  if not target.is_relative_to(source) or Path(member.name).parts[0]!='libsecret-0.21.8.2' or not(member.isfile() or member.isdir()):raise SystemExit('Unsafe native source archive')
 bundle.extractall(source,filter='data')
tree=source/'libsecret-0.21.8.2'
subprocess.run(['patch','--batch','--forward','--fuzz=0','-p1','-i',str(patch)],cwd=tree,check=True)
subprocess.run(['meson','setup',str(build),str(tree),'-Dcrypto=libgcrypt','-Dgtk_doc=false','-Dmanpage=false','-Dintrospection=false','-Dvapi=false','-Dtest_setup=disabled','-Dbash_completion=disabled','--buildtype=release'],check=True)
subprocess.run(['meson','compile','-C',str(build),'codex-switch-secret-1','secret-1'],check=True)
flags=shlex.split(subprocess.run(['pkg-config','--cflags','--libs','glib-2.0','gio-2.0','libgcrypt'],capture_output=True,text=True,check=True).stdout)
test=output/'native-encoding'
subprocess.run(['cc','-DSECRET_COMPILATION',str(repository/'tests/linux-vault/native-encoding.c'),'-I'+str(tree),'-I'+str(build),'-I'+str(build/'libsecret'),'-I'+str(build/'egg'),str(build/'libsecret/libsecret-1.a'),str(build/'egg/libegg.a'),*flags,'-o',str(test)],check=True)
subprocess.run([str(test)],check=True,timeout=40)
binary=build/'libsecret/libcodex-switch-secret-1.so.0.0.0';library=output/manifest['library']
shutil.copyfile(binary,library);library.chmod(0o755)
native=ctypes.CDLL(str(library));native.secret_codex_compat_abi.restype=ctypes.c_uint
if native.secret_codex_compat_abi()!=manifest['abi']:raise SystemExit('Native compatibility ABI mismatch')
exports=subprocess.run(['nm','-D','--defined-only',str(library)],capture_output=True,text=True,check=True).stdout
if any(name not in exports for name in ('secret_codex_service_open_sync','secret_service_get_sync','secret_item_create_sync','secret_value_new')):raise SystemExit('Incomplete native compatibility exports')
shutil.copyfile(tree/'COPYING',output/'libsecret.LGPL-2.1.txt')
receipt={'schema':1,'upstreamVersion':manifest['version'],'upstreamSha256':manifest['sha256'],'patchSha256':hashlib.sha256(patch.read_bytes()).hexdigest(),'library':manifest['library'],'abi':manifest['abi'],'architecture':platform.machine(),'sha256':hashlib.sha256(library.read_bytes()).hexdigest(),'scope':'App-private LGPL library, encrypted transport only; no system library/keyring replacement or account data.'}
(output/'native-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n',encoding='utf-8')
print(json.dumps(receipt))
