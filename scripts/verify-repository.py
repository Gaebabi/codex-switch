"""Verify tracked UTF-8 documents, local links and contributor/CI configuration."""
from pathlib import Path
import plistlib
import re
import subprocess
from urllib.parse import unquote, urlsplit
import xml.etree.ElementTree as ET
import yaml
import importlib.util

spec = importlib.util.spec_from_file_location('document_links', Path(__file__).with_name('document-links.py'))
document_links = importlib.util.module_from_spec(spec)
spec.loader.exec_module(document_links)

root = Path(__file__).resolve().parents[1]
names = subprocess.check_output(["git", "-C", str(root), "ls-files", "-z"]).decode("utf-8").split("\0")
documents = links = configurations = 0
errors = []

version = ET.parse(root / "Directory.Build.props").findtext("./PropertyGroup/Version")
if not version or not re.fullmatch(r"\d+\.\d+\.\d+", version):
    errors.append("Directory.Build.props: expected one semantic managed version")
for project in ("src/core/CodexSwitch.Core.csproj", "src/platforms/security/CodexSwitch.PlatformSecurity.csproj", "src/platforms/client/CodexSwitch.DesktopClient.csproj", "src/platforms/protocol/CodexSwitch.Protocol.csproj", "src/platforms/sqlite/CodexSwitch.Sqlite.csproj", "src/windows/CodexSwitch.Windows.csproj", "src/macos/CodexSwitch.Mac.csproj"):
    if ET.parse(root / project).find("./PropertyGroup/Version") is not None:
        errors.append(f"{project}: use the shared managed version")
with (root / "src/macos/Info.plist").open("rb") as bundle:
    metadata = plistlib.load(bundle)
if metadata.get("CFBundleVersion") != version or metadata.get("CFBundleShortVersionString") != version:
    errors.append("src/macos/Info.plist: bundle and managed version must match")

for name in filter(None, names):
    if not name.isascii():
        errors.append(f"{name}: tracked file names must be portable ASCII")
    if "/" not in name and Path(name).suffix in (".cs", ".csproj", ".css"):
        errors.append(f"{name}: application source/resources belong in their directories")

for name in filter(None, names):
    path = root / name
    if path.suffix not in (".md", ".yml", ".yaml"):
        continue
    source = path.read_text(encoding="utf-8", errors="strict")
    if "\ufffd" in source:
        errors.append(f"{name}: Unicode replacement character")
    if path.suffix == ".md":
        documents += 1
        for target in document_links.targets(source):
            parsed = urlsplit(target)
            if parsed.scheme or parsed.netloc:
                continue
            resolved = (path.parent / unquote(parsed.path)).resolve() if parsed.path else path.resolve()
            if not resolved.is_relative_to(root) or not resolved.exists():
                errors.append(f"{name}: missing or external local target {target}")
            elif parsed.fragment and resolved.suffix == '.md':
                if unquote(parsed.fragment) not in document_links.anchors(resolved.read_text(encoding='utf-8')):
                    errors.append(f"{name}: missing heading anchor {target}")
            links += 1
    else:
        configurations += 1
        data = yaml.load(source, Loader=yaml.BaseLoader)
        if name.startswith(".github/ISSUE_TEMPLATE/") and "body" in data:
            identifiers = [field["id"] for field in data["body"] if "id" in field]
            if len(identifiers) != len(set(identifiers)):
                errors.append(f"{name}: duplicate issue field ID")
        if name.startswith(".github/workflows/"):
            if data.get("permissions") != {"contents": "read"}:
                errors.append(f"{name}: PR verification needs contents: read only")
            if "pull_request_target" in data.get("on", {}) or "workflow_run" in data.get("on", {}):
                errors.append(f"{name}: privileged event is outside PR verification scope")
            for job in data.get("jobs", {}).values():
                for step in job.get("steps", []):
                    action = step.get("uses")
                    if action and not re.fullmatch(r"actions/[a-z-]+@[0-9a-f]{40}", action):
                        errors.append(f"{name}: action must use a verified full SHA")
                    if action and action.startswith("actions/checkout@") and step.get("with", {}).get("persist-credentials") != "false":
                        errors.append(f"{name}: checkout must not persist credentials")
            if "secrets." in source:
                errors.append(f"{name}: PR verification must not consume repository secrets")

print(f"Checked {documents} Markdown documents, {links} local links and {configurations} YAML configurations.")
for error in errors:
    print(error)
raise SystemExit(1 if errors else 0)
