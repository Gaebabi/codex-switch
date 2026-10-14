"""Package native vector renders into Windows/Mac icons, preserving each ICO size.

Run CodexSwitch.exe --export-icons <directory> first, then pass that directory.
The checked-in outputs are enough for ordinary source builds.
"""
from pathlib import Path
from PIL import Image
import shutil
import struct
import sys

root = Path(__file__).resolve().parents[1]
renders = Path(sys.argv[1])
image = Image.open(renders / "1024.png").convert("RGBA")
if image.width != image.height or image.getextrema()[3][0] != 0:
    raise ValueError("Expected a square mark with genuine transparency")
sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
payloads = [(size, (renders / f"{size}.png").read_bytes()) for size in sizes]
offset = 6 + 16 * len(payloads)
directory = bytearray(struct.pack("<HHH", 0, 1, len(payloads)))
for size, png in payloads:
    directory += struct.pack("<BBBBHHII", size if size < 256 else 0, size if size < 256 else 0, 0, 0, 1, 32, len(png), offset)
    offset += len(png)
(root / "assets/brand/app-icon.ico").write_bytes(directory + b"".join(png for _, png in payloads))
image.save(root / "assets/macos/app-icon.icns", format="ICNS")
shutil.copyfile(renders / "1024.png", root / "assets/macos/app-icon.png")
