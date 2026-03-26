import { app, Tray, Menu, nativeImage, ipcMain, BrowserWindow, clipboard, dialog, shell } from 'electron';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import fs from 'node:fs';
import os from 'node:os';
import { spawn } from 'node:child_process';
import { createRequire } from 'node:module';
import { execa } from 'execa';

// @ts-ignore
import { ensureModels, getModelPath } from '@marswave/coli';

const require = createRequire(import.meta.url);
const __dirname = path.dirname(fileURLToPath(import.meta.url));

let sherpaOnnx;
try {
    sherpaOnnx = require('sherpa-onnx-node');
} catch (err) {
    console.error('Failed to load sherpa-onnx-node:', err.message);
}

let keyboardHotkeyReady = false;
let keyboardShutdownDone = false;
let psCtrlWatcher = null;
let psCtrlStdoutRemainder = '';

let settings = {
    autoPaste: true,
    playSound: true
};

let tray = null;
let recordingWindow = null;
let isRecording = false;
let isTranscribing = false;
let lastCtrlTime = 0;
let lastToggleTime = 0;
let lastPhysicalKeyDownAt = 0;

const ASR_SAMPLE_RATE = 16000;
const ASR_FEATURE_DIM = 80;
const ASR_NUM_THREADS = Math.max(2, Math.min((os.cpus?.().length || 4), 8));
let asrRecognizer = null;
let asrModelDir = null;

function getPowerShellCommandCandidates() {
    const systemRoot = process.env.SystemRoot || process.env.WINDIR || 'C:\\Windows';
    const candidates = [
        path.join(systemRoot, 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe'),
        'powershell.exe',
        'powershell',
        'pwsh.exe',
        'pwsh'
    ];
    return [...new Set(candidates)];
}

function isCommandNotFoundError(err) {
    const msg = `${err?.message || ''}`.toLowerCase();
    return err?.code === 'ENOENT'
        || msg.includes('is not recognized as an internal or external command')
        || msg.includes('the term')
        || msg.includes('not found');
}

async function runPowerShellCommand(script) {
    const candidates = getPowerShellCommandCandidates();
    let lastErr;
    for (const command of candidates) {
        try {
            await execa(command, ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { windowsHide: true });
            return true;
        } catch (err) {
            lastErr = err;
            if (!isCommandNotFoundError(err)) break;
        }
    }
    if (lastErr) throw lastErr;
    return false;
}

function handleCtrlDown() {
    const now = Date.now();
    if (isTranscribing || (now - lastToggleTime < 800)) return;
    if (now - lastPhysicalKeyDownAt < 50) return;
    lastPhysicalKeyDownAt = now;

    if (lastCtrlTime > 0 && now - lastCtrlTime < 400) {
        toggleRecording();
        lastToggleTime = now;
        lastCtrlTime = 0;
    } else {
        lastCtrlTime = now;
    }
}

function setHotkeyState(ready) {
    keyboardHotkeyReady = ready;
    if (tray) updateTrayMenu('Idle');
}

function createPowerShellCtrlWatcherScript() {
    return [
        "$ErrorActionPreference = 'Stop'",
        "$signature = @\"",
        "using System;",
        "using System.Runtime.InteropServices;",
        "public static class KeyStateNative {",
        "  [DllImport(\"user32.dll\")]",
        "  public static extern short GetAsyncKeyState(int vKey);",
        "}",
        "\"@",
        "Add-Type -TypeDefinition $signature -ErrorAction Stop",
        "$lastL = $false",
        "$lastR = $false",
        "while ($true) {",
        "  $l = ([KeyStateNative]::GetAsyncKeyState(0xA2) -band 0x8000) -ne 0",
        "  $r = ([KeyStateNative]::GetAsyncKeyState(0xA3) -band 0x8000) -ne 0",
        "  if (($l -and -not $lastL) -or ($r -and -not $lastR)) {",
        "    [Console]::Out.WriteLine('CTRL_DOWN')",
        "    [Console]::Out.Flush()",
        "  }",
        "  $lastL = $l",
        "  $lastR = $r",
        "  Start-Sleep -Milliseconds 12",
        "}"
    ].join('\n');
}

function processPowerShellHotkeyOutput(chunk) {
    psCtrlStdoutRemainder += chunk.toString();
    const lines = psCtrlStdoutRemainder.split(/\r?\n/);
    psCtrlStdoutRemainder = lines.pop() || '';
    for (const line of lines) {
        if (line.trim() === 'CTRL_DOWN') {
            handleCtrlDown();
        }
    }
}

async function tryStartPowerShellCtrlWatcherWithCommand(command, script) {
    return await new Promise((resolve) => {
        let settled = false;
        const settle = (ok) => {
            if (settled) return;
            settled = true;
            resolve(ok);
        };

        let watcher;
        try {
            watcher = spawn(command, ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], {
                windowsHide: true,
                stdio: ['ignore', 'pipe', 'pipe']
            });
        } catch (err) {
            console.error(`[Keyboard] failed to spawn PowerShell watcher (${command}):`, err?.message || err);
            setHotkeyState(false);
            psCtrlWatcher = null;
            psCtrlStdoutRemainder = '';
            settle(false);
            return;
        }

        psCtrlWatcher = watcher;
        psCtrlStdoutRemainder = '';

        watcher.stdout.on('data', (chunk) => processPowerShellHotkeyOutput(chunk));
        watcher.stderr.on('data', (chunk) => {
            const msg = chunk.toString().trim();
            if (msg) console.error('[Keyboard][PS]', msg);
        });

        watcher.once('error', (err) => {
            console.error(`[Keyboard] PowerShell watcher error (${command}):`, err?.message || err);
            setHotkeyState(false);
            psCtrlWatcher = null;
            psCtrlStdoutRemainder = '';
            settle(false);
        });

        watcher.once('exit', (code, signal) => {
            const msg = `[Keyboard] PowerShell watcher exited (${command}) (code=${code}, signal=${signal})`;
            psCtrlWatcher = null;
            if (!settled) {
                console.error(msg);
                setHotkeyState(false);
                settle(false);
                return;
            }
            if (!keyboardShutdownDone) {
                console.error(msg);
                setHotkeyState(false);
            }
        });

        setTimeout(() => {
            if (watcher.exitCode === null) {
                setHotkeyState(true);
                console.log(`[Keyboard] global hotkey ready (powershell-fallback via ${command})`);
                settle(true);
            } else {
                settle(false);
            }
        }, 800);
    });
}

