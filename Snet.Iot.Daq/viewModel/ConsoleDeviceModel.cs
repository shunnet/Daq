using CommunityToolkit.Mvvm.Input;
using Opc.Ua;
using Snet.Core.channel;
using Snet.Core.handler;
using Snet.Iot.Daq.data;
using Snet.Iot.Daq.handler;
using Snet.Iot.Daq.opc.ua.service;
using Snet.Model.data;
using Snet.Model.@enum;
using Snet.Utility;
using Snet.Windows.Core.mvvm;
using System.Collections.Concurrent;
using System.IO;

namespace Snet.Iot.Daq.viewModel
{
    public class ConsoleDeviceModel : BindNotify, IDisposable, IAsyncDisposable
    {
        public override string ToString()
        {
            return DaqData.Guid;
        }

        public void Dispose()
        {
            daqHandler.Dispose();
            daqHandler = null;
            foreach (var item in mqHandlers)
            {
                item.Value.Dispose();
            }
            mqHandlers.Clear();
            runtime.Stop();
            StopPolling();
            bytesHandler?.Dispose();
            bytesModels.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            await daqHandler.DisposeAsync();
            daqHandler = null;
            foreach (var item in mqHandlers)
            {
                await item.Value.DisposeAsync();
            }
            mqHandlers.Clear();
            runtime.Stop();
            StopPolling();
            if (bytesHandler != null)
            {
                await bytesHandler.DisposeAsync();
            }
            bytesModels.Clear();
        }

        /// <summary>
        /// 无参构造函数
        /// </summary>
        public ConsoleDeviceModel()
        {
            StartPolling(runtime);
            Snet.Core.handler.LanguageHandler.OnLanguageEventAsync += LanguageHandler_OnLanguageEventAsync;
        }

        #region 属性

        /// <summary>
        /// 字节处理
        /// </summary>
        private BytesHandler bytesHandler;

        /// <summary>
        /// 外部回调需要显示的信息
        /// </summary>
        private Func<string, Task> ShowAsync;

        /// <summary>
        /// 外部回调需要显示的结果信息
        /// </summary>
        private Func<PluginConfigModel, BaseModel, Task> ResultAsync;

        /// <summary>
        /// 采集处理
        /// </summary>
        private DqaHandler daqHandler;

        /// <summary>
        /// 传入处理
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
        /// 原始地址 -> OPCUA真实地址映射
        /// </summary>
        private readonly ConcurrentDictionary<string, string> _addressMap = new();

        /// <summary>
        /// 服务空间名称
        /// </summary>
        private string uaServerAddressSpaceName;

        /// <summary>
        /// opcua服务层级
        /// </summary>
        private FolderState folderState;

        /// <summary>
        /// 层级集合
        /// </summary>
        private List<FolderState> folderStates = new();

