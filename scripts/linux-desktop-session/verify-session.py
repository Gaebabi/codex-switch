"""Exercise actual installed UI, keyring and sandboxed client in an owned Xfce session."""
import hashlib
import json
import os
import errno
import pty
from pathlib import Path
import platform
import re
import secrets
import selectors
import select
import shutil
import subprocess
import sys
import tempfile
import time
from xml.sax.saxutils import escape
from PIL import Image

expected = json.loads(Path('/work/expected.json').read_text())
results = Path('/results')

def run(command, **kwargs):
    try:
        return subprocess.run(command, check=True, timeout=kwargs.pop('timeout', 20), **kwargs)
    except subprocess.CalledProcessError as error:
        if error.stderr:
            with (results / 'failed-command-private.log').open('ab') as diagnostic:
                diagnostic.write(error.stderr if isinstance(error.stderr, bytes) else error.stderr.encode('utf-8'))
        raise

def wait_for(probe, timeout=20):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        value = probe()
        if value:
            return value
        time.sleep(0.1)
    raise TimeoutError('The owned desktop state did not arrive')

if os.geteuid() != 1000 or platform.machine() != 'x86_64' or os.environ.get('CODEX_SWITCH_DISPOSABLE_RUNNER') != 'true':
    raise SystemExit('Only the declared normal-user disposable native x64 container is allowed')
if len(sys.argv) == 1:
    root = Path(tempfile.mkdtemp(prefix='codex-linux-keyring-test-', dir=Path.home()))
    for folder in ('home', 'share', 'config', 'cache', 'runtime', 'bus', 'control'):
        (root / folder).mkdir(mode=0o700)
    config = root / 'bus.conf'
    config.write_text('<busconfig><type>session</type><listen>unix:tmpdir=' + escape(str(root / 'bus')) + '</listen><auth>EXTERNAL</auth><servicedir>/usr/share/dbus-1/services</servicedir><policy context="default"><allow user="1000"/><allow send_destination="*"/><allow receive_sender="*"/><allow own="*"/></policy></busconfig>')
    config.chmod(0o600)
    env = {'PATH': '/usr/bin:/bin', 'LANG': 'C.UTF-8', 'HOME': str(root / 'home'), 'XDG_DATA_HOME': str(root / 'share'), 'XDG_CONFIG_HOME': str(root / 'config'), 'XDG_CACHE_HOME': str(root / 'cache'), 'XDG_RUNTIME_DIR': str(root / 'runtime'), 'GNOME_KEYRING_CONTROL': str(root / 'control'), 'CODEX_HOME': str(root / 'absent-codex'), 'CODEX_SWITCH_LINUX_TEST_ROOT': str(root), 'CODEX_SWITCH_DISPOSABLE_RUNNER': 'true', 'XDG_SESSION_TYPE': 'x11', 'XDG_CURRENT_DESKTOP': 'XFCE', 'XDG_SESSION_DESKTOP': 'xfce', 'GDK_BACKEND': 'x11'}
    run(['dbus-run-session', '--config-file', str(config), '--', 'xvfb-run', '-a', '-s', '-screen 0 1600x1000x24 -nolisten tcp', sys.executable, __file__, '--inside'], env=env, timeout=450)
    raise SystemExit(0)

root = Path(os.environ['CODEX_SWITCH_LINUX_TEST_ROOT'])
if not root.name.startswith('codex-linux-keyring-test-') or root.is_symlink() or root.stat().st_uid != os.geteuid() or not os.environ.get('DBUS_SESSION_BUS_ADDRESS', '').startswith('unix:path=' + str(root / 'bus') + '/'):
    raise SystemExit('Only the owned private desktop session is allowed')
checks = []
images = []
phases = []
private_log = (results / 'native-private.log').open('wb')

def check(name, value):
    if not value:
        raise RuntimeError('Desktop session contract failed: ' + name)
    checks.append(name)
    print(json.dumps({'check': name, 'passed': True}), flush=True)

