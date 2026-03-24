# VoiceCtrl

**English** | [中文](README.md)

VoiceCtrl for Windows: Double-click `Ctrl` to start recording, then double-click `Ctrl` again to stop recording and transcribe; the result can be automatically pasted into your active application or simply copied to your clipboard.

> **Acknowledgements**: This project is deeply inspired by the open-source project [marswaveai/TypeNo](https://github.com/marswaveai/TypeNo) and successfully ports its core macOS interaction logic (double-clicking the modifier key to control dictation) to the Windows platform.

Core mechanics retained from the original:
- Global trigger: Double-click `Ctrl`
- Local recording: 16kHz / Mono / wav
- Local transcription: `coli asr <audio-file>`
- Output: Supports automatic paste or copying to clipboard only

## Method 1: Direct Download

For most users, the easiest way is to download the latest executable version directly:

- Follow the link to the [Releases Page](https://github.com/hension-code/VoiceCtrl/releases)
- Download the latest `.exe` package for Windows based on your preference:
  - **Installer Version (Recommended)**: Download `VoiceCtrl_Installer_v1.0.exe` for an assisted wizard installation (supports running on startup).
  - **Portable Version**: Download `VoiceCtrl_Portable_v1.0.exe` to skip installation entirely. Just place it anywhere and run it.

### If Windows Prompts "Windows protected your PC" (SmartScreen Block)

Because the current release is not backed by an expensive Windows Code Signing Certificate, Windows Defender SmartScreen might block the application upon its first launch.

To seamlessly bypass it, please perform the following:

1. On the blue block-screen popup, click the text **"More info"**.
2. Afterward, a hidden button named **"Run anyway"** will appear on the bottom right. Click it.

Subsequent launches will be unhindered natively without any further prompt. VoiceCtrl might support formal Microsoft Code Signage in future releases.

## Install Speech Recognition Engine

VoiceCtrl relies heavily on the `coli` client library to perform accurate local speech transcription. You'll first need to install [Node.js](https://nodejs.org/), then issue the following within any terminal (PowerShell or CMD):

```powershell
npm i -g @marswave/coli
```

## Features & Usage

- Double-click `Ctrl` once: Start recording (triggers a system sound and updates tray-icon text).
- Double-click `Ctrl` again: Stop the recording and start local automated transcription.
- 5-Minute Safety Cutoff: In case the recording isn't stopped properly, the system automatically abandons the buffer 5 minutes later to save SSD storage from exhausting itself. 
- Tray Right-Click Context Menu: Manage sound cues, toggle auto-pasting behavior, or even manually choose a past `.wav` file you'd like to explicitly transcribe onto the clipboard.

---

## Developer Directives (Build from Source)

If you're eager to build/modify the code on your end, ensure you've installed [.NET 8 SDK](https://dotnet.microsoft.com/download), then you can do the following:

```powershell
# Quietly run natively in the background tray:
cd Windows\VoiceCtrl
dotnet run -c Release

# Create a self-contained portable distribution natively:
cd Windows\scripts
.\build.ps1
```