        /// <summary>
        /// DataType 与 BuiltInType 映射缓存
        /// </summary>
        private static readonly Dictionary<DataType, BuiltInType> _typeMap = new()
        {
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
        /// 通道操作
        /// </summary>
        private ChannelOperate<(string addressName, DataType dataType, object? value)> UaServerValueChannel;

        /// <summary>
        /// 采集数据
        /// </summary>
        private PluginConfigModel DaqData
        {
            get => GetProperty(() => DaqData);
            set => SetProperty(() => DaqData, value);
        }

        /// <summary>
        /// 地址数据
        /// </summary>
        private ConcurrentDictionary<AddressModel, List<PluginConfigModel>> AddressDatas
        {
            get => GetProperty(() => AddressDatas);
            set => SetProperty(() => AddressDatas, value);
        }

        /// <summary>
        /// 项目详情
        /// </summary>
        private ProjectTreeViewModel Project
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
        /// 设备层级
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
        /// 设备状态
        /// </summary>
        public bool DeviceStatus
        {
            get => GetProperty(() => DeviceStatus);
            set => SetProperty(() => DeviceStatus, value);
        }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime UpdateTime
        {
            get => GetProperty(() => UpdateTime);
            set => SetProperty(() => UpdateTime, value);
        }
        #endregion

        #region 事件
        /// <summary>
        /// 信息事件
        /// </summary>
        private async Task DqaHandler_OnInfoEventAsync(object? sender, EventInfoResult e)
        {
            //回写结果数据
            ResultAsync?.Invoke(DaqData, new ResultModel(e.Status, e.Message) { Time = e.Time });
        }

        /// <summary>
        /// 数据事件
        /// </summary>
        private async Task DqaHandler_OnDataEventAsync(object? sender, EventDataResult e)
        {
            if (!e.Status)
            {
                ResultAsync?.Invoke(DaqData, e);
                return;
            }

            var keys = e.GetSource<ConcurrentDictionary<string, AddressValue>>();
            if (keys == null || keys.Count == 0)
                return;

            //构建 Address 索引

            // Address(string) -> AddressModel
            var addressIndex = AddressDatas.Keys
                .Where(a => !string.IsNullOrEmpty(a.Address))
                .ToDictionary(a => a.Address!);

            //构建输入参数

            var inParam = new Dictionary<AddressModel, AddressValue>(keys.Count);

            foreach (var kv in keys)
            {
                if (addressIndex.TryGetValue(kv.Key, out var addressModel))
                {
                    //处理字节操作
                    if (addressModel.ExpandParam != null)
                    {
                        if (!File.Exists(addressModel.ExpandParam))
                        {
                            ShowAsync?.Invoke(DeviceHierarchyToolTip + ", " + $" {addressModel.Address} -" + "扩展参数文件不存在".GetLanguageValue(App.LanguageOperate));
                            continue;
                        }
                        //从文件中读取字节处理模型
                        if (!bytesModels.TryGetValue(addressModel.Address, out List<BytesModel>? bm))
                        {
                            bm = FileHandler.FileToString(addressModel.ExpandParam).ToJsonEntity<List<BytesModel>>();
                            bytesModels[addressModel.Address] = bm;
                        }
                        //实例化处理对象
                        if (bytesHandler == null)
                        {
                            bytesHandler = await BytesHandler.InstanceAsync(DeviceName);
                        }
                        //数据转换
                        OperateResult result = await bytesHandler.TransformAsync(kv.Value.ResultValue.GetSource<byte[]>(), kv.Value.Time, bm);
                        //获取结果
                        if (result.GetDetails(out ConcurrentDictionary<string, AddressValue>? res))
                        {
                            foreach (var item in res)
                            {
                                //转换结果
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
                                //赋值结果
                                inParam[newModel] = item.Value;
                                if (UaServerValueChannel != null)
                                {
                                    await UaServerValueChannel.WriteAsync((newModel.Address, newModel.Type, item.Value.ResultValue), CancellationToken.None);
                                }
                            }
                        }
                    }
                    else
                    {
                        inParam[addressModel] = kv.Value;
                        if (UaServerValueChannel != null)
                        {
                            await UaServerValueChannel.WriteAsync((addressModel.Address, addressModel.Type, kv.Value.ResultValue), CancellationToken.None);
                        }
                    }
                }
            }

            if (inParam.Count == 0)
                return;

            //预构建 Address → PluginConfigs 映射

            var pluginMap = AddressDatas
                .Where(kv => !string.IsNullOrEmpty(kv.Key.Address))
                .GroupBy(kv => kv.Key.Address!)
                .ToDictionary(
                    g => g.Key,
                    g => g.SelectMany(x => x.Value).ToList()
                );

            //处理 MQ
            foreach (var kv in keys)
            {
                if (!pluginMap.TryGetValue(kv.Key, out var pluginConfigs))
                    continue;

                foreach (var item in pluginConfigs)
                {
                    if (!mqHandlers.TryGetValue(item.Guid, out var mq))
                    {
                        mq = await MqHandler.InstanceAsync(item);
                        mqHandlers[item.Guid] = mq;
                    }

                    var result = await mq.ProduceAsync(item.Guid, inParam);
                    ResultAsync?.Invoke(item, result);
                }
            }
        }
        #endregion

        #region 命令

        /// <summary>
        /// webapi启动
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
                ShowAsync?.Invoke(DeviceHierarchyToolTip + ", " + "未设置WebApi参数".GetLanguageValue(App.LanguageOperate));
                return;
            }

            if ((await daqHandler.WAStatusAsync(DaqData.Guid)).GetDetails(out string? message))
            {
                ShowAsync?.Invoke(DeviceHierarchyToolTip + ", " + message);
                return;
            }

            OperateResult result = await daqHandler.WAOnAsync(DaqData.Guid, DaqData.WebApi);
            //回写结果数据
            ShowAsync?.Invoke(DeviceHierarchyToolTip + ", " + (result.Status ? "WebApi启动成功".GetLanguageValue(App.LanguageOperate) : "WebApi启动失败".GetLanguageValue(App.LanguageOperate) + "," + result.Message));
        }

