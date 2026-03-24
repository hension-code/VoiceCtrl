using System.Diagnostics;
using System.Drawing;
using System.Media;
using System.Windows.Forms;
using VoiceCtrl.Interop;
using VoiceCtrl.Services;

namespace VoiceCtrl;

internal sealed class VoiceCtrlApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly GlobalKeyboardHook _hotkey;
    private readonly AudioRecorderService _audioRecorder = new();
    private readonly ColiAsrService _asr = new();
    private readonly AppSettingsService _settingsService = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly System.Windows.Forms.Timer _cutoffTimer = new();

    private static bool IsZh => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);

    private readonly AppSettings _settings;
    private ToolStripMenuItem? _recordMenuItem;
    private ToolStripMenuItem? _autoPasteMenuItem;
    private ToolStripMenuItem? _soundCueMenuItem;

    private bool _isRecording;
    private bool _isTranscribing;
    private string? _recordingFile;
    private nint _previousWindow = nint.Zero;

    public VoiceCtrlApplicationContext()
    {
        _settings = _settingsService.Load();

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = true,
            Text = IsZh ? "VoiceCtrl - 空闲" : "VoiceCtrl - Idle",
            ContextMenuStrip = BuildMenu()
        };

        _hotkey = new GlobalKeyboardHook();
        _hotkey.ToggleRequested += OnToggleRequested;
        _hotkey.Start();

        _cutoffTimer.Interval = 5 * 60 * 1000;
        _cutoffTimer.Tick += OnCutoffTimerTick;

        ApplyMenuChecks();
        UpdateUiState(IsZh ? "VoiceCtrl - 空闲" : "VoiceCtrl - Idle");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cutoffTimer.Dispose();
            _hotkey.ToggleRequested -= OnToggleRequested;
            _hotkey.Dispose();
            _audioRecorder.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _gate.Dispose();
        }

        base.Dispose(disposing);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        _recordMenuItem = new ToolStripMenuItem(IsZh ? "开始录音" : "Start Recording");
        _recordMenuItem.Click += async (_, _) => await ToggleAsync();
        menu.Items.Add(_recordMenuItem);

        var transcribeFileItem = new ToolStripMenuItem(IsZh ? "转写本地文件..." : "Transcribe File...");
        transcribeFileItem.Click += async (_, _) => await TranscribeFileAsync();
        menu.Items.Add(transcribeFileItem);

        menu.Items.Add(new ToolStripSeparator());

        _autoPasteMenuItem = new ToolStripMenuItem(IsZh ? "自动粘贴" : "Auto Paste")
        {
            CheckOnClick = true
        };
        _autoPasteMenuItem.Click += (_, _) =>
        {
            _settings.AutoPaste = _autoPasteMenuItem.Checked;
            _settingsService.Save(_settings);
        };
        menu.Items.Add(_autoPasteMenuItem);

        _soundCueMenuItem = new ToolStripMenuItem(IsZh ? "提示音" : "Sound Cue")
        {
            CheckOnClick = true
        };
        _soundCueMenuItem.Click += (_, _) =>
        {
            _settings.SoundCueEnabled = _soundCueMenuItem.Checked;
            _settingsService.Save(_settings);
        };
        menu.Items.Add(_soundCueMenuItem);

        menu.Items.Add(new ToolStripSeparator());

        var openPrivacy = new ToolStripMenuItem(IsZh ? "打开麦克风隐私设置" : "Open Microphone Privacy Settings");
        openPrivacy.Click += (_, _) =>
        {
            Process.Start(new ProcessStartInfo("ms-settings:privacy-microphone") { UseShellExecute = true });
        };
        menu.Items.Add(openPrivacy);

        menu.Items.Add(new ToolStripSeparator());

        var quitItem = new ToolStripMenuItem(IsZh ? "退出" : "Quit");
        quitItem.Click += (_, _) => ExitThread();
        menu.Items.Add(quitItem);

        return menu;
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            using var stream = typeof(VoiceCtrlApplicationContext).Assembly.GetManifestResourceStream("VoiceCtrl.VoiceCtrl.ico");
            if (stream != null)
            {
                return new Icon(stream);
            }
        }
        catch { }
        return SystemIcons.Application;
    }

    private async void OnToggleRequested()
    {
        await ToggleAsync();
    }

    private async void OnCutoffTimerTick(object? sender, EventArgs e)
    {
        _cutoffTimer.Stop();
        if (_isRecording)
        {
            ShowInfo("VoiceCtrl", IsZh ? "已达5分钟最大时长，已自动抛弃本次录音" : "Max 5-minute duration reached, recording discarded automatically");
            await AbortRecordingAsync();
        }
    }

    private async Task AbortRecordingAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!_isRecording)
            {
                return;
            }

            _cutoffTimer.Stop();
            _audioRecorder.Stop();
            _isRecording = false;
            CleanupRecordingFile();
            PlayCue(Cue.Stop);
            UpdateUiState(IsZh ? "VoiceCtrl - 空闲" : "VoiceCtrl - Idle");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ToggleAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_isTranscribing)
            {
                return;
            }

            if (_isRecording)
            {
                await StopAndTranscribeAsync();
            }
            else
            {
                StartRecording();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void StartRecording()
    {
        try
        {
            _previousWindow = NativeMethods.GetForegroundWindow();
            _recordingFile = Path.Combine(Path.GetTempPath(), "VoiceCtrl", $"{Guid.NewGuid():N}.wav");
            _audioRecorder.Start(_recordingFile);
            _isRecording = true;
            _cutoffTimer.Start();
            UpdateUiState(IsZh ? "VoiceCtrl - 录音中" : "VoiceCtrl - Recording");
            PlayCue(Cue.Start);
            ShowInfo("VoiceCtrl", IsZh ? "录音已开始" : "Recording started");
        }
        catch (Exception ex)
        {
            _cutoffTimer.Stop();
            _isRecording = false;
            _recordingFile = null;
            UpdateUiState(IsZh ? "VoiceCtrl - 错误" : "VoiceCtrl - Error");
            ShowInfo(IsZh ? "VoiceCtrl 错误" : "VoiceCtrl Error", ex.Message);
        }
    }

    private async Task StopAndTranscribeAsync()
    {
        try
        {
            _cutoffTimer.Stop();
            _audioRecorder.Stop();
            _isRecording = false;
            _isTranscribing = true;
            UpdateUiState(IsZh ? "VoiceCtrl - 转写中" : "VoiceCtrl - Transcribing");
            PlayCue(Cue.Stop);

            if (string.IsNullOrWhiteSpace(_recordingFile) || !File.Exists(_recordingFile))
            {
                throw new InvalidOperationException("No recording file found.");
            }

            var text = await _asr.TranscribeAsync(_recordingFile);
            await ProcessTranscriptAsync(text);
        }
        catch (Exception ex)
        {
            ShowInfo(IsZh ? "VoiceCtrl 错误" : "VoiceCtrl Error", ex.Message);
        }
        finally
        {
            CleanupRecordingFile();
            _isTranscribing = false;
            _previousWindow = nint.Zero;
            UpdateUiState(IsZh ? "VoiceCtrl - 空闲" : "VoiceCtrl - Idle");
        }
    }

    private async Task TranscribeFileAsync()
    {
        if (_isRecording || _isTranscribing)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = IsZh ? "音频文件 (*.wav;*.mp3;*.m4a;*.aac)|*.wav;*.mp3;*.m4a;*.aac" : "Audio Files (*.wav;*.mp3;*.m4a;*.aac)|*.wav;*.mp3;*.m4a;*.aac",
            Multiselect = false,
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            _isTranscribing = true;
            _previousWindow = NativeMethods.GetForegroundWindow();
            UpdateUiState(IsZh ? "VoiceCtrl - 转写中" : "VoiceCtrl - Transcribing");

            var text = await _asr.TranscribeAsync(dialog.FileName);
            await ProcessTranscriptAsync(text);
        }
        catch (Exception ex)
        {
            ShowInfo(IsZh ? "VoiceCtrl 错误" : "VoiceCtrl Error", ex.Message);
        }
        finally
        {
            _isTranscribing = false;
            _previousWindow = nint.Zero;
            UpdateUiState(IsZh ? "VoiceCtrl - 空闲" : "VoiceCtrl - Idle");
            _gate.Release();
        }
    }

    private async Task ProcessTranscriptAsync(string text)
    {
        var transcript = text.Trim();
        if (string.IsNullOrWhiteSpace(transcript))
        {
            ShowInfo("VoiceCtrl", IsZh ? "未检测到语音" : "No speech detected");
            return;
        }

        if (_settings.AutoPaste)
        {
            await ClipboardPasteService.CopyAndPasteAsync(transcript, _previousWindow);
            ShowInfo("VoiceCtrl", IsZh ? "转写结果已粘贴" : "Inserted transcript");
        }
        else
        {
            ClipboardPasteService.CopyOnly(transcript);
            ShowInfo("VoiceCtrl", IsZh ? "转写结果已复制到剪贴板" : "Transcript copied");
        }
    }

    private void CleanupRecordingFile()
    {
        if (string.IsNullOrWhiteSpace(_recordingFile))
        {
            return;
        }

        try
        {
            if (File.Exists(_recordingFile))
            {
                File.Delete(_recordingFile);
            }
        }
        catch
        {
            // Ignore temporary file cleanup errors.
        }
        finally
        {
            _recordingFile = null;
        }
    }

    private void ApplyMenuChecks()
    {
        if (_autoPasteMenuItem is not null)
        {
            _autoPasteMenuItem.Checked = _settings.AutoPaste;
        }

        if (_soundCueMenuItem is not null)
        {
            _soundCueMenuItem.Checked = _settings.SoundCueEnabled;
        }

    }

    private void UpdateUiState(string text)
    {
        _notifyIcon.Text = text;
        if (_recordMenuItem is not null)
        {
            _recordMenuItem.Text = _isRecording 
                ? (IsZh ? "停止录音 (双击 Ctrl)" : "Stop Recording (Double Ctrl)") 
                : (IsZh ? "开始录音 (双击 Ctrl)" : "Start Recording (Double Ctrl)");
        }
    }

    private void ShowInfo(string title, string message)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(1600);
    }

    private void PlayCue(Cue cue)
    {
        if (!_settings.SoundCueEnabled)
        {
            return;
        }

        try
        {
            switch (cue)
            {
                case Cue.Start:
                    SystemSounds.Asterisk.Play();
                    break;
                case Cue.Stop:
                    SystemSounds.Beep.Play();
                    break;
            }
        }
        catch
        {
            // Ignore audio cue failures.
        }
    }

    private enum Cue
    {
        Start,
        Stop
    }
}
