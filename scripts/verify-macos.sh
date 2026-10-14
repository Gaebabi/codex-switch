#!/bin/bash
set -euo pipefail
task_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
task_binary="${1:-$task_root/src/macos/bin/Release/net10.0/CodexSwitch}"
if [[ $# -eq 0 ]]; then
  dotnet build "$task_root/src/macos/CodexSwitch.Mac.csproj" -c Release -p:UseSharedCompilation=false
fi
task_output="$task_root/artifacts/macos-verification/$(date -u +%Y%m%dT%H%M%SZ)-$$"
mkdir -p "$task_output"
"$task_binary" --verify-macos "$task_output/native.json"
python3 "$task_root/scripts/verify-queues.py" --executable "$task_binary"
"$task_binary" --preview-macos "$task_output/renders"
printf 'Native report: %s\nRender report: %s\n' "$task_output/native.json" "$task_output/renders/render-report.json"
echo 'Real login, switching and chat resumption remain separate from these checks.'
