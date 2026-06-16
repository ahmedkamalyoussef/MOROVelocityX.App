using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using MOROVelocityX.Services;

namespace MOROVelocityX.ViewModels;

public enum ApplicationStatus
{
    Ready,
    Running,
    Stopped
}

public partial class MainWindowViewModel : ViewModelBase
{
    private string _triggerKey = "F1";
    private string _clickKey = "Mouse1";
    private bool _isToggleMode = true;
    private bool _isHoldMode = false;
    private int _cps = 10;
    private ApplicationStatus _status = ApplicationStatus.Ready;
    private bool _isCapturingTriggerKey = false;
    private bool _isCapturingClickKey = false;

    public string TriggerKey
    {
        get => _triggerKey;
        set
        {
            if (SetProperty(ref _triggerKey, value))
            {
                _globalHotkeyService.RegisterHotkey(value);
            }
        }
    }

    public string ClickKey
    {
        get => _clickKey;
        set => SetProperty(ref _clickKey, value);
    }

    public bool IsToggleMode
    {
        get => _isToggleMode;
        set
        {
            SetProperty(ref _isToggleMode, value);
            if (value) IsHoldMode = false;
        }
    }

    public bool IsHoldMode
    {
        get => _isHoldMode;
        set
        {
            SetProperty(ref _isHoldMode, value);
            if (value) IsToggleMode = false;
        }
    }

    public int CPS
    {
        get => _cps;
        set => SetProperty(ref _cps, value);
    }

    public ApplicationStatus Status
    {
        get => _status;
        set
        {
            SetProperty(ref _status, value);
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string StatusText => Status.ToString();

    public bool IsCapturingTriggerKey
    {
        get => _isCapturingTriggerKey;
        set => SetProperty(ref _isCapturingTriggerKey, value);
    }

    public bool IsCapturingClickKey
    {
        get => _isCapturingClickKey;
        set => SetProperty(ref _isCapturingClickKey, value);
    }

    public ICommand CaptureTriggerKeyCommand { get; }
    public ICommand CaptureClickKeyCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }

    private readonly GlobalHotkeyService _globalHotkeyService;

    public MainWindowViewModel(GlobalHotkeyService globalHotkeyService)
    {
        _globalHotkeyService = globalHotkeyService;
        CaptureTriggerKeyCommand = new RelayCommand(CaptureTriggerKey);
        CaptureClickKeyCommand = new RelayCommand(CaptureClickKey);
        StartCommand = new RelayCommand(Start);
        StopCommand = new RelayCommand(Stop);

        // Subscribe to global hotkey events
        _globalHotkeyService.HotkeyPressed += OnGlobalHotkeyPressed;
    }

    private void OnGlobalHotkeyPressed(object? sender, string key)
    {
        // Handle global hotkey press (trigger key)
        // For now, just detect it - no macro logic yet
        if (key == TriggerKey)
        {
            // TODO: Implement macro toggle logic when trigger key is pressed globally
        }
    }

    private void CaptureTriggerKey()
    {
        IsCapturingTriggerKey = true;
    }

    private void CaptureClickKey()
    {
        IsCapturingClickKey = true;
    }

    public void OnKeyPressed(string key)
    {
        if (IsCapturingTriggerKey)
        {
            TriggerKey = key;
            IsCapturingTriggerKey = false;
        }
        else if (IsCapturingClickKey)
        {
            ClickKey = key;
            IsCapturingClickKey = false;
        }
    }

    private void Start()
    {
        Status = ApplicationStatus.Running;
    }

    private void Stop()
    {
        Status = ApplicationStatus.Stopped;
    }
}
