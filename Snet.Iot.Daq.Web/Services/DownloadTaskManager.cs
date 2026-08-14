using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.handler;

namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// 插件下载作业管理器：包装 Core PluginDownloadHandler 为异步作业（任务状态机 + 进度推送）。
/// Core 零改动 → 无行级进度，按 排队→下载→完成/失败 状态推进。
/// </summary>
public class DownloadTaskManager
{
    #region 字段与作业模型
    private readonly LoggerBuffer _logger;
    private readonly DeviceRuntimeManager _runtimeManager;
    private readonly AppStateService _appState;
    private readonly DaqHostedService _hosted;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<DownloadJob> _jobs = new();
    private CancellationTokenSource? _stopCts;

    /// <summary>取消所有进行中的下载（「停止下载」按钮）</summary>
    #endregion

    #region 队列控制
    public void StopAll()
    {
        lock (_gate)
        {
            _stopCts?.Cancel();
            _stopCts = null;
        }
    }

    public record DownloadJob(string Id, string PackName, string Status, int Progress, string? Error);

    public event Action<DownloadJob>? JobChanged;

    public DownloadTaskManager(LoggerBuffer logger, DeviceRuntimeManager runtimeManager, AppStateService appState, DaqHostedService hosted)
    {
        _logger = logger;
        _runtimeManager = runtimeManager;
        _appState = appState;
        _hosted = hosted;
    }

    /// <summary>dotnet CLI 可用性探测（Core PluginDownloadHandler 依赖 dotnet publish）。异步版：不阻塞电路线程</summary>
    #endregion

    #region SDK 探测
    public static async Task<bool> IsSdkAvailableAsync()
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("dotnet", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (p is null) return false;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await p.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { p.Kill(); } catch { /* 进程已退出 */ }
                return false;
            }
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public IReadOnlyList<DownloadJob> Jobs
    {
        get
        {
            lock (_gate)
                return _jobs.ToList();
        }
    }

