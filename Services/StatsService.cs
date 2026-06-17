using System;
using System.Threading;
using System.Timers;

namespace MOROVelocityX.Services;

public class StatsService : IDisposable
{
    private long _totalClicks;
    private long _clicksInWindow;
    private long _windowStartMs;
    private double _currentCps;
    private double _currentFps;
    private long _lastClickMs;
    private long _lastFrameMs;
    private long _frameCount;
    private readonly System.Timers.Timer _updateTimer;
    private readonly object _lock = new();
    private const int CpsResetMs = 1500;
    private const int UpdateIntervalMs = 100;

    public double CurrentCps
    {
        get { lock (_lock) return _currentCps; }
        private set { lock (_lock) _currentCps = value; }
    }

    public double CurrentFps
    {
        get { lock (_lock) return _currentFps; }
        private set { lock (_lock) _currentFps = value; }
    }

    public long TotalClicks => Interlocked.Read(ref _totalClicks);

    public event Action? StatsUpdated;

    public StatsService()
    {
        _lastFrameMs = Environment.TickCount64;
        _windowStartMs = _lastFrameMs;
        _updateTimer = new System.Timers.Timer(UpdateIntervalMs);
        _updateTimer.Elapsed += OnUpdateTimer;
        _updateTimer.AutoReset = true;
        _updateTimer.Start();
    }

    public void RecordClick()
    {
        Interlocked.Increment(ref _totalClicks);
        Interlocked.Increment(ref _clicksInWindow);
        Volatile.Write(ref _lastClickMs, Environment.TickCount64);
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _totalClicks, 0);
        Interlocked.Exchange(ref _clicksInWindow, 0);
        lock (_lock)
        {
            _currentCps = 0;
            _lastClickMs = 0;
            _windowStartMs = Environment.TickCount64;
        }
    }

    private void OnUpdateTimer(object? sender, ElapsedEventArgs e)
    {
        long now = Environment.TickCount64;

        lock (_lock)
        {
            _frameCount++;
            long elapsed = now - _lastFrameMs;
            if (elapsed >= 500)
            {
                _currentFps = _frameCount * 1000.0 / elapsed;
                _frameCount = 0;
                _lastFrameMs = now;
            }

            long windowElapsed = now - _windowStartMs;
            if (windowElapsed >= 1000)
            {
                _currentCps = _clicksInWindow * 1000.0 / windowElapsed;
                Interlocked.Exchange(ref _clicksInWindow, 0);
                _windowStartMs = now;
            }

            long lastClick = Volatile.Read(ref _lastClickMs);
            if (lastClick > 0 && now - lastClick > CpsResetMs)
                _currentCps = 0;
        }

        StatsUpdated?.Invoke();
    }

    public void Dispose()
    {
        _updateTimer?.Stop();
        _updateTimer?.Dispose();
    }
}
