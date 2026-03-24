# VoiceCtrl

[English](README_EN.md) | **中文**

VoiceCtrl Windows 版本：双击 `Ctrl` 开始录音，再双击 `Ctrl` 结束录音并转写；结果可自动粘贴或仅复制。

> **鸣谢 / Acknowledgements**: 本项目深度受到开源项目 [marswaveai/TypeNo](https://github.com/marswaveai/TypeNo) 启发，并将其出色的 macOS 核心交互模式（双击修饰键控制录音转写）完美移植到了 Windows 平台。

核心机制保留：
- 全局触发：双击 `Ctrl`
- 本地录音：16kHz / 单声道 / wav
- 本地转写：`coli asr <audio-file>`
- 输出：支持自动粘贴或仅复制到剪贴板

## 方式一：直接下载应用

对大多数用户来说，最简单的方式是直接下载最新版本：

- 前往 [Releases 页面](https://github.com/hension-code/VoiceCtrl/releases)
- 下载最新的 Windows 发布包：
  - **安装版 (推荐)**：下载 `VoiceCtrl_Installer_v1.0.exe` 获得向导式安装，并自带开机启动。
  - **便携版**：下载 `VoiceCtrl_Portable_v1.0.exe`，免安装单文件双击即用。

### 如果 Windows 提示应用安全拦截（SmartScreen）

当前发行版本还没有经过高昂的 Windows 代码证书签名，所以 Windows 系统在下载后可能会拦截应用的首次运行。

请按顺序尝试以下方法：

1. 在拦截弹窗界面点击 **更多信息 (More info)** 收起折叠。
2. 点击随后下方浮现出的 **仍要运行 (Run anyway)** 按钮。

VoiceCtrl 会在后续条件允许时考虑支持正式的微软签名与认证。

## 安装语音识别引擎

VoiceCtrl 使用 coli 进行本地语音识别（请确保已安装 Node.js）：

```powershell
npm i -g @marswave/coli
```

## 功能操作说明

- 双击 `Ctrl` 一次：开始录音（伴随系统清脆提示音，托盘文本刷新）
- 再次双击 `Ctrl`：停止录音并执行自动化转写
- 托盘右键菜单：支持一键开关“自动贴入输入框”、“提示音”，或手动选择过往的录音文件做单独转写。

> 🛡️ **5分钟防呆断电保护机制 (Safety Cutoff)**  
> 考虑到用户在使用全局快捷键时可能**遗忘关闭录制**，VoiceCtrl Windows 独家增设了底层保护逻辑。若单次录音时长突破 5 分钟死线，为了防止海量的背景音频瞬间塞爆内存，并避免拖垮底层转写引擎（ASR OOM 崩溃），系统将会立刻**拉闸废弃**这段噪音记录，保护宿主机硬盘与性能。

---

## 开发者指令（编译构建）

若你要深入源码并自行调试，请预先配置好 [.NET 8 SDK](https://dotnet.microsoft.com/download)。

```powershell
# 直接构建并静默运行在托盘中：
cd Windows\VoiceCtrl
dotnet run -c Release

# 打包发布全盘免环境(Self-Contained)版：
cd Windows\scripts
.\build.ps1
```
