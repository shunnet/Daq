using CommunityToolkit.Mvvm.Input;
using Opc.Ua;
using Snet.Core.handler;
using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.handler;
using Snet.Iot.Daq.Core.@interface;
using Snet.Iot.Daq.Core.mvvm;
using Snet.Iot.Daq.data;
using Snet.Log;
using Snet.Model.data;
using Snet.Model.@enum;
using Snet.Opc.ua.service;
using Snet.Utility;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Channels;

namespace Snet.Iot.Daq.viewModel
{
    /// <summary>
    /// 控制台设备视图模型，负责单个采集设备的运行控制、数据读写、字节处理、OPC UA 地址空间同步以及 MQ 消息转发。
    /// </summary>
    public class ConsoleDeviceModel : BindNotify, IDisposable, IAsyncDisposable
    {
        #region 构造函数
        /// <summary>
        /// 无参构造函数
        /// </summary>
        public ConsoleDeviceModel()
        {
            StartPolling(runtime);
            Snet.Core.handler.LanguageHandler.OnLanguageEventAsync += LanguageHandler_OnLanguageEventAsync;
        }
        #endregion

        #region 属性

        /// <summary>
        /// 字节处理
        /// </summary>
        private BytesHandler bytesHandler;

        /// <summary>
        /// 外部回调需要显示的消息
        /// </summary>
        private Func<string, Task> ShowAsync;

        /// <summary>
        /// 外部回调需要显示的结果消息
        /// </summary>
        private Func<PluginConfigModel, BaseModel, Task> ResultAsync;

        /// <summary>
        /// 采集驱动
        /// </summary>
        private DqaHandler daqHandler;

        /// <summary>
        /// 消息处理
        /// </summary>
        private ConcurrentDictionary<string, MqHandler> mqHandlers = new();

        /// <summary>
        /// 字节处理模型缓存<br/>
        /// 值保存参数来源与解析结果，来源变化(配置更新/组包移除)时自动重新解析或清除
        /// </summary>
        private ConcurrentDictionary<string, (object Source, List<BytesModel> Models)> bytesModels = new();

        /// <summary>
        /// 字节模型解析失败的地址集合<br/>
        /// 避免每个数据事件对同一地址重复提示
        /// </summary>
        private readonly ConcurrentDictionary<string, byte> _failedBytesModels = new();

        /// <summary>
        /// 运行时间记录
        /// </summary>
        private RuntimeSecondsRecorderHandler runtime = new();

        /// <summary>
        /// 原始地址 -> OPCUA 真实地址映射
        /// </summary>
        private readonly ConcurrentDictionary<string, string> _addressMap = new();

        /// <summary>
        /// 创建或映射失败的地址集合<br/>
        /// 避免每个数据事件对同一地址重复调用 CreateAddress 并刷屏消息
        /// </summary>
        private readonly ConcurrentDictionary<string, byte> _failedAddress = new();

        /// <summary>
        /// UA 写入复用字典（单线程路径，无需并发容器）
        /// </summary>
        private readonly ConcurrentDictionary<string, WriteModel> _singleWriteDict = new();

        /// <summary>
        /// 地址空间名称
        /// </summary>
        private string uaServerAddressSpaceName;

        /// <summary>
        /// opcua 父级层级
        /// </summary>
        private FolderState folderState;

        /// <summary>
        /// 层级集合
        /// </summary>
        private List<FolderState> folderStates = new();

        /// <summary>
        /// 地址索引缓存（单线程路径，每次重建）
        /// </summary>
        private Dictionary<string, IAddressModel> _addressIndex = new();

        /// <summary>
        /// MQ 配置映射缓存（单线程路径，每次重建）
        /// </summary>
        private Dictionary<string, List<PluginConfigModel>> _mqPluginMap = new();

        /// <summary>
        /// DataType 与 BuiltInType 映射缓存
        /// </summary>
        private static readonly Dictionary<DataType, BuiltInType> _typeMap = new()
        {
            { Model.@enum.DataType.Byte, BuiltInType.Byte },
            { Model.@enum.DataType.Bool, BuiltInType.Boolean },
            { Model.@enum.DataType.Double, BuiltInType.Double },
            { Model.@enum.DataType.Float, BuiltInType.Float },
            { Model.@enum.DataType.Single, BuiltInType.Float },
            { Model.@enum.DataType.Short, BuiltInType.Int16 },
            { Model.@enum.DataType.Int16, BuiltInType.Int16 },
            { Model.@enum.DataType.Ushort, BuiltInType.UInt16 },
            { Model.@enum.DataType.UInt16, BuiltInType.UInt16 },
            { Model.@enum.DataType.Int, BuiltInType.Int32 },
            { Model.@enum.DataType.Int32, BuiltInType.Int32 },
            { Model.@enum.DataType.Uint, BuiltInType.UInt32 },
            { Model.@enum.DataType.UInt32, BuiltInType.UInt32 },
            { Model.@enum.DataType.Long, BuiltInType.Int64 },
            { Model.@enum.DataType.Int64, BuiltInType.Int64 },
            { Model.@enum.DataType.Ulong, BuiltInType.UInt64 },
            { Model.@enum.DataType.UInt64, BuiltInType.UInt64 },
            { Model.@enum.DataType.String, BuiltInType.String },
            { Model.@enum.DataType.Char, BuiltInType.String },
        };


