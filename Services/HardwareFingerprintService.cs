using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace MOROVelocityX.Services;

public sealed class HardwareFingerprintService
{
    public string GenerateFingerprint()
    {
        var cpuId = GetCpuId();
        var diskId = GetDiskId();
        var motherboardId = GetMotherboardId();

        var raw = $"{cpuId}|{diskId}|{motherboardId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }

    private static string GetCpuId()
    {
        if (OperatingSystem.IsLinux())
        {
            return ReadLinuxCpuId();
        }

        if (OperatingSystem.IsWindows())
        {
            return RunCommand("wmic", "cpu get ProcessorId") ?? "unknown-cpu";
        }

        return Environment.ProcessorCount + "-" + Environment.Is64BitOperatingSystem;
    }

    private static string GetDiskId()
    {
        if (OperatingSystem.IsLinux())
        {
            if (File.Exists("/etc/machine-id"))
            {
                var machineId = File.ReadAllText("/etc/machine-id").Trim();
                if (!string.IsNullOrEmpty(machineId))
                {
                    return machineId;
                }
            }

            var serial = RunCommand("lsblk", "-dn -o SERIAL /dev/sda 2>/dev/null");
            if (!string.IsNullOrWhiteSpace(serial))
            {
                return serial.Trim();
            }

            return ReadSysFile("/sys/class/dmi/id/product_uuid");
        }

        if (OperatingSystem.IsWindows())
        {
            return RunCommand("wmic", "diskdrive get SerialNumber") ?? "unknown-disk";
        }

        return "unknown-disk";
    }

    private static string GetMotherboardId()
    {
        if (OperatingSystem.IsLinux())
        {
            var boardSerial = ReadSysFile("/sys/class/dmi/id/board_serial");
            if (!string.IsNullOrEmpty(boardSerial) && boardSerial != "None")
            {
                return boardSerial;
            }

            return ReadSysFile("/sys/class/dmi/id/product_uuid");
        }

        if (OperatingSystem.IsWindows())
        {
            return RunCommand("wmic", "baseboard get SerialNumber") ?? "unknown-board";
        }

        return "unknown-board";
    }

    private static string ReadLinuxCpuId()
    {
        try
        {
            if (!File.Exists("/proc/cpuinfo"))
            {
                return "unknown-cpu";
            }

            var model = string.Empty;
            var physicalId = string.Empty;

            foreach (var line in File.ReadLines("/proc/cpuinfo"))
            {
                if (line.StartsWith("model name", StringComparison.OrdinalIgnoreCase))
                {
                    model = line.Split(':', 2)[1].Trim();
                }
                else if (line.StartsWith("physical id", StringComparison.OrdinalIgnoreCase))
                {
                    physicalId = line.Split(':', 2)[1].Trim();
                }
            }

            if (!string.IsNullOrEmpty(model))
            {
                return $"{model}-{physicalId}";
            }
        }
        catch
        {
        }

        return "unknown-cpu";
    }

    private static string ReadSysFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return File.ReadAllText(path).Trim();
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private static string? RunCommand(string fileName, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length >= 2)
            {
                return lines[1];
            }

            return output.Trim();
        }
        catch
        {
            return null;
        }
    }
}
