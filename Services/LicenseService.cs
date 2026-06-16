using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MOROVelocityX.Data;
using MOROVelocityX.Models;

namespace MOROVelocityX.Services;

public sealed class LicenseValidationResult
{
    public LicenseState State { get; init; }
    public ActivatedLicenseInfo? License { get; init; }
    public string Message { get; init; } = string.Empty;
    public TimeSpan? Remaining { get; init; }
}

public sealed class LicenseActivationResult
{
    public bool Success { get; init; }
    public LicenseState State { get; init; }
    public string Message { get; init; } = string.Empty;
    public ActivatedLicenseInfo? License { get; init; }
}

public sealed class LicenseService
{
    private readonly HardwareFingerprintService _fingerprintService;
    private readonly EncryptionService _encryptionService;
    private readonly string _licenseFilePath;
    private readonly string _usedCodesFilePath;
    private readonly string _currentFingerprint;

    public LicenseService(
        HardwareFingerprintService fingerprintService,
        EncryptionService encryptionService)
    {
        _fingerprintService = fingerprintService;
        _encryptionService = encryptionService;
        _currentFingerprint = fingerprintService.GenerateFingerprint();

        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MOROVelocityX");
        _licenseFilePath = Path.Combine(appDataDir, "license.dat");
        _usedCodesFilePath = Path.Combine(appDataDir, "used_codes.dat");
    }

    public string HardwareFingerprint => _currentFingerprint;

    public LicenseValidationResult ValidateOnStartup()
    {
        var license = _encryptionService.LoadEncrypted<ActivatedLicenseInfo>(_licenseFilePath);
        if (license == null || string.IsNullOrWhiteSpace(license.LicenseCode))
        {
            return new LicenseValidationResult
            {
                State = LicenseState.NotActivated,
                Message = "No license activated. Please enter a license code."
            };
        }

        return ValidateLicense(license);
    }

    public LicenseActivationResult Activate(string code)
    {
        var normalizedCode = LicenseCodeCatalog.NormalizeCode(code);
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return Fail(LicenseState.Invalid, "Please enter a license code.");
        }

        if (!LicenseCodeCatalog.TryGetDefinition(normalizedCode, out var definition))
        {
            return Fail(LicenseState.Invalid, "Invalid license code.");
        }

        var usedCodes = LoadUsedCodes();
        if (usedCodes.Contains(normalizedCode))
        {
            var existing = _encryptionService.LoadEncrypted<ActivatedLicenseInfo>(_licenseFilePath);
            if (existing != null &&
                string.Equals(existing.LicenseCode, normalizedCode, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.HardwareFingerprint, _currentFingerprint, StringComparison.Ordinal))
            {
                var validation = ValidateLicense(existing);
                return new LicenseActivationResult
                {
                    Success = validation.State == LicenseState.Active,
                    State = validation.State,
                    Message = validation.Message,
                    License = existing
                };
            }

            return Fail(LicenseState.Invalid, "This license code has already been used and cannot be reused.");
        }

        var activatedAt = DateTime.UtcNow;
        DateTime? expiresAt = definition.Duration.HasValue
            ? activatedAt.Add(definition.Duration.Value)
            : null;

        var license = new ActivatedLicenseInfo
        {
            LicenseCode = normalizedCode,
            Type = definition.Type,
            HardwareFingerprint = _currentFingerprint,
            ActivatedAtUtc = activatedAt,
            ExpiresAtUtc = expiresAt
        };

        usedCodes.Add(normalizedCode);
        SaveUsedCodes(usedCodes);
        _encryptionService.SaveEncrypted(_licenseFilePath, license);

        var result = ValidateLicense(license);
        return new LicenseActivationResult
        {
            Success = result.State == LicenseState.Active,
            State = result.State,
            Message = result.State == LicenseState.Active
                ? "License activated successfully."
                : result.Message,
            License = license
        };
    }

    private LicenseValidationResult ValidateLicense(ActivatedLicenseInfo license)
    {
        if (!LicenseCodeCatalog.TryGetDefinition(license.LicenseCode, out _))
        {
            return new LicenseValidationResult
            {
                State = LicenseState.Invalid,
                License = license,
                Message = "Stored license code is not recognized."
            };
        }

        if (!string.Equals(license.HardwareFingerprint, _currentFingerprint, StringComparison.Ordinal))
        {
            return new LicenseValidationResult
            {
                State = LicenseState.Invalid,
                License = license,
                Message = "License is bound to a different machine."
            };
        }

        if (license.ExpiresAtUtc.HasValue)
        {
            var remaining = license.ExpiresAtUtc.Value - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return new LicenseValidationResult
                {
                    State = LicenseState.Expired,
                    License = license,
                    Message = "License has expired.",
                    Remaining = TimeSpan.Zero
                };
            }

            return new LicenseValidationResult
            {
                State = LicenseState.Active,
                License = license,
                Message = $"License active. Expires in {FormatRemaining(remaining)}.",
                Remaining = remaining
            };
        }

        return new LicenseValidationResult
        {
            State = LicenseState.Active,
            License = license,
            Message = "Lifetime license active.",
            Remaining = null
        };
    }

    private static LicenseActivationResult Fail(LicenseState state, string message)
    {
        return new LicenseActivationResult
        {
            Success = false,
            State = state,
            Message = message
        };
    }

    private HashSet<string> LoadUsedCodes()
    {
        var codes = _encryptionService.LoadEncrypted<List<string>>(_usedCodesFilePath);
        return codes != null
            ? new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private void SaveUsedCodes(HashSet<string> codes)
    {
        _encryptionService.SaveEncrypted(_usedCodesFilePath, codes.ToList());
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining.TotalDays >= 1)
        {
            return $"{(int)remaining.TotalDays}d {remaining.Hours}h";
        }

        if (remaining.TotalHours >= 1)
        {
            return $"{(int)remaining.TotalHours}h {remaining.Minutes}m";
        }

        return $"{Math.Max(1, (int)remaining.TotalMinutes)}m";
    }
}
