using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MOROVelocityX.Services;

public class MacroService : IDisposable
{
    private enum ClickAction
    {
        MouseLeft,
        MouseRight,
        MouseMiddle,
        Keyboard
    }

    private volatile bool _isRunning;
    private bool _isToggleMode;
    private bool _isHoldMode;
    private volatile int _cps;
    private ClickAction _clickAction;
    private byte _keyboardVk;
    private Thread? _macroThread;
    private readonly object _lock = new();
    private readonly bool _isWindows;
    private readonly bool _isLinux;
    private static readonly string YdotoolSocket = "/tmp/ydotool.sock";

    private readonly INPUT[] _mouseInputs = new INPUT[2];
    private readonly INPUT[] _keyInputs = new INPUT[2];
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    public StatsService? StatsService { get; set; }

    public event EventHandler<bool>? MacroStatusChanged;
    public event EventHandler<string>? MacroError;

    public bool IsRunning => _isRunning;
    public bool IsInputSimulationSupported { get; private set; }

    public MacroService()
    {
        _isToggleMode = true;
        _isHoldMode = false;
        _cps = 10;
        _clickAction = ClickAction.MouseLeft;
        _isWindows = OperatingSystem.IsWindows();
        _isLinux = OperatingSystem.IsLinux();
        IsInputSimulationSupported = _isWindows || _isLinux;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint uPeriod);

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public void Configure(bool isToggleMode, int cps, string clickKey)
    {
        lock (_lock)
        {
            _isToggleMode = isToggleMode;
            _isHoldMode = !isToggleMode;
            _cps = Math.Clamp(cps, 1, 500);
            UpdateClickAction(clickKey);
        }
    }

