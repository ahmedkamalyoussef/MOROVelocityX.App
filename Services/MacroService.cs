using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    public StatsService? StatsService { get; set; }

    public event EventHandler<bool>? MacroStatusChanged;
    public event EventHandler<string>? MacroError;

    public bool IsRunning => _isRunning;
    public bool IsInputSimulationSupported { get; private set; }
    public volatile bool IsSimulating;

    // Path to the diagnostic log file — written next to the EXE.
    // Useful for post-mortem analysis when the app is not attached to a debugger.
    private static readonly string DiagLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MOROVelocityX", "diagnostic.log");

    public MacroService()
    {
        _isToggleMode = true;
        _isHoldMode = false;
        _cps = 10;
        _clickAction = ClickAction.MouseLeft;
        _isWindows = OperatingSystem.IsWindows();
        _isLinux = OperatingSystem.IsLinux();
        IsInputSimulationSupported = _isWindows || _isLinux;
        // Log INPUT struct size once at startup so the developer can verify
        // the layout is correct (expected: 40 bytes on x64, 28 bytes on x86).
        LogSendInputDiagnostics();
        AppendDiagnosticLog(
            $"MacroService started. " +
            $"InputSize={InputSize} bytes. " +
            $"IsWindows={_isWindows}. " +
            $"IntPtr.Size={IntPtr.Size} bytes (expected 8 on x64).");
    }

    // -------------------------------------------------------------------------
    // INPUT structure — canonical x64-safe P/Invoke definition.
    //
    // On x64, IntPtr is 8 bytes. Because MOUSEINPUT and KEYBDINPUT both end with
    // an IntPtr (dwExtraInfo), the union requires 8-byte alignment. The CLR
    // therefore inserts 4 bytes of padding between `type` (uint, 4 bytes) and
    // the union. Marshal.SizeOf<INPUT>() == 40 on x64, 28 on x86 — both match
    // what Windows expects when cbSize is passed to SendInput.
    //
    // Pack = 0 lets each member use its natural alignment (the default and
    // correct value). Do NOT set Pack = 1 or Pack = 4 — that destroys the
    // IntPtr alignment and corrupts the structure on x64.
    // -------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    private struct INPUT
    {
        public uint type;     // 4 bytes
        // 4 bytes implicit padding (CLR aligns union to 8 bytes because of IntPtr inside)
        public InputUnion U;  // 32 bytes on x64 (MOUSEINPUT is the largest member)
    }

    [StructLayout(LayoutKind.Explicit, Pack = 0)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    private struct MOUSEINPUT
    {
        public int dx;            // 4 bytes
        public int dy;            // 4 bytes
        public uint mouseData;    // 4 bytes
        public uint dwFlags;      // 4 bytes
        public uint time;         // 4 bytes
        // 4 bytes implicit padding to align IntPtr to 8 bytes on x64
        public IntPtr dwExtraInfo; // 8 bytes on x64
    }

    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    private struct KEYBDINPUT
    {
        public ushort wVk;         // 2 bytes
        public ushort wScan;       // 2 bytes
        public uint dwFlags;       // 4 bytes
        public uint time;          // 4 bytes
        // 4 bytes implicit padding to align IntPtr to 8 bytes on x64
        public IntPtr dwExtraInfo; // 8 bytes on x64
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint uPeriod);

    private const uint INPUT_MOUSE    = 0;
    private const uint INPUT_KEYBOARD = 1;

    // Mouse flags
    private const uint MOUSEEVENTF_LEFTDOWN   = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP     = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN  = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP    = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP   = 0x0040;

    // Keyboard flags
    private const uint KEYEVENTF_KEYUP    = 0x0002;
    // KEYEVENTF_SCANCODE instructs Windows to treat wScan as the hardware scan
    // code rather than a virtual key. This is required for games that read input
    // via DirectInput, Raw Input, or engine-level polling — they query the
    // hardware key state, not the Windows message queue, so VK-only injection
    // is invisible to them.
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    // -------------------------------------------------------------------------
    // VK → Hardware Scan Code map.
    //
    // Scan codes are the Set-1 XT (IBM PC/AT) scan codes that represent physical
    // key positions on a standard US QWERTY keyboard. These are the values that
    // DirectInput and Raw Input consumers see from the keyboard driver.
    //
    // Values verified against the official Microsoft scan-code tables:
    // https://learn.microsoft.com/en-us/windows/win32/inputdev/about-keyboard-input
    // -------------------------------------------------------------------------
    private static readonly Dictionary<byte, ushort> VkToScanCode = new()
    {
        // Alphabet
        { 0x41, 0x1E }, // A
        { 0x42, 0x30 }, // B
        { 0x43, 0x2E }, // C
        { 0x44, 0x20 }, // D
        { 0x45, 0x12 }, // E
        { 0x46, 0x21 }, // F
        { 0x47, 0x22 }, // G
        { 0x48, 0x23 }, // H
        { 0x49, 0x17 }, // I
        { 0x4A, 0x24 }, // J
        { 0x4B, 0x25 }, // K
        { 0x4C, 0x26 }, // L
        { 0x4D, 0x32 }, // M
        { 0x4E, 0x31 }, // N
        { 0x4F, 0x18 }, // O
        { 0x50, 0x19 }, // P
        { 0x51, 0x10 }, // Q
        { 0x52, 0x13 }, // R
        { 0x53, 0x1F }, // S
        { 0x54, 0x14 }, // T
        { 0x55, 0x16 }, // U
        { 0x56, 0x2F }, // V
        { 0x57, 0x11 }, // W
        { 0x58, 0x2D }, // X
        { 0x59, 0x15 }, // Y
        { 0x5A, 0x2C }, // Z
        // Top-row digits
        { 0x30, 0x0B }, // 0
        { 0x31, 0x02 }, // 1
        { 0x32, 0x03 }, // 2
        { 0x33, 0x04 }, // 3
        { 0x34, 0x05 }, // 4
        { 0x35, 0x06 }, // 5
        { 0x36, 0x07 }, // 6
        { 0x37, 0x08 }, // 7
        { 0x38, 0x09 }, // 8
        { 0x39, 0x0A }, // 9
        // Function keys
        { 0x70, 0x3B }, // F1
        { 0x71, 0x3C }, // F2
        { 0x72, 0x3D }, // F3
        { 0x73, 0x3E }, // F4
        { 0x74, 0x3F }, // F5
        { 0x75, 0x40 }, // F6
        { 0x76, 0x41 }, // F7
        { 0x77, 0x42 }, // F8
        { 0x78, 0x43 }, // F9
        { 0x79, 0x44 }, // F10
        { 0x7A, 0x57 }, // F11
        { 0x7B, 0x58 }, // F12
        // Space
        { 0x20, 0x39 }, // Space
    };

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
        IsSimulating = true;
        try
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
        finally
        {
            IsSimulating = false;
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
        var inputDown = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion { mi = new MOUSEINPUT { dwFlags = downFlag } }
        };

        var inputUp = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion { mi = new MOUSEINPUT { dwFlags = upFlag } }
        };

        // Determine hold time based on CPS to prevent throttling while ensuring games catch the input
        int cps = Math.Max(1, _cps);
        int holdMs = Math.Max(2, Math.Min(15, 1000 / (cps * 2)));

        uint resultDown = SendInput(1, new[] { inputDown }, InputSize);
        LogSendInputResult("Mouse Down", resultDown);

        Thread.Sleep(holdMs);

        uint resultUp = SendInput(1, new[] { inputUp }, InputSize);
        LogSendInputResult("Mouse Up", resultUp);
    }

    private void SendKeyPress(byte vkCode)
    {
        // Use KEYEVENTF_SCANCODE so that games which read hardware state via
        // DirectInput / Raw Input (instead of the Windows message queue) will
        // see the simulated keystroke. With VK-only injection (dwFlags = 0),
        // those games receive nothing because the hardware key state table is
        // not updated.
        //
        // Rules (per MSDN SendInput / KEYBDINPUT documentation):
        //   - wVk   must be 0 when KEYEVENTF_SCANCODE is set.
        //   - wScan must be the hardware Set-1 XT scan code of the key.
        //   - For key-up:  dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP

        if (!VkToScanCode.TryGetValue(vkCode, out ushort scanCode))
        {
            // No scan code available — fall back to VK injection.
            // This still works in normal apps; games may ignore it.
            Debug.WriteLine($"[MacroService] No scan code for VK=0x{vkCode:X2}, falling back to VK injection.");
            scanCode = 0;
        }

        bool useScan = scanCode != 0;
        uint downFlags = useScan ? KEYEVENTF_SCANCODE : 0u;
        uint upFlags   = useScan ? (KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP) : KEYEVENTF_KEYUP;

        var inputDown = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk   = useScan ? (ushort)0 : vkCode,  // must be 0 when using scan code
                    wScan = scanCode,
                    dwFlags = downFlags
                }
            }
        };

        var inputUp = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk   = useScan ? (ushort)0 : vkCode,
                    wScan = scanCode,
                    dwFlags = upFlags
                }
            }
        };

        // Determine hold time based on CPS to prevent throttling while ensuring games catch the input
        int cps = Math.Max(1, _cps);
        int holdMs = Math.Max(2, Math.Min(15, 1000 / (cps * 2)));

        uint resultDown = SendInput(1, new[] { inputDown }, InputSize);
        LogSendInputResult($"Key VK=0x{vkCode:X2} Scan=0x{scanCode:X2} Down", resultDown);

        Thread.Sleep(holdMs);

        uint resultUp = SendInput(1, new[] { inputUp }, InputSize);
        LogSendInputResult($"Key VK=0x{vkCode:X2} Scan=0x{scanCode:X2} Up", resultUp);
    }

    // -------------------------------------------------------------------------
    // Diagnostic logging.
    //
    // Debug.WriteLine — visible in the debugger Output window only.
    // AppendDiagnosticLog — writes to %LocalAppData%\MOROVelocityX\diagnostic.log
    //   so failures are visible even in Release builds without a debugger.
    // -------------------------------------------------------------------------
    [System.Diagnostics.Conditional("DEBUG")]
    private static void LogSendInputDiagnostics()
    {
        Debug.WriteLine($"[MacroService] Marshal.SizeOf<INPUT>() = {Marshal.SizeOf<INPUT>()} bytes (expected 40 on x64, 28 on x86)");
    }

    // Instance method — can fire MacroError and write to the log file.
    private void LogSendInputResult(string label, uint result)
    {
        if (result == 0)
        {
            int err = Marshal.GetLastWin32Error();
            // Common Win32 errors:
            //   5  = ERROR_ACCESS_DENIED — UIPI blocked the call. The app must run
            //        as Administrator to send input to a higher-integrity game process.
            //  87  = ERROR_INVALID_PARAMETER — INPUT struct size mismatch.
            // 1400 = ERROR_INVALID_WINDOW_HANDLE — no focused window (rare).
            string errName = err switch
            {
                5    => "ERROR_ACCESS_DENIED (UIPI — run app as Administrator!)",
                87   => "ERROR_INVALID_PARAMETER (INPUT struct size wrong)",
                1400 => "ERROR_INVALID_WINDOW_HANDLE",
                _    => $"Win32Error={err}"
            };
            string msg = $"SendInput FAILED [{label}]: {errName}";
            Debug.WriteLine($"[MacroService] {msg}");
            AppendDiagnosticLog(msg);
            MacroError?.Invoke(this, $"Injection failed: {errName}");
        }
        else
        {
            Debug.WriteLine($"[MacroService] SendInput OK [{label}]: injected {result} event(s)");
        }
    }

    // Appends a timestamped line to the diagnostic log file.
    // Never throws — wrapped in try/catch so log failures never crash the macro loop.
    private static void AppendDiagnosticLog(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DiagLogPath)!);
            File.AppendAllText(DiagLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}" + Environment.NewLine);
        }
        catch { /* never let logging crash the macro loop */ }
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
