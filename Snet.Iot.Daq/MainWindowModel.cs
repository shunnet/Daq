using CommunityToolkit.Mvvm.Input;
using Snet.Iot.Daq.data;
using Snet.Iot.Daq.view;
using Snet.Iot.Daq.viewModel;
using Snet.Model.data;
using Snet.Windows.Controls.handler;
using Snet.Windows.Core.mvvm;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using Wpf.Ui.Controls;
using LanguageHandler = Snet.Core.handler.LanguageHandler;
namespace Snet.Iot.Daq
{
    /// <summary>
    /// 主窗口视图模型，负责初始化导航菜单项、托盘操作命令以及设备状态托盘绑定。
    /// </summary>
    public class MainWindowModel : BindNotify
    {
        /// <summary>
        /// 构造函数，初始化菜单项数据源
        /// </summary>
        public MainWindowModel(SettingsHandler settings)
        {
            // 初始化菜单项数据源
            MenuItemsSource = MenuItemsOperate(App.LanguageOperate);
            FooterMenuItemsSource = FooterMenuItemsOperate(App.LanguageOperate);

            this.settings = settings;
            LanguageHandler.OnLanguageEvent += LanguageHandler_OnLanguageEvent;
            LanguageHandler_OnLanguageEvent(null, null);
        }

        /// <summary>
        /// 系统设置处理器
        /// </summary>
        private SettingsHandler settings;

        /// <summary>
        /// 语言切换事件
        /// </summary>
        private void LanguageHandler_OnLanguageEvent(object? sender, EventLanguageResult e)
        {
            SystemTitle = $"{LanguageHandler.GetLanguageValue("SystemTitle", App.LanguageOperate)}{(settings.IsRunAsAdmin() ? " [ " + LanguageHandler.GetLanguageValue("管理员运行", App.LanguageOperate) + " ]" : string.Empty)}";
        }

        /// <summary>
        /// 系统标题
        /// </summary>
        public string SystemTitle
        {
            get => GetProperty(() => SystemTitle);
            set => SetProperty(() => SystemTitle, value);
        }

        /// <summary>
        /// 菜单项数据源
        /// </summary>
        public ICollection<object> MenuItemsSource
        {
            get => GetProperty(() => MenuItemsSource);
            set => SetProperty(() => MenuItemsSource, value);
        }

        /// <summary>
        /// 底部菜单项数据源
        /// </summary>
        public ICollection<object> FooterMenuItemsSource
        {
            get => GetProperty(() => FooterMenuItemsSource);
            set => SetProperty(() => FooterMenuItemsSource, value);
        }

        /// <summary>
        /// 托盘设备状态集合，供系统托盘右键菜单绑定使用
        /// </summary>
        public ObservableCollection<ConsoleDeviceModel> TrayDevices => GlobalConfigModel.TrayDevices;

        /// <summary>
        /// 创建主菜单项集合
        /// </summary>
        /// <param name="model">语言模型，用于多语言支持</param>
        /// <returns>返回主菜单项集合</returns>
        public ObservableCollection<object> MenuItemsOperate(LanguageModel model) => new(){
            WpfUiHandler.CreationControl("主页", SymbolRegular.Home24, typeof(Home),true,model),
            WpfUiHandler.CreationControl("插件设置", SymbolRegular.DocumentQueue20, typeof(PluginSettings),true,model),
            WpfUiHandler.CreationControl("地址设置", SymbolRegular.Fluid20, typeof(AddressSettings),true,model),
            WpfUiHandler.CreationControl("项目设置", SymbolRegular.ProjectionScreen16, typeof(ProjectSettings),true,model),
            WpfUiHandler.CreationControl("控制台", SymbolRegular.WindowConsole20, typeof(Snet.Iot.Daq.view.Console),true,model),
         };

        /// <summary>
        /// 创建底部菜单项集合
        /// </summary>
        /// <param name="model">语言模型，用于多语言支持</param>
        /// <returns>返回底部菜单项集合</returns>
        public ObservableCollection<object> FooterMenuItemsOperate(LanguageModel model) => new(){
            WpfUiHandler.CreationControl("关于", SymbolRegular.Info28, typeof(About), true, model)
        };

        /// <summary>
        /// 显示主窗口命令，从系统托盘恢复窗口显示
        /// </summary>
        public IAsyncRelayCommand ShowWindow => p_ShowWindow ??= new AsyncRelayCommand(ShowWindowAsync);
        private IAsyncRelayCommand p_ShowWindow;

        /// <summary>
        /// 安全显示主窗口
        /// </summary>
        private Task ShowWindowAsync()
        {
            var window = Application.Current.MainWindow;
            if (window == null)
                return Task.CompletedTask;

            window.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    // 🔥 1. 如果窗口隐藏（托盘）
                    if (!window.IsVisible)
                    {
                        window.ShowInTaskbar = true;
                        window.Show();
                    }

                    // 🔥 2. 如果最小化，恢复
                    if (window.WindowState == WindowState.Minimized)
                    {
                        window.WindowState = WindowState.Normal;
                    }

                    // 🔥 3. 用 Focus 替代 Activate（关键！）
                    window.Focus();

                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ShowWindowAsync] 异常: {ex.Message}");
                }

            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            return Task.CompletedTask;
        }

        /// <summary>
        /// 关闭应用程序命令（从托盘菜单调用，真正退出程序）
        /// </summary>
        public IAsyncRelayCommand Close => p_Close ??= new AsyncRelayCommand(CloseAsync);
        private IAsyncRelayCommand p_Close;

        /// <summary>
        /// 关闭应用程序，设置强制关闭标志后执行退出
        /// </summary>
        /// <returns>已完成的任务</returns>
        private Task CloseAsync()
        {
            // 设置强制关闭标志，避免 OnClosing 拦截
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.IsForceClose = true;
            }
            Application.Current.Shutdown();
            return Task.CompletedTask;
        }
    }
}
