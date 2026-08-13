using System.Collections.Concurrent;
using System.Threading.Channels;
using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.handler;
using Snet.Iot.Daq.Core.@interface;
using Snet.Model.data;
using Snet.Opc.core;
using Snet.Opc.ua.service;
using Opc.Ua;

namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// 采集运行时（薄移植 WPF ConsoleDeviceModel 的编排胶水）：
/// 只做「Channel + 调 Core API」，协议/设备操作/转发全部调用 DqaHandler/MqHandler/PluginHandlerCore。
/// </summary>
public class DeviceRuntime : IAsyncDisposable
{
    private const int ChannelCapacity = 1024;
    private static readonly TimeSpan FailLogThrottleWindow = TimeSpan.FromSeconds(5);
    /// <summary>状态推送节流窗口：数据事件高频时避免每条样本都触发整页重渲染（对齐 WPF 状态翻转才通知 + 1s 轮询运行时间）</summary>
    private static readonly TimeSpan StatePushThrottle = TimeSpan.FromMilliseconds(500);

    /// <summary>DataType → OPC UA BuiltInType 映射（对齐 WPF ConsoleDeviceModel._typeMap）</summary>
    private static readonly Dictionary<DataType, BuiltInType> UaTypeMap = new()
    {
        { DataType.Byte, BuiltInType.Byte },
        { DataType.Bool, BuiltInType.Boolean },
        { DataType.Double, BuiltInType.Double },
        { DataType.Float, BuiltInType.Float },
        { DataType.Single, BuiltInType.Float },
        { DataType.Short, BuiltInType.Int16 },
        { DataType.Int16, BuiltInType.Int16 },
        { DataType.Ushort, BuiltInType.UInt16 },
        { DataType.UInt16, BuiltInType.UInt16 },
        { DataType.Int, BuiltInType.Int32 },
        { DataType.Int32, BuiltInType.Int32 },
        { DataType.Uint, BuiltInType.UInt32 },
        { DataType.UInt32, BuiltInType.UInt32 },
        { DataType.Long, BuiltInType.Int64 },
        { DataType.Int64, BuiltInType.Int64 },
        { DataType.Ulong, BuiltInType.UInt64 },
        { DataType.UInt64, BuiltInType.UInt64 },
        { DataType.String, BuiltInType.String },
        { DataType.Char, BuiltInType.String },
    };

    private readonly ConcurrentDictionary<string, DateTime> _lastFailLog = new();
    private DateTime _lastStatePush = DateTime.MinValue;
    private PluginConfigModel _daqConfig;
    private IProjectTreeViewModel _deviceNode;
    private ConcurrentDictionary<IAddressModel, List<PluginConfigModel>> _addressDatas;
    private string _hierarchyPath;
    private string _settingsSignature = "";
    private readonly Action<string> _pushLog;
    private readonly Action<DeviceRuntime> _pushState;
    private readonly Func<OpcUaServiceOperate?> _uaService;
    private readonly LocalizationService _localization;

    private DqaHandler? _daqHandler;
    private Channel<EventDataResult>? _dataChannel;
    private readonly ConcurrentDictionary<string, MqHandler> _mqHandlers = new();
    private readonly RuntimeSecondsRecorderHandler _runtime = new();
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _collectGate = new(1, 1);
    private readonly ConcurrentDictionary<string, IAddressModel> _addressIndex = new();

    // UA 地址空间转发状态（对齐 WPF ConsoleDeviceModel.UaSyncChannelDataEventAsync）
    private FolderState? _uaFolder;
    private string _uaAddressSpaceName = "";
    private readonly Dictionary<string, string> _uaAddressMap = new();
    private readonly HashSet<string> _uaFailedAddresses = new();

    public string Guid => _daqConfig.Guid;
    public bool IsRun { get; private set; }
    public string DeviceName { get; private set; }
    public string DeviceType => _daqConfig.Name;
    public string DeviceHierarchy => _hierarchyPath;
    public int AddressCount => _addressDatas.Count;
    public string CollectStatus { get; private set; } = "未采集";
    public bool LedGreen { get; private set; }
    public bool LedRed { get; private set; }
    public string UpdateTime { get; private set; } = "-";
    public int CollectTimeSeconds => (int)_runtime.TotalSeconds;

