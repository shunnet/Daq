using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.handler;

namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// 插件下载作业管理器：包装 Core PluginDownloadHandler 为异步作业（任务状态机 + 进度推送）。
/// Core 零改动 → 无行级进度，按 排队→下载→完成/失败 状态推进。
/// </summary>
public class DownloadTaskManager
{
    private readonly LoggerBuffer _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<DownloadJob> _jobs = new();
    private CancellationTokenSource? _stopCts;

    /// <summary>取消所有进行中的下载（「停止下载」按钮）</summary>
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

    public DownloadTaskManager(LoggerBuffer logger) => _logger = logger;

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
            var installResults = TryAutoInstall(names);
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
    /// 返回成功安装的接口数。
    /// </summary>
    private static int TryAutoInstall(List<string> names)
    {
        var installed = 0;
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
                    if (Directory.Exists(targetPath)) Directory.Delete(targetPath, true);
                    Directory.Move(srcPath, targetPath);
                    foreach (var (model, _) in result)
                    {
                        model.Path = targetPath;
                        var plugin = new PluginListModel(model.Name, type, model.Version, DateTime.Now, model);
                        var list = LoadPluginList();
                        if (list.All(p => p.Name != plugin.Name))
                            list.Add(plugin);
                        PluginHandlerCore.SavePluginUIConfig(new System.Collections.ObjectModel.ObservableCollection<PluginListModel>(list), WebPaths.PluginListConfigPath);
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
        return installed;
    }

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
}