    public void StartToggleMode()
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            lock (_lock)
            {
                if (_isRunning)
                    StopLocked();
                else
                    StartLocked();
            }
        });
    }

    public void StartHoldMode()
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            lock (_lock)
            {
                if (!_isRunning)
                    StartLocked();
            }
        });
    }

    public void StopHoldMode()
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            lock (_lock)
            {
                if (_isRunning && _isHoldMode)
                    StopLocked();
            }
        });
    }

    private void StartLocked()
    {
        if (_isRunning)
            return;

        _isRunning = true;
        _macroThread = new Thread(RunMacroLoop)
        {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
            Name = "MacroLoop"
        };
        _macroThread.Start();
        MacroStatusChanged?.Invoke(this, true);
    }

    private void StopLocked()
    {
        if (!_isRunning)
            return;

        _isRunning = false;
        ReleaseClickKey();
        MacroStatusChanged?.Invoke(this, false);
    }

    public void Stop()
    {
        lock (_lock)
        {
            StopLocked();
        }
    }

    private void RunMacroLoop()
    {
        if (_isWindows)
            timeBeginPeriod(1);

        try
        {
            var sw = Stopwatch.StartNew();
            long nextClickTicks = sw.ElapsedTicks;

            while (_isRunning)
            {
                PerformClick();

                int cps = Math.Max(1, _cps);
                long intervalTicks = Stopwatch.Frequency / cps;
                nextClickTicks += intervalTicks;

                long remainingTicks = nextClickTicks - sw.ElapsedTicks;
                if (remainingTicks <= 0)
                    continue;

                long sleepMs = remainingTicks * 1000 / Stopwatch.Frequency;
                if (sleepMs > 1)
                    Thread.Sleep((int)(sleepMs - 1));

                while (_isRunning && sw.ElapsedTicks < nextClickTicks)
                    Thread.SpinWait(50);
            }
        }
        catch (Exception ex)
        {
            MacroError?.Invoke(this, ex.Message);
        }
        finally
        {
            if (_isWindows)
                timeEndPeriod(1);

            _isRunning = false;
        }
    }

    private void ReleaseClickKey()
    {
        if (!_isLinux)
            return;

        int keycode = _clickAction switch
        {
            ClickAction.Keyboard => KeyToLinuxKeycodeFromVk(_keyboardVk),
            _ => 0
        };

        if (keycode <= 0)
            return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ydotool",
                Arguments = $"key {keycode}:0",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.EnvironmentVariables["YDOTOOL_SOCKET"] = YdotoolSocket;
            Process.Start(psi);
        }
        catch { }
    }

    public void ReleaseKey(string key)
    {
        if (!_isLinux)
            return;

        int keycode = KeyToLinuxKeycode(key);
        if (keycode <= 0)
            return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ydotool",
                Arguments = $"key {keycode}:0",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.EnvironmentVariables["YDOTOOL_SOCKET"] = YdotoolSocket;
            Process.Start(psi);
        }
        catch { }
    }

    private void PerformClick()
    {
        try
        {
            StatsService?.RecordClick();

            if (_isWindows)
                PerformClickWindows();
            else if (_isLinux)
                PerformClickLinux();
        }
        catch (Exception ex)
        {
            MacroError?.Invoke(this, $"Click simulation failed: {ex.Message}");
        }
    }

    private void PerformClickWindows()
    {
        switch (_clickAction)
        {
            case ClickAction.MouseLeft:
                SendMouseClick(MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
                break;
            case ClickAction.MouseRight:
                SendMouseClick(MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP);
                break;
            case ClickAction.MouseMiddle:
                SendMouseClick(MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP);
                break;
            case ClickAction.Keyboard:
                SendKeyPress(_keyboardVk);
                break;
        }
    }

    private void PerformClickLinux()
    {
        string? clickArg = _clickAction switch
        {
            ClickAction.MouseLeft => "0xC0",
            ClickAction.MouseRight => "0xC1",
            ClickAction.MouseMiddle => "0xC2",
            _ => null
        };

        if (clickArg != null)
        {
            RunYdotool("click", clickArg);
            return;
        }

        if (_clickAction == ClickAction.Keyboard)
        {
            int keycode = KeyToLinuxKeycodeFromVk(_keyboardVk);
            if (keycode > 0)
                RunYdotool("key", $"{keycode}:1 {keycode}:0");
        }
    }

    private void RunYdotool(string command, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ydotool",
                Arguments = $"{command} {args}",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.EnvironmentVariables["YDOTOOL_SOCKET"] = YdotoolSocket;
            using var process = Process.Start(psi);
            process?.WaitForExit(100);
        }
        catch (Exception ex)
        {
            MacroError?.Invoke(this, $"ydotool execution failed: {ex.Message}");
        }
    }

    private void SendMouseClick(uint downFlag, uint upFlag)
    {
        _mouseInputs[0] = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion { mi = new MOUSEINPUT { dwFlags = downFlag } }
        };
        _mouseInputs[1] = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion { mi = new MOUSEINPUT { dwFlags = upFlag } }
        };
        SendInput(2, _mouseInputs, InputSize);
    }

    private void SendKeyPress(byte vkCode)
    {
        _keyInputs[0] = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = vkCode } }
        };
        _keyInputs[1] = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = vkCode, dwFlags = KEYEVENTF_KEYUP } }
        };
        SendInput(2, _keyInputs, InputSize);
    }

    private void UpdateClickAction(string clickKey)
    {
        switch (clickKey.ToUpper())
        {
            case "MOUSE1":
            case "LEFTBUTTON":
                _clickAction = ClickAction.MouseLeft;
                break;
            case "MOUSE2":
            case "RIGHTBUTTON":
                _clickAction = ClickAction.MouseRight;
                break;
            case "MOUSE3":
            case "MIDDLEBUTTON":
                _clickAction = ClickAction.MouseMiddle;
                break;
            default:
                _clickAction = ClickAction.Keyboard;
                _keyboardVk = KeyToVirtualKey(clickKey);
                break;
        }
    }

    private static int KeyToLinuxKeycodeFromVk(byte vk)
    {
        return vk switch
        {
            0x70 => 59, 0x71 => 60, 0x72 => 61, 0x73 => 62,
            0x74 => 63, 0x75 => 64, 0x76 => 65, 0x77 => 66,
            0x78 => 67, 0x79 => 68, 0x7A => 87, 0x7B => 88,
            0x41 => 30, 0x42 => 48, 0x43 => 46, 0x44 => 32,
            0x45 => 18, 0x46 => 33, 0x47 => 34, 0x48 => 35,
            0x49 => 23, 0x4A => 36, 0x4B => 37, 0x4C => 38,
            0x4D => 50, 0x4E => 49, 0x4F => 24, 0x50 => 25,
            0x51 => 16, 0x52 => 19, 0x53 => 31, 0x54 => 20,
            0x55 => 22, 0x56 => 47, 0x57 => 17, 0x58 => 45,
            0x59 => 21, 0x5A => 44,
            0x30 => 11, 0x31 => 2, 0x32 => 3, 0x33 => 4,
            0x34 => 5, 0x35 => 6, 0x36 => 7, 0x37 => 8,
            0x38 => 9, 0x39 => 10,
            _ => 0
        };
    }

    private static int KeyToLinuxKeycode(string key)
    {
        return KeyToLinuxKeycodeFromVk(KeyToVirtualKey(key));
    }

    private static byte KeyToVirtualKey(string key)
    {
        var keyUpper = key.ToUpper().Replace(" ", "");

        return keyUpper switch
        {
            "F1" => 0x70, "F2" => 0x71, "F3" => 0x72, "F4" => 0x73,
            "F5" => 0x74, "F6" => 0x75, "F7" => 0x76, "F8" => 0x77,
            "F9" => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
            "A" => 0x41, "B" => 0x42, "C" => 0x43, "D" => 0x44,
            "E" => 0x45, "F" => 0x46, "G" => 0x47, "H" => 0x48,
            "I" => 0x49, "J" => 0x4A, "K" => 0x4B, "L" => 0x4C,
            "M" => 0x4D, "N" => 0x4E, "O" => 0x4F, "P" => 0x50,
            "Q" => 0x51, "R" => 0x52, "S" => 0x53, "T" => 0x54,
            "U" => 0x55, "V" => 0x56, "W" => 0x57, "X" => 0x58,
            "Y" => 0x59, "Z" => 0x5A,
            "0" => 0x30, "1" => 0x31, "2" => 0x32, "3" => 0x33,
            "4" => 0x34, "5" => 0x35, "6" => 0x36, "7" => 0x37,
            "8" => 0x38, "9" => 0x39,
            "SPACE" or "SPACEBAR" => 0x20,
            _ => 0
        };
    }

    public void Dispose()
    {
        Stop();
        _macroThread?.Join(500);
    }
}
