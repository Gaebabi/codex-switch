"""Capture the actual source app on an owned Xvfb display and non-activating private bus/HOME."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
from xml.sax.saxutils import escape

p=argparse.ArgumentParser(description=__doc__);p.add_argument('--app',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
app=a.app.resolve();output=a.output.resolve()
if not app.is_file() or output.exists():raise SystemExit('An actual published app and fresh own-window output are required')
root=Path(tempfile.mkdtemp(prefix='codex-linux-window-test-')).resolve()
try:
    for folder in ('home','share','config','cache','runtime','bus'):(root/folder).mkdir(mode=0o700)
    config=root/'bus.conf'
    config.write_text('<busconfig><type>session</type><listen>unix:tmpdir='+escape(str(root/'bus'))+'</listen><auth>EXTERNAL</auth><policy context="default"><allow user="'+str(os.geteuid())+'"/><allow send_destination="*"/><allow receive_sender="*"/><allow own="*"/></policy></busconfig>',encoding='utf-8');config.chmod(0o600)
    env=dict(os.environ,HOME=str(root/'home'),XDG_DATA_HOME=str(root/'share'),XDG_CONFIG_HOME=str(root/'config'),XDG_CACHE_HOME=str(root/'cache'),XDG_RUNTIME_DIR=str(root/'runtime'),CODEX_HOME=str(root/'absent-codex'),XDG_SESSION_TYPE='x11')
    for key in ('DISPLAY','XAUTHORITY','WAYLAND_DISPLAY','DBUS_SESSION_BUS_ADDRESS','G_DBUS_DEBUG','LIBSECRET_DEBUG'):env.pop(key,None)
    subprocess.run(['dbus-run-session','--config-file',str(config),'--','xvfb-run','-a','-s','-screen 0 1920x1080x24 -nolisten tcp',str(app),'--verify-ui',str(output)],env=env,check=True,timeout=90)
finally:
    if root.parent!=Path(tempfile.gettempdir()).resolve() or not root.name.startswith('codex-linux-window-test-') or root.is_symlink() or root.stat().st_uid!=os.geteuid():raise RuntimeError('Unexpected window diagnostic cleanup root')
    shutil.rmtree(root)
