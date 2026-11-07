"""Verify version metadata from the actual, account-free Windows resource command."""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import xml.etree.ElementTree as ET


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('executable', type=Path)
    parser.add_argument('--expected-version', help='Independent build version; defaults to Directory.Build.props')
    parser.add_argument('--output', type=Path, help='Identifier-free verification receipt, never raw hardware samples')
    args = parser.parse_args()
    if os.name != 'nt':
        raise SystemExit('The actual Windows diagnostic must run on Windows.')
    expected = args.expected_version or ET.parse(Path(__file__).resolve().parents[1] / 'Directory.Build.props').findtext('./PropertyGroup/Version')
    if not expected or not re.fullmatch(r'\d+\.\d+\.\d+', expected):
        raise SystemExit('An independently declared three-part build version is required.')
    executable = args.executable.resolve(strict=True)
    with tempfile.TemporaryDirectory(prefix='codex-switch-resource-report-') as directory:
        root = Path(directory)
        report = root / 'resources.json'
        data, home = root / 'absent-data', root / 'absent-home'
        result = subprocess.run([str(executable), '--verify-resources', str(report), '--data-dir', str(data), '--codex-home', str(home)],
                                stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=45,
                                creationflags=subprocess.CREATE_NO_WINDOW)
        if result.returncode not in (0, 1) or not report.is_file():
            raise SystemExit(f'Resource command failed before producing its report (exit {result.returncode}).')
        value = json.loads(report.read_text(encoding='utf-8'))
        if value.get('version') != expected:
            raise SystemExit(f"Resource report version mismatch: expected {expected}, observed {value.get('version')!r}.")
        if data.exists() or home.exists():
            raise SystemExit('Account-free diagnostic unexpectedly initialized a data/home directory.')
        samples, available = value.get('samples'), value.get('available')
        if not isinstance(samples, list) or not samples or type(available) is not bool:
            raise SystemExit('Actual samples and explicit availability are required.')
        families = ('Cpu', 'Memory', 'Gpu', 'Vram')
        complete = any(all(sample[family]['Percent'] is not None for family in families) for sample in samples)
        if available != complete or result.returncode != (0 if available else 1):
            raise SystemExit('Original all-counter availability/exit-code contract changed.')
        receipt = {'schema': 1, 'version': value['version'], 'expectedVersion': expected, 'passed': 3, 'failed': 0,
                   'checks': ['Actual report identifies the declared application build', 'Diagnostic leaves absent account/home directories untouched',
                              'Original all-counter availability and exit status agree with actual samples'],
                   'sampleCount': len(samples), 'allCounterFamiliesAvailable': available, 'diagnosticExitCode': result.returncode,
                   'scope': 'Actual Windows published-app resource command; read-only native counters, no UI/account/client actions. Missing GPU/VRAM may retain diagnostic exit1. Raw hardware samples are not exported.'}
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(receipt))


if __name__ == '__main__':
    main()
