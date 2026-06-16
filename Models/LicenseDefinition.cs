using System;

namespace MOROVelocityX.Models;

public sealed class LicenseDefinition
{
    public LicenseDefinition(LicenseType type, TimeSpan? duration)
    {
        Type = type;
        Duration = duration;
    }

    public LicenseType Type { get; }
    public TimeSpan? Duration { get; }
}
