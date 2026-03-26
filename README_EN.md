# VoiceCtrl

**English** | [中文](README.md)

VoiceCtrl for Windows: Double-click `Ctrl` to start recording, then double-click `Ctrl` again to stop recording and transcribe; the result can be automatically pasted into your active application or simply copied to your clipboard.

> **Acknowledgements**: This project is deeply inspired by the open-source project [marswaveai/TypeNo](https://github.com/marswaveai/TypeNo) and successfully ports its core macOS interaction logic (double-clicking the modifier key to control dictation) to the Windows platform.

Core mechanics retained from the original:
- Global trigger: Double-click `Ctrl`
- Local recording: 16kHz / Mono / wav
- Local transcription: `coli asr <audio-file>`
- Output: Supports automatic paste or copying to clipboard only

There are two editions available for Windows:

- 🚀 **VoiceCtrl_Electron (Recommended)**: Built with Electron, features a modern UI and **comes with the ASR engine pre-integrated. No Node.js or coli installation required.**
- `VoiceCtrl`: C# / WinForms edition (Classic, lightweight, requires manual `coli` setup).

## 📥 Download

Download the latest version from the **[Releases Page](https://github.com/hension-code/VoiceCtrl/releases)**. There are three types of packages available:

| Filename Prefix | Description | Dependencies |
| :--- | :--- | :--- |
| **`VoiceCtrl-Electron` (Recommended)** | Ready to use with a modern UI. | **Pre-bundled** ASR. No extra setup. |
| **`VoiceCtrl_Installer`** | Standard installer for C# edition. | Requires manual `coli` setup. |
| **`VoiceCtrl_Portable`** | Single-file portable edition for C#. | Requires manual `coli` setup. |

### ⚠️ First Run Note (Electron Edition Only)
If you are using the **Electron version**, the app will automatically download the speech model (~40MB) from HuggingFace/ModelScope upon the first transcription attempt. Please ensure a stable network connection.

> [!TIP]
> **About Security (SmartScreen)**: As the app is not code-signed, Windows might block it. Click "More info" -> "Run anyway" to proceed.

## Developer ASR Installation (Optional)

If you are using the C# version or want to use ASR via CLI, install it via (requires Node.js):

```powershell
npm i -g @marswave/coli
```

## Features & Usage

- Double-click `Ctrl` once: Start recording (triggers a system sound and updates tray-icon text).
- Double-click `Ctrl` again: Stop the recording and start local automated transcription.
- Tray Right-Click Context Menu: Manage sound cues, toggle auto-pasting behavior, or even manually choose a past `.wav` file you'd like to explicitly transcribe onto the clipboard.

> 🛡️ **5-Minute Safety Cutoff Guard**  
> Because it relies on global keyboard shortcuts, users might occasionally **forget to stop recording**. To counter this, VoiceCtrl for Windows features an exclusive safety fail-safe mechanism: if a single recording exceeds 5 minutes, in order to protect your drive from being flooded by massive audio queues and guard the transcription engine against out-of-memory (OOM) crashes, the system will **automatically kill and discard** the recording session.

---

## Developer Directives (Build from Source)

If you're eager to build/modify the code on your end, ensure you've installed [.NET 8 SDK](https://dotnet.microsoft.com/download), then you can do the following:

```powershell
# C# edition: run quietly in the tray
cd VoiceCtrl
dotnet run -c Release

# C# edition: create a self-contained distribution
cd ..\scripts
.\build.ps1
```

### Electron Edition (Dev & Build)

```powershell
cd VoiceCtrl_Electron
npm install

# Development run
npm start

# Build (Windows portable)
npm run build
```
