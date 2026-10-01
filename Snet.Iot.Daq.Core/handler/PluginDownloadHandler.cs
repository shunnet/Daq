using Snet.Core.extend;
using Snet.Core.handler;
using Snet.Iot.Daq.Core.data;
using Snet.Model.data;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Snet.Iot.Daq.Core.handler
{
    /// <summary>
    /// 插件下载处理器
    /// 通过 dotnet CLI 将指定 NuGet 包发布为运行时文件，并可选打包为 ZIP
    /// </summary>
    public class PluginDownloadHandler : CoreUnify<PluginDownloadHandler, string>, IDisposable, IAsyncDisposable
    {
        // ============ 静态配置 ============
        /// <summary>dotnet 可执行文件路径（优先环境变量 DOTNET_ROOT，其次 PATH）</summary>
        private static readonly string DotnetExe = GetDotnetPath();

        /// <summary>全局 dotnet publish 并发数（避免系统过载）</summary>
        private static readonly SemaphoreSlim PublishSemaphore = new(3);

        /// <summary>全局 ZIP 压缩并发数</summary>
        private static readonly SemaphoreSlim ZipSemaphore = new(6);

        /// <summary>默认命令超时（10分钟）</summary>
        private const int DefaultTimeoutMs = 10 * 60 * 1000;

        /// <summary>NuGet 源地址（可改为私有源）</summary>
        private const string DefaultNugetSource = "https://api.nuget.org/v3/index.json";

        /// <summary>包名白名单（仅允许字母数字、下划线、点、连字符；杜绝路径穿越与参数注入）</summary>
        private static readonly System.Text.RegularExpressions.Regex s_packageNameRegex =
            new(@"^[A-Za-z0-9_\.\-]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>版本号白名单（如 1.0.0 或 1.0.0-beta.1）</summary>
        private static readonly System.Text.RegularExpressions.Regex s_versionRegex =
            new(@"^[0-9]+(\.[0-9]+){0,3}(-[A-Za-z0-9\.\-]+)?$", System.Text.RegularExpressions.RegexOptions.Compiled);

        // ============ 实例字段 ============
        /// <summary>短同步边界只用于发布任务、令牌和终态，不在锁内等待 I/O。</summary>
        private readonly object lifecycleGate = new();
        private readonly List<Task> activeDownloads = new();
        private Task? disposalTask;
        private bool _disposed;
        private readonly string _pluginStoragePath;
        private CancellationTokenSource _globalCts = new CancellationTokenSource();

        /// <summary>
        /// 插件包发布后的存储根目录
        /// </summary>
        public string PluginStoragePath => _pluginStoragePath;

        // ============ 构造函数 ============
        /// <summary>
        /// 无参构造（默认使用临时目录作为存储路径）
        /// </summary>
        public PluginDownloadHandler() : this(Path.Combine(Path.GetTempPath(), "Snet.Iot.Daq", "plugins"))
        {
        }

        /// <summary>
        /// 带存储路径的构造函数
        /// </summary>
        /// <param name="path">插件下载存储根目录</param>
        public PluginDownloadHandler(string path) : base(path)
        {
            _pluginStoragePath = path ?? throw new ArgumentNullException(nameof(path));
            Directory.CreateDirectory(_pluginStoragePath);
        }

        /// <summary>
        /// 停止所有正在进行的下载任务（包括发布与压缩）
        /// </summary>
        public void Stop()
        {
            CancellationTokenSource source;
            lock (lifecycleGate)
            {
                if (_disposed) return;
                source = _globalCts;
                _globalCts = new CancellationTokenSource();
            }
            try { source.Cancel(); }
            finally { source.Dispose(); }
        }

        // ============ 公开下载方法 ============
        /// <inheritdoc cref="DownloadAsync(List{string}, bool, CancellationToken)"/>
        public Task<bool> DownloadAsync(PluginBrowseDataGridModel model, bool zip, CancellationToken cancellationToken = default)
            => DownloadAsync(new List<PluginBrowseDataGridModel> { model }, zip, cancellationToken);

        /// <summary>
        /// 批量下载插件（按模型列表），支持版本号
        /// </summary>
        public async Task<bool> DownloadAsync(List<PluginBrowseDataGridModel> models, bool zip, CancellationToken cancellationToken = default)
        {
            if (models == null || models.Count == 0)
                return false;

            var downloadTasks = models.Select(m => new { m.PackName, m.Version });
            return await DownloadInternalAsync(downloadTasks, zip, cancellationToken);
        }

        /// <inheritdoc cref="DownloadAsync(List{string}, bool, CancellationToken)"/>
        public Task<bool> DownloadAsync(string name, bool zip, CancellationToken cancellationToken = default)
            => DownloadAsync(new List<string> { name }, zip, cancellationToken);

        /// <summary>
        /// 批量下载插件（按包名列表，自动使用最新稳定版）
        /// </summary>
        public Task<bool> DownloadAsync(List<string> names, bool zip, CancellationToken cancellationToken = default)
        {
            var tasks = names.Select(n => new { PackName = n, Version = (string?)null });
            return DownloadInternalAsync(tasks, zip, cancellationToken);
        }

        // ============ 内部核心逻辑 ============
        /// <summary>在同步边界内创建链接令牌并登记完整作业，保证停止或释放不会与令牌注册交错。</summary>
        private Task<bool> DownloadInternalAsync(IEnumerable<dynamic> packages, bool zip, CancellationToken cancellationToken)
        {
            lock (lifecycleGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var linked = CancellationTokenSource.CreateLinkedTokenSource(_globalCts.Token, cancellationToken);
                Task<bool> task;
                try { task = DownloadCoreAsync(packages.ToList(), zip, linked); }
                catch { linked.Dispose(); throw; }
                activeDownloads.RemoveAll(static task => task.IsCompleted);
                activeDownloads.Add(task);
                return task;
            }
        }

        /// <summary>执行一次批量发布及压缩；拥有链接令牌，并将取消转换为原有的 false 返回契约。</summary>
        private async Task<bool> DownloadCoreAsync(List<dynamic> packageList, bool zip, CancellationTokenSource source)
        {
            // 先登记所有权，再允许外部消息回调或 I/O 执行。
            await Task.Yield();
            using var linkedCts = source;
            var token = source.Token;

            var successPackages = new ConcurrentBag<string>();
            var tasks = packageList.Select(async pkg =>
            {
                await PublishSemaphore.WaitAsync(token);
                try
                {
                    await PublishSinglePackageAsync(pkg.PackName, pkg.Version as string, token);
                    successPackages.Add(pkg.PackName);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    string msg = LanguageHandler.GetLanguage() == Model.@enum.LanguageType.zh ? "下载失败" : "Download failed";
                    await OnInfoEventHandlerAsync(this, EventInfoResult.CreateFailureResult($"{msg} [{pkg.PackName}]: {ex.Message}"));
                }
                finally
                {
                    PublishSemaphore.Release();
                }
            });

            try
            {
                await Task.WhenAll(tasks);
                if (zip && !successPackages.IsEmpty && !await ZipPackagesAsync(successPackages.ToList(), token))
                    return false;
                return successPackages.Count == packageList.Count;
            }
            catch (OperationCanceledException)
            {
                await OnInfoEventHandlerAsync(this, EventInfoResult.CreateFailureResult(LanguageHandler.GetLanguage() == Model.@enum.LanguageType.zh ? "下载已被取消" : "Download canceled"));
                return false;
            }


        }

        /// <summary>
        /// 发布单个 NuGet 包为运行时文件（输出目录名即为包名，不再添加 .Pack 后缀）
        /// </summary>
        private async Task PublishSinglePackageAsync(string packageName, string? version, CancellationToken cancellationToken)
        {
            // 包名/版本白名单校验：杜绝参数注入（如 --source http://evil）与路径穿越（如 ..\..\x）
            if (string.IsNullOrWhiteSpace(packageName) ||
                !s_packageNameRegex.IsMatch(packageName) || packageName == "." ||
                packageName.Contains("..", StringComparison.Ordinal))
            {
                throw new ArgumentException($"非法的包名：{packageName}");
            }
            if (version != null && !s_versionRegex.IsMatch(version))
            {
                throw new ArgumentException($"非法的版本号：{version}");
            }

            // 输出目录直接使用包名
            string outDir = Path.Combine(_pluginStoragePath, packageName);

            // 路径越界校验：确保输出目录仍在插件存储根目录内
            string fullOutDir = Path.GetFullPath(outDir);
            string fullStorage = Path.GetFullPath(_pluginStoragePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // 输出只能是根目录的子目录，不能等于根目录；否则清理旧输出会删除所有已装插件。
            if (fullOutDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Equals(fullStorage, StringComparison.OrdinalIgnoreCase) ||
                !fullOutDir.StartsWith(fullStorage + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"输出目录越界：{packageName}");
            }

            string workDir = Path.Combine(Path.GetTempPath(), $"{packageName}_{Guid.NewGuid():N}");

            try
            {
                await SafeDeleteAsync(outDir, throwOnFailure: true);
                Directory.CreateDirectory(workDir);

                string projectName = $"{packageName}.Runtime";
                await RunDotnetAsync(["new", "classlib", "-n", projectName], workDir, cancellationToken);
                string projectDir = Path.Combine(workDir, projectName);

                List<string> addArgs = ["add", "package", packageName, "--source", DefaultNugetSource];
                if (version != null)
                {
                    addArgs.Add("--version");
                    addArgs.Add(version);
                }
                await RunDotnetAsync(addArgs, projectDir, cancellationToken);

                await RunDotnetAsync(
                    ["publish", "-c", "Release", "-o", outDir],
                    projectDir, cancellationToken);

                await OnInfoEventHandlerAsync(this, EventInfoResult.CreateSuccessResult($"[OK] {packageName} {version ?? "latest"}"));
            }
            finally
            {
                await SafeDeleteAsync(workDir);
            }
        }

        /// <summary>
        /// 将发布成功的包目录压缩为 ZIP（并行且节流，不卡界面）
        /// </summary>
        private async Task<bool> ZipPackagesAsync(List<string> packageNames, CancellationToken cancellationToken)
        {
            int success = 0;
            var tasks = packageNames.Select(async pkg =>
            {
                await ZipSemaphore.WaitAsync(cancellationToken);
                try
                {
                    // 目录和 ZIP 文件均直接使用包名，不加 .Pack
                    string dir = Path.Combine(_pluginStoragePath, pkg);
                    string zip = dir + ".zip";

                    if (!Directory.Exists(dir))
                        throw new DirectoryNotFoundException($"目录不存在: {dir}");

                    // 分块异步压缩支持中途取消；新包完整写成之前保留已有 ZIP。
                    await PluginArchive.CreateAsync(dir, zip, cancellationToken);

                    Interlocked.Increment(ref success);
                    await OnInfoEventHandlerAsync(this,
                        EventInfoResult.CreateSuccessResult($"[ZIP] {Path.GetFileName(zip)}"));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await OnInfoEventHandlerAsync(this,
                        EventInfoResult.CreateFailureResult($"[ZIP FAIL] {pkg}: {ex.Message}"));
                }
                finally
                {
                    ZipSemaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            return success == packageNames.Count;
        }

        /// <summary>
        /// 异步执行 dotnet 命令，带超时和取消支持<br/>
        /// 使用 ArgumentList 传参，避免字符串拼接导致的参数注入
        /// </summary>
        private async Task RunDotnetAsync(IReadOnlyList<string> args, string workDir, CancellationToken cancellationToken)
        {
            var psi = new ProcessStartInfo
            {
                FileName = DotnetExe,
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (string arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = new Process { StartInfo = psi };
            process.Start();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(DefaultTimeoutMs);
            var outputTask = ReadOutputAsync(process.StandardOutput, cts.Token);
            var errorTask = ReadOutputAsync(process.StandardError, cts.Token);
            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                }
                try { await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (IOException) { }
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationToken);
                throw new TimeoutException($"dotnet 命令超时 ({DefaultTimeoutMs / 1000}s): {string.Join(' ', args)}");
            }
            string stdErr = await errorTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
                throw new Exception($"dotnet {string.Join(' ', args)} 失败 (ExitCode={process.ExitCode}): {stdErr}");

        }

        /// <summary>持续排空进程输出但最多保留 64 KiB 字符，防止长时间发布无限积累字符串或堵塞重定向管道。</summary>
        /// <param name="reader">由进程持有的标准输出或错误读取器，不在此处释放。</param>
        /// <param name="token">进程超时和取消共用的令牌。</param>
        /// <returns>用于错误诊断的有界文本前缀。</returns>
        private static async Task<string> ReadOutputAsync(StreamReader reader, CancellationToken token)
        {
            var text = new StringBuilder();
            var buffer = new char[4096];
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
            {
                var retained = Math.Min(count, 64 * 1024 - text.Length);
                if (retained > 0) text.Append(buffer, 0, retained);
            }
            return text.ToString();
        }

        // ============ 工具方法 ============
        private static string GetDotnetPath()
        {
            var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!string.IsNullOrEmpty(dotnetRoot))
            {
                string exe = Path.Combine(dotnetRoot, "dotnet.exe");
                if (File.Exists(exe)) return exe;
                exe = Path.Combine(dotnetRoot, "dotnet");
                if (File.Exists(exe)) return exe;
            }

            string[] commonPaths =
            {
                @"C:\Program Files\dotnet\dotnet.exe",
                @"/usr/share/dotnet/dotnet",
                @"/usr/local/share/dotnet/dotnet"
            };
            foreach (var path in commonPaths)
            {
                if (File.Exists(path)) return path;
            }

            return "dotnet";
        }

        /// <summary>清理本次作业拥有的目录；消息通知可等待，强制清理失败时中止发布。</summary>
        private async Task SafeDeleteAsync(string path, bool throwOnFailure = false)
        {
            if (Directory.Exists(path))
            {
                try { Directory.Delete(path, true); }
                catch (Exception ex)
                {
                    await OnInfoEventHandlerAsync(this,
                        EventInfoResult.CreateFailureResult($"删除目录失败 {path}: {ex.Message}"));
                    if (throwOnFailure)
                        throw new IOException($"无法清理旧插件目录: {path}", ex);
                }
            }
        }

        // ============ 资源释放 ============
        /// <summary>同步兼容入口；UI 应使用 DisposeAsync，以免阻塞仍需 UI 线程完成的消息回调。</summary>
        public override void Dispose() => GetDisposalTask().GetAwaiter().GetResult();

        /// <summary>进入终态、取消所有作业并等待发布及压缩退出后释放令牌和基类注册。</summary>
        public override ValueTask DisposeAsync() => new(GetDisposalTask());

        /// <summary>先在锁内发布唯一清理任务，再在锁外启动清理。</summary>
        private Task GetDisposalTask()
        {
            TaskCompletionSource completion;
            Task[] tasks;
            lock (lifecycleGate)
            {
                if (disposalTask is not null) return disposalTask;
                _disposed = true;
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                disposalTask = completion.Task;
                tasks = activeDownloads.ToArray();
            }
            _ = CompleteDisposalAsync(completion, tasks);
            return completion.Task;
        }

        /// <summary>清理流程由完成源共同拥有；即使作业失败也释放所有资源，并向调用方传递异常。</summary>
        private async Task CompleteDisposalAsync(TaskCompletionSource completion, Task[] tasks)
        {
            try
            {
                try
                {
                    _globalCts.Cancel();
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                finally
                {
                    _globalCts.Dispose();
                    base.Dispose();
                }
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        }
    }
}