        /// <summary>
        /// 数据通道容量上限<br/>
        /// 原为 ushort.MaxValue(65535)，消费端变慢时每条事件携带整包地址数据，积压可达数百 MB。<br/>
        /// 1024 已能满足正常采集吞吐，超出时由 Wait 模式提供背压。
        /// </summary>
        private const int ChannelCapacity = 1024;

        /// <summary>
        /// 通道配置，延迟创建
        /// </summary>
        private BoundedChannelOptions channel => p_Channel ??= new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };
        private BoundedChannelOptions? p_Channel;

        /// <summary>
        /// Ua 地址同步通道
        /// </summary>
        private Channel<AddressValue> UaSyncChannel;

        /// <summary>
        /// 数据事件通道
        /// </summary>
        private Channel<EventDataResult> DataSyncChannel;

        /// <summary>
        /// 全局消息取消通知
        /// </summary>
        private CancellationTokenSource TokenSource;

        /// <summary>
        /// 是否正在运行采集
        /// </summary>
        public bool IsRun = false;

        /// <summary>
        /// 采集配置
        /// </summary>
        private PluginConfigModel DaqData
        {
            get => GetProperty(() => DaqData);
            set => SetProperty(() => DaqData, value);
        }

        /// <summary>
        /// 采集插件路径
        /// </summary>
        public string DaqPluginPath { get; set; }

        /// <summary>
        /// 消息插件路径
        /// </summary>
        public List<string> MqPluginPath { get; set; }

        /// <summary>
        /// 地址数量
        /// </summary>
        public int AddressCount
        {
            get => GetProperty(() => AddressCount);
            set => SetProperty(() => AddressCount, value);
        }

        /// <summary>
        /// 地址数据
        /// </summary>
        private ConcurrentDictionary<IAddressModel, List<PluginConfigModel>> AddressDatas
        {
            get => GetProperty(() => AddressDatas);
            set => SetProperty(() => AddressDatas, value);
        }

        /// <summary>
        /// 项目信息
        /// </summary>
        private IProjectTreeViewModel Project
        {
            get => GetProperty(() => Project);
            set => SetProperty(() => Project, value);
        }

        /// <summary>
        /// 设备指示灯是否闪烁
        /// </summary>
        public bool DeviceStatusFlashing
        {
            get => GetProperty(() => DeviceStatusFlashing);
            set => SetProperty(() => DeviceStatusFlashing, value);
        }

        /// <summary>
        /// 设备状态常亮 绿色代表正常
        /// </summary>
        public bool DeviceStatusChangLiang
        {
            get => GetProperty(() => DeviceStatusChangLiang);
            set => SetProperty(() => DeviceStatusChangLiang, value);
        }

        /// <summary>
        /// 设备名称
        /// </summary>
        public string DeviceName
        {
            get => GetProperty(() => DeviceName);
            set => SetProperty(() => DeviceName, value);
        }

        /// <summary>
        /// 设备类型
        /// </summary>
        public string DeviceType
        {
            get => GetProperty(() => DeviceType);
            set => SetProperty(() => DeviceType, value);
        }

        /// <summary>
        /// 底层插件包版本（热更新后一目了然）
        /// </summary>
        public string DeviceVersion
        {
            get => GetProperty(() => DeviceVersion);
            set => SetProperty(() => DeviceVersion, value);
        }

        /// <summary>
        /// 设备层级
        /// </summary>
        public string DeviceHierarchy
        {
            get => GetProperty(() => DeviceHierarchy);
            set => SetProperty(() => DeviceHierarchy, value);
        }
        /// <summary>
        /// 设备层级（完整路径）
        /// </summary>
        public string DeviceHierarchyToolTip
        {
            get => GetProperty(() => DeviceHierarchyToolTip);
            set => SetProperty(() => DeviceHierarchyToolTip, value);
        }


        /// <summary>
        /// 采集时间
        /// </summary>
        public int CollectTime
        {
            get => GetProperty(() => CollectTime);
            set => SetProperty(() => CollectTime, value);
        }

        /// <summary>
        /// 采集状态
        /// </summary>
        public string CollectStatus
        {
            get => collectStatus;
            set => SetProperty(ref collectStatus, value);
        }
        private string collectStatus = LanguageHandler.GetLanguageValue("未知", App.LanguageOperate);

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdateTime
        {
            get => GetProperty(() => UpdateTime);
            set => SetProperty(() => UpdateTime, value);
        }

        /// <summary>
        /// LED 颜色
        /// </summary>
        public System.Windows.Media.Color LedColor
        {
            get => ledColor;
            set => SetProperty(ref ledColor, value);
        }
        private System.Windows.Media.Color ledColor = System.Windows.Media.Colors.Green;
        #endregion