async function tryStartPowerShellCtrlWatcher() {
    const script = createPowerShellCtrlWatcherScript();
    const candidates = getPowerShellCommandCandidates();
    for (const command of candidates) {
        const ok = await tryStartPowerShellCtrlWatcherWithCommand(command, script);
        if (ok) return true;
    }
    return false;
}

async function startKeyboardListener() {
    const psOk = await tryStartPowerShellCtrlWatcher();
    if (!psOk) {
        console.error('[Keyboard] no hotkey backend available.');
    }
}

function shutdownKeyboardListener() {
    if (keyboardShutdownDone) return;
    keyboardShutdownDone = true;
    if (psCtrlWatcher && psCtrlWatcher.exitCode === null && !psCtrlWatcher.killed) {
        try {
            psCtrlWatcher.kill();
        } catch (err) {
            console.error('[Keyboard] safe shutdown ignored powershell watcher error:', err?.message || err);
        } finally {
            psCtrlWatcher = null;
            psCtrlStdoutRemainder = '';
        }
    }
    setHotkeyState(false);
}

async function initializeApp() {
  console.log('Initializing VoiceCtrl Full Engine (Packaged-Aware)...');
  try {
    await ensureModels(['sensevoice']);
    if (sherpaOnnx) {
      // Warm up recognizer once at startup so first transcription is faster.
      getAsrRecognizer();
    }
  } catch (err) {
    console.error('[ASR] model prepare failed:', err?.message || err);
  }

  recordingWindow = new BrowserWindow({
    show: false,
    webPreferences: {
      nodeIntegration: true,
      contextIsolation: false
    }
  });
  recordingWindow.loadFile('index.html');

  const iconPath = path.join(__dirname, 'icon.ico');
  const icon = fs.existsSync(iconPath) ? nativeImage.createFromPath(iconPath) : nativeImage.createEmpty();
  
  tray = new Tray(icon);
  tray.setToolTip('VoiceCtrl - Windows Port (Electron)');
  await startKeyboardListener();
  updateTrayMenu('Idle');

  if (!keyboardHotkeyReady) {
    console.error('[Keyboard] Hotkey unavailable. Tray menu remains usable.');
  }
  console.log(`VoiceCtrl Ready. Mode: ${app.isPackaged ? 'Packaged' : 'Development'}`);
}

