"""Center verified native window pixels on a solid background without repainting the UI."""
import argparse
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET
from PIL import Image

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--windows',type=Path,required=True)
p.add_argument('--linux',type=Path,required=True)
p.add_argument('--output',type=Path,required=True)
a=p.parse_args()
root=Path(__file__).resolve().parents[1]
version=ET.parse(root/'Directory.Build.props').findtext('./PropertyGroup/Version')
output=a.output.resolve()
if not output.is_relative_to(root/'docs/media'):
    raise SystemExit('Gallery output must remain under docs/media')
output.mkdir(parents=True,exist_ok=True)
windows=json.loads((a.windows/'evidence.json').read_text(encoding='utf-8'))
linux=json.loads((a.linux/'session.json').read_text(encoding='utf-8'))
if windows['os']!='Windows' or windows['architecture']!='X64' or windows['appVersion']!=version or windows['runtime']!='.NET 10.0.13':
    raise SystemExit('Expected current native Windows evidence')
if linux['failed']!=0 or not linux['ordinaryUi'] or linux['accountsUsed'] or linux['expected']['candidateVersion']!=version:
    raise SystemExit('Expected passing ordinary installed Linux desktop evidence')

def compose(folder,images,name,target):
    entry=next(item for item in images if item['file']==name)
    source=folder/name
    if hashlib.sha256(source.read_bytes()).hexdigest()!=entry['sha256']:
        raise SystemExit('Native source image does not match its receipt')
    with Image.open(source) as image:
        if image.info:raise SystemExit('Only metadata-free source images are allowed')
        rgb=image.convert('RGB')
    if rgb.width>950 or rgb.height>750 or rgb.width<500 or rgb.height<360 or rgb.entropy()<1:
        raise SystemExit('Unexpected native image geometry/content')
    canvas=Image.new('RGB',(1000,800),(24,32,43))
    x,y=(canvas.width-rgb.width)//2,(canvas.height-rgb.height)//2
    canvas.paste(rgb,(x,y))
    region=(x,y,x+rgb.width,y+rgb.height)
    if canvas.crop(region).tobytes()!=rgb.tobytes():
        raise SystemExit('Original window pixels were altered')
    destination=output/target
    canvas.save(destination)
    with Image.open(destination) as checked:
        if checked.info or checked.crop(region).tobytes()!=rgb.tobytes():raise SystemExit('Export changed original pixels or added metadata')
    return {'file':target,'sha256':hashlib.sha256(destination.read_bytes()).hexdigest(),'sourceFile':name,'sourceSha256':entry['sha256'],
            'windowRegion':{'x':x,'y':y,'width':rgb.width,'height':rgb.height},'originalWindowPixelsPreserved':True}

win=compose(a.windows,windows['images'],'en-dark-800x560.png','windows.png')
entry=next(item for item in linux['images'] if item['file']=='ordinary-native-frame.png')
if 'direct-parent native WM frame' not in entry['capture']:
    raise SystemExit('Linux gallery requires the actual verified window-manager frame')
lin=compose(a.linux,linux['images'],'ordinary-native-frame.png','linux.png')
win.update(os='Windows',architecture='X64',runtime=windows['runtime'],revision=windows['revision'],scope='Actual native WPF window with fictional dashboard accounts and unavailable resources; no account or personal-screen capture.')
lin.update(os='Debian13/Xfce Docker userspace',architecture='X64',runtime='.NET 10.0.13',revision=linux['expected']['candidateRevision'],testRevision=linux['expected']['testRevision'],scope='Actual ordinary installed empty-account application with its native xfwm4 titlebar; not a booted VM, GNOME/KDE/Wayland or authenticated handoff claim.')
receipt={'schema':1,'applicationVersion':version,'canvas':{'width':1000,'height':800,'background':'#18202b','scaling':'none'},'metadataRemoved':True,
         'images':[win,lin],'macbook':{'pending':True,'slot':'macbook.png','requirement':'Maintainer real-device capture with original titlebar and privacy/provenance review; no substitute image.'}}
(output/'gallery.json').write_text(json.dumps(receipt,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'nativeWindowsAndLinuxImages':2,'originalWindowPixelsPreserved':True,'metadataRemoved':True,'macbookScreenshotPending':True}))
