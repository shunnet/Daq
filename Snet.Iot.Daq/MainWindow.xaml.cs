using Snet.Core.handler;
using Snet.Iot.Daq.data;
using Snet.Iot.Daq.viewModel;
using Snet.Windows.Controls.handler;
using Snet.Windows.Controls.ledgauge;
using Snet.Windows.Core;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Snet.Iot.Daq
{
    /// <summary>
    /// 主窗口交互逻辑，负责导航初始化、窗口关闭拦截（隐藏到托盘）以及托盘设备状态菜单的动态构建。
    /// </summary>
    public partial class MainWindow : WindowBase
    {
        /// <summary>
        /// 标记是否为强制关闭（由托盘"关闭"命令触发），为 true 时不拦截关闭事件
        /// </summary>
        public bool IsForceClose { get; set; }

        /// <summary>
        /// 托盘设备菜单项的标识标签，用于区分动态添加的设备项和静态菜单项
        /// </summary>
        private const string DeviceMenuItemTag = "TrayDeviceItem";

        /// <summary>
        /// 构造函数，初始化组件并设置默认导航页面和托盘设备状态监听
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            // 默认导航到控制台页面
            NavigationViewControls.SelectNavigationViewDefaultItem(this, typeof(Snet.Iot.Daq.view.Console), App.LanguageOperate, "mainGrid");

            // 监听托盘设备集合变化，动态重建托盘菜单
            GlobalConfigModel.TrayDevices.CollectionChanged += OnTrayDevicesCollectionChanged;

            // 监听语言切换事件，语言变化时重建托盘菜单以更新显示文本
            LanguageHandler.OnLanguageEvent += LanguageHandler_OnLanguageEvent;
        }



        /// <summary>
        /// 重写窗口关闭行为：非强制关闭时，将窗口隐藏到系统托盘而非真正关闭
        /// </summary>
        /// <param name="e">关闭事件参数，可通过 Cancel 属性取消关闭</param>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (!IsForceClose)
            {
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                return;
            }
            base.OnClosing(e);
        }

        /// <summary>
        /// 托盘图标左键点击事件处理：恢复窗口显示并激活
        /// </summary>
        private void TrayIcon_LeftClick(object sender, RoutedEventArgs e)
        {
            Show();
            ShowInTaskbar = true;
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
        }

        /// <summary>
        /// 托盘设备集合变化事件处理：在 UI 线程重建托盘设备状态菜单
        /// </summary>
        private void OnTrayDevicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.Invoke(RebuildTrayDeviceMenu);
        }

        /// <summary>
        /// 语言切换事件处理：当 Culture 属性变化时，重建托盘菜单以刷新本地化文本
        /// </summary>
        private void LanguageHandler_OnLanguageEvent(object? sender, Model.data.EventLanguageResult e)
        {
            Dispatcher.Invoke(RebuildTrayDeviceMenu);
        }

        /// <summary>
        /// 重建托盘右键菜单中的设备状态项。<br/>
        /// 先移除所有已有的动态设备项，再根据当前设备集合重新插入到分隔符之前。
        /// </summary>
        private void RebuildTrayDeviceMenu()
        {
            var menu = TrayContextMenu;
            if (menu == null) return;

            // 移除所有标记为设备项的菜单项
            var toRemove = menu.Items.OfType<FrameworkElement>()
                .Where(item => DeviceMenuItemTag.Equals(item.Tag))
                .ToList();
            foreach (var item in toRemove)
            {
                menu.Items.Remove(item);
            }

            // 查找分隔符位置，在其前方依次插入设备项
            int separatorIndex = menu.Items.IndexOf(TrayDeviceSeparator);
            if (separatorIndex < 0) separatorIndex = 0;

            int insertIndex = separatorIndex;
            foreach (var model in GlobalConfigModel.TrayDevices)
            {
                var deviceItem = CreateDeviceMenuItem(model);
                menu.Items.Insert(insertIndex++, deviceItem);
            }

            // 无设备时隐藏分隔符，有设备时显示
            TrayDeviceSeparator.Visibility = GlobalConfigModel.TrayDevices.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>
        /// 为指定设备模型创建托盘菜单项。<br/>
        /// 菜单项头部包含 LED 指示灯和设备名称，子菜单包含采集/停止/重试操作。
        /// </summary>
        /// <param name="model">设备视图模型，提供设备状态和操作命令</param>
        /// <returns>构建好的设备菜单项</returns>
        private FrameworkElement CreateDeviceMenuItem(ConsoleDeviceModel model)
        {
            // 创建头部面板：LED 指示灯 + 设备名称
            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            // LED 指示灯控件，绑定设备状态属性
            var led = new LedGaugeControl
            {
                Width = 16,
                Height = 16,
                OffLightness = 0.2
            };
            led.SetBinding(LedGaugeControl.IsFlashingProperty,
                new Binding(nameof(ConsoleDeviceModel.DeviceStatusFlashing)) { Source = model });
            led.SetBinding(LedGaugeControl.IsOnProperty,
                new Binding(nameof(ConsoleDeviceModel.DeviceStatusChangLiang)) { Source = model });
            led.SetBinding(LedGaugeControl.ColorProperty,
                new Binding(nameof(ConsoleDeviceModel.LedColor)) { Source = model });
            header.Children.Add(led);

            // 设备名称文本
            var nameText = new TextBlock
            {
                Margin = new Thickness(5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            nameText.SetBinding(TextBlock.TextProperty,
                new Binding(nameof(ConsoleDeviceModel.DeviceName)) { Source = model });
            header.Children.Add(nameText);

            // 创建父级菜单项
            var menuItem = new Wpf.Ui.Controls.MenuItem
            {
                Header = header,
                Tag = DeviceMenuItemTag
            };

            // 添加子菜单：采集、停止、重试
            menuItem.Items.Add(CreateActionMenuItem(
                LanguageHandler.GetLanguageValue("采集", App.LanguageOperate),
                model.Collect,
                "PaletteGreenBrush",
                Wpf.Ui.Controls.SymbolRegular.Play20));
            menuItem.Items.Add(CreateActionMenuItem(
                LanguageHandler.GetLanguageValue("停止", App.LanguageOperate),
                model.Stop,
                "PaletteRedBrush",
                Wpf.Ui.Controls.SymbolRegular.Stop20));
            menuItem.Items.Add(CreateActionMenuItem(
                LanguageHandler.GetLanguageValue("重试", App.LanguageOperate),
                model.Retry,
                "PaletteBlueBrush",
                Wpf.Ui.Controls.SymbolRegular.ArrowClockwise20));

            return menuItem;
        }

        /// <summary>
        /// 创建设备操作子菜单项（采集/停止/重试）
        /// </summary>
        /// <param name="header">菜单项显示文本</param>
        /// <param name="command">绑定的异步命令</param>
        /// <param name="brushKey">前景色动态资源键名</param>
        /// <param name="symbol">图标符号</param>
        /// <returns>构建好的操作菜单项</returns>
        private static Wpf.Ui.Controls.MenuItem CreateActionMenuItem(
            string header,
            System.Windows.Input.ICommand command,
            string brushKey,
            Wpf.Ui.Controls.SymbolRegular symbol)
        {
            var item = new Wpf.Ui.Controls.MenuItem
            {
                Header = header,
                Command = command,
                Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = symbol, Filled = true }
            };
            if (Application.Current.TryFindResource(brushKey) is Brush brush)
            {
                item.Foreground = brush;
            }
            return item;
        }
    }
}