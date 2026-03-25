using CommunityToolkit.Mvvm.Input;
using Snet.Iot.Daq.data;
using Snet.Utility;
using Snet.Windows.Core.mvvm;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Snet.Iot.Daq.viewModel
{
    /// <summary>
    /// 处理器视图模型，提供字节数据处理配置的导入/导出、DataGrid 右键菜单交互等功能。
    /// </summary>
    public class HandlerModel : BindNotify
    {
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

        #region 属性
        /// <summary>
        /// 数据表格源
        /// </summary>
        public ObservableCollection<BytesBindNotifyModel> HandlerItemsSource
        {
            get => _HandlerItemsSource;
            set => SetProperty(ref _HandlerItemsSource, value);
        }
        private ObservableCollection<BytesBindNotifyModel> _HandlerItemsSource = new ObservableCollection<BytesBindNotifyModel>();

        /// <summary>
        /// 选中的项
        /// </summary>
        public BytesBindNotifyModel HandlerConfigSelectedItem
        {
            get => GetProperty(() => HandlerConfigSelectedItem);
            set => SetProperty(() => HandlerConfigSelectedItem, value);
        }
        #endregion

        #region 命令
        /// <summary>
        /// 导入
        /// </summary>
        public IAsyncRelayCommand Import => import ??= new AsyncRelayCommand(ImportAsync);
        private IAsyncRelayCommand import;
        private async Task ImportAsync()
        {
            string file = GlobalConfigModel.SelectFiles("json");
            if (!string.IsNullOrEmpty(file))
            {
                HandlerItemsSource = FileHandler.FileToString(file).ToJsonEntity<ObservableCollection<BytesBindNotifyModel>>();
            }
        }


        /// <summary>
        /// 导出
        /// </summary>
        public IAsyncRelayCommand Export => export ??= new AsyncRelayCommand(ExportAsync);
        private IAsyncRelayCommand export;
        private async Task ExportAsync()
        {
            if (HandlerItemsSource.Count > 0)
            {
                string path = GlobalConfigModel.SelectFolder();
                if (!string.IsNullOrEmpty(path))
                {
                    FileHandler.StringToFile(Path.Combine(path, $"BytesHandler[{DateTime.Now.ToString("yyyyMMddHHmmss")}].json"), HandlerItemsSource.ToJson(true));
                }
            }
        }

        /// <summary>
        /// 添加
        /// </summary>
        public IAsyncRelayCommand Add => add ??= new AsyncRelayCommand(AddAsync);
        private IAsyncRelayCommand add;
        private async Task AddAsync()
        {
            HandlerItemsSource.Add(new());
        }

        /// <summary>
        /// 删除
        /// </summary>
        public IAsyncRelayCommand Delete => delete ??= new AsyncRelayCommand(DeleteAsync);
        private IAsyncRelayCommand delete;
        private async Task DeleteAsync()
        {
            if (HandlerConfigSelectedItem != null)
            {
                HandlerItemsSource.Remove(HandlerConfigSelectedItem);
            }
        }
        #endregion
    }
}
