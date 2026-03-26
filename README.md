# VoiceCtrl

[English](README_EN.md) | **中文**

VoiceCtrl Windows 版本：双击 `Ctrl` 开始录音，再双击 `Ctrl` 结束录音并转写；结果可自动粘贴或仅复制。

> **鸣谢 / Acknowledgements**: 本项目深度受到开源项目 [marswaveai/TypeNo](https://github.com/marswaveai/TypeNo) 启发，并将其出色的 macOS 核心交互模式（双击修饰键控制录音转写）完美移植到了 Windows 平台。

核心机制保留：
- 全局触发：双击 `Ctrl`
- 本地录音：16kHz / 单声道 / wav
- 本地转写：`coli asr <audio-file>`
- 输出：支持自动粘贴或仅复制到剪贴板

当前仓库包含两个 Windows 实现版本：

- 🚀 **VoiceCtrl_Electron (推荐)**：基于 Electron 构建，界面更现代化，**已集成语音识别引擎，无需额外安装 Node.js 或 coli**。
- `VoiceCtrl`：C# / WinForms 版本（经典、轻量，需手动配置 coli 引擎）。

## 📥 下载与安装

请前往 **[Releases 页面](https://github.com/hension-code/VoiceCtrl/releases)** 下载最新版本。发布的安装包主要有以下三种，请按需选择：

| 文件名前缀 | 说明 | 环境依赖 |
| :--- | :--- | :--- |
| **`VoiceCtrl-Electron` (推荐)** | 开箱即用，界面更现代。 | **内置** ASR 环境，无需额外安装。 |
| **`VoiceCtrl_Installer`** | C# 版本的标准安装包。 | 需手动配置 `coli` ASR 引擎。 |
| **`VoiceCtrl_Portable`** | C# 版本的免安装便携版。 | 需手动配置 `coli` ASR 引擎。 |

### ⚠️ 首次运行说明
无论你选择哪个版本，在第一次执行转写时，程序都会自动从网络（HuggingFace/ModelScope）下载语音识别模型（约 40MB）。若网络环境不佳可能导致下载失败，请确保网络畅通。

> [!TIP]
> **关于安全拦截 (SmartScreen)**：由于应用未经过代码签名，下载运行后可能被系统拦截。请点击“更多信息” -> “仍要运行”即可正常使用。

## 开发者安装 ASR 引擎 (可选)

如果你使用的是 C# 或想要在命令行使用 ASR，可以使用以下方式安装（需已安装 Node.js）：

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
# C# 版本：直接构建并静默运行在托盘中：
cd VoiceCtrl
dotnet run -c Release

# C# 版本：打包发布全盘免环境(Self-Contained)版：
cd ..\scripts
.\build.ps1
```

### Electron 版本（开发与打包）

```powershell
cd VoiceCtrl_Electron
npm install

# 开发运行
npm start

# 打包（Windows 便携版）
npm run build
```