    public DeviceRuntime(IProjectTreeViewModel deviceNode, Func<OpcUaServiceOperate?> uaService, Action<string> pushLog, Action<DeviceRuntime> pushState, LocalizationService localization)
    {
        _daqConfig = deviceNode.DaqDetails!;
        _deviceNode = deviceNode;
        _addressDatas = ProjectHandlerCore.ToAddressMqDictionary(deviceNode.Details ?? new());
        foreach (var address in _addressDatas.Keys)
            _addressIndex[address.Address] = address;
        _hierarchyPath = deviceNode.GetHierarchyPath();
        _uaService = uaService;
        _pushLog = pushLog;
        _pushState = pushState;
        _localization = localization;
        DeviceName = deviceNode.Name;
    }

    private string T(string key) => _localization.T(key);

    /// <summary>
    /// 刷新配置快照（对齐 WPF 每次刷新重读设备配置）：参数/地址集/层级/名称同步到最新项目树。
    /// 返回是否发生实质变更（参数 JSON 或地址集变化），供调用方决定是否重订阅。
    /// </summary>
    public bool RefreshSettings(IProjectTreeViewModel deviceNode)
    {
        _daqConfig = deviceNode.DaqDetails!;
        _deviceNode = deviceNode;
        var newDict = ProjectHandlerCore.ToAddressMqDictionary(deviceNode.Details ?? new());
        var signature = _daqConfig.Param + "|" + string.Join("|", newDict.Keys.Select(k => k.Guid).OrderBy(g => g, StringComparer.Ordinal));
        var changed = signature != _settingsSignature;
        _settingsSignature = signature;
        _addressDatas = newDict;
        _addressIndex.Clear();
        foreach (var address in newDict.Keys)
            _addressIndex[address.Address] = address;
        _hierarchyPath = deviceNode.GetHierarchyPath();
        DeviceName = deviceNode.Name;
        return changed;
    }

    /// <summary>启动采集（对齐 WPF CollectAsync：订阅地址 → 起通道 → 计时）。
    /// 通道与事件订阅仅在首次启动时创建一次，避免重复订阅叠加；SemaphoreSlim 防双击并发重复订阅。</summary>
    public async Task CollectAsync()
    {
        if (IsRun) return;
        await _collectGate.WaitAsync();
        try
        {
            if (IsRun) return;
            if (_daqHandler is null)
            {
                _daqHandler = new DqaHandler(_daqConfig);
                _dataChannel = Channel.CreateBounded<EventDataResult>(ChannelCapacity);
                _daqHandler.OnDataEventAsync += OnDataEvent;
                _cts = new CancellationTokenSource();
                // 消费循环整体脱离电路同步上下文（Task.Run 入口 + 内部 await 沿用线程池）：
                // 插件失败风暴/阻塞调用不会占住 Blazor 电路线程，停止按钮始终可响应
                _ = Task.Run(() => ConsumeAsync(_cts.Token));
            }
            var result = await _daqHandler.SubscribeAsync(_daqConfig.Guid, _addressDatas.Keys.ToList(), _daqConfig.AutoPack);
            if (!result.Status)
            {
                // 异常详情只进日志（信息区），状态区保持通用文案
                CollectStatus = "启动失败";
                LedGreen = false;
                LedRed = true;
                _pushState(this);
                _pushLog(string.Format(T("[{0}] 启动采集失败: {1}"), DeviceName, result.Message));
                return;
            }

            IsRun = true;
            CollectStatus = "正常";
            LedGreen = true;
            LedRed = false;
            _runtime.Start();
            _pushState(this);
            _pushLog(string.Format(T("[{0}] 启动采集成功，地址数 {1}"), DeviceName, AddressCount));

            if (_daqConfig.WebApi is not null)
            {
                try
                {
                    var waResult = await _daqHandler.WAOnAsync(_daqConfig.Guid, _daqConfig.WebApi);
                    // 对齐 WPF CollectAsync 的 WASatrtAsync 提示：无论成败都反馈
                    _pushLog(string.Format(T("[{0}] WebApi 启动{1}: {2}"), DeviceName, waResult.Status ? T("成功") : T("失败"), waResult.Message));
                }
                catch (Exception ex)
                {
                    _pushLog(string.Format(T("[{0}] WebApi 启动异常: {1}"), DeviceName, ex.Message));
                }
            }
        }
        catch (Exception ex)
        {
            CollectStatus = "异常";
            LedRed = true;
            _pushState(this);
            _pushLog(string.Format(T("[{0}] 启动采集异常: {1}"), DeviceName, ex.Message));
        }
        finally
        {
            _collectGate.Release();
        }
    }

