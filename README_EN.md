# VoiceCtrl

**English** | [中文](README.md)

VoiceCtrl for Windows: Double-click `Ctrl` to start recording, then double-click `Ctrl` again to stop recording and transcribe; the result can be automatically pasted into your active application or simply copied to your clipboard.

> **Acknowledgements**: This project is deeply inspired by the open-source project [marswaveai/TypeNo](https://github.com/marswaveai/TypeNo) and successfully ports its core macOS interaction logic (double-clicking the modifier key to control dictation) to the Windows platform.

Core mechanics retained from the original:
- Global trigger: Double-click `Ctrl`
- Local recording: 16kHz / Mono / wav
- Local transcription: `coli asr <audio-file>`
- Output: Supports automatic paste or copying to clipboard only

## Dependencies

1. Windows 10/11
2. [.NET 8 SDK](https://dotnet.microsoft.com/download)
3. [Node.js LTS](https://nodejs.org/)
4. Install `coli`:

```powershell
npm i -g @marswave/coli
```


## Running Locally

```powershell
cd Windows\VoiceCtrl
dotnet run -c Release
```

The application runs in the background. Only the tray icon will be displayed.

## Build Single Executable

```powershell
cd Windows\scripts
.\build.ps1
```

Output directory:

`VoiceCtrl\bin\Release\net8.0-windows\win-x64\publish\`

## Usage

- Double-click `Ctrl` once: Start recording
- Double-click `Ctrl` again: Stop recording and execute transcription
- Systems sounds will play cueing the start/stop of the recording
- Tray Menu:
  - Start Recording / 开始录音
  - Transcribe File... / 转写本地文件
  - Auto Paste / 自动粘贴 (Toggle)
  - Sound Cue / 提示音 (Toggle)
  - Open Microphone Privacy Settings / 打开麦克风隐私设置
  - Quit / 退出

## Notes

- On first use, Windows might prompt for microphone permissions.
- If it says `coli not found`, ensure your `npm -g` global path is fully added to your system `PATH`, or restart your terminal.
- The double `Ctrl` tap requires two short presses within about 1.2 seconds, with no other key presses in between.
- Because the local execution environment requires `dotnet` and `node` to compile and run, please install the dependencies before running the application.
