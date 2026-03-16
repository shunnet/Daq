using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using Snet.Core.handler;
using Snet.Iot.Daq.data;
using Snet.Iot.Daq.@enum;
using Snet.Iot.Daq.handler;
using Snet.Iot.Daq.view;
using Snet.Utility;
using Snet.Windows.Core.mvvm;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Snet.Iot.Daq.viewModel
{
    public class ProjectSettingsModel : BindNotify
    {
        #region 构造函数
        public ProjectSettingsModel()
        {
            _ = InitAsync();
        }
        #endregion

        #region 属性
        /// <summary>
        /// 节点集合
        /// </summary>
        public ObservableCollection<ProjectTreeViewModel> ProjectNode
        {
            get => projectNode;
            set => SetProperty(ref projectNode, value);
        }
        private ObservableCollection<ProjectTreeViewModel> projectNode = new ObservableCollection<ProjectTreeViewModel>();

        /// <summary>
        /// 选中的节点
        /// </summary>
        public ProjectTreeViewModel? ProjectNodeSelectedItem
        {
            get => GetProperty(() => ProjectNodeSelectedItem);
            set => SetProperty(() => ProjectNodeSelectedItem, value);
        }

        /// <summary>
        /// 显示隐藏
        /// </summary>
        public Visibility TabControlVisibility
        {
            get => tabControlVisibility;
            set => SetProperty(ref tabControlVisibility, value);
        }
        private Visibility tabControlVisibility = Visibility.Collapsed;

        /// <summary>
        /// 详情节点
        /// </summary>
        public ObservableCollection<ProjectDetailsTabControlModel> DetailsNode
        {
            get => detailsNode;
            set => SetProperty(ref detailsNode, value);
        }
        private ObservableCollection<ProjectDetailsTabControlModel> detailsNode = new ObservableCollection<ProjectDetailsTabControlModel>();

        /// <summary>
        /// 设备选中的节点
        /// </summary>
        public ProjectDetailsTabControlModel DetailsNodeSelectedItem
        {
            get => GetProperty(() => DetailsNodeSelectedItem);
            set => SetProperty(() => DetailsNodeSelectedItem, value);
            //{
            //    // 如果选中的项与当前的第一项不同
            //    if (detailsNode.Contains(value) && !ReferenceEquals(detailsNode[0], value))
            //    {
            //        // 移除原来的位置
            //        detailsNode.Remove(value);

            //        // 插入到第一个位置
            //        detailsNode.Insert(0, value);
            //    }

            //    SetProperty(() => DetailsNodeSelectedItem, value);
            //}
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
        /// 展开所有项目
        /// </summary>
        public IAsyncRelayCommand ExpandAllProject => expandAllProject ??= new AsyncRelayCommand(ExpandAllProjectAsync);
        private IAsyncRelayCommand? expandAllProject;
        private async Task ExpandAllProjectAsync()
        {
            ProjectNode.IsExpandedAll(true);
        }

        /// <summary>
        /// 折叠所有项目
        /// </summary>
        public IAsyncRelayCommand CollapseAllProject => collapseAllProject ??= new AsyncRelayCommand(CollapseAllProjectAsync);
        private IAsyncRelayCommand? collapseAllProject;
        private async Task CollapseAllProjectAsync()
        {
            ProjectNode.IsExpandedAll(false);
        }

        /// <summary>
        /// 展开选中项目的所有子项目
        /// </summary>
        public IAsyncRelayCommand ExpandProject => expandProject ??= new AsyncRelayCommand(ExpandProjectAsync);
        private IAsyncRelayCommand? expandProject;
        private async Task ExpandProjectAsync()
        {
            ProjectNodeSelectedItem?.SetExpandedRecursive(true);
        }

        /// <summary>
        /// 折叠选中项目的所有子项目
        /// </summary>
        public IAsyncRelayCommand CollapseProject => collapseProject ??= new AsyncRelayCommand(CollapseProjectAsync);
        private IAsyncRelayCommand? collapseProject;
        private async Task CollapseProjectAsync()
        {
            ProjectNodeSelectedItem?.SetExpandedRecursive(false);
        }

        /// <summary>
        /// 添加顶级项
        /// </summary>
        public IAsyncRelayCommand AddTopProject => addTopProject ??= new AsyncRelayCommand(AddTopProjectAsync);
        private IAsyncRelayCommand? addTopProject;
        private async Task AddTopProjectAsync()
        {
            ProjectTreeViewModel project = new ProjectTreeViewModel(string.Empty);
            GlobalConfigModel.param.SetBasics(project);
            if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
            {
                ProjectTreeViewModel item = GlobalConfigModel.param.GetBasics().GetSource<ProjectTreeViewModel>();
                if (item.Name.IsNullOrWhiteSpace())
                {
                    await Windows.Controls.message.MessageBox.Show("存在空数据！".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
                    return;
                }
                ProjectNode.Add(item);
                ProjectNodeSelectedItem = item;
                await ProjectNodeSelectedItem?.SetAsync(ProjectNode);
                await GlobalConfigModel.RefreshAsync();
            }
        }

        /// <summary>
        /// 导出项目
        /// </summary>
        public IAsyncRelayCommand ExportProject => exportProject ??= new AsyncRelayCommand(ExportProjectAsync);
        private IAsyncRelayCommand? exportProject;
        private async Task ExportProjectAsync()
        {
            if (ProjectNode.Count > 0)
            {
                string path = GlobalConfigModel.SelectFolder();
                if (!string.IsNullOrEmpty(path))
                {
                    FileHandler.StringToFile(Path.Combine(path, $"Project[{DateTime.Now.ToString("yyyyMMddHHmmss")}].json"), ProjectNode.ToJson());
                    await Windows.Controls.message.MessageBox.Show(App.LanguageOperate.GetLanguageValue("导出成功"), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
                }
            }
        }

        /// <summary>
        /// 导入项目
        /// </summary>
        public IAsyncRelayCommand ImportProject => importProject ??= new AsyncRelayCommand(ImportProjectAsync);
        private IAsyncRelayCommand? importProject;
        private async Task ImportProjectAsync()
        {
            string file = GlobalConfigModel.SelectFiles("json");
            if (!string.IsNullOrEmpty(file))
            {
                ObservableCollection<ProjectTreeViewModel>? models = FileHandler.FileToString(file).ToJsonEntity<ObservableCollection<ProjectTreeViewModel>>();
                if (models == null)
                {
                    await Windows.Controls.message.MessageBox.Show("导入失败".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
                }
                else
                {
                    if (ProjectNode.Count > 0)
                    {
                        if (!await Windows.Controls.message.MessageBox.Show("确定覆盖当前项目列表吗？".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OKCancel, Windows.Controls.@enum.MessageBoxImage.Information))
                        {
                            return;
                        }
                    }

                    //设置父级关系
                    models.InitChildrenParent();
                    ProjectNode.Clear();
                    ProjectNode = models;
                    ProjectNodeSelectedItem = ProjectHandler.GetFirstSelectItem(ProjectNode);
                    if (ProjectNodeSelectedItem?.NodeType == ProjectNodeType.Device)
                    {
                        DetailsNode.Clear();
                        _ = AddSelectDeviceNodeAsync(ProjectNodeSelectedItem).ConfigureAwait(false);
                    }
                    //保存配置
                    await ProjectHandler.SaveConfigAsync(ProjectNode, GlobalConfigModel.UI_ProjectConfigPath);
                    await Windows.Controls.message.MessageBox.Show("导入成功".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
                }
            }
        }

        /// <summary>
        /// 添加项目
        /// </summary>
        public IAsyncRelayCommand AddProject => addProject ??= new AsyncRelayCommand(AddProjectAsync);
        private IAsyncRelayCommand? addProject;
        private async Task AddProjectAsync()
        {
            ProjectTreeViewModel project = new ProjectTreeViewModel(string.Empty);
            GlobalConfigModel.param.SetBasics(project);
            if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
            {
                ProjectTreeViewModel item = GlobalConfigModel.param.GetBasics().GetSource<ProjectTreeViewModel>();
                if (item.Name.IsNullOrWhiteSpace())
                {
                    await Windows.Controls.message.MessageBox.Show("存在空数据！".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
                    return;
                }
                ProjectNodeSelectedItem?.Children.Add(item);  //选中的节点添加子集
                ProjectNodeSelectedItem = item; //在把子集设置为选中的节点
                await ProjectNodeSelectedItem?.SetAsync(ProjectNode);
                await GlobalConfigModel.RefreshAsync();
            }
        }

        /// <summary>
        /// 修改项目
        /// </summary>
        public IAsyncRelayCommand UpdateProject => updateProject ??= new AsyncRelayCommand(UpdateProjectAsync);
        private IAsyncRelayCommand? updateProject;
        private async Task UpdateProjectAsync()
        {
            GlobalConfigModel.param.SetBasics(ProjectNodeSelectedItem);
            if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
            {
                ProjectTreeViewModel item = GlobalConfigModel.param.GetBasics().GetSource<ProjectTreeViewModel>();
                if (item.Name.IsNullOrWhiteSpace())
                {
                    await Windows.Controls.message.MessageBox.Show("存在空数据！".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
                    return;
                }
                await ProjectNodeSelectedItem?.SetAsync(ProjectNode);
                await GlobalConfigModel.RefreshAsync();
            }
        }

        /// <summary>
        /// 添加设备
        /// </summary>
        public IAsyncRelayCommand AddDevice => addDevice ??= new AsyncRelayCommand(AddDeviceAsync);
        private IAsyncRelayCommand? addDevice;
        private async Task AddDeviceAsync()
        {
            GlobalConfigModel.deviceModel.SetValue(PluginType.Daq);
            if ((await DialogHost.Show(GlobalConfigModel.device, GlobalConfigModel.DialogHostTag_ClickClose)).ToBool())
            {
                PluginConfigModel plugin = GlobalConfigModel.deviceModel.GetValue().Guid.GetPlugin();
                ProjectTreeViewModel item = new ProjectTreeViewModel(plugin);

                if (ProjectNode.QueryDeviceUnique(item))
                {
                    ProjectNodeSelectedItem?.Children.Add(item);  //选中的节点添加子集
                    ProjectNodeSelectedItem = item; //在把子集设置为选中的节点
                    await ProjectNodeSelectedItem?.SetAsync(ProjectNode);
                    _ = AddSelectDeviceNodeAsync(ProjectNodeSelectedItem).ConfigureAwait(false);
                    await GlobalConfigModel.RefreshAsync();
                }
                else
                {
                    await Windows.Controls.message.MessageBox.Show("此设备已添加！".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 移除
        /// </summary>
        public IAsyncRelayCommand Remove => remove ??= new AsyncRelayCommand(RemoveAsync);
        private IAsyncRelayCommand? remove;
        private async Task RemoveAsync()
        {
            if (ProjectNodeSelectedItem != null)
            {
                if (await Windows.Controls.message.MessageBox.Show("确定移除选中的项吗？".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OKCancel, Windows.Controls.@enum.MessageBoxImage.Question))
                {
                    //拷贝
                    ProjectTreeViewModel? Parent = ProjectNodeSelectedItem?.Parent;
                    //移除
                    ProjectNode.RemoveNode(ProjectNodeSelectedItem);
                    //更新特殊数据
                    Parent?.UpdateSpecialData();
                    //清理tab
                    _ = RemoveSelectDeviceNodeAsync(ProjectNodeSelectedItem).ConfigureAwait(false);
                    // 清空选中项，防止绑定未更新
                    ProjectNodeSelectedItem = null;
                    //保存配置
                    await ProjectHandler.SaveConfigAsync(ProjectNode, GlobalConfigModel.UI_ProjectConfigPath);
                    await Windows.Controls.message.MessageBox.Show("移除成功".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
                }
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
                    ProjectTreeViewModel? model = treeItem?.DataContext.GetSource<ProjectTreeViewModel>();
                    ProjectNodeSelectedItem = model;
                    _ = ProjectNodeSelectedItem?.SetAsync(ProjectNode).ConfigureAwait(false);
                }
                treeItem?.Focus();
                e.Handled = true;
            }
            else
            {
                ProjectNode.EnsureSingleSelection();
                ProjectNodeSelectedItem = null;
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
            if (e?.NewValue is not ProjectTreeViewModel model)
                return;
            _ = model?.SetAsync(ProjectNode).ConfigureAwait(false);
            if (model?.NodeType == ProjectNodeType.Device)
            {
                (ProjectDetails view, ProjectDetailsModel model) details = ProjectNode.CreateDetails(model);
                _ = AddSelectDeviceNodeAsync(model).ConfigureAwait(false);
            }
        }



        /// <summary>
        /// 文本触发
        /// </summary>
        public IAsyncRelayCommand AutoSuggestBox_TextChanged => autoSuggestBox_TextChanged ??= new AsyncRelayCommand<object>(AutoSuggestBox_TextChangedAsync);
        private IAsyncRelayCommand? autoSuggestBox_TextChanged;
        private async Task AutoSuggestBox_TextChangedAsync(object? sender)
        {
            SelectItems = ProjectNode.GetAllNames();
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

        /// <summary>
        /// 关闭 tab 项
        /// </summary>
        public IAsyncRelayCommand CloseTabCommand => closeTabCommand ??= new AsyncRelayCommand<ProjectDetailsTabControlModel>(CloseTabCommandAsync);
        private IAsyncRelayCommand? closeTabCommand;
        private async Task CloseTabCommandAsync(ProjectDetailsTabControlModel? tab)
        {
            if (tab != null)
            {
                DetailsNode.Remove(tab);
                if (DetailsNode.Count == 0)
                {
                    TabControlVisibility = Visibility.Collapsed;
                }
            }
        }

        /// <summary>
        /// tab 项被选中触发
        /// </summary>
        public IAsyncRelayCommand TabControl_SelectionChanged => tabControl_SelectionChanged ??= new AsyncRelayCommand<SelectionChangedEventArgs>(TabControl_SelectionChangedAsync);
        private IAsyncRelayCommand? tabControl_SelectionChanged;
        private async Task TabControl_SelectionChangedAsync(SelectionChangedEventArgs? e)
        {
            if (e?.OriginalSource is not TabControl tabControl)
                return;
            ProjectDetailsTabControlModel selectedItem = tabControl.SelectedItem.GetSource<ProjectDetailsTabControlModel>();
            if (selectedItem is null)
                return;
            var select = ProjectNode.FindByDaqGuid(selectedItem.DaqDetails);
            if (select is null)
                return;
            ProjectNode.EnsureSingleSelection(select);
        }


        #endregion

        #region 方法
        /// <summary>
        /// 初始化
        /// </summary>
        /// <returns></returns>
        private async Task InitAsync()
        {
            ProjectNode = GlobalConfigModel.ProjectDict;

            ProjectNodeSelectedItem = ProjectHandler.GetFirstSelectItem(ProjectNode);
            if (ProjectNodeSelectedItem?.NodeType == ProjectNodeType.Device)
                await AddSelectDeviceNodeAsync(ProjectNodeSelectedItem).ConfigureAwait(false);
        }
        /// <summary>
        /// 移除设备节点
        /// </summary>
        /// <param name="model">模型</param>
        private async Task RemoveSelectDeviceNodeAsync(ProjectTreeViewModel model)
        {
            List<ProjectDetailsTabControlModel> device;
            if (model.DaqDetails != null)
            {
                device = DetailsNode.Where(c => c.DaqDetails.Guid == model.DaqDetails.Guid).ToList();
            }
            else
            {
                device = DetailsNode.Where(c => c.Header == model.Name).ToList();
            }

            if (device.Count > 0)
            {
                DetailsNode.Remove(device[0]);
            }
            if (model.Children.Count > 0)
            {
                foreach (var item in model.Children)
                {
                    await RemoveSelectDeviceNodeAsync(item).ConfigureAwait(false);
                }
            }
            if (DetailsNode.Count == 0)
            {
                TabControlVisibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 添加并选中设备节点
        /// </summary>
        /// <param name="model">模型</param>
        public async Task AddSelectDeviceNodeAsync(ProjectTreeViewModel project)
        {
            (ProjectDetails view, ProjectDetailsModel model) details = ProjectNode.CreateDetails(project);
            ProjectDetailsTabControlModel tabModel = new ProjectDetailsTabControlModel(project.DaqDetails.Guid.GetPlugin(), details.view);
            var existing = DetailsNode.FirstOrDefault(c => c.DaqDetails.Guid == tabModel.DaqDetails.Guid);
            if (existing != null)
            {
                DetailsNodeSelectedItem = existing;
            }
            else
            {
                DetailsNode.Add(tabModel);
                DetailsNodeSelectedItem = tabModel;
            }
            TabControlVisibility = Visibility.Visible;
        }

        /// <summary>
        /// 查询内容
        /// </summary>
        /// <returns></returns>
        private async Task QueryProjectAsync()
        {
            if (!QueryCntent.IsNullOrWhiteSpace())
            {
                ProjectTreeViewModel? project = ProjectNode.FindByName(QueryCntent);
                if (project is not null)
                {
                    _ = project?.SetAsync(ProjectNode).ConfigureAwait(false);
                }
                else
                {
                    await Windows.Controls.message.MessageBox.Show("未检索到对应项".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
                }
            }
        }
        #endregion
    }
}
