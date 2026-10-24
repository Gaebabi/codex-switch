<p align="center"><img src="../assets/macos/app-icon.png" width="88" height="88" alt="Codex Switch app icon" /></p>
<h1 align="center">Codex Switch</h1>

Codexアカウントを手動または任意の残量基準で切り替え、再開可能な未完了ローカルチャットを続けます。**1.42.2 · Windows、macOS15以降、Linuxプレビュー。**

[English](../README.md) / [한국어](README.ko.md) / 日本語 / [简体中文](README.zh-CN.md)

1.42.2では警告から対象アカウントのブラウザー再ログイン、初期状態オフの自動切替、最適選択/優先順位、再起動の復旧記録、契約日付の見出し、公式Pro 100/200/500表記を追加しました。[動作と検証範囲](development/ACCOUNT_AUTOMATION.md) / [リリース手順](development/RELEASE_1_42_2.md)。

## ダウンロード

- [Windows x64 ZIP](https://github.com/Gaebabi/codex-switch/releases/download/v1.42.2/CodexSwitch-v1.42.2-win-x64.zip)
- macOS15以降：[Apple Silicon ZIP](https://github.com/Gaebabi/codex-switch/releases/download/v1.42.2/CodexSwitch-v1.42.2-osx-arm64.zip) / [Intel ZIP](https://github.com/Gaebabi/codex-switch/releases/download/v1.42.2/CodexSwitch-v1.42.2-osx-x64.zip)
- Linux **プレビュー** x64・ARM64：[DEB・RPM・tar.gzとSHA256](https://github.com/Gaebabi/codex-switch/releases/tag/v1.42.2)

.NETを同梱しており、ビルドやランタイムのインストールは不要です。フォルダー・アプリ全体を保持してください。Windows/Linuxは未署名、Macはアドホック署名で**Apple未公証**です。[インストール](INSTALL.md) / [Mac初回起動](MACOS.md)

Linuxは統合プレビューです。キーストア・インストール済みUI・パッケージ検証と、認証済み2アカウントの引き継ぎ・完全なデスクトップ対応は区別します。デスクトップ統合には検証済みOpenAI Linuxクライアント26.1007.71326、既存のfile認証設定とX11/XWaylandが必要です。新しいクライアントは個別検証まで拒否します。明示的チェックサムのCLI専用モードはデスクトップチャットを再開しません。[要件](development/LINUX.md) / [パッケージ](development/LINUX_PACKAGES.md) / [対応計画](PLATFORM_SUPPORT_PLAN.md)

## 使い方と機能

警告にホバーすると原因を表示し、押すと対象アカウントでブラウザーに再ログインできます。[実際のアプリ検証](evidence/account-automation/README.md)。

**Linux1.42.2の配布物には、ソース1.41.0で追加した端末のアカウント切り替えと保存済みCLIセッションの再開が含まれます。** `CodexSwitch cli`で画面サーバーなしに既存の暗号化アカウントを管理し、`cli switch <account> --resume <CLI-session-UUID>`で手動切り替えと公式CLIの再開を実行します。`--continue`は追加メッセージ送信を明示し、`--exec --continue`は非対話実行です。検証済みのネイティブCLI・チェックサムと、ロック解除済みの対応Secret Serviceが必要です。Linux x64で実際の2アカウント切り替え後に元の文脈を保持したCLI再開を確認しました。ARM64・オフライン検証と完全なデスクトップ対応は別の範囲です。[コマンド・実際の画像・検証範囲](development/LINUX_TERMINAL.md)。

1. Windowsは`CodexSwitch.exe`、Macは`Install.command`でインストールした**Codex Switch.app**を開きます。Linuxは完全なプレビューパッケージと対応するSecret Serviceが必要です。
2. **+**で公式ブラウザー画面から各アカウントをログインします。以前のアカウントが自動選択される場合は、別のブラウザープロファイル・プライベートウィンドウを使います。Windowsは**↻**の右クリックで現在のログインを取り込めます。
3. 残量を比較して**Switch**を押します。**アカウント自動切替は初期状態オフで、上部メニューから有効にできます。** Linuxでは作業前に本アプリの**Start Codex**を使います。

- 日付付きチャット名を保存・検証してログインをバックアップし、Codexを正常終了します。同じチャットID・フォルダー・モデル・権限を開いて、再開可能な未完了の待機中チャットに**Continueを一度だけ**送り、自動再開します。実行中・完了済みの作業は重複実行しません。
- 暗号化された進行状況を**Resume chats**で再開できます。送信結果が不明なら自動再送せず、名前保存・正常終了の失敗時はログイン変更を停止します。Windowsでは子プロセス終了を待ち、一度のSwitchで処理を進めます。[切り替えの詳細](SWITCHING.md)
- 残量、リセット、プラン、契約期限、クレジットと消費予測。**... → History**で履歴を確認できます。カードの日付は利用者のローカル時間帯で表示し、共通履歴画面の時間軸にはKSTを明記します。有効期限72時間・リセット48時間以内は警告色になります。
- 実行・キュー・目標数、リソース履歴とディスク容量。Windowsは取得可能なリソース/ディスク温度を独立した色で表示します。不明な値は不明のままです。Linuxのリソース計測は未対応です。[温度の範囲](TEMPERATURES.md)
- 常時60秒更新、**↻・F5**で即時取得。起動時にキーリングがロックされていても、解除後は同じ周期で初期化を自動再試行します。9言語のUI、英語フォールバック付きJSON言語パック、アイコンのテーマ操作、ダーク/ライトと共通スクロールバー。[翻訳への貢献](LOCALIZATION.md)

切り替えはCodexを再起動し、同じログインを共有するCLI/IDEにも影響する場合があります。残るLinux実アカウント・デスクトップ・アクセシビリティ検証は対応計画で追跡します。

## 実際のアプリ画面

[Windows/Linux最新画像と検証範囲](media/release-1.42.2/README.md)。実際のタイトルバーを残して単色背景に配置し、架空のUI入力と実アカウント検証を明記します。

![Windows実行画面](media/release-1.42.2/windows.png)

**Windows x64:** 実際のWPFアプリの架空アカウント、番号付きProプラン、明確な自動切替表示です。デモのみOnを選択し、既定はOffです。個人アカウントやデスクトップは使用していません。
![実際のXfce OSタイトルバーと4つのデモアカウントを同時に撮影したLinuxアプリ](media/release-1.42.2/linux-x64-dark.png)

**Linux x64 · X11/Xfwm4:** 実際のOSタイトルバーと架空のデモアカウント4つを同じウィンドウで撮影しました。タイトルバーの合成や個人のログイン情報はありません。[ライトテーマ・出典・ハッシュ・検証範囲](media/release-1.42.2/README.md)。

**MacBook実機のスクリーンショットは後日追加予定です。**
<!-- MacBook screenshot slot: media/release-1.42.2/macbook.png; privacy/provenance review required. -->

[貢献](../CONTRIBUTING.md) / [セキュリティ](../SECURITY.md) / [検証資料](evidence/README.md) / [消費予測](FORECAST.md)

OpenAIの公式製品ではない独立したオープンソースです。認証情報はローカルで暗号化して保存します。[MIT](../LICENSE)
