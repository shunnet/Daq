using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.handler;
using Snet.Utility;

namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// 插件下载作业管理器：包装 Core PluginDownloadHandler 为异步作业（任务状态机 + 进度推送）。
/// Core 零改动 → 无行级进度，按 排队→下载→完成/失败 状态推进。
/// </summary>
public sealed class DownloadTaskManager : IAsyncDisposable
{
    #region 字段与作业模型
    private readonly LoggerBuffer _logger;
    private readonly DeviceRuntimeManager _runtimeManager;
    private readonly AppStateService _appState;
    private readonly DaqHostedService _hosted;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly List<DownloadJob> _jobs = new();
    private readonly List<Task> _runningTasks = new();
    private CancellationTokenSource? _stopCts;

    #endregion

    #region 队列控制
    /// <summary>取消排队和下载；已进入目录替换的安装会完成当前包，避免留下半安装状态。</summary>
    public void StopAll()
    {
        lock (_gate)
        {
            _stopCts?.Cancel();
            _stopCts?.Dispose();
            _stopCts = null;
        }
    }

    /// <summary>描述一个插件下载作业的不可变状态快照。</summary>
    public sealed record DownloadJob
    {
        /// <summary>创建插件下载作业快照。</summary>
        /// <param name="id">作业唯一标识。</param>
        /// <param name="packName">待下载的插件包显示名称。</param>
        /// <param name="status">当前状态文本。</param>
        /// <param name="progress">0 到 100 的完成百分比。</param>
        /// <param name="error">失败原因；作业未失败时为空。</param>
        public DownloadJob(string id, string packName, string status, int progress, string? error)
        {
            Id = id;
            PackName = packName;
            Status = status;
            Progress = progress;
            Error = error;
        }

        /// <summary>获取作业唯一标识。</summary>
        public string Id { get; init; }
        /// <summary>获取待下载的插件包显示名称。</summary>
        public string PackName { get; init; }
        /// <summary>获取当前状态文本。</summary>
        public string Status { get; init; }
        /// <summary>获取 0 到 100 的完成百分比。</summary>
        public int Progress { get; init; }
        /// <summary>获取失败原因；作业未失败时为空。</summary>
        public string? Error { get; init; }
        /// <summary>取消请求到达安装阶段前后，已成功安装的插件接口数。</summary>
        public int InstalledCount { get; init; }
    }

    /// <summary>下载作业状态变化时触发。</summary>
    public event Action<DownloadJob>? JobChanged;

    /// <summary>创建插件下载作业管理器。</summary>
    /// <param name="logger">应用内日志缓冲区。</param>
    /// <param name="runtimeManager">设备运行时管理器。</param>
    /// <param name="appState">应用配置与运行状态。</param>
    /// <param name="hosted">采集宿主服务。</param>
    public DownloadTaskManager(LoggerBuffer logger, DeviceRuntimeManager runtimeManager, AppStateService appState, DaqHostedService hosted)
    {
        _logger = logger;
        _runtimeManager = runtimeManager;
        _appState = appState;
        _hosted = hosted;
    }

    #endregion

    #region SDK 探测
    /// <summary>dotnet CLI 可用性探测（Core PluginDownloadHandler 依赖 dotnet publish）。异步版：不阻塞电路线程</summary>
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

    /// <summary>获取下载作业列表快照。</summary>
    public IReadOnlyList<DownloadJob> Jobs
    {
        get
        {
            lock (_gate)
                return _jobs.ToList();
        }
    }

