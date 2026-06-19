using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MOROVelocityX.Services;

public class GlobalHotkeyService : IDisposable
{
    private IntPtr _windowHandle;
    private string? _registeredKey;
    private readonly HashSet<string> _registeredKeys = new();
    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _hookProc;
    private readonly bool _isWindows;
    private readonly bool _isLinux;

    private FileStream? _linuxEventStream;
    private CancellationTokenSource? _linuxCts;
    private Task? _linuxListenerTask;
    private bool _keyboardGrabbed;
    private static readonly string YdotoolSocket = "/tmp/ydotool.sock";

    public Func<string, bool>? ShouldSuppressKey { get; set; }

    [StructLayout(LayoutKind.Sequential)]
    private struct InputEvent
    {
        public long tv_sec;
        public long tv_usec;
        public ushort type;
        public ushort code;
        public int value;
    }

    private static readonly Dictionary<int, string> LinuxKeyCodeToName = new()
    {
        { 1, "Escape" },
        { 2, "D1" }, { 3, "D2" }, { 4, "D3" }, { 5, "D4" },
        { 6, "D5" }, { 7, "D6" }, { 8, "D7" }, { 9, "D8" }, { 10, "D9" }, { 11, "D0" },
        { 14, "Back" },
        { 15, "Tab" },
        { 16, "Q" }, { 17, "W" }, { 18, "E" }, { 19, "R" }, { 20, "T" }, { 21, "Y" },
        { 22, "U" }, { 23, "I" }, { 24, "O" }, { 25, "P" },
        { 28, "Return" },
        { 29, "LeftCtrl" },
        { 30, "A" }, { 31, "S" }, { 32, "D" }, { 33, "F" }, { 34, "G" }, { 35, "H" },
        { 36, "J" }, { 37, "K" }, { 38, "L" },
        { 42, "LeftShift" },
        { 44, "Z" }, { 45, "X" }, { 46, "C" }, { 47, "V" }, { 48, "B" }, { 49, "N" }, { 50, "M" },
        { 54, "RightShift" },
        { 56, "LeftAlt" },
        { 57, "Space" },
        { 59, "F1" }, { 60, "F2" }, { 61, "F3" }, { 62, "F4" },
        { 63, "F5" }, { 64, "F6" }, { 65, "F7" }, { 66, "F8" },
        { 67, "F9" }, { 68, "F10" },
        { 87, "F11" }, { 88, "F12" },
        { 97, "RightCtrl" },
        { 100, "RightAlt" },
        { 102, "Home" },
        { 103, "Up" },
        { 104, "PageUp" },
        { 105, "Left" },
        { 106, "Right" },
        { 107, "End" },
        { 108, "Down" },
        { 109, "PageDown" },
        { 110, "Insert" },
        { 111, "Delete" },
        { 125, "LWin" },
        { 126, "RWin" },
    };

