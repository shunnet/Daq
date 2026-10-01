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
    /// <summary>管理插件元数据缓存、分页及下载；拥有初始化任务和取消源，在应用退出时等待 I/O 清理。</summary>
    public class PluginBrowseModel : BindNotify, IAsyncDisposable
    {
        public PluginBrowseModel()
        {
            initializationTask = InitializeObservedAsync();
        }
        /// <summary>
        /// 初始化
        /// </summary>
        /// <returns></returns>
        public Task InitAsync() => initializationTask;

        /// <summary>在初始化失败或退出取消时观察任务结果，不让构造函数启动的任务成为未观察异常。</summary>
        private async Task InitializeObservedAsync()
        {
            try { await InitializeCoreAsync(); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception ex) { Snet.Log.LogHelper.Error($"插件浏览初始化失败: {ex.Message}"); }
        }

        /// <summary>启动消息处理器并读取缓存或仓库，整个过程由初始化任务拥有。</summary>
        private async Task InitializeCoreAsync()
        {
            uiMessage.OnInfoEventAsync += UiMessage_OnInfoEventAsync;
            await uiMessage.StartAsync();
            await QueryAsync();
            await uiMessage.ShowAsync("插件默认路径：".GetLanguageValue(App.LanguageOperate) + PluginPath);
        }

        /// <summary>页面终止时取消初始化、元数据读取和下载；每个资源由本模型拥有。</summary>
        private readonly CancellationTokenSource lifetime = new();
        private readonly Task initializationTask;
        private readonly object disposalGate = new();
        private Task? disposalTask;
        private int stopping;

        /// <summary>把消息处理器事件转换为界面文本；使用可准确退订的命名委托。</summary>
        private Task UiMessage_OnInfoEventAsync(object? sender, EventInfoResult e)
        {
            if (Volatile.Read(ref stopping) == 0) Info = e.Message;
            return Task.CompletedTask;
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
        private string PluginPath = Path.Combine(AppContext.BaseDirectory, "lib");

        /// <summary>
        /// 更新插件
        /// </summary>
        public IAsyncRelayCommand Update => p_Update ??= new AsyncRelayCommand(UpdateAsync);
        private IAsyncRelayCommand? p_Update;
        /// <summary>取消可传播的元数据刷新；成功前保持原缓存，退出取消不报告为操作失败。</summary>
        public async Task UpdateAsync()
        {
            if (Volatile.Read(ref stopping) != 0) return;
            try
            {
                await uiMessage.ShowAsync("正在更新插件，请耐心等待".GetLanguageValue(App.LanguageOperate));
                var data = await pluginBrowseHandler.GetPluginBrowseDataGridModelsAsync(cancellationToken: lifetime.Token);
                lifetime.Token.ThrowIfCancellationRequested();
                await File.WriteAllTextAsync(GlobalConfigModel.UI_PluginBrowseCachePath, data.ToJson(true), lifetime.Token);
                allPlugin = data;
                await PageIndexChangedExecuteAsync(1);
                await uiMessage.ShowAsync("插件更新完成".GetLanguageValue(App.LanguageOperate));
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception ex) { await uiMessage.ShowAsync(ex.Message); }
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
            if (Volatile.Read(ref stopping) != 0) return;
            if (!string.IsNullOrEmpty(PluginPath) && Directory.Exists(PluginPath))
            {
                List<PluginBrowseDataGridModel> selectedModels = Plugins.Where(p => p.IsSelected).ToList();

                if (selectedModels.Count > 0)
                {
                    bool zip = await MessageBox.Show("是否需要打包 ZIP？只有打包成 zip 文件后才能进行上传！".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (Volatile.Read(ref stopping) != 0) return;
                    if (download == null)
                    {
                        download = PluginDownloadHandler.Instance(PluginPath);
                        download.OnInfoEventAsync -= Download_OnInfoEventAsync;
                        download.OnInfoEventAsync += Download_OnInfoEventAsync;
                    }
                    await uiMessage.ShowAsync("开始下载插件，请耐心等待".GetLanguageValue(App.LanguageOperate));
                    bool status = await download.DownloadAsync(selectedModels, zip, lifetime.Token);
                    if (Volatile.Read(ref stopping) != 0) return;
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
            if (Volatile.Read(ref stopping) == 0) await uiMessage.ShowAsync(e.Message);
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
                if (download is not null)
                {
                    download.OnInfoEventAsync -= Download_OnInfoEventAsync;
                    await download.DisposeAsync();
                    download = null;
                }
                if (Volatile.Read(ref stopping) != 0) return;
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
        private int pageSize = 50;

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
        /// <summary>按当前关键词筛选缓存后分页，保持筛选条件并规范有效页码，不额外请求插件仓库。</summary>
        private async Task PageIndexChangedExecuteAsync(int index)
        {
            if (Volatile.Read(ref stopping) != 0) return;
            try
            {
                var table = allPlugin ??= await GetNugetPluginAsync();
                var term = QueryContent?.Trim();
                var filtered = string.IsNullOrWhiteSpace(term) ? table : table.Where(plugin =>
                    (plugin.PackName ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase)
                    || (plugin.Describe ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
                Total = filtered.Count;
                PageIndex = Math.Clamp(index, 1, (int)Math.Max(1, (Total + (long)PageSize - 1) / PageSize));
                Plugins = new ObservableCollection<PluginBrowseDataGridModel>(filtered.OrderByDescending(plugin => plugin.UpdateTime)
                    .Skip((PageIndex - 1) * PageSize).Take(PageSize));
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        }

        /// <summary>
        /// 查询地址
        /// </summary>
        public IAsyncRelayCommand Query => query ??= new AsyncRelayCommand(QueryAsync);
        private IAsyncRelayCommand? query;
        /// <summary>查询当前缓存并刷新第一页；零结果同样更新列表，不保留旧记录。</summary>
        private async Task QueryAsync()
        {
            await PageIndexChangedExecuteAsync(1);
            if (Total == 0 && !string.IsNullOrWhiteSpace(QueryContent))
                await uiMessage.ShowAsync("未查询到对应内容".GetLanguageValue(App.LanguageOperate));
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
                data = (await File.ReadAllTextAsync(GlobalConfigModel.UI_PluginBrowseCachePath, lifetime.Token)).ToJsonEntity<List<PluginBrowseDataGridModel>>();
            }
            if (data == null)
            {
                data = await pluginBrowseHandler.GetPluginBrowseDataGridModelsAsync(cancellationToken: lifetime.Token);
                await File.WriteAllTextAsync(GlobalConfigModel.UI_PluginBrowseCachePath, data.ToJson(true), lifetime.Token);
            }
            return data;
        }
        /// <summary>进入终态并共同等待元数据及下载清理；应用退出前必须异步等待。</summary>
        public ValueTask DisposeAsync()
        {
            lock (disposalGate)
            {
                Volatile.Write(ref stopping, 1);
                return new ValueTask(disposalTask ??= DisposeCoreAsync());
            }
        }

        /// <summary>取消网络读取和下载，再等待初始化及已有刷新完成；消息资源最后退订与释放。</summary>
        private async Task DisposeCoreAsync()
        {
            await Task.Yield();
            lifetime.Cancel();
            pluginBrowseHandler.Dispose();
            await initializationTask;
            if (p_Update?.ExecutionTask is Task updating) await updating;
            if (download is not null)
            {
                download.OnInfoEventAsync -= Download_OnInfoEventAsync;
                await download.DisposeAsync();
            }
            uiMessage.OnInfoEventAsync -= UiMessage_OnInfoEventAsync;
            await uiMessage.DisposeAsync();
            lifetime.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
