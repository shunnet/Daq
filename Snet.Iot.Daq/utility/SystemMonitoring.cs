using LibreHardwareMonitor.Hardware;
using System.Diagnostics;
using System.Management;
using System.Security.Principal;

namespace Snet.Iot.Daq.utility
{
    /// <summary>
    /// 系统监控器（单例），基于 LibreHardwareMonitor 和 WMI 获取 CPU、GPU、内存、硬盘、BIOS、网络等硬件信息。
    /// 实现 IDisposable 以确保底层 Computer 资源被正确释放。
    /// </summary>
    public class SystemMonitoring : IDisposable
    {

        private static readonly Lazy<SystemMonitoring> _instance = new(() => new SystemMonitoring(), true);
        /// <summary>
        /// 获取当前对象（单例模式）
        /// </summary>
        /// <returns></returns>
        public static SystemMonitoring Instance() => _instance.Value;
        private Computer computer;
        private UpdateVisitor updateVisitor = new UpdateVisitor();

        /// <summary>
        /// 传感器信息类型，表示单个传感器的键值对数据。
        /// </summary>
        public class SensorDataType
        {
            /// <summary>传感器类型与名称组合键，例如 "负载,CPU Total"</summary>
            public string Key { get; set; } = string.Empty;
            /// <summary>传感器当前数值（字符串形式）</summary>
            public string Value { get; set; } = string.Empty;
        }

        /// <summary>
        /// 硬件信息类型
        /// </summary>
        public class HardwareDataType : SensorDataType
        {
            public HardwareDataType()
            {
                Values = new List<SensorDataType>();
            }
            public List<SensorDataType> Values { get; set; }
        }

        /// <summary>
        /// 硬件信息
        /// </summary>
        public class HardwareData
        {
            public HardwareData()
            {
                Info = new List<HardwareDataType>();
            }

            /// <summary>
            /// 信息
            /// </summary>
            public List<HardwareDataType> Info { get; set; }

            /// <summary>
            /// 系统名称（计算机名）
            /// </summary>
            public string SystemName { get; set; } = string.Empty;

            /// <summary>
            /// 系统版本（操作系统名称）
            /// </summary>
            public string SystemVer { get; set; } = string.Empty;

            /// <summary>
            /// 系统运行时间（格式：X天X小时X分钟）
            /// </summary>
            public string SystemRunTime { get; set; } = string.Empty;

            /// <summary>
            /// 处理器信息
            /// </summary>
            public string CpuInfo { get; set; } = string.Empty;
            /// <summary>
            /// 内存信息
            /// </summary>
            public string MemoryInfo { get; set; } = string.Empty;
            /// <summary>
            /// 硬盘信息
            /// </summary>
            public string DiskInfo { get; set; } = string.Empty;
            /// <summary>
            /// 显卡信息
            /// </summary>
            public string GpuInfo { get; set; } = string.Empty;
            /// <summary>
            /// bios信息
            /// </summary>
            public string BiosInfo { get; set; } = string.Empty;
            /// <summary>
            /// 网络信息
            /// </summary>
            public string NetworkInfo { get; set; } = string.Empty;
        }

        /// <summary>
        /// 构造函数，初始化 LibreHardwareMonitor 的 Computer 实例并启用所有硬件监控。
        /// </summary>
        public SystemMonitoring()
        {
            computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true,
                IsNetworkEnabled = true,
                IsStorageEnabled = true
            };
            Init();
        }

        /// <summary>
        /// 初始化硬件监控，打开底层驱动连接。
        /// </summary>
        public void Init()
        {
            computer.Open();
        }

        /// <summary>
        /// 结束硬件监控，关闭底层驱动连接。
        /// </summary>
        public void End()
        {
            computer.Close();
        }

