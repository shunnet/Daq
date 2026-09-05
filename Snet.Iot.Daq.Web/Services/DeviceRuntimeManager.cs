using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.handler;
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
    private readonly LocalizationService _localization = new();
    private readonly object _syncLock = new();

    public event Action? RuntimesChanged;
    public event Action<DeviceRuntime>? RuntimeStateChanged;

    public DeviceRuntimeManager(LoggerBuffer logger, AppStateService appState)
    {
        _logger = logger;
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
                _ = ApplySettingsAsync(runtime, device);
            }
            foreach (var guid in _runtimes.Keys.Where(g => !valid.Contains(g)).ToList())
            {
                if (_runtimes.TryRemove(guid, out var rt))
                    _ = rt.DisposeAsync();
            }
            RuntimesChanged?.Invoke();
        }
    }

    private async Task ApplySettingsAsync(DeviceRuntime runtime, IProjectTreeViewModel device)
    {
        try { await runtime.ApplySettingsAsync(device); }
        catch (Exception ex) { _logger.Push($"[Error] 设备配置更新失败 {runtime.DeviceName}: {ex.Message}"); }
    }

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
        // 插件类名 → 包目录名 映射（重复 Name 取首条，异常残留无害降级）
        var deviceToPack = LoadPluginList()
            .Where(p => !string.IsNullOrWhiteSpace(p.PluginDetails?.Path))
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key,
                g => Path.GetFileName(Path.TrimEndingDirectorySeparator(g.First().PluginDetails!.Path)),
                StringComparer.OrdinalIgnoreCase);
        foreach (var rt in Runtimes)
        {
            // 类名 == 包名的插件（单类插件）直接命中；多类插件走映射
            var match = type == Snet.Model.@enum.PluginType.Daq
                ? rt.IsRun && (rt.DeviceType.Equals(packageName, StringComparison.OrdinalIgnoreCase)
                    || (deviceToPack.TryGetValue(rt.DeviceType, out var pack)
                        && pack.Equals(packageName, StringComparison.OrdinalIgnoreCase)))
                : rt.IsRun; // MQ 插件与设备关联在地址转发中，简化：全部停止
            if (match)
            {
                await rt.StopAsync();
                stopped.Add(rt);
            }
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
