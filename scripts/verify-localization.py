"""Check shipped language coverage, stable aliases and .NET placeholder contracts."""
import json
from pathlib import Path
import re
import unicodedata

root = Path(__file__).resolve().parents[1]
def pairs(rows):
    result = {}
    for key, value in rows:
        if key in result:
            raise ValueError("Duplicate JSON key: " + key)
        result[key] = value
    return result
def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"), object_pairs_hook=pairs)
def signature(text):
    arguments, index = [], 0
    while index < len(text):
        if text[index:index+2] in ("{{", "}}"):
            index += 2
        elif text[index] == "{":
            match = re.match(r"\{(\d+) *(?:, *(-?\d+) *)?(?::([^{}]*))?\}", text[index:])
            if not match:
                raise ValueError("Invalid composite format: " + text)
            arguments.append((int(match[1]), int(match[2] or 0), match[3] or ""))
            index += len(match[0])
        elif text[index] == "}":
            raise ValueError("Unescaped closing brace: " + text)
        else:
            index += 1
    return sorted(arguments)
aliases = read(root / "localization/legacy-keys.json")
codes = read(root / "localization/index.json")
basis = read(root / "localization/packs/en.json")["strings"]
assert len(codes) == len(set(codes)) and codes[0] == "en"
assert set(codes) == {p.stem for p in (root / "localization/packs").glob("*.json")}
assert all(value in basis for value in aliases.values())
for code in codes:
    path = root / ("localization/packs/" + code + ".json")
    pack = read(path)
    assert pack["schemaVersion"] == 1 and pack["code"] == code and pack["direction"] in ("ltr", "rtl")
    assert set(pack["strings"]) == set(basis), code + ": missing or unknown messages"
    assert path.stat().st_size <= 1024 * 1024
    for key, value in pack["strings"].items():
        assert isinstance(value,str) and value.strip() and len(value) <= 4096, code + ": empty message " + key
        assert signature(value) == signature(basis[key]), code + ": placeholder mismatch " + key
        assert not any((unicodedata.category(c) == "Cc" and c not in "\n\t") or c == "\ufffd" or "\u202a" <= c <= "\u202e" or "\u2066" <= c <= "\u2069" for c in value), code + ": hidden control " + key
    for key, forms in pack.get("plurals",{}).items():
        assert key in basis and "other" in forms
        for category,value in forms.items():
            assert category in ("zero","one","two","few","many","other") and signature(value) == signature(basis[key])
for path in (root / "src").rglob("*.cs"):
    if any(part in ("bin","obj") for part in path.parts):
        continue
    source = path.read_text(encoding="utf-8")
    for text in re.findall(r'Loc\.(?:T|F|Count)\(\s*"((?:[^"\\]|\\.)*)"',source):
        value = json.loads('"' + text + '"')
        assert value in aliases or value in basis, str(path.relative_to(root)) + ": missing source mapping " + value
print(f"Validated {len(codes)} language packs, {len(basis)} messages each, aliases/source calls, placeholders, plurals and controls.")
