# macOS 개발 안내

설치와 기본 사용은 [Mac setup](../MACOS.md), 공통 구조는 [Development map](../ARCHITECTURE.md)를 확인합니다. 현재 소스는1.31.0 / .NET10 LTS이며 검토한 지원 기준은macOS15+입니다. 공개 다운로드는1.27.6을 유지하며 계정 전환·재개 수용은 별도입니다.

## 빌드와 검증

```bash
bash scripts/build-macos.sh osx-arm64
bash scripts/build-macos.sh osx-x64
bash scripts/verify-macos.sh
python3 scripts/verify-macos-install.py /path/to/CodexSwitch-osx-arm64.zip
```

빌드에는 .NET10.0.4xx SDK, 선택적 SQLite/설치 검사에는 Python3이 필요합니다. 생성물은 ignored artifacts에 저장합니다. `--verify-ui`는 실제 게시용 앱의 자체NSWindow만 캡처하며 가상계정과 CI software rendering을 사용합니다. 계정 없는 임시 Keychain/storage/socket fixtures나 창 실행을 실제 로그인/전환 성공으로 해석하지 않습니다.

## 연결과 저장소

- 앱 데이터: `~/Library/Application Support/CodexAccountDesk`
- Keychain 서비스: `com.gaebabi.codex-account-desk.vault`; 폴더 0700/파일 0600
- Codex home: `CODEX_HOME` 또는 `~/.codex`; 같은 UID의 `ipc/ipc.sock`
- Codex 로그인은 file 저장 방식을 사용하며 keyring/auto를 자동 변경하지 않습니다.
- 실행 중인 Codex 번들을 우선 탐색하고 필요한 경우 `CODEX_ACCOUNT_DESK_DESKTOP` / `CODEX_ACCOUNT_DESK_CLI`로 경로를 지정합니다.

## 남은 native 확인

Apple Silicon/Intel의 설치와 Keychain 재로드, 실제 로그인/사용량/계정 전환/같은 채팅 재개, 큐/목표 변화, GPU/VRAM 지원, 볼륨 설정을 기기에서 확인합니다. Apple Silicon 통합 메모리를 전용 VRAM으로 표시하지 않습니다.
