"""Independent SQLite/WAL integration checks in disposable homes, without Codex tasks."""
import hashlib
from contextlib import closing
import argparse
import json
import os
from pathlib import Path
import sqlite3
import subprocess
import sys
import tempfile
import uuid
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
VERSION = ET.parse(ROOT / 'Directory.Build.props').findtext('./PropertyGroup/Version')
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--executable", type=Path, default=ROOT / "artifacts/win-x64/CodexSwitch.exe")
EXE = parser.parse_args().executable.resolve()
checks = []


def check(name, condition):
    if not condition:
        raise AssertionError(name)
    checks.append(name)


def probe(home, name, expected=None):
    reports = home / "reports"
    reports.mkdir(exist_ok=True)
    report = reports / (name + ".json")
    watched = [home / "queue_1.sqlite", home / "queue_1.sqlite-wal", home / "state_5.sqlite", home / "state_5.sqlite-wal"]
    before = {p: hashlib.sha256(p.read_bytes()).digest() for p in watched if p.exists()}
    process = subprocess.run([str(EXE), "--codex-home", str(home), "--verify-queues", str(report)], timeout=10,
                             capture_output=True, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    data = json.loads(report.read_text(encoding="utf-8"))
    check(name + " identifies the declared application build", data.get("version") == VERSION)
    after = {p: hashlib.sha256(p.read_bytes()).digest() for p in watched if p.exists()}
    check(name + " leaves database and WAL presence/bytes unchanged", after == before)
    if expected is None:
        check(name + " reports unknown", process.returncode == 1 and not data["available"] and data["total"] is None and bool(data["error"]))
    else:
        check(name, process.returncode == 0 and data["available"] and (data["saved"], data["server"], data["total"]) == expected)
    return data


def saved(home, queues):
    (home / ".codex-global-state.json").write_text(json.dumps({"queued-follow-ups": queues}), encoding="utf-8")


(ROOT / "artifacts").mkdir(exist_ok=True)
with tempfile.TemporaryDirectory(prefix="queue-검증-", dir=ROOT / "artifacts") as directory, closing(sqlite3.connect(Path(directory) / "queue_1.sqlite")) as queue:
    home = Path(directory)
    root, child, cli = [str(uuid.uuid4()) for _ in range(3)]
    saved(home, {root: [{"id": "client-a"}, {"id": "legacy-b", "pausedReason": "paused"},
                        {"id": "remote", "submission": {"hostId": "remote"}}]})
    with sqlite3.connect(home / "state_5.sqlite") as state:
        state.execute("CREATE TABLE threads (id TEXT PRIMARY KEY, originator TEXT, source TEXT, agent_path TEXT)")
        state.executemany("INSERT INTO threads VALUES (?, ?, ?, ?)", [(root, "Codex Desktop", "vscode", None),
                         (child, "Codex Desktop", "vscode", "/root/child"), (cli, "codex_cli_rs", "cli", None)])
    state.close()
    queue.execute("PRAGMA journal_mode=WAL")
    queue.execute("PRAGMA wal_autocheckpoint=0")
    queue.execute("CREATE TABLE queued_items (id TEXT PRIMARY KEY, thread_id TEXT NOT NULL, payload_json TEXT NOT NULL, queue_order INTEGER, created_at_ms INTEGER, updated_at_ms INTEGER)")
    queue.commit()
    queue.execute("PRAGMA wal_checkpoint(TRUNCATE)")

    def insert(identifier, thread=root, client=None, payload=None):
        payload = payload or json.dumps({"UserInput": {"client_id": client, "content": [{"type": "text", "text": "isolated fixture"}]}})
        queue.execute("INSERT INTO queued_items VALUES (?, ?, ?, 0, 1, 1)", (identifier, thread, payload))

    insert("server-a", client="client-a")
    insert("server-new", client="client-new")
    insert("server-child", child, "child-client")
    insert("server-cli", cli, "cli-client")
    queue.commit()
    immutable = sqlite3.connect((home / "queue_1.sqlite").as_uri() + "?mode=ro&immutable=1", uri=True)
    check("WAL-only committed fixture is absent from main file", immutable.execute("SELECT COUNT(*) FROM queued_items").fetchone()[0] == 0)
    immutable.close()
    probe(home, "server-plus-saved-dedup-and-child-cli-exclusion", (2, 2, 3))
    if sys.platform == "linux":
        paths = [home / name for name in ("queue_1.sqlite", "queue_1.sqlite-wal", "queue_1.sqlite-shm", "state_5.sqlite")]
        modes = {path: path.stat().st_mode & 0o777 for path in paths}
        home_mode = home.stat().st_mode & 0o777
        try:
            for path in paths: path.chmod(0o400)
            home.chmod(0o500)
            check("Native read-only directory/file fixture cannot be written", os.geteuid() != 0 and not os.access(home, os.W_OK) and all(not os.access(path, os.W_OK) for path in paths))
            probe(home, "read-only-permissions-retain-committed-WAL", (2, 2, 3))
        finally:
            home.chmod(home_mode)
            for path, mode in modes.items(): path.chmod(mode)
    insert("uncommitted", client="new-client")
    probe(home, "uncommitted-message-stays-invisible", (2, 2, 3))
    queue.commit()
    probe(home, "committed-addition-in-WAL", (2, 3, 4))
    queue.execute("DELETE FROM queued_items WHERE id='server-new'")
    queue.commit()
    probe(home, "started-or-cancelled-message-decreases-total", (2, 2, 3))
    saved(home, {})
    probe(home, "saved-removal-with-server-items-remaining", (0, 2, 2))
    queue.execute("DELETE FROM queued_items")
    queue.commit()
    probe(home, "all-items-removed-in-WAL", (0, 0, 0))
    insert("bad-client", payload=json.dumps({"UserInput": {"client_id": 7}}))
    queue.commit()
    probe(home, "unsupported-client-identity")
    queue.execute("DELETE FROM queued_items")
    insert("unknown-thread", str(uuid.uuid4()), "unknown")
    queue.commit()
    probe(home, "missing-thread-metadata")
    queue.execute("DELETE FROM queued_items")
    queue.commit()
    queue.close()
    future = home / "queue_2.sqlite"
    future.write_bytes(b"unsupported version")
    probe(home, "unsupported-database-version")
    future.unlink()
    (home / "queue_1.sqlite").unlink()
    probe(home, "legacy-only-home-with-no-server-database", (0, 0, 0))
    (home / "queue_1.sqlite").write_bytes(b"invalid sqlite database")
    probe(home, "corrupt-database-does-not-fabricate-zero")

print(json.dumps({"passed": len(checks), "failed": 0, "scope": "Disposable SQLite/WAL fixtures only; no native Codex queue or task mutations.", "checks": checks}, indent=2))