function playSystemCue(type) {
  if (!settings.playSound) return;
  const soundName = type === 'start' ? 'Asterisk' : 'Beep';
  const psCmd = `Add-Type -AssemblyName System.Media; [System.Media.SystemSounds]::${soundName}.Play()`;
  runPowerShellCommand(psCmd).catch(() => {});
}

function toggleRecording() {
    isRecording = !isRecording;
    recordingWindow.webContents.send('toggle-recording', isRecording);
    playSystemCue(isRecording ? 'start' : 'stop');
    updateTrayMenu(isRecording ? 'Recording...' : 'Transcribing...');
}

function updateTrayMenu(status) {
    const hotkeyHint = keyboardHotkeyReady ? '双击 Ctrl' : '快捷键不可用，请用菜单';
    const contextMenu = Menu.buildFromTemplate([
        { label: isRecording ? `停止录音 (${hotkeyHint})` : `开始录音 (${hotkeyHint})`, click: () => toggleRecording() },
        { label: '转写本地文件...', click: () => transcribeLocalFile() },
        { type: 'separator' },
        { 
            label: '自动粘贴', 
            type: 'checkbox', 
            checked: settings.autoPaste, 
            click: (item) => { settings.autoPaste = item.checked; } 
        },
        { 
            label: '提示音', 
            type: 'checkbox', 
            checked: settings.playSound, 
            click: (item) => { settings.playSound = item.checked; } 
        },
        { type: 'separator' },
        { label: '打开麦克风隐私设置', click: () => shell.openExternal('ms-settings:privacy-microphone') },
        { label: '退出', click: () => { 
            shutdownKeyboardListener();
            app.quit();
        }}
    ]);
    tray.setContextMenu(contextMenu);
}

// 核心 ASR 处理
function getAsrRecognizer() {
    if (!sherpaOnnx) {
        throw new Error('sherpa-onnx-node is not available');
    }
    if (asrRecognizer) return asrRecognizer;

    asrModelDir = asrModelDir || getModelPath('sensevoice');
    const t0 = Date.now();
    asrRecognizer = new sherpaOnnx.OfflineRecognizer({
        featConfig: { sampleRate: ASR_SAMPLE_RATE, featureDim: ASR_FEATURE_DIM },
        modelConfig: {
            senseVoice: {
                model: path.join(asrModelDir, 'model.int8.onnx'),
                useInverseTextNormalization: 1,
            },
            tokens: path.join(asrModelDir, 'tokens.txt'),
            numThreads: ASR_NUM_THREADS,
            provider: 'cpu',
            debug: 0,
        },
    });
    console.log(`[ASR] recognizer initialized in ${Date.now() - t0}ms (threads=${ASR_NUM_THREADS})`);
    return asrRecognizer;
}

function wavToFloat32(wavBytes) {
    if (!wavBytes || wavBytes.byteLength < 44) return null;
    const samplesInt16 = new Int16Array(wavBytes.buffer, wavBytes.byteOffset + 44, (wavBytes.byteLength - 44) / 2);
    const samplesFloat32 = new Float32Array(samplesInt16.length);
    for (let i = 0; i < samplesInt16.length; i++) {
        samplesFloat32[i] = samplesInt16[i] / 32768.0;
    }
    return samplesFloat32;
}