def gdbus(destination, path, method, *arguments):
    return run(['gdbus', 'call', '--session', '--dest', destination, '--object-path', path, '--method', method, *arguments], capture_output=True, text=True).stdout.strip()

def phase(name, password=None):
    value = json.loads(run(['/contracts/vault/CodexSwitch.LinuxVault.Tests', name], input=password, capture_output=True, timeout=90).stdout)
    check('Actual GNOME Keyring phase: ' + name, value['failed'] == 0 and value['architecture'] == 'X64')
    phases.append(value)

def owner(daemon):
    bus = gdbus('org.freedesktop.DBus', '/org/freedesktop/DBus', 'org.freedesktop.DBus.GetNameOwner', 'org.freedesktop.secrets')
    unique = re.fullmatch(r"\('(:[0-9.]+)',\)", bus)
    if not unique:
        raise RuntimeError('Expected private Secret Service owner')
    pid = gdbus('org.freedesktop.DBus', '/org/freedesktop/DBus', 'org.freedesktop.DBus.GetConnectionUnixProcessID', unique[1])
    if pid != '(uint32 ' + str(daemon.pid) + ',)':
        raise RuntimeError('Secret Service was not the original owned provider')

password = secrets.token_urlsafe(32).encode()

def keyring():
    daemon = subprocess.Popen(['gnome-keyring-daemon', '--foreground', '--unlock', '--components=secrets', '--control-directory', str(root / 'control')], stdin=subprocess.PIPE, stdout=private_log, stderr=private_log)
    daemon.stdin.write(password)
    daemon.stdin.close()
    wait_for(lambda: 'true' in gdbus('org.freedesktop.DBus', '/org/freedesktop/DBus', 'org.freedesktop.DBus.NameHasOwner', 'org.freedesktop.secrets'))
    owner(daemon)
    return daemon

def windows(pid):
    value = subprocess.run(['xdotool', 'search', '--onlyvisible', '--pid', str(pid)], capture_output=True, text=True, timeout=5)
    return value.stdout.splitlines() if value.returncode == 0 else []

def capture(window, pid, name):
    check('Capture is bound to original application PID: ' + name, run(['xdotool', 'getwindowpid', window], capture_output=True, text=True).stdout.strip() == str(pid))
    raw = root / 'capture.xwd'
    file = results / (name + '.png')
    run(['xwd', '-silent', '-id', window, '-out', str(raw)])
    run(['convert', str(raw), '-strip', str(file)])
    with Image.open(file) as source:
        rgb = source.convert('RGB')
        if source.info or rgb.width < 400 or rgb.height < 280 or rgb.entropy() < 1:
            raise RuntimeError('Invalid/blank/metadata-bearing actual UI capture')
        image = Image.new('RGB', rgb.size)
        image.paste(rgb)
        image.save(file)
        images.append({'file': file.name, 'width': image.width, 'height': image.height, 'sha256': hashlib.sha256(file.read_bytes()).hexdigest(), 'capture': 'Actual ordinary installed application own XID; no --verify-ui or synthetic account injection'})


def wait_for_initialized_ui(window):
    def observed():
        native = root / 'initialization-private.xwd'
        png = root / 'initialization-private.png'
        run(['xwd', '-silent', '-id', window, '-out', str(native)], stdout=private_log, stderr=private_log)
        run(['convert', str(native), str(png)], stdout=private_log, stderr=private_log)
        with Image.open(png) as frame:
            frame = frame.convert('RGB')
            background = frame.getpixel((5, frame.height - 65))
            def ink(box):
                return max(max(abs(pixel[channel] - background[channel]) for channel in range(3)) for pixel in frame.crop(box).getdata())
            return ink((9, frame.height - 24, 140, frame.height - 5)) > 20 and ink((frame.width - 149, frame.height - 24, frame.width - 9, frame.height - 5)) > 30
    wait_for(observed, 40)

