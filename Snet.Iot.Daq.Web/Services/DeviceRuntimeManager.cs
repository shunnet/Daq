using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.handler;
using System.Collections.Concurrent;

namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// 设备运行时管理器：按项目树设备节点惰性创建/同步 DeviceRuntime，配置变更自动重载。
/// 订阅 AppState.EntityChanged 自动同步（对齐 WPF 全局单例 ConsoleModel：配置一变所有设备快照立即刷新，
/// 不依赖某个控制台页面电路是否在线）。
/// </summary>
public class DeviceRuntimeManager : IAsyncDisposable
{
    #region 字段与事件
    private readonly ConcurrentDictionary<string, DeviceRuntime> _runtimes = new();
    private readonly LoggerBuffer _logger;
    private readonly LocalizationService _localization = new();
    private readonly object _syncLock = new();
    private Task _pendingSync = Task.CompletedTask;
    private readonly AppStateService _appState;
    /// <summary>终止标志与共享释放任务只在同步锁内访问。</summary>
    private bool _disposed;
    private Task? _disposeTask;

    /// <summary>设备集合发生新增或移除时触发。</summary>
    public event Action? RuntimesChanged;
    /// <summary>单个设备运行状态变化时触发。</summary>
    public event Action<DeviceRuntime>? RuntimeStateChanged;

    /// <summary>创建设备运行时管理器并订阅全局配置变化。</summary>
    /// <param name="logger">应用内日志缓冲区。</param>
    /// <param name="appState">项目配置和服务端状态。</param>
    public DeviceRuntimeManager(LoggerBuffer logger, AppStateService appState)
    {
        _logger = logger;
        _appState = appState;
        // 感知更新：插件/地址/项目修改后立即同步设备快照（SN、层级、地址集）
        appState.EntityChanged += OnEntityChanged;
    }

    /// <summary>获取当前设备运行时的线程安全快照视图。</summary>
    public IEnumerable<DeviceRuntime> Runtimes => _runtimes.Values;
    /// <summary>获取当前设备运行时数量。</summary>
    public int Count => _runtimes.Count;

    #endregion

    #region 同步与生命周期
    /// <summary>按项目树同步设备集合（新增/移除），不自动启停。项目树快照与结构修改共用一把锁。</summary>
    public void SyncFromProjects(AppStateService appState)
    {
        var devices = new List<IProjectTreeViewModel>();
        lock (appState.ProjectTreeLock)
            CollectDevices(appState.ProjectDict, devices);
        lock (_syncLock)
        {
            if (_disposed) return;
            _pendingSync = SynchronizeAfterAsync(_pendingSync, appState, devices);
        }
    }

    /// <summary>等待前一次同步完成后应用最新项目快照，确保配置重载和释放按顺序执行。</summary>
    private async Task SynchronizeAfterAsync(
        Task previous,
        AppStateService appState,
        IReadOnlyCollection<IProjectTreeViewModel> devices)
    {
        // 不在创建任务的短同步锁内执行设备初始化或外部回调。
        await Task.Yield();
        try
        {
            await previous.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Push($"[Error] 上一次设备同步失败: {ex.Message}");
        }

        var valid = new HashSet<string>(StringComparer.Ordinal);
        foreach (var device in devices)
        {
            if (device.DaqDetails is null)
                continue;

            valid.Add(device.DaqDetails.Guid);
            if (!_runtimes.TryGetValue(device.DaqDetails.Guid, out var runtime))
            {
                runtime = new DeviceRuntime(device, () => appState.UaService, _logger.Push,
                    rt => RuntimeStateChanged?.Invoke(rt), _localization, appState.ProjectTreeLock);
                _runtimes[device.DaqDetails.Guid] = runtime;
            }
            await ApplySettingsAsync(runtime, device).ConfigureAwait(false);
        }

        foreach (var guid in _runtimes.Keys.Where(guid => !valid.Contains(guid)))
        {
            if (_runtimes.TryRemove(guid, out var runtime))
                await runtime.DisposeAsync().ConfigureAwait(false);
        }
        RuntimesChanged?.Invoke();
    }

    private async Task ApplySettingsAsync(DeviceRuntime runtime, IProjectTreeViewModel device)
    {
        try { await runtime.ApplySettingsAsync(device); }
        catch (Exception ex) { _logger.Push($"[Error] 设备配置更新失败 {runtime.DeviceName}: {ex.Message}"); }
    }

