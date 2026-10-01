using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using ScottPlot.WPF;
using Snet.Core.handler;
using Snet.Iot.Daq.chart;
using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.handler;
using Snet.Iot.Daq.Core.@interface;
using Snet.Iot.Daq.Core.mvvm;
using Snet.Iot.Daq.Core.opc.ua.service;
using Snet.Iot.Daq.data;
using Snet.Iot.Daq.utility;
using Snet.Iot.Daq.view;
using Snet.Log;
using Snet.Model.data;
using Snet.Mqtt.service;
using Snet.Utility;
using Snet.Windows.Controls.handler;
using Snet.Windows.Controls.message;
using System.Collections.ObjectModel;
using System.IO;
using static Snet.Iot.Daq.utility.SystemMonitoring;

namespace Snet.Iot.Daq.viewModel
{
    /// <summary>
    /// 控制台视图模型，负责系统监控信息显示、OPC UA/MQTT 服务端管理、日志输出以及采集设备运行状态的综合管理。
    /// </summary>
    public class ConsoleModel : BindNotify, IDisposable, IAsyncDisposable
    {
        #region 构造函数
        /// <summary>
        /// 构造函数
        /// </summary>
        public ConsoleModel()
        {
            _initializationTask = InitializeObservedAsync();
        }
        #endregion

        /// <summary>初始化、系统采样和清理均由视图模型持有，应用退出时等待任务结束。</summary>
        private readonly Task _initializationTask;
        private Task? _monitorTask;
        private readonly SemaphoreSlim _refreshGate = new(1, 1);
        private readonly SemaphoreSlim _serverGate = new(1, 1);
        private readonly object _disposalGate = new();
        private Task? _disposalTask;
        private int _stopping;

        /// <summary>观察构造时启动的初始化；错误记录到日志，避免丢弃任务导致异常无人处理。</summary>
        private async Task InitializeObservedAsync()
        {
            try { await InitAsync(); }
            catch (OperationCanceledException) when (globalToken.IsCancellationRequested) { }
            catch (Exception ex) { LogHelper.Error($"控制台初始化失败: {ex.Message}"); }
        }

        #region 监控信息

        private static readonly System.Windows.Media.SolidColorBrush s_cpuBrush = CreateFrozenBrush("#4CAF50");
        private static readonly System.Windows.Media.SolidColorBrush s_gpuBrush = CreateFrozenBrush("#F44336");
        private static readonly System.Windows.Media.SolidColorBrush s_ramBrush = CreateFrozenBrush("#2196F3");

