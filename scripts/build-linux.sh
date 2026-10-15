#!/usr/bin/env bash
set -euo pipefail
task_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
case "$(uname -m)" in
  x86_64) task_rid=linux-x64 ;;
  aarch64|arm64) task_rid=linux-arm64 ;;
  *) echo "The Linux source preview currently declares x64 and ARM64 only." >&2; exit 1 ;;
esac
python3 "$task_root/scripts/build-linux-secret.py" --output "$task_root/artifacts/linux-secret"
dotnet publish "$task_root/src/linux/CodexSwitch.Linux.csproj" -c Release -r "$task_rid" --self-contained true -p:UseSharedCompilation=false -o "$task_root/artifacts/$task_rid"
python3 "$task_root/scripts/verify-runtime-package.py" "$task_root/artifacts/$task_rid" --rid "$task_rid" --output "$task_root/artifacts/runtime-$task_rid.json"
echo "Source preview: $task_root/artifacts/$task_rid/CodexSwitch (account/client/handoff support is still being verified)."
