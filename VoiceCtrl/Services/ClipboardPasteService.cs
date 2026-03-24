using System.Runtime.InteropServices;
using System.Windows.Forms;
using VoiceCtrl.Interop;

namespace VoiceCtrl.Services;

internal static class ClipboardPasteService
{
    public static void CopyOnly(string text)
    {
        SetClipboardWithRetry(text);
    }

    public static async Task CopyAndPasteAsync(string text, nint targetWindow)
    {
        SetClipboardWithRetry(text);

        if (targetWindow != nint.Zero)
        {
            NativeMethods.SetForegroundWindow(targetWindow);
            await Task.Delay(120);
        }

        var ok = TrySendCtrlV();
        if (!ok)
        {
            try
            {
                // Fallback path for environments where SendInput is blocked.
                SendKeys.SendWait("^v");
                ok = true;
            }
            catch
            {
                // Keep false and raise a detailed error below.
            }
        }

        if (!ok)
        {
            var last = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"Failed to send paste key sequence (Win32={last}). " +
                "If target app is running as Administrator, run VoiceCtrl with the same privilege level.");
        }
    }

    private static void SetClipboardWithRetry(string text)
    {
        Exception? lastError = null;
        for (var i = 0; i < 6; i++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch (ExternalException ex)
            {
                lastError = ex;
                Thread.Sleep(50);
            }
        }

        throw new InvalidOperationException("Unable to access clipboard.", lastError);
    }

    private static bool TrySendCtrlV()
    {
        var inputs = new[]
        {
            KeyDown((ushort)Keys.ControlKey),
            KeyDown((ushort)Keys.V),
            KeyUp((ushort)Keys.V),
            KeyUp((ushort)Keys.ControlKey)
        };

        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
        return sent == (uint)inputs.Length;
    }

    private static NativeMethods.Input KeyDown(ushort vk)
    {
        return new NativeMethods.Input
        {
            type = NativeMethods.InputKeyboard,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KeyboardInput
                {
                    wVk = vk,
                    dwFlags = 0
                }
            }
        };
    }

    private static NativeMethods.Input KeyUp(ushort vk)
    {
        return new NativeMethods.Input
        {
            type = NativeMethods.InputKeyboard,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KeyboardInput
                {
                    wVk = vk,
                    dwFlags = NativeMethods.KeyeventfKeyup
                }
            }
        };
    }
}
