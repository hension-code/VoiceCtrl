using System.Diagnostics;
using System.Text;

namespace VoiceCtrl.Services;

internal sealed class ColiAsrService
{
    private static readonly string WhereExe = Path.Combine(Environment.SystemDirectory, "where.exe");
    private static readonly string CmdExe = Path.Combine(Environment.SystemDirectory, "cmd.exe");
    private static string? _coliExePath;

    public async Task<string> TranscribeAsync(string audioPath)
    {
        if (!File.Exists(audioPath))
        {
            throw new FileNotFoundException("Audio file does not exist.", audioPath);
        }

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

        return result.StdOut.Trim();
    }

    private static async Task<string?> ResolveColiExeAsync()
    {
        if (!string.IsNullOrWhiteSpace(_coliExePath) && File.Exists(_coliExePath))
        {
            return _coliExePath;
        }

        // npm global bin on Windows usually exposes coli.cmd.
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

    private static Task<(int ExitCode, string StdOut, string StdErr)> RunColiAsync(string coliPath, string audioPath)
    {
        var ext = Path.GetExtension(coliPath).ToLowerInvariant();
        if (ext == ".exe")
        {
            var escapedPath = audioPath.Replace("\"", "\\\"");
            return RunProcessAsync(coliPath, $"asr \"{escapedPath}\"");
        }

        // .cmd/.bat or extensionless shim must be launched via cmd.exe
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
}
