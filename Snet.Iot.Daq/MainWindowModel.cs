using CommunityToolkit.Mvvm.Input;
using Snet.Iot.Daq.view;
using Snet.Model.data;
using Snet.Windows.Controls.handler;
using Snet.Windows.Core.mvvm;
using System.Collections.ObjectModel;
using System.Windows;
using Wpf.Ui.Controls;

namespace Snet.Iot.Daq
{
    public class MainWindowModel : BindNotify
    {
        public MainWindowModel()
        {
            // 初始化菜单项数据源
            MenuItemsSource = MenuItemsOperate(App.LanguageOperate);   //给菜单项赋值
            FooterMenuItemsSource = FooterMenuItemsOperate(App.LanguageOperate);  //给底部菜单项赋值
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
        /// 菜单项操作
        /// </summary>
        /// <returns>返回新的菜单项</returns>
        public ObservableCollection<object> MenuItemsOperate(LanguageModel model) => new(){
            WpfUiHandler.CreationControl("主页", SymbolRegular.Home24, typeof(Home),true,model),
            WpfUiHandler.CreationControl("插件设置", SymbolRegular.DocumentQueue20, typeof(PluginSettings),true,model),
            WpfUiHandler.CreationControl("地址设置", SymbolRegular.Fluid20, typeof(AddressSettings),true,model),
            WpfUiHandler.CreationControl("项目设置", SymbolRegular.ProjectionScreen16, typeof(ProjectSettings),true,model),
            WpfUiHandler.CreationControl("控制台", SymbolRegular.WindowConsole20, typeof(Snet.Iot.Daq.view.Console),true,model),
         };

        /// <summary>
        /// 底部菜单项操作
        /// </summary>
        /// <returns>返回新的菜单项</returns>
        public ObservableCollection<object> FooterMenuItemsOperate(LanguageModel model) => new(){
            WpfUiHandler.CreationControl("关于", SymbolRegular.Info28, typeof(About), true, model)
        };


        /// <summary>
        /// 关闭
        /// </summary>
        public IAsyncRelayCommand Close => p_Close ??= new AsyncRelayCommand(CloseAsync);
        private IAsyncRelayCommand p_Close;
        private Task CloseAsync()
        {
            Application.Current.Shutdown();
            return Task.CompletedTask;
        }

        #region 右键托盘 Item MVVM 
        /*

        ////注册托盘点击事件
           //foreach (var menuItem in TrayMenuItems)
           //{
           //    if (menuItem is MenuItem item)
           //    {
           //        item.Click += OnTrayMenuItemClick;
           //    }
           //}

        */
        ///// <summary>
        ///// 托盘右键菜单
        ///// </summary>
        //public ObservableCollection<System.Windows.Controls.Control> TrayMenuItems => _trayMenuItems ??= [
        //        new Wpf.Ui.Controls.MenuItem()
        //        {
        //            Header = "关闭",
        //            Tag = "tray_close",
        //            Icon = new SymbolIcon { Symbol = SymbolRegular.Dismiss24 },
        //        },
        //    ];
        //private ObservableCollection<System.Windows.Controls.Control> _trayMenuItems;
        ///// <summary>
        ///// 托盘点击
        ///// </summary>
        //private void OnTrayMenuItemClick(object sender, RoutedEventArgs e)
        //{
        //    if (sender is not Wpf.Ui.Controls.MenuItem menuItem)
        //        return;
        //    var tag = menuItem.Tag?.ToString() ?? string.Empty;
        //    switch (tag)
        //    {
        //        case "tray_close":
        //            Application.Current.Shutdown();
        //            break;
        //    }
        //} 
        #endregion
    }
}
