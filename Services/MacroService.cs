using System;
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
    private bool _isWindows;

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
        IsInputSimulationSupported = _isWindows;
    }

#if WINDOWS
    // Windows APIs for input simulation - only used on Windows
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

            if (!_isWindows)
            {
                MacroError?.Invoke(this, "Macro execution is only supported on Windows");
                return;
            }

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
                // Task was cancelled, this is expected
            }
            catch (OperationCanceledException)
            {
                // Task was cancelled, this is expected
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

                // Perform click action
                PerformClick();

                // Calculate delay based on CPS
                int delayMs = 1000 / _cps;

                // Wait with cancellation support
                await Task.Delay(delayMs, cancellationToken);
            }
        }
        catch (TaskCanceledException)
        {
            // Expected when cancellation is requested
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
            
            // Only raise status changed if we're not being explicitly stopped
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
            if (!_isWindows)
            {
                MacroError?.Invoke(this, "Input simulation is only supported on Windows");
                return;
            }

            string clickKey;
            bool isToggleMode;

            lock (_lock)
            {
                clickKey = _clickKey;
                isToggleMode = _isToggleMode;
            }

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
                    // It's a keyboard key
                    SimulateKeyPress(clickKey);
                    break;
            }
#else
            MacroError?.Invoke(this, "Input simulation is only supported on Windows");
#endif
        }
        catch (Exception ex)
        {
            MacroError?.Invoke(this, $"Click simulation failed: {ex.Message}");
        }
    }

#if WINDOWS
    private void SimulateMouseClick(uint downFlag, uint upFlag)
    {
        try
        {
            mouse_event(downFlag, 0, 0, 0, 0);
            Thread.Sleep(10); // Small delay between down and up
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
                Thread.Sleep(10); // Small delay between down and up
                keybd_event(vkCode, 0, KEYEVENTF_KEYUP, 0);
            }
        }
        catch (Exception ex)
        {
            MacroError?.Invoke(this, $"Key press simulation failed: {ex.Message}");
        }
    }
#else
    private void SimulateMouseClick(uint downFlag, uint upFlag)
    {
        // Not supported on non-Windows platforms
    }

    private void SimulateKeyPress(string key)
    {
        // Not supported on non-Windows platforms
    }
#endif

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
