using Opc.Ua;
using Snet.Model.@enum;
using System.Collections.Frozen;

namespace Snet.Iot.Daq.Core.handler;

/// <summary>WPF 与 Web 共用的 UA 转发类型及地址规则，不持有服务实例或可变运行状态。</summary>
public static class UaForwarding
{
    /// <summary>采集数据类型对应的 UA 内置类型；只读冻结后可安全用于多个设备线程。</summary>
    public static FrozenDictionary<DataType, BuiltInType> TypeMap { get; } = new Dictionary<DataType, BuiltInType>
    {
        [DataType.Byte] = BuiltInType.Byte,
        [DataType.Bool] = BuiltInType.Boolean,
        [DataType.Double] = BuiltInType.Double,
        [DataType.Float] = BuiltInType.Float,
        [DataType.Single] = BuiltInType.Float,
        [DataType.Short] = BuiltInType.Int16,
        [DataType.Int16] = BuiltInType.Int16,
        [DataType.Ushort] = BuiltInType.UInt16,
        [DataType.UInt16] = BuiltInType.UInt16,
        [DataType.Int] = BuiltInType.Int32,
        [DataType.Int32] = BuiltInType.Int32,
        [DataType.Uint] = BuiltInType.UInt32,
        [DataType.UInt32] = BuiltInType.UInt32,
        [DataType.Long] = BuiltInType.Int64,
        [DataType.Int64] = BuiltInType.Int64,
        [DataType.Ulong] = BuiltInType.UInt64,
        [DataType.UInt64] = BuiltInType.UInt64,
        [DataType.String] = BuiltInType.String,
        [DataType.Char] = BuiltInType.String
    }.ToFrozenDictionary();

    /// <summary>按 Core 节点创建规则取得已创建地址的 NodeId，无需获取并扫描完整地址空间。</summary>
    /// <param name="parent">CreateAddress 使用的父文件夹标识；当前 Core 文件夹使用字符串标识。</param>
    /// <param name="addressName">传给 CreateAddress 的原始地址名称。</param>
    /// <returns>保留父文件夹命名空间的完整节点标识。</returns>
    /// <exception cref="ArgumentException">父节点不使用字符串标识，或地址名称为空。</exception>
    public static NodeId CreateAddressNodeId(NodeId parent, string addressName)
    {
        if (!parent.TryGetValue(out string identifier) || string.IsNullOrEmpty(addressName))
            throw new ArgumentException("UA 父节点标识或地址名称不合法");
        return new NodeId(identifier + "." + addressName, parent.NamespaceIndex);
    }
}
