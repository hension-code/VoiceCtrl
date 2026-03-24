# VoiceCtrl

[English](README_EN.md) | **中文**

VoiceCtrl Windows 版本：双击 `Ctrl` 开始录音，再双击 `Ctrl` 结束录音并转写；结果可自动粘贴或仅复制。

> **鸣谢 / Acknowledgements**: 本项目深度受到开源项目 [marswaveai/TypeNo](https://github.com/marswaveai/TypeNo) 启发，并将其出色的 macOS 核心交互模式（双击修饰键控制录音转写）完美移植到了 Windows 平台。

核心机制保留：
- 全局触发：双击 `Ctrl`
- 本地录音：16kHz / 单声道 / wav
- 本地转写：`coli asr <audio-file>`
- 输出：支持自动粘贴或仅复制到剪贴板

## 依赖

1. Windows 10/11
2. [.NET 8 SDK](https://dotnet.microsoft.com/download)
3. [Node.js LTS](https://nodejs.org/)
4. 安装 coli：

```powershell
npm i -g @marswave/coli
```


## 运行

```powershell
cd Windows\VoiceCtrl
dotnet run -c Release
```

启动后不会弹主窗口，只会显示托盘图标。

## 发布单文件

```powershell
cd Windows\scripts
.\build.ps1
```

产物目录：

`VoiceCtrl\bin\Release\net8.0-windows\win-x64\publish\`

## 使用

- 双击 `Ctrl` 一次：开始录音
- 再双击 `Ctrl`：停止录音并执行转写
- 开始/结束录音会播放系统提示音
- 托盘菜单：
  - Start Recording / 开始录音
  - Transcribe File... / 转写本地文件
  - Auto Paste / 自动粘贴（开关）
  - Sound Cue / 提示音（开关）
  - Open Microphone Privacy Settings / 打开麦克风隐私设置
  - Quit / 退出

## 注意事项

- 首次录音时，Windows 可能提示麦克风权限。
- 若提示 `coli not found`，确认 `npm -g` 全局路径已加入 `PATH`，或重启终端后再试。
- 双击 `Ctrl` 触发要求两次短按在约 1.2 秒内完成，且中间不要按其他键。
- 由于运行环境缺少 `dotnet/node` 时无法本地编译测试，请先安装依赖再运行上述命令。
