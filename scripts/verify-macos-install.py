"""Check ZIP signing and staged installation in a disposable directory."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time

root = Path(__file__).resolve().parents[1]
archive = Path(sys.argv[1]).resolve()
checks = []


def check(name, condition):
    if not condition:
        raise AssertionError(name)
    checks.append(name)


def install(bundle, destination):
    return subprocess.run(["bash", str(root / "scripts/install-macos.sh"), str(bundle), str(destination)],
                          capture_output=True, text=True, timeout=60)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


with tempfile.TemporaryDirectory(prefix="mac-install-test-", dir=root / "artifacts") as directory:
    fixture = Path(directory)
    unpacked = fixture / "unpacked"
    subprocess.run(["ditto", "-x", "-k", str(archive), str(unpacked)], check=True, capture_output=True)
    bundle = unpacked / "Codex Switch.app"
    for name in ("OpenAI-Codex.LICENSE.txt", "OpenAI-Codex.NOTICE.txt"):
        check("Model instruction license survives bundle packaging: " + name,
              digest(bundle / "Contents/Resources/licenses" / name) == digest(root / "licenses" / name))
    signed = subprocess.run(["codesign", "--verify", "--deep", "--strict", str(bundle)], capture_output=True)
    check("ZIP extraction preserves complete bundle signatures", signed.returncode == 0)
    destination = fixture / "Applications"
    result = install(bundle, destination)
    installed = destination / "Codex Switch.app"
    executable = installed / "Contents/MacOS/CodexSwitch"
    check("First staged installation boots without an SDK", result.returncode == 0 and executable.exists())
    previous = digest(executable)

    bad = fixture / "malformed.app"
    (bad / "Contents/MacOS").mkdir(parents=True)
    (bad / "Contents/Resources").mkdir()
    shutil.copy2(executable, bad / "Contents/MacOS/CodexSwitch")
    shutil.copy2(bundle / "Contents/Resources/entitlements.plist", bad / "Contents/Resources/entitlements.plist")
    (bad / "Contents/Info.plist").write_text("invalid plist", encoding="utf-8")
    result = install(bad, destination)
    check("Invalid staged bundle preserves existing installation", result.returncode != 0 and digest(executable) == previous
          and not list(destination.glob("Codex Switch-*.app")))
    check("Failed staging removes its temporary folder and lock", not list(destination.glob(".account-desk-install.*"))
          and not (destination / ".codex-account-desk-install.lock").exists())

    result = install(bundle, destination)
    backups = list(destination.glob("Codex Switch-*.app"))
    check("Successful update preserves a bootable previous app", result.returncode == 0 and len(backups) == 1
          and digest(backups[0] / "Contents/MacOS/CodexSwitch") == previous)

    preview = subprocess.Popen([str(executable), "--preview-macos", str(fixture / "renders")], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        for _ in range(100):
            if subprocess.run(["pgrep", "-x", "CodexSwitch"], capture_output=True).returncode == 0:
                break
            time.sleep(0.02)
        result = install(bundle, destination)
        check("Running Account Desk is preserved and replacement refused", result.returncode != 0
              and "Quit Codex Switch" in result.stderr and digest(executable) == previous)
    finally:
        preview.wait(timeout=30)
    check("Synthetic running fixture exits successfully", preview.returncode == 0)

print(json.dumps({"passed": len(checks), "failed": 0, "checks": checks,
                  "scope": "Disposable ZIP/install/preview fixtures only; no user installation, account or Codex changes."}, indent=2))