    /// <summary>验证下载参数并将选中的插件包加入串行下载队列。</summary>
    /// <param name="models">插件仓库中的选中项。</param>
    /// <returns>新建或已存在的作业标识。</returns>
    public Task<string> EnqueueAsync(IEnumerable<PluginBrowseDataGridModel> models)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var packages = models.Select(m => new PluginBrowseDataGridModel
        { PackName = m.PackName, Version = m.Version }).ToList();
        var names = packages.Select(m => m.PackName).ToList();
        if (names.Count == 0) throw new ArgumentException("请选择至少一个插件", nameof(models));
        lock (_gate)
        {
            // 入队前清理终态任务（否则第 10 次下载后队列永久满）
            _jobs.RemoveAll(j => j.Status is "完成" or "失败" or "已取消");
            // 同名去重 + 队列上限，防反复点击积压
            if (_jobs.Any(j => j.PackName == string.Join(", ", names.Take(3)) && j.Status is "排队" or "下载中"))
            {
                _logger.Push($"[Warn] 同名插件下载任务已存在: {names.FirstOrDefault()}");
                return Task.FromResult(_jobs.First(j => j.PackName == string.Join(", ", names.Take(3))).Id);
            }
            if (_jobs.Count >= 10)
                throw new InvalidOperationException("下载队列已满（上限 10）");
            // 取消令牌在入队即创建：SDK 探测窗口内的「停止下载」也能生效
            _stopCts ??= new CancellationTokenSource();
            var job = new DownloadJob(id, string.Join(", ", names.Take(3)), "排队", 0, null);
            _jobs.Add(job);
            JobChanged?.Invoke(job);
            _runningTasks.RemoveAll(static task => task.IsCompleted);
            _runningTasks.Add(RunAsync(job, packages, _stopCts.Token));
        }
        return Task.FromResult(id);
    }

    #endregion

    #region 下载作业执行
    private async Task RunAsync(DownloadJob job, List<PluginBrowseDataGridModel> packages, CancellationToken token)
    {
        await Task.Yield();
        var names = packages.Select(m => m.PackName).ToList();
        var entered = false;
        try
        {
            await _runGate.WaitAsync(token);
            entered = true;
            token.ThrowIfCancellationRequested();
            if (!await IsSdkAvailableAsync())
            {
                Update(job with { Status = "失败", Error = "服务器未安装 .NET SDK，无法下载插件" });
                return;
            }
            token.ThrowIfCancellationRequested();
            Update(job with { Status = "下载中", Progress = 10 });
            _logger.Push($"[Info] 开始下载插件: {job.PackName}");
            using var handler = new PluginDownloadHandler(WebPaths.FilePath);
            var ok = await handler.DownloadAsync(packages, zip: true, token);
            token.ThrowIfCancellationRequested();
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
            var installResults = await TryAutoInstallAsync(names, token);
            if (token.IsCancellationRequested)
            {
                Update(job with { Status = "已取消", Progress = installResults > 0 ? 100 : 0, InstalledCount = installResults });
                _logger.Push($"[Warn] 插件安装已停止: {job.PackName}，已安装 {installResults} 个接口");
                return;
            }
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
        finally
        {
            if (entered) _runGate.Release();
        }
    }

    #endregion

    #region 自动安装
    /// <summary>
    /// 下载即安装：把 lib/{name}/ 探测类型后归位到 lib/{type}/{name}/ 并 InitPlugin 注册。
    /// 同名插件执行热更新（对齐上传路径）：停使用该插件的设备 → 卸载旧程序集 → 替换目录 → 恢复设备采集。
    /// 返回成功安装的接口数。
    /// </summary>
    private async Task<int> TryAutoInstallAsync(List<string> names, CancellationToken token)
    {
        var installed = 0;
        var stopped = new List<DeviceRuntime>();
        var serverServicesNeedRestart = false;
        try
        {
            foreach (var name in names)
            {
                if (token.IsCancellationRequested) break;
                try
                {
                    var srcPath = Path.Combine(WebPaths.FilePath, name);
                    if (!Directory.Exists(srcPath)) continue;
                    // 探测 Daq / Mq 接口
                    foreach (var type in new[] { Snet.Model.@enum.PluginType.Daq, Snet.Model.@enum.PluginType.Mq })
                    {
                        // 探测可能加载程序集；从这里到配置落盘必须作为一个提交单元完成。
                        if (token.IsCancellationRequested) break;
                        var iName = $"Snet.Model.interface.I{type}";
                        var result = PluginHandlerCore.PluginOperate.InitPlugin(srcPath, iName);
                        if (result.Count == 0) continue;
                        // 归位到 lib/{type小写}/{name}/
                        var typePath = Path.Combine(WebPaths.FilePath, type.ToString().ToLower());
                        var targetPath = Path.Combine(typePath, name);
                        Directory.CreateDirectory(typePath);
                        var isHotUpdate = Directory.Exists(targetPath) || LoadPluginList().Any(p => p.Name == name);
                        string? backupPath = null;
                        if (isHotUpdate)
                        {
                            serverServicesNeedRestart = true;
                            _logger.Push($"[Info] 检测到同名插件 {name}，执行热更新");
                            // 停用使用该插件的运行设备：设备插件类型是插件类名（如 SiemensOperate），
                            // 下载名是包名（如 Snet.Siemens），类名→包名映射由 RuntimeManager 统一处理
                            // （对齐 WPF libPath == DaqPluginPath 语义）
                            stopped.AddRange(await _runtimeManager.StopDevicesUsingPluginAsync(type, name));
                            // 卸载程序集前优雅停止 UA/MQTT 服务端：释放监听端口，防僵尸 socket 占用导致新服务端绑定失败
                            try { await _hosted.StopServerServicesAsync(); }
                            catch (Exception ex) { _logger.Push($"[Error] 服务端停止失败: {ex.Message}"); }
                            // 卸载旧程序集并回收，避免文件锁/旧实例残留（对齐 WPF PrivateRemovalPlugin）
                            foreach (var old in LoadPluginList().Where(p => p.Name == name))
                            {
                                try { await PluginHandlerCore.PluginOperate.RemovePluginAsync(old.Name); } catch { /* 未注册/已卸载忽略 */ }
                            }
                        }
                        if (Directory.Exists(targetPath))
                        {
                            backupPath = targetPath + ".backup-" + Guid.NewGuid().ToString("N");
                            try
                            {
                                Directory.Move(targetPath, backupPath);
                            }
                            catch (IOException)
                            {
                                await Task.Run(() =>
                                {
                                    GC.Collect();
                                    GC.WaitForPendingFinalizers();
                                });
                                Directory.Move(targetPath, backupPath);
                            }
                        }
                        try
                        {
                            Directory.Move(srcPath, targetPath);
                        }
                        catch
                        {
                            if (backupPath is not null && Directory.Exists(backupPath) && !Directory.Exists(targetPath))
                                Directory.Move(backupPath, targetPath);
                            throw;
                        }
                        // 重新注册最终路径：探测注册的是下载临时目录（Move 后失效），
                        // 不重注册则设备启动采集报"插件尚未加载"（对齐 WPF InitPlugin(libPath) 流程）
                        foreach (var (model, _) in result)
                        {
                            try { await PluginHandlerCore.PluginOperate.RemovePluginAsync(model.Name); } catch { /* 未注册忽略 */ }
                        }
                        try
                        {
                            var registered = PluginHandlerCore.PluginOperate.InitPlugin(targetPath, iName);
                            if (registered.Count == 0)
                                throw new InvalidDataException($"插件 {name} 在最终目录中未发现 {iName} 实现");
                        }
                        catch (Exception ex)
                        {
                            _logger.Push($"[Error] 插件运行时重新注册失败 {name}: {ex.Message}");
                            if (Directory.Exists(targetPath)) Directory.Delete(targetPath, true);
                            if (backupPath is not null && Directory.Exists(backupPath))
                            {
                                Directory.Move(backupPath, targetPath);
                                PluginHandlerCore.PluginOperate.InitPlugin(targetPath, iName);
                            }
                            throw;
                        }
                        var pluginList = LoadPluginList();
                        foreach (var (model, _) in result)
                        {
                            model.Path = targetPath;
                            var plugin = new PluginListModel(model.Name, type, model.Version, DateTime.Now, model);
                            var index = pluginList.FindIndex(p => p.Name == plugin.Name);
                            if (index >= 0) pluginList[index] = plugin;
                            else pluginList.Add(plugin);
                        }
                        await _appState.ConfigSaveGate.WaitAsync();
                        try
                        {
                            if (!await ProjectHandlerCore.WriteToFileWithRetryAsync(WebPaths.PluginListConfigPath, pluginList.ToJson(true)))
                                throw new IOException($"插件列表配置写入失败: {name}");
                        }
                        catch
                        {
                            foreach (var (model, _) in result)
                            {
                                try { await PluginHandlerCore.PluginOperate.RemovePluginAsync(model.Name); } catch { /* 回滚继续 */ }
                            }
                            if (Directory.Exists(targetPath)) Directory.Delete(targetPath, true);
                            if (backupPath is not null && Directory.Exists(backupPath))
                            {
                                Directory.Move(backupPath, targetPath);
                                PluginHandlerCore.PluginOperate.InitPlugin(targetPath, iName);
                            }
                            throw;
                        }
                        finally
                        {
                            _appState.ConfigSaveGate.Release();
                        }
                        if (backupPath is not null && Directory.Exists(backupPath))
                        {
                            try { Directory.Delete(backupPath, true); }
                            catch (Exception ex) { _logger.Push($"[Warning] 插件备份目录清理失败 {backupPath}: {ex.Message}"); }
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
            if (serverServicesNeedRestart)
            {
                try { await _hosted.InitServerServicesAsync(); }
                catch (Exception ex) { _logger.Push($"[Error] 热更新后恢复服务端失败: {ex.Message}"); }
            }
            // 恢复热更新前正在运行的设备（只恢复本次停掉的）。
            // 对齐 WPF PrivateInit：更新完成后走 Retry（重置计时 → 停止 → 用新插件重建 handler 后启动采集）
            // 单个设备恢复失败不影响其他设备与安装结果（异常不外抛覆盖 installed 计数）
            foreach (var rt in stopped.Distinct())
            {
                try { await rt.RetryAsync(); }
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

    /// <summary>取消并等待管理器拥有的全部下载作业，然后释放同步资源。</summary>
    public async ValueTask DisposeAsync()
    {
        Task[] tasks;
        lock (_gate)
        {
            _stopCts?.Cancel();
            tasks = _runningTasks.ToArray();
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        lock (_gate)
        {
            _stopCts?.Dispose();
            _stopCts = null;
            _runningTasks.Clear();
        }
        _runGate.Dispose();
        GC.SuppressFinalize(this);
    }
    #endregion
}
