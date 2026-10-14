<p align="center">
  <img src="../assets/macos/app-icon.png" width="88" height="88" alt="Codex Switch app icon" />
</p>
<h1 align="center">Codex Switch</h1>

快速切换 Codex 账号，先看清下一个账号的剩余额度。**Windows / macOS / 1.27.6**

[English](../README.md) / [한국어](README.ko.md) / [日本語](README.ja.md) / 简体中文

![展示不同余额、平稳资源变化和状态颜色的日常虚构演示](media/demo.png)

*虚构数据呈现的日常使用示例：不同账号余额、平缓的资源变化以及正常/注意状态，使用当前Windows UI和圆角窗口外框渲染。*

## 开始使用

1. Windows 完整解压后运行 `CodexSwitch.exe`。Mac 运行 `Install.command` 安装，再打开 **Codex Switch.app**。[构建与安装](INSTALL.md)
2. 点击标题栏的 **+** 添加账号。Windows 可右键点击 **↻** 导入当前登录。
3. 比较剩余额度并点击 **Switch**。每60秒自动刷新，也可点击 **↻** 或按 **F5** 立即刷新。

请保留完整的程序包。运行包无需安装 .NET；当前源码构建需要 .NET10.0.4xx SDK 和已安装的 Codex 应用/CLI。源码1.30.2使用 .NET10 LTS/runtime10.0.13，已评估的Mac支持基线为macOS15及以上。公开下载1.27.6在单独发布前保持不变。

## 一眼查看

- 剩余额度、重置倒计时、套餐、订阅结束日期、重置次数与积分余额
- 使用记录和基于记录的耗尽预测。账号的 **...** 菜单中可打开 **History**。
- 本地运行/队列/目标数量、资源趋势和磁盘剩余空间。悬停在仪表上可查看详情。
- **1.29.0 源码：** 在资源和磁盘仪表右侧显示温度，并独立应用正常、注意和高温颜色。优先使用设备报告的阈值，否则使用文档说明的显示默认值。不可用时显示 `—°C`；CPU/RAM 可能需要兼容的传感器提供程序。[温度支持范围](TEMPERATURES.md)
- **1.29.1 源码：** Windows 正常关闭后等待子进程退出，在同一次手动 Switch 操作中继续应用账号、重启和恢复聊天。当前下载尚未包含此修复。[切换流程](SWITCHING.md)
- **1.29.2 源码：** 到期日期和倒计时在3天（72小时）以内、重置在2天（48小时）以内时使用主题警告色，并每秒更新。[界面对比及支持范围](evidence/date-warnings/README.md)
- **1.29.3 源码：** 自动刷新始终以60秒为间隔运行。已移除 Windows 底部开关和说明文字，以及 Mac 设置项；以前的关闭设置不会再阻止刷新。[界面对比及验证范围](evidence/always-refresh/README.md)

1.28.0 源码支持含简体/繁体中文在内的9种 UI 语言和 JSON 语言包。目前公开下载的1.27.6仍为英语/韩语 UI。可在标题栏选择语言和浅色/深色主题。[语言包与翻译贡献](LOCALIZATION.md)

切换前保存并验证带日期的本地主聊天名称，备份当前登录。**切换会重启 Codex**，重新打开相同聊天 ID、目录、模型和权限，并向符合条件的空闲聊天发送一次 **Continue**，自动继续未完成的工作。已运行或完成的工作不重复启动。中断的交接可用 **Resume chats** 恢复；发送结果不明确时不会自动重发。名称保存或正常退出失败会阻止登录变更，也可能影响使用相同登录的 CLI/IDE。[切换详情](SWITCHING.md)

Linux 支持正在规划中，尚无执行包。[支持计划](PLATFORM_SUPPORT_PLAN.md) / [Issue #1](https://github.com/Gaebabi/codex-switch/issues/1)。欢迎不同语言、环境和无障碍相关的贡献。

[Mac](MACOS.md) / [预测](FORECAST.md) / [安全](../SECURITY.md) / [开发](../CONTRIBUTING.md)

公开开源。[Windows / Mac 执行包](https://github.com/Gaebabi/codex-switch/releases/tag/v1.27.6)无需源码构建或安装 .NET。Mac 包未公证。本项目不是OpenAI官方产品。认证信息在本地加密保存。[MIT](../LICENSE)
