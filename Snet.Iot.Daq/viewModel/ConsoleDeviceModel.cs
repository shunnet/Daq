using CommunityToolkit.Mvvm.Input;
using Opc.Ua;
using Snet.Core.handler;
using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.handler;
using Snet.Iot.Daq.Core.@interface;
using Snet.Iot.Daq.Core.mvvm;
using Snet.Iot.Daq.Core.opc.ua.service;
using Snet.Iot.Daq.data;
using Snet.Log;
using Snet.Model.data;
using Snet.Model.@enum;
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
        private BytesHandler? bytesHandler;

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
        private DqaHandler? daqHandler;

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
        private FolderState? folderState;
        /// <summary>文件夹所属的 UA 实例，服务重启后必须丢弃旧地址空间缓存。</summary>
        private OpcUaServiceOperate? folderService;
        /// <summary>运行配置快照签名，避免其他设备编辑或无关 UI 刷新导致重复停止和订阅。</summary>
        private string settingsSignature = string.Empty;

        /// <summary>
        /// 层级集合
        /// </summary>
        private List<FolderState> folderStates = new();

        /// <summary>
        /// 地址索引缓存（单线程路径，每次重建）
        /// </summary>
        private readonly ConcurrentDictionary<string, IAddressModel> _addressIndex = new();

        /// <summary>
        /// MQ 配置映射缓存（单线程路径，每次重建）
        /// </summary>
        private readonly ConcurrentDictionary<string, List<PluginConfigModel>> _mqPluginMap = new();




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
        private Task? _dataConsumerTask;
        private Task? _uaConsumerTask;

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
            var channel = DataSyncChannel;
            var source = TokenSource;
            if (channel is null || source is null) return;

            try { await channel.Writer.WriteAsync(e, source.Token); }
            catch (ObjectDisposedException) when (!ReferenceEquals(source, TokenSource)) { /* 停止已释放这次事件所属的取消源。 */ }
            catch (OperationCanceledException) { /* 停止时取消入队，驱动事件正常结束。 */ }
            catch (ChannelClosedException) { /* 停止时通道完成，不将其报告为驱动故障。 */ }
        }

        #endregion

        #region 命令

        /// <summary>
        /// webapi 启动
        /// </summary>
        public IAsyncRelayCommand WASatrt => waStart ??= new AsyncRelayCommand(WASatrtAsync);
        private IAsyncRelayCommand? waStart;
        /// <summary>与采集启停共用操作门，避免 WebApi 操作访问正在释放的驱动。</summary>
        private Task WASatrtAsync() => RunDeviceOperationAsync(WASatrtCoreAsync);

        /// <summary>操作门内执行 WebApi WASatrt，处理器不存在时不创建新连接。</summary>
        private async Task WASatrtCoreAsync()
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
        /// <summary>与采集启停共用操作门，避免 WebApi 操作访问正在释放的驱动。</summary>
        private Task WAStopAsync() => RunDeviceOperationAsync(WAStopCoreAsync);

        /// <summary>操作门内执行 WebApi WAStop，处理器不存在时不创建新连接。</summary>
        private async Task WAStopCoreAsync()
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
        /// <summary>与采集启停共用操作门，避免 WebApi 操作访问正在释放的驱动。</summary>
        private Task WARequestExampleAsync() => RunDeviceOperationAsync(WARequestExampleCoreAsync);

        /// <summary>操作门内执行 WebApi WARequestExample，处理器不存在时不创建新连接。</summary>
        private async Task WARequestExampleCoreAsync()
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
            if (ShowAsync != null) await ShowAsync($"{DeviceHierarchyToolTip}\r\n" + (result.ResultData?.ToString() ?? result.Message ?? string.Empty));
        }

        /// <summary>
        /// 采集
        /// </summary>
        public IAsyncRelayCommand Collect => collect ??= new AsyncRelayCommand(CollectAsync);
        private IAsyncRelayCommand? collect;
        /// <summary>启动与停止、配置替换共用操作门，避免不同命令并发创建处理器和消费者。</summary>
        private Task CollectAsync() => RunDeviceOperationAsync(CollectCoreAsync);

        /// <summary>操作门内启动采集；消费任务归本实例所有，停止时等待退出。</summary>
        private async Task CollectCoreAsync()
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
                OperateResult result = await daqHandler.SubscribeAsync(DaqData.Guid, AddressDatas.Keys.ToList(), DaqData.AutoPack);

                if (result.Status)
                {
                    if (folderStates.Count > 0)
                    {
                        folderService?.RemoveFolder([folderStates[^1].NodeId]);
                        folderStates.Clear();
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
                        await WASatrtCoreAsync();
                    }

                    if (TokenSource == null)
                    {
                        TokenSource = new CancellationTokenSource();
                    }

                    if (UaSyncChannel == null)
                    {
                        UaSyncChannel = Channel.CreateBounded<AddressValue>(channel);
                        var token = TokenSource.Token;
                        _uaConsumerTask = Task.Run(() => UaSyncChannelDataEventAsync(token));
                    }

                    if (DataSyncChannel == null)
                    {
                        DataSyncChannel = Channel.CreateBounded<EventDataResult>(channel);
                        var token = TokenSource.Token;
                        _dataConsumerTask = Task.Run(() => DataSyncChannelDataEventAsync(token));
                    }

                    IsRun = true;
                }
                else
                {
                    await StopCoreAsync();
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
        /// <summary>等待其他设备操作完成后停止，不因处理器已为空而漏清理消费者。</summary>
        private Task StopAsync() => RunDeviceOperationAsync(StopCoreAsync);

        /// <summary>操作门内取消生产与消费，等待任务退出后释放驱动和通道。</summary>
        private async Task StopCoreAsync()
        {
            // 逐项清理并记录插件错误，一个资源失败不能阻止其余资源释放。
            async Task ReleaseAsync(Func<Task> action)
            {
                try { await action(); }
                catch (Exception ex) { await LogHelper.ErrorAsync($"设备停止清理异常：{ex.Message}"); }
            }
            daqHandler?.OnDataEventAsync -= DqaHandler_OnDataEventAsync;
            DataSyncChannel?.Writer.TryComplete();
            UaSyncChannel?.Writer.TryComplete();

            // 取消
            if (TokenSource != null)
            {
                TokenSource.Cancel();

            }

            await ReleaseAsync(() => Task.WhenAll(_dataConsumerTask ?? Task.CompletedTask, _uaConsumerTask ?? Task.CompletedTask));
            _dataConsumerTask = null;
            _uaConsumerTask = null;
            // 先撤下可见引用，再释放取消源；迟到事件不能取到已释放的 Token。
            var stoppedSource = TokenSource;
            TokenSource = null;
            stoppedSource?.Dispose();

            if (DaqData?.WebApi != null)
            {
                await ReleaseAsync(WAStopCoreAsync);
            }

            daqHandler?.OnDataEventAsync -= DqaHandler_OnDataEventAsync;
            daqHandler?.OnInfoEventAsync -= DqaHandler_OnInfoEventAsync;
            if (daqHandler is not null)
            {
                await ReleaseAsync(() => daqHandler.UnSubscribeAsync(DaqData.Guid, AddressDatas.Keys.ToList()));
                await ReleaseAsync(() => daqHandler.DisposeAsync().AsTask());
            }
            daqHandler = null;

            foreach (var item in mqHandlers)
            {
                await ReleaseAsync(() => item.Value.DisposeAsync().AsTask());
            }
            mqHandlers.Clear();

            CollectStatus = LanguageHandler.GetLanguageValue("停止", App.LanguageOperate);
            DeviceStatusFlashing = false;
            DeviceStatusChangLiang = false;
            IsRun = false;
            runtime.Stop();
            _lastResultStatus = null;

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
        /// <summary>重试作为一个完整操作执行，停止与重新启动之间不允许配置写入。</summary>
        private Task RetryAsync() => RunDeviceOperationAsync(RetryCoreAsync);

        /// <summary>操作门内重新建立采集，重置本次运行计时。</summary>
        private async Task RetryCoreAsync()
        {
            runtime.Reset();
            await StopCoreAsync();
            await CollectCoreAsync();
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

                        var service = GlobalConfigModel.uaService;
                        if (service is null) continue;
                        FolderState? fs = await UaCreateFolder(service);
                        if (fs == null)
                        {
                            continue;
                        }

                        //数据源
                        string addressName = addressValue.AddressName;
                        DataType dataType = addressValue.AddressDataType;
                        object? value = addressValue.ResultValue;

                        //校验
                        if (!ReferenceEquals(service, GlobalConfigModel.uaService) || !service.GetStatus().Status)
                            continue;

                        if (!_addressMap.ContainsKey(addressName) && !_failedAddress.ContainsKey(addressName))
                        {
                            if (!UaForwarding.TypeMap.TryGetValue(dataType, out var builtInType))
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
                                _failedAddress[addressName] = 0;
                                if (ShowAsync is not null) await ShowAsync(createResult.Message);
                                continue;
                            }

                            // CreateAddress 与此映射共用父节点标识，保留命名空间且不做全量地址扫描。
                            _addressMap[addressName] = UaForwarding.CreateAddressNodeId(fs.NodeId, addressName).ToString();

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

                        var writeResult = await service.WriteAsync(_singleWriteDict, token);

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
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // 正常停止消费，不将取消显示为采集故障。
            }
            catch (Exception ex4)
            {
                await ResultMsgAsync(DaqData, EventInfoResult.CreateFailureResult("[ UaSyncChannelDataEventAsync ] 异常：" + ex4.Message));
            }
        }
        /// <summary>在捕获的 UA 服务上创建或复用设备层级；服务实例变化时重建缓存，避免向旧地址空间写入。</summary>
        /// <param name="service">本次消费捕获的服务，文件夹和后续写入必须使用同一实例。</param>
        /// <returns>成功创建的设备文件夹；服务未运行或创建失败时返回 null。</returns>
        private async Task<FolderState?> UaCreateFolder(OpcUaServiceOperate service)
        {
            if (!ReferenceEquals(folderService, service))
            {
                folderService = service;
                folderState = null;
                folderStates.Clear();
                _addressMap.Clear();
                _failedAddress.Clear();
                uaServerAddressSpaceName = string.Empty;
            }
            if (!service.GetStatus().Status) return null;
            if (folderState is not null) return folderState;
            FolderState? folder = null;
            foreach (var name in DeviceHierarchyToolTip.TrimAll().Split('>'))
            {
                var result = service.CreateFolder(name, folder);
                if (!result.Status || result.ResultData is not FolderState created)
                {
                    if (ShowAsync is not null) await ShowAsync(result.Message);
                    return null;
                }
                folder = created;
                folderStates.Add(created);
            }
            folderState = folder;
            return folder;
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
            catch (TaskCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (ChannelClosedException ex2)
            {
                await ResultMsgAsync(DaqData, EventInfoResult.CreateFailureResult("[ DataSyncChannelDataEventAsync ] 通道已关闭：" + ex2.Message));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
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

            OperateResult result = await bytesHandler.TransformAsync(addressValue.ResultValue.GetSource<byte[]>(), addressValue.Time, bm, isStringReverseByteWord: DaqData.AutoPack?.IsStringReverseByteWord ?? false);
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
        private async Task MqTransmissionAsync(ConcurrentDictionary<IAddressModel, AddressValue> inParam, List<PluginConfigModel> pluginConfigs)
        {
            foreach (var item in pluginConfigs)
            {
                if (!mqHandlers.TryGetValue(item.Guid, out var mq))
                {
                    mq = MqHandler.CreateScoped(item);
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
            _addressIndex.Clear();
            foreach (var address in AddressDatas.Keys.Where(a => !string.IsNullOrEmpty(a.Address)))
                _addressIndex[address.Address] = address;
            _mqPluginMap.Clear();
            foreach (var group in AddressDatas.Where(kv => !string.IsNullOrEmpty(kv.Key.Address)).GroupBy(kv => kv.Key.Address))
                _mqPluginMap[group.Key] = group.SelectMany(x => x.Value).ToList();

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
        public Task SettingsAsync(IProjectTreeViewModel model, Func<PluginConfigModel, BaseModel, Task> resultAsync, Func<string, Task> showAsync)
            => RunDeviceOperationAsync(() => SettingsCoreAsync(model, resultAsync, showAsync));

        /// <summary>操作门内先停止旧配置，再刷新项目引用及缓存，按原状态恢复采集。</summary>
        private async Task SettingsCoreAsync(IProjectTreeViewModel model, Func<PluginConfigModel, BaseModel, Task> resultAsync, Func<string, Task> showAsync)
        {
            var newAddresses = model.Details.ToAddressMqDictionary();
            var signature = DeviceSettings.CreateSignature(model, newAddresses);
            var first = string.IsNullOrEmpty(settingsSignature);
            var changed = signature != settingsSignature;
            var restart = IsRun && changed;
            // 配置确实改变时先等待旧消费者退出，无关刷新不会打断正在运行的设备。
            if (changed && (IsRun || daqHandler is not null)) await StopCoreAsync();
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
            AddressCount = ProjectHandlerCore.CountAddressNodes(model.Details);
            if (changed)
            {
                AddressDatas = newAddresses;
                RebuildAddressCache();
                settingsSignature = signature;
            }
            DaqData = model.DaqDetails;
            if (restart || (first && model.IsSoftStart))
            {
                if (restart) runtime.Reset();
                await CollectCoreAsync();
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
            if (ResultAsync is not null) await ResultAsync.Invoke(pcm, bm);
        }

        /// <summary>
        /// 开始每秒读取运行时间
        /// </summary>
        public void StartPolling(RuntimeSecondsRecorderHandler recorder)
        {
            lock (disposalLock)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (_pollTask is { IsCompleted: false }) return;
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                _pollTask = PollAsync(recorder, _cts.Token);
            }
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
        /// <summary>运行时间轮询的取消源，停止后仍由清理流程等待任务退出。</summary>
        private CancellationTokenSource? _cts;
        /// <summary>本实例拥有的运行时间轮询任务。</summary>
        private Task? _pollTask;
        /// <summary>
        /// 停止轮询
        /// </summary>
        public void StopPolling()
        {
            _cts?.Cancel();
        }

        public override string ToString()
        {
            return DaqData.Guid;
        }

        /// <summary>设备操作互斥门，仅使用异步等待，不阻塞界面线程。</summary>
        private readonly SemaphoreSlim operationGate = new(1, 1);
        /// <summary>保护清理任务的首次创建；临界区不执行或等待设备 I/O。</summary>
        private readonly object disposalLock = new();
        /// <summary>所有释放入口共享的清理任务，重复调用不会重复释放。</summary>
        private Task? disposalTask;
        /// <summary>终态标记；取得操作门后再次检查，防止释放后启动。</summary>
        private bool disposed;

        /// <summary>串行执行一个设备操作；已释放实例不再接受任何新的操作。</summary>
        /// <param name="operation">须在互斥门内完成的异步操作。</param>
        private async Task RunDeviceOperationAsync(Func<Task> operation)
        {
            await operationGate.WaitAsync();
            try { if (!disposed) await operation(); }
            finally { operationGate.Release(); }
        }

        /// <summary>兼容同步入口，启动同一异步清理并观察异常；需要等待完成的调用方应使用 DisposeAsync。</summary>
        public void Dispose()
        {
            _ = GetDisposalTask().ContinueWith(task => LogHelper.Error(task.Exception!.GetBaseException().Message),
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        /// <summary>等待消费者和轮询全部退出，再释放资源；所有调用等待同一任务。</summary>
        public ValueTask DisposeAsync() => new(GetDisposalTask());

        /// <summary>取得或创建唯一清理任务，创建流程先异步让出以缩短同步临界区。</summary>
        private Task GetDisposalTask()
        {
            lock (disposalLock) return disposalTask ??= DisposeCoreAsync();
        }

        /// <summary>终态清理；无论驱动停止是否成功都清理轮询和解析器。</summary>
        private async Task DisposeCoreAsync()
        {
            await Task.Yield();
            await operationGate.WaitAsync();
            try
            {
                disposed = true;
                LanguageHandler.OnLanguageEventAsync -= LanguageHandler_OnLanguageEventAsync;
                try { await StopCoreAsync(); }
                finally
                {
                    StopPolling();
                    try { if (_pollTask is not null) await _pollTask; }
                    finally
                    {
                        _cts?.Dispose();
                        _cts = null;
                        _mqPluginMap.Clear();
                        try { if (bytesHandler is not null) await bytesHandler.DisposeAsync(); }
                        finally { bytesHandler = null; bytesModels.Clear(); GC.SuppressFinalize(this); }
                    }
                }
            }
            finally { operationGate.Release(); }
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