daemon = keyring()
desktop = subprocess.Popen(['xfce4-session'], stdout=private_log, stderr=private_log)
try:
    wm = wait_for(lambda: subprocess.run(['xprop', '-root', '_NET_SUPPORTING_WM_CHECK'], capture_output=True, text=True, timeout=5).stdout if 'window id #' in subprocess.run(['xprop', '-root', '_NET_SUPPORTING_WM_CHECK'], capture_output=True, text=True, timeout=5).stdout else None)
    wm_id = wm.rsplit(' ', 1)[-1].strip()
    wm_name = run(['xprop', '-id', wm_id, '_NET_WM_NAME'], capture_output=True, text=True).stdout
    check('Real Xfce session and xfwm4 window manager are running', desktop.poll() is None and 'Xfwm4' in wm_name)
    panel = wait_for(lambda: subprocess.run(['pgrep', '-u', str(os.getuid()), '-x', 'xfce4-panel'], capture_output=True, timeout=5).stdout.strip())
    check('Real desktop panel is running', bool(panel))
    check('No global .NET host/SDK/runtime is installed', shutil.which('dotnet') is None and not Path('/usr/share/dotnet').exists())
    markers = {root / 'absent-codex/auth.json': b'Owned synthetic invalid authentication marker\x00\xff', root / 'data/preserved/accounts.vault': b'Owned synthetic preserved opaque vault marker\x00\xff'}
    for path, value in markers.items():
        path.parent.mkdir(parents=True, mode=0o700, exist_ok=True)
        path.write_bytes(value)
        path.chmod(0o600)
    phase('roundtrip')
    phase('concurrent')
    alias = gdbus('org.freedesktop.secrets', '/org/freedesktop/secrets', 'org.freedesktop.Secret.Service.ReadAlias', 'default')
    collection = re.search(r"'(/org/freedesktop/secrets/collection/[^']+)'", alias)[1]
    gdbus('org.freedesktop.secrets', '/org/freedesktop/secrets', 'org.freedesktop.Secret.Service.Lock', "['" + collection + "']")
    phase('locked')
    phase('unlock', password)
    owner(daemon)
    phase('read-after-unlock')
    original_vault = root / 'data/linux-self-test-protector/accounts.vault'
    original_hash = hashlib.sha256(original_vault.read_bytes()).hexdigest()
    daemon.terminate()
    daemon.wait(timeout=10)
    daemon = keyring()
    phase('read-after-restart')
    check('Provider restart preserves original ciphertext bytes', hashlib.sha256(original_vault.read_bytes()).hexdigest() == original_hash)
    handoff = json.loads(run(['/contracts/handoff/CodexSwitch.LinuxHandoff.Tests', str(root / 'codex-linux-handoff-test-integration')], capture_output=True, timeout=200).stdout)
    check('Production Linux account/coordinator/metadata/managed desktop integration passes offline', handoff['failed'] == 0 and handoff['passed'] == 8)
    phases.append(handoff)
    ordinary_home = root / 'ordinary-codex'
    ordinary_home.mkdir(mode=0o700)
    (ordinary_home / 'config.toml').write_text('cli_auth_credentials_store = "file"\nanalytics.enabled = false\n')
    (ordinary_home / 'config.toml').chmod(0o600)
    os.environ['CODEX_HOME'] = str(ordinary_home)
    app = '/opt/codex-switch/CodexSwitch'
    info = json.loads(run([app, '--package-info'], capture_output=True).stdout)
    check('Installed candidate version/architecture/runtime match', info['version'] == expected['candidateVersion'] and info['architecture'] == 'X64' and info['runtime'] == '.NET 10.0.13')
    run([app, '--install-package'], stdout=private_log, stderr=private_log)
    entry = root / 'share/applications/codex-switch.desktop'
    run(['desktop-file-validate', str(entry)])
    run(['gio', 'launch', str(entry)], stdout=private_log, stderr=private_log)
    executable = (root / 'share/codex-switch/current/CodexSwitch').resolve()
    def find_app():
        for process in Path('/proc').glob('[0-9]*'):
            try:
                if (process / 'exe').resolve() == executable:
                    ids = windows(int(process.name))
                    if ids:
                        return int(process.name), ids[0]
            except (FileNotFoundError, PermissionError):
                continue
        return None
    pid, window = wait_for(find_app, 30)
    check('Ordinary desktop entry launches the actual per-user installed program', Path('/proc/' + str(pid) + '/exe').resolve() == executable)
    wait_for_initialized_ui(window)
    check('Ordinary account initialization visibly completes with configured volume and capability status', (root / 'share/CodexAccountDesk/accounts.vault').read_bytes().startswith(b'CADL1') and not (ordinary_home / 'auth.json').exists())
    run(['xdotool', 'windowactivate', '--sync', window])
    for width, height in ((500, 360), (600, 420), (800, 600)):
        run(['xdotool', 'windowsize', '--sync', window, str(width), str(height)])
        time.sleep(0.6)
        capture(window, pid, f'ordinary-dark-{width}x{height}')
    run(['xdotool', 'windowsize', '--sync', window, '500', '360'])
    before_login_vault = (root / 'share/CodexAccountDesk/accounts.vault').read_bytes()
    run(['xdotool', 'mousemove', '--window', window, '359', '17', 'click', '1'])
    def login_controls(pending):
        raw = root / 'login-controls-private.xwd'
        png = root / 'login-controls-private.png'
        run(['xwd', '-silent', '-id', window, '-out', str(raw)])
        run(['convert', str(raw), str(png)])
        with Image.open(png) as current, Image.open(results / 'ordinary-dark-500x360.png') as before:
            changed = current.convert('RGB').crop((8, 4, 55, 30)).tobytes() != before.convert('RGB').crop((8, 4, 55, 30)).tobytes()
            return changed == pending
    wait_for(lambda: login_controls(True), 40)
    check('Ordinary account addition exposes native pending-login cancellation at minimum size', not (ordinary_home / 'auth.json').exists())
    capture(window, pid, 'ordinary-pending-login-500x360')
    run(['xdotool', 'mousemove', '--window', window, '32', '17', 'click', '1'])
    wait_for(lambda: login_controls(False), 20)
    check('Ordinary Cancel restores the ready toolbar without changing existing ciphertext or creating authentication', not (ordinary_home / 'auth.json').exists() and (root / 'share/CodexAccountDesk/accounts.vault').read_bytes() == before_login_vault)
    run(['xdotool', 'windowsize', '--sync', window, '800', '600'])
    run(['xdotool', 'mousemove', '--window', window, '631', '17', 'click', '1'])
    def managed_client():
        for process in Path('/proc').glob('[0-9]*'):
            try:
                if (process / 'exe').resolve() != Path('/usr/lib/chatgpt/ChatGPT'):
                    continue
                parent = re.search(r'^PPid:\s+(\d+)$', (process / 'status').read_text(), re.MULTILINE)
                if parent and int(parent[1]) == pid and windows(int(process.name)):
                    return int(process.name), windows(int(process.name))[0]
            except (FileNotFoundError, PermissionError):
                continue
        return None
    client_pid, client_window = wait_for(managed_client, 40)
    check('Ordinary Start Codex button launches its own actual approved desktop child', Path('/proc/' + str(client_pid) + '/exe').resolve() == Path('/usr/lib/chatgpt/ChatGPT'))
    run(['xdotool', 'windowactivate', '--sync', client_window])
    active = run(['xdotool', 'getactivewindow'], capture_output=True, text=True).stdout.strip()
    if active != client_window:
        raise RuntimeError('The original managed client must own focus before normal Quit input')
    # Normal input follows X focus. Explicit-XID key delivery instead sends the
    # modifier release to an already destroyed window when Quit exits promptly.
    run(['xdotool', 'key', 'ctrl+q'])
    wait_for(lambda: not Path('/proc/' + str(client_pid)).exists(), 40)
    check('Ordinary managed start keeps authentication empty and allows normal application Quit', not (ordinary_home / 'auth.json').exists())
    run(['xdotool', 'windowactivate', '--sync', window])
    run(['xdotool', 'mousemove', '--window', window, '758', '17', 'click', '1'])
    run(['xdotool', 'mousemove', '--window', window, '20', '80'])
    time.sleep(0.5)
    capture(window, pid, 'ordinary-light-800x600')
    with Image.open(results / 'ordinary-dark-800x600.png') as before, Image.open(results / 'ordinary-light-800x600.png') as after:
        check('Ordinary theme button changes the rendered application theme', sum(after.convert('RGB').getpixel((5, 80))) > sum(before.convert('RGB').getpixel((5, 80))) + 200)
    run(['xdotool', 'key', '--window', window, 'F5'])
    check('Ordinary refresh keeps the installed program alive', Path('/proc/' + str(pid)).exists())
    run(['xdotool', 'key', '--window', window, 'ctrl+w'])
    wait_for(lambda: not Path('/proc/' + str(pid)).exists())
    check('Ordinary trayless close exits the installed process', not Path('/proc/' + str(pid)).exists())
    run(['gio', 'launch', str(entry)], stdout=private_log, stderr=private_log)
    pid, window = wait_for(find_app, 30)
    wait_for_initialized_ui(window)
    capture(window, pid, 'ordinary-reopened')
    run(['xdotool', 'windowactivate', '--sync', window])
    run(['xdotool', 'key', '--window', window, 'ctrl+w'])
    wait_for(lambda: not Path('/proc/' + str(pid)).exists())
    cli_home = root / 'ordinary-cli-only'
    cli_home.mkdir(mode=0o700)
    (cli_home / 'config.toml').write_text('cli_auth_credentials_store = "file"\nanalytics.enabled = false\n')
    (cli_home / 'config.toml').chmod(0o600)
    cli = Path('/usr/lib/chatgpt/resources/codex')
    cli_ui = subprocess.Popen([str(executable), '--codex-home', str(cli_home), '--codex-cli', str(cli), '--codex-cli-sha256', hashlib.sha256(cli.read_bytes()).hexdigest()], stdout=private_log, stderr=private_log)
    pid, window = wait_for(find_app, 30)
    wait_for_initialized_ui(window)
    check('Ordinary checksum-selected CLI-only UI launches without a desktop or authentication', pid == cli_ui.pid and not (cli_home / 'auth.json').exists())
    run(['xdotool', 'windowsize', '--sync', window, '800', '600'])
    time.sleep(1)
    capture(window, pid, 'ordinary-cli-only-800x600')
    run(['xdotool', 'windowactivate', '--sync', window])
    run(['xdotool', 'key', '--window', window, 'ctrl+w'])
    cli_ui.wait(timeout=30)
    check('Ordinary CLI-only trayless close preserves its empty auth home', cli_ui.returncode == 0 and not (cli_home / 'auth.json').exists())
    removed = json.loads(run([str(executable), '--uninstall-package'], capture_output=True).stdout)
    check('Fresh installed self-uninstall returns successful JSON through redirected output', removed['uninstalled'] is True)
    check('Per-user uninstall removes only owned menu/current integration', not entry.exists() and not (root / 'share/codex-switch/current').exists())
    run([app, '--install-package'], stdout=private_log, stderr=private_log)
    master, slave = pty.openpty()
    terminal = subprocess.Popen([str(executable), '--uninstall-package'], stdin=subprocess.DEVNULL, stdout=slave, stderr=slave)
    os.close(slave)
    terminal_output = bytearray()
    try:
        deadline = time.monotonic() + 30
        while time.monotonic() < deadline:
            if select.select([master], [], [], 0.1)[0]:
                try:
                    block = os.read(master, 8192)
                except OSError as error:
                    if error.errno != errno.EIO:
                        raise
                    break
                if not block:
                    break
                terminal_output.extend(block)
            elif terminal.poll() is not None:
                break
        terminal.wait(timeout=5)
    finally:
        os.close(master)
    check('Fresh installed self-uninstall also succeeds on a real PTY', terminal.returncode == 0 and json.loads(terminal_output)['uninstalled'] is True and not entry.exists() and not executable.exists())
    neutral = subprocess.Popen(['xmessage', '-title', 'Owned unrelated desktop verification', 'Owned neutral fixture; no personal screen'], stdout=private_log, stderr=private_log)
    neutral_window = wait_for(lambda: subprocess.run(['xdotool', 'search', '--onlyvisible', '--name', '^Owned unrelated desktop verification$'], capture_output=True, text=True, timeout=5).stdout.splitlines())[0]
    for attempt in (1, 2):
        layout = 'us' if attempt == 1 else 'fr'
        run(['setxkbmap', '-layout', layout, '-option', ''])
        home = root / 'codex-linux-desktop-test-session-peer'
        if not home.exists():
            home.mkdir(mode=0o700)
        peer = subprocess.Popen(['/contracts/desktop/CodexSwitch.LinuxDesktop.Tests', '--lifecycle', str(home)], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=private_log, text=True)
        selector = selectors.DefaultSelector()
        selector.register(peer.stdout, selectors.EVENT_READ)
        if not selector.select(40):
            raise TimeoutError('Official sandboxed client did not reach IPC readiness')
        first = peer.stdout.readline()
        selector.close()
        if not first:
            raise RuntimeError('Official native peer exited before IPC; see private diagnostics')
        started = json.loads(first)
        native_pid = started['ownedProcess']
        check('Actual native keyboard layout is ' + layout, re.search(r'^layout:\s+' + layout + r'\s*$', run(['setxkbmap', '-query'], capture_output=True, text=True).stdout, re.MULTILINE) is not None)
        native_window = wait_for(lambda: windows(native_pid), 15)[0]
        capture(native_window, native_pid, 'official-client-start-' + str(attempt))
        run(['xdotool', 'windowactivate', '--sync', neutral_window])
        rest, _ = peer.communicate('request-normal-close\n', timeout=45)
        if peer.returncode != 0:
            raise RuntimeError('The original native client did not complete normal application Quit; inspect the private native diagnostics')
        value = json.loads(rest)
        check('Actual sandboxed official-client start/IPC/normal close cycle ' + str(attempt), peer.returncode == 0 and value['failed'] == 0 and value['passed'] == 7)
        check('Native Quit preserves the independent neutral window with ' + layout + ' key mapping', neutral.poll() is None)
        value['keyboardLayout'] = layout
        phases.append(value)
    run(['setxkbmap', '-layout', 'us', '-option', ''])
    neutral.terminate()
    neutral.wait(timeout=5)
    for path, value in markers.items():
        check('Original opaque synthetic marker bytes/modes preserved', path.read_bytes() == value and path.stat().st_mode & 0o777 == 0o600)
    phase('read-after-restart')
    check('Original native-key ciphertext survives desktop/UI/client lifecycle', hashlib.sha256(original_vault.read_bytes()).hexdigest() == original_hash)
    packages = run(['dpkg-query', '-W', '-f=${Package} ${Version}\n', 'xfce4-session', 'xfwm4', 'xfce4-panel', 'gnome-keyring', 'chatgpt', 'codex-switch'], capture_output=True, text=True).stdout.splitlines()
    result = {'schema': 1, 'expected': expected, 'passed': len(checks), 'failed': 0, 'checks': checks, 'nativePhases': phases, 'images': images, 'packages': packages, 'kernel': platform.release(), 'ordinaryUi': True, 'accountsUsed': False, 'fictionalCredentialsInContracts': True, 'display': 'X11/Xvfb with actual Xfce session/xfwm4/panel', 'scope': 'Actual normal-user containerized desktop userspace; ordinary installed account/CLI-only UI, resize/refresh/close/reopen, GNOME encrypted storage, fictional local auth/coordinator/recovery/context policy, actual offline CLI metadata and managed desktop discovery/thread-open/normal close/relaunch. No authenticated account switch/Continue, login manager/PAM, booted VM, native GPU, GNOME/KDE/Wayland, ARM64 desktop or supported Release acceptance.'}
    (results / 'session.json').write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps({'passed': len(checks), 'failed': 0, 'images': len(images)}), flush=True)
finally:
    for owned in (daemon, desktop):
        if owned.poll() is None:
            owned.terminate()
    private_log.close()
