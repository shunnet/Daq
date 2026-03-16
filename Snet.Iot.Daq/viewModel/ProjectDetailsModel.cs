using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using Snet.Core.handler;
using Snet.Iot.Daq.data;
using Snet.Iot.Daq.@enum;
using Snet.Iot.Daq.handler;
using Snet.Model.data;
using Snet.Utility;
using Snet.Windows.Core.mvvm;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Snet.Iot.Daq.viewModel
{
    public partial class ProjectDetailsModel : BindNotify
    {
        #region 属性
        /// <summary>
        /// 父节点的对象<br/>
        /// 用于操作完成赋值
        /// </summary>

        private ProjectTreeViewModel ProjectTree;

        /// <summary>
        /// 父类的集合对象
        /// </summary>
        private ObservableCollection<ProjectTreeViewModel> BossProjectTree;


        /// <summary>
        /// 节点集合
        /// </summary>
        public ObservableCollection<ProjectDetailsTreeViewModel> DetailsNode
        {
            get => detailsNode;
            set => SetProperty(ref detailsNode, value);
        }
        private ObservableCollection<ProjectDetailsTreeViewModel> detailsNode = new ObservableCollection<ProjectDetailsTreeViewModel>();

        /// <summary>
        /// 选中的节点
        /// </summary>
        public ProjectDetailsTreeViewModel? DetailsNodeSelectedItem
        {
            get => GetProperty(() => DetailsNodeSelectedItem);
            set
            {
                _ = ProjectTree.SetAsync(BossProjectTree).ConfigureAwait(false);  //父级保存
                SetProperty(() => DetailsNodeSelectedItem, value);
            }
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
        /// 查询的项
        /// </summary>
        public List<string> SelectItems
        {
            get => _selectItems;
            set => SetProperty(ref _selectItems, value);
        }
        private List<string> _selectItems = new List<string>();
        #endregion

        #region 命令
        /// <summary>
        /// 添加地址
        /// </summary>
        public IAsyncRelayCommand AddAddress => addAddress ??= new AsyncRelayCommand(AddAddressAsync);
        private IAsyncRelayCommand? addAddress;
        private async Task AddAddressAsync()
        {
            GlobalConfigModel.addressModel.SetSelectItem();
            if ((await DialogHost.Show(GlobalConfigModel.address, GlobalConfigModel.DialogHostTag_ClickClose)).ToBool())
            {
                foreach (var item in GlobalConfigModel.addressModel.GetSelectItem())
                {
                    if (!DetailsNode.Any(c => c.AddressDetails.Address == item.Address || c.AddressDetails.AnotherName == item.AnotherName))
                    {
                        DetailsNode.Add(new ProjectDetailsTreeViewModel(item.Guid.GetAddress()));
                    }
                }
                ProjectTree.Details = DetailsNode;  //给父级赋值
                _ = ProjectTree.SetAsync(BossProjectTree).ConfigureAwait(false);  //父级保存
            }
        }

        /// <summary>
        /// 为所有地址添加设备
        /// </summary>
        public IAsyncRelayCommand AllAddressAddDevice => allAddressAddDevice ??= new AsyncRelayCommand(AllAddressAddDeviceAsync);
        private IAsyncRelayCommand? allAddressAddDevice;
        private async Task AllAddressAddDeviceAsync()
        {
            GlobalConfigModel.deviceModel.SetValue(PluginType.Mq);
            if ((await DialogHost.Show(GlobalConfigModel.device, GlobalConfigModel.DialogHostTag_ClickClose)).ToBool())
            {
                PluginConfigModel plugin = GlobalConfigModel.deviceModel.GetValue();
                foreach (var items in DetailsNode)
                {
                    ProjectDetailsTreeViewModel item = new ProjectDetailsTreeViewModel(plugin);
                    if (!items?.Children.Any(c => c.MqDetails.SN == item.MqDetails.SN) ?? true)
                    {
                        items?.Children.Add(item);
                    }
                }
                if (DetailsNode.Count > 0)
                {
                    DetailsNode.EnsureSingleSelection();
                    _ = DetailsNode[DetailsNode.Count - 1].SetAsync(DetailsNode).ConfigureAwait(false);
                }
                ProjectTree.Details = DetailsNode;  //给父级赋值
                _ = ProjectTree.SetAsync(BossProjectTree).ConfigureAwait(false);  //父级保存
            }
        }

        /// <summary>
        /// 添加设备
        /// </summary>
        public IAsyncRelayCommand AddDevice => addDevice ??= new AsyncRelayCommand(AddDeviceAsync);
        private IAsyncRelayCommand? addDevice;
        private async Task AddDeviceAsync()
        {
            GlobalConfigModel.deviceModel.SetValue(PluginType.Mq);
            if ((await DialogHost.Show(GlobalConfigModel.device, GlobalConfigModel.DialogHostTag_ClickClose)).ToBool())
            {

                PluginConfigModel plugin = GlobalConfigModel.deviceModel.GetValue().Guid.GetPlugin();
                ProjectDetailsTreeViewModel item = new ProjectDetailsTreeViewModel(plugin);

                if (DetailsNodeSelectedItem?.Children.Any(c => c.MqDetails.SN == item.MqDetails.SN) == false)
                {
                    DetailsNodeSelectedItem?.Children.Add(item);  //选中的节点添加子集
                    DetailsNodeSelectedItem = item; //在把子集设置为选中的节点
                    _ = DetailsNodeSelectedItem?.SetAsync(DetailsNode).ConfigureAwait(false);

                    ProjectTree.Details = DetailsNode;  //给父级赋值
                    _ = ProjectTree.SetAsync(BossProjectTree).ConfigureAwait(false);  //父级保存
                }
                else
                {
                    await Windows.Controls.message.MessageBox.Show("此设备已添加！".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 展开所有项目
        /// </summary>
        public IAsyncRelayCommand ExpandAllProject => expandAllProject ??= new AsyncRelayCommand(ExpandAllProjectAsync);
        private IAsyncRelayCommand? expandAllProject;
        private async Task ExpandAllProjectAsync()
        {
            DetailsNode.IsExpandedAll(true);
        }

        /// <summary>
        /// 折叠所有项目
        /// </summary>
        public IAsyncRelayCommand CollapseAllProject => collapseAllProject ??= new AsyncRelayCommand(CollapseAllProjectAsync);
        private IAsyncRelayCommand? collapseAllProject;
        private async Task CollapseAllProjectAsync()
        {
            DetailsNode.IsExpandedAll(false);
        }

        /// <summary>
        /// 展开选中项目的所有子项目
        /// </summary>
        public IAsyncRelayCommand ExpandProject => expandProject ??= new AsyncRelayCommand(ExpandProjectAsync);
        private IAsyncRelayCommand? expandProject;
        private async Task ExpandProjectAsync()
        {
            DetailsNodeSelectedItem?.SetExpandedRecursive(true);
        }

        /// <summary>
        /// 折叠选中项目的所有子项目
        /// </summary>
        public IAsyncRelayCommand CollapseProject => collapseProject ??= new AsyncRelayCommand(CollapseProjectAsync);
        private IAsyncRelayCommand? collapseProject;
        private async Task CollapseProjectAsync()
        {
            DetailsNodeSelectedItem?.SetExpandedRecursive(false);
        }

        /// <summary>
        /// 移除
        /// </summary>
        public IAsyncRelayCommand Remove => remove ??= new AsyncRelayCommand(RemoveAsync);
        private IAsyncRelayCommand? remove;
        private async Task RemoveAsync()
        {
            if (DetailsNodeSelectedItem != null)
            {
                if (await Windows.Controls.message.MessageBox.Show("确定移除选中的项吗？".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OKCancel, Windows.Controls.@enum.MessageBoxImage.Question))
                {
                    //拷贝
                    ProjectDetailsTreeViewModel? Parent = DetailsNodeSelectedItem?.Parent;
                    //移除
                    DetailsNode.RemoveNode(DetailsNodeSelectedItem);
                    //更新特殊数据
                    Parent?.UpdateSpecialData();
                    // 清空选中项，防止绑定未更新
                    DetailsNodeSelectedItem = null;
                    //保存配置
                    ProjectTree.Details = DetailsNode;  //给父级赋值
                    _ = ProjectTree.SetAsync(BossProjectTree).ConfigureAwait(false);  //父级保存
                    await Windows.Controls.message.MessageBox.Show("移除成功".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
                }
            }
        }


        /// <summary>
        /// 移除所有
        /// </summary>
        public IAsyncRelayCommand RemoveAll => removeAll ??= new AsyncRelayCommand(RemoveAllAsync);
        private IAsyncRelayCommand? removeAll;
        private async Task RemoveAllAsync()
        {
            if (DetailsNodeSelectedItem != null)
            {
                if (await Windows.Controls.message.MessageBox.Show("确定移除所有项吗？".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OKCancel, Windows.Controls.@enum.MessageBoxImage.Question))
                {
                    DetailsNode.Clear();
                    // 清空选中项，防止绑定未更新
                    DetailsNodeSelectedItem = null;
                    //保存配置
                    ProjectTree.Details = DetailsNode;  //给父级赋值
                    _ = ProjectTree.SetAsync(BossProjectTree).ConfigureAwait(false);  //父级保存
                    await Windows.Controls.message.MessageBox.Show("移除成功".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
                }
            }
        }

        /// <summary>
        /// 读取
        /// </summary>
        public IAsyncRelayCommand Read => read ??= new AsyncRelayCommand(ReadAsync);
        private IAsyncRelayCommand? read;
        public async Task ReadAsync()
        {
            AddressModel address = DetailsNodeSelectedItem.AddressDetails;
            PluginConfigModel daq = ProjectTree.DaqDetails;
            OperateResult result = await address.TestReadAddressAsync(daq);
            if (result.GetDetails(out string? msg, out ConcurrentDictionary<string, AddressValue>? data))
            {
                AddressValue value = data[address.Address];
                if (DetailsNodeSelectedItem.Children.Count > 0)
                {
                    StringBuilder error_strs = new StringBuilder();
                    error_strs.AppendLine("存在传输失败数据如下".GetLanguageValue(App.LanguageOperate));
                    foreach (var item in DetailsNodeSelectedItem.Children)
                    {
                        string desName = $"{item.Name}{item.SpecialData}";  //详细名称
                        PluginConfigModel mq = item.MqDetails;
                        result = await address.TestTransmitDataAsync(mq, value);
                        if (!result.GetDetails(out msg))
                        {
                            error_strs.AppendLine();
                            error_strs.AppendLine($"{desName} - {"传输失败".GetLanguageValue(App.LanguageOperate)}");
                            error_strs.AppendLine(msg);
                        }
                    }
                    if (error_strs.Length > 1)
                    {
                        await Windows.Controls.message.MessageBox.Show($"{"读取成功".GetLanguageValue(App.LanguageOperate)}\r\n{value.AddressName}\r\n{value.ResultValue}\r\n\r\n{error_strs}", "结果".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
                    }
                    else
                    {
                        await Windows.Controls.message.MessageBox.Show($"{"读取成功".GetLanguageValue(App.LanguageOperate)}\r\n{value.AddressName}\r\n{value.ResultValue}\r\n\r\n{"传输成功".GetLanguageValue(App.LanguageOperate)}", "结果".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
                    }
                }
                else
                {
                    await Windows.Controls.message.MessageBox.Show($"{"读取成功".GetLanguageValue(App.LanguageOperate)}\r\n{value.AddressName}\r\n{value.ResultValue}", "结果".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
                }
            }
            else
            {
                await Windows.Controls.message.MessageBox.Show($"{"读取失败".GetLanguageValue(App.LanguageOperate)}:{msg}", "结果".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 写入
        /// </summary>
        public IAsyncRelayCommand Write => write ??= new AsyncRelayCommand(WriteAsync);
        private IAsyncRelayCommand? write;
        public async Task WriteAsync()
        {
            GlobalConfigModel.param.SetBasics(new WriteModel() { AddressDataType = DetailsNodeSelectedItem.AddressDetails.Type, EncodingType = DetailsNodeSelectedItem.AddressDetails.EncodingType });
            if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
            {
                WriteModel write = GlobalConfigModel.param.GetBasics().GetSource<WriteModel>();
                AddressModel address = DetailsNodeSelectedItem.AddressDetails;
                PluginConfigModel daq = ProjectTree.DaqDetails;
                OperateResult result = await address.TestWriteAddressAsync(daq, write);
                await Windows.Controls.message.MessageBox.Show($"{"写入".GetLanguageValue(App.LanguageOperate)}{(result.Status ? "成功".GetLanguageValue(App.LanguageOperate) : "失败".GetLanguageValue(App.LanguageOperate) + $":{result.Message}")}", "结果".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, result.Status ? Windows.Controls.@enum.MessageBoxImage.Information : Windows.Controls.@enum.MessageBoxImage.Error);
            }
        }
        #endregion

        #region 界面事件
        /// <summary>
        /// 内容菜单打开触发
        /// </summary>
        public IAsyncRelayCommand TreeView_ContextMenuOpening => treeView_ContextMenuOpening ??= new AsyncRelayCommand<ContextMenuEventArgs>(TreeView_ContextMenuOpeningAsync);
        private IAsyncRelayCommand? treeView_ContextMenuOpening;
        private async Task TreeView_ContextMenuOpeningAsync(ContextMenuEventArgs? e)
        {
            if (e?.Source is not TreeView treeView)
                return;

            // 最终裁决：
            // 只要当前不是“行右键”，就禁止弹出
            if (treeView.SelectedItem == null)
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// 鼠标右键点击触发
        /// </summary>
        public IAsyncRelayCommand TreeView_PreviewMouseRightButtonDown => treeView_PreviewMouseRightButtonDown ??= new AsyncRelayCommand<MouseButtonEventArgs>(TreeView_PreviewMouseRightButtonDownAsync);
        private IAsyncRelayCommand? treeView_PreviewMouseRightButtonDown;
        private async Task TreeView_PreviewMouseRightButtonDownAsync(MouseButtonEventArgs? e)
        {
            if (e?.OriginalSource is not DependencyObject source)
                return;

            System.Windows.DependencyObject dep = (System.Windows.DependencyObject)e.OriginalSource;

            while (dep != null && dep is not TreeViewItem)
                dep = VisualTreeHelper.GetParent(dep);

            if (dep is TreeViewItem treeItem)
            {
                if (treeItem != null)
                {
                    ProjectDetailsTreeViewModel? model = treeItem?.DataContext.GetSource<ProjectDetailsTreeViewModel>();
                    DetailsNodeSelectedItem = model;
                    _ = DetailsNodeSelectedItem?.SetAsync(DetailsNode).ConfigureAwait(false);
                }
                treeItem?.Focus();
                e.Handled = true;
            }
            else
            {
                DetailsNode.EnsureSingleSelection();
                DetailsNodeSelectedItem = null;
                e.Handled = true; // 阻止默认右键
            }
        }

        /// <summary>
        /// 选中触发
        /// </summary>
        public IAsyncRelayCommand TreeView_SelectedItemChanged => treeView_SelectedItemChanged ??= new AsyncRelayCommand<RoutedPropertyChangedEventArgs<object>>(TreeView_SelectedItemChangedAsync);
        private IAsyncRelayCommand? treeView_SelectedItemChanged;
        private async Task TreeView_SelectedItemChangedAsync(RoutedPropertyChangedEventArgs<object>? e)
        {
            if (e?.NewValue is not ProjectDetailsTreeViewModel model)
                return;
            _ = model?.SetAsync(DetailsNode).ConfigureAwait(false);
        }

        /// <summary>
        /// 文本触发
        /// </summary>
        public IAsyncRelayCommand AutoSuggestBox_TextChanged => autoSuggestBox_TextChanged ??= new AsyncRelayCommand<object>(AutoSuggestBox_TextChangedAsync);
        private IAsyncRelayCommand? autoSuggestBox_TextChanged;
        private async Task AutoSuggestBox_TextChangedAsync(object? sender)
        {
            SelectItems = DetailsNode.GetAllNames(false);
        }

        /// <summary>
        /// 查询内容触发
        /// </summary>
        public IAsyncRelayCommand AutoSuggestBox_QuerySubmitted => autoSuggestBox_QuerySubmitted ??= new AsyncRelayCommand<object>(AutoSuggestBox_QuerySubmittedAsync);
        private IAsyncRelayCommand? autoSuggestBox_QuerySubmitted;
        private async Task AutoSuggestBox_QuerySubmittedAsync(object? sender)
        {
            await QueryProjectAsync();
        }

        /// <summary>
        /// 查询触发
        /// </summary>
        public IAsyncRelayCommand AutoSuggestBox_SuggestionChosen => autoSuggestBox_SuggestionChosen ??= new AsyncRelayCommand<object>(AutoSuggestBox_SuggestionChosenAsync);
        private IAsyncRelayCommand? autoSuggestBox_SuggestionChosen;
        private async Task AutoSuggestBox_SuggestionChosenAsync(object? sender)
        {
            QueryCntent = sender.GetSource<Wpf.Ui.Controls.AutoSuggestBoxSuggestionChosenEventArgs>().SelectedItem.ToString();
            await QueryProjectAsync();
        }
        #endregion

        #region 方法
        /// <summary>
        /// 查询内容
        /// </summary>
        /// <returns></returns>
        private async Task QueryProjectAsync()
        {
            if (!QueryCntent.IsNullOrWhiteSpace())
            {
                ProjectDetailsTreeViewModel? project = DetailsNode.FindByName(QueryCntent);
                if (project is not null)
                {
                    _ = project?.SetAsync(DetailsNode).ConfigureAwait(false);
                }
                else
                {
                    await Windows.Controls.message.MessageBox.Show("未检索到对应项".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 设置参数
        /// </summary>
        /// <param name="details">设备详情</param>
        /// <param name="bossProject">父类的设备集合</param>
        /// <param name="project">项目</param>
        public void SetValue(ProjectTreeViewModel project, ObservableCollection<ProjectTreeViewModel> bossProject)
        {
            this.BossProjectTree = bossProject;
            this.ProjectTree = project;
            if (project != null)
            {
                if (project.Details != null && project.Details.Count > 0)
                {
                    DetailsNode = new ObservableCollection<ProjectDetailsTreeViewModel>(project.Details);
                    DetailsNode.RebindGlobals();
                }
            }
        }
        #endregion

    }
}
