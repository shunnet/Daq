using Snet.Core.handler;
using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.@interface;
using Snet.Model.data;
using Snet.Model.@enum;
using Snet.Model.@interface;
using Snet.Opc.ua.service;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;

namespace Snet.Iot.Daq.Core.handler;

/// <summary>
/// WPF 与 Web 共用的 OPC UA 转发入口。
/// <para>转发方式：把设备采集通道正在使用的 <see cref="IDaq"/> 对象直接注入 Snet.Opc 服务端
/// （<see cref="OpcUaServiceOperate.RegisterDaqAsync"/>），不再额外建立连接。服务端接管后自行完成节点创建、
/// 采集订阅、断线重连，并把 OPC 客户端对点位的写入反写到设备；Daq 不再逐点创建节点、映射 NodeId 或写值。</para>
/// <para>生命周期：设备归采集通道所有，服务端只“借用”。平台把 <c>OpcUaServiceData.Basics.CloseDaqOnStop</c> 默认设为 false
/// （见 <see cref="ApplyPlatformDefaults"/>），服务端停止或释放时只取消注册、不会关闭设备；
/// 设备停止或销毁前由 <see cref="UnregisterAsync"/> 先取消注册，再由采集通道释放对象。
/// 设备参数或点位变更会重启设备并换成新的 IDaq 对象，下次采集时按新配置重新注册，无需重启 OPC UA 服务端。</para>
/// </summary>
public static class UaForwarding
{
    /// <summary>
    /// Snet.Opc 桥接支持的点位数据类型，必须与 Snet.Opc 的 OpcUaDaqTypeMap 保持一致（有测试逐项比对）；
    /// 桥接遇到不支持的类型会拒绝整台设备，所以在注册前先筛掉并提示。
    /// </summary>
    private static readonly FrozenSet<DataType> SupportedTypes = new[]
    {
        DataType.Bool, DataType.BoolArray, DataType.Byte, DataType.ByteArray,
        DataType.Short, DataType.Int16, DataType.ShortArray, DataType.Int16Array,
        DataType.Ushort, DataType.UInt16, DataType.UshortArray, DataType.UInt16Array,
        DataType.Int, DataType.Int32, DataType.IntArray, DataType.Int32Array,
        DataType.Uint, DataType.UInt32, DataType.UintArray, DataType.UInt32Array,
        DataType.Long, DataType.Int64, DataType.LongArray, DataType.Int64Array,
        DataType.Ulong, DataType.UInt64, DataType.UlongArray, DataType.UInt64Array,
        DataType.Float, DataType.Single, DataType.FloatArray, DataType.SingleArray,
        DataType.Double, DataType.DoubleArray, DataType.String, DataType.Char, DataType.DateTime
    }.ToFrozenSet();

    /// <summary>每个 UA 服务实例各自的设备登记表；弱键，服务释放回收后条目随之回收。</summary>
    private static readonly ConditionalWeakTable<OpcUaServiceOperate, UaDeviceRegistry> Registries = new();

    /// <summary>读取服务实例对应的登记表（不存在则创建）。</summary>
    private static UaDeviceRegistry RegistryOf(OpcUaServiceOperate service)
        => Registries.GetValue(service, static _ => new UaDeviceRegistry());

    /// <summary>指定数据类型能否被 Snet.Opc 桥接发布。</summary>
    /// <param name="type">点位数据类型。</param>
    public static bool IsSupportedType(DataType type) => SupportedTypes.Contains(type);

    /// <summary>
    /// 套用采集平台对 OPC UA 服务端的默认值：设备由采集通道拥有、服务端只借用，所以 <c>CloseDaqOnStop</c> 默认为 false。
    /// 配置里已有该键时尊重用户的取值；没有（新建配置或旧版配置文件）才套用默认。
    /// </summary>
    /// <param name="basics">已解析出的服务端基础配置。</param>
    /// <param name="json">配置原文；新建配置时传 null。</param>
    /// <returns>传入的同一个配置对象，便于链式使用。</returns>
    public static OpcUaServiceData.Basics ApplyPlatformDefaults(OpcUaServiceData.Basics basics, string? json)
    {
        ArgumentNullException.ThrowIfNull(basics);
        if (string.IsNullOrWhiteSpace(json) || !json.Contains(nameof(OpcUaServiceData.Basics.CloseDaqOnStop), StringComparison.Ordinal))
            basics.CloseDaqOnStop = false;
        return basics;
    }