async function performAsrFromSamples(samplesFloat32, sampleRate = ASR_SAMPLE_RATE) {
    if (!samplesFloat32 || samplesFloat32.length === 0) return null;
    if (sampleRate !== ASR_SAMPLE_RATE) {
        console.warn(`[ASR] unexpected sampleRate=${sampleRate}, expected=${ASR_SAMPLE_RATE}`);
    }

    const recognizer = getAsrRecognizer();
    const t0 = Date.now();
    const stream = recognizer.createStream();
    stream.acceptWaveform({ sampleRate: ASR_SAMPLE_RATE, samples: samplesFloat32 });
    recognizer.decode(stream);
    const result = recognizer.getResult(stream);
    console.log(`[ASR] decode completed in ${Date.now() - t0}ms (samples=${samplesFloat32.length})`);
    return result.text.trim();
}

async function performAsrFromWavFile(wavFile) {
    const wavData = fs.readFileSync(wavFile);
    const samples = wavToFloat32(wavData);
    if (!samples) return null;
    return await performAsrFromSamples(samples, ASR_SAMPLE_RATE);
}

function simulatePaste() {
    const cmd = `Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.SendKeys]::SendWait('^v')`;
    runPowerShellCommand(cmd).catch(() => {});
}

ipcMain.on('recording-finished', async (event, payload) => {
    isTranscribing = true;
    const startedAt = Date.now();
    let wavFile = null;
    try {
        let text = null;
        if (payload && payload.format === 'f32le' && payload.samples) {
            const sampleRate = Number(payload.sampleRate) || ASR_SAMPLE_RATE;
            const sampleBytes = Buffer.from(payload.samples);
            const alignedBuffer = sampleBytes.buffer.slice(
                sampleBytes.byteOffset,
                sampleBytes.byteOffset + sampleBytes.byteLength
            );
            const samplesFloat32 = new Float32Array(alignedBuffer);
            text = await performAsrFromSamples(samplesFloat32, sampleRate);
        } else {
            // Backward compatibility: older renderer sends WAV bytes.
            wavFile = path.join(os.tmpdir(), `vctrl-fin-${Date.now()}.wav`);
            fs.writeFileSync(wavFile, payload);
            text = await performAsrFromWavFile(wavFile);
        }

        if (text) {
            clipboard.writeText(text);
            if (settings.autoPaste) simulatePaste();
        }
        console.log(`[ASR] total pipeline completed in ${Date.now() - startedAt}ms`);
    } catch (err) {
        console.error('ASR error:', err);
    } finally {
        if (wavFile && fs.existsSync(wavFile)) fs.unlinkSync(wavFile);
        isTranscribing = false;
        updateTrayMenu('Idle');
    }
});

async function transcribeLocalFile() {
    const { canceled, filePaths } = await dialog.showOpenDialog({
        title: '选择音频文件',
        filters: [{ name: 'Audio', extensions: ['wav', 'mp3', 'm4a', 'webm'] }],
        properties: ['openFile']
    });
    if (canceled || filePaths.length === 0) return;
    isTranscribing = true;
    updateTrayMenu('Transcribing manual file...');
    const targetFile = filePaths[0];
    const wavFile = path.join(os.tmpdir(), `vctrl-man-${Date.now()}.wav`);
    try {
        await execa('ffmpeg', ['-i', targetFile, '-ar', '16000', '-ac', '1', '-f', 'wav', '-acodec', 'pcm_s16le', wavFile, '-y']);
        const text = await performAsrFromWavFile(wavFile);
        if (text) {
            clipboard.writeText(text);
            if (settings.autoPaste) simulatePaste();
        }
    } finally {
        if (fs.existsSync(wavFile)) fs.unlinkSync(wavFile);
        isTranscribing = false;
        updateTrayMenu('Idle');
    }
}

app.whenReady().then(initializeApp);
app.on('will-quit', () => { shutdownKeyboardListener(); });
app.on('window-all-closed', () => { app.quit(); });