        #region 事件
        /// <summary>
        /// 信息事件
        /// </summary>
        private async Task DqaHandler_OnInfoEventAsync(object? sender, EventInfoResult e)
        {
            //写入结果回调
            await ResultMsgAsync(DaqData, new ResultModel(e.Status, e.Message) { Time = e.Time });
        }

        /// <summary>
        /// 数据事件
        /// </summary>
        private async Task DqaHandler_OnDataEventAsync(object? sender, EventDataResult e)
        {
            if (DataSyncChannel is null)
                return;
            if (TokenSource is null)
                return;

            await DataSyncChannel.Writer.WriteAsync(e, TokenSource.Token);
        }

        #endregion

        #region 命令

        /// <summary>
        /// webapi 启动
        /// </summary>
        public IAsyncRelayCommand WASatrt => waStart ??= new AsyncRelayCommand(WASatrtAsync);
        private IAsyncRelayCommand? waStart;
        private async Task WASatrtAsync()
        {
            if (daqHandler == null)
            {
                return;
            }
            if (DaqData.WebApi == null)
            {
                if (ShowAsync != null) await ShowAsync(DeviceHierarchyToolTip + ", " + "未设置WebApi参数".GetLanguageValue(App.LanguageOperate));
                return;
            }

            if ((await daqHandler.WAStatusAsync(DaqData.Guid)).GetDetails(out string? message))
            {
                if (ShowAsync != null) await ShowAsync(DeviceHierarchyToolTip + ", " + message);
                return;
            }

            OperateResult result = await daqHandler.WAOnAsync(DaqData.Guid, DaqData.WebApi);
            //写入结果回调
            if (ShowAsync != null) await ShowAsync(DeviceHierarchyToolTip + ", " + (result.Status ? "WebApi启动成功".GetLanguageValue(App.LanguageOperate) : "WebApi启动失败".GetLanguageValue(App.LanguageOperate) + "," + result.Message));
        }

        /// <summary>
        /// webapi 停止
        /// </summary>
        public IAsyncRelayCommand WAStop => waStop ??= new AsyncRelayCommand(WAStopAsync);
        private IAsyncRelayCommand? waStop;
        private async Task WAStopAsync()
        {
            if (daqHandler == null)
            {
                return;
            }
            if (DaqData.WebApi == null)
            {
                if (ShowAsync != null) await ShowAsync(DeviceHierarchyToolTip + ", " + "未设置WebApi参数".GetLanguageValue(App.LanguageOperate));
                return;
            }

            if (!(await daqHandler.WAStatusAsync(DaqData.Guid)).GetDetails(out string? message))
            {
                if (ShowAsync != null) await ShowAsync(DeviceHierarchyToolTip + ", " + message);
                return;
            }

            OperateResult result = await daqHandler.WAOffAsync(DaqData.Guid);
            //写入结果回调
            if (ShowAsync != null) await ShowAsync(DeviceHierarchyToolTip + ", " + (result.Status ? "WebApi停止成功".GetLanguageValue(App.LanguageOperate) : "WebApi停止失败".GetLanguageValue(App.LanguageOperate) + "," + result.Message));
        }

        /// <summary>
        /// webapi 示例请求
        /// </summary>
        public IAsyncRelayCommand WARequestExample => waRequestExample ??= new AsyncRelayCommand(WARequestExampleAsync);
        private IAsyncRelayCommand? waRequestExample;
        private async Task WARequestExampleAsync()
        {
            if (daqHandler == null)
            {
                return;
            }
            if (DaqData.WebApi == null)
            {
                if (ShowAsync != null) await ShowAsync(DeviceHierarchyToolTip + ", " + "未设置WebApi参数".GetLanguageValue(App.LanguageOperate));
                return;
            }
            OperateResult result = await daqHandler.WARequestExampleAsync(DaqData.Guid);
            //写入结果回调
            if (ShowAsync != null) await ShowAsync($"{DeviceHierarchyToolTip}\r\n" + result.ResultData.ToString());
        }

