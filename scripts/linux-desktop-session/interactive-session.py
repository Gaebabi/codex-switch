"""Owner-operated Linux acceptance desktop. No automatic account login or capture."""
import json
import os
from pathlib import Path
import secrets
import signal
import socket
import subprocess
import sys
import time

root = Path('/home/verifier/interactive')


def run(command, **kwargs):
    return subprocess.run(command, check=True, timeout=kwargs.pop('timeout', 15), **kwargs)


def wait_for(probe, timeout=40):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if probe():
            return
        time.sleep(0.2)
    raise TimeoutError('The private desktop service did not become ready')


if os.geteuid() != 1000 or os.environ.get('CODEX_SWITCH_INTERACTIVE') != 'owner-operated':
    raise SystemExit('Only an explicitly owner-operated normal-user desktop is allowed')
os.umask(0o077)
if len(sys.argv) == 1:
    root.mkdir(mode=0o700, exist_ok=True)
    if root.is_symlink() or root.stat().st_uid != os.geteuid() or root.stat().st_mode & 0o077:
        raise SystemExit('Refuse a nonprivate session root')
    for folder in ('home', 'runtime', 'control', 'bus', 'home/.codex', 'home/project'):
        (root / folder).mkdir(mode=0o700, exist_ok=True)
    policy = root / 'home/.codex/config.toml'
    if not policy.exists():
        policy.write_text('cli_auth_credentials_store = "file"\n', encoding='utf-8')
    bus = root / 'bus.conf'
    bus.write_text('<busconfig><type>session</type><listen>unix:tmpdir=' + str(root / 'bus') + '</listen><auth>EXTERNAL</auth><servicedir>/usr/share/dbus-1/services</servicedir><policy context="default"><allow user="1000"/><allow send_destination="*"/><allow receive_sender="*"/><allow own="*"/></policy></busconfig>', encoding='utf-8')
    env = {'PATH': '/usr/bin:/bin', 'LANG': 'C.UTF-8', 'HOME': str(root / 'home'), 'DISPLAY': ':0',
           'XDG_RUNTIME_DIR': str(root / 'runtime'), 'GNOME_KEYRING_CONTROL': str(root / 'control'),
           'CODEX_HOME': str(root / 'home/.codex'), 'XDG_SESSION_TYPE': 'x11',
           'XDG_CURRENT_DESKTOP': 'XFCE', 'XDG_SESSION_DESKTOP': 'xfce', 'GDK_BACKEND': 'x11',
           'BROWSER': '/usr/bin/firefox-esr', 'CODEX_SWITCH_INTERACTIVE': 'owner-operated'}
    os.execvpe('dbus-run-session', ['dbus-run-session', '--config-file', str(bus), '--', sys.executable, __file__, '--inside'], env)

private = (root / 'services-private.log').open('ab', buffering=0)
children = []


def start(command):
    process = subprocess.Popen(command, stdin=subprocess.DEVNULL, stdout=private, stderr=private)
    children.append(process)
    return process


def listening(port):
    try:
        with socket.create_connection(('127.0.0.1', port), timeout=0.5):
            return True
    except OSError:
        return False


def stop(_signum, _frame):
    raise SystemExit(0)


signal.signal(signal.SIGTERM, stop)
signal.signal(signal.SIGINT, stop)
try:
    start(['Xvfb', ':0', '-screen', '0', '1600x1000x24', '-nolisten', 'tcp'])
    wait_for(lambda: subprocess.run(['xdpyinfo'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode == 0)
    run(['dbus-update-activation-environment', 'DISPLAY', 'XDG_CURRENT_DESKTOP', 'XDG_SESSION_TYPE', 'HOME', 'GNOME_KEYRING_CONTROL'], stdout=private, stderr=private)
    start(['xfce4-session'])
    wait_for(lambda: b'window id #' in run(['xprop', '-root', '_NET_SUPPORTING_WM_CHECK'], capture_output=True).stdout)
    wait_for(lambda: subprocess.run(['pgrep', '-u', '1000', '-x', 'xfce4-panel'], stdout=subprocess.DEVNULL).returncode == 0)
    run(['xdg-settings', 'set', 'default-web-browser', 'firefox-esr.desktop'], stdout=private, stderr=private)
    run(['xdg-mime', 'default', 'firefox-esr.desktop', 'x-scheme-handler/https', 'x-scheme-handler/http'], stdout=private, stderr=private)
    password = ''.join(secrets.choice('abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789') for _ in range(8))
    password_file = root / 'vnc-password'
    run(['x11vnc', '-storepasswd', password, str(password_file)], stdout=private, stderr=private)
    password_file.chmod(0o600)
    start(['x11vnc', '-display', ':0', '-localhost', '-rfbport', '5900', '-rfbauth', str(password_file), '-forever', '-shared', '-nosel', '-noxdamage', '-quiet'])
    wait_for(lambda: listening(5900))
    start(['websockify', '--web', '/usr/share/novnc', '0.0.0.0:6080', '127.0.0.1:5900'])
    wait_for(lambda: listening(6080))
    # This file is private session access material; never export it as evidence.
    (root / 'viewer-private.json').write_text(json.dumps({'password': password, 'resolution': [1600, 1000], 'authenticationCaptured': False}), encoding='utf-8')
    keyring = start([sys.executable, '/work/prepare-keyring.py'])
    application = None
    (root / 'ready').write_text('ready\n', encoding='utf-8')
    while True:
        if any(child.poll() is not None for child in children[:4]):
            raise RuntimeError('A private desktop service exited')
        if application is None and keyring.poll() is not None:
            # A cancelled prompt still lets the owner inspect the ordinary
            # application's real capability error and retry after native setup.
            application = start(['/opt/codex-switch/CodexSwitch'])
        time.sleep(1)
finally:
    (root / 'ready').unlink(missing_ok=True)
    for child in reversed(children):
        if child.poll() is None:
            child.terminate()
    for child in reversed(children):
        try:
            child.wait(timeout=10)
        except subprocess.TimeoutExpired:
            child.kill()
            child.wait(timeout=5)
    private.close()