        /// <summary>
        /// webapi停止
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
                ShowAsync?.Invoke(DeviceHierarchyToolTip + ", " + "未设置WebApi参数".GetLanguageValue(App.LanguageOperate));
                return;
            }

            if (!(await daqHandler.WAStatusAsync(DaqData.Guid)).GetDetails(out string? message))
            {
                ShowAsync?.Invoke(DeviceHierarchyToolTip + ", " + message);
                return;
            }

            OperateResult result = await daqHandler.WAOffAsync(DaqData.Guid);
            //回写结果数据
            ShowAsync?.Invoke(DeviceHierarchyToolTip + ", " + (result.Status ? "WebApi停止成功".GetLanguageValue(App.LanguageOperate) : "WebApi停止失败".GetLanguageValue(App.LanguageOperate) + "," + result.Message));
        }

        /// <summary>
        /// webapi示例请求
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
                ShowAsync?.Invoke(DeviceHierarchyToolTip + ", " + "未设置WebApi参数".GetLanguageValue(App.LanguageOperate));
                return;
            }
            OperateResult result = await daqHandler.WARequestExampleAsync(DaqData.Guid);
            //回写结果数据
            ShowAsync?.Invoke($"{DeviceHierarchyToolTip}\r\n" + result.ResultData.ToString());
        }

        /// <summary>
        /// 采集
        /// </summary>
        public IAsyncRelayCommand Collect => collect ??= new AsyncRelayCommand(CollectAsync);
        private IAsyncRelayCommand? collect;
        private async Task CollectAsync()
        {
            if (!DeviceStatusFlashing)
            {
                if (daqHandler == null)
                {
                    daqHandler = await DqaHandler.InstanceAsync(DaqData);
                    daqHandler.OnDataEventAsync -= DqaHandler_OnDataEventAsync;
                    daqHandler.OnDataEventAsync += DqaHandler_OnDataEventAsync;
                    daqHandler.OnInfoEventAsync -= DqaHandler_OnInfoEventAsync;
                    daqHandler.OnInfoEventAsync += DqaHandler_OnInfoEventAsync;
                }
                if (DaqData.WebApi != null)
                {
                    await WASatrtAsync();
                }
                OperateResult result = await daqHandler.SubscribeAsync(DaqData.Guid, AddressDatas.Keys.ToList());
                if (result.Status)
                {
                    CollectStatus = LanguageHandler.GetLanguageValue("启动", App.LanguageOperate);
                    DeviceStatusFlashing = true;
                    DeviceStatus = true;
                    runtime.Start();

                    if (UaServerValueChannel == null)
                    {
                        UaServerValueChannel = ChannelOperate<(string addressName, DataType dataType, object? value)>.Instance(new ChannelData { SN = $"{DeviceName}.{DeviceType}", IsSync = false });
                        UaServerValueChannel.OnDataEventAsync += UaServerValueChannel_OnDataEventAsync;
                    }

                    if (folderStates.Count > 0)
                    {
                        GlobalConfigModel.uaService.RemoveFolder(new List<NodeId>() { folderStates[0].NodeId });
                        folderStates[0].Dispose();
                        folderStates.Clear();
                        folderState.Dispose();
                        folderState = null;
                    }

                    _addressMap.Clear();

                    ShowAsync?.Invoke(DeviceHierarchyToolTip + ", " + "启动采集".GetLanguageValue(App.LanguageOperate));
                }
                else
                {
                    DeviceStatus = false;
                }
                //回写结果数据
                ResultAsync?.Invoke(DaqData, result);
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
            if (DaqData.WebApi != null)
            {
                await WAStopAsync();
            }
            OperateResult result = await daqHandler.UnSubscribeAsync(DaqData.Guid, AddressDatas.Keys.ToList());
            if (result.Status)
            {
                CollectStatus = LanguageHandler.GetLanguageValue("停止", App.LanguageOperate);
                DeviceStatusFlashing = false;
                DeviceStatus = true;
                runtime.Stop();

                if (UaServerValueChannel != null)
                {
                    await UaServerValueChannel.DisposeAsync();
                    UaServerValueChannel = null;
                }

                ShowAsync?.Invoke(DeviceHierarchyToolTip + ", " + "停止采集".GetLanguageValue(App.LanguageOperate));
            }

            //回写结果数据
            ResultAsync?.Invoke(DaqData, result);
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
            ShowAsync?.Invoke(DeviceHierarchyToolTip + ", " + "重试".GetLanguageValue(App.LanguageOperate));
        }

        /// <summary>
        /// 软启采集
        /// </summary>
        public IAsyncRelayCommand OnSoftCollect => onSoftCollect ??= new AsyncRelayCommand(OnSoftCollectAsync);
        private IAsyncRelayCommand? onSoftCollect;
        private async Task OnSoftCollectAsync()
        {
            Project.IsSoftStart = true;
            await Project.SetAsync(GlobalConfigModel.ProjectDict);
            ShowAsync?.Invoke(DeviceHierarchyToolTip + ", " + "添加软启采集成功".GetLanguageValue(App.LanguageOperate));
        }

        /// <summary>
        /// 取消软启采集
        /// </summary>
        public IAsyncRelayCommand OffSoftCollect => offSoftCollect ??= new AsyncRelayCommand(OffSoftCollectAsync);
        private IAsyncRelayCommand? offSoftCollect;
        private async Task OffSoftCollectAsync()
        {
            Project.IsSoftStart = false;
            await Project.SetAsync(GlobalConfigModel.ProjectDict);
            ShowAsync?.Invoke(DeviceHierarchyToolTip + ", " + "取消软启采集成功".GetLanguageValue(App.LanguageOperate));
        }
        #endregion

        #region 方法
        /// <summary>
        /// 通道数据事件触发
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <returns></returns>
        private async Task UaServerValueChannel_OnDataEventAsync(object? sender, EventDataResult e)
        {
            if (!e.Status)
                return;

            if (GlobalConfigModel.uaService != null && GlobalConfigModel.uaService.GetStatus().Status)
            {
                //层级
                if (folderState == null)
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
                            await ShowAsync?.Invoke(msg);
                        }
                    }
                    folderState = folder;
                }
            }
            else
            {
                return;
            }

            if (folderState == null)
            {
                return;
            }

            //数据源
            var result = e.GetSource<(string addressName, DataType dataType, object? value)>();
            string addressName = result.addressName;
            DataType dataType = result.dataType;
            object? value = result.value;

            //服务
            var service = GlobalConfigModel.uaService;
            if (service is null || !service.GetStatus().Status)
                return;

            //比对层级
            if (uaServerAddressSpaceName.IsNullOrWhiteSpace())
            {
                uaServerAddressSpaceName = GlobalConfigModel.uaService.GetBasicsData().GetSource<OpcUaServiceData.Basics>().AddressSpaceName;
            }

            if (!_addressMap.ContainsKey(addressName))
            {
                if (!_typeMap.TryGetValue(dataType, out var builtInType))
                    return;

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
                    await ShowAsync?.Invoke(createResult.Message);
                    return;
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
                return;

            var dict = new ConcurrentDictionary<string, WriteModel>()
            {
                [realAddress] = new WriteModel(value, dataType)
            };

            var writeResult = await service.WriteAsync(dict);

            if (!writeResult.Status && ShowAsync != null)
                await ShowAsync.Invoke(writeResult.Message);
        }

        private int AddressCount = 0;

        /// <summary>
        /// 设置
        /// </summary>
        /// <param name="model">项目信息</param>
        public async Task SettingsAsync(ProjectTreeViewModel model, Func<PluginConfigModel, BaseModel, Task> resultAsync, Func<string, Task> showAsync)
        {
            ResultAsync = resultAsync;
            ShowAsync = showAsync;
            Project = model;
            DeviceName = model.Name;
            DeviceType = model.DaqDetails.Name;
            UpdateTime = model.DaqDetails.Time;
            DeviceHierarchyToolTip = model.GetHierarchyPath();
            DeviceHierarchy = DeviceHierarchyToolTip.TruncateByBytes(36);
            AddressDatas = model.Details.ToAddressMqDictionary();
            AddressCount = AddressDatas.Count();
            DaqData = model.DaqDetails;

            if (model.IsSoftStart)
            {
                await CollectAsync();
            }
            if (DeviceStatusFlashing)
            {
                _ = RetryAsync().ConfigureAwait(false);
            }
        }



        /// <summary>
        /// 开始每秒获取运行时间
        /// </summary>
        public void StartPolling(RuntimeSecondsRecorderHandler recorder)
        {
            _cts = new CancellationTokenSource();

            Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    double seconds = recorder.TotalSeconds;
                    CollectTime = (int)seconds;
                    await Task.Delay(1000, _cts.Token);
                }
            }, _cts.Token);
        }
        private CancellationTokenSource _cts;
        /// <summary>
        /// 停止轮询
        /// </summary>
        public void StopPolling()
        {
            _cts?.Cancel();
        }


        #endregion

        #region 状态
        private async Task LanguageHandler_OnLanguageEventAsync(object? sender, EventLanguageResult e)
        {
            string text = CollectStatus;
            CollectStatus = LanguageHandler.GetLanguageValue(text, App.LanguageOperate);
        }

        #endregion

    }
}
