using System;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace MOROVelocityX.Services;

public class GlobalHotkeyService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private IntPtr _windowHandle;
    private string? _registeredKey;
    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _hookProc;
    private bool _isWindows;

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    public event EventHandler<string>? HotkeyPressed;

    public GlobalHotkeyService()
    {
        _isWindows = OperatingSystem.IsWindows();
    }

    public void Initialize(IntPtr windowHandle)
    {
        if (!_isWindows)
            return;

        _windowHandle = windowHandle;
        StartKeyMonitoring();
    }

    private void StartKeyMonitoring()
    {
        if (!_isWindows)
            return;

        try
        {
            _hookProc = HookCallback;
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);
        }
        catch
        {
            // Ignore errors on non-Windows systems
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)0x0100) // WM_KEYDOWN
        {
            int vkCode = Marshal.ReadInt32(lParam);
            string keyName = VirtualKeyToString(vkCode);
            
            if (!string.IsNullOrEmpty(_registeredKey) && keyName == _registeredKey)
            {
                HotkeyPressed?.Invoke(this, _registeredKey);
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void RegisterHotkey(string key)
    {
        _registeredKey = key;
    }

    public void UnregisterHotkey()
    {
        _registeredKey = null;
    }

    private string VirtualKeyToString(int vkCode)
    {
        return vkCode switch
        {
            0x70 => "F1",
            0x71 => "F2",
            0x72 => "F3",
            0x73 => "F4",
            0x74 => "F5",
            0x75 => "F6",
            0x76 => "F7",
            0x77 => "F8",
            0x78 => "F9",
            0x79 => "F10",
            0x7A => "F11",
            0x7B => "F12",
            0x41 => "A",
            0x42 => "B",
            0x43 => "C",
            0x44 => "D",
            0x45 => "E",
            0x46 => "F",
            0x47 => "G",
            0x48 => "H",
            0x49 => "I",
            0x4A => "J",
            0x4B => "K",
            0x4C => "L",
            0x4D => "M",
            0x4E => "N",
            0x4F => "O",
            0x50 => "P",
            0x51 => "Q",
            0x52 => "R",
            0x53 => "S",
            0x54 => "T",
            0x55 => "U",
            0x56 => "V",
            0x57 => "W",
            0x58 => "X",
            0x59 => "Y",
            0x5A => "Z",
            0x30 => "0",
            0x31 => "1",
            0x32 => "2",
            0x33 => "3",
            0x34 => "4",
            0x35 => "5",
            0x36 => "6",
            0x37 => "7",
            0x38 => "8",
            0x39 => "9",
            _ => vkCode.ToString()
        };
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }
}
