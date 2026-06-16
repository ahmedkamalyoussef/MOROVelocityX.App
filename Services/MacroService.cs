using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MOROVelocityX.Services;

public class MacroService : IDisposable
{
    private bool _isRunning;
    private bool _isToggleMode;
    private bool _isHoldMode;
    private int _cps;
    private string _clickKey;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _macroTask;
    private readonly object _lock = new object();
    private readonly bool _isWindows;
    private readonly bool _isLinux;
    private static readonly string YdotoolSocket = "/tmp/ydotool.sock";

    public event EventHandler<bool>? MacroStatusChanged;
    public event EventHandler<string>? MacroError;

    public bool IsRunning => _isRunning;
    public bool IsInputSimulationSupported { get; private set; }

    public MacroService()
    {
        _isRunning = false;
        _isToggleMode = true;
        _isHoldMode = false;
        _cps = 10;
        _clickKey = "Mouse1";
        _isWindows = OperatingSystem.IsWindows();
        _isLinux = OperatingSystem.IsLinux();
        IsInputSimulationSupported = _isWindows || _isLinux;
    }

#if WINDOWS
    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, uint dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint KEYEVENTF_KEYUP = 0x0002;
#endif

    public void Configure(bool isToggleMode, int cps, string clickKey)
    {
        lock (_lock)
        {
            _isToggleMode = isToggleMode;
            _isHoldMode = !isToggleMode;
            _cps = Math.Clamp(cps, 1, 500);
            _clickKey = clickKey;
        }
    }

    public void StartToggleMode()
    {
        lock (_lock)
        {
            if (_isRunning)
            {
                Stop();
            }
            else
            {
                Start();
            }
        }
    }

    public void StartHoldMode()
    {
        lock (_lock)
        {
            if (!_isRunning)
            {
                Start();
            }
        }
    }

    public void StopHoldMode()
    {
        lock (_lock)
        {
            if (_isRunning && _isHoldMode)
            {
                Stop();
            }
        }
    }

