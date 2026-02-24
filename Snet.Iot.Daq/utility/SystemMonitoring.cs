using LibreHardwareMonitor.Hardware;
using System.Diagnostics;
using System.Management;
using System.Security.Principal;

namespace Snet.Iot.Daq.utility
{
    /// <summary>
    /// 系统监控器
    /// </summary>
    public class SystemMonitoring
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
        /// 传感器信息类型
        /// </summary>
        public class SensorDataType
        {
            public string Key { get; set; }
            public string Value { get; set; }
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
            /// 系统名称
            /// </summary>
            public string SystemName { get; set; }

            /// <summary>
            /// 系统版本
            /// </summary>
            public string SystemVer { get; set; }

            /// <summary>
            /// 系统运行时间
            /// </summary>
            public string SystemRunTime { get; set; }

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
        /// 构造函数
        /// </summary>
        public SystemMonitoring()
        {
            if (computer == null)
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
        }

        /// <summary>
        /// 初始化
        /// </summary>
        /// <returns></returns>
        public void Init()
        {
            computer.Open();
        }

        /// <summary>
        /// 结束
        /// </summary>
        public void End()
        {
            computer.Close();
        }

        /// <summary>
        /// 硬件数据获取
        /// </summary>
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
                    string SensorsNameCn = GetSensorsNameCn(hardware.Sensors[i].SensorType);
                    if (!string.IsNullOrEmpty(hardware.Sensors[i].Value.ToString()))
                    {
                        hardwareDataType.Values.Add(new SensorDataType() { Key = $"{SensorsNameCn},{hardware.Sensors[i].Name}", Value = hardware.Sensors[i].Value.ToString() });
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

        private string GetGpuInfo()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name,DriverVersion FROM Win32_VideoController");
                return string.Join("；", searcher.Get().Cast<ManagementObject>().Select(m => $"{m["Name"]} / 驱动 {m["DriverVersion"]}"));
            }
            catch { return "未知显卡"; }
        }

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

        private sealed class UpdateVisitor : IVisitor
        {
            public void VisitComputer(IComputer computer) => computer.Traverse(this);
            public void VisitHardware(IHardware hardware)
            {
                hardware.Update();
                foreach (var sub in hardware.SubHardware) sub.Accept(this);
            }
            public void VisitSensor(ISensor sensor) { }
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