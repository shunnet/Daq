using System.Collections.Concurrent;

namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// 设备运行时管理器：按项目树设备节点惰性创建/同步 DeviceRuntime，配置变更自动重载。
/// 订阅 AppState.EntityChanged 自动同步（对齐 WPF 全局单例 ConsoleModel：配置一变所有设备快照立即刷新，
/// 不依赖某个控制台页面电路是否在线）。
/// </summary>
public class DeviceRuntimeManager
{
    #region 字段与事件
    private readonly ConcurrentDictionary<string, DeviceRuntime> _runtimes = new();
    private readonly LoggerBuffer _logger;
    private readonly LocalizationService _localization;
    private readonly object _syncLock = new();

    public event Action? RuntimesChanged;
    public event Action<DeviceRuntime>? RuntimeStateChanged;

    public DeviceRuntimeManager(LoggerBuffer logger, AppStateService appState, LocalizationService localization)
    {
        _logger = logger;
        _localization = localization;
        // 感知更新：插件/地址/项目修改后立即同步设备快照（SN、层级、地址集）
        appState.EntityChanged += () => SyncFromProjects(appState);
    }

    public IEnumerable<DeviceRuntime> Runtimes => _runtimes.Values;
    public int Count => _runtimes.Count;

    /// <summary>按项目树同步设备集合（新增/移除），不自动启停。加锁串行：多电路并发修改时快照一致</summary>
    #endregion

    #region 同步与生命周期
    public void SyncFromProjects(AppStateService appState)
    {
        lock (_syncLock)
        {
            var devices = new List<IProjectTreeViewModel>();
            CollectDevices(appState.ProjectDict, devices);

            var valid = new HashSet<string>();
            foreach (var device in devices)
            {
                if (device.DaqDetails is null) continue;
                valid.Add(device.DaqDetails.Guid);
                var runtime = _runtimes.GetOrAdd(device.DaqDetails.Guid, guid =>
                    new DeviceRuntime(device, () => appState.UaService, _logger.Push, rt => RuntimeStateChanged?.Invoke(rt), _localization));
                // 配置快照刷新：参数/地址集变化时运行中设备自动重订阅（对齐 WPF 修改后自动 Retry）
                var changed = runtime.RefreshSettings(device);
                // 软启设备（IsSoftStart 持久化于项目树）：宿主启动/配置同步时自动恢复采集
                if (device.IsSoftStart && !runtime.IsRun)
                    _ = runtime.CollectAsync();
                else if (changed && runtime.IsRun)
                    _ = runtime.RetryAsync();
                else if (changed && !runtime.IsRun)
                    // 配置变更且未运行：清理旧 handler（插件实例缓存旧参数快照），下次手动采集用最新配置重建
                    _ = runtime.ResetHandlerAsync();
            }
            foreach (var guid in _runtimes.Keys.Where(g => !valid.Contains(g)).ToList())
            {
                if (_runtimes.TryRemove(guid, out var rt))
                    _ = rt.DisposeAsync();
            }
            RuntimesChanged?.Invoke();
        }
    }

    public DeviceRuntime? Get(string guid) => _runtimes.TryGetValue(guid, out var rt) ? rt : null;

    public async Task StopAllAsync()
    {
        foreach (var rt in _runtimes.Values)
            await rt.StopAsync();
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
