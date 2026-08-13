using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.handler;
using Snet.Iot.Daq.Core.@interface;
using Snet.Iot.Daq.Web.Data;
using Snet.Mqtt.service;
using Snet.Opc.ua.service;
using Snet.Utility;

namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// 全局状态服务（对应 WPF 端 GlobalConfigModel 的三个全局字典 + RebindGlobals 回灌 + uaService/mqttService 服务端实例）。
/// 只做加载/缓存/通知，业务操作一律调 Core。
/// </summary>
public class AppStateService
{
    private readonly DbGate _dbGate;
    private readonly LoggerBuffer _logger;

    /// <summary>配置写门：串行化 PluginConfig.json / ProjectConfig.json 的检查→写文件→改字典→落盘流程（Blazor 多电路并发保护）</summary>
    public readonly SemaphoreSlim ConfigSaveGate = new(1, 1);

    public ConcurrentDictionary<string, PluginConfigModel> PluginDict { get; } = new();
    public ConcurrentDictionary<string, IAddressModel> AddressDict { get; } = new();
    public ObservableCollection<IProjectTreeViewModel> ProjectDict { get; } = new();

    /// <summary>OPC UA 服务端实例（对应 WPF GlobalConfigModel.uaService）</summary>
    public OpcUaServiceOperate? UaService { get; set; }

    /// <summary>MQTT 服务端实例（对应 WPF GlobalConfigModel.mqttService）</summary>
    public MqttServiceOperate? MqttService { get; set; }

    /// <summary>服务端状态变更通知（UA/MQTT 启动/停止后触发，供控制台刷新）</summary>
    public event Action? ServerStateChanged;

    public void NotifyServerStateChanged() => ServerStateChanged?.Invoke();

    /// <summary>实体变更通知（地址/插件信息修改后触发，对齐原版 RefreshAsync 感知更新）</summary>
    public event Action? EntityChanged;

    public AppStateService(DbGate dbGate, LoggerBuffer logger)
    {
        _dbGate = dbGate;
        _logger = logger;
    }

