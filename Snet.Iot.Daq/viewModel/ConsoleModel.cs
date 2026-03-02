using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using ScottPlot.WPF;
using Snet.Core.handler;
using Snet.Iot.Daq.chart;
using Snet.Iot.Daq.data;
using Snet.Iot.Daq.handler;
using Snet.Iot.Daq.opc.ua.service;
using Snet.Iot.Daq.utility;
using Snet.Iot.Daq.view;
using Snet.Model.data;
using Snet.Utility;
using Snet.Windows.Controls.handler;
using Snet.Windows.Controls.message;
using Snet.Windows.Core.mvvm;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using static Snet.Iot.Daq.utility.SystemMonitoring;

namespace Snet.Iot.Daq.viewModel
{
    public class ConsoleModel : BindNotify
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        public ConsoleModel()
        {
            // 界面消息处理
            uiMessage.OnInfoEventAsync += async (object? sender, Model.data.EventInfoResult e) => Info = e.Message;
            uiMessage.StartAsync().ConfigureAwait(false);

            // 图表操作
            chartOperate = ChartOperate.Instance(new()
            {
                ChartControl = ChartControl,
                LineAdjust = true,
                HideGrid = true,
                RefreshTime = _interval
            });
            chartOperate.On();
            chartOperate.Create(new() { SN = "Cpu", Title = "处理器", TitleEN = "Cpu", Color = "#4CAF50" });
            chartOperate.Create(new() { SN = "Gpu", Title = "显卡", TitleEN = "Gpu", Color = "#F44336" });
            chartOperate.Create(new() { SN = "RAM", Title = "内存", TitleEN = "RAM", Color = "#2196F3" });

            // 系统监控
            systemMonitoring = SystemMonitoring.Instance();

            // 更新系统检测值
            _ = UpdateSystemMonitoringValueAsync(globalToken.Token).ConfigureAwait(false);

            //OPCUA服务端启动
            _ = OpcUaServerInitAsync().ConfigureAwait(false);

            //赋值插件信息
            GlobalConfigModel.RefreshAsyncFunc = RefreshAsync;

            //刷新
            _ = RefreshAsync().ConfigureAwait(false);

        }

        #region 监控信息
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
        private System.Windows.Media.Brush cpu_Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#4CAF50");

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
        private System.Windows.Media.Brush gpu_Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#F44336");

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
        private System.Windows.Media.Brush ram_Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#2196F3");

        /// <summary>
        /// 更新系统检测值
        /// </summary>
        private async Task UpdateSystemMonitoringValueAsync(CancellationToken token = default)
        {
            try
            {
                await Task.Run(async () =>
                {
                    //让刻度同步
                    ConcurrentDictionary<string, double> values = new ConcurrentDictionary<string, double>();

                    while (!token.IsCancellationRequested)
                    {

                        HardwareData hardwareData = systemMonitoring.GetInfo();

                        foreach (var iteminfolist in hardwareData.Info)
                        {
                            if (iteminfolist.Key.Equals("内存"))
                            {
                                foreach (var item in iteminfolist.Values)
                                {
                                    double value = double.Parse(item.Value);
                                    if (item.Key.Equals("负载,Memory") && value > 0)
                                    {
                                        values["RAM"] = value;
                                    }
                                }
                            }
                            if (iteminfolist.Key.Equals("英伟达显卡") || iteminfolist.Key.Equals("因特尔显卡") || iteminfolist.Key.Equals("AMD显卡"))
                            {
                                foreach (var item in iteminfolist.Values)
                                {
                                    double value = double.Parse(item.Value);
                                    if (item.Key.Equals("负载,GPU Core") && value > 0)
                                    {
                                        values["Gpu"] = value;
                                    }

                                }
                            }
                            if (iteminfolist.Key.Equals("处理器"))
                            {
                                foreach (var item in iteminfolist.Values)
                                {
                                    double value = double.Parse(item.Value);
                                    if (item.Key.Equals("负载,CPU Total") && value > 0)
                                    {
                                        values["Cpu"] = value;
                                    }

                                }
                            }
                        }
                        if (values.Count == 3)
                        {
                            foreach (var item in values)
                            {
                                double value = Math.Round(item.Value, 2);
                                UpdateLineSeriesData(item.Key, value);

                                switch (item.Key)
                                {
                                    case "Cpu":
                                        Cpu = Math.Round(value, 2, MidpointRounding.AwayFromZero);
                                        break;
                                    case "Gpu":
                                        Gpu = Math.Round(value, 2, MidpointRounding.AwayFromZero);
                                        break;
                                    case "RAM":
                                        RAM = Math.Round(value, 2, MidpointRounding.AwayFromZero);
                                        break;
                                }
                            }
                        }
                        await Task.Delay(_interval, token);
                    }
                }, token).ConfigureAwait(false);
            }
            catch (TaskCanceledException ex) { await uiMessage.ShowAsync(ex.Message); }
            catch (OperationCanceledException ex) { await uiMessage.ShowAsync(ex.Message); }
            catch (Exception ex) { await uiMessage.ShowAsync(ex.Message); }
        }