    private void Start()
    {
        lock (_lock)
        {
            if (_isRunning)
                return;

            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            _macroTask = RunMacroAsync(_cancellationTokenSource.Token);
            MacroStatusChanged?.Invoke(this, true);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRunning)
                return;

            _isRunning = false;
            _cancellationTokenSource?.Cancel();

            try
            {
                _macroTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
            }
            catch (OperationCanceledException)
            {
            }

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            _macroTask = null;

            MacroStatusChanged?.Invoke(this, false);
        }
    }

    private async Task RunMacroAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                lock (_lock)
                {
                    if (!_isRunning)
                        break;
                }

                PerformClick();

                int delayMs = 1000 / _cps;

                await Task.Delay(delayMs, cancellationToken);
            }
        }
        catch (TaskCanceledException)
        {
        }
        catch (Exception ex)
        {
            MacroError?.Invoke(this, ex.Message);
        }
        finally
        {
            lock (_lock)
            {
                _isRunning = false;
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                MacroStatusChanged?.Invoke(this, false);
            }
        }
    }

    private void PerformClick()
    {
        try
        {
            string clickKey;
            bool isToggleMode;

            lock (_lock)
            {
                clickKey = _clickKey;
                isToggleMode = _isToggleMode;
            }

            if (_isWindows)
            {
                PerformClickWindows(clickKey);
            }
            else if (_isLinux)
            {
                PerformClickLinux(clickKey);
            }
        }
        catch (Exception ex)
        {
            MacroError?.Invoke(this, $"Click simulation failed: {ex.Message}");
        }
    }

    private void PerformClickWindows(string clickKey)
    {
#if WINDOWS
        switch (clickKey.ToUpper())
        {
            case "MOUSE1":
            case "LEFTBUTTON":
                SimulateMouseClick(MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
                break;
            case "MOUSE2":
            case "RIGHTBUTTON":
                SimulateMouseClick(MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP);
                break;
            case "MOUSE3":
            case "MIDDLEBUTTON":
                SimulateMouseClick(MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP);
                break;
            default:
                SimulateKeyPress(clickKey);
                break;
        }
#endif
    }

    private void PerformClickLinux(string clickKey)
    {
        string? clickArg = clickKey.ToUpper() switch
        {
            "MOUSE1" or "LEFTBUTTON" => "0xC0",
            "MOUSE2" or "RIGHTBUTTON" => "0xC1",
            "MOUSE3" or "MIDDLEBUTTON" => "0xC2",
            _ => null
        };

        if (clickArg != null)
        {
            RunYdotool("click", clickArg);
        }
        else
        {
            int keycode = KeyToLinuxKeycode(clickKey);
            if (keycode > 0)
            {
                RunYdotool("key", $"{keycode}:1 {keycode}:0");
            }
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
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.EnvironmentVariables["YDOTOOL_SOCKET"] = YdotoolSocket;

            using var process = Process.Start(psi);
            process?.WaitForExit(500);
        }
        catch (Exception ex)
        {
            MacroError?.Invoke(this, $"ydotool execution failed: {ex.Message}");
        }
    }

#if WINDOWS
    private void SimulateMouseClick(uint downFlag, uint upFlag)
    {
        try
        {
            mouse_event(downFlag, 0, 0, 0, 0);
            Thread.Sleep(10);
            mouse_event(upFlag, 0, 0, 0, 0);
        }
        catch (Exception ex)
        {
            MacroError?.Invoke(this, $"Mouse click simulation failed: {ex.Message}");
        }
    }

    private void SimulateKeyPress(string key)
    {
        try
        {
            byte vkCode = KeyToVirtualKey(key);
            if (vkCode != 0)
            {
                keybd_event(vkCode, 0, 0, 0);
                Thread.Sleep(10);
                keybd_event(vkCode, 0, KEYEVENTF_KEYUP, 0);
            }
        }
        catch (Exception ex)
        {
            MacroError?.Invoke(this, $"Key press simulation failed: {ex.Message}");
        }
    }
#endif

    private static int KeyToLinuxKeycode(string key)
    {
        var keyUpper = key.ToUpper().Replace(" ", "");

        return keyUpper switch
        {
            "F1" => 59,
            "F2" => 60,
            "F3" => 61,
            "F4" => 62,
            "F5" => 63,
            "F6" => 64,
            "F7" => 65,
            "F8" => 66,
            "F9" => 67,
            "F10" => 68,
            "F11" => 87,
            "F12" => 88,
            "A" => 30,
            "B" => 48,
            "C" => 46,
            "D" => 32,
            "E" => 18,
            "F" => 33,
            "G" => 34,
            "H" => 35,
            "I" => 23,
            "J" => 36,
            "K" => 37,
            "L" => 38,
            "M" => 50,
            "N" => 49,
            "O" => 24,
            "P" => 25,
            "Q" => 16,
            "R" => 19,
            "S" => 31,
            "T" => 20,
            "U" => 22,
            "V" => 47,
            "W" => 17,
            "X" => 45,
            "Y" => 21,
            "Z" => 44,
            "0" => 11,
            "1" => 2,
            "2" => 3,
            "3" => 4,
            "4" => 5,
            "5" => 6,
            "6" => 7,
            "7" => 8,
            "8" => 9,
            "9" => 10,
            "SPACE" or "SPACEBAR" => 57,
            "ENTER" or "RETURN" => 28,
            "TAB" => 15,
            "BACKSPACE" => 14,
            "ESCAPE" or "ESC" => 1,
            "LEFTSHIFT" or "LSHIFT" => 42,
            "RIGHTSHIFT" or "RSHIFT" => 54,
            "LEFTCONTROL" or "LCONTROL" or "LCTRL" => 29,
            "RIGHTCONTROL" or "RCONTROL" or "RCTRL" => 97,
            "LEFTALT" or "LALT" => 56,
            "RIGHTALT" or "RALT" => 100,
            _ => 0
        };
    }

    private byte KeyToVirtualKey(string key)
    {
        var keyUpper = key.ToUpper().Replace(" ", "");

        return keyUpper switch
        {
            "F1" => 0x70,
            "F2" => 0x71,
            "F3" => 0x72,
            "F4" => 0x73,
            "F5" => 0x74,
            "F6" => 0x75,
            "F7" => 0x76,
            "F8" => 0x77,
            "F9" => 0x78,
            "F10" => 0x79,
            "F11" => 0x7A,
            "F12" => 0x7B,
            "A" => 0x41,
            "B" => 0x42,
            "C" => 0x43,
            "D" => 0x44,
            "E" => 0x45,
            "F" => 0x46,
            "G" => 0x47,
            "H" => 0x48,
            "I" => 0x49,
            "J" => 0x4A,
            "K" => 0x4B,
            "L" => 0x4C,
            "M" => 0x4D,
            "N" => 0x4E,
            "O" => 0x4F,
            "P" => 0x50,
            "Q" => 0x51,
            "R" => 0x52,
            "S" => 0x53,
            "T" => 0x54,
            "U" => 0x55,
            "V" => 0x56,
            "W" => 0x57,
            "X" => 0x58,
            "Y" => 0x59,
            "Z" => 0x5A,
            "0" => 0x30,
            "1" => 0x31,
            "2" => 0x32,
            "3" => 0x33,
            "4" => 0x34,
            "5" => 0x35,
            "6" => 0x36,
            "7" => 0x37,
            "8" => 0x38,
            "9" => 0x39,
            _ => 0
        };
    }

    public void Dispose()
    {
        Stop();
    }
}
