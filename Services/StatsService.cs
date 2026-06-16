using System;
using System.Collections.Generic;
using System.Timers;

namespace MOROVelocityX.Services;

public class StatsService : IDisposable
{
    private readonly Queue<DateTime> _clickTimestamps = new();
    private long _totalClicks;
    private double _currentCps;
    private double _currentFps;
    private DateTime _lastClickTime = DateTime.MinValue;
    private DateTime _lastFrameTime = DateTime.UtcNow;
    private readonly Queue<double> _frameDurations = new();
    private readonly Timer _updateTimer;
    private readonly object _lock = new();
    private const int CpsWindowMs = 1000;
    private const int CpsResetMs = 1500;
    private const int UpdateIntervalMs = 50;

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

    public long TotalClicks
    {
        get { lock (_lock) return _totalClicks; }
        private set { lock (_lock) _totalClicks = value; }
    }

    public event Action? StatsUpdated;

    public StatsService()
    {
        _lastFrameTime = DateTime.UtcNow;
        _updateTimer = new Timer(UpdateIntervalMs);
        _updateTimer.Elapsed += OnUpdateTimer;
        _updateTimer.AutoReset = true;
        _updateTimer.Start();
    }

    public void RecordClick()
    {
        lock (_lock)
        {
            _totalClicks++;
            _lastClickTime = DateTime.UtcNow;
            _clickTimestamps.Enqueue(_lastClickTime);

            while (_clickTimestamps.Count > 0 &&
                   (DateTime.UtcNow - _clickTimestamps.Peek()).TotalMilliseconds > CpsWindowMs)
            {
                _clickTimestamps.Dequeue();
            }

            _currentCps = _clickTimestamps.Count;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _totalClicks = 0;
            _currentCps = 0;
            _clickTimestamps.Clear();
            _lastClickTime = DateTime.MinValue;
        }
    }

    private void OnUpdateTimer(object? sender, ElapsedEventArgs e)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            double elapsed = (now - _lastFrameTime).TotalSeconds;
            _lastFrameTime = now;

            if (elapsed > 0)
            {
                double fps = 1.0 / elapsed;
                _frameDurations.Enqueue(fps);
                while (_frameDurations.Count > 10)
                    _frameDurations.Dequeue();

                double sum = 0;
                foreach (var f in _frameDurations)
                    sum += f;
                _currentFps = sum / _frameDurations.Count;
            }

            if (_lastClickTime != DateTime.MinValue &&
                (now - _lastClickTime).TotalMilliseconds > CpsResetMs)
            {
                _currentCps = 0;
            }
        }

        StatsUpdated?.Invoke();
    }

    public void Dispose()
    {
        _updateTimer?.Stop();
        _updateTimer?.Dispose();
    }
}