        private static System.Windows.Media.SolidColorBrush CreateFrozenBrush(string hex)
        {
            var brush = (System.Windows.Media.SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFromString(hex);
            brush.Freeze();
            return brush;
        }
        public double Cpu
        {
            get => GetProperty(() => Cpu);
            set => SetProperty(() => Cpu, value);
        }
        public System.Windows.Media.Brush Cpu_Foreground
        {
            get => cpu_Foreground;
            set => SetProperty(ref cpu_Foreground, value);
        }
        private System.Windows.Media.Brush cpu_Foreground = s_cpuBrush;

        public double Gpu
        {
            get => GetProperty(() => Gpu);
            set => SetProperty(() => Gpu, value);
        }
        public System.Windows.Media.Brush Gpu_Foreground
        {
            get => gpu_Foreground;
            set => SetProperty(ref gpu_Foreground, value);
        }
        private System.Windows.Media.Brush gpu_Foreground = s_gpuBrush;

        public double RAM
        {
            get => GetProperty(() => RAM);
            set => SetProperty(() => RAM, value);
        }
        public System.Windows.Media.Brush RAM_Foreground
        {
            get => ram_Foreground;
            set => SetProperty(ref ram_Foreground, value);
        }
        private System.Windows.Media.Brush ram_Foreground = s_ramBrush;

        /// <summary>
        /// 更新系统检测值
        /// </summary>
        private async Task UpdateSystemMonitoringValueAsync(CancellationToken token = default)
        {
            try
            {
                await Task.Run(async () =>
                {
                    // 在循环外分配字典，避免每次迭代产生 GC 压力
                    System.Collections.Concurrent.ConcurrentDictionary<string, double> values = new();

                    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
                    while (await timer.WaitForNextTickAsync(token))
                    {
                        values.Clear(); // 每次采样独立，缺失传感器不沿用上次数据。
                        HardwareData hardwareData = systemMonitoring.GetInfo();

                        foreach (var iteminfolist in hardwareData.Info)
                        {
                            if (iteminfolist.Key.Equals("内存"))
                            {
                                foreach (var item in iteminfolist.Values)
                                {
                                    if (double.TryParse(item.Value, System.Globalization.CultureInfo.InvariantCulture, out double value)
                                        && item.Key.Equals("负载,Memory") && double.IsFinite(value))
                                    {
                                        values["RAM"] = value;
                                    }
                                }
                            }
                            if (iteminfolist.Key.Equals("英伟达显卡") || iteminfolist.Key.Equals("英特尔显卡") || iteminfolist.Key.Equals("AMD显卡"))
                            {
                                foreach (var item in iteminfolist.Values)
                                {
                                    if (double.TryParse(item.Value, System.Globalization.CultureInfo.InvariantCulture, out double value)
                                        && item.Key.Equals("负载,GPU Core") && double.IsFinite(value))
                                    {
                                        values["Gpu"] = value;
                                    }
                                }
                            }
                            if (iteminfolist.Key.Equals("处理器"))
                            {
                                foreach (var item in iteminfolist.Values)
                                {
                                    if (double.TryParse(item.Value, System.Globalization.CultureInfo.InvariantCulture, out double value)
                                        && item.Key.Equals("负载,CPU Total") && double.IsFinite(value))
                                    {
                                        values["Cpu"] = value;
                                    }
                                }
                            }
                        }
                        if (!values.IsEmpty)
                        {
                            foreach (var item in values)
                            {
                                double value = Math.Round(item.Value, 2);
                                UpdateLineSeriesData(item.Key, value);

                                switch (item.Key)
                                {
                                    case "Cpu":
                                        Cpu = value;
                                        break;
                                    case "Gpu":
                                        Gpu = value;
                                        break;
                                    case "RAM":
                                        RAM = value;
                                        break;
                                }
                            }
                        }
                    }
                }, token).ConfigureAwait(false);
            }
            catch (TaskCanceledException) { }
            catch (OperationCanceledException) { }
            catch (Exception ex) { await uiMessage.ShowAsync(ex.Message); }
        }

        /// <summary>
        /// 更新指定名称的图表线条数据，将最新数值推送到图表组件进行实时绘制。
        /// </summary>
        /// <param name="name">线条名称（如 "Cpu"、"Gpu"、"RAM"）</param>
        /// <param name="value">最新数值</param>
        private void UpdateLineSeriesData(string name, double value)
        {
            chartOperate.Update(name, value);
        }
        #endregion

        #region 属性
        /// <summary>
        /// ui信息处理器
        /// </summary>
        private UiMessageHandler uiMessage = UiMessageHandler.Instance("Info");

        /// <summary>
        /// 图表操作
        /// </summary>
        private ChartOperate chartOperate;

        /// <summary>
        /// 系统信息监控
        /// </summary>
        private SystemMonitoring systemMonitoring;

        /// <summary>
        /// 全局的任务取消控制
        /// </summary>
        private CancellationTokenSource globalToken = new CancellationTokenSource();

        /// <summary>
        /// 控件
        /// </summary>
        public WpfPlot ChartControl
        {
            get => chartControl;
            set => SetProperty(ref chartControl, value);
        }
        private WpfPlot chartControl = new WpfPlot();

        /// <summary>
        /// 信息事件
        /// </summary>
        public string Info
        {
            get => GetProperty(() => Info);
            set => SetProperty(() => Info, value);
        }

        /// <summary>
        /// 设备集合
        /// </summary>
        public ObservableCollection<ConsoleDevice> Devices
        {
            get => _Devices;
            set => SetProperty(ref _Devices, value);
        }
        private ObservableCollection<ConsoleDevice> _Devices = new ObservableCollection<ConsoleDevice>();
        #endregion

        #region 命令与方法
        /// <summary>
        /// 数据清空
        /// </summary>
        public IAsyncRelayCommand Clear => p_Clear ??= new AsyncRelayCommand(ClearAsync);
        IAsyncRelayCommand p_Clear;
        /// <summary>
        /// 清空消息
        /// </summary>
        /// <returns></returns>
        public async Task ClearAsync()
        {
            await uiMessage.ClearAsync();
        }

        /// <summary>
        /// Mqtt服务数据修改
        /// </summary>
        public IAsyncRelayCommand MqttServerUpdate => p_MqttServerUpdate ??= new AsyncRelayCommand(MqttServerUpdateAsync);
        IAsyncRelayCommand p_MqttServerUpdate;
        public async Task MqttServerUpdateAsync()
        {
            if (File.Exists(GlobalConfigModel.MqttServerConfigPath))
            {
                MqttServiceData.Basics? basics = FileHandler.FileToString(GlobalConfigModel.MqttServerConfigPath).ToJsonEntity<MqttServiceData.Basics>();
                GlobalConfigModel.param.SetBasics(basics);
                if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
                {
                    basics = GlobalConfigModel.param.GetBasics().GetSource<MqttServiceData.Basics>();
                    //写入配置
                    FileHandler.StringToFile(GlobalConfigModel.MqttServerConfigPath, basics.ToJson(true));
                }
            }
        }

        /// <summary>
        /// 启动Mqtt服务
        /// </summary>
        public IAsyncRelayCommand MqttServerStart => p_MqttServerStart ??= new AsyncRelayCommand(MqttServerStartAsync);
        IAsyncRelayCommand p_MqttServerStart;
        public async Task MqttServerStartAsync()
        {
            await _serverGate.WaitAsync();
            try
            {
                if (Volatile.Read(ref _stopping) != 0) return;
                await MqttServerStartCoreAsync();
            }
            finally { _serverGate.Release(); }
        }

        /// <summary>在服务端生命周期门内执行实际操作；调用方拥有该门。</summary>
        private async Task MqttServerStartCoreAsync()
        {
            if (GlobalConfigModel.mqttService is null)
            {
                GlobalConfigModel.param.SetBasics(new MqttServiceData.Basics());
                if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
                {
                    MqttServiceData.Basics basics = GlobalConfigModel.param.GetBasics().GetSource<MqttServiceData.Basics>();
                    GlobalConfigModel.mqttService = await MqttServiceOperate.InstanceAsync(basics);
                    //创建本地配置
                    if (!Directory.Exists(GlobalConfigModel.ServerConfigPath))
                    {
                        Directory.CreateDirectory(GlobalConfigModel.ServerConfigPath);
                    }
                    FileHandler.StringToFile(GlobalConfigModel.MqttServerConfigPath, basics.ToJson(true));

                    await MqttServerInitCoreAsync();
                    globalToken.Token.ThrowIfCancellationRequested();
                    await RefreshAsync();
                }
            }
            else
            {
                await MessageBox.Show("已启动".GetLanguageValue(App.LanguageOperate), "Mqtt");
            }
        }

        /// <summary>
        /// Mqtt服务端初始化
        /// </summary>
        /// <returns></returns>
        private async Task MqttServerInitAsync()
        {
            await _serverGate.WaitAsync();
            try
            {
                if (Volatile.Read(ref _stopping) != 0) return;
                await MqttServerInitCoreAsync();
            }
            finally { _serverGate.Release(); }
        }

        /// <summary>在服务端生命周期门内执行实际操作；调用方拥有该门。</summary>
        private async Task MqttServerInitCoreAsync()
        {
            if (File.Exists(GlobalConfigModel.MqttServerConfigPath))
            {
                //实例化参数
                MqttServiceData.Basics? basics = FileHandler.FileToString(GlobalConfigModel.MqttServerConfigPath).ToJsonEntity<MqttServiceData.Basics>();
                //实例化
                GlobalConfigModel.mqttService = MqttServiceOperate.Instance(basics ??= new());
            }

            if (GlobalConfigModel.mqttService is not null)
            {
                GlobalConfigModel.mqttService.OnInfoEventAsync += MqttService_OnInfoEventAsync;
                OperateResult result = await GlobalConfigModel.mqttService.OnAsync();
                await ShowAsync(result.ToJson(true));
                if (!result.Status)
                {
                    GlobalConfigModel.mqttService.OnInfoEventAsync -= MqttService_OnInfoEventAsync;
                    await GlobalConfigModel.mqttService.DisposeAsync();
                    GlobalConfigModel.mqttService = null;
                }
            }
        }

        private async Task MqttService_OnInfoEventAsync(object? sender, EventInfoResult e)
        {
            await ShowAsync($"[ MqttService ] {e.Status.ToString().ToUpperInvariant()}\r\n{e.ToJson(true)}");
        }

        /// <summary>
        /// 停止Mqtt服务
        /// </summary>
        public IAsyncRelayCommand MqttServerStop => p_MqttServerStop ??= new AsyncRelayCommand(MqttServerStopAsync);
        IAsyncRelayCommand p_MqttServerStop;
        public async Task MqttServerStopAsync()
        {
            await _serverGate.WaitAsync();
            try
            {
                if (Volatile.Read(ref _stopping) != 0) return;
                await MqttServerStopCoreAsync();
            }
            finally { _serverGate.Release(); }
        }

        /// <summary>在服务端生命周期门内执行实际操作；调用方拥有该门。</summary>
        private async Task MqttServerStopCoreAsync()
        {
            if (GlobalConfigModel.mqttService is not null)
            {
                var service = GlobalConfigModel.mqttService;
                GlobalConfigModel.mqttService = null;
                service.OnInfoEventAsync -= MqttService_OnInfoEventAsync;
                try
                {
                    OperateResult result = await service.OffAsync();
                    await ShowAsync(result.ToJson(true));
                }
                finally { await service.DisposeAsync(); }
                await RefreshAsync();
            }
            else
            {
                await MessageBox.Show("未启动".GetLanguageValue(App.LanguageOperate), "Mqtt");
            }
        }

        /// <summary>
        /// OPCUA服务数据修改
        /// </summary>
        public IAsyncRelayCommand OpcUaServerUpdate => p_OpcUaServerUpdate ??= new AsyncRelayCommand(OpcUaServerUpdateAsync);
        IAsyncRelayCommand p_OpcUaServerUpdate;
        public async Task OpcUaServerUpdateAsync()
        {
            if (File.Exists(GlobalConfigModel.UaServerConfigPath))
            {
                OpcUaServiceData.Basics? basics = FileHandler.FileToString(GlobalConfigModel.UaServerConfigPath).ToJsonEntity<OpcUaServiceData.Basics>();
                GlobalConfigModel.param.SetBasics(basics);
                if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
                {
                    basics = GlobalConfigModel.param.GetBasics().GetSource<OpcUaServiceData.Basics>();
                    //写入配置
                    FileHandler.StringToFile(GlobalConfigModel.UaServerConfigPath, basics.ToJson(true));
                }
            }
        }

        /// <summary>
        /// 启动OPCUA服务
        /// </summary>
        public IAsyncRelayCommand OpcUaServerStart => p_OpcUaServerStart ??= new AsyncRelayCommand(OpcUaServerStartAsync);
        IAsyncRelayCommand p_OpcUaServerStart;
        public async Task OpcUaServerStartAsync()
        {
            await _serverGate.WaitAsync();
            try
            {
                if (Volatile.Read(ref _stopping) != 0) return;
                await OpcUaServerStartCoreAsync();
            }
            finally { _serverGate.Release(); }
        }

        /// <summary>在服务端生命周期门内执行实际操作；调用方拥有该门。</summary>
        private async Task OpcUaServerStartCoreAsync()
        {
            if (GlobalConfigModel.uaService is null)
            {
                GlobalConfigModel.param.SetBasics(new OpcUaServiceData.Basics());
                if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
                {
                    OpcUaServiceData.Basics basics = GlobalConfigModel.param.GetBasics().GetSource<OpcUaServiceData.Basics>();
                    GlobalConfigModel.uaService = await OpcUaServiceOperate.InstanceAsync(basics);
                    //创建本地配置
                    if (!Directory.Exists(GlobalConfigModel.ServerConfigPath))
                    {
                        Directory.CreateDirectory(GlobalConfigModel.ServerConfigPath);
                    }
                    FileHandler.StringToFile(GlobalConfigModel.UaServerConfigPath, basics.ToJson(true));

                    await OpcUaServerInitCoreAsync();
                    globalToken.Token.ThrowIfCancellationRequested();
                    await RefreshAsync();
                }
            }
            else
            {
                await MessageBox.Show("已启动".GetLanguageValue(App.LanguageOperate), "OpcUa");
            }
        }

        /// <summary>
        /// OPCUA服务端初始化
        /// </summary>
        /// <returns></returns>
        private async Task OpcUaServerInitAsync()
        {
            await _serverGate.WaitAsync();
            try
            {
                if (Volatile.Read(ref _stopping) != 0) return;
                await OpcUaServerInitCoreAsync();
            }
            finally { _serverGate.Release(); }
        }

        /// <summary>在服务端生命周期门内执行实际操作；调用方拥有该门。</summary>
        private async Task OpcUaServerInitCoreAsync()
        {
            if (File.Exists(GlobalConfigModel.UaServerConfigPath))
            {
                //实例化参数
                OpcUaServiceData.Basics? basics = FileHandler.FileToString(GlobalConfigModel.UaServerConfigPath).ToJsonEntity<OpcUaServiceData.Basics>();
                //实例化
                GlobalConfigModel.uaService = OpcUaServiceOperate.Instance(basics ??= new());
            }

            if (GlobalConfigModel.uaService is not null)
            {
                GlobalConfigModel.uaService.OnInfoEventAsync += UaService_OnInfoEventAsync;
                OperateResult result = await GlobalConfigModel.uaService.OnAsync();
                await ShowAsync(result.ToJson(true));
                if (!result.Status)
                {
                    GlobalConfigModel.uaService.OnInfoEventAsync -= UaService_OnInfoEventAsync;
                    await GlobalConfigModel.uaService.DisposeAsync();
                    GlobalConfigModel.uaService = null;
                }
            }
        }

        private async Task UaService_OnInfoEventAsync(object? sender, EventInfoResult e)
        {
            await ShowAsync($"[ OpcUaService ] {e.Status.ToString().ToUpperInvariant()}\r\n{e.ToJson(true)}");
        }

        /// <summary>
        /// 停止OPCUA服务
        /// </summary>
        public IAsyncRelayCommand OpcUaServerStop => p_OpcUaServerStop ??= new AsyncRelayCommand(OpcUaServerStopAsync);
        IAsyncRelayCommand p_OpcUaServerStop;
        public async Task OpcUaServerStopAsync()
        {
            await _serverGate.WaitAsync();
            try
            {
                if (Volatile.Read(ref _stopping) != 0) return;
                await OpcUaServerStopCoreAsync();
            }
            finally { _serverGate.Release(); }
        }

        /// <summary>在服务端生命周期门内执行实际操作；调用方拥有该门。</summary>
        private async Task OpcUaServerStopCoreAsync()
        {
            if (GlobalConfigModel.uaService is not null)
            {
                var service = GlobalConfigModel.uaService;
                GlobalConfigModel.uaService = null;
                service.OnInfoEventAsync -= UaService_OnInfoEventAsync;
                try
                {
                    OperateResult result = await service.OffAsync();
                    await ShowAsync(result.ToJson(true));
                }
                finally { await service.DisposeAsync(); }
                await RefreshAsync();
            }
            else
            {
                await MessageBox.Show("未启动".GetLanguageValue(App.LanguageOperate), "OpcUa");
            }
        }

        /// <summary>
        /// 刷新
        /// </summary>
        public IAsyncRelayCommand Refresh => refresh ??= new AsyncRelayCommand(GlobalConfigModel.RefreshAsyncFunc ??= RefreshAsync);
        private IAsyncRelayCommand refresh;
        /// <summary>串行同步设备集合；等待前后检查退出状态，避免多个刷新创建同一设备或在关闭后启动设备。</summary>
        public async Task RefreshAsync()
        {
            if (Volatile.Read(ref _stopping) != 0) return;
            await _refreshGate.WaitAsync();
            try
            {
                if (Volatile.Read(ref _stopping) != 0) return;
                List<IProjectTreeViewModel> devices = GlobalConfigModel.ProjectDict.GetAllDeviceNodes();
                await ShowAsync(devices.Count + " " + "台设备已成功加载".GetLanguageValue(App.LanguageOperate));
                await SyncDevicesAsync(devices, Devices, ResultAsync, ShowAsync);
            }
            finally { _refreshGate.Release(); }
        }

        /// <summary>
        /// 同步设备集合（正向创建 / 更新 + 反向移除）
        /// </summary>
        /// <param name="sourceDevices">项目树中的设备节点</param>
        /// <param name="uiDevices">UI 显示的设备集合</param>
        /// <param name="resultAsync">结果回调</param>
        /// <param name="showAsync">提示回调</param>
        private async Task SyncDevicesAsync(
            List<IProjectTreeViewModel> sourceDevices,
            ObservableCollection<ConsoleDevice> uiDevices,
            Func<PluginConfigModel, BaseModel, Task> resultAsync,
            Func<string, Task> showAsync)
        {
            if (sourceDevices == null || uiDevices == null)
                return;

            //构建 guid → device 索引，避免 O(n2) 查找
            var deviceMap = new System.Collections.Concurrent.ConcurrentDictionary<string, ConsoleDevice>();
            foreach (var d in uiDevices)
                deviceMap[d.DataContext.GetSource<ConsoleDeviceModel>().ToString()] = d;

            //正向同步：创建 / 更新
            foreach (var item in sourceDevices)
            {
                string guid = item.DaqDetails.Guid;

                if (!deviceMap.TryGetValue(guid, out var existedDevice))
                {
                    // 新建设备
                    ConsoleDevice device = new ConsoleDevice();
                    ConsoleDeviceModel model = device.DataContext.GetSource<ConsoleDeviceModel>();

                    await model.SettingsAsync(item, resultAsync, showAsync);
                    uiDevices.Add(device);
                }
                else
                {
                    // 更新已有设备
                    await existedDevice.DataContext.GetSource<ConsoleDeviceModel>().SettingsAsync(item, resultAsync, showAsync);
                }
            }
            //反向同步：移除不存在的设备
            var validGuidSet = sourceDevices
                .Select(d => d.DaqDetails.Guid)
                .ToHashSet();

            var removeList = uiDevices
                .Where(d =>
                {
                    var model = d.DataContext.GetSource<ConsoleDeviceModel>();
                    return !validGuidSet.Contains(model.ToString());
                })
                .ToList();

            foreach (var device in removeList)
            {
                await device.DataContext.GetSource<ConsoleDeviceModel>().DisposeAsync();
                uiDevices.Remove(device);
            }

            // 同步托盘设备状态集合，供系统托盘右键菜单使用
            GlobalConfigModel.TrayDevices.Clear();
            foreach (var device in uiDevices)
            {
                GlobalConfigModel.TrayDevices.Add(device.DataContext.GetSource<ConsoleDeviceModel>());
            }
        }


        /// <summary>
        /// 失败消息节流缓存（相同设备+消息在 1 秒窗口内只显示一次）<br/>
        /// 防止 MQ/采集持续失败时消息洪峰刷屏界面并持续写日志
        /// </summary>
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _errorThrottle = new();

        /// <summary>
        /// 设备结果信息
        /// </summary>
        /// <param name="result">结果</param>
        private async Task ResultAsync(PluginConfigModel model, BaseModel result)
        {
            if (!result.Status)
            {
                string msg = $"[ Error ] {result.Time} : [ {model.Type} ] {result.Message}";
                string key = $"{model.Guid}|{result.Message}";
                long now = DateTime.UtcNow.Ticks;
                if (_errorThrottle.TryGetValue(key, out long last))
                {
                    if (now - last < TimeSpan.TicksPerSecond)
                        return;
                }
                _errorThrottle[key] = now;
                if (_errorThrottle.Count > 1000)
                    _errorThrottle.Clear();  // 防止失败消息种类过多时字典本身无限增长

                await uiMessage.ShowAsync(msg, withTime: false);
                LogHelper.Error(msg, foldername: "msg");
            }
        }

        /// <summary>
        /// 界面显示信息
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        private async Task ShowAsync(string message)
        {
            string msg = $"[ Info ] {DateTime.Now} : {message}";
            await uiMessage.ShowAsync(msg, withTime: false);
            LogHelper.Info(msg, foldername: "msg");
        }


        /// <summary>
        /// 初始化
        /// </summary>
        /// <returns></returns>
        private async Task InitAsync()
        {
            // 界面消息处理
            uiMessage.OnInfoEventAsync += UiMessage_OnInfoEventAsync;
            await uiMessage.StartAsync();
            globalToken.Token.ThrowIfCancellationRequested();

            // 图表操作
            chartOperate = ChartOperate.Instance(new()
            {
                ChartControl = ChartControl,
                LineAdjust = true,
                HideGrid = true
            });
            chartOperate.On();
            chartOperate.Create(new() { SN = "Cpu", Title = "处理器", TitleEN = "Cpu", Color = "#4CAF50" });
            chartOperate.Create(new() { SN = "Gpu", Title = "显卡", TitleEN = "Gpu", Color = "#F44336" });
            chartOperate.Create(new() { SN = "RAM", Title = "内存", TitleEN = "RAM", Color = "#2196F3" });

            // 系统监控
            systemMonitoring = SystemMonitoring.Instance();

            // 更新系统检测值
            _monitorTask = UpdateSystemMonitoringValueAsync(globalToken.Token);

            //OPCUA服务端启动
            await OpcUaServerInitAsync();
            globalToken.Token.ThrowIfCancellationRequested();

            //Mqtt服务端启动
            await MqttServerInitAsync();
            globalToken.Token.ThrowIfCancellationRequested();

            //赋值插件信息
            GlobalConfigModel.RefreshAsyncFunc = RefreshAsync;

            //刷新
            await RefreshAsync();
        }
        #endregion
        /// <summary>把信息处理器事件转换为绑定文本；命名委托用于退出时准确退订。</summary>
        private Task UiMessage_OnInfoEventAsync(object? sender, EventInfoResult e)
        {
            Info = e.Message;
            return Task.CompletedTask;
        }

        /// <summary>同步容器兼容入口只启动并观察清理；正常应用退出必须先等待 DisposeAsync。</summary>
        public void Dispose()
        {
            _ = DisposeAsync().AsTask().ContinueWith(task => LogHelper.Error($"控制台退出清理失败: {task.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        /// <summary>进入终态并共同等待初始化、采样、设备及服务端的释放；UI 调用须异步等待，以允许已排队绘图完成。</summary>
        public ValueTask DisposeAsync()
        {
            lock (_disposalGate)
            {
                Volatile.Write(ref _stopping, 1);
                return new ValueTask(_disposalTask ??= DisposeCoreAsync());
            }
        }

        /// <summary>取消后台采样后逐项清理；一个插件异常不阻止其余设备或服务端退出。</summary>
        private async Task DisposeCoreAsync()
        {
            await Task.Yield();
            async Task ReleaseAsync(Func<Task> action)
            {
                try { await action(); }
                catch (Exception ex) { LogHelper.Error($"控制台资源清理失败: {ex.Message}"); }
            }
            globalToken.Cancel();
            await _initializationTask;
            if (_monitorTask is not null) await ReleaseAsync(() => _monitorTask);
            await _refreshGate.WaitAsync();
            try
            {
                foreach (var device in Devices.ToArray())
                    await ReleaseAsync(() => device.DataContext.GetSource<ConsoleDeviceModel>().DisposeAsync().AsTask());
                Devices.Clear();
                GlobalConfigModel.TrayDevices.Clear();
                if (GlobalConfigModel.RefreshAsyncFunc == RefreshAsync) GlobalConfigModel.RefreshAsyncFunc = null;
            }
            finally { _refreshGate.Release(); }
            await _serverGate.WaitAsync();
            try
            {
                var mqtt = GlobalConfigModel.mqttService;
                var ua = GlobalConfigModel.uaService;
                GlobalConfigModel.mqttService = null;
                GlobalConfigModel.uaService = null;
                if (mqtt is not null)
                {
                    mqtt.OnInfoEventAsync -= MqttService_OnInfoEventAsync;
                    await ReleaseAsync(async () => { try { await mqtt.OffAsync(); } finally { await mqtt.DisposeAsync(); } });
                }
                if (ua is not null)
                {
                    ua.OnInfoEventAsync -= UaService_OnInfoEventAsync;
                    await ReleaseAsync(async () => { try { await ua.OffAsync(); } finally { await ua.DisposeAsync(); } });
                }
            }
            finally { _serverGate.Release(); }
            if (chartOperate is not null) await ReleaseAsync(() => chartOperate.DisposeAsync().AsTask());
            systemMonitoring?.Dispose();
            uiMessage.OnInfoEventAsync -= UiMessage_OnInfoEventAsync;
            await ReleaseAsync(() => uiMessage.DisposeAsync().AsTask());
            globalToken.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
