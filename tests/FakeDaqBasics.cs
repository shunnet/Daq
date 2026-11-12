namespace Snet.Iot.Daq.Tests;

/// <summary>桥接回归测试设备的基础配置，形状与常用驱动一致。</summary>
public sealed class FakeDaqBasics
{
    /// <summary>设备唯一标识。</summary>
    public string SN { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>使用真实西门子组包器的协议键。</summary>
    public string ProtocolType { get; set; } = "SiemensS7Net_S1500";
}