    /// <summary>按唯一标识查找设备运行时。</summary>
    /// <param name="guid">设备采集插件配置的唯一标识。</param>
    /// <returns>找到的运行时；不存在时返回 <see langword="null"/>。</returns>
    public DeviceRuntime? Get(string guid) => _runtimes.TryGetValue(guid, out var rt) ? rt : null;

    /// <summary>
    /// 停止使用指定插件的运行设备并返回列表（插件更新/移除前调用，恢复采集用）。
    /// 对齐 WPF UploadPluginAsync / PrivateRemovalPlugin 的停设备语义：
    /// 设备插件类型是插件类名（如 SiemensOperate），插件包名是目录名（如 Snet.Siemens），
    /// 二者不一致，必须经 PluginList 的 Name → 包目录名 映射关联（等价 WPF libPath == DaqPluginPath 判定）。
    /// MQ 插件与设备关联在地址转发中，简化：全部运行设备停止。
    /// </summary>
    /// <param name="type">插件类型</param>
    /// <param name="packageName">插件包名（lib/{type}/{包名} 目录名，如 Snet.Siemens）</param>
    public async Task<List<DeviceRuntime>> StopDevicesUsingPluginAsync(Snet.Model.@enum.PluginType type, string packageName)
    {
        var stopped = new List<DeviceRuntime>();
        if (string.IsNullOrWhiteSpace(packageName)) return stopped;
        var names = LoadPluginList().Where(plugin => plugin.Type == type
            && !string.IsNullOrWhiteSpace(plugin.PluginDetails?.Path)
            && Path.GetFileName(Path.TrimEndingDirectorySeparator(plugin.PluginDetails!.Path))
                .Equals(packageName, StringComparison.OrdinalIgnoreCase))
            .Select(plugin => plugin.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        names.Add(packageName);
        foreach (var runtime in Runtimes)
        {
            if (!runtime.UsesPlugin(type, names)) continue;
            var wasRunning = runtime.IsRun;
            // 即使采集已停止，也关闭可能独立打开的 WebApi/驱动；仅恢复原来运行的设备。
            await runtime.StopAsync();
            if (wasRunning) stopped.Add(runtime);
        }
        return stopped;
    }

    /// <summary>从 PluginList.json 读取插件清单（类名 → 包目录名 映射数据源）</summary>
    private static List<PluginListModel> LoadPluginList()
    {
        if (!File.Exists(WebPaths.PluginListConfigPath)) return new();
        try
        {
            return PluginHandlerCore.GetPluginUIConfig<System.Collections.ObjectModel.ObservableCollection<PluginListModel>>(WebPaths.PluginListConfigPath)?.ToList() ?? new();
        }
        catch
        {
            return new();
        }
    }

    /// <summary>等待配置同步完成并依次停止全部设备。</summary>
    public async Task StopAllAsync()
    {
        Task pendingSync;
        lock (_syncLock)
            pendingSync = _pendingSync;
        await pendingSync.ConfigureAwait(false);

        foreach (var rt in _runtimes.Values)
            await rt.StopAsync().ConfigureAwait(false);
    }

    /// <summary>响应全局配置变化；使用可退订的命名回调，避免管理器释放后重新创建设备。</summary>
    private void OnEntityChanged() => SyncFromProjects(_appState);

    /// <summary>进入不可恢复的释放状态，退订配置事件；重复调用等待同一清理任务。</summary>
    public ValueTask DisposeAsync()
    {
        lock (_syncLock)
        {
            _disposed = true;
            _appState.EntityChanged -= OnEntityChanged;
            return new ValueTask(_disposeTask ??= DisposeCoreAsync(_pendingSync));
        }
    }

    /// <summary>等待已排队的配置同步结束后释放所有设备；单个设备失败仍继续清理其他设备。</summary>
    /// <param name="pending">进入终态之前最后一次配置同步任务。</param>
    private async Task DisposeCoreAsync(Task pending)
    {
        await Task.Yield();
        try { await pending.ConfigureAwait(false); }
        catch (Exception ex) { _logger.Push($"[Error] 设备同步停止失败: {ex.Message}"); }
        foreach (var runtime in _runtimes.Values)
        {
            try { await runtime.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { _logger.Push($"[Error] 设备释放失败 {runtime.DeviceName}: {ex.Message}"); }
        }
        _runtimes.Clear();
        RuntimesChanged?.Invoke();
    }

    private static void CollectDevices(IEnumerable<IProjectTreeViewModel> nodes, List<IProjectTreeViewModel> result)
    {
        foreach (var node in nodes)
        {
            if (node.NodeType == ProjectNodeType.Device)
                result.Add(node);
            CollectDevices(node.Children, result);
        }
    }
    #endregion
}
