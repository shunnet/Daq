using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using Snet.Core.handler;
using Snet.Iot.Daq.data;
using Snet.Utility;
using Snet.Windows.Controls.message;
using Snet.Windows.Core.mvvm;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static Snet.Iot.Daq.handler.AddressHandler;

namespace Snet.Iot.Daq.viewModel
{
    /// <summary>
    /// 地址设置视图模型
    /// </summary>
    public class AddressSettingsModel : BindNotify
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
        /// 处理
        /// </summary>
        public IAsyncRelayCommand Handler => p_handler ??= new AsyncRelayCommand(HandlerAsync);
        private IAsyncRelayCommand? p_handler;
        private async Task HandlerAsync()
        {
            GlobalConfigModel.handlerModel.HandlerItemsSource?.Clear();
            GlobalConfigModel.handlerModel.HandlerConfigSelectedItem = null;
            GlobalConfigModel.handlerModel.HandlerItemsSource?.Add(new());
            await DialogHost.Show(GlobalConfigModel.handler, GlobalConfigModel.DialogHostTag);
        }

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
                    await ResetUiAsync(models.Count, 1, models);
                }
                else
                {
                    await MessageBox.Show("未查询到对应内容".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Asterisk);
                }
            }
        }

        /// <summary>
        /// 添加地址
        /// </summary>
        public IAsyncRelayCommand AddAddress => addAddress ??= new AsyncRelayCommand(AddAddressAsync);
        private IAsyncRelayCommand? addAddress;
        private async Task AddAddressAsync()
        {
            GlobalConfigModel.param.SetBasics(new Snet.Iot.Daq.data.AddressModel());
            if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
            {
                Snet.Iot.Daq.data.AddressModel param = GlobalConfigModel.param.GetBasics().GetSource<Snet.Iot.Daq.data.AddressModel>();
                try
                {
                    if (param.Address.IsNullOrWhiteSpace())
                    {
                        await MessageBox.Show("地址不能为空".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
                        return;
                    }

                    if (param.AnotherName.IsNullOrWhiteSpace())
                    {
                        await MessageBox.Show("地址别名不能为空".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
                        return;
                    }

                    BatchInsertResult result = handler.AddressHandler.InsertUnique(GlobalConfigModel.sqliteOperate, [param], x => x.AnotherName, x => x.Address);
                    if (result.Duplicate == 0)
                    {
                        await PageIndexChangedExecuteAsync(PageIndex);
                        //往全局集合中添加
                        param.SetAddress();
                    }
                    else
                    {
                        await MessageBox.Show("添加失败，地址或别名重复！".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    await MessageBox.Show(ex.Message, "异常".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 导入地址
        /// </summary>
        public IAsyncRelayCommand ImportAddress => importAddress ??= new AsyncRelayCommand(ImportAddressAsync);
        private IAsyncRelayCommand? importAddress;
        private async Task ImportAddressAsync()
        {
            string file = GlobalConfigModel.SelectFiles("json");
            if (!string.IsNullOrEmpty(file))
            {
                List<Snet.Iot.Daq.data.AddressModel>? models = FileHandler.FileToString(file).ToJsonEntity<List<Snet.Iot.Daq.data.AddressModel>>();
                if (models == null)
                {
                    await MessageBox.Show("导入失败".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
                }
                else
                {
                    BatchInsertResult result = handler.AddressHandler.InsertUnique(GlobalConfigModel.sqliteOperate, models, x => x.AnotherName, x => x.Address);
                    await PageIndexChangedExecuteAsync(1);
                    if (result.Failed > 0)
                    {
                        await MessageBox.Show($"{"存在".GetLanguageValue(App.LanguageOperate)}“{result.Failed}”{"个点位导入失败".GetLanguageValue(App.LanguageOperate)}", "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
                    }
                    else
                    {
                        await MessageBox.Show($"导入成功".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
                    }
                }
            }
        }


        /// <summary>
        /// 导出地址
        /// </summary>
        public IAsyncRelayCommand ExportAddress => exportAddress ??= new AsyncRelayCommand(ExportAddressAsync);
        private IAsyncRelayCommand? exportAddress;
        private async Task ExportAddressAsync()
        {
            if (AddressConfig.Count > 0)
            {
                string path = GlobalConfigModel.SelectFolder();
                if (!string.IsNullOrEmpty(path))
                {
                    //查询所有点位
                    List<AddressModel> models = GlobalConfigModel.sqliteOperate.Table<AddressModel>().ToList();
                    FileHandler.StringToFile(Path.Combine(path, $"Address[{DateTime.Now.ToString("yyyyMMddHHmmss")}].json"), models.ToJson());
                    await MessageBox.Show(App.LanguageOperate.GetLanguageValue("导出成功"), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
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

        /// <summary>
        /// 删除地址
        /// </summary>
        public IAsyncRelayCommand DeleteAddress => deleteAddress ??= new AsyncRelayCommand(DeleteAddressAsync);
        private IAsyncRelayCommand? deleteAddress;
        private async Task DeleteAddressAsync()
        {
            List<Snet.Iot.Daq.data.AddressModel> models = AddressConfig.Where(x => x.IsSelected).ToList();
            if (models.Count > 0)
            {
                StringBuilder builder = new StringBuilder();
                foreach (var model in models)
                {
                    if (UseCheck(model))
                    {
                        builder.AppendLine($"{model.Address} - {"地址配置在项目设置中有使用".GetLanguageValue(App.LanguageOperate)}");
                    }
                }
                if (builder.Length > 0)
                {
                    await MessageBox.Show(builder.ToString(), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OKCancel, Windows.Controls.@enum.MessageBoxImage.Warning);
                    return;
                }


                if ((await MessageBox.Show("确认删除选中的地址项吗？".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OKCancel, Windows.Controls.@enum.MessageBoxImage.Question)).ToBool())
                {
                    int failCount = 0;
                    GlobalConfigModel.sqliteOperate.RunInTransaction(() =>
                    {
                        foreach (var item in models)
                        {
                            if (GlobalConfigModel.sqliteOperate.Execute("DELETE FROM AddressModel WHERE [Index] = ?", item.Index) <= 0)
                                failCount++;
                            GlobalConfigModel.AddressDict.Remove(item.Guid, out _);
                        }
                    });
                    await PageIndexChangedExecuteAsync(1);
                    if (failCount > 0)
                    {
                        await MessageBox.Show($"{"存在".GetLanguageValue(App.LanguageOperate)}“{failCount}”{"个点位删除失败".GetLanguageValue(App.LanguageOperate)}", "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
                    }
                    else
                    {
                        await MessageBox.Show($"删除成功".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
                    }

                }
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
            await ResetUiAsync(models.Count, index, models);
        }
        #endregion

        #region 方法
        /// <summary>
        /// 使用检查
        /// </summary>
        /// <returns>false:没有被使用  true:被使用了</returns>
        private bool UseCheck(AddressModel model)
        {
            //检查是否有被使用
            string checkFile = GlobalConfigModel.UI_ProjectConfigPath;
            if (File.Exists(checkFile))
            {
                string content = FileHandler.FileToString(checkFile);
                if (content.Contains(model.Guid))
                {
                    return true;
                }
            }
            return false;
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
            foreach (var item in models.OrderByDescending(x => x.Time).Skip((pageIndex - 1) * PageSize).Take(PageSize))
            {
                AddressConfig.Add(item);
            }
            return Task.CompletedTask;
        }
        #endregion
    }
}