        /// <summary>
        /// 释放资源，关闭 Computer 连接。
        /// </summary>
        public void Dispose()
        {
            computer?.Close();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 获取当前硬件传感器数据。当 baseInfo 为 true 时，同时并行获取系统基本信息（系统名、版本、运行时间、CPU、内存、硬盘、GPU、BIOS、网络）。
        /// </summary>
        /// <param name="baseInfo">是否同时获取系统基本信息</param>
        /// <returns>包含所有硬件传感器数据和可选基本信息的 HardwareData 对象</returns>
        public HardwareData GetInfo(bool baseInfo = false)
        {
            computer.Accept(updateVisitor);
            HardwareData hardwareData = new HardwareData();
            if (baseInfo)
            {
                Task.WaitAll(
                    Task.Run(() => hardwareData.SystemName = GetSystemName()),
                        Task.Run(() => hardwareData.SystemVer = GetSystemVer()),
                        Task.Run(() => hardwareData.SystemRunTime = GetSystemRunTime()),
                        Task.Run(() => hardwareData.CpuInfo = GetCpuInfo()),
                        Task.Run(() => hardwareData.MemoryInfo = GetMemoryInfo()),
                        Task.Run(() => hardwareData.DiskInfo = GetDiskInfo()),
                        Task.Run(() => hardwareData.GpuInfo = GetGpuInfo()),
                        Task.Run(() => hardwareData.BiosInfo = GetBiosInfo()),
                        Task.Run(() => hardwareData.NetworkInfo = GetNetworkInfo()));
            }
            foreach (IHardware hardware in computer.Hardware)  //硬件
            {
                HardwareDataType hardwareDataType = new HardwareDataType() { Key = GetHardwareNameCn(hardware), Value = hardware.Name };
                for (int i = 0; i < hardware.Sensors.Length; i++)
                {
                    string sensorsNameCn = GetSensorsNameCn(hardware.Sensors[i].SensorType);
                    if (hardware.Sensors[i].Value.HasValue)
                    {
                        hardwareDataType.Values.Add(new SensorDataType() { Key = $"{sensorsNameCn},{hardware.Sensors[i].Name}", Value = hardware.Sensors[i].Value.ToString()! });
                    }
                }
                hardwareData.Info.Add(hardwareDataType);
            }
            return hardwareData;
        }

        /// <summary>
        /// 获取系统版本
        /// </summary>
        /// <returns></returns>
        private string GetSystemVer()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem");
                foreach (var os in searcher.Get().Cast<ManagementObject>())
                    return os["Caption"]?.ToString() ?? "未知系统";
            }
            catch { }
            return "未知系统";
        }
        /// <summary>
        /// 通过 WMI 获取处理器信息（名称、核心数、线程数）。
        /// </summary>
        /// <returns>处理器描述字符串</returns>
        private string GetCpuInfo()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name,NumberOfCores,NumberOfLogicalProcessors FROM Win32_Processor");
                var info = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                return info == null ? "未知CPU" : $"{info["Name"]} / {info["NumberOfCores"]}核{info["NumberOfLogicalProcessors"]}线程";
            }
            catch { return "未知CPU"; }
        }

        /// <summary>
        /// 通过 WMI 获取物理内存总容量（单位 GB）。
        /// </summary>
        /// <returns>内存容量描述字符串</returns>
        private string GetMemoryInfo()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory");
                var total = searcher.Get().Cast<ManagementObject>().Sum(m => Convert.ToInt64(m["Capacity"]));
                return $"{Math.Round(total / 1024.0 / 1024 / 1024, 1)} GB";
            }
            catch { return "未知内存"; }
        }

        /// <summary>
        /// 通过 WMI 获取所有物理硬盘的型号与容量。
        /// </summary>
        /// <returns>硬盘信息描述字符串，多块硬盘用 "；" 分隔</returns>
        private string GetDiskInfo()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Model,Size FROM Win32_DiskDrive");
                return string.Join("；", searcher.Get().Cast<ManagementObject>().Select(m =>
                {
                    var size = Convert.ToInt64(m["Size"]) / 1024.0 / 1024 / 1024;
                    return $"{m["Model"]} ({Math.Round(size, 1)} GB)";
                }));
            }
            catch { return "未知硬盘"; }
        }

        /// <summary>
        /// 通过 WMI 获取所有显卡的名称与驱动版本。
        /// </summary>
        /// <returns>显卡信息描述字符串，多显卡用 "；" 分隔</returns>
        private string GetGpuInfo()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name,DriverVersion FROM Win32_VideoController");
                return string.Join("；", searcher.Get().Cast<ManagementObject>().Select(m => $"{m["Name"]} / 驱动 {m["DriverVersion"]}"));
            }
            catch { return "未知显卡"; }
        }

        /// <summary>
        /// 通过 WMI 获取 BIOS 的厂商、版本及发布日期信息。
        /// </summary>
        /// <returns>BIOS 信息描述字符串</returns>
        private string GetBiosInfo()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Manufacturer,SMBIOSBIOSVersion,ReleaseDate FROM Win32_BIOS");
                var info = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                if (info == null) return "未知BIOS";
                return $"{info["Manufacturer"]} {info["SMBIOSBIOSVersion"]} ({info["ReleaseDate"]?.ToString()?.Substring(0, 8)})";
            }
            catch { return "未知BIOS"; }
        }

        /// <summary>
        /// 通过 WMI 获取所有具有 MAC 地址的网络适配器信息。
        /// </summary>
        /// <returns>网络适配器信息描述字符串，多适配器用 "；" 分隔</returns>
        private string GetNetworkInfo()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name,MACAddress FROM Win32_NetworkAdapter WHERE MACAddress IS NOT NULL");
                return string.Join("；", searcher.Get().Cast<ManagementObject>().Select(m => $"{m["Name"]} [{m["MACAddress"]}]"));
            }
            catch { return "未知网络"; }
        }

        /// <summary>
        /// 获取系统名称
        /// </summary>
        /// <returns></returns>
        private string GetSystemName() => Environment.MachineName;

        /// <summary>
        /// 获取系统运行时间
        /// </summary>
        /// <returns></returns>
        private string GetSystemRunTime()
        {
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            return $"{(int)uptime.TotalDays}天{uptime.Hours}小时{uptime.Minutes}分钟";
        }
        /// <summary>
        /// 获取硬件名称
        /// </summary>
        /// <returns></returns>
        private string GetHardwareNameCn(IHardware hardware) => hardware.HardwareType switch
        {
            HardwareType.Motherboard => "主板",
            HardwareType.SuperIO => "IO芯片",
            HardwareType.Cpu => "处理器",
            HardwareType.Memory => "内存",
            HardwareType.GpuNvidia => "英伟达显卡",
            HardwareType.GpuAmd => "AMD显卡",
            HardwareType.GpuIntel => "英特尔显卡",
            HardwareType.Storage => "硬盘",
            HardwareType.Network => "网络",
            HardwareType.Cooler => "散热器",
            HardwareType.EmbeddedController => "嵌入式控制器",
            HardwareType.Psu => "电源",
            HardwareType.Battery => "电池",
            _ => "未知硬件"
        };
        /// <summary>
        /// 获取传感器名称
        /// </summary>
        /// <returns></returns>
        private string GetSensorsNameCn(SensorType type) => type switch
        {
            SensorType.Voltage => "电压",
            SensorType.Current => "电流",
            SensorType.Power => "功率",
            SensorType.Clock => "时钟",
            SensorType.Temperature => "温度",
            SensorType.Load => "负载",
            SensorType.Frequency => "频率",
            SensorType.Fan => "风扇转速",
            SensorType.Flow => "流量",
            SensorType.Control => "控制器",
            SensorType.Level => "电平",
            SensorType.Factor => "系数",
            SensorType.Data => "数据区",
            SensorType.SmallData => "小数据",
            SensorType.Throughput => "吞吐量",
            SensorType.TimeSpan => "时间间隔",
            SensorType.Energy => "能耗",
            SensorType.Noise => "噪声",
            SensorType.Conductivity => "电导率",
            SensorType.Humidity => "湿度",
            _ => "未知传感器"
        };

        /// <summary>
        /// 硬件更新访问者，递归遍历所有硬件和子硬件并触发数据更新。
        /// </summary>
        private sealed class UpdateVisitor : IVisitor
        {
            /// <summary>访问计算机节点，触发遍历所有硬件</summary>
            public void VisitComputer(IComputer computer) => computer.Traverse(this);

            /// <summary>访问硬件节点，更新传感器数据并递归访问子硬件</summary>
            public void VisitHardware(IHardware hardware)
            {
                hardware.Update();
                foreach (var sub in hardware.SubHardware) sub.Accept(this);
            }

            /// <summary>访问传感器节点（无需额外处理）</summary>
            public void VisitSensor(ISensor sensor) { }

            /// <summary>访问参数节点（无需额外处理）</summary>
            public void VisitParameter(IParameter parameter) { }
        }

        /// <summary>
        /// 判断当前程序是否以管理员身份运行<br/>
        /// </summary>
        public bool IsAdmin()
        {
            using var id = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(id);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        /// <summary>
        /// 检测程序是否已经存在多开<br/>
        /// </summary>
        public bool IsOpen()
        {
            var current = Process.GetCurrentProcess();
            return Process.GetProcessesByName(current.ProcessName).Any(p => p.Id != current.Id);
        }
    }
}