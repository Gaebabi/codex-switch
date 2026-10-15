"""Test-only PAM startup and real password-dialog control for an owned KSecrets6.30 wallet."""
from concurrent.futures import ThreadPoolExecutor
from contextlib import ExitStack, contextmanager
import hashlib
import os
from pathlib import Path
import socket
import subprocess
import time


@contextmanager
def pam_login(root, password):
    salt=root/'share/kwalletd/codex-switch-native-test.salt'
    hashed=bytearray(hashlib.pbkdf2_hmac('sha512',password,salt.read_bytes(),50000,56))
    path=root/'pam-control.sock'
    try:
        with ExitStack() as cleanup:
            listener=cleanup.enter_context(socket.socket(socket.AF_UNIX,socket.SOCK_STREAM))
            listener.bind(str(path));listener.listen(1)
            read,write=os.pipe();cleanup.callback(os.close,read);cleanup.callback(os.close,write)
            def deliver():
                if os.write(write,hashed)!=len(hashed):raise RuntimeError('Owned PAM pipe transfer was incomplete')
                with socket.socket(socket.AF_UNIX,socket.SOCK_STREAM) as peer:peer.connect(str(path))
            yield (['--pam-login',str(read),str(listener.fileno())],(read,listener.fileno()),deliver)
    finally:
        hashed[:]=bytes(len(hashed))
        if path.exists():path.unlink()


def unlock_dialog(process_id, password, phase, require_dialog):
    used=False
    with ThreadPoolExecutor(max_workers=1) as executor:
        result=executor.submit(phase,'kwallet-unlock')
        deadline=time.monotonic()+7
        while not result.done():
            found=subprocess.run(['xdotool','search','--onlyvisible','--pid',str(process_id)],capture_output=True,text=True,timeout=2)
            windows=found.stdout.split()
            if windows:
                if found.returncode or len(windows)!=1 or not windows[0].isdigit():raise RuntimeError('Expected one owned provider password dialog')
                subprocess.run(['xdotool','windowfocus','--sync',windows[0]],check=True,capture_output=True,timeout=3)
                subprocess.run(['xdotool','type','--clearmodifiers','--file','-'],input=password,capture_output=True,check=True,timeout=3)
                subprocess.run(['xdotool','key','Return'],check=True,capture_output=True,timeout=3)
                used=True
                break
            if time.monotonic()>deadline:raise RuntimeError('Owned password dialog did not appear before the deadline')
            time.sleep(0.05)
        result.result(timeout=12)
    if require_dialog and not used:raise RuntimeError('In-place unlock must use the actual owned password dialog')
    return used
