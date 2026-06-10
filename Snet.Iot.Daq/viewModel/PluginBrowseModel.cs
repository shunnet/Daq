using CommunityToolkit.Mvvm.Input;
using Snet.Core.handler;
using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.handler;
using Snet.Iot.Daq.Core.mvvm;
using Snet.Iot.Daq.data;
using Snet.Model.data;
using Snet.Utility;
using Snet.Windows.Controls.@enum;
using Snet.Windows.Controls.handler;
using Snet.Windows.Controls.message;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Snet.Iot.Daq.viewModel
{
    public class PluginBrowseModel : BindNotify
    {
        public PluginBrowseModel()
        {
            _ = InitAsync();
        }
        /// <summary>
        /// 初始化
        /// </summary>
        /// <returns></returns>
        public async Task InitAsync()
        {
            uiMessage.OnInfoEventAsync += async (object? sender, EventInfoResult e) => Info = e.Message;
            await uiMessage.StartAsync();

            await QueryAsync();
        }

        /// <summary>
        /// ui 消息
        /// </summary>
        private readonly UiMessageHandler uiMessage = UiMessageHandler.Instance("Snet");

        /// <summary>
        /// 插件下载
        /// </summary>
        private PluginDownloadHandler? download;

        /// <summary>
        /// 插件存储路径
        /// </summary>
        private string PluginPath;

        /// <summary>
        /// 更新插件
        /// </summary>
        public IAsyncRelayCommand Update => p_Update ??= new AsyncRelayCommand(UpdateAsync);
        private IAsyncRelayCommand? p_Update;
        public async Task UpdateAsync()
        {
            await MessageBox.Show("正在更新插件，请耐心等待".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, MessageBoxImage.Information);
            var data = await pluginBrowseHandler.GetPluginBrowseDataGridModelsAsync();
            FileHandler.StringToFile(GlobalConfigModel.UI_PluginBrowseCachePath, data.ToJson(true));
            await PageIndexChangedExecuteAsync(1);
            await MessageBox.Show("插件更新完成".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// 停止下载插件
        /// </summary>
        public IAsyncRelayCommand StopDownloadPlugin => p_StopDownloadPlugin ??= new AsyncRelayCommand(StopDownloadPluginAsync);
        private IAsyncRelayCommand? p_StopDownloadPlugin;
        public async Task StopDownloadPluginAsync()
        {
            if (download != null)
            {
                download.Stop();
                await uiMessage.ShowAsync("已停止下载插件".GetLanguageValue(App.LanguageOperate));
            }
        }

        /// <summary>
        /// 下载插件
        /// </summary>
        public IAsyncRelayCommand DownloadPlugin => p_DownloadPlugin ??= new AsyncRelayCommand(DownloadPluginAsync);
        private IAsyncRelayCommand? p_DownloadPlugin;
        public async Task DownloadPluginAsync()
        {
            if (!string.IsNullOrEmpty(PluginPath) && Directory.Exists(PluginPath))
            {
                List<PluginBrowseDataGridModel> selectedModels = Plugins.Where(p => p.IsSelected).ToList();

                if (selectedModels.Count > 0)
                {
                    bool zip = await MessageBox.Show("是否需要打包 ZIP？只有打包成 zip 文件后才能进行上传！".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (download == null)
                    {
                        download = PluginDownloadHandler.Instance(PluginPath);
                        download.OnInfoEventAsync -= Download_OnInfoEventAsync;
                        download.OnInfoEventAsync += Download_OnInfoEventAsync;
                    }
                    await uiMessage.ShowAsync("开始下载插件，请耐心等待".GetLanguageValue(App.LanguageOperate));
                    bool status = await download.DownloadAsync(selectedModels, zip);
                    if (status)
                    {
                        await uiMessage.ShowAsync("插件下载完成".GetLanguageValue(App.LanguageOperate));
                    }
                    else
                    {
                        await uiMessage.ShowAsync("插件下载失败".GetLanguageValue(App.LanguageOperate));
                    }
                }
                else
                {
                    await uiMessage.ShowAsync("请先选择要下载的插件".GetLanguageValue(App.LanguageOperate));
                }
            }
            else
            {
                await uiMessage.ShowAsync("插件存储路径不存在".GetLanguageValue(App.LanguageOperate));
            }
        }

        /// <summary>
        /// 下载的消息
        /// </summary>
        private async Task Download_OnInfoEventAsync(object? sender, EventInfoResult e)
        {
            await uiMessage.ShowAsync(e.Message);
        }


        /// <summary>
        /// 选择插件存储路径
        /// </summary>
        public IAsyncRelayCommand SelectPluginPath => p_SelectPluginPath ??= new AsyncRelayCommand(SelectPluginPathAsync);
        private IAsyncRelayCommand? p_SelectPluginPath;
        public async Task SelectPluginPathAsync()
        {
            string path = GlobalConfigModel.SelectFolder();
            if (!string.IsNullOrEmpty(path))
            {
                PluginPath = path;
                await uiMessage.ShowAsync($"{"已选择插件存储路径：".GetLanguageValue(App.LanguageOperate)}{PluginPath}");
            }
        }

        /// <summary>
        /// 打开插件存储路径
        /// </summary>
        public IAsyncRelayCommand OpenPluginPath => p_OpenPluginPath ??= new AsyncRelayCommand(OpenPluginPathAsync);
        private IAsyncRelayCommand? p_OpenPluginPath;
        public async Task OpenPluginPathAsync()
        {
            if (!string.IsNullOrEmpty(PluginPath) && Directory.Exists(PluginPath))
            {
                Process.Start("explorer.exe", PluginPath);
            }
            else
            {
                await uiMessage.ShowAsync("插件存储路径不存在".GetLanguageValue(App.LanguageOperate));
            }
        }


        /// <summary>
        /// 清空插件存储路径
        /// </summary>
        public IAsyncRelayCommand ClearPluginPath => p_ClearPluginPath ??= new AsyncRelayCommand(ClearPluginPathAsync);
        private IAsyncRelayCommand? p_ClearPluginPath;
        public async Task ClearPluginPathAsync()
        {
            if (!string.IsNullOrEmpty(PluginPath) && Directory.Exists(PluginPath))
            {
                //移除插件存储路径下的所有文件
                try
                {
                    DirectoryInfo directoryInfo = new DirectoryInfo(PluginPath);
                    foreach (FileInfo file in directoryInfo.GetFiles())
                    {
                        file.Delete();
                    }
                    foreach (DirectoryInfo dir in directoryInfo.GetDirectories())
                    {
                        dir.Delete(true);
                    }
                    await uiMessage.ShowAsync($"{"已清空插件存储路径：".GetLanguageValue(App.LanguageOperate)}{PluginPath}");
                }
                catch (Exception ex)
                {
                    await uiMessage.ShowAsync($"{"清空插件存储路径失败：".GetLanguageValue(App.LanguageOperate)}{ex.Message}");
                }
            }
            else
            {
                await uiMessage.ShowAsync("插件存储路径不存在".GetLanguageValue(App.LanguageOperate));
            }
        }

        /// <summary>
        /// 清空信息
        /// </summary>
        public IAsyncRelayCommand Clear => p_Clear ??= new AsyncRelayCommand(ClearAsync);
        private IAsyncRelayCommand? p_Clear;
        public async Task ClearAsync()
        {
            await uiMessage.ClearAsync();
        }

        /// <summary>
        /// 信息框事件
        /// 让滚动条一直处在最下方
        /// </summary>
        public IAsyncRelayCommand InfoTextChanged => p_InfoTextChanged ??= new AsyncRelayCommand<TextChangedEventArgs>(InfoTextChangedAsync);
        IAsyncRelayCommand p_InfoTextChanged;
        public Task InfoTextChangedAsync(TextChangedEventArgs? e)
        {
            System.Windows.Controls.TextBox textBox = e.Source.GetSource<System.Windows.Controls.TextBox>();
            textBox.SelectionStart = textBox.Text.Length;
            textBox.SelectionLength = 0;
            textBox.ScrollToEnd();
            return Task.CompletedTask;
        }

        /// <summary>
        /// 信息事件
        /// </summary>
        public string Info
        {
            get => GetProperty(() => Info);
            set => SetProperty(() => Info, value);
        }

        /// <summary>
        /// 插件浏览模型
        /// </summary>
        private readonly PluginBrowseHandler pluginBrowseHandler = PluginBrowseHandler.Instance("browse");
        private List<PluginBrowseDataGridModel>? allPlugin;
        /// <summary>
        /// 插件集合
        /// </summary>
        public ObservableCollection<PluginBrowseDataGridModel> Plugins
        {
            get => plugins;
            set => SetProperty(ref plugins, value);
        }
        private ObservableCollection<PluginBrowseDataGridModel> plugins = new ObservableCollection<PluginBrowseDataGridModel>();

        /// <summary>
        /// 插件被选中的项
        /// </summary>
        public PluginBrowseDataGridModel PluginsSelectedItem
        {
            get => GetProperty(() => PluginsSelectedItem);
            set => SetProperty(() => PluginsSelectedItem, value);
        }

        /// <summary>
        /// 查询的内容
        /// </summary>
        /// <returns></returns>
        public string QueryContent
        {
            get => GetProperty(() => QueryContent);
            set => SetProperty(() => QueryContent, value);
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
        private int pageSize = 20;

        /// <summary>
        /// 页索引
        /// </summary>
        public int PageIndex
        {
            get => pageIndex;
            set => SetProperty(ref pageIndex, value);
        }
        private int pageIndex = 1;

        /// <summary>
        /// 全选地址
        /// </summary>
        public IAsyncRelayCommand AllSelect => allSelect ??= new AsyncRelayCommand(AllSelectAsync);
        private IAsyncRelayCommand? allSelect;
        private Task AllSelectAsync()
        {
            foreach (var item in Plugins)
            {
                item.IsSelected = true;
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// 反选地址
        /// </summary>
        public IAsyncRelayCommand Inverse => inverse ??= new AsyncRelayCommand(InverseAsync);
        private IAsyncRelayCommand? inverse;
        private Task InverseAsync()
        {
            foreach (var item in Plugins)
            {
                item.IsSelected = !item.IsSelected;
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// 重置界面
        /// </summary>
        /// <param name="total">总数</param>
        /// <param name="pageIndex">页码</param>
        /// <param name="models">数据</param>
        /// <returns></returns>
        private Task ResetUiAsync(int total, int pageIndex, List<PluginBrowseDataGridModel> models)
        {
            PageIndex = pageIndex;
            Total = total;
            Plugins = new ObservableCollection<PluginBrowseDataGridModel>(models.OrderByDescending(x => x.UpdateTime).Skip((pageIndex - 1) * PageSize).Take(PageSize));
            return Task.CompletedTask;
        }

        /// <summary>
        /// 内容菜单打开触发
        /// </summary>
        public IAsyncRelayCommand DataGrid_ContextMenuOpening => dataGrid_ContextMenuOpening ??= new AsyncRelayCommand<ContextMenuEventArgs>(DataGrid_ContextMenuOpeningAsync);
        private IAsyncRelayCommand? dataGrid_ContextMenuOpening;
        private Task DataGrid_ContextMenuOpeningAsync(ContextMenuEventArgs? e)
        {
            if (e?.Source is not DataGrid dataGrid)
                return Task.CompletedTask;

            // 最终裁决：
            // 只要当前不是”行右键”，就禁止弹出
            if (dataGrid.SelectedItem == null)
            {
                e.Handled = true;
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// 鼠标右键点击触发
        /// </summary>
        public IAsyncRelayCommand DataGrid_PreviewMouseRightButtonDown => dataGrid_PreviewMouseRightButtonDown ??= new AsyncRelayCommand<MouseButtonEventArgs>(DataGrid_PreviewMouseRightButtonDownAsync);
        private IAsyncRelayCommand? dataGrid_PreviewMouseRightButtonDown;
        private Task DataGrid_PreviewMouseRightButtonDownAsync(MouseButtonEventArgs? e)
        {
            if (e?.Source is not DataGrid dataGrid)
                return Task.CompletedTask;

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
            return Task.CompletedTask;
        }

        /// <summary>
        /// 当前页
        /// </summary>
        public IAsyncRelayCommand PageIndexChanged => pageIndexChanged ??= new AsyncRelayCommand<int>(PageIndexChangedExecuteAsync);
        private IAsyncRelayCommand? pageIndexChanged;
        private async Task PageIndexChangedExecuteAsync(int index)
        {
            var table = allPlugin ??= await GetNugetPluginAsync();
            int total = table.Count();
            var page = table.OrderByDescending(x => x.UpdateTime)
                            .Skip((index - 1) * PageSize)
                            .Take(PageSize)
                            .ToList();
            PageIndex = index;
            Total = total;
            Plugins = new ObservableCollection<PluginBrowseDataGridModel>(page);
        }

        /// <summary>
        /// 查询地址
        /// </summary>
        public IAsyncRelayCommand Query => query ??= new AsyncRelayCommand(QueryAsync);
        private IAsyncRelayCommand? query;
        private async Task QueryAsync()
        {
            if (QueryContent.IsNullOrWhiteSpace())
            {
                //查询所有
                await PageIndexChangedExecuteAsync(1);
            }
            else
            {
                //模糊查询
                List<PluginBrowseDataGridModel> models = (allPlugin ??= await GetNugetPluginAsync()).Where(p =>
                p.PackName.Contains(QueryContent) ||
                p.Describe.Contains(QueryContent)).ToList();
                if (models.Count > 0)
                {
                    await ResetUiAsync(models.Count, 1, models);
                }
                else
                {
                    await uiMessage.ShowAsync("未查询到对应内容".GetLanguageValue(App.LanguageOperate));
                }
            }
        }

        /// <summary>
        /// 获取插件
        /// </summary>
        /// <returns></returns>
        private async Task<List<PluginBrowseDataGridModel>> GetNugetPluginAsync()
        {
            List<PluginBrowseDataGridModel>? data = null;
            if (File.Exists(GlobalConfigModel.UI_PluginBrowseCachePath))
            {
                data = FileHandler.FileToString(GlobalConfigModel.UI_PluginBrowseCachePath).ToJsonEntity<List<PluginBrowseDataGridModel>>();
            }
            if (data == null)
            {
                data = await pluginBrowseHandler.GetPluginBrowseDataGridModelsAsync();
                FileHandler.StringToFile(GlobalConfigModel.UI_PluginBrowseCachePath, data.ToJson(true));
            }
            return data;
        }
    }
}
