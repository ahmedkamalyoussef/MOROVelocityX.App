using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using MOROVelocityX.Models;

namespace MOROVelocityX.Data;

public static class LicenseCodeCatalog
{
    private const string Secret = "MOROVelocityX-License-v1-7k9mPx";

    public static IReadOnlyDictionary<string, LicenseDefinition> All { get; }

    static LicenseCodeCatalog()
    {
        var codes = new Dictionary<string, LicenseDefinition>(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i <= 5; i++)
        {
            AddCode(codes, LicenseType.Lifetime, "LIFE", i, null);
        }

        for (var i = 1; i <= 5; i++)
        {
            AddCode(codes, LicenseType.Temporary, "TEMP", i, TimeSpan.FromMinutes(3));
        }

        for (var i = 1; i <= 200; i++)
        {
            AddCode(codes, LicenseType.Monthly, "MNTH", i, TimeSpan.FromDays(30));
        }

        All = codes;
    }

    public static bool TryGetDefinition(string code, out LicenseDefinition definition)
    {
        return All.TryGetValue(NormalizeCode(code), out definition!);
    }

    public static string NormalizeCode(string code)
    {
        return code.Trim().ToUpperInvariant();
    }

    private static void AddCode(
        Dictionary<string, LicenseDefinition> codes,
        LicenseType type,
        string prefix,
        int index,
        TimeSpan? duration)
    {
        var code = GenerateCode(prefix, index);
        codes[code] = new LicenseDefinition(type, duration);
    }

    internal static string GenerateCode(string prefix, int index)
    {
        var payload = $"{prefix}-{index:D3}";
        var checksum = ComputeChecksum(payload);
        return $"MORO-{prefix}-{index:D3}-{checksum}";
    }

    private static string ComputeChecksum(string payload)
    {
        var key = Encoding.UTF8.GetBytes(Secret);
        var data = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(key, data);
        return Convert.ToHexString(hash, 0, 3);
    }
}