    private const ushort EV_KEY = 1;
    private const uint EVIOCGRAB = 0x40044590;

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, uint request, int arg);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private const int WH_KEYBOARD_LL = 13;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    public event EventHandler<string>? HotkeyPressed;
    public event EventHandler<string>? HotkeyReleased;

    public bool IsGlobalHotkeySupported { get; private set; }

    public GlobalHotkeyService()
    {
        _isWindows = OperatingSystem.IsWindows();
        _isLinux = OperatingSystem.IsLinux();
        IsGlobalHotkeySupported = _isWindows || _isLinux;
    }

    public void Initialize(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;

        if (_isWindows)
        {
            StartKeyMonitoring();
        }
        else if (_isLinux)
        {
            StartLinuxKeyMonitoring();
        }
    }

    private void StartKeyMonitoring()
    {
        try
        {
            _hookProc = HookCallback;
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);
        }
        catch
        {
            IsGlobalHotkeySupported = false;
        }
    }

    private void StartLinuxKeyMonitoring()
    {
        try
        {
            string? devicePath = FindKeyboardDevice();
            if (devicePath == null)
            {
                Console.WriteLine("[DEBUG] No keyboard device found for global hotkeys");
                return;
            }

            Console.WriteLine($"[DEBUG] Opening keyboard device: {devicePath}");
            _linuxEventStream = new FileStream(devicePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            _linuxCts = new CancellationTokenSource();
            _linuxListenerTask = Task.Run(() => LinuxKeyListenerLoop(_linuxCts.Token));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Failed to start Linux key monitoring: {ex.Message}");
            IsGlobalHotkeySupported = false;
        }
    }

    private void StopLinuxKeyMonitoring()
    {
        try
        {
            if (_keyboardGrabbed)
            {
                SetKeyboardGrab(false);
            }

            _linuxCts?.Cancel();

            _linuxEventStream?.Dispose();

            _linuxListenerTask?.Wait(2000);
        }
        catch { }
        _linuxEventStream = null;
        _linuxCts = null;
        _linuxListenerTask = null;
    }

    private static string? FindKeyboardDevice()
    {
        try
        {
            string kbdDir = "/dev/input/by-path";
            if (Directory.Exists(kbdDir))
            {
                string[] kbdDevices = Directory.GetFiles(kbdDir, "*-kbd");
                if (kbdDevices.Length > 0)
                {
                    return Path.GetFullPath(kbdDevices[0]);
                }
            }
        }
        catch { }
        return null;
    }

    private void LinuxKeyListenerLoop(CancellationToken token)
    {
        if (_linuxEventStream == null) return;

        byte[] buffer = new byte[24];

        while (!token.IsCancellationRequested)
        {
            try
            {
                int offset = 0;
                while (offset < 24)
                {
                    int read = _linuxEventStream.Read(buffer, offset, 24 - offset);
                    if (read == 0) break;
                    offset += read;
                }
                if (offset < 24) continue;

                InputEvent evt = MemoryMarshal.Read<InputEvent>(buffer);

                if (evt.type == EV_KEY && (evt.value == 0 || evt.value == 1))
                {
                    string? keyName = LinuxKeyCodeToName.GetValueOrDefault(evt.code);
                    if (keyName != null)
                    {
                        Console.WriteLine($"[DEBUG] evdev: {keyName} code=0x{evt.code:x} val={evt.value} registered={_registeredKey}");
                    }

                    bool suppress = keyName != null && ShouldSuppressKey?.Invoke(keyName) == true;

                    if (_keyboardGrabbed && !suppress)
                    {
                        ReinjectLinuxKey(evt.code, evt.value);
                    }

                    if (keyName == null) continue;

                    if (_registeredKeys.Count > 0 && _registeredKeys.Contains(keyName))
                    {
                        if (evt.value == 1)
                        {
                            HotkeyPressed?.Invoke(this, keyName);
                        }
                        else if (evt.value == 0)
                        {
                            HotkeyReleased?.Invoke(this, keyName);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] LinuxKeyListenerLoop error: {ex.Message}");
                Thread.Sleep(100);
            }
        }
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        StopLinuxKeyMonitoring();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            string keyName = VirtualKeyToString(vkCode);

            // KBDLLHOOKSTRUCT flags is at offset 8.
            // LLKHF_INJECTED (0x10) and LLKHF_LOWER_IL_INJECTED (0x02) indicate simulated input.
            int flags = Marshal.ReadInt32(lParam, 8);
            bool isInjected = (flags & 0x10) != 0 || (flags & 0x02) != 0;

            if (!isInjected && _registeredKeys.Count > 0 && _registeredKeys.Contains(keyName))
            {
                if (wParam == (IntPtr)0x0100)
                {
                    HotkeyPressed?.Invoke(this, keyName);
                }
                else if (wParam == (IntPtr)0x0101)
                {
                    HotkeyReleased?.Invoke(this, keyName);
                }

                if (ShouldSuppressKey?.Invoke(keyName) == true)
                {
                    return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void RegisterHotkey(string key)
    {
        _registeredKey = key;
        _registeredKeys.Add(key);
    }

    public void UnregisterHotkey()
    {
        _registeredKey = null;
        _registeredKeys.Clear();
    }

    public void RegisterAdditionalHotkey(string key)
    {
        _registeredKeys.Add(key);
    }

    public void SetKeyboardGrab(bool grab)
    {
        if (!_isLinux || _linuxEventStream == null)
            return;

        try
        {
            int fd = _linuxEventStream.SafeFileHandle.DangerousGetHandle().ToInt32();
            int result = ioctl(fd, EVIOCGRAB, grab ? 1 : 0);
            _keyboardGrabbed = grab && result == 0;
            Console.WriteLine($"[DEBUG] SetKeyboardGrab({grab}): result={result}, grabbed={_keyboardGrabbed}");
        }
        catch (Exception ex)
        {
            _keyboardGrabbed = false;
            Console.WriteLine($"[DEBUG] SetKeyboardGrab failed: {ex.Message}");
        }
    }

    private static void ReinjectLinuxKey(ushort code, int value)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ydotool",
                Arguments = $"key {code}:{value}",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.EnvironmentVariables["YDOTOOL_SOCKET"] = YdotoolSocket;
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] ReinjectLinuxKey failed: {ex.Message}");
        }
    }

    private string VirtualKeyToString(int vkCode)
    {
        return vkCode switch
        {
            0x70 => "F1", 0x71 => "F2", 0x72 => "F3", 0x73 => "F4",
            0x74 => "F5", 0x75 => "F6", 0x76 => "F7", 0x77 => "F8",
            0x78 => "F9", 0x79 => "F10", 0x7A => "F11", 0x7B => "F12",
            0x41 => "A", 0x42 => "B", 0x43 => "C", 0x44 => "D",
            0x45 => "E", 0x46 => "F", 0x47 => "G", 0x48 => "H",
            0x49 => "I", 0x4A => "J", 0x4B => "K", 0x4C => "L",
            0x4D => "M", 0x4E => "N", 0x4F => "O", 0x50 => "P",
            0x51 => "Q", 0x52 => "R", 0x53 => "S", 0x54 => "T",
            0x55 => "U", 0x56 => "V", 0x57 => "W", 0x58 => "X",
            0x59 => "Y", 0x5A => "Z",
            0x30 => "0", 0x31 => "1", 0x32 => "2", 0x33 => "3",
            0x34 => "4", 0x35 => "5", 0x36 => "6", 0x37 => "7",
            0x38 => "8", 0x39 => "9",
            _ => vkCode.ToString()
        };
    }
}
