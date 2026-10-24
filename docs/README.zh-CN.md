<p align="center"><img src="../assets/macos/app-icon.png" width="88" height="88" alt="Codex Switch app icon" /></p>
<h1 align="center">Codex Switch</h1>

**在一个桌面界面中管理OpenAI Codex账号切换和使用额度。** 比较各账号的剩余额度、选择下一个账号，并通过已保存的记录恢复中断的切换。可选的额度阈值自动切换默认**关闭**。

Windows · macOS15及以上 · Linux预览版 · MIT开源 · **1.42.2**

[English](../README.md) / [한국어](README.ko.md) / [日本語](README.ja.md) / 简体中文

[下载](#下载) · [开始使用](INSTALL.md) · [更新内容](https://github.com/Gaebabi/codex-switch/releases/tag/v1.42.2)

<p align="center"><img src="media/discovery/en-dark-demo.png" width="500" height="400" alt="Codex Switch英文UI演示：账号剩余额度、CPU与内存及GPU使用率和两个存储设备" /></p>

使用模拟账号和系统数据的Windows英文UI演示。[演示来源](media/discovery/README.md) / [原生验证](evidence/account-automation/README.md)。凭证保存在本地加密存储中，**切换账号会重启Codex。** 本项目独立开发，与OpenAI没有隶属关系。

**如果觉得有用，欢迎Star本仓库并分享给其他Codex用户。**

1.42.2新增警告按钮的指定账号浏览器重新登录、默认关闭的额度阈值自动切换、最佳选择/自定义顺序、重启恢复记录、订阅日期标题及官方Pro 100/200/500名称。[行为与验证范围](development/ACCOUNT_AUTOMATION.md) / [发布流程](development/RELEASE_1_42_2.md)。

## 下载

- [Windows x64 ZIP](https://github.com/Gaebabi/codex-switch/releases/download/v1.42.2/CodexSwitch-v1.42.2-win-x64.zip)
- macOS15及以上：[Apple Silicon ZIP](https://github.com/Gaebabi/codex-switch/releases/download/v1.42.2/CodexSwitch-v1.42.2-osx-arm64.zip) / [Intel ZIP](https://github.com/Gaebabi/codex-switch/releases/download/v1.42.2/CodexSwitch-v1.42.2-osx-x64.zip)
- Linux **预览版** x64、ARM64：[DEB、RPM、tar.gz及SHA256](https://github.com/Gaebabi/codex-switch/releases/tag/v1.42.2)

完整程序包包含.NET运行时，无需构建或安装.NET。请保留完整文件夹或应用包。Windows/Linux未签名；Mac为临时签名，**未经Apple公证**。[安装](INSTALL.md) / [Mac首次启动](MACOS.md)

Linux仍为集成预览版。原生密钥环、已安装UI和程序包验证，与真实双账号交接及完整桌面支持分开记录。桌面集成需要已验证的OpenAI Linux客户端26.1007.71326、现有file认证策略以及X11/XWayland。新客户端需单独审核，否则拒绝使用。显式校验和的CLI专用模式不恢复桌面聊天。[要求](development/LINUX.md) / [安装包](development/LINUX_PACKAGES.md) / [支持计划](PLATFORM_SUPPORT_PLAN.md)

## 使用与功能

悬停警告图标可查看原因，点击可在浏览器中重新登录指定账号。[实际应用验证](evidence/account-automation/README.md)。

**Linux1.42.2下载包含源码1.41.0新增的终端账号切换和已保存CLI会话恢复功能。** `CodexSwitch cli`无需显示服务器即可管理现有加密账号；`cli switch <account> --resume <CLI-session-UUID>`将手动切换与官方CLI恢复连接起来。`--continue`明确请求发送后续消息，`--exec --continue`选择非交互执行。需要经过验证的原生CLI、校验和以及已解锁的受支持Secret Service。已在Linux x64上验证真实双账号切换后保留原有上下文的CLI恢复。ARM64、离线验证及完整桌面支持仍分别记录。[命令、实际截图与验证范围](development/LINUX_TERMINAL.md)。

1. Windows运行`CodexSwitch.exe`；Mac运行`Install.command`后打开已安装的**Codex Switch.app**。Linux需要完整预览包及受支持的Secret Service。
2. **+**通过官方浏览器页面登录各账号。如果自动选择了之前的账号，请使用独立的浏览器配置或隐私窗口。Windows可右键点击**↻**导入当前登录。
3. 比较额度后点击**Switch**。**账号自动切换默认关闭，可从上方菜单手动开启。** Linux用户请先通过本工具的**Start Codex**启动Codex。

- 保存并验证带日期的聊天名称、备份当前登录，正常退出Codex后应用所选账号。重新打开相同聊天ID、目录、模型及权限，向符合条件的未完成空闲聊天**发送一次Continue**，自动继续工作。不重复运行已执行或已完成的工作。
- **Resume chats**恢复加密保存的交接进度。发送结果不明确时不会自动重发；名称保存或正常退出失败时停止登录变更。Windows等待子进程退出，使一次Switch即可继续后续流程。[切换原理](SWITCHING.md)
- 剩余额度、重置、套餐、到期日、重置次数、积分和消耗预测。**... → History**查看记录。卡片日期使用用户本地时区，共享历史界面的时间轴明确标注KST。到期72小时、重置48小时以内显示警告色。
- 本地运行、队列和目标数量，资源趋势及磁盘空间。Windows对可用的资源/磁盘温度独立着色；不可用值保持未知。Linux资源监测尚未支持。[温度范围](TEMPERATURES.md)
- 始终每60秒刷新，**↻、F5**立即刷新。启动时密钥环锁定导致初始化失败时，解锁后会按同一周期自动重试初始化。九种UI语言、带英语回退的JSON语言包、图标主题操作、深浅色主题及统一滚动条。[翻译贡献](LOCALIZATION.md)

切换会重启Codex，也可能影响共享登录的CLI/IDE。Linux真实账号交接和更广泛的桌面、无障碍验证通过支持计划跟踪。

## 实际应用截图

[Windows/Linux最新截图及验证范围](media/release-1.42.2/README.md)。保留实际标题栏并放置在纯色背景上，明确区分虚构UI输入与真实账号验证。

![Windows运行截图](media/release-1.42.2/windows.png)

**Windows x64：**实际WPF应用中的虚构账号、带编号的Pro方案及明确的账号自动切换文案。仅演示选择开启，默认关闭。未使用个人账号或桌面。
![同一窗口中实际Xfce系统标题栏和4个演示账号的Linux截图](media/release-1.42.2/linux-x64-dark.png)

**Linux x64 · X11/Xfwm4：**实际系统标题栏与4个虚构演示账号在同一窗口内一起拍摄。未合成标题栏，也没有个人登录信息。[浅色主题、来源、哈希及验证范围](media/release-1.42.2/README.md)。

**预留MacBook实机截图位置，稍后由维护者添加。**
<!-- MacBook screenshot slot: media/release-1.42.2/macbook.png; privacy/provenance review required. -->

[贡献](../CONTRIBUTING.md) / [安全](../SECURITY.md) / [验证资料](evidence/README.md) / [消耗预测](FORECAST.md)

这是独立开源软件，并非OpenAI官方产品。认证信息在本地加密保存。[MIT](../LICENSE)
