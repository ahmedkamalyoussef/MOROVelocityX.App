using System;
using System.IO;
using System.Text;
using MOROVelocityX.Data;
using MOROVelocityX.Models;

namespace MOROVelocityX.Tools;

public static class LicenseCodeExporter
{
    public static void ExportToFile(string outputPath)
    {
        var builder = new StringBuilder();
        builder.AppendLine("MOROVelocityX License Codes");
        builder.AppendLine("===========================");
        builder.AppendLine();
        AppendSection(builder, LicenseType.Lifetime, "Lifetime (no expiration)", 5);
        AppendSection(builder, LicenseType.Temporary, "Temporary (3 minutes)", 5);
        AppendSection(builder, LicenseType.Monthly, "Monthly (30 days)", 200);
        File.WriteAllText(outputPath, builder.ToString());
    }

    private static void AppendSection(StringBuilder builder, LicenseType type, string title, int expectedCount)
    {
        builder.AppendLine(title);
        builder.AppendLine(new string('-', title.Length));
        var count = 0;
        foreach (var pair in LicenseCodeCatalog.All)
        {
            if (pair.Value.Type != type)
            {
                continue;
            }

            builder.AppendLine(pair.Key);
            count++;
        }

        builder.AppendLine($"Total: {count} (expected {expectedCount})");
        builder.AppendLine();
    }
}