    public async Task<string> EnqueueAsync(IEnumerable<PluginBrowseDataGridModel> models)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var names = models.Select(m => m.PackName).ToList();
        await _gate.WaitAsync();
        try
        {
            // 入队前清理终态任务（否则第 10 次下载后队列永久满）
            _jobs.RemoveAll(j => j.Status is "完成" or "失败" or "已取消");
            // 同名去重 + 队列上限，防反复点击积压
            if (_jobs.Any(j => j.PackName == string.Join(", ", names.Take(3)) && j.Status is "排队" or "下载中"))
            {
                _logger.Push($"[Warn] 同名插件下载任务已存在: {names.FirstOrDefault()}");
                return _jobs.First(j => j.PackName == string.Join(", ", names.Take(3))).Id;
            }
            if (_jobs.Count >= 10)
                throw new InvalidOperationException("下载队列已满（上限 10）");
            // 取消令牌在入队即创建：SDK 探测窗口内的「停止下载」也能生效
            _stopCts ??= new CancellationTokenSource();
            var job = new DownloadJob(id, string.Join(", ", names.Take(3)), "排队", 0, null);
            _jobs.Add(job);
            JobChanged?.Invoke(job);
            _ = RunAsync(job, names);
        }
        finally
        {
            _gate.Release();
        }
        return id;
    }

    #endregion

    #region 下载作业执行
    private async Task RunAsync(DownloadJob job, List<string> names)
    {
        if (!await IsSdkAvailableAsync())
        {
            Update(job with { Status = "失败", Error = "服务器未安装 .NET SDK，无法下载插件" });
            _logger.Push("[Error] 插件下载失败：.NET SDK 不可用");
            return;
        }
        CancellationToken token;
        lock (_gate)
        {
            _stopCts ??= new CancellationTokenSource();
            token = _stopCts.Token;
        }
        // 探测前已停止则不再进入下载（令牌在 EnqueueAsync 即创建，探测期间点「停止下载」也能取消）
        if (token.IsCancellationRequested)
        {
            Update(job with { Status = "已取消", Error = null });
            return;
        }
        try
        {
            Update(job with { Status = "下载中", Progress = 10 });
            _logger.Push($"[Info] 开始下载插件: {job.PackName}");
            using var handler = new PluginDownloadHandler(WebPaths.FilePath);
            var ok = await handler.DownloadAsync(names, zip: true, token);
            if (!ok)
            {
                Update(job with { Status = "失败", Error = "下载失败" });
                _logger.Push($"[Error] 插件下载失败: {job.PackName}");
                return;
            }
            Update(job with { Status = "安装中", Progress = 60 });
            _logger.Push($"[Info] 插件下载完成，开始安装: {job.PackName}");
            // 下载即安装：探测类型 → 归位 lib/{type}/{name}/ → InitPlugin → 注册 PluginList.json
            // 同名插件走热更新（停设备 → 卸载 → 替换 → 恢复），对齐上传路径语义
            var installResults = await TryAutoInstallAsync(names);
            Update(job with
            {
                Status = installResults > 0 ? "完成" : "失败",
                Progress = installResults > 0 ? 100 : 0,
                Error = installResults > 0 ? null : "下载完成但安装失败，请到插件设置手动上传"
            });
            _logger.Push(installResults > 0
                ? $"[Info] 插件安装成功: {job.PackName}（{installResults} 个接口）"
                : $"[Error] 插件安装失败: {job.PackName}，可在插件设置页手动上传");
        }
        catch (OperationCanceledException)
        {
            Update(job with { Status = "已取消", Error = null });
            _logger.Push($"[Warn] 插件下载已取消: {job.PackName}");
        }
        catch (Exception ex)
        {
            Update(job with { Status = "失败", Error = ex.Message });
            _logger.Push($"[Error] 插件下载异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 下载即安装：把 lib/{name}/ 探测类型后归位到 lib/{type}/{name}/ 并 InitPlugin 注册。
    /// 同名插件执行热更新（对齐上传路径）：停使用该插件的设备 → 卸载旧程序集 → 替换目录 → 恢复设备采集。
    /// 返回成功安装的接口数。
    /// </summary>
    #endregion

    #region 自动安装
    private async Task<int> TryAutoInstallAsync(List<string> names)
    {
        var installed = 0;
        var stopped = new List<DeviceRuntime>();
        try
        {
            foreach (var name in names)
            {
                try
                {
                    var srcPath = Path.Combine(WebPaths.FilePath, name);
                    if (!Directory.Exists(srcPath)) continue;
                    // 探测 Daq / Mq 接口
                    foreach (var type in new[] { Snet.Model.@enum.PluginType.Daq, Snet.Model.@enum.PluginType.Mq })
                    {
                        var iName = $"Snet.Model.interface.I{type}";
                        var result = PluginHandlerCore.PluginOperate.InitPlugin(srcPath, iName);
                        if (result.Count == 0) continue;
                        // 归位到 lib/{type小写}/{name}/
                        var typePath = Path.Combine(WebPaths.FilePath, type.ToString().ToLower());
                        var targetPath = Path.Combine(typePath, name);
                        Directory.CreateDirectory(typePath);
                        var isHotUpdate = Directory.Exists(targetPath) || LoadPluginList().Any(p => p.Name == name);
                        if (isHotUpdate)
                        {
                            _logger.Push($"[Info] 检测到同名插件 {name}，执行热更新");
                            // 停使用该插件的 DAQ 设备：设备类型是插件类名（如 SiemensOperate），下载名是包名（如 Snet.Siemens），
                            // 通过 PluginList 的 Name → 包目录名 映射关联（对齐 WPF libPath == DaqPluginPath 语义）
                            // BUG 修复：ToDictionary 在 PluginList 出现重复 Name（异常残留/手改）时会抛
                            // "相同键已存在"，导致整个自动安装中断。改为 GroupBy 取首条，重复条目无害降级。
                            var deviceToPack = LoadPluginList()
                                .Where(p => !string.IsNullOrWhiteSpace(p.PluginDetails.Path))
                                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                                .ToDictionary(g => g.Key,
                                    g => Path.GetFileName(Path.TrimEndingDirectorySeparator(g.First().PluginDetails.Path)),
                                    StringComparer.OrdinalIgnoreCase);
                            foreach (var rt in _runtimeManager.Runtimes.Where(rt => rt.IsRun && type == Snet.Model.@enum.PluginType.Daq
                                && deviceToPack.TryGetValue(rt.DeviceType, out var pack)
                                && pack.Equals(name, StringComparison.OrdinalIgnoreCase)))
                            {
                                await rt.StopAsync();
                                stopped.Add(rt);
                            }
                            // 卸载程序集前优雅停止 UA/MQTT 服务端：释放监听端口，防僵尸 socket 占用导致新服务端绑定失败
                            try { await _hosted.StopServerServicesAsync(); }
                            catch (Exception ex) { _logger.Push($"[Error] 服务端停止失败: {ex.Message}"); }
                            // 卸载旧程序集并回收，避免文件锁/旧实例残留（对齐 WPF PrivateRemovalPlugin）
                            foreach (var old in LoadPluginList().Where(p => p.Name == name))
                            {
                                try { PluginHandlerCore.PluginOperate.RemovePluginAsync(old.Name); } catch { /* 未注册/已卸载忽略 */ }
                            }
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                        }
                        if (Directory.Exists(targetPath)) Directory.Delete(targetPath, true);
                        Directory.Move(srcPath, targetPath);
                        foreach (var (model, _) in result)
                        {
                            model.Path = targetPath;
                            var plugin = new PluginListModel(model.Name, type, model.Version, DateTime.Now, model);
                            var list = LoadPluginList();
                            // 热更新：替换同名旧条目（刷新路径/版本/时间）；新插件：追加
                            var index = list.FindIndex(p => p.Name == plugin.Name);
                            if (index >= 0) list[index] = plugin;
                            else list.Add(plugin);
                            PluginHandlerCore.SavePluginUIConfig(new System.Collections.ObjectModel.ObservableCollection<PluginListModel>(list), WebPaths.PluginListConfigPath);
                        }
                        // 重新注册最终路径：探测注册的是下载临时目录（Move 后失效），
                        // 不重注册则设备启动采集报"插件尚未加载"（对齐 WPF InitPlugin(libPath) 流程）
                        foreach (var (model, _) in result)
                        {
                            try { PluginHandlerCore.PluginOperate.RemovePluginAsync(model.Name); } catch { /* 未注册忽略 */ }
                        }
                        try
                        {
                            PluginHandlerCore.PluginOperate.InitPlugin(targetPath, iName);
                        }
                        catch (Exception ex)
                        {
                            _logger.Push($"[Error] 插件运行时重新注册失败 {name}: {ex.Message}");
                        }
                        // 服务端重启（仅热更新：程序集卸载波及服务端，此时端口已释放可重新绑定）
                        if (isHotUpdate)
                        {
                            await _hosted.InitServerServicesAsync();
                        }
                        installed += result.Count;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DownloadTaskManager] 自动安装失败 {name}: {ex.Message}");
                }
            }
        }
        finally
        {
            // 恢复热更新前正在运行的设备（只恢复本次停掉的）
            // 单个设备恢复失败不影响其他设备与安装结果（异常不外抛覆盖 installed 计数）
            foreach (var rt in stopped)
            {
                try { await rt.CollectAsync(); }
                catch (Exception ex)
                {
                    _logger.Push($"[Error] 热更新后恢复设备采集失败 {rt.DeviceName}: {ex.Message}");
                }
            }
            // 感知更新：同步设备底层版本号（设备卡展示热更新后的新版本）
            _appState.NotifyEntityChanged();
        }
        return installed;
    }

    #endregion

    #region 列表读取与状态更新
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

    private void Update(DownloadJob job)
    {
        lock (_gate)
        {
            var index = _jobs.FindIndex(j => j.Id == job.Id);
            if (index >= 0) _jobs[index] = job;
        }
        JobChanged?.Invoke(job);
    }
    #endregion
}
