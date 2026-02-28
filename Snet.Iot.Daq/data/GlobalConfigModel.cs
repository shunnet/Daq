using Snet.Core.handler;
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
    /// 全局配置
    /// </summary>
    public static class GlobalConfigModel
    {
        static GlobalConfigModel()
        {
            string directory = Path.GetDirectoryName(GlobalConfigModel.dbPath);
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
        public static readonly string UiConfigPath = Path.Combine(AppContext.BaseDirectory, "config", "ui");

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
        /// 选中文件
        /// </summary>
        /// <param name="fileExt">文件格式</param>
        /// <returns></returns>
        public static string SelectFiles(string fileExt)
        {
            var filters = new Dictionary<string, string>
            {
                { $"(*.{fileExt})", $"*.{fileExt}" },
            };
            return Win32Handler.Select(App.LanguageOperate.GetLanguageValue("请选择文件"), false, filters);
        }

        /// <summary>
        /// 选中文件夹
        /// </summary>
        /// <returns></returns>
        public static string SelectFolder()
        {
            return Win32Handler.Select(App.LanguageOperate.GetLanguageValue("请选择文件夹"), true);
        }
    }
}
