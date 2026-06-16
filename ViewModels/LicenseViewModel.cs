using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using MOROVelocityX.Models;
using MOROVelocityX.Services;

namespace MOROVelocityX.ViewModels;

public partial class LicenseViewModel : ViewModelBase
{
    private string _licenseCode = string.Empty;
    private string _statusMessage = string.Empty;
    private LicenseState _state = LicenseState.NotActivated;
    private string _hardwareFingerprint = string.Empty;
    private bool _isBusy;

    public LicenseViewModel(LicenseService licenseService, LicenseValidationResult? initialResult = null)
    {
        _licenseService = licenseService;
        ActivateCommand = new RelayCommand(Activate, () => !IsBusy && !string.IsNullOrWhiteSpace(LicenseCode));
        ExitCommand = new RelayCommand(Exit);

        HardwareFingerprint = licenseService.HardwareFingerprint;
        ApplyValidation(initialResult ?? licenseService.ValidateOnStartup());
    }

    public event EventHandler? ActivationSucceeded;
    public event EventHandler? ExitRequested;

    public string LicenseCode
    {
        get => _licenseCode;
        set
        {
            if (SetProperty(ref _licenseCode, value))
            {
                ((RelayCommand)ActivateCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public LicenseState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(IsBlocked));
            }
        }
    }

    public string StateText => State switch
    {
        LicenseState.Active => "Active",
        LicenseState.Expired => "Expired",
        LicenseState.Invalid => "Invalid",
        _ => "Not Activated"
    };

    public bool IsBlocked => State != LicenseState.Active;

    public string HardwareFingerprint
    {
        get => _hardwareFingerprint;
        private set => SetProperty(ref _hardwareFingerprint, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ((RelayCommand)ActivateCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public ICommand ActivateCommand { get; }
    public ICommand ExitCommand { get; }

    private readonly LicenseService _licenseService;

    private void Activate()
    {
        IsBusy = true;
        try
        {
            var result = _licenseService.Activate(LicenseCode);
            StatusMessage = result.Message;
            State = result.State;

            if (result.Success)
            {
                ActivationSucceeded?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Exit()
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyValidation(LicenseValidationResult result)
    {
        State = result.State;
        StatusMessage = result.Message;

        if (result.License != null)
        {
            LicenseCode = result.License.LicenseCode;
        }
    }
}