        /// <summary>
        /// 采集
        /// </summary>
        public IAsyncRelayCommand Collect => collect ??= new AsyncRelayCommand(CollectAsync);
        private IAsyncRelayCommand? collect;
        private async Task CollectAsync()
        {
            if (!IsRun)
            {
                if (daqHandler == null)
                {
                    daqHandler = await DqaHandler.InstanceAsync(DaqData);
                    daqHandler.OnDataEventAsync -= DqaHandler_OnDataEventAsync;
                    daqHandler.OnInfoEventAsync -= DqaHandler_OnInfoEventAsync;
                    daqHandler.OnDataEventAsync += DqaHandler_OnDataEventAsync;
                    daqHandler.OnInfoEventAsync += DqaHandler_OnInfoEventAsync;
                }

                //内部实现组包
                OperateResult result = result = await daqHandler.SubscribeAsync(DaqData.Guid, AddressDatas.Keys.ToList(), DaqData.AutoPack);

                if (result.Status)
                {
                    if (folderStates.Count > 0)
                    {
                        GlobalConfigModel.uaService.RemoveFolder([folderStates[0].NodeId]);
                        folderStates[0].Dispose();
                        folderStates.Clear();
                        folderState.Dispose();
                        folderState = null;
                    }

                    _addressMap.Clear();
                    _failedAddress.Clear();
                    _failedBytesModels.Clear();

                    if (ShowAsync != null) await ShowAsync(DeviceHierarchyToolTip + ", " + "启动采集".GetLanguageValue(App.LanguageOperate));

                    CollectStatus = LanguageHandler.GetLanguageValue("正常", App.LanguageOperate);
                    DeviceStatusFlashing = true;
                    DeviceStatusChangLiang = true;
                    runtime.Start();

                    if (DaqData.WebApi != null)
                    {
                        await WASatrtAsync();
                    }

                    if (TokenSource == null)
                    {
                        TokenSource = new CancellationTokenSource();
                    }

                    if (UaSyncChannel == null)
                    {
                        UaSyncChannel = Channel.CreateBounded<AddressValue>(channel);
                        _ = UaSyncChannelDataEventAsync(TokenSource.Token);
                    }

                    if (DataSyncChannel == null)
                    {
                        DataSyncChannel = Channel.CreateBounded<EventDataResult>(channel);
                        _ = DataSyncChannelDataEventAsync(TokenSource.Token);
                    }

                    IsRun = true;
                }
                else
                {
                    DeviceStatusFlashing = false;
                    DeviceStatusChangLiang = false;
                }
                //写入结果回调
                await ResultMsgAsync(DaqData, result);
                if (!result.Status && AddressDatas.Count == 0)
                {
                    await ResultMsgAsync(DaqData, OperateResult.CreateFailureResult("请检查“项目详情”中传输设备是否正确设置给每个地址".GetLanguageValue(App.LanguageOperate)));
                }
            }
        }

        /// <summary>
        /// 停止
        /// </summary>
        public IAsyncRelayCommand Stop => stop ??= new AsyncRelayCommand(StopAsync);
        private IAsyncRelayCommand? stop;
        private async Task StopAsync()
        {
            if (daqHandler == null)
            {
                return;
            }

            // 取消
            if (TokenSource != null)
            {
                TokenSource.Cancel();
                TokenSource.Dispose();
                TokenSource = null;
            }

            if (DaqData.WebApi != null)
            {
                await WAStopAsync();
            }

            daqHandler?.OnDataEventAsync -= DqaHandler_OnDataEventAsync;
            daqHandler?.OnInfoEventAsync -= DqaHandler_OnInfoEventAsync;
            if (daqHandler is not null)
            {
                await daqHandler.UnSubscribeAsync(DaqData.Guid, AddressDatas.Keys.ToList());
                await daqHandler.DisposeAsync();
            }
            daqHandler = null;

            foreach (var item in mqHandlers)
            {
                await item.Value.DisposeAsync();
            }
            mqHandlers.Clear();

            CollectStatus = LanguageHandler.GetLanguageValue("停止", App.LanguageOperate);
            DeviceStatusFlashing = false;
            DeviceStatusChangLiang = false;
            IsRun = false;
            runtime.Stop();

            if (UaSyncChannel != null)
            {
                //停止
                UaSyncChannel.Writer.TryComplete();
                //清空队列
                while (UaSyncChannel.Reader.TryRead(out AddressValue? item)) { }
                //置空
                UaSyncChannel = null;
            }

            if (DataSyncChannel != null)
            {
                //停止
                DataSyncChannel.Writer.TryComplete();
                //清空队列
                while (DataSyncChannel.Reader.TryRead(out EventDataResult? item)) { }
                //置空
                DataSyncChannel = null;
            }

            if (ShowAsync != null) await ShowAsync(DeviceHierarchyToolTip + ", " + "停止采集".GetLanguageValue(App.LanguageOperate));

        }

        /// <summary>
        /// 重试
        /// </summary>
        public IAsyncRelayCommand Retry => retry ??= new AsyncRelayCommand(RetryAsync);
        private IAsyncRelayCommand? retry;
        private async Task RetryAsync()
        {
            runtime.Reset();
            await StopAsync();
            await CollectAsync();
            if (ShowAsync != null) await ShowAsync(DeviceHierarchyToolTip + ", " + "重试".GetLanguageValue(App.LanguageOperate));
        }

        /// <summary>
        /// 软启动采集
        /// </summary>
        public IAsyncRelayCommand OnSoftCollect => onSoftCollect ??= new AsyncRelayCommand(OnSoftCollectAsync);
        private IAsyncRelayCommand? onSoftCollect;
        private async Task OnSoftCollectAsync()
        {
            Project.IsSoftStart = true;
            await Project.SetAsync(GlobalConfigModel.ProjectDict);
            if (ShowAsync != null) await ShowAsync(DeviceHierarchyToolTip + ", " + "添加软启采集成功".GetLanguageValue(App.LanguageOperate));
        }

