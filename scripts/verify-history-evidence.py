"""Validate English-only native history evidence from disposable application fixtures."""
import argparse
import hashlib
import json
from pathlib import Path
import re
from PIL import Image

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("source", type=Path)
parser.add_argument("destination", type=Path)
parser.add_argument("--revision", required=True)
args = parser.parse_args()
if not re.fullmatch(r"[a-f0-9]{40}", args.revision):
    raise SystemExit("Exact source revision required")
report = json.loads((args.source / "history-report.json").read_text(encoding="utf-8"))
if (report["os"] != "macOS" or report["failures"] != 0 or report["nativeWindowCapture"] is not True
        or report["language"] != "en" or report["rendering"] != "Software (native window)"
        or report["architecture"] not in ("Arm64", "X64") or len(report["checks"]) != 74):
    raise SystemExit("Complete English native history verification required")
if not re.fullmatch(r"\d+\.\d+\.\d+", report["version"]) or not re.fullmatch(r"\.NET \d+\.\d+\.\d+", report["runtime"]):
    raise SystemExit("Invalid application/runtime version")
expected = {f"en-{theme}-history-{case}-native.png" for theme in ("dark", "light")
            for case in ("overview", "minimum", "empty", "single", "multiple", "unavailable", "quota", "30days", "date-editor", "custom")}
if {path.name for path in args.source.glob("*.png")} != expected:
    raise SystemExit("All twenty declared history window/popup captures required")
args.destination.mkdir(parents=True, exist_ok=True)
images = []
for name in sorted(expected):
    with Image.open(args.source / name) as image:
        rgb = image.convert("RGB")
        minimum = (120, 60) if "-quota-" in name else (400, 280)
        if rgb.width < minimum[0] or rgb.height < minimum[1] or rgb.width > 4000 or rgb.height > 3000 or rgb.entropy() < 1:
            raise SystemExit("Blank or invalid history capture")
        clean = Image.new("RGB", rgb.size)
        clean.paste(rgb)
        output = args.destination / name
        clean.save(output, format="PNG")
    with Image.open(output) as exported:
        if exported.info:
            raise SystemExit("Image metadata was not stripped")
    images.append({"file": name, "width": rgb.width, "height": rgb.height,
                   "sha256": hashlib.sha256(output.read_bytes()).hexdigest(), "capture": "Native own window or popup"})
receipt = {"schema": 1, "revision": args.revision, "os": report["os"], "architecture": report["architecture"],
           "runtime": report["runtime"], "appVersion": report["version"], "language": "en", "rendering": report["rendering"],
           "checks": report["checks"], "images": images, "scope": report["scope"]}
(args.destination / "evidence.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"historyScreenshots": len(images), "nativeChecks": len(report["checks"]), "metadataRemoved": True}))
