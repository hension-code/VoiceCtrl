using System.ComponentModel;
using System.Windows.Forms;
using VoiceCtrl.Interop;

namespace VoiceCtrl.Services;

internal enum HotkeyMode
{
    CtrlSpace,
    CtrlShiftSpace
}

internal sealed class GlobalHotkeyService : NativeWindow, IDisposable
{
    private const int HotkeyId = 0x544E; // "TN"
    private bool _registered;
    private HotkeyMode _currentMode = HotkeyMode.CtrlSpace;

    public event Action? ToggleRequested;

    public GlobalHotkeyService()
    {
        CreateHandle(new CreateParams());
    }

    public void Register(HotkeyMode mode)
    {
        if (_registered && _currentMode == mode)
        {
            return;
        }

        Unregister();

        var modifiers = mode switch
        {
            HotkeyMode.CtrlSpace => NativeMethods.ModControl,
            HotkeyMode.CtrlShiftSpace => NativeMethods.ModControl | NativeMethods.ModShift,
            _ => NativeMethods.ModControl
        };

        var ok = NativeMethods.RegisterHotKey(Handle, HotkeyId, modifiers, NativeMethods.VkSpace);
        if (!ok)
        {
            throw new Win32Exception($"Failed to register hotkey {ToDisplayText(mode)}. It may already be in use.");
        }

        _registered = true;
        _currentMode = mode;
    }

    public static string ToDisplayText(HotkeyMode mode)
    {
        return mode switch
        {
            HotkeyMode.CtrlSpace => "Ctrl+Space",
            HotkeyMode.CtrlShiftSpace => "Ctrl+Shift+Space",
            _ => "Ctrl+Space"
        };
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
        GC.SuppressFinalize(this);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WmHotkey && m.WParam.ToInt32() == HotkeyId)
        {
            ToggleRequested?.Invoke();
        }

        base.WndProc(ref m);
    }

    private void Unregister()
    {
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            _registered = false;
        }
    }
}