        /// <summary>
        /// 取消软启动采集
        /// </summary>
        public IAsyncRelayCommand OffSoftCollect => offSoftCollect ??= new AsyncRelayCommand(OffSoftCollectAsync);
        private IAsyncRelayCommand? offSoftCollect;
        private async Task OffSoftCollectAsync()
        {
            Project.IsSoftStart = false;
            await Project.SetAsync(GlobalConfigModel.ProjectDict);
            if (ShowAsync != null) await ShowAsync(DeviceHierarchyToolTip + ", " + "取消软启采集成功".GetLanguageValue(App.LanguageOperate));
        }
        #endregion

        #region 功能方法
        /// <summary>
        /// 通道地址事件消费
        /// </summary>
        private async Task UaSyncChannelDataEventAsync(CancellationToken token)
        {
            try
            {
                while (await UaSyncChannel.Reader.WaitToReadAsync(token))
                {
                    while (UaSyncChannel.Reader.TryRead(out AddressValue? addressValue))
                    {
                        if (token.IsCancellationRequested)
                            break;

                        if (addressValue.Quality != QualityType.Normal)
                        {
                            await LogHelper.ErrorAsync($"{addressValue.AddressName} - {addressValue.Message}", foldername: Path.Combine("UaService", "Transmit", "Failure"), token: token);
                            continue;
                        }

                        FolderState fs = await UaCreateFolder();
                        if (fs == null)
                        {
                            continue;
                        }

                        //数据源
                        string addressName = addressValue.AddressName;
                        DataType dataType = addressValue.AddressDataType;
                        object? value = addressValue.ResultValue;

                        //校验
                        var service = GlobalConfigModel.uaService;
                        if (service is null || !service.GetStatus().Status)
                            continue;

                        if (!_addressMap.ContainsKey(addressName) && !_failedAddress.ContainsKey(addressName))
                        {
                            if (!_typeMap.TryGetValue(dataType, out var builtInType))
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
                            }, folderState);

                            if (!createResult.Status)
                            {
                                // 标记失败，避免每个数据事件重复创建并刷屏消息
                                _failedAddress[addressName] = 0;
                                await ShowAsync?.Invoke(createResult.Message);
                                continue;
                            }

                            // 只在创建成功后刷新一次地址列表
                            var res = service.GetAddressArray().GetSource<List<string>>();
                            string format = $"s={uaServerAddressSpaceName}.{Project.GetHierarchyPath(".")}.{addressName}";
                            if (res != null)
                            {
                                foreach (var nodeId in res)
                                {
                                    if (nodeId.Contains(format, StringComparison.Ordinal))
                                    {
                                        _addressMap[addressName] = nodeId;
                                        break;
                                    }
                                }
                            }
                        }

                        // 写入
                        if (!_addressMap.TryGetValue(addressName, out var realAddress))
                        {
                            // 创建成功但未能映射到真实地址，标记避免重复创建
                            if (!_failedAddress.ContainsKey(addressName))
                                _failedAddress[addressName] = 0;
                            continue;
                        }

                        _singleWriteDict[realAddress] = new WriteModel(value, dataType);

                        var writeResult = await service.WriteAsync(_singleWriteDict);

                        _singleWriteDict.Clear();

                        if (!writeResult.Status && ShowAsync != null)
                            await ShowAsync.Invoke(writeResult.Message);
                    }
                }
            }
            catch (TaskCanceledException)
            {
            }
            catch (ChannelClosedException ex2)
            {
                await ResultMsgAsync(DaqData, EventInfoResult.CreateFailureResult("[ UaSyncChannelDataEventAsync ] 通道已关闭：" + ex2.Message));
            }
            catch (OperationCanceledException ex3)
            {
                await ResultMsgAsync(DaqData, EventInfoResult.CreateFailureResult("[ UaSyncChannelDataEventAsync ] 操作已取消：" + ex3.Message));
            }
            catch (Exception ex4)
            {
                await ResultMsgAsync(DaqData, EventInfoResult.CreateFailureResult("[ UaSyncChannelDataEventAsync ] 异常：" + ex4.Message));
            }
        }
        /// <summary>
        /// 创建UA层级
        /// </summary>
        /// <returns></returns>
        private async Task<FolderState?> UaCreateFolder()
        {
            try
            {
                if (GlobalConfigModel.uaService is null)
                    return null;

                if (folderState != null)
                {
                    return folderState;
                }

                //比对层级
                if (uaServerAddressSpaceName.IsNullOrWhiteSpace())
                {
                    uaServerAddressSpaceName = GlobalConfigModel.uaService.GetBasicsArgs().GetSource<OpcUaServiceData.Basics>().AddressSpaceName;
                }

                if (GlobalConfigModel.uaService != null && GlobalConfigModel.uaService.GetStatus().Status)
                {
                    FolderState folder = null;
                    //创建层级
                    foreach (var item in DeviceHierarchyToolTip.TrimAll().Split('>'))
                    {
                        OperateResult operateResult = GlobalConfigModel.uaService.CreateFolder(item, folder);
                        if (operateResult.GetDetails(out string? msg))
                        {
                            folder = operateResult.GetSource<FolderState>();
                            folderStates.Add(folder);
                        }
                        else
                        {
                            await ShowAsync.Invoke(msg);
                        }
                    }
                    folderState = folder;
                }
                else
                {
                    return null;
                }
                return folderState;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 通道数据事件消费
        /// </summary>
        private async Task DataSyncChannelDataEventAsync(CancellationToken token)
        {
            try
            {
                while (await DataSyncChannel.Reader.WaitToReadAsync(token))
                {
                    if (DataSyncChannel is null)
                        return;
                    while (DataSyncChannel.Reader.TryRead(out EventDataResult? e))
                    {
                        if (token.IsCancellationRequested)
                            break;

                        if (!e.Status)
                        {
                            await ResultMsgAsync(DaqData, e);
                            continue;
                        }

                        // 支持字典与列表两种数据形态（列表为多批次解包结果）
                        switch (e.ResultData)
                        {
                            case ConcurrentDictionary<string, AddressValue> dict:
                                await ProcessKeysAsync(dict, token);
                                break;
                            case List<ConcurrentDictionary<string, AddressValue>> list:
                                foreach (var d in list)
                                    await ProcessKeysAsync(d, token);
                                break;
                            default:
                                await LogHelper.ErrorAsync($"[ DataSyncChannelDataEventAsync ] 未知数据形态：{e.Message}");
                                break;
                        }
                    }
                }
            }
            catch (TaskCanceledException ex1)
            {
                await ResultMsgAsync(DaqData, EventInfoResult.CreateFailureResult("[ DataSyncChannelDataEventAsync ] 操作已取消：" + ex1.Message));
            }
            catch (ChannelClosedException ex2)
            {
                await ResultMsgAsync(DaqData, EventInfoResult.CreateFailureResult("[ DataSyncChannelDataEventAsync ] 通道已关闭：" + ex2.Message));
            }
            catch (OperationCanceledException ex3)
            {
                await ResultMsgAsync(DaqData, EventInfoResult.CreateFailureResult("[ DataSyncChannelDataEventAsync ] 操作已取消：" + ex3.Message));
            }
            catch (Exception ex4)
            {
                await ResultMsgAsync(DaqData, EventInfoResult.CreateFailureResult("[ DataSyncChannelDataEventAsync ] 异常：" + ex4.Message));
            }
        }

        /// <summary>
        /// 处理一组地址值：逐地址解包/转发，单地址异常不影响整组消费
        /// </summary>
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
                        await ResultMsgAsync(DaqData, EventInfoResult.CreateFailureResult($"{DeviceHierarchyToolTip}, {addressValue.AddressName} - {kv.Value.Message}"));
                        continue;
                    }

                    if (!_addressIndex.TryGetValue(kv.Key, out var addressModel) ||
                        !_mqPluginMap.TryGetValue(kv.Key, out var pluginConfigs))
                        continue;

                    // 字节处理模型：缓存命中且参数来源未变才复用；组包移除(参数为空)时清除缓存
                    List<BytesModel>? bm = GetBytesModels(addressValue);

                    // 参数存在但解析失败：该地址配置了字节解析但无法获得模型，提示后丢弃，避免每周期刷屏
                    if (bm == null && addressValue.AddressExtendParam != null)
                    {
                        if (_failedBytesModels.TryAdd(addressValue.AddressName, 0))
                            ShowAsync?.Invoke($"{DeviceHierarchyToolTip}, {addressValue.AddressName} - {"扩展参数不正确".GetLanguageValue(App.LanguageOperate)}");
                        continue;
                    }

                    // 无字节模型，直接转发
                    if (bm == null)
                    {
                        if (TokenSource is null)
                            return;
                        await UaSyncChannel.Writer.WriteAsync(addressValue, TokenSource.Token);
                        await MqTransmissionAsync(new() { [addressModel] = addressValue }, pluginConfigs);
                        continue;
                    }

                    // 字节转换与转发（组包批次 / 手动设置扩展参数的地址均按模型解包，不区分值是否字节数组）
                    await TransformAndForwardAsync(addressValue, bm, addressModel, pluginConfigs);
                }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested)
                        break;
                    await ResultMsgAsync(DaqData, EventInfoResult.CreateFailureResult($"[ DataSyncChannelDataEventAsync ] 地址 {kv.Key} 处理异常：" + ex.Message));
                }
            }
        }

        /// <summary>
        /// 获取地址的字节处理模型<br/>
        /// 组包配置存在时：缓存命中且参数来源未变直接复用，来源变化(配置更新)时重新解析<br/>
        /// 组包配置移除时：立即清除缓存（配置为权威信号）；过渡期数据仍带参数则照常解析不丢<br/>
        /// 数据不携带扩展参数(不可组包地址/未按组包订阅地址)：无缓存或缓存不被使用，直接直通，不做任何缓存操作
        /// </summary>
        private List<BytesModel>? GetBytesModels(AddressValue addressValue)
        {
            object? param = addressValue.AddressExtendParam;

            // 组包配置已移除：清除缓存（配置为权威信号，不依赖数据是否仍携带参数）
            if (DaqData?.AutoPack == null)
            {
                bytesModels.TryRemove(addressValue.AddressName, out _);
                // 数据仍带参数：驱动尚未重新订阅，参数来自数据本身，照常解析过渡期数据（不再重建缓存）
                return param == null ? null : ParseBytesModels(param);
            }

            // 数据不携带扩展参数：这类地址本无缓存，或值非字节数组不会被解包使用，直接直通
            if (param == null)
                return null;

            // 缓存命中且参数来源未变，直接复用，避免每个采集周期重复反序列化与文件读取
            if (bytesModels.TryGetValue(addressValue.AddressName, out var cached) && cached.Source == param)
                return cached.Models;

            List<BytesModel>? models = ParseBytesModels(param);
            if (models != null)
                bytesModels[addressValue.AddressName] = (param, models);
            return models;
        }

        /// <summary>
        /// 解析扩展参数为字节处理模型<br/>
        /// 支持组包模型集合、JSON 字符串、JSON 文件路径三种来源（手动设置与组包格式一致）
        /// </summary>
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

        /// <summary>
        /// 字节转换并转发到 UA 通道与 MQ
        /// </summary>
        private async Task TransformAndForwardAsync(AddressValue addressValue, List<BytesModel> bm, IAddressModel addressModel, List<PluginConfigModel> pluginConfigs)
        {
            bytesHandler ??= await BytesHandler.InstanceAsync(DeviceName);

            OperateResult result = await bytesHandler.TransformAsync(addressValue.ResultValue.GetSource<byte[]>(), addressValue.Time, bm);
            if (!result.GetDetails(out ConcurrentDictionary<string, AddressValue>? res))
            {
                await ResultMsgAsync(DaqData, EventInfoResult.CreateFailureResult($"{DeviceHierarchyToolTip}, {addressValue.AddressName} - 解包失败：" + result.Message));
                return;
            }

            foreach (var item in res)
            {
                // 以原始地址名重新查索引与 MQ 配置，避免整批数据共用批次首地址的配置
                _addressIndex.TryGetValue(item.Key, out var sourceModel);
                _mqPluginMap.TryGetValue(item.Key, out var sourcePlugins);
                sourceModel ??= addressModel;

                AddressModel newModel = new()
                {
                    Address = item.Key,
                    Describe = item.Value.AddressDescribe,
                    EncodingType = item.Value.EncodingType,
                    Guid = sourceModel.Guid,
                    SimplifyValue = sourceModel.SimplifyValue,
                    Length = item.Value.Length,
                    Time = item.Value.Time,
                    Topic = sourceModel.Topic,
                    Type = item.Value.AddressDataType,
                };
                if (TokenSource is null)
                    return;
                await UaSyncChannel.Writer.WriteAsync(item.Value, TokenSource.Token);
                await MqTransmissionAsync(new() { [newModel] = item.Value }, sourcePlugins ?? pluginConfigs);
            }
        }

        /// <summary>
        /// MQ 传输
        /// </summary>
        private async Task MqTransmissionAsync(Dictionary<IAddressModel, AddressValue> inParam, List<PluginConfigModel> pluginConfigs)
        {
            foreach (var item in pluginConfigs)
            {
                if (!mqHandlers.TryGetValue(item.Guid, out var mq))
                {
                    mq = await MqHandler.InstanceAsync(item);
                    mqHandlers[item.Guid] = mq;
                }
                var result = await mq.ProduceAsync(item.Guid, inParam);
                await ResultMsgAsync(item, result);
            }
        }

        /// <summary>
        /// 重建地址缓存
        /// </summary>
        private void RebuildAddressCache()
        {
            _addressIndex = AddressDatas.Keys
                .Where(a => !string.IsNullOrEmpty(a.Address))
                .ToDictionary(a => a.Address!);

            _mqPluginMap = AddressDatas
                .Where(kv => !string.IsNullOrEmpty(kv.Key.Address))
                .GroupBy(kv => kv.Key.Address!)
                .ToDictionary(g => g.Key, g => g.SelectMany(x => x.Value).ToList());

            //循环MQ插件路径（清空操作移出循环，避免只保留最后一组插件的路径）
            MqPluginPath ??= new();
            MqPluginPath.Clear();
            foreach (var item in _mqPluginMap)
            {
                foreach (var model in item.Value)
                {
                    string path = PluginHandlerCore.PluginOperate.GetPluginPath(model.Name);
                    if (!MqPluginPath.Contains(path))
                    {
                        MqPluginPath.Add(path);
                    }
                }
            }
        }


        /// <summary>
        /// 按插件类名查 PluginList.json 的包版本（与 Web 端 DeviceRuntime 同源逻辑）
        /// </summary>
        private static string ResolvePluginVersion(string pluginName)
        {
            try
            {
                var list = PluginHandlerCore.GetPluginUIConfig<System.Collections.ObjectModel.ObservableCollection<PluginListModel>>(GlobalConfigModel.UI_PluginListConfigPath);
                return list?.FirstOrDefault(p => p.Name == pluginName)?.Version ?? "-";
            }
            catch
            {
                return "-";
            }
        }

        /// <summary>
        /// 配置
        /// </summary>
        /// <param name="model">项目信息</param>
        public async Task SettingsAsync(IProjectTreeViewModel model, Func<PluginConfigModel, BaseModel, Task> resultAsync, Func<string, Task> showAsync)
        {
            DaqPluginPath = PluginHandlerCore.PluginOperate.GetPluginPath(model.DaqDetails.Name);
            ResultAsync = resultAsync;
            ShowAsync = showAsync;
            Project = model;
            DeviceName = model.Name;
            DeviceType = model.DaqDetails.Name;
            DeviceVersion = ResolvePluginVersion(model.DaqDetails.Name);
            UpdateTime = model.DaqDetails.Time;
            DeviceHierarchyToolTip = model.GetHierarchyPath();
            DeviceHierarchy = DeviceHierarchyToolTip.TruncateByBytes(36);
            AddressDatas = model.Details.ToAddressMqDictionary();
            AddressCount = AddressDatas.Count;
            RebuildAddressCache();
            DaqData = model.DaqDetails;
            if (IsRun)
            {
                await RetryAsync();
            }
            if (model.IsSoftStart)
            {
                await CollectAsync();
            }
        }

        /// <summary>
        /// 上次结果状态（用于避免每条样本都触发 UI 属性通知）
        /// </summary>
        private bool? _lastResultStatus;

        /// <summary>
        /// 结果消息抛出<br/>
        /// 仅在状态翻转（成功→失败 / 失败→成功）时更新 UI 属性，高频率采集下避免每样本触发绑定通知
        /// </summary>
        public async Task ResultMsgAsync(PluginConfigModel pcm, BaseModel bm)
        {
            if (_lastResultStatus != bm.Status)
            {
                _lastResultStatus = bm.Status;
                if (bm.Status)
                {
                    LedColor = System.Windows.Media.Colors.Green;
                    CollectStatus = LanguageHandler.GetLanguageValue("正常", App.LanguageOperate);
                }
                else
                {
                    LedColor = System.Windows.Media.Colors.Red;
                    CollectStatus = LanguageHandler.GetLanguageValue("异常", App.LanguageOperate);
                    DeviceStatusChangLiang = true;
                }
            }
            await ResultAsync.Invoke(pcm, bm);
        }

        /// <summary>
        /// 开始每秒读取运行时间
        /// </summary>
        public void StartPolling(RuntimeSecondsRecorderHandler recorder)
        {
            _cts = new CancellationTokenSource();

            _ = PollAsync(recorder, _cts.Token);
        }

        private async Task PollAsync(RuntimeSecondsRecorderHandler recorder, CancellationToken token)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try
            {
                while (await timer.WaitForNextTickAsync(token))
                {
                    CollectTime = (int)recorder.TotalSeconds;
                }
            }
            catch (OperationCanceledException) { }
        }
        private CancellationTokenSource _cts;
        /// <summary>
        /// 停止轮询
        /// </summary>
        public void StopPolling()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        public override string ToString()
        {
            return DaqData.Guid;
        }

        public void Dispose()
        {
            // 取消静态语言事件订阅，防止实例被静态事件根住无法回收
            Snet.Core.handler.LanguageHandler.OnLanguageEventAsync -= LanguageHandler_OnLanguageEventAsync;

            // 取消令牌，防止后台异步任务继续执行
            if (TokenSource != null)
            {
                TokenSource.Cancel();
                TokenSource.Dispose();
                TokenSource = null;
            }
            daqHandler?.Dispose();
            daqHandler = null;
            foreach (var item in mqHandlers)
            {
                item.Value.Dispose();
            }
            mqHandlers.Clear();
            _mqPluginMap.Clear();
            runtime.Stop();
            StopPolling();
            bytesHandler?.Dispose();
            bytesModels.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            // 取消静态语言事件订阅，防止实例被静态事件根住无法回收
            Snet.Core.handler.LanguageHandler.OnLanguageEventAsync -= LanguageHandler_OnLanguageEventAsync;

            if (daqHandler != null)
                await daqHandler.DisposeAsync();
            daqHandler = null;
            foreach (var item in mqHandlers)
            {
                await item.Value.DisposeAsync();
            }
            mqHandlers.Clear();
            _mqPluginMap.Clear();
            runtime.Stop();
            StopPolling();
            if (bytesHandler != null)
            {
                await bytesHandler.DisposeAsync();
            }
            bytesModels.Clear();
            await StopAsync();
        }
        #endregion

        #region 状态
        private Task LanguageHandler_OnLanguageEventAsync(object? sender, EventLanguageResult e)
        {
            string text = CollectStatus;
            CollectStatus = LanguageHandler.GetLanguageValue(text, App.LanguageOperate);
            return Task.CompletedTask;
        }

        #endregion
    }
}
