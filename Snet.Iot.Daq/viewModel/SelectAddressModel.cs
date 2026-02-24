using CommunityToolkit.Mvvm.Input;
using Snet.Core.handler;
using Snet.Iot.Daq.data;
using Snet.Utility;
using Snet.Windows.Controls.message;
using Snet.Windows.Core.mvvm;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Snet.Iot.Daq.viewModel
{
    public class SelectAddressModel : BindNotify
    {

        #region 属性
        /// <summary>
        /// 地址配置集合
        /// </summary>
        public ObservableCollection<Snet.Iot.Daq.data.AddressModel> AddressConfig
        {
            get => addressConfig;
            set => SetProperty(ref addressConfig, value);
        }
        private ObservableCollection<Snet.Iot.Daq.data.AddressModel> addressConfig = new ObservableCollection<Snet.Iot.Daq.data.AddressModel>();


        /// <summary>
        /// 地址配置被选中的项
        /// </summary>
        public Snet.Iot.Daq.data.AddressModel AddressConfigSelectedItem
        {
            get => GetProperty(() => AddressConfigSelectedItem);
            set => SetProperty(() => AddressConfigSelectedItem, value);
        }

        /// <summary>
        /// 查询的内容
        /// </summary>
        /// <returns></returns>
        public string QueryCntent
        {
            get => GetProperty(() => QueryCntent);
            set => SetProperty(() => QueryCntent, value);
        }

        /// <summary>
        /// 是否表格中显示的全部项选中
        /// </summary>
        public bool IsAllItems1Selected
        {
            get => GetProperty(() => IsAllItems1Selected);
            set => SetProperty(() => IsAllItems1Selected, value);
        }

        /// <summary>
        /// 总数量
        /// </summary>
        public int Total
        {
            get => GetProperty(() => Total);
            set => SetProperty(() => Total, value);
        }

        /// <summary>
        /// 每页的页数
        /// </summary>
        public int PageSize
        {
            get => pageSize;
            set => SetProperty(ref pageSize, value);
        }
        private int pageSize = 50;

        /// <summary>
        /// 页索引
        /// </summary>
        public int PageIndex
        {
            get => GetProperty(() => PageIndex);
            set => SetProperty(() => PageIndex, value);
        }
        #endregion

        #region 命令
        /// <summary>
        /// 查询地址
        /// </summary>
        public IAsyncRelayCommand QueryAddress => queryAddress ??= new AsyncRelayCommand(QueryAddressAsync);
        private IAsyncRelayCommand? queryAddress;
        private async Task QueryAddressAsync()
        {
            if (QueryCntent.IsNullOrWhiteSpace())
            {
                //查询所有
                await PageIndexChangedExecuteAsync(1);
            }
            else
            {
                //模糊查询
                List<Snet.Iot.Daq.data.AddressModel> models = GlobalConfigModel.sqliteOperate.Table<Snet.Iot.Daq.data.AddressModel>().Where(p =>
                p.AnotherName.Contains(QueryCntent) || p.AnotherName.Equals(QueryCntent) ||
                p.Address.Contains(QueryCntent) || p.Address.Equals(QueryCntent) ||
                p.Describe.Contains(QueryCntent) || p.Describe.Equals(QueryCntent)).ToList();
                if (models.Count > 0)
                {
                    await ResetUiAsync(models.Count(), 1, models);
                }
                else
                {
                    await MessageBox.Show("未查询到对应内容".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Asterisk);
                }
            }
        }


        /// <summary>
        /// 全选地址
        /// </summary>
        public IAsyncRelayCommand AllSelectAddress => allSelectAddress ??= new AsyncRelayCommand(AllSelectAddressAsync);
        private IAsyncRelayCommand? allSelectAddress;
        private async Task AllSelectAddressAsync()
        {
            foreach (var item in AddressConfig)
            {
                item.IsSelected = true;
            }
        }

        /// <summary>
        /// 反选地址
        /// </summary>
        public IAsyncRelayCommand InverseAddress => inverseAddress ??= new AsyncRelayCommand(InverseAddressAsync);
        private IAsyncRelayCommand? inverseAddress;
        private async Task InverseAddressAsync()
        {
            foreach (var item in AddressConfig)
            {
                item.IsSelected = !item.IsSelected;
            }
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



        /// <summary>
        /// 当前页
        /// </summary>
        public IAsyncRelayCommand PageIndexChanged => pageIndexChanged ??= new AsyncRelayCommand<int>(PageIndexChangedExecuteAsync);
        private IAsyncRelayCommand? pageIndexChanged;
        private async Task PageIndexChangedExecuteAsync(int index)
        {
            List<Snet.Iot.Daq.data.AddressModel> models = GlobalConfigModel.sqliteOperate.Table<Snet.Iot.Daq.data.AddressModel>().ToList();
            await ResetUiAsync(models.Count(), index, models);
        }
        #endregion

        #region 方法
        /// <summary>
        /// 获取选中的项
        /// </summary>
        /// <returns>返回选中的 AddressModel 列表</returns>
        public List<AddressModel> GetSelectItem()
        {
            // 获取所有选中状态为 true 的 AddressModel 项
            return AddressConfig.Where(x => x.IsSelected).ToList();
        }

        /// <summary>
        /// 设置选中的项
        /// </summary>
        /// <param name="models">项集合</param>
        public void SetSelectItem(List<AddressModel>? models = null)
        {
            if (models == null)
            {
                foreach (var item in AddressConfig.Where(x => x.IsSelected))
                {
                    item.IsSelected = false;
                }
            }
            else
            {
                // 遍历传入的模型集合，设置其选中状态
                foreach (var model in models)
                {
                    // 更新 AddressModel 对象中的 IsSelected 状态
                    var addressModel = AddressConfig.FirstOrDefault(x => x.Index == model.Index);
                    if (addressModel != null)
                    {
                        addressModel.IsSelected = model.IsSelected;
                    }
                }
            }
        }

        /// <summary>
        /// 重置界面
        /// </summary>
        /// <param name="total">总数</param>
        /// <param name="pageIndex">页码</param>
        /// <param name="models">数据</param>
        /// <returns></returns>
        private Task ResetUiAsync(int total, int pageIndex, List<Snet.Iot.Daq.data.AddressModel> models)
        {
            PageIndex = pageIndex;
            Total = total;
            AddressConfig.Clear();
            foreach (var item in models.OrderByDescending(x => x.Time).Skip((pageIndex - 1) * PageSize).Take(PageSize).ToList())
            {
                AddressConfig.Add(item);
            }
            return Task.CompletedTask;
        }
        #endregion

    }
}