    /// <summary>停止采集（对齐 WPF StopAsync：取消 → 退订 → 释放）。与 CollectAsync 共用 _collectGate 串行，避免停止/启动交错产生僵尸运行态。
    /// 注意：只保留门内双检（门外早退会与启动流程竞态，可能漏停进行中的启动）</summary>
    public async Task StopAsync()
    {
        await _collectGate.WaitAsync();
        try
        {
            if (!IsRun && _daqHandler is null) return;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _runtime.Stop();

            if (_daqHandler is not null)
            {
                try
                {
                    if (IsRun)
                        await _daqHandler.UnSubscribeAsync(_daqConfig.Guid, _addressDatas.Keys.ToList());
                }
                catch (Exception ex)
                {
                    _pushLog(string.Format(T("[{0}] 退订异常: {1}"), DeviceName, ex.Message));
                }
                // 对齐 WPF StopAsync：停止设备时同步关闭 WebApi
                if (_daqConfig.WebApi is not null)
                {
                    try
                    {
                        await _daqHandler.WAOffAsync(_daqConfig.Guid);
                    }
                    catch (Exception ex)
                    {
                        _pushLog(string.Format(T("[{0}] WebApi 关闭异常: {1}"), DeviceName, ex.Message));
                    }
                }
                _daqHandler.OnDataEventAsync -= OnDataEvent;
                await _daqHandler.DisposeAsync();
                _daqHandler = null;
            }
            if (_dataChannel is not null)
                _dataChannel.Writer.TryComplete();
            _dataChannel = null;

            foreach (var mq in _mqHandlers.Values)
                await mq.DisposeAsync();
            _mqHandlers.Clear();

            IsRun = false;
            CollectStatus = "未采集";
            LedGreen = false;
            LedRed = false;
            _pushState(this);
            _pushLog(string.Format(T("[{0}] 停止采集"), DeviceName));
        }
        finally
        {
            _collectGate.Release();
        }
    }

    /// <summary>重试：重置计时后停止并重新启动</summary>
    public async Task RetryAsync()
    {
        _runtime.Reset();
        await StopAsync();
        await CollectAsync();
    }

    /// <summary>随软启状态（对齐 WPF ConsoleDeviceModel.IsSoftStart：持久化于项目树，宿主启动/配置同步时自动恢复采集）</summary>
    public bool IsSoftStart => _deviceNode.IsSoftStart;

    /// <summary>添加/取消软启采集（对齐 WPF OnSoftCollectAsync/OffSoftCollectAsync：改项目节点标志，落盘由页面调 SaveProjectsAsync）</summary>
    public void SetSoftCollect(bool on)
    {
        _deviceNode.IsSoftStart = on;
        _pushLog(string.Format(T("[{0}] {1}"), DeviceName, on ? T("添加软启采集") : T("取消软启采集")));
    }

    /// <summary>WebApi 启动（对齐 WPF WASatrtAsync：状态预检 → 未设置参数/未运行提示失败 → WAOnAsync）</summary>
    public async Task<OperateResult> WebApiStartAsync()
    {
        var handler = _daqHandler;
        if (handler is null)
            return OperateResult.CreateFailureResult(string.Format(T("[{0}] 采集未启动，无法操作 WebApi"), DeviceName));
        if (_daqConfig.WebApi is null)
            return OperateResult.CreateFailureResult(string.Format(T("[{0}] 未设置 WebApi 参数"), DeviceName));
        // 对齐 WPF WASatrtAsync：先查状态，失败（设备未连接等）则提示并停止
        var status = await handler.WAStatusAsync(_daqConfig.Guid);
        if (!status.Status)
            return OperateResult.CreateFailureResult(string.Format(T("[{0}] {1}"), DeviceName, status.Message));
        var result = await handler.WAOnAsync(_daqConfig.Guid, _daqConfig.WebApi);
        _pushLog(string.Format(T("[{0}] WebApi 启动{1}: {2}"), DeviceName, result.Status ? T("成功") : T("失败"), result.Message));
        return result;
    }

    /// <summary>WebApi 停止（对齐 WPF WAStopAsync）</summary>
    public async Task<OperateResult> WebApiStopAsync()
    {
        var handler = _daqHandler;
        if (handler is null)
            return OperateResult.CreateFailureResult(string.Format(T("[{0}] 采集未启动，无法操作 WebApi"), DeviceName));
        var result = await handler.WAOffAsync(_daqConfig.Guid);
        _pushLog(string.Format(T("[{0}] WebApi 停止{1}: {2}"), DeviceName, result.Status ? T("成功") : T("失败"), result.Message));
        return result;
    }

