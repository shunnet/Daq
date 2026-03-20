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
using System.Diagnostics;
using System.IO;
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
        /// 获取信息框模型集合
        /// </summary>
        /// <returns></returns>
        private static List<EditModel> GetEditModels()
        {
            List<EditModel> models = new List<EditModel>();

            models.Add(new EditModel
            {
                Name = "[ Info ]",
                Color = "#4CAF50"
            });
            models.Add(new EditModel
            {
                Name = "[ Error ]",
                Color = "#F44336"
            });

            models.Add(new EditModel
            {
                Name = "异常",
                Color = "#F44336"
            });
            models.Add(new EditModel
            {
                Name = "Exception",
                Color = "#F44336"
            });
            models.Add(new EditModel
            {
                Name = "[ Mq ]",
                Color = "#2196F3"
            });

            models.Add(new EditModel
            {
                Name = "[ Daq ]",
                Color = "#2196F3"
            });

            models.Add(new EditModel
            {
                Name = "[ MqttService ]",
                Color = "#2196F3"
            });

            models.Add(new EditModel
            {
                Name = "[ OpcUaService ]",
                Color = "#2196F3"
            });

            models.Add(new EditModel
            {
                Name = "[ MqttServiceOperate ]",
                Color = "#2196F3"
            });

            models.Add(new EditModel
            {
                Name = "[ OpcUaServiceOperate ]",
                Color = "#2196F3"
            });

            models.Add(new EditModel
            {
                Name = "TRUE",
                Color = "#4CAF50"
            });

            models.Add(new EditModel
            {
                Name = "FALSE",
                Color = "#F44336"
            });

            models.Add(new EditModel
            {
                Name = ">",
                Color = "#FBC31D"
            });

            models.Add(new EditModel
            {
                Name = "<",
                Color = "#FBC31D"
            });



            return models;
        }

        /// <summary>
        /// 在应用程序关闭时发生
        /// </summary>
        private void OnExit(object sender, ExitEventArgs e)
        {
            InjectionWpf.ClearService();
            GC.SuppressFinalize(this);
            GC.Collect();
        }

        /// <summary>
        /// 在加载应用程序时发生
        /// </summary>
        private void OnStartup(object sender, StartupEventArgs e)
        {
            //初始化
            Init();

            //启动全局异常捕捉
            RegisterEvents();

            //加载本地自定义图标
            IconsHandler.Loading("pack://application:,,,/Snet.Iot.Daq;component/resources/icons.xaml");

            //打开主窗口
            InjectionWpf.Window<MainWindow, MainWindowModel>(true).Show();
        }

        /// <summary>
        /// 初始化
        /// </summary>
        private void Init()
        {
            //注入参数设置
            PropertyControl control = new PropertyControl();
            control.ButtonVisibility = Visibility.Visible;
            InjectionWpf.AddService(s =>
            {
                s.AddSingleton(control);
            });

            //注入设备选择
            InjectionWpf.UserControl<SelectDevice, Snet.Iot.Daq.viewModel.SelectDeviceModel>(true);

            //注入地址选择
            InjectionWpf.UserControl<SelectAddress, Snet.Iot.Daq.viewModel.SelectAddressModel>(true);

            //注入处理
            InjectionWpf.UserControl<Handler, Snet.Iot.Daq.viewModel.HandlerModel>(true);

            // 处理任务 -------------------------
            string batDirectory = GlobalConfigModel.TaskPath;
            if (Directory.Exists(batDirectory))
            {
                foreach (var batFile in Directory.GetFiles(batDirectory, "*.bat"))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = batFile,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                }
            }

            //初始化数据库 -------------------------
            GlobalConfigModel.sqliteOperate.CreateTable<AddressModel>();

            //初始化插件 -------------------------
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
        /// 全局异常捕捉
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

        //Task线程报错
        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                var exception = e.Exception as Exception;
                if (exception.HResult == -2146233088)
                    return;

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
                e.SetObserved();
            }
        }

        //非UI线程未捕获异常处理事件(例如自己创建的一个子线程)
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

        //UI线程未捕获异常处理事件（UI主线程）
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
