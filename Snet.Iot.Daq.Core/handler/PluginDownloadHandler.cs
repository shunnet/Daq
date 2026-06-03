using Snet.Core.extend;
using Snet.Core.handler;
using Snet.Iot.Daq.Core.data;
using Snet.Model.data;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
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

        // ============ 实例字段 ============
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
        public PluginDownloadHandler() : base()
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
            var oldCts = Interlocked.Exchange(ref _globalCts, new CancellationTokenSource());
            try
            {
                oldCts.Cancel();
            }
            finally
            {
                oldCts.Dispose();
            }
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
        private async Task<bool> DownloadInternalAsync(IEnumerable<dynamic> packages, bool zip, CancellationToken cancellationToken)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_globalCts.Token, cancellationToken);
            var token = linkedCts.Token;

            var successPackages = new ConcurrentBag<string>();
            var tasks = packages.Select(async pkg =>
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
                    OnInfoEventHandlerAsync(this, EventInfoResult.CreateFailureResult($"{msg} [{pkg.PackName}]: {ex.Message}"));
                }
                finally
                {
                    PublishSemaphore.Release();
                }
            });

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                OnInfoEventHandlerAsync(this, EventInfoResult.CreateFailureResult(LanguageHandler.GetLanguage() == Model.@enum.LanguageType.zh ? "下载已被取消" : "Download canceled"));
                return false;
            }

            if (zip && !successPackages.IsEmpty)
            {
                await ZipPackagesAsync(successPackages.ToList(), token);
            }

            return successPackages.Count == packages.Count();
        }

        /// <summary>
        /// 发布单个 NuGet 包为运行时文件（输出目录名即为包名，不再添加 .Pack 后缀）
        /// </summary>
        private async Task PublishSinglePackageAsync(string packageName, string? version, CancellationToken cancellationToken)
        {
            // 输出目录直接使用包名
            string outDir = Path.Combine(_pluginStoragePath, packageName);
            string workDir = Path.Combine(Path.GetTempPath(), $"{packageName}_{Guid.NewGuid():N}");

            try
            {
                SafeDelete(outDir);
                Directory.CreateDirectory(workDir);

                string projectName = $"{packageName}.Runtime";
                await RunDotnetAsync($"new classlib -n {projectName}", workDir, cancellationToken);
                string projectDir = Path.Combine(workDir, projectName);

                string packageRef = version != null
                    ? $"{packageName} --version {version}"
                    : packageName;
                await RunDotnetAsync(
                    $"add package {packageRef} --source {DefaultNugetSource}",
                    projectDir, cancellationToken);

                await RunDotnetAsync(
                    $"publish -c Release -o \"{outDir}\"",
                    projectDir, cancellationToken);

                OnInfoEventHandlerAsync(this, EventInfoResult.CreateSuccessResult($"[OK] {packageName} {version ?? "latest"}"));
            }
            finally
            {
                SafeDelete(workDir);
            }
        }

        /// <summary>
        /// 将发布成功的包目录压缩为 ZIP（并行且节流，不卡界面）
        /// </summary>
        private async Task ZipPackagesAsync(List<string> packageNames, CancellationToken cancellationToken)
        {
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

                    if (File.Exists(zip))
                        File.Delete(zip);

                    // 在独立线程上执行压缩，避免占用线程池导致界面卡顿
                    await Task.Factory.StartNew(() =>
                        ZipFile.CreateFromDirectory(dir, zip, CompressionLevel.Optimal, false),
                        cancellationToken,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default);

                    OnInfoEventHandlerAsync(this,
                        EventInfoResult.CreateSuccessResult($"[ZIP] {Path.GetFileName(zip)}"));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    OnInfoEventHandlerAsync(this,
                        EventInfoResult.CreateFailureResult($"[ZIP FAIL] {pkg}: {ex.Message}"));
                }
                finally
                {
                    ZipSemaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// 异步执行 dotnet 命令，带超时和取消支持
        /// </summary>
        private async Task RunDotnetAsync(string args, string workDir, CancellationToken cancellationToken)
        {
            var psi = new ProcessStartInfo
            {
                FileName = DotnetExe,
                Arguments = args,
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(DefaultTimeoutMs);

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                }
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationToken);
                throw new TimeoutException($"dotnet 命令超时 ({DefaultTimeoutMs / 1000}s): {args}");
            }

            string stdOut = await outputTask;
            string stdErr = await errorTask;

            if (process.ExitCode != 0)
                throw new Exception($"dotnet {args} 失败 (ExitCode={process.ExitCode}): {stdErr}");

            //OnInfoEventHandlerAsync(this, EventInfoResult.CreateSuccessResult($"[dotnet] {args}: {stdOut.Trim()}"));
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

        private void SafeDelete(string path)
        {
            if (Directory.Exists(path))
            {
                try { Directory.Delete(path, true); }
                catch (Exception ex)
                {
                    OnInfoEventHandlerAsync(this,
                        EventInfoResult.CreateFailureResult($"删除目录失败 {path}: {ex.Message}"));
                }
            }
        }

        // ============ 资源释放 ============
        public override void Dispose()
        {
            if (!_disposed)
            {
                _globalCts.Cancel();
                _globalCts.Dispose();
                _disposed = true;
            }
            base.Dispose();
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _globalCts.Cancel();
                _globalCts.Dispose();
                _disposed = true;
            }
            await base.DisposeAsync();
        }
    }
}