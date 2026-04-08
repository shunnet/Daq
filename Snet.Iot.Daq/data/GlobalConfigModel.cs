using Snet.Core.handler;
using Snet.Iot.Daq.mqtt.service;
using Snet.Iot.Daq.opc.ua.service;
using Snet.Iot.Daq.view;
using Snet.Iot.Daq.viewModel;
using Snet.Utility;
using Snet.Windows.Controls.handler;
using Snet.Windows.Controls.property;
using Snet.Windows.Core.handler;
using SQLite;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;

namespace Snet.Iot.Daq.data
{
    /// <summary>
    /// 全局配置模型<br/>
    /// 存储应用程序全局共享的配置、缓存、数据库连接、服务实例等资源
    /// </summary>
    public static class GlobalConfigModel
    {
        /// <summary>
        /// 静态构造函数：初始化 SQLite 数据库连接<br/>
        /// 自动创建数据库目录（若不存在）
        /// </summary>
        static GlobalConfigModel()
        {
            string directory = Path.GetDirectoryName(dbPath)!;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            sqliteOperate = new SQLiteConnection(dbPath);
        }

        /// <summary>
        /// 地址数据
        /// </summary>
        public static readonly ConcurrentDictionary<string, AddressModel> AddressDict = new();

        /// <summary>
        /// 设备 / 插件数据
        /// </summary>
        public static readonly ConcurrentDictionary<string, PluginConfigModel> PluginDict = new();

        /// <summary>
        /// 项目数据
        /// </summary>
        public static ObservableCollection<ProjectTreeViewModel> ProjectDict { get; set; }

        /// <summary>
        /// 数据库路径
        /// </summary>
        public static readonly string dbPath = Path.Combine(AppContext.BaseDirectory, "db", "address.db");

        /// <summary>
        /// 数据库的操作
        /// </summary>
        public static SQLiteConnection sqliteOperate;

        /// <summary>
        /// Opcua服务端
        /// </summary>
        public static OpcUaServiceOperate uaService;

        /// <summary>
        /// Mqtt服务端
        /// </summary>
        public static MqttServiceOperate mqttService;

        /// <summary>
        /// 刷新插件信息方法
        /// </summary>
        public static Func<Task>? RefreshAsyncFunc;

        /// <summary>
        /// 刷新插件信息
        /// </summary>
        public static async Task RefreshAsync() => RefreshAsyncFunc?.Invoke();

        /// <summary>
        /// 接口名称
        /// </summary>
        public static readonly string InterfaceFullName = "Snet.Model.interface.I{0}";

        /// <summary>
        /// 默认要执行的任务文件夹路径
        /// </summary>
        public static readonly string TaskPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "task");

        /// <summary>
        /// 库配置唯一标识符键
        /// </summary>
        public static readonly string LibConfigSNKey = "SN";

        /// <summary>
        /// 弹窗标识符
        /// </summary>
        public static readonly string DialogHostTag = "DialogHost";

        /// <summary>
        /// 弹窗标识符 - 点击外侧自动关闭
        /// </summary>
        public static readonly string DialogHostTag_ClickClose = "DialogHost_ClickClose";

        /// <summary>
        /// 缓存参数对话框
        /// </summary>
        public static readonly PropertyControl param = InjectionWpf.GetService<PropertyControl>();

        /// <summary>
        /// 缓存设备选择
        /// </summary>
        public static readonly SelectDevice device = InjectionWpf.GetService<SelectDevice>();

        /// <summary>
        /// 缓存处理
        /// </summary>
        public static readonly Handler handler = InjectionWpf.GetService<Handler>();

        /// <summary>
        /// 缓存处理模型
        /// </summary>
        public static readonly HandlerModel handlerModel = InjectionWpf.GetService<Handler>().DataContext.GetSource<HandlerModel>();

        /// <summary>
        /// 缓存设备选择视图模型
        /// </summary>
        public static readonly SelectDeviceModel deviceModel = device.DataContext.GetSource<SelectDeviceModel>();

        /// <summary>
        /// 缓存地址选择
        /// </summary>
        public static readonly SelectAddress address = InjectionWpf.GetService<SelectAddress>();
        /// <summary>
        /// 缓存地址选择视图模型
        /// </summary>
        public static readonly SelectAddressModel addressModel = address.DataContext.GetSource<SelectAddressModel>();

        /// <summary>
        /// 默认文件路径
        /// </summary>
        public static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "lib");

        /// <summary>
        /// 默认配置路径
        /// </summary>
        public static readonly string ConfigPath = Path.Combine(AppContext.BaseDirectory, "config");

        /// <summary>
        /// 界面配置
        /// </summary>
        public static readonly string UiConfigPath = Path.Combine(ConfigPath, "ui");

        /// <summary>
        /// 服务配置
        /// </summary>
        public static readonly string ServerConfigPath = Path.Combine(ConfigPath, "server");

        /// <summary>
        /// ua服务端配置
        /// </summary>
        public static readonly string UaServerConfigPath = Path.Combine(ServerConfigPath, "UaServerConfig.json");

        /// <summary>
        /// Mqtt服务端配置
        /// </summary>
        public static readonly string MqttServerConfigPath = Path.Combine(ServerConfigPath, "MqttServerConfig.json");

        /// <summary>
        /// 界面插件集合配置路径
        /// </summary>
        public static readonly string UI_PluginListConfigPath = Path.Combine(UiConfigPath, "PluginList.json");

        /// <summary>
        /// 界面插件配置路径
        /// </summary>
        public static readonly string UI_PluginConfigPath = Path.Combine(UiConfigPath, "PluginConfig.json");

        /// <summary>
        /// 界面项目配置路径
        /// </summary>
        public static readonly string UI_ProjectConfigPath = Path.Combine(UiConfigPath, "ProjectConfig.json");

        /// <summary>
        /// 打开文件选择对话框，根据指定扩展名过滤文件
        /// </summary>
        /// <param name="fileExt">文件扩展名（不含点号，如 "json"、"csv"）</param>
        /// <returns>选中的文件路径，未选择时返回空字符串</returns>
        public static string SelectFiles(string fileExt)
        {
            var filters = new Dictionary<string, string>
            {
                { $"(*.{fileExt})", $"*.{fileExt}" },
            };
            return Win32Handler.Select(App.LanguageOperate.GetLanguageValue("请选择文件"), false, filters);
        }

        /// <summary>
        /// 打开文件夹选择对话框
        /// </summary>
        /// <returns>选中的文件夹路径，未选择时返回空字符串</returns>
        public static string SelectFolder()
        {
            return Win32Handler.Select(App.LanguageOperate.GetLanguageValue("请选择文件夹"), true);
        }

        /// <summary>
        /// 托盘设备状态集合，供系统托盘右键菜单绑定使用。<br/>
        /// 由 ConsoleModel 在刷新设备时同步维护。
        /// </summary>
        public static ObservableCollection<Snet.Iot.Daq.viewModel.ConsoleDeviceModel> TrayDevices { get; } = new();
    }
}
