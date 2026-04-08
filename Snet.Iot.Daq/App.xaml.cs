using Microsoft.Extensions.DependencyInjection;
using Snet.Core.handler;
using Snet.Iot.Daq.data;
using Snet.Iot.Daq.handler;
using Snet.Iot.Daq.view;
using Snet.Log;
using Snet.Model.data;
using Snet.Windows.Controls.data;
using Snet.Windows.Controls.property;
using Snet.Windows.Core.handler;
using System.Collections.ObjectModel;
using System.Windows;

namespace Snet.Iot.Daq
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// 语言操作
        /// </summary>
        public readonly static LanguageModel LanguageOperate = new LanguageModel("Snet.Iot.Daq", "Language", "Snet.Iot.Daq.dll");

        /// <summary>
        /// 信息框模型集合
        /// </summary>
        public readonly static List<EditModel> EditModels = GetEditModels();

        /// <summary>
        /// 获取信息框模型集合，定义日志输出文本的颜色高亮规则
        /// </summary>
        /// <returns>编辑模型集合，包含各类日志标签对应的高亮颜色</returns>
        private static List<EditModel> GetEditModels() =>
        [
            new() { Name = "[ Info ]",                Color = "#4CAF50" },
            new() { Name = "[ Error ]",               Color = "#F44336" },
            new() { Name = "异常",                     Color = "#F44336" },
            new() { Name = "Exception",               Color = "#F44336" },
            new() { Name = "[ Mq ]",                  Color = "#2196F3" },
            new() { Name = "[ Daq ]",                 Color = "#2196F3" },
            new() { Name = "[ MqttService ]",         Color = "#2196F3" },
            new() { Name = "[ OpcUaService ]",        Color = "#2196F3" },
            new() { Name = "[ MqttServiceOperate ]",  Color = "#2196F3" },
            new() { Name = "[ OpcUaServiceOperate ]", Color = "#2196F3" },
            new() { Name = "TRUE",                    Color = "#4CAF50" },
            new() { Name = "FALSE",                   Color = "#F44336" },
            new() { Name = ">",                       Color = "#FBC31D" },
            new() { Name = "<",                       Color = "#FBC31D" },
        ];

        /// <summary>
        /// 在应用程序关闭时发生，释放全局注入的服务资源
        /// </summary>
        private void OnExit(object sender, ExitEventArgs e)
        {
            InjectionWpf.ClearService();
        }

        /// <summary>
        /// 在加载应用程序时发生，执行初始化和全局异常注册后打开主窗口
        /// </summary>
        private void OnStartup(object sender, StartupEventArgs e)
        {
            // 初始化依赖注入、数据库、插件等
            Init();

            // 启动全局异常捕捉
            RegisterEvents();

            // 加载本地自定义图标资源
            IconsHandler.Loading("pack://application:,,,/Snet.Iot.Daq;component/resources/icons.xaml");

            // 打开主窗口
            InjectionWpf.Window<MainWindow, MainWindowModel>(true).Show();
        }

        /// <summary>
        /// 初始化应用程序核心资源：依赖注入、用户控件注册、任务执行、数据库建表、插件加载
        /// </summary>
        private void Init()
        {
            // 注入参数设置控件
            PropertyControl control = new PropertyControl();
            control.ButtonVisibility = Visibility.Visible;
            InjectionWpf.AddService(s =>
            {
                s.AddSingleton(control);
            });

            // 注入设备选择控件
            InjectionWpf.UserControl<SelectDevice, Snet.Iot.Daq.viewModel.SelectDeviceModel>(true);

            // 注入地址选择控件
            InjectionWpf.UserControl<SelectAddress, Snet.Iot.Daq.viewModel.SelectAddressModel>(true);

            // 注入处理器控件
            InjectionWpf.UserControl<Handler, Snet.Iot.Daq.viewModel.HandlerModel>(true);

            // 初始化 SQLite 数据库表
            GlobalConfigModel.sqliteOperate.CreateTable<AddressModel>();

            // 加载并初始化所有已配置的插件
            ObservableCollection<PluginListModel> plugins = PluginHandler.GetPluginUIConfig<ObservableCollection<PluginListModel>>(GlobalConfigModel.UI_PluginListConfigPath) ?? new();
            //初始化插件
            foreach (var item in plugins)
            {
                PluginHandler.InitPlugin(item.PluginDetails.PluginPath, string.Format(GlobalConfigModel.InterfaceFullName, item.Type));
            }

            //获取所有已存在的插件
            Snet.Iot.Daq.handler.PluginHandler.GetAllPlugin();

            //获取所有地址
            Snet.Iot.Daq.handler.AddressHandler.GetAllAddress();

            //获取所有项目
            Snet.Iot.Daq.handler.ProjectHandler.GetAllProject();

        }

        #region 全局异常捕捉

        /// <summary>
        /// 注册全局异常捕获事件，覆盖 Task 线程、UI 线程和非 UI 线程的未处理异常
        /// </summary>
        private void RegisterEvents()
        {
            //Task线程内未捕获异常处理事件
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            //UI线程未捕获异常处理事件（UI主线程）
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;

            //非UI线程未捕获异常处理事件(例如自己创建的一个子线程)
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        /// <summary>
        /// Task 线程内未捕获异常处理事件，先进行空判断再访问 HResult 属性以避免空引用异常。
        /// </summary>
        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                var exception = e.Exception as Exception;
                if (exception == null)
                    return;

                if (exception.HResult == -2146233088)
                    return;

                HandleException(exception);
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
            finally
            {
                e.SetObserved();
            }
        }

        /// <summary>
        /// 非UI线程未捕获异常处理事件（例如自己创建的子线程）
        /// </summary>
        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var exception = e.ExceptionObject as Exception;
                if (exception != null)
                {
                    HandleException(exception);
                }
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
            finally
            {
                //ignore
            }
        }

        /// <summary>
        /// UI线程未捕获异常处理事件（UI主线程）
        /// </summary>
        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                HandleException(e.Exception);
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
            finally
            {
                //处理完后，我们需要将Handler=true表示已此异常已处理过
                e.Handled = true;
            }
        }

        /// <summary>
        /// 处理异常到界面显示与本地日志记录
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        private async Task HandleException(Exception e)
        {
            string source = e.Source ?? string.Empty;
            string message = e.Message ?? string.Empty;
            string stackTrace = e.StackTrace ?? string.Empty;
            string msg;
            if (!string.IsNullOrEmpty(source))
            {
                msg = source;
                if (!string.IsNullOrEmpty(message))
                    msg += $"\r\n{message}";
                if (!string.IsNullOrEmpty(stackTrace))
                    msg += $"\r\n\r\n{stackTrace}";
            }
            else if (!string.IsNullOrEmpty(message))
            {
                msg = message;
                if (!string.IsNullOrEmpty(stackTrace))
                    msg += $"\r\n\r\n{stackTrace}";
            }
            else if (!string.IsNullOrEmpty(stackTrace))
                msg = stackTrace;
            else
                msg = "未知异常";
            if (Application.Current == null)
                return;
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                await Snet.Windows.Controls.message.MessageBox.Show(msg, LanguageOperate.GetLanguageValue("全局异常捕获"), Snet.Windows.Controls.@enum.MessageBoxButton.OK, Snet.Windows.Controls.@enum.MessageBoxImage.Exclamation);
            }
            , System.Windows.Threading.DispatcherPriority.Loaded);

            LogHelper.Error(msg, "Snet.Iot.Daq.log", e);
        }

        #endregion
    }
}
