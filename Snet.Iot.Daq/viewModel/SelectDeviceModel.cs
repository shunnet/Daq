using CommunityToolkit.Mvvm.Input;
using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.@enum;
using Snet.Iot.Daq.Core.handler;
using Snet.Iot.Daq.Core.mvvm;
using Snet.Iot.Daq.data;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Snet.Iot.Daq.viewModel
{
    /// <summary>
    /// 选择设备视图模型，提供插件配置列表展示、状态验证、DataGrid 右键菜单交互等功能。
    /// </summary>
    public class SelectDeviceModel : BindNotify
    {
        #region 属性
        /// <summary>
        /// 插件配置集合
        /// </summary>
        public ObservableCollection<PluginConfigModel> PluginConfig
        {
            get => _PluginConfig;
            set => SetProperty(ref _PluginConfig, value);
        }
        private ObservableCollection<PluginConfigModel> _PluginConfig = new ObservableCollection<PluginConfigModel>();

        /// <summary>
        /// 选中的插件配置
        /// </summary>
        public PluginConfigModel PluginConfigSelectedItem
        {
            get => GetProperty(() => PluginConfigSelectedItem);
            set => SetProperty(() => PluginConfigSelectedItem, value);
        }
        #endregion

        #region 命令
        /// <summary>
        /// 状态验证
        /// </summary>
        public IAsyncRelayCommand StatusVerification => statusVerification ??= new AsyncRelayCommand(StatusVerificationAsync);
        private IAsyncRelayCommand? statusVerification;
        public async Task StatusVerificationAsync()
        {
            await Task.Run(async () =>
            {
                foreach (var item in PluginConfig)
                {
                    //插件类型
                    PluginType plugin = item.Type;
                    //接口名称
                    string iName = string.Format(GlobalConfigModel.InterfaceFullName, plugin);
                    item.Status = (await PluginHandlerCore.StatusVerifyAsync(iName, item.Name, item.Param)).Status;
                }
            });
        }
        #endregion

        #region 界面事件
        /// <summary>
        /// 内容菜单打开触发
        /// </summary>
        public IAsyncRelayCommand DataGrid_ContextMenuOpening => dataGrid_ContextMenuOpening ??= new AsyncRelayCommand<ContextMenuEventArgs>(DataGrid_ContextMenuOpeningAsync);
        private IAsyncRelayCommand? dataGrid_ContextMenuOpening;
        private async Task DataGrid_ContextMenuOpeningAsync(ContextMenuEventArgs? e)
        {
            if (e?.Source is not DataGrid dataGrid)
                return;

            // 最终裁决：
            // 只要当前不是“行右键”，就禁止弹出
            if (dataGrid.SelectedItem == null)
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// 鼠标右键点击触发
        /// </summary>
        public IAsyncRelayCommand DataGrid_PreviewMouseRightButtonDown => dataGrid_PreviewMouseRightButtonDown ??= new AsyncRelayCommand<MouseButtonEventArgs>(DataGrid_PreviewMouseRightButtonDownAsync);
        private IAsyncRelayCommand? dataGrid_PreviewMouseRightButtonDown;
        private async Task DataGrid_PreviewMouseRightButtonDownAsync(MouseButtonEventArgs? e)
        {
            if (e?.Source is not DataGrid dataGrid)
                return;

            System.Windows.DependencyObject dep = (System.Windows.DependencyObject)e.OriginalSource;

            while (dep != null && dep is not DataGridRow)
                dep = VisualTreeHelper.GetParent(dep);

            if (dep is DataGridRow row)
            {
                // 右键在行上
                dataGrid.SelectedItem = row.Item;
                row.IsSelected = true;
                row.Focus();
            }
            else
            {
                // 右键空白：清空选择
                dataGrid.SelectedItem = null;
                e.Handled = true; // 阻止默认右键
            }
        }
        #endregion

        #region 方法
        /// <summary>
        /// 获取
        /// </summary>
        /// <returns></returns>
        public PluginConfigModel GetValue()
        {
            return PluginConfigSelectedItem ?? new();
        }

        /// <summary>
        /// 设置
        /// </summary>
        /// <param name="type">插件类型</param>
        public void SetValue(PluginType type)
        {
            PluginConfig.Clear();
            foreach (var item in PluginHandlerCore.GetPluginUIConfig<ObservableCollection<PluginConfigModel>>(GlobalConfigModel.UI_PluginConfigPath) ?? new())
            {
                if (item.Type == type)
                    PluginConfig.Add(item);
            }
        }
        #endregion
    }
}