    /// <summary>从 JSON 配置 + SQLite 加载插件/地址/项目树并回灌全局引用（对齐 WPF App.xaml.cs Init）</summary>
    public async Task LoadAllAsync()
    {
        PluginDict.Clear();
        if (File.Exists(WebPaths.PluginConfigPath))
        {
            var plugins = PluginHandlerCore.GetPluginUIConfig<ObservableCollection<PluginConfigModel>>(WebPaths.PluginConfigPath);
            if (plugins is not null)
            {
                var pathRepaired = false;
                foreach (var item in plugins)
                {
                    // 自愈：迁移/换机后 PluginConfig.json 可能残留旧环境绝对 ConfigPath（如 C:\Users\...\config\daq），
                    // 后续 UpdateLocalConfig 会把 per-SN 文件写进旧目录（幽灵文件），当前环境的 per-SN 永远是陈旧副本。
                    // 对齐 WPF：ConfigPath 一律是 config/{daq|mq} 相对路径（运行时再绝对化）
                    if (!string.IsNullOrWhiteSpace(item.ConfigPath) && Path.IsPathRooted(item.ConfigPath)
                        && !IsPathUnder(item.ConfigPath, WebPaths.DataDir))
                    {
                        item.ConfigPath = item.Type == Snet.Model.@enum.PluginType.Daq ? "config/daq" : "config/mq";
                        pathRepaired = true;
                    }
                    NormalizeConfigPath(item);
                    PluginDict[item.Guid] = item;
                    // 修复过路径的配置：立即把 per-SN 参数文件重写到当前环境（刷新陈旧副本）
                    if (pathRepaired) item.UpdateLocalConfig();
                }
                // 文件层统一落相对路径（对齐 WPF：ConfigPath = config/{daq|mq}），内存保持绝对化结果；
                // 任何绝对路径（含当前环境绝对）都转相对，幂等，防再次迁移/换机残留
                if (pathRepaired || PluginDict.Values.Any(p => !string.IsNullOrWhiteSpace(p.ConfigPath) && Path.IsPathRooted(p.ConfigPath)))
                {
                    foreach (var item in PluginDict.Values)
                    {
                        if (!string.IsNullOrWhiteSpace(item.ConfigPath) && Path.IsPathRooted(item.ConfigPath))
                            item.ConfigPath = Path.GetRelativePath(WebPaths.DataDir, item.ConfigPath);
                    }
                    await ProjectHandlerCore.WriteToFileWithRetryAsync(WebPaths.PluginConfigPath,
                        new ObservableCollection<PluginConfigModel>(PluginDict.Values.OrderBy(p => p.Index)).ToJson(true));
                    _logger.Push("[Info] 已规范化插件配置路径（对齐 WPF 相对路径布局）");
                }
            }
        }

        AddressDict.Clear();
        lock (_dbGate.DbLock)
        {
            foreach (var row in _dbGate.Db.Table<AddressModel>().ToList())
                AddressDict[row.Guid] = row;
        }

        ProjectDict.Clear();
        if (File.Exists(WebPaths.ProjectConfigPath))
        {
            var projects = ProjectHandlerCore.GetConfig<ObservableCollection<IProjectTreeViewModel>>(WebPaths.ProjectConfigPath)?
                .GetSource<ObservableCollection<IProjectTreeViewModel>>() ?? new();
            ProjectHandlerCore.InitChildrenParent(projects);
            foreach (var node in projects)
            {
                node.IsExpanded = true;
                foreach (var child in node.Children)
                    ExpandAll(child);
                RebindProjectNode(node);
                ProjectDict.Add(node);
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 实体变更：地址/插件信息修改后调用 → 刷新项目树全部引用与名称（感知更新）→ 持久化项目树。
    /// 对齐原版 GlobalConfigModel.RefreshAsync + 节点 OnInfoEvent 后的 SetAsync：一处改变，所有用到的地方跟着变，且名称变更落盘。
    /// 落盘走 Task.Run + WaitAsync（调用方可能正持有 ConfigSaveGate，同线程再等待会死锁，后台线程安全等待即可）。
    /// </summary>
    public void NotifyEntityChanged()
    {
        RefreshProjectBindings();
        _ = Task.Run(PersistProjectsAfterEntityChangeAsync);
        EntityChanged?.Invoke();
    }

    /// <summary>实体变更后的项目树持久化（跨门等待，与 SaveProjectsAsync 串行共用写门）</summary>
    private async Task PersistProjectsAfterEntityChangeAsync()
    {
        try
        {
            await SaveProjectsAsync();
        }
        catch (Exception ex)
        {
            _logger.Push($"[Error] 项目配置感知更新落盘失败: {ex.Message}");
        }
    }

    /// <summary>刷新项目树引用与名称：替换为全局字典最新对象 + 更新节点名（不塞回已删除实体）</summary>
    private void RefreshProjectBindings()
    {
        foreach (var node in ProjectDict)
            RefreshProjectNode(node);
    }

    private void RefreshProjectNode(IProjectTreeViewModel node)
    {
        if (node.DaqDetails is not null)
        {
            if (PluginDict.TryGetValue(node.DaqDetails.Guid, out var global))
                node.DaqDetails = global;
            node.UpdateName();
        }
        if (node.Details is not null)
        {
            foreach (var detail in node.Details)
                RefreshDetailNode(detail);
        }
        foreach (var child in node.Children)
            RefreshProjectNode(child);
    }

    private void RefreshDetailNode(IProjectDetailsTreeViewModel node)
    {
        if (node.AddressDetails is not null)
        {
            if (AddressDict.TryGetValue(node.AddressDetails.Guid, out var addr))
                node.AddressDetails = addr;
            node.UpdateAddressName();
        }
        if (node.MqDetails is not null)
        {
            if (PluginDict.TryGetValue(node.MqDetails.Guid, out var mq))
                node.MqDetails = mq;
            node.UpdateMqName();
        }
        foreach (var child in node.Children)
            RefreshDetailNode(child);
    }

    /// <summary>Web 端树默认全展开（加载时 IsExpanded 可能丢失，统一展开提升可操作性）</summary>
    private static void ExpandAll(IProjectTreeViewModel node)
    {
        node.IsExpanded = true;
        foreach (var child in node.Children)
            ExpandAll(child);
    }

    public async Task SaveProjectsAsync()
    {
        await ConfigSaveGate.WaitAsync();
        try
        {
            // 注意：Core 该方法失败返回 false 而不抛异常，必须检查返回值
            if (!await ProjectHandlerCore.WriteToFileWithRetryAsync(WebPaths.ProjectConfigPath, ProjectDict.ToJson(true)))
            {
                // 重试已耗尽：写入失败不中断电路，但必须让用户可见（控制台信息区），否则静默丢更新
                _logger.Push("[Error] 项目配置写入失败（已重试 5 次）：ProjectConfig.json 可能被占用");
            }
        }
        finally
        {
            ConfigSaveGate.Release();
        }
    }

    /// <summary>
    /// 绝对化插件配置的 ConfigPath：WPF 存的是相对路径（config/daq），
    /// 统一解析到数据目录，保证 per-SN 参数文件在 WPF/Web 间读写同一位置。
    /// </summary>
    public static void NormalizeConfigPath(PluginConfigModel model)
    {
        if (!string.IsNullOrWhiteSpace(model.ConfigPath) && !Path.IsPathRooted(model.ConfigPath))
            model.ConfigPath = Path.GetFullPath(Path.Combine(WebPaths.DataDir, model.ConfigPath));
    }

    /// <summary>绝对路径是否位于某根目录之下（用于识别迁移残留的旧环境路径）</summary>
    private static bool IsPathUnder(string path, string root)
    {
        try
        {
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var baseDir = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>导入项目树准备：初始化父子关系 + 全展开 + 回灌全局引用（对齐 LoadAllAsync 处理流程，供导入功能复用）。缺失地址同步落库</summary>
    public void PrepareImportedProjects(ObservableCollection<IProjectTreeViewModel> projects)
    {
        ProjectHandlerCore.InitChildrenParent(projects);
        foreach (var node in projects)
        {
            node.IsExpanded = true;
            foreach (var child in node.Children)
                ExpandAll(child);
            RebindProjectNode(node, persistMissingAddresses: true);
        }
    }

    /// <summary>回灌项目树节点的全局引用（DaqDetails/AddressDetails/MqDetails → 全局字典），防断链</summary>
    private void RebindProjectNode(IProjectTreeViewModel node, bool persistMissingAddresses = false)
    {
        if (node.DaqDetails is not null)
        {
            var guid = node.DaqDetails.Guid;
            if (!PluginDict.TryGetValue(guid, out var global))
            {
                // 项目内嵌副本回灌字典：同样执行 ConfigPath 绝对化（与 LoadAllAsync 一致）
                NormalizeConfigPath(node.DaqDetails);
                PluginDict[guid] = node.DaqDetails;
            }
            else
            {
                node.DaqDetails = global;
            }
            node.UpdateName();
        }
        if (node.Details is not null)
        {
            foreach (var detail in node.Details)
                RebindDetailNode(detail, persistMissingAddresses);
        }
        foreach (var child in node.Children)
            RebindProjectNode(child, persistMissingAddresses);
    }

    private void RebindDetailNode(IProjectDetailsTreeViewModel node, bool persistMissingAddresses)
    {
        if (node.AddressDetails is not null)
        {
            var guid = node.AddressDetails.Guid;
            if (AddressDict.TryGetValue(guid, out var addr))
            {
                node.AddressDetails = addr;
            }
            else
            {
                IAddressModel target = node.AddressDetails;
                if (persistMissingAddresses)
                {
                    // 导入的项目引用本机不存在的地址：转为 AddressModel 实体落库（保留 Guid），避免项目树与 DB 长期分叉
                    var source = node.AddressDetails;
                    var entity = new AddressModel
                    {
                        Guid = source.Guid,
                        Address = source.Address,
                        AnotherName = source.AnotherName,
                        Type = source.Type,
                        Length = source.Length,
                        EncodingType = source.EncodingType,
                        Describe = source.Describe,
                        Topic = source.Topic,
                        SimplifyValue = source.SimplifyValue,
                        ExpandParam = source.ExpandParam,
                        Time = source.Time
                    };
                    var result = _dbGate.InsertUniqueAddresses(new[] { entity });
                    target = result.Success > 0 ? entity : target;
                }
                node.AddressDetails = target;
                AddressDict[guid] = target;
            }
            node.UpdateAddressName();
        }
        if (node.MqDetails is not null)
        {
            var guid = node.MqDetails.Guid;
            if (!PluginDict.TryGetValue(guid, out var mq))
            {
                NormalizeConfigPath(node.MqDetails);
                PluginDict[guid] = node.MqDetails;
            }
            else
            {
                node.MqDetails = mq;
            }
            node.UpdateMqName();
        }
        foreach (var child in node.Children)
            RebindDetailNode(child, persistMissingAddresses);
    }
}
