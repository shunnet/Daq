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
        /// 自动组包处理
        /// </summary>
        private PackerHandler autoPack;

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
        /// 字节处理模型
        /// </summary>

        private ConcurrentDictionary<string, List<BytesModel>> bytesModels = new();

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

                //使用自动组包，可降低PLC压力
                string[] keys = PackerHandler.GetSupportAutoPackDeviceTypes();
                string? key = keys.FirstOrDefault(k => DaqData.Param.Contains(k));
                OperateResult result = OperateResult.CreateFailureResult("采集失败".GetLanguageValue(App.LanguageOperate));
                //判断点位是否都放置了MQ传输设备

                if (key != null && DaqData.AutoPack != null)
                {
                    autoPack ??= PackerHandler.Instance(key);
                    List<IAddressModel>? models = AddressAutoPack(AddressDatas.Keys.ToList(), key, DaqData.AutoPack.MaxByteLength, DaqData.AutoPack.Format);
                    if (models != null)
                    {
                        result = await daqHandler.SubscribeAsync(DaqData.Guid, models);
                    }
                }
                else
                {
                    result = await daqHandler.SubscribeAsync(DaqData.Guid, AddressDatas.Keys.ToList());
                }


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
        /// 地址自动组包入口方法(插件工具自动组包入口)
        /// 根据设备类型将离散地址集合合并为批量读取结构，减少通信轮次
        /// </summary>
        /// <param name="addressModels">插件工具地址，包含待组包的地址列表</param>
        /// <param name="deviceType">设备类型标识（目前支持 "SiemensS7"）</param>
        /// <param name="maxByteLength">单次批量读取的最大字节数（西门子S7默认240/400）</param>
        /// <param name="format">数据字节序格式</param>
        /// <returns>组包后的地址对象，失败返回null</returns>
        public List<IAddressModel>? AddressAutoPack(List<IAddressModel> addressModels, string deviceType = "SiemensS7Net", int maxByteLength = 200, DataFormat format = DataFormat.ABCD)
        {
            Address address = new Address();
            address.AddressArray = addressModels.Where(m => string.IsNullOrEmpty(m.ExpandParam)).Select(m => new AddressDetails
            {
                SN = m.Guid,
                AddressAnotherName = m.AnotherName,
                AddressName = m.Address,
                AddressDataType = m.Type,
                AddressDescribe = m.Describe,
                EncodingType = m.EncodingType,

            }).ToList();
            Address? result = autoPack.AddressAutoPack(address, deviceType, maxByteLength, format);
            if (result == null) return null;
            List<IAddressModel> models = new List<IAddressModel>();
            foreach (var model in result.AddressArray)
            {
                models.Add(new AddressModelCore
                {
                    Length = model.Length,
                    EncodingType = model.EncodingType,
                    Address = model.AddressName,
                    Type = model.AddressDataType,
                    Describe = model.AddressDescribe,
                    ExpandParam = model.AddressExtendParam.ToJson()
                });
            }
            return models;
        }
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
                            continue;

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
                            continue;

                        if (!e.Status)
                        {
                            await ResultMsgAsync(DaqData, e);
                            continue;
                        }

                        var keys = e.GetSource<ConcurrentDictionary<string, AddressValue>>();
                        if (keys == null || keys.Count == 0)
                            continue;

                        foreach (var kv in keys)
                        {
                            if (!_addressIndex.TryGetValue(kv.Key, out var addressModel) ||
                                !_mqPluginMap.TryGetValue(kv.Key, out var pluginConfigs))
                                continue;

                            // 根据字节处理模型，优先从JSON字符串获取，否则从文件获取
                            // 统一以 addressModel.Address 为缓存键，先查缓存、miss 才解析，避免每个采集周期重复 JSON 反序列化与文件读取
                            if (!bytesModels.TryGetValue(addressModel.Address, out List<BytesModel>? bm) || bm == null)
                            {
                                bm = null;
                                try
                                {
                                    bm = kv.Value?.AddressExtendParam?.ToString()?.ToJsonEntity<List<BytesModel>>();
                                }
                                catch (System.Text.Json.JsonException)
                                {
                                    // AddressExtendParam 不是有效的 BytesModel JSON，可能是文件模式
                                }

                                if (bm != null)
                                {
                                    bytesModels.TryAdd(addressModel.Address, bm);
                                }
                                else if (addressModel.ExpandParam != null)
                                {
                                    if (!File.Exists(addressModel.ExpandParam))
                                    {
                                        ShowAsync?.Invoke(DeviceHierarchyToolTip + ", " + $" {addressModel.Address} -" + "扩展参数文件不存在".GetLanguageValue(App.LanguageOperate));
                                        continue;
                                    }
                                    bm = FileHandler.FileToString(addressModel.ExpandParam).ToJsonEntity<List<BytesModel>>();
                                    if (bm != null)
                                    {
                                        bytesModels.TryAdd(addressModel.Address, bm);
                                    }
                                }
                            }

                            // 无字节模型，直接转发
                            if (bm == null)
                            {
                                if (TokenSource is null)
                                    return;
                                await UaSyncChannel.Writer.WriteAsync(kv.Value, TokenSource.Token);
                                await MqTransmissionAsync(new() { [addressModel] = kv.Value }, pluginConfigs);
                                continue;
                            }

                            // 字节转换与转发
                            await TransformAndForwardAsync(kv.Value, bm, addressModel, pluginConfigs);
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
        /// 字节转换并转发到 UA 通道与 MQ
        /// </summary>
        private async Task TransformAndForwardAsync(AddressValue addressValue, List<BytesModel> bm, IAddressModel addressModel, List<PluginConfigModel> pluginConfigs)
        {
            bytesHandler ??= await BytesHandler.InstanceAsync(DeviceName);

            OperateResult result = await bytesHandler.TransformAsync(addressValue.ResultValue.GetSource<byte[]>(), addressValue.Time, bm);
            if (!result.GetDetails(out ConcurrentDictionary<string, AddressValue>? res))
                return;

            foreach (var item in res)
            {
                AddressModel newModel = new()
                {
                    Address = item.Key,
                    Describe = item.Value.AddressDescribe,
                    EncodingType = item.Value.EncodingType,
                    Guid = item.Value.SN,
                    SimplifyValue = addressModel.SimplifyValue,
                    Length = item.Value.Length,
                    Time = item.Value.Time,
                    Topic = addressModel.Topic,
                    Type = item.Value.AddressDataType,
                };
                if (TokenSource is null)
                    return;
                await UaSyncChannel.Writer.WriteAsync(item.Value, TokenSource.Token);
                await MqTransmissionAsync(new() { [newModel] = item.Value }, pluginConfigs);
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
