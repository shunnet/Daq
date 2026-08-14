using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.handler;
using Snet.Model.data;
using Snet.Mqtt.service;
using Snet.Opc.ua.service;
using Snet.Utility;
using System.Collections.ObjectModel;

namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// Daq 宿主服务：进程生命周期编排（对齐 WPF App.xaml.cs Init + ConsoleModel 的服务启动）。
/// 启动：加载配置 → InitPlugin 加载插件程序集 → 启动 UA/MQTT 服务端；停止：逆序释放。
/// </summary>
public class DaqHostedService : BackgroundService
{
    private readonly AppStateService _appState;
    private readonly DeviceRuntimeManager _runtimeManager;
    private readonly MonitorSampler _sampler;
    private readonly LoggerBuffer _loggerBuffer;
    private readonly LocalizationService _localization;
    private readonly ILogger<DaqHostedService> _logger;

    #region 字段与构造
    public DaqHostedService(
            AppStateService appState,
            DeviceRuntimeManager runtimeManager,
            MonitorSampler sampler,
            LoggerBuffer loggerBuffer,
            LocalizationService localization,
            ILogger<DaqHostedService> logger)
    {
        _appState = appState;
        _runtimeManager = runtimeManager;
        _sampler = sampler;
        _loggerBuffer = loggerBuffer;
        _localization = localization;
        _logger = logger;
    }

    #endregion