    /// <summary>WebApi 请求示例（对齐 WPF WARequestExampleAsync，返回示例数据）</summary>
    public async Task<OperateResult> WebApiExampleAsync()
    {
        var handler = _daqHandler;
        if (handler is null)
            return OperateResult.CreateFailureResult(string.Format(T("[{0}] 采集未启动，无法操作 WebApi"), DeviceName));
        return await handler.WARequestExampleAsync(_daqConfig.Guid);
    }

    /// <summary>数据事件入队（一次订阅，避免重复叠加；带取消令牌防停止时挂起）</summary>
    private async Task OnDataEvent(object? sender, EventDataResult e)
    {
        var channel = _dataChannel;
        if (channel is null) return;
        try
        {
            await channel.Writer.WriteAsync(e, _cts?.Token ?? default);
        }
        catch (OperationCanceledException)
        {
            // 停止采集的正常路径
        }
        catch (Exception ex)
        {
            _pushLog(string.Format(T("[{0}] 数据入队异常: {1}"), DeviceName, ex.Message));
        }
    }

    private async Task ConsumeAsync(CancellationToken token)
    {
        // 局部捕获 handler 与 channel：重试（Stop→Collect）后旧循环退出时只退订自己代际的事件，
        // 不会误退订新 handler 的事件订阅
        var handler = _daqHandler!;
        var channel = _dataChannel!;
        try
        {
            await foreach (var e in channel.Reader.ReadAllAsync(token))
            {
                if (!e.Status) continue;
                // 支持字典与列表两种数据形态（列表为多批次解包结果），对齐 WPF DataSyncChannelDataEventAsync
                switch (e.ResultData)
                {
                    case ConcurrentDictionary<string, AddressValue> dict:
                        await ProcessKeysAsync(dict);
                        break;
                    case List<ConcurrentDictionary<string, AddressValue>> list:
                        foreach (var d in list)
                            await ProcessKeysAsync(d);
                        break;
                }
                // 对齐 WPF ContentStringFormat：yyyy-MM-dd HH:mm:ss
                UpdateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                // 状态推送节流：数据高频时 500ms 最多推一次（运行时间由控制台 1s 监控采样渲染兜底）
                var now = DateTime.UtcNow;
                if (now - _lastStatePush >= StatePushThrottle)
                {
                    _lastStatePush = now;
                    _pushState(this);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 停止采集的正常路径
        }
        catch (Exception ex)
        {
            _pushLog(string.Format(T("[{0}] 数据通道异常: {1}"), DeviceName, ex.Message));
        }
        finally
        {
            // H1 修复：消费循环退出（取消或异常）时自动退订事件，避免事件链永久挂起/泄漏
            handler.OnDataEventAsync -= OnDataEvent;
        }
    }

    /// <summary>按地址转发 MQ（对齐 WPF ProcessKeysAsync 的 MQ 转发段），单地址异常不影响整组。
    /// O(1) 地址索引 + 质量校验（L1/M3 修复）。失败日志按地址节流（5 秒窗口），防高频失败刷爆日志缓冲。</summary>
    private async Task ProcessKeysAsync(ConcurrentDictionary<string, AddressValue> keys)
    {
        foreach (var (addressKey, value) in keys)
        {
            try
            {
                // 质量异常不转发（对齐 WPF：先检查 QualityType 再转发）
                if (value.Quality != QualityType.Normal) continue;
                if (!_addressIndex.TryGetValue(addressKey, out var address)
                    || !_addressDatas.TryGetValue(address, out var mqConfigs)) continue;
                // OPC UA 服务端转发（对齐 WPF UaSyncChannelDataEventAsync：服务端启动时采集数据写入 UA 地址空间）
                await UaForwardAsync(addressKey, value);
                foreach (var mqConfig in mqConfigs)
                {
                    var mq = _mqHandlers.GetOrAdd(mqConfig.Guid, _ => new MqHandler(mqConfig));
                    var result = await mq.ProduceAsync(mqConfig.Guid, address, value);
                    if (!result.Status)
                        ThrottledLog(string.Format(T("MQ 转发失败 {0}: {1}"), address.Address, result.Message), address.Address);
                }
            }
            catch (Exception ex)
            {
                ThrottledLog(string.Format(T("地址 {0} 处理异常: {1}"), addressKey, ex.Message), addressKey);
            }
        }
    }

    /// <summary>
    /// OPC UA 服务端转发（对齐 WPF ConsoleDeviceModel.UaSyncChannelDataEventAsync）：
    /// 服务端未启动/未运行则跳过；首次遇到地址时创建层级文件夹与 UA 地址，之后按映射的 NodeId 写入。
    /// </summary>
    private async Task UaForwardAsync(string addressKey, AddressValue value)
    {
        var service = _uaService();
        if (service is null) return;
        try
        {
            if (!service.GetStatus().Status) return;
            var folder = await UaCreateFolderAsync(service);
            if (folder is null) return;

            var addressName = string.IsNullOrWhiteSpace(value.AddressName) ? addressKey : value.AddressName;
            var dataType = value.AddressDataType;
            if (!_uaAddressMap.ContainsKey(addressName) && !_uaFailedAddresses.Contains(addressName))
            {
                if (!UaTypeMap.TryGetValue(dataType, out var builtInType)) return;
                object? defaultValue = value.ResultValue;
                if (builtInType == BuiltInType.String) defaultValue ??= string.Empty;

                // 创建地址
                var createResult = service.CreateAddress(new List<AddressBody>
                {
                    new()
                    {
                        AddressName = addressName,
                        Dynamic = false,
                        DefaultValue = defaultValue,
                        DataType = builtInType,
                        AccessLevel = 3
                    }
                }, folder);
                if (!createResult.Status)
                {
                    // 标记失败，避免每个数据事件重复创建并刷屏
                    _uaFailedAddresses.Add(addressName);
                    ThrottledLog(string.Format(T("UA 地址创建失败 {0}: {1}"), addressName, createResult.Message), "ua:" + addressName);
                    return;
                }

                // 创建成功后映射真实 NodeId（对齐 WPF：GetAddressArray 匹配 s={地址空间}.{层级}.{地址名}）
                var array = service.GetAddressArray();
                if (array.Status && array.ResultData is List<string> list)
                {
                    var format = $"s={_uaAddressSpaceName}.{_hierarchyPath.Replace(" > ", ".")}.{addressName}";
                    foreach (var nodeId in list)
                    {
                        if (nodeId.Contains(format, StringComparison.Ordinal))
                        {
                            _uaAddressMap[addressName] = nodeId;
                            break;
                        }
                    }
                }
            }

            if (!_uaAddressMap.TryGetValue(addressName, out var realAddress))
            {
                // 创建成功但未能映射到真实地址，标记避免重复创建
                if (!_uaFailedAddresses.Contains(addressName))
                    _uaFailedAddresses.Add(addressName);
                return;
            }

            var writeDict = new ConcurrentDictionary<string, WriteModel>
            {
                [realAddress] = new WriteModel(value.ResultValue, dataType)
            };
            var writeResult = await service.WriteAsync(writeDict, CancellationToken.None);
            if (!writeResult.Status)
                ThrottledLog(string.Format(T("UA 写入失败 {0}: {1}"), addressName, writeResult.Message), "ua:" + addressName);
        }
        catch (Exception ex)
        {
            ThrottledLog(string.Format(T("UA 转发异常 {0}: {1}"), addressKey, ex.Message), "ua:" + addressKey);
        }
    }

    /// <summary>创建 UA 层级文件夹（对齐 WPF UaCreateFolder：按设备层级逐层 CreateFolder）</summary>
    private async Task<FolderState?> UaCreateFolderAsync(OpcUaServiceOperate service)
    {
        try
        {
            if (_uaFolder is not null) return _uaFolder;
            if (string.IsNullOrWhiteSpace(_uaAddressSpaceName))
            {
                var basics = service.GetBasicsArgs();
                if (basics.Status && basics.ResultData is OpcUaServiceData.Basics b && !string.IsNullOrWhiteSpace(b.AddressSpaceName))
                    _uaAddressSpaceName = b.AddressSpaceName;
            }
            if (!service.GetStatus().Status) return null;
            FolderState? folder = null;
            foreach (var item in _hierarchyPath.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var result = service.CreateFolder(item, folder);
                if (result.Status && result.ResultData is FolderState fs)
                {
                    folder = fs;
                }
                else
                {
                    ThrottledLog(string.Format(T("UA 层级创建失败 {0}: {1}"), item, result.Message), "uafolder:" + item);
                }
            }
            _uaFolder = folder;
            return folder;
        }
        catch (Exception ex)
        {
            ThrottledLog(string.Format(T("UA 层级创建异常: {0}"), ex.Message), "uafolder");
            return null;
        }
    }

    /// <summary>失败日志节流：同一键 5 秒内只记一次，避免高频失败刷爆日志缓冲</summary>
    private void ThrottledLog(string message, string key)
    {
        var now = DateTime.UtcNow;
        if (_lastFailLog.TryGetValue(key, out var last) && now - last < FailLogThrottleWindow) return;
        _lastFailLog[key] = now;
        _pushLog($"[{DeviceName}] {message}");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        GC.SuppressFinalize(this);
    }
}