        /// <summary>
        /// 更新线条数据
        /// </summary>
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
        /// 间隔
        /// </summary>
        private int _interval = 1000;

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
                    File.WriteAllText(GlobalConfigModel.UaServerConfigPath, basics.ToJson(true));
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
                    File.WriteAllText(GlobalConfigModel.UaServerConfigPath, basics.ToJson(true));

                    await OpcUaServerInitAsync();
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
            await ShowAsync(e.ToJson(true));
        }

        /// <summary>
        /// 停止OPCUA服务
        /// </summary>
        public IAsyncRelayCommand OpcUaServerStop => p_OpcUaServerStop ??= new AsyncRelayCommand(OpcUaServerStopAsync);
        IAsyncRelayCommand p_OpcUaServerStop;
        public async Task OpcUaServerStopAsync()
        {
            if (GlobalConfigModel.uaService is not null)
            {
                OperateResult result = await GlobalConfigModel.uaService.OffAsync();
                await ShowAsync(result.ToJson(true));
                GlobalConfigModel.uaService.OnInfoEventAsync -= UaService_OnInfoEventAsync;
                await GlobalConfigModel.uaService.DisposeAsync();
                GlobalConfigModel.uaService = null;
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
        public async Task RefreshAsync()
        {
            List<ProjectTreeViewModel> devices = GlobalConfigModel.ProjectDict.GetAllDeviceNodes();
            await ShowAsync(devices.Count + " " + "台设备已成功加载".GetLanguageValue(App.LanguageOperate));
            await SyncDevicesAsync(devices, Devices, ResultAsync, ShowAsync);
        }

        /// <summary>
        /// 同步设备集合（正向创建 / 更新 + 反向移除）
        /// </summary>
        /// <param name="sourceDevices">项目树中的设备节点</param>
        /// <param name="uiDevices">UI 显示的设备集合</param>
        /// <param name="resultAsync">结果回调</param>
        /// <param name="showAsync">提示回调</param>
        private async Task SyncDevicesAsync(
            List<ProjectTreeViewModel> sourceDevices,
            ObservableCollection<ConsoleDevice> uiDevices,
            Func<PluginConfigModel, BaseModel, Task> resultAsync,
            Func<string, Task> showAsync)
        {
            if (sourceDevices == null || uiDevices == null)
                return;
            //正向同步：创建 / 更新
            foreach (var item in sourceDevices)
            {
                string guid = item.DaqDetails.Guid;

                var existedDevice = uiDevices.FirstOrDefault(d => d.DataContext.GetSource<ConsoleDeviceModel>().ToString() == guid);

                if (existedDevice == null)
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
        }


        /// <summary>
        /// 设备结果信息
        /// </summary>
        /// <param name="result">结果</param>
        private async Task ResultAsync(PluginConfigModel model, BaseModel result)
        {
            if (!result.Status)
            {
                string msg = $"[ Error ] {result.Time} : [ {model.Type} ] : {result.Message}";
                await uiMessage.ShowAsync(msg, withTime: false);
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
        }
        #endregion
    }
}
