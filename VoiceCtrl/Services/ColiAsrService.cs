using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace VoiceCtrl.Services;

internal sealed class ColiAsrService : IDisposable
{
    private sealed record WorkerResult(string Text, long DurMs);

    private static readonly string WhereExe = Path.Combine(Environment.SystemDirectory, "where.exe");
    private static readonly string CmdExe = Path.Combine(Environment.SystemDirectory, "cmd.exe");
    private static string? _coliExePath;
    private static string? _nodeExePath;

    private readonly SemaphoreSlim _workerStartGate = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<WorkerResult>> _pendingRequests = new();
    private readonly string _workerScriptPath;

    private Process? _workerProcess;
    private StreamWriter? _workerStdin;
    private TaskCompletionSource<bool>? _workerReady;
    private bool _workerDisabled;
    private int _nextRequestId;

    public ColiAsrService()
    {
        _workerScriptPath = ResolveWorkerScriptPath();
    }

    public async Task PreloadAsync()
    {
        if (_workerDisabled)
        {
            return;
        }

        await EnsureWorkerReadyAsync();
    }

    public async Task<string> TranscribeAsync(string audioPath)
    {
        if (!File.Exists(audioPath))
        {
            throw new FileNotFoundException("Audio file does not exist.", audioPath);
        }

        var totalSw = Stopwatch.StartNew();
        if (await EnsureWorkerReadyAsync())
        {
            try
            {
                var workerResult = await TranscribeViaWorkerAsync(audioPath);
                Trace.WriteLine($"[ASR][CSharpWorker] decode={workerResult.DurMs}ms total={totalSw.ElapsedMilliseconds}ms");
                return workerResult.Text.Trim();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ASR][CSharpWorker] failed, fallback to coli CLI: {ex.Message}");
                if (_workerProcess is null || _workerProcess.HasExited)
                {
                    await DisableWorkerAsync(disablePermanently: false);
                }
            }
        }