    /// <summary>
    /// 热路径检查：该 IDaq 对象在该服务上当前是否无需任何动作（已注册、注册进行中、失败退避中）。
    /// 不修改状态、不分配，可以在每个数据批次前调用；返回 false 时再启动后台注册。
    /// </summary>
    /// <param name="service">当前 OPC UA 服务实例。</param>
    /// <param name="deviceKey">设备键（插件配置 Guid）。</param>
    /// <param name="daq">设备采集通道当前使用的 IDaq 对象。</param>
    public static bool IsUpToDate(OpcUaServiceOperate service, string deviceKey, IDaq daq)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(daq);
        return RegistryOf(service).IsSettled(deviceKey, daq);
    }

    /// <summary>
    /// 确保设备的 IDaq 对象已注入到指定 UA 服务；可以反复调用，同一对象已注册时直接返回。
    /// 注册会等待服务端的生命周期门（服务启动、停止期间可能要数秒），调用方应放到后台任务里执行，不要在采集主循环里 await。
    /// 失败时不会关闭或释放 <paramref name="daq"/>（它归采集通道所有）。
    /// </summary>
    /// <param name="service">当前 OPC UA 服务实例（运行中或已创建未启动均可，服务启动前后均允许注册）。</param>
    /// <param name="plugin">设备采集插件配置（取设备键）。</param>
    /// <param name="daq">设备采集通道当前使用的 IDaq 对象。</param>
    /// <param name="addresses">取得设备全部点位的委托；仅在真正需要注册时调用。</param>
    /// <param name="usePacker">设备是否启用自动组包，传给服务端用于组包采集（服务端使用设备自身的字节序等参数组包）。</param>
    /// <param name="token">取消令牌；取消时撤销占位并抛出 <see cref="OperationCanceledException"/>，下次调用立即重试。</param>
    /// <returns>无事可做返回 null；否则返回本次注册成功或失败的结果，调用方只需把消息记入日志。</returns>
    public static async Task<OperateResult?> EnsureRegisteredAsync(OpcUaServiceOperate service, PluginConfigModel plugin, IDaq daq,
        Func<IReadOnlyCollection<IAddressModel>> addresses, bool usePacker, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(daq);
        ArgumentNullException.ThrowIfNull(addresses);
        UaDeviceRegistry registry = RegistryOf(service);
        string key = plugin.Guid;
        if (registry.Begin(key, daq) == UaDeviceRegistry.Decision.Skip) return null;

        // 走到这里已经占位：无论成功、失败、取消还是异常，都必须让登记表离开 InFlight。
        try
        {
            return await RegisterAsync(service, registry, key, plugin, daq, addresses(), usePacker, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // 调用方正在停止：撤销占位，下次启动立即重试，不记为失败。
            registry.Abandon(key);
            throw;
        }
        catch (Exception ex)
        {
            registry.Failed(key, daq, permanent: false);
            return OperateResult.CreateFailureResult(string.Format(Text("OPC UA 注册失败：{0}"), ex.Message));
        }
    }

    /// <summary>
    /// 设备停止或销毁前取消注册：清掉登记，并在该对象确实注册过时通知服务端移除节点、停止转发。
    /// 服务端不会关闭设备；调用方应在它返回之后再取消订阅、释放设备。
    /// </summary>
    /// <param name="service">当前 OPC UA 服务实例。</param>
    /// <param name="deviceKey">设备键（插件配置 Guid）。</param>
    /// <param name="daq">即将停止的 IDaq 对象。</param>
    /// <param name="token">取消令牌。</param>
    /// <returns>没有注册过返回 null；否则返回取消结果（服务已释放时为失败，属正常情况）。</returns>
    public static async Task<OperateResult?> UnregisterAsync(OpcUaServiceOperate service, string deviceKey, IDaq daq,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(daq);
        if (!RegistryOf(service).Release(deviceKey, daq)) return null;
        return await service.UnRegisterDaqAsync(daq, token).ConfigureAwait(false);
    }

    /// <summary>
    /// 把设备点位转换为桥接可接受的逻辑地址，并筛掉桥接会拒绝或无法正确发布的点位。
    /// 桥接只要遇到一个非法点位就会拒绝整台设备，所以这里逐个剔除并返回原因，由调用方提示。
    /// </summary>
    /// <param name="models">设备下的全部点位模型。</param>
    /// <returns>可注册的逻辑地址，以及被跳过点位的“地址（原因）”说明，按输入顺序保留首次出现者。</returns>
    public static (Address Address, IReadOnlyList<string> Skipped) BuildAddress(IEnumerable<IAddressModel> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        var points = new List<AddressDetails>();
        var skipped = new List<string>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (IAddressModel model in models)
        {
            AddressDetails point = model.Convert();
            string? reason = null;
            if (string.IsNullOrWhiteSpace(point.AddressName)) reason = "名称为空";
            else if (!SupportedTypes.Contains(point.AddressDataType)) reason = "类型不支持";
            else if (point.Length == 0) reason = "长度为 0";
            // 手动设置了扩展参数的字节块由 Daq 自己解包成多个子点位；桥接没有这个能力，原样发布只会是一团字节，所以不转发。
            else if (point.AddressDataType == DataType.ByteArray && HasExtendParam(point.AddressExtendParam)) reason = "扩展参数解包点位未转发";
            else if (!names.Add(point.AddressName)) reason = "名称重复";

            if (reason is null) points.Add(point);
            else skipped.Add($"{point.AddressName}（{Text(reason)}）");
        }
        return (new Address { AddressArray = points }, skipped);
    }

    /// <summary>扩展参数是否有实际内容（空字符串、全空白视为没有）。</summary>
    private static bool HasExtendParam(object? param) => param switch
    {
        null => false,
        string text => !string.IsNullOrWhiteSpace(text),
        _ => true
    };

    /// <summary>
    /// 把设备的 IDaq 注入服务端，并把结果记入登记表。失败不会关闭或释放设备（它归采集通道所有）；
    /// 服务端接管成功后立刻记账，之后的消息拼装不会再改写状态。
    /// </summary>
    private static async Task<OperateResult> RegisterAsync(OpcUaServiceOperate service, UaDeviceRegistry registry, string key,
        PluginConfigModel plugin, IDaq daq, IReadOnlyCollection<IAddressModel> models, bool usePacker, CancellationToken token)
    {
        var (address, skipped) = BuildAddress(models);
        if (address.AddressArray.Count == 0)
        {
            registry.Failed(key, daq, permanent: true); // 点位集合变了设备会重启换新对象，到时再试
            return OperateResult.CreateFailureResult(Text("没有可注册到 OPC UA 的点位"));
        }

        OperateResult result = await service.RegisterDaqAsync(daq, address, usePacker, token).ConfigureAwait(false);
        if (!result.Status)
        {
            // 取消在桥接里会被转成失败结果而不是异常，这里还原成取消，避免把“正在停止”记成 30 秒的失败退避。
            token.ThrowIfCancellationRequested();
            registry.Failed(key, daq, permanent: false);
            return OperateResult.CreateFailureResult(string.Format(Text("OPC UA 注册失败：{0}"), result.Message));
        }

        registry.Succeeded(key, daq);
        return OperateResult.CreateSuccessResult(Describe(plugin, address, skipped, result.ResultData as OpcUaDaqRegistration));
    }

    /// <summary>拼装注册成功的日志文本；拼装失败时退回最简文本，绝不能抛出（此时服务端已经接管设备）。</summary>
    private static string Describe(PluginConfigModel plugin, Address address, IReadOnlyList<string> skipped, OpcUaDaqRegistration? registration)
    {
        try
        {
            string message = string.Format(Text("OPC UA 注册成功：设备目录 {0}，点位 {1} 个"),
                registration?.FolderName ?? plugin.Guid, registration?.PointCount ?? address.AddressArray.Count);
            if (!string.IsNullOrWhiteSpace(registration?.Message)) message += " " + registration.Message;
            if (skipped.Count > 0)
                message += " " + string.Format(Text("已跳过 {0} 个无法注册到 OPC UA 的点位：{1}"),
                    skipped.Count, string.Join("、", skipped.Take(5)) + (skipped.Count > 5 ? "…" : string.Empty));
            return message;
        }
        catch (FormatException)
        {
            return "OPC UA 注册成功";
        }
    }

    /// <summary>读取资源文本；资源缺失时回退为键本身（键即中文原文），保证返回值非空，可直接用于 string.Format。</summary>
    private static string Text(string key) => key.GetLanguageValue(Core.LanguageOperate) ?? key;
}
