using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.@interface;
using System.Collections.Concurrent;

namespace Snet.Iot.Daq.Core.handler;

/// <summary>两端共用的设备配置比较规则，只对采集和转发行为产生影响的变更触发重订阅。</summary>
public static class DeviceSettings
{
    /// <summary>生成与集合顺序无关的运行配置签名；调用方需保证配置树在读取过程中不被修改。</summary>
    /// <param name="deviceNode">包含采集插件配置及父级关系的设备节点。</param>
    /// <param name="newDict">设备全部地址与 MQ 绑定的快照，未绑定 MQ 的地址也必须包含在内。</param>
    /// <returns>用于前后比较的稳定字符串，不包含运行状态、最后更新时间和树选择状态。</returns>
    public static string CreateSignature(IProjectTreeViewModel deviceNode,
        ConcurrentDictionary<IAddressModel, List<PluginConfigModel>> newDict)
    {
        var config = deviceNode.DaqDetails!;
        // 组包与 WebApi 独立于 Param；签名必须涵盖所有影响订阅、数据加工和转发的配置。
        var ap = config.AutoPack;
        var wa = config.WebApi;
        var autoPackSig = ap is null ? "0" : $"{ap.MaxByteLength}|{ap.Format}|{ap.IsStringReverseByteWord}";
        var webApiSig = wa is null ? "0" : $"{wa.IpAddress}|{wa.Port}|{wa.CrossDomain}";
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            config.Guid,
            config.Name,
            config.SN,
            config.Param,
            autoPackSig,
            webApiSig,
            Path = deviceNode.GetHierarchyPath(),
            Addresses = newDict.OrderBy(kv => kv.Key.Guid, StringComparer.Ordinal).Select(kv => new
            {
                kv.Key.Guid,
                kv.Key.Address,
                kv.Key.AnotherName,
                kv.Key.Type,
                kv.Key.Length,
                kv.Key.EncodingType,
                kv.Key.ExpandParam,
                kv.Key.Topic,
                kv.Key.SimplifyValue,
                kv.Key.Describe,
                Mq = kv.Value.OrderBy(m => m.Guid, StringComparer.Ordinal).Select(m => new { m.Guid, m.Name, m.SN, m.Param })
            })
        });
    }

}
