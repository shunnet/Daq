using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// 系统监控采样器（1s）：本机 CPU 总负载 / 本机内存占用率（对齐 DAQ 原版 LibreHardwareMonitor 的系统级语义，非本进程）。
/// Windows 用 PerformanceCounter + GlobalMemoryStatusEx；非 Windows 环境 CPU 降级为 0。
/// </summary>
public class MonitorSampler : IDisposable
{
    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(1));
    private readonly CancellationTokenSource _cts = new();
    private PerformanceCounter? _cpuCounter;
    private Task? _loop;

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
        try
        {
            // 本机 CPU 总负载（所有核）
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        }
        catch
        {
            _cpuCounter = null;
        }
        _loop ??= LoopAsync();
    }

    private async Task LoopAsync()
    {
        while (await _timer.WaitForNextTickAsync(_cts.Token))
        {
            var cpu = _cpuCounter?.NextValue() ?? 0;
            var ms = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            double totalMb = 0, ramPercent = 0;
            if (GlobalMemoryStatusEx(ref ms))
            {
                totalMb = ms.ullTotalPhys / 1024.0 / 1024.0;
                ramPercent = ms.dwMemoryLoad;
            }
            Sample?.Invoke(new MonitorSample(
                Math.Clamp(cpu, 0, 100),
                Math.Clamp(ramPercent, 0, 100),
                totalMb));
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _timer.Dispose();
        _cpuCounter?.Dispose();
        GC.SuppressFinalize(this);
    }
}
