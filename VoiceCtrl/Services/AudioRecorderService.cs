using NAudio.Wave;

namespace VoiceCtrl.Services;

internal sealed class AudioRecorderService : IDisposable
{
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private bool _isRecording;

    public void Start(string outputPath)
    {
        if (_isRecording)
        {
            throw new InvalidOperationException("Recording is already in progress.");
        }

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 50
        };
        _writer = new WaveFileWriter(outputPath, _waveIn.WaveFormat);

        _waveIn.DataAvailable += (_, args) =>
        {
            _writer?.Write(args.Buffer, 0, args.BytesRecorded);
            _writer?.Flush();
        };

        _waveIn.StartRecording();
        _isRecording = true;
    }

    public void Stop()
    {
        if (!_isRecording)
        {
            return;
        }

        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;

        _writer?.Dispose();
        _writer = null;

        _isRecording = false;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