        var fallbackText = await TranscribeViaColiCliAsync(audioPath);
        Trace.WriteLine($"[ASR][CSharpCLI] total={totalSw.ElapsedMilliseconds}ms");
        return fallbackText.Trim();
    }

    private async Task<string> TranscribeViaColiCliAsync(string audioPath)
    {
        var coliExe = await ResolveColiExeAsync();
        if (string.IsNullOrWhiteSpace(coliExe))
        {
            throw new InvalidOperationException("coli not found. Install with: npm i -g @marswave/coli");
        }

        var result = await RunColiAsync(coliExe, audioPath);
        if (result.ExitCode != 0)
        {
            var err = string.IsNullOrWhiteSpace(result.StdErr) ? "coli transcription failed." : result.StdErr.Trim();
            throw new InvalidOperationException(err);
        }

        return result.StdOut;
    }

    private static string ResolveWorkerScriptPath()
    {
        var outputPath = Path.Combine(AppContext.BaseDirectory, "Assets", "asr-worker.cjs");
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        var fallback = Path.Combine(AppContext.BaseDirectory, "asr-worker.cjs");
        if (File.Exists(fallback))
        {
            return fallback;
        }

        return outputPath;
    }

    private async Task<bool> EnsureWorkerReadyAsync()
    {
        if (_workerDisabled)
        {
            return false;
        }

        if (_workerProcess is { HasExited: false } && _workerStdin is not null)
        {
            return true;
        }

        await _workerStartGate.WaitAsync();
        try
        {
            if (_workerDisabled)
            {
                return false;
            }

            if (_workerProcess is { HasExited: false } && _workerStdin is not null)
            {
                return true;
            }

            var coliExe = await ResolveColiExeAsync();
            if (string.IsNullOrWhiteSpace(coliExe))
            {
                _workerDisabled = true;
                return false;
            }

            var nodeExe = await ResolveNodeExeAsync();
            if (string.IsNullOrWhiteSpace(nodeExe))
            {
                _workerDisabled = true;
                return false;
            }

            if (!File.Exists(_workerScriptPath))
            {
                Trace.WriteLine($"[ASR][CSharpWorker] script not found: {_workerScriptPath}");
                _workerDisabled = true;
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = nodeExe,
                Arguments = $"\"{_workerScriptPath}\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.Environment["COLI_EXE_PATH"] = coliExe;

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            _workerReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            process.OutputDataReceived += OnWorkerStdout;
            process.ErrorDataReceived += OnWorkerStderr;
            process.Exited += OnWorkerExited;

            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start ASR worker process.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _workerProcess = process;
            _workerStdin = process.StandardInput;
            _workerStdin.AutoFlush = true;

            await _workerReady.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Trace.WriteLine("[ASR][CSharpWorker] ready");
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ASR][CSharpWorker] startup failed: {ex.Message}");
            await DisableWorkerAsync(disablePermanently: false);
            return false;
        }
        finally
        {
            _workerStartGate.Release();
        }
    }

    private void OnWorkerStdout(object? sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(e.Data);
            var root = doc.RootElement;

            if (root.TryGetProperty("type", out var typeElement))
            {
                var type = typeElement.GetString();
                if (string.Equals(type, "ready", StringComparison.OrdinalIgnoreCase))
                {
                    _workerReady?.TrySetResult(true);
                    return;
                }

                if (string.Equals(type, "fatal", StringComparison.OrdinalIgnoreCase))
                {
                    var error = root.TryGetProperty("error", out var errElement)
                        ? errElement.GetString() ?? "Worker fatal error"
                        : "Worker fatal error";
                    _workerReady?.TrySetException(new InvalidOperationException(error));
                    return;
                }
            }

            if (!root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt32(out var id))
            {
                return;
            }

            if (!_pendingRequests.TryRemove(id, out var tcs))
            {
                return;
            }

            if (root.TryGetProperty("ok", out var okElement) && okElement.ValueKind == JsonValueKind.True)
            {
                var text = root.TryGetProperty("text", out var textElement) ? textElement.GetString() ?? string.Empty : string.Empty;
                var durMs = root.TryGetProperty("durMs", out var durElement) && durElement.TryGetInt64(out var ms) ? ms : -1;
                tcs.TrySetResult(new WorkerResult(text, durMs));
            }
            else
            {
                var error = root.TryGetProperty("error", out var errElement) ? errElement.GetString() ?? "Worker request failed" : "Worker request failed";
                tcs.TrySetException(new InvalidOperationException(error));
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ASR][CSharpWorker] parse stdout failed: {ex.Message}");
        }
    }

    private static void OnWorkerStderr(object? sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.Data))
        {
            Trace.WriteLine($"[ASR][CSharpWorker] {e.Data}");
        }
    }

    private void OnWorkerExited(object? sender, EventArgs e)
    {
        _workerReady?.TrySetException(new InvalidOperationException("ASR worker exited during startup."));
        FailAllPending(new InvalidOperationException("ASR worker exited."));
        _workerProcess = null;
        _workerStdin = null;
    }

    private async Task<WorkerResult> TranscribeViaWorkerAsync(string audioPath)
    {
        if (_workerProcess is null || _workerProcess.HasExited || _workerStdin is null)
        {
            throw new InvalidOperationException("ASR worker is not running.");
        }

        var id = Interlocked.Increment(ref _nextRequestId);
        var tcs = new TaskCompletionSource<WorkerResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingRequests.TryAdd(id, tcs))
        {
            throw new InvalidOperationException("Failed to queue ASR request.");
        }

        try
        {
            var request = JsonSerializer.Serialize(new { id, op = "transcribe", audioPath });
            await _workerStdin.WriteLineAsync(request);
            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch
        {
            _pendingRequests.TryRemove(id, out _);
            throw;
        }
    }

    private async Task DisableWorkerAsync(bool disablePermanently = true)
    {
        _workerDisabled = disablePermanently;
        var process = _workerProcess;
        _workerProcess = null;

        try
        {
            if (_workerStdin is not null)
            {
                try
                {
                    var request = JsonSerializer.Serialize(new { id = -1, op = "shutdown" });
                    await _workerStdin.WriteLineAsync(request);
                }
                catch
                {
                    // Ignore shutdown write errors.
                }
                _workerStdin.Dispose();
                _workerStdin = null;
            }

            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Ignore cleanup errors.
        }
        finally
        {
            process?.Dispose();
        }

        FailAllPending(new InvalidOperationException("ASR worker disabled."));
    }

    private void FailAllPending(Exception ex)
    {
        foreach (var pair in _pendingRequests)
        {
            if (_pendingRequests.TryRemove(pair.Key, out var tcs))
            {
                tcs.TrySetException(ex);
            }
        }
    }

    private static async Task<string?> ResolveColiExeAsync()
    {
        if (!string.IsNullOrWhiteSpace(_coliExePath) && File.Exists(_coliExePath))
        {
            return _coliExePath;
        }

        foreach (var name in new[] { "coli.cmd", "coli.exe", "coli" })
        {
            var result = await RunProcessAsync(WhereExe, name);
            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StdOut))
            {
                continue;
            }

            var path = result.StdOut
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static x => x.Trim())
                .FirstOrDefault(File.Exists);

            if (!string.IsNullOrWhiteSpace(path))
            {
                _coliExePath = path;
                return path;
            }
        }

        return null;
    }

    private static async Task<string?> ResolveNodeExeAsync()
    {
        if (!string.IsNullOrWhiteSpace(_nodeExePath) && File.Exists(_nodeExePath))
        {
            return _nodeExePath;
        }

        foreach (var name in new[] { "node.exe", "node" })
        {
            var result = await RunProcessAsync(WhereExe, name);
            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StdOut))
            {
                continue;
            }

            var path = result.StdOut
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static x => x.Trim())
                .FirstOrDefault(File.Exists);

            if (!string.IsNullOrWhiteSpace(path))
            {
                _nodeExePath = path;
                return path;
            }
        }

        return null;
    }

    private static Task<(int ExitCode, string StdOut, string StdErr)> RunColiAsync(string coliPath, string audioPath)
    {
        var ext = Path.GetExtension(coliPath).ToLowerInvariant();
        if (ext == ".exe")
        {
            var escapedPath = audioPath.Replace("\"", "\\\"");
            return RunProcessAsync(coliPath, $"asr \"{escapedPath}\"");
        }

        var cmdColi = EscapeForCmd(coliPath);
        var cmdAudio = EscapeForCmd(audioPath);
        var args = $"/d /s /c \"\"{cmdColi}\" asr \"{cmdAudio}\"\"";
        return RunProcessAsync(CmdExe, args);
    }

    private static string EscapeForCmd(string value)
    {
        return value.Replace("\"", "\"\"");
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(string fileName, string arguments)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        var stdoutDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stdoutDone.TrySetResult(true);
                return;
            }
            stdout.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stderrDone.TrySetResult(true);
                return;
            }
            stderr.AppendLine(e.Data);
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {fileName}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();
        await Task.WhenAll(stdoutDone.Task, stderrDone.Task);

        return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    public void Dispose()
    {
        try
        {
            _workerDisabled = true;
            _workerStdin?.Dispose();
            _workerStdin = null;

            if (_workerProcess is { HasExited: false })
            {
                _workerProcess.Kill(entireProcessTree: true);
            }
            _workerProcess?.Dispose();
            _workerProcess = null;

            FailAllPending(new InvalidOperationException("ASR worker disposed."));
        }
        catch
        {
            // Ignore disposal errors.
        }

        _workerStartGate.Dispose();
    }
}
