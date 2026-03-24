using System.Diagnostics;
using System.Runtime.InteropServices;
using VoiceCtrl.Interop;

namespace VoiceCtrl.Services;

internal sealed class GlobalKeyboardHook : IDisposable
{
    private const double SingleTapMaxMs = 550;
    private const double DoubleTapWindowMs = 1200;

    private readonly NativeMethods.HookProc _proc;
    private nint _hookId = nint.Zero;

    private long _ctrlDownAt;
    private bool _ctrlSessionActive;
    private bool _otherKeyPressedDuringSession;
    private long _lastValidCtrlTapAt;
    private bool _waitingSecondTap;

    public event Action? ToggleRequested;

    public GlobalKeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hookId != nint.Zero)
        {
            return;
        }

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleHandle = NativeMethods.GetModuleHandle(module?.ModuleName);
        _hookId = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, _proc, moduleHandle, 0);
        if (_hookId == nint.Zero)
        {
            throw new InvalidOperationException("Failed to install keyboard hook.");
        }
    }

    public void Dispose()
    {
        if (_hookId != nint.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = nint.Zero;
        }
        GC.SuppressFinalize(this);
    }

    private nint HookCallback(int nCode, nuint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var msg = unchecked((int)wParam);
            var data = Marshal.PtrToStructure<NativeMethods.Kbdllhookstruct>(lParam);
            var vk = unchecked((int)data.vkCode);

            switch (msg)
            {
                case NativeMethods.WmKeyDown:
                case NativeMethods.WmSysKeyDown:
                    OnKeyDown(vk);
                    break;
                case NativeMethods.WmKeyUp:
                case NativeMethods.WmSysKeyUp:
                    OnKeyUp(vk);
                    break;
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void OnKeyDown(int vk)
    {
        if (IsControlKey(vk))
        {
            if (!_ctrlSessionActive)
            {
                _ctrlSessionActive = true;
                _ctrlDownAt = Stopwatch.GetTimestamp();
                _otherKeyPressedDuringSession = false;
            }
            return;
        }

        if (_ctrlSessionActive)
        {
            _otherKeyPressedDuringSession = true;
        }

        // If user typed anything else while waiting second tap, reset this round.
        if (_waitingSecondTap)
        {
            _lastValidCtrlTapAt = 0;
            _waitingSecondTap = false;
        }
    }

    private void OnKeyUp(int vk)
    {
        if (!IsControlKey(vk) || !_ctrlSessionActive)
        {
            return;
        }

        // Wait until both Ctrl keys are released to finish one tap.
        var lCtrlDown = vk is NativeMethods.VkLControl or NativeMethods.VkControl ? false : IsKeyPressed(NativeMethods.VkLControl);
        var rCtrlDown = vk is NativeMethods.VkRControl or NativeMethods.VkControl ? false : IsKeyPressed(NativeMethods.VkRControl);

        if (lCtrlDown || rCtrlDown)
        {
            return;
        }

        var downAt = _ctrlDownAt;
        _ctrlSessionActive = false;
        _ctrlDownAt = 0;

        if (downAt == 0)
        {
            _otherKeyPressedDuringSession = false;
            return;
        }

        var hold = Stopwatch.GetElapsedTime(downAt).TotalMilliseconds;
        var cleanTap = hold <= SingleTapMaxMs
            && !_otherKeyPressedDuringSession
            && !HasOtherModifiersPressed();
        _otherKeyPressedDuringSession = false;

        if (!cleanTap)
        {
            _lastValidCtrlTapAt = 0;
            _waitingSecondTap = false;
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (_waitingSecondTap && _lastValidCtrlTapAt != 0)
        {
            var sinceLastTap = Stopwatch.GetElapsedTime(_lastValidCtrlTapAt).TotalMilliseconds;
            if (sinceLastTap <= DoubleTapWindowMs)
            {
                _lastValidCtrlTapAt = 0;
                _waitingSecondTap = false;
                ToggleRequested?.Invoke();
                return;
            }
        }

        _lastValidCtrlTapAt = now;
        _waitingSecondTap = true;
    }

    private static bool IsControlKey(int vk)
    {
        return vk is NativeMethods.VkControl or NativeMethods.VkLControl or NativeMethods.VkRControl;
    }

    private static bool HasOtherModifiersPressed()
    {
        return IsKeyPressed(NativeMethods.VkShift)
            || IsKeyPressed(NativeMethods.VkLShift)
            || IsKeyPressed(NativeMethods.VkRShift)
            || IsKeyPressed(NativeMethods.VkMenu)
            || IsKeyPressed(NativeMethods.VkLMenu)
            || IsKeyPressed(NativeMethods.VkRMenu)
            || IsKeyPressed(NativeMethods.VkLWin)
            || IsKeyPressed(NativeMethods.VkRWin);
    }

    private static bool IsKeyPressed(int vk)
    {
        return (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
    }
}
