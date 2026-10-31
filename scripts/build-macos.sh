#!/bin/bash
set -euo pipefail
task_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
task_rid="${1:-}"
if [[ -z "$task_rid" ]]; then
  case "$(uname -m)" in arm64) task_rid=osx-arm64 ;; x86_64) task_rid=osx-x64 ;; *) echo 'Unsupported Mac architecture' >&2; exit 1 ;; esac
fi
case "$task_rid" in osx-arm64|osx-x64) ;; *) echo 'Use osx-arm64 or osx-x64' >&2; exit 1 ;; esac
task_output="$task_root/artifacts/macos/$task_rid/$(date -u +%Y%m%dT%H%M%SZ)-$$"
task_app="$task_output/Codex Switch.app"
mkdir -p "$task_app/Contents/MacOS" "$task_app/Contents/Resources"
dotnet publish "$task_root/src/macos/CodexSwitch.Mac.csproj" -c Release -r "$task_rid" --self-contained true \
  -p:UseSharedCompilation=false -p:UseAppHost=true -o "$task_app/Contents/MacOS"
if [[ -d "$task_app/Contents/MacOS/licenses" ]]; then
  mv "$task_app/Contents/MacOS/licenses" "$task_app/Contents/Resources/licenses"
fi
cmp "$task_root/licenses/OpenAI-Codex.LICENSE.txt" "$task_app/Contents/Resources/licenses/OpenAI-Codex.LICENSE.txt"
cmp "$task_root/licenses/OpenAI-Codex.NOTICE.txt" "$task_app/Contents/Resources/licenses/OpenAI-Codex.NOTICE.txt"
cp "$task_root/src/macos/Info.plist" "$task_app/Contents/Info.plist"
cp "$task_root/assets/macos/app-icon.icns" "$task_app/Contents/Resources/app-icon.icns"
cp "$task_root/licenses/Burn0Assets.LICENSE.txt" "$task_app/Contents/Resources/Burn0Assets.LICENSE.txt"
cp "$task_root/src/macos/entitlements.plist" "$task_app/Contents/Resources/entitlements.plist"
cp "$task_root/scripts/install-macos.sh" "$task_output/Install.command"
chmod +x "$task_output/Install.command"
chmod +x "$task_app/Contents/MacOS/CodexSwitch"
task_identity="${MAC_CODESIGN_IDENTITY:--}"
task_timestamp=(--timestamp=none)
if [[ "$task_identity" != "-" ]]; then task_timestamp=(--timestamp); fi
while IFS= read -r -d '' task_binary; do
  codesign --force --sign "$task_identity" "${task_timestamp[@]}" --options runtime \
    --entitlements "$task_root/src/macos/entitlements.plist" "$task_binary"
done < <(find "$task_app/Contents/MacOS" -type f ! -name CodexSwitch -print0)
codesign --force --sign "$task_identity" "${task_timestamp[@]}" --options runtime \
  --entitlements "$task_root/src/macos/entitlements.plist" "$task_app"
plutil -lint "$task_app/Contents/Info.plist"
codesign --verify --deep --strict "$task_app"
task_architecture="$(lipo -archs "$task_app/Contents/MacOS/CodexSwitch")"
case "$task_rid:$task_architecture" in osx-arm64:arm64|osx-x64:x86_64) ;; *) echo 'Bundle executable architecture does not match the requested RID' >&2; exit 1 ;; esac
ditto -c -k --sequesterRsrc --keepParent "$task_app" "$task_output/CodexSwitch-$task_rid.zip"
(cd "$task_output" && /usr/bin/zip -q "CodexSwitch-$task_rid.zip" Install.command)
printf 'App: %s\nZIP: %s\n' "$task_app" "$task_output/CodexSwitch-$task_rid.zip"
echo 'No tests or app launches were run. Install/open the app to validate on your Mac.'
