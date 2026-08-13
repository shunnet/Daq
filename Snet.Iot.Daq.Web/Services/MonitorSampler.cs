using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// 系统监控采样器（1s）：本机 CPU 总负载 / 本机内存占用率（对齐 DAQ 原版 LibreHardwareMonitor 的系统级语义，非本进程）。
/// 跨平台：Windows 用 PerformanceCounter + GlobalMemoryStatusEx；Linux 读 /proc/stat（CPU 差值）+ /proc/meminfo；
/// 其他平台（macOS 等）无 API 可读，CPU/内存均降级为 0（采样循环不受影响）。
/// </summary>
public class MonitorSampler : IDisposable
{
    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(1));
    private readonly CancellationTokenSource _cts = new();
    private PerformanceCounter? _cpuCounter;
    private Task? _loop;
    private long _prevTotal;
    private long _prevIdle;
    private bool _firstCpu = true;

    public event Action<MonitorSample>? Sample;

    public record MonitorSample(double Cpu, double RamPercent, double TotalRamMb);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    public void Start()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                // 本机 CPU 总负载（所有核）
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            }
            catch
            {
                _cpuCounter = null;
            }
        }
        _loop ??= LoopAsync();
    }

    private async Task LoopAsync()
    {
        while (await _timer.WaitForNextTickAsync(_cts.Token))
        {
            Sample?.Invoke(new MonitorSample(
                Math.Clamp(ReadCpu(), 0, 100),
                Math.Clamp(ReadRamPercent(), 0, 100),
                ReadTotalRamMb()));
        }
    }

    private double ReadCpu()
    {
        if (_cpuCounter is not null)
            return _cpuCounter.NextValue();
        if (!OperatingSystem.IsLinux())
            return 0;
        // /proc/stat cpu 行：user nice system idle iowait ...；idle 取 idle+iowait，差值算占用率
        var (total, idle) = ReadProcStat();
        if (_firstCpu)
        {
            _firstCpu = false;
            _prevTotal = total;
            _prevIdle = idle;
            return 0;
        }
        var dTotal = total - _prevTotal;
        var dIdle = idle - _prevIdle;
        _prevTotal = total;
        _prevIdle = idle;
        return dTotal > 0 ? (1 - dIdle / (double)dTotal) * 100 : 0;
    }

    private static (long total, long idle) ReadProcStat()
    {
        try
        {
            var line = File.ReadLines("/proc/stat").FirstOrDefault();
            if (line is null || !line.StartsWith("cpu "))
                return (0, 0);
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(long.Parse).ToArray();
            return (parts.Sum(), parts.Length > 4 ? parts[3] + parts[4] : parts.Sum());
        }
        catch
        {
            return (0, 0);
        }
    }

    private static double ReadTotalRamMb()
    {
        if (OperatingSystem.IsWindows())
        {
            var ms = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            return GlobalMemoryStatusEx(ref ms) ? ms.ullTotalPhys / 1024.0 / 1024.0 : 0;
        }
        if (OperatingSystem.IsLinux())
            return ReadMemInfo().totalKb / 1024.0;
        return 0;
    }

    private static double ReadRamPercent()
    {
        if (OperatingSystem.IsWindows())
        {
            var ms = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            return GlobalMemoryStatusEx(ref ms) ? ms.dwMemoryLoad : 0;
        }
        if (OperatingSystem.IsLinux())
        {
            var (totalKb, availKb) = ReadMemInfo();
            return totalKb > 0 ? (1 - availKb / (double)totalKb) * 100 : 0;
        }
        return 0;
    }

    private static (ulong totalKb, ulong availKb) ReadMemInfo()
    {
        try
        {
            ulong totalKb = 0, availKb = 0;
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:"))
                    totalKb = ParseKb(line);
                else if (line.StartsWith("MemAvailable:"))
                    availKb = ParseKb(line);
            }
            return (totalKb, availKb);
        }
        catch
        {
            return (0, 0);
        }
    }

    private static ulong ParseKb(string line)
    {
        var value = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
        return ulong.TryParse(value, out var kb) ? kb : 0;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _timer.Dispose();
        _cpuCounter?.Dispose();
        GC.SuppressFinalize(this);
    }
}
