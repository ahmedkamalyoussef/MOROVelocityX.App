using System;

namespace MOROVelocityX.Models;

public sealed class ActivatedLicenseInfo
{
    public string LicenseCode { get; set; } = string.Empty;
    public LicenseType Type { get; set; }
    public string HardwareFingerprint { get; set; } = string.Empty;
    public DateTime ActivatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
}
