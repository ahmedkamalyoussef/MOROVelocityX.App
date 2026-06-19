using Avalonia;
using System;
using System.IO;
using MOROVelocityX.Tools;

namespace MOROVelocityX;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "--generate-license-codes")
            {
                var outputPath = args.Length > 1
                    ? args[1]
                    : Path.Combine(AppContext.BaseDirectory, "license_codes.txt");
                LicenseCodeExporter.ExportToFile(outputPath);
                Console.WriteLine($"License codes exported to: {outputPath}");
                return;
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL: {ex.GetType().FullName}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