    #region 生命周期
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DaqHostedService 启动");
        try
        {
            await _appState.LoadAllAsync();
            InitPlugins();
            _runtimeManager.SyncFromProjects(_appState);
            // 启动内嵌服务端（对齐 WPF ConsoleModel.InitAsync：读 config/server/*.json 自动启动 UA/MQTT）
            await InitServerServicesAsync();
            _sampler.Start();
            _loggerBuffer.Push(string.Format(T("[Info] Daq 宿主启动完成，设备 {0} 台"), _runtimeManager.Runtimes.Count()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Daq 宿主初始化失败");
            _loggerBuffer.Push(string.Format(T("[Error] Daq 宿主初始化失败: {0}"), ex.Message));
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    /// <summary>按 PluginList.json 逐个 InitPlugin 加载插件程序集（对齐 WPF Init 流程）</summary>
    #endregion

    #region 插件加载
    private void InitPlugins()
    {
        if (!File.Exists(WebPaths.PluginListConfigPath)) return;
        var plugins = PluginHandlerCore.GetPluginUIConfig<ObservableCollection<PluginListModel>>(WebPaths.PluginListConfigPath);
        if (plugins is null) return;
        foreach (var item in plugins)
        {
            try
            {
                // WPF 配置直接复用/数据目录迁移场景：Path 指向旧目录时回退到本数据目录
                // lib/{type}/{原目录名}（目录名=包名，如 Snet.Siemens，不能用插件类名拼）
                var path = item.PluginDetails.Path;
                if (!Directory.Exists(path))
                {
                    var dirName = string.IsNullOrEmpty(path) ? item.Name : Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
                    var fallback = Path.Combine(WebPaths.FilePath, item.Type.ToString().ToLower(), dirName);
                    if (Directory.Exists(fallback)) path = fallback;
                }
                var iName = string.Format("Snet.Model.interface.I{0}", item.Type);
                PluginHandlerCore.PluginOperate.InitPlugin(path, iName);
                _logger.LogInformation("插件加载成功: {Name} {Version}", item.Name, item.Version);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "插件加载失败: {Name}", item.Name);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("DaqHostedService 停止");
        // 进程停止时逆序释放：先停采集，再关服务端
        await _runtimeManager.StopAllAsync();
        await StopServerServicesAsync();
        _sampler.Dispose();
        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// 启动服务端（对齐 WPF ConsoleModel.MqttServerInitAsync / OpcUaServerInitAsync）：
    /// 配置存在才实例化并 OnAsync，失败退订事件 + Dispose + 置空。
    /// </summary>
    #endregion

    #region 服务端启动
    public async Task InitServerServicesAsync()
    {
        await InitMqttServerAsync();
        await InitUaServerAsync();
        _appState.NotifyServerStateChanged();
    }

    /// <summary>启动单个服务端（供控制台按钮调用：已启动则忽略）</summary>
    public async Task<(bool Ok, string Message)> StartServerAsync(string kind)
    {
        if (kind == "mqtt")
        {
            if (_appState.MqttService is not null) return (false, T("服务已启动"));
            // 首次启动且无配置：写默认配置（对齐 WPF MqttServerStartAsync）
            if (!File.Exists(WebPaths.MqttServerConfigPath))
            {
                Directory.CreateDirectory(WebPaths.ServerConfigPath);
                File.WriteAllText(WebPaths.MqttServerConfigPath, new MqttServiceData.Basics().ToJson(true));
            }
            var ok = await InitMqttServerAsync();
            _appState.NotifyServerStateChanged();
            return (ok, ok ? T("MQTT 服务启动成功") : T("MQTT 服务启动失败"));
        }
        if (kind == "ua")
        {
            if (_appState.UaService is not null) return (false, T("服务已启动"));
            if (!File.Exists(WebPaths.UaServerConfigPath))
            {
                Directory.CreateDirectory(WebPaths.ServerConfigPath);
                File.WriteAllText(WebPaths.UaServerConfigPath, new OpcUaServiceData.Basics().ToJson(true));
            }
            var ok = await InitUaServerAsync();
            _appState.NotifyServerStateChanged();
            return (ok, ok ? T("OPC UA 服务启动成功") : T("OPC UA 服务启动失败"));
        }
        return (false, T("未知服务"));
    }

    /// <summary>停止单个服务端（对齐 WPF MqttServerStopAsync / OpcUaServerStopAsync）</summary>
    #endregion

    #region 服务端停止
    public async Task<(bool Ok, string Message)> StopServerAsync(string kind)
    {
        try
        {
            if (kind == "mqtt")
            {
                if (_appState.MqttService is null) return (false, T("服务未启动"));
                await _appState.MqttService.OffAsync();
                _appState.MqttService.OnInfoEventAsync -= MqttService_OnInfoEventAsync;
                await _appState.MqttService.DisposeAsync();
                _appState.MqttService = null;
                _appState.NotifyServerStateChanged();
                return (true, T("MQTT 服务已停止"));
            }
            if (kind == "ua")
            {
                if (_appState.UaService is null) return (false, T("服务未启动"));
                await _appState.UaService.OffAsync();
                _appState.UaService.OnInfoEventAsync -= UaService_OnInfoEventAsync;
                await _appState.UaService.DisposeAsync();
                _appState.UaService = null;
                _appState.NotifyServerStateChanged();
                return (true, T("OPC UA 服务已停止"));
            }
        }
        catch (Exception ex)
        {
            _loggerBuffer.Push(string.Format(T("[Error] 服务停止异常: {0}"), ex.Message));
            return (false, ex.Message);
        }
        return (false, T("未知服务"));
    }

    #endregion

    #region 内嵌服务端初始化
    private async Task<bool> InitMqttServerAsync()
    {
        if (!File.Exists(WebPaths.MqttServerConfigPath)) return false;
        try
        {
            var basics = File.ReadAllText(WebPaths.MqttServerConfigPath).ToJsonEntity<MqttServiceData.Basics>() ?? new MqttServiceData.Basics();
            var service = MqttServiceOperate.Instance(basics);
            service.OnInfoEventAsync += MqttService_OnInfoEventAsync;
            var result = await service.OnAsync();
            if (!result.Status)
            {
                service.OnInfoEventAsync -= MqttService_OnInfoEventAsync;
                await service.DisposeAsync();
                _loggerBuffer.Push(string.Format(T("[Error] MQTT 服务端启动失败: {0}"), result.Message));
                return false;
            }
            _appState.MqttService = service;
            _loggerBuffer.Push(T("[Info] MQTT 服务端已启动"));
            return true;
        }
        catch (Exception ex)
        {
            _loggerBuffer.Push(string.Format(T("[Error] MQTT 服务端启动异常: {0}"), ex.Message));
            return false;
        }
    }

    private async Task<bool> InitUaServerAsync()
    {
        if (!File.Exists(WebPaths.UaServerConfigPath)) return false;
        try
        {
            var basics = File.ReadAllText(WebPaths.UaServerConfigPath).ToJsonEntity<OpcUaServiceData.Basics>() ?? new OpcUaServiceData.Basics();
            var service = OpcUaServiceOperate.Instance(basics);
            service.OnInfoEventAsync += UaService_OnInfoEventAsync;
            var result = await service.OnAsync();
            if (!result.Status)
            {
                service.OnInfoEventAsync -= UaService_OnInfoEventAsync;
                await service.DisposeAsync();
                _loggerBuffer.Push(string.Format(T("[Error] OPC UA 服务端启动失败: {0}"), result.Message));
                return false;
            }
            _appState.UaService = service;
            _loggerBuffer.Push(T("[Info] OPC UA 服务端已启动"));
            return true;
        }
        catch (Exception ex)
        {
            _loggerBuffer.Push(string.Format(T("[Error] OPC UA 服务端启动异常: {0}"), ex.Message));
            return false;
        }
    }

    #endregion

    #region 停服与事件转发
    private async Task StopServerServicesAsync()
    {
        try
        {
            if (_appState.MqttService is not null)
            {
                await _appState.MqttService.OffAsync();
                _appState.MqttService.OnInfoEventAsync -= MqttService_OnInfoEventAsync;
                await _appState.MqttService.DisposeAsync();
                _appState.MqttService = null;
            }
        }
        catch (Exception ex) { _loggerBuffer.Push(string.Format(T("[Error] MQTT 服务端停止异常: {0}"), ex.Message)); }
        try
        {
            if (_appState.UaService is not null)
            {
                await _appState.UaService.OffAsync();
                _appState.UaService.OnInfoEventAsync -= UaService_OnInfoEventAsync;
                await _appState.UaService.DisposeAsync();
                _appState.UaService = null;
            }
        }
        catch (Exception ex) { _loggerBuffer.Push(string.Format(T("[Error] OPC UA 服务端停止异常: {0}"), ex.Message)); }
    }

    /// <summary>服务端事件 → 控制台信息区（对齐 WPF ShowAsync 的 ToJson 展示）</summary>
    private Task MqttService_OnInfoEventAsync(object? sender, EventInfoResult e)
    {
        _loggerBuffer.Push($"[MqttService] {e.ToJson(true)}");
        return Task.CompletedTask;
    }

    private Task UaService_OnInfoEventAsync(object? sender, EventInfoResult e)
    {
        _loggerBuffer.Push($"[OpcUaService] {e.ToJson(true)}");
        return Task.CompletedTask;
    }

    #endregion

    #region 本地化
    private string T(string key) => _localization.T(key);
    #endregion
}
