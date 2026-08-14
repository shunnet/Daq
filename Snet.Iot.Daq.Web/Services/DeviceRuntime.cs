using Opc.Ua;
using Snet.Core.handler;
using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.handler;
using Snet.Model.data;
using Snet.Opc.ua.service;
using Snet.Utility;
using System.Collections.Concurrent;
using System.Threading.Channels;

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
    private Channel<AddressValue>? _uaSyncChannel;
    private FolderState? _uaFolder;
    private readonly List<FolderState> _uaFolderStates = new();
    private string _uaAddressSpaceName = "";
    private readonly Dictionary<string, string> _uaAddressMap = new();
    private readonly HashSet<string> _uaFailedAddresses = new();
    private readonly ConcurrentDictionary<string, WriteModel> _singleWriteDict = new();
    // 字节解包链（对齐 WPF ConsoleDeviceModel：GetBytesModels 缓存 + TransformAndForwardAsync）
    private readonly ConcurrentDictionary<string, (object Source, List<BytesModel> Models)> _bytesModels = new();
    private readonly ConcurrentDictionary<string, int> _failedBytesModels = new();
    private BytesHandler? _bytesHandler;

    public string Guid => _daqConfig.Guid;
    public bool IsRun { get; private set; }
    public string DeviceName { get; private set; }
    public string DeviceType => _daqConfig.Name;
    /// <summary>底层插件包版本（热更新后由配置同步刷新，控制台设备卡展示）</summary>
    public string DeviceVersion { get; private set; } = "-";
    public string DeviceHierarchy => _hierarchyPath;
    /// <summary>设备下全部点位（Address 节点）数量：添加/删除点位经 SyncFromProjects → RefreshSettings 感知更新。
    /// 注意：采集订阅集是 _addressDatas（仅配了 MQ 的地址），地址数量显示全量点位更符合直觉。</summary>
    public int AddressCount { get; private set; }
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
        AddressCount = CountAddressNodes(deviceNode.Details);
    }

    /// <summary>统计设备下全部 Address 节点（含层级嵌套），供控制台地址数量展示</summary>
    private static int CountAddressNodes(IEnumerable<IProjectDetailsTreeViewModel>? nodes)
    {
        if (nodes is null) return 0;
        var count = 0;
        foreach (var node in nodes)
        {
            if (node.NodeType == ProjectDetailsNodeType.Address) count++;
            count += CountAddressNodes(node.Children);
        }
        return count;
    }

    /// <summary>设备下未配置 MQ 传输设备的地址名列表（全量点位 - 订阅集），供启动采集前主动告知用户</summary>
    private List<string> GetAddressesWithoutMq()
    {
        var all = new List<string>();
        CollectAddressNames(_deviceNode.Details, all);
        var bound = new HashSet<string>(_addressDatas.Keys.Select(a => a.Address));
        return all.Where(name => !bound.Contains(name)).ToList();
    }

    private static void CollectAddressNames(IEnumerable<IProjectDetailsTreeViewModel>? nodes, List<string> names)
    {
        if (nodes is null) return;
        foreach (var node in nodes)
        {
            if (node.NodeType == ProjectDetailsNodeType.Address && node.AddressDetails is not null)
                names.Add(node.AddressDetails.Address);
            CollectAddressNames(node.Children, names);
        }
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
        DeviceVersion = ResolvePluginVersion(DeviceType);
        AddressCount = CountAddressNodes(deviceNode.Details);
        return changed;
    }

    /// <summary>按插件类名查 PluginList.json 的包版本（热更新后版本号变化，控制台一目了然）</summary>
    private static string ResolvePluginVersion(string pluginName)
    {
        try
        {
            if (!File.Exists(WebPaths.PluginListConfigPath)) return "-";
            var list = PluginHandlerCore.GetPluginUIConfig<System.Collections.ObjectModel.ObservableCollection<Snet.Iot.Daq.Core.data.PluginListModel>>(WebPaths.PluginListConfigPath);
            return list?.FirstOrDefault(p => p.Name == pluginName)?.Version ?? "-";
        }
        catch
        {
            return "-";
        }
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
            // 前置检查（启动前主动告知）：订阅集为空直接失败并列出未配置传输设备的点位，
            // 避免底层报"组包结果为空"这类无法定位的消息
            if (_addressDatas.Count == 0)
            {
                var missingMq = GetAddressesWithoutMq();
                CollectStatus = "启动失败";
                LedGreen = false;
                LedRed = true;
                _pushState(this);
                if (missingMq.Count > 0)
                {
                    _pushLog(string.Format(T("[{0}] 启动采集失败: {1}"), DeviceName,
                        string.Format(T("以下地址未配置传输设备，无法采集：{0}"), string.Join("、", missingMq))));
                    _pushLog(string.Format(T("[{0}] {1}"), DeviceName, T("请检查项目详情中传输设备是否正确设置给每个地址")));
                }
                else
                {
                    _pushLog(string.Format(T("[{0}] 启动采集失败: {1}"), DeviceName, T("设备下没有可采集的地址")));
                }
                return;
            }
            // 部分点位缺传输设备：警告哪些地址不参与采集，其余正常订阅
            var missingPartial = GetAddressesWithoutMq();
            if (missingPartial.Count > 0)
                _pushLog(string.Format(T("[{0}] {1}"), DeviceName,
                    string.Format(T("警告: {0} 个地址未配置传输设备，不参与采集: {1}"), missingPartial.Count, string.Join("、", missingPartial))));
            if (_daqHandler is null)
            {
                // 对齐 WPF CollectAsync：InstanceAsync 单例（CoreUnify 静态容器，同配置复用）
                _daqHandler = await DqaHandler.InstanceAsync(_daqConfig);
                _dataChannel = Channel.CreateBounded<EventDataResult>(ChannelCapacity);
                _daqHandler.OnDataEventAsync += OnDataEvent;
                // 对齐 WPF CollectAsync：订阅信息事件（驱动错误/连接状态经此上报到信息栏，如 Socket 异常）
                _daqHandler.OnInfoEventAsync += OnInfoEvent;
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
                // 对齐 WPF CollectAsync：订阅集为空（地址未配置 MQ 传输设备）时追加友好提示，
                // 否则"组包结果为空"这类底层消息用户无法定位原因
                if (_addressDatas.Count == 0)
                    _pushLog(string.Format(T("[{0}] {1}"), DeviceName, T("请检查项目详情中传输设备是否正确设置给每个地址")));
                // 对齐 WPF 重采语义：失败即释放 handler（插件实例缓存创建时的参数快照），
                // 否则下次采集复用旧实例（如改端口后仍连旧端口）
                await ReleaseHandlerInternalAsync();
                return;
            }

            // 对齐 WPF CollectAsync：再次采集时先清理旧 UA 层级与地址映射（UA 服务重启/配置变更后旧 FolderState 失效）
            if (_uaFolderStates.Count > 0)
            {
                var srv = _uaService();
                if (srv is not null)
                {
                    try { srv.RemoveFolder([_uaFolderStates[0].NodeId]); } catch { }
                    try { _uaFolderStates[0].Dispose(); } catch { }
                }
                _uaFolderStates.Clear();
                _uaFolder?.Dispose();
                _uaFolder = null;
            }
            _uaAddressMap.Clear();
            _uaFailedAddresses.Clear();
            _failedBytesModels.Clear();

            IsRun = true;
            CollectStatus = "正常";
            LedGreen = true;
            LedRed = false;
            _runtime.Start();
            _pushState(this);
            // 日志显示实际订阅数（配了 MQ 的地址）；AddressCount 是全量点位，语义不同
            _pushLog(string.Format(T("[{0}] 启动采集成功，地址数 {1}"), DeviceName, _addressDatas.Count));

            // 启动 UA 转发消费任务（对齐 WPF：UaSyncChannel 独立通道 + 独立消费循环，与 MQ 转发解耦）
            if (_uaSyncChannel is null)
            {
                _uaSyncChannel = Channel.CreateBounded<AddressValue>(ChannelCapacity);
                _ = Task.Run(() => UaSyncChannelDataEventAsync(_cts.Token));
            }

            if (_daqConfig.WebApi is not null)
            {
                try
                {
                    var waResult = await _daqHandler.WAOnAsync(_daqConfig.Guid, _daqConfig.WebApi);
                    // 对齐 WPF CollectAsync 的 WASatrtAsync 提示：无论成败都反馈
                    var tip = !waResult.Status && _daqConfig.WebApi.Port > 0 && _daqConfig.WebApi.Port < 1024
                        ? T("（端口小于 1024 需管理员权限运行或 netsh URLACL 授权）")
                        : "";
                    _pushLog(string.Format(T("[{0}] WebApi 启动{1}: {2}") + "{3}", DeviceName, waResult.Status ? T("成功") : T("失败"), waResult.Message, tip));
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

    /// <summary>释放采集 handler 及相关资源（无锁版，须在 _collectGate 持有内调用）。
    /// 插件实例缓存创建时的参数快照（IP/端口等），配置变更或采集失败后必须置空，下次采集用最新配置重建</summary>
    private async Task ReleaseHandlerInternalAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        if (_daqHandler is not null)
        {
            _daqHandler.OnDataEventAsync -= OnDataEvent;
            _daqHandler.OnInfoEventAsync -= OnInfoEvent;
            try { await _daqHandler.DisposeAsync(); } catch { }
            _daqHandler = null;
        }
        if (_dataChannel is not null)
        {
            _dataChannel.Writer.TryComplete();
            // 对齐 WPF StopAsync：消费循环退出后清空队列滞留元素
            while (_dataChannel.Reader.TryRead(out _)) { }
        }
        _dataChannel = null;
        if (_uaSyncChannel is not null)
        {
            _uaSyncChannel.Writer.TryComplete();
            // 对齐 WPF StopAsync：消费循环退出后清空队列滞留元素
            while (_uaSyncChannel.Reader.TryRead(out _)) { }
        }
        _uaSyncChannel = null;
        foreach (var mq in _mqHandlers.Values)
            await mq.DisposeAsync();
        _mqHandlers.Clear();
    }

    /// <summary>带锁释放采集 handler（供配置变更时未运行设备清理旧实例，对齐 WPF 改配置后重新采集语义）</summary>
    public async Task ResetHandlerAsync()
    {
        await _collectGate.WaitAsync();
        try
        {
            await ReleaseHandlerInternalAsync();
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
                _daqHandler.OnInfoEventAsync -= OnInfoEvent;
                await _daqHandler.DisposeAsync();
                _daqHandler = null;
            }
            if (_dataChannel is not null)
            {
                _dataChannel.Writer.TryComplete();
                // 对齐 WPF StopAsync：消费循环退出后清空队列滞留元素
                while (_dataChannel.Reader.TryRead(out _)) { }
            }
            _dataChannel = null;
            if (_uaSyncChannel is not null)
            {
                _uaSyncChannel.Writer.TryComplete();
                // 对齐 WPF StopAsync：消费循环退出后清空队列滞留元素
                while (_uaSyncChannel.Reader.TryRead(out _)) { }
            }
            _uaSyncChannel = null;

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

    /// <summary>添加/取消软启采集（对齐 WPF OnSoftCollectAsync/OffSoftCollectAsync：改项目节点标志 + 成功提示，落盘由页面调 SaveProjectsAsync 等价 Project.SetAsync）</summary>
    public void SetSoftCollect(bool on)
    {
        _deviceNode.IsSoftStart = on;
        _pushLog(string.Format(T("[{0}] {1}"), DeviceName, on ? T("添加软启采集成功") : T("取消软启采集成功")));
    }

    /// <summary>WebApi 启动（对齐 WPF WASatrtAsync：状态预检 → 未设置参数/未运行提示失败 → WAOnAsync）</summary>
    public async Task<OperateResult> WebApiStartAsync()
    {
        var handler = _daqHandler;
        if (handler is null)
            return OperateResult.CreateFailureResult(string.Format(T("[{0}] 采集未启动，无法操作 WebApi"), DeviceName));
        if (_daqConfig.WebApi is null)
            return OperateResult.CreateFailureResult(string.Format(T("[{0}] 未设置 WebApi 参数"), DeviceName));
        // 对齐 WPF WASatrtAsync：状态正常（WebApi 已在运行）→ 提示状态消息并返回，不重复启动；未运行才执行 WAOnAsync。
        // GetStatusAsync 在插件连接异常时可能直接抛异常，这里兜底转为失败消息（否则异常上抛到页面崩溃）
        OperateResult? status;
        try
        {
            status = await handler.WAStatusAsync(_daqConfig.Guid);
        }
        catch (Exception ex)
        {
            return OperateResult.CreateFailureResult(string.Format(T("[{0}] WebApi 状态查询异常: {1}"), DeviceName, ex.Message));
        }
        if (status.Status)
        {
            _pushLog(string.Format(T("[{0}] {1}"), DeviceName, status.Message));
            return status;
        }
        var result = await handler.WAOnAsync(_daqConfig.Guid, _daqConfig.WebApi);
        var tip = !result.Status && _daqConfig.WebApi.Port > 0 && _daqConfig.WebApi.Port < 1024
            ? T("（端口小于 1024 需管理员权限运行或 netsh URLACL 授权）")
            : "";
        _pushLog(string.Format(T("[{0}] WebApi 启动{1}: {2}") + "{3}", DeviceName, result.Status ? T("成功") : T("失败"), result.Message, tip));
        return result;
    }

    /// <summary>WebApi 停止（对齐 WPF WAStopAsync：未设置参数/未运行提示返回，运行中才停止）</summary>
    public async Task<OperateResult> WebApiStopAsync()
    {
        var handler = _daqHandler;
        if (handler is null)
            return OperateResult.CreateFailureResult(string.Format(T("[{0}] 采集未启动，无法操作 WebApi"), DeviceName));
        if (_daqConfig.WebApi is null)
            return OperateResult.CreateFailureResult(string.Format(T("[{0}] 未设置 WebApi 参数"), DeviceName));
        OperateResult? status;
        try
        {
            status = await handler.WAStatusAsync(_daqConfig.Guid);
        }
        catch (Exception ex)
        {
            return OperateResult.CreateFailureResult(string.Format(T("[{0}] WebApi 状态查询异常: {1}"), DeviceName, ex.Message));
        }
        // 对齐 WPF WAStopAsync：WebApi 未运行 → 提示状态消息并返回，无需停止
        if (!status.Status)
        {
            _pushLog(string.Format(T("[{0}] {1}"), DeviceName, status.Message));
            return status;
        }
        var result = await handler.WAOffAsync(_daqConfig.Guid);
        _pushLog(string.Format(T("[{0}] WebApi 停止{1}: {2}"), DeviceName, result.Status ? T("成功") : T("失败"), result.Message));
        return result;
    }

    /// <summary>WebApi 请求示例（对齐 WPF WARequestExampleAsync：未设置参数提示返回，请求结果由页面展示）</summary>
    public async Task<OperateResult> WebApiExampleAsync()
    {
        var handler = _daqHandler;
        if (handler is null)
            return OperateResult.CreateFailureResult(string.Format(T("[{0}] 采集未启动，无法操作 WebApi"), DeviceName));
        if (_daqConfig.WebApi is null)
            return OperateResult.CreateFailureResult(string.Format(T("[{0}] 未设置 WebApi 参数"), DeviceName));
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

    /// <summary>信息事件（对齐 WPF DqaHandler_OnInfoEventAsync：驱动错误/连接状态经 ResultMsgAsync 显示到信息栏）</summary>
    private Task OnInfoEvent(object? sender, EventInfoResult e)
    {
        if (!string.IsNullOrWhiteSpace(e.Message))
            ThrottledLog(e.Message, "info:" + e.Message);
        return Task.CompletedTask;
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
                // 失败数据事件上报信息栏（对齐 WPF DataSyncChannelDataEventAsync 的 ResultMsgAsync 分支）
                if (!e.Status)
                {
                    if (!string.IsNullOrWhiteSpace(e.Message))
                        ThrottledLog(e.Message, "e:" + e.Message);
                    continue;
                }
                // 支持字典与列表两种数据形态（列表为多批次解包结果），对齐 WPF DataSyncChannelDataEventAsync
                switch (e.ResultData)
                {
                    case ConcurrentDictionary<string, AddressValue> dict:
                        await ProcessKeysAsync(dict, token);
                        break;
                    case List<ConcurrentDictionary<string, AddressValue>> list:
                        foreach (var d in list)
                            await ProcessKeysAsync(d, token);
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

    /// <summary>处理一组地址值（原样移植 WPF ConsoleDeviceModel.ProcessKeysAsync）：
    /// 质量校验 → 字节解包（GetBytesModels）→ UA 通道 + MQ 转发，单地址异常不影响整组消费。</summary>
    private async Task ProcessKeysAsync(ConcurrentDictionary<string, AddressValue> keys, CancellationToken token)
    {
        if (keys.Count == 0)
            return;

        foreach (var kv in keys)
        {
            try
            {
                //地址的值
                AddressValue addressValue = kv.Value;

                // 数据质量异常先上报（不依赖地址是否在索引中）
                if (kv.Value.Quality != QualityType.Normal)
                {
                    ThrottledLog($"{DeviceHierarchy}, {addressValue.AddressName} - {kv.Value.Message}", "q:" + kv.Key);
                    continue;
                }

                if (!_addressIndex.TryGetValue(kv.Key, out var addressModel) ||
                    !_addressDatas.TryGetValue(addressModel, out var pluginConfigs))
                    continue;

                // 字节处理模型：缓存命中且参数来源未变才复用；组包移除(参数为空)时清除缓存
                List<BytesModel>? bm = GetBytesModels(addressValue);

                // 参数存在但解析失败：该地址配置了字节解析但无法获得模型，提示后丢弃，避免每周期刷屏
                if (bm == null && addressValue.AddressExtendParam != null)
                {
                    if (_failedBytesModels.TryAdd(addressValue.AddressName, 0))
                        ThrottledLog($"{DeviceHierarchy}, {addressValue.AddressName} - {T("扩展参数不正确")}", "bm:" + addressValue.AddressName);
                    continue;
                }

                // 无字节模型，直接转发
                if (bm == null)
                {
                    if (_cts is null)
                        return;
                    await _uaSyncChannel!.Writer.WriteAsync(addressValue, _cts.Token);
                    foreach (var mqConfig in pluginConfigs)
                    {
                        // 对齐 WPF MqTransmissionAsync：InstanceAsync 单例 + guid 字典缓存
                        if (!_mqHandlers.TryGetValue(mqConfig.Guid, out var mq))
                        {
                            mq = await MqHandler.InstanceAsync(mqConfig);
                            _mqHandlers[mqConfig.Guid] = mq;
                        }
                        var result = await mq.ProduceAsync(mqConfig.Guid, addressModel, addressValue);
                        if (!result.Status)
                            ThrottledLog(string.Format(T("MQ 转发失败 {0}: {1}"), addressModel.Address, result.Message), addressModel.Address);
                    }
                    continue;
                }

                // 字节转换与转发（组包批次 / 手动设置扩展参数的地址均按模型解包，不区分值是否字节数组）
                await TransformAndForwardAsync(addressValue, bm, addressModel, pluginConfigs);
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested)
                    break;
                ThrottledLog(string.Format(T("地址 {0} 处理异常: {1}"), kv.Key, ex.Message), kv.Key);
            }
        }
    }

    /// <summary>获取地址的字节处理模型（原样移植 WPF ConsoleDeviceModel.GetBytesModels）：
    /// 组包配置存在时：缓存命中且参数来源未变直接复用，来源变化(配置更新)时重新解析；
    /// 组包配置移除时：立即清除缓存（配置为权威信号）；数据不携带扩展参数直接直通。</summary>
    private List<BytesModel>? GetBytesModels(AddressValue addressValue)
    {
        object? param = addressValue.AddressExtendParam;

        // 组包配置已移除：清除缓存（配置为权威信号，不依赖数据是否仍携带参数）
        if (_daqConfig.AutoPack == null)
        {
            _bytesModels.TryRemove(addressValue.AddressName, out _);
            // 数据仍带参数：驱动尚未重新订阅，参数来自数据本身，照常解析过渡期数据（不再重建缓存）
            return param == null ? null : ParseBytesModels(param);
        }

        // 数据不携带扩展参数：这类地址本无缓存，或值非字节数组不会被解包使用，直接直通
        if (param == null)
            return null;

        // 缓存命中且参数来源未变，直接复用，避免每个采集周期重复反序列化与文件读取
        if (_bytesModels.TryGetValue(addressValue.AddressName, out var cached) && cached.Source == param)
            return cached.Models;

        List<BytesModel>? models = ParseBytesModels(param);
        if (models != null)
            _bytesModels[addressValue.AddressName] = (param, models);
        return models;
    }

    /// <summary>解析扩展参数为字节处理模型（原样移植 WPF ConsoleDeviceModel.ParseBytesModels）：
    /// 支持组包模型集合、JSON 字符串、JSON 文件路径三种来源（手动设置与组包格式一致）</summary>
    private static List<BytesModel>? ParseBytesModels(object? param) => param switch
    {
        // 组包直接传入模型集合
        List<BytesModel> list => list,
        // 手动设置的扩展参数 json 字符串组包
        string str when str.IsJson() => str.ToJsonEntity<List<BytesModel>>(),
        // 扩展参数为 json 文件路径时读取解析
        string filePath when File.Exists(filePath) => FileHandler.FileToString(filePath).ToJsonEntity<List<BytesModel>>(),
        _ => null
    };

    /// <summary>字节转换并转发到 UA 通道与 MQ（原样移植 WPF ConsoleDeviceModel.TransformAndForwardAsync）</summary>
    private async Task TransformAndForwardAsync(AddressValue addressValue, List<BytesModel> bm, IAddressModel addressModel, List<PluginConfigModel> pluginConfigs)
    {
        _bytesHandler ??= await BytesHandler.InstanceAsync(DeviceName);

        OperateResult result = await _bytesHandler.TransformAsync(addressValue.ResultValue.GetSource<byte[]>(), addressValue.Time, bm);
        if (!result.GetDetails(out ConcurrentDictionary<string, AddressValue>? res))
        {
            ThrottledLog($"{DeviceHierarchy}, {addressValue.AddressName} - {string.Format(T("解包失败：{0}"), result.Message)}", "t:" + addressValue.AddressName);
            return;
        }

        foreach (var item in res)
        {
            // 以原始地址名重新查索引与 MQ 配置，避免整批数据共用批次首地址的配置
            _addressIndex.TryGetValue(item.Key, out var sourceModel);
            _addressDatas.TryGetValue(sourceModel ?? addressModel, out var sourcePlugins);

            AddressModelCore newModel = new()
            {
                Address = item.Key,
                Describe = item.Value.AddressDescribe,
                EncodingType = item.Value.EncodingType,
                Guid = sourceModel?.Guid ?? addressModel.Guid,
                SimplifyValue = sourceModel?.SimplifyValue ?? addressModel.SimplifyValue,
                Length = item.Value.Length,
                Time = item.Value.Time,
                Topic = sourceModel?.Topic ?? addressModel.Topic,
                Type = item.Value.AddressDataType,
            };
            if (_cts is null)
                return;
            await _uaSyncChannel!.Writer.WriteAsync(item.Value, _cts.Token);
            foreach (var mqConfig in sourcePlugins ?? pluginConfigs)
            {
                // 对齐 WPF MqTransmissionAsync：InstanceAsync 单例 + guid 字典缓存
                if (!_mqHandlers.TryGetValue(mqConfig.Guid, out var mq))
                {
                    mq = await MqHandler.InstanceAsync(mqConfig);
                    _mqHandlers[mqConfig.Guid] = mq;
                }
                var mqResult = await mq.ProduceAsync(mqConfig.Guid, newModel, item.Value);
                if (!mqResult.Status)
                    ThrottledLog(string.Format(T("MQ 转发失败 {0}: {1}"), item.Key, mqResult.Message), item.Key);
            }
        }
    }

    /// <summary>UA 通道数据事件消费（原样移植 WPF ConsoleDeviceModel.UaSyncChannelDataEventAsync）：
    /// 质量校验 → 层级文件夹 → 首次地址创建 + NodeId 映射 → 写入 UA 地址空间。</summary>
    private async Task UaSyncChannelDataEventAsync(CancellationToken token)
    {
        try
        {
            var channel = _uaSyncChannel;
            if (channel is null) return;
            while (await channel.Reader.WaitToReadAsync(token))
            {
                while (channel.Reader.TryRead(out AddressValue? addressValue))
                {
                    if (token.IsCancellationRequested)
                        break;

                    if (addressValue.Quality != QualityType.Normal)
                    {
                        ThrottledLog($"{addressValue.AddressName} - {addressValue.Message}", "uaq:" + addressValue.AddressName);
                        continue;
                    }

                    FolderState? fs = await UaCreateFolder();
                    if (fs == null)
                    {
                        continue;
                    }

                    //数据源
                    string addressName = addressValue.AddressName;
                    DataType dataType = addressValue.AddressDataType;
                    object? value = addressValue.ResultValue;

                    //校验
                    var service = _uaService();
                    if (service is null)
                    {
                        ThrottledLog(T("UA 服务端未启动，跳过转发"), "ua:notstarted");
                        continue;
                    }
                    if (!service.GetStatus().Status)
                    {
                        ThrottledLog(T("UA 服务端未运行，跳过转发"), "ua:notrunning");
                        continue;
                    }

                    if (!_uaAddressMap.ContainsKey(addressName) && !_uaFailedAddresses.Contains(addressName))
                    {
                        if (!UaTypeMap.TryGetValue(dataType, out var builtInType))
                            continue;

                        if (builtInType == BuiltInType.String)
                            value ??= string.Empty;

                        //创建地址
                        var createResult = service.CreateAddress(new()
                        {
                            new()
                            {
                                AddressName = addressName,
                                Dynamic = false,
                                DefaultValue = value,
                                DataType = builtInType,
                                AccessLevel = 3
                            }
                        }, fs);

                        if (!createResult.Status)
                        {
                            // 标记失败，避免每个数据事件重复创建并刷屏消息
                            _uaFailedAddresses.Add(addressName);
                            ThrottledLog(string.Format(T("UA 地址创建失败 {0}: {1}"), addressName, createResult.Message), "ua:" + addressName);
                            continue;
                        }

                        // 只在创建成功后刷新一次地址列表
                        var res = service.GetAddressArray();
                        string format = $"s={_uaAddressSpaceName}.{_deviceNode.GetHierarchyPath(".")}.{addressName}";
                        if (res.Status && res.ResultData is List<string> list)
                        {
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

                    // 写入
                    if (!_uaAddressMap.TryGetValue(addressName, out var realAddress))
                    {
                        // 创建成功但未能映射到真实地址，标记避免重复创建
                        if (!_uaFailedAddresses.Contains(addressName))
                        {
                            _uaFailedAddresses.Add(addressName);
                            ThrottledLog(string.Format(T("UA 地址映射失败 {0}（AddressSpaceName={1}）"), addressName, _uaAddressSpaceName), "uamap:" + addressName);
                        }
                        continue;
                    }

                    _singleWriteDict[realAddress] = new WriteModel(value, dataType);

                    var writeResult = await service.WriteAsync(_singleWriteDict);

                    _singleWriteDict.Clear();

                    if (!writeResult.Status)
                        ThrottledLog(string.Format(T("UA 写入失败 {0}: {1}"), addressName, writeResult.Message), "ua:" + addressName);
                }
            }
        }
        catch (TaskCanceledException)
        {
        }
        catch (ChannelClosedException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ThrottledLog(string.Format(T("UA 转发异常: {0}"), ex.Message), "ua:loop");
        }
    }

    /// <summary>创建 UA 层级（原样移植 WPF ConsoleDeviceModel.UaCreateFolder）：
    /// folderState 缓存 + AddressSpaceName 取 Basics + GetStatus 门 + 按设备层级逐层 CreateFolder。</summary>
    private async Task<FolderState?> UaCreateFolder()
    {
        try
        {
            var service = _uaService();
            if (service is null)
                return null;

            if (_uaFolder != null)
            {
                return _uaFolder;
            }

            //比对层级
            if (string.IsNullOrWhiteSpace(_uaAddressSpaceName))
            {
                var basics = service.GetBasicsArgs();
                if (basics.Status && basics.ResultData is OpcUaServiceData.Basics b)
                    _uaAddressSpaceName = b.AddressSpaceName;
            }

            if (service.GetStatus().Status)
            {
                FolderState? folder = null;
                //创建层级
                foreach (var item in _hierarchyPath.TrimAll().Split('>'))
                {
                    var operateResult = service.CreateFolder(item, folder);
                    if (operateResult.Status && operateResult.ResultData is FolderState fs)
                    {
                        folder = fs;
                        _uaFolderStates.Add(fs);
                    }
                    else
                    {
                        ThrottledLog(string.Format(T("UA 层级创建失败 {0}: {1}"), item, operateResult.Message), "uafolder:" + item);
                    }
                }
                _uaFolder = folder;
            }
            else
            {
                return null;
            }
            return _uaFolder;
        }
        catch (Exception)
        {
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
