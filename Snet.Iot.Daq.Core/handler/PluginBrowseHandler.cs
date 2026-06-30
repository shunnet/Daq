using NuGet.Versioning;
using Snet.Core.extend;
using Snet.Iot.Daq.Core.data;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Xml.Linq;

namespace Snet.Iot.Daq.Core.handler
{
    /// <summary>
    /// 插件浏览处理器
    /// 用于从 NuGet.org 获取指定插件的元数据（版本、作者、依赖等）
    /// 支持单个或批量查询，自动选择最新稳定版（可含预发行版）
    /// 采用并发控制提升批量请求速度
    /// </summary>
    public class PluginBrowseHandler : CoreUnify<PluginBrowseHandler, string>, IDisposable, IAsyncDisposable
    {
        #region 数据模型

        /// <summary>.nuspec 文件中的包元数据</summary>
        public class NuspecMetadata
        {
            /// <summary>包 ID（唯一标识）</summary>
            public string Id { get; set; }
            /// <summary>版本号</summary>
            public string Version { get; set; }
            /// <summary>显示标题</summary>
            public string Title { get; set; }
            /// <summary>作者</summary>
            public string Authors { get; set; }
            /// <summary>所有者</summary>
            public string Owners { get; set; }
            /// <summary>描述信息</summary>
            public string Description { get; set; }
            /// <summary>发行说明</summary>
            public string ReleaseNotes { get; set; }
            /// <summary>摘要</summary>
            public string Summary { get; set; }
            /// <summary>版权声明</summary>
            public string Copyright { get; set; }
            /// <summary>标签（空格分隔）</summary>
            public string Tags { get; set; }
            /// <summary>项目主页 URL</summary>
            public string ProjectUrl { get; set; }
            /// <summary>旧式图标 URL（HTTP/HTTPS）</summary>
            public string IconUrl { get; set; }
            /// <summary>许可证 URL</summary>
            public string LicenseUrl { get; set; }
            /// <summary>是否需要接受许可证才能安装</summary>
            public bool RequireLicenseAcceptance { get; set; }
            /// <summary>按目标框架分组的依赖项列表</summary>
            public List<DependencyGroup> Dependencies { get; set; } = new List<DependencyGroup>();
        }

        /// <summary>一个目标框架下的依赖集合</summary>
        public class DependencyGroup
        {
            /// <summary>目标框架，例如 ".NETFramework4.7.2" 或 "net6.0"</summary>
            public string TargetFramework { get; set; }
            /// <summary>该框架下的具体依赖包</summary>
            public List<Dependency> Dependencies { get; set; } = new List<Dependency>();
        }

        /// <summary>单个依赖包信息</summary>
        public class Dependency
        {
            /// <summary>依赖包 ID</summary>
            public string Id { get; set; }
            /// <summary>依赖包的版本范围（如 "6.0.0" 或 "[1.0.0, 2.0.0)"）</summary>
            public string Version { get; set; }
        }

        #endregion

        /// <summary>释放标志，防止重复释放</summary>
        private bool _disposed = false;

        /// <summary>预定义的 Snet 插件包名列表（采集/传输）</summary>
        private static readonly List<string> _plugins = new()
        {
            // 传输插件
            "Snet.Mqtt",
            "Snet.Kafka",
            "Snet.RabbitMQ",
            "Snet.Netty",
            "Snet.NetMQ",

            // 采集插件
            "Snet.AllenBradley",
            "Snet.Beckhoff",
            "Snet.DB",
            "Snet.Delta",
            "Snet.Fatek",
            "Snet.Fuji",
            "Snet.GE",
            "Snet.Inovance",
            "Snet.Invt",
            "Snet.Keyence",
            "Snet.LSis",
            "Snet.MegMeet",
            "Snet.Mitsubishi",
            "Snet.Modbus",
            "Snet.Opc",
            "Snet.Panasonic",
            "Snet.PQDIF",
            "Snet.Siemens",
            "Snet.Omron",
            "Snet.Sim",
            "Snet.TEP",
            "Snet.Toyota",
            "Snet.Vigor",
            "Snet.WeCon",
            "Snet.XinJE",
            "Snet.Yamatake",
            "Snet.Yaskawa",
            "Snet.Yokogawa",
            "Snet.Freedom",
            "Snet.RKC",
            "Snet.Turck",
            "Snet.Fanuc",
            "Snet.OrientalMotor",
            "Snet.Kossi",
            "Snet.YuDian",
        };

        /// <summary>用于 HTTP 请求的客户端，生命周期由本类管理</summary>
        private readonly HttpClient _httpClient;

        /// <summary>版本发布时间缓存，避免同一包的注册索引被重复请求</summary>
        private readonly ConcurrentDictionary<string, Dictionary<string, DateTime>> _versionTimesCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 无参构造函数（建议仅在反射/序列化时使用）
        /// </summary>
        public PluginBrowseHandler() : base()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        /// <summary>
        /// 带序列号的构造函数
        /// </summary>
        public PluginBrowseHandler(string sn) : base(sn)
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        #region 核心方法

        /// <summary>
        /// 获取所有预定义插件的 nuspec 元数据（最新稳定版或预发行版）
        /// 单个包获取失败不会中断整体流程，错误将被输出到 Debug 控制台
        /// </summary>
        /// <param name="includePrerelease">是否包含预发行版本，默认 false（仅稳定版）</param>
        /// <returns>成功获取的元数据列表</returns>
        public async Task<List<NuspecMetadata>> GetNuspecAsync(bool includePrerelease = false, CancellationToken cancellationToken = default)
        {
            var results = new ConcurrentBag<NuspecMetadata>();
            await Parallel.ForEachAsync(_plugins,
                new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken },
                async (packageName, ct) =>
                {
                    try
                    {
                        var metadata = await GetNuspecAsync(packageName, version: null, includePrerelease, ct);
                        if (metadata != null)
                            results.Add(metadata);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"获取包 {packageName} 信息失败: {ex.Message}");
                    }
                });

            return results.ToList();
        }

        /// <summary>
        /// 获取指定包的 .nuspec 元数据
        /// </summary>
        /// <param name="packageName">包名（大小写不敏感）</param>
        /// <param name="version">版本号（可选），为 null 时自动获取最新稳定版</param>
        /// <param name="includePrerelease">仅在 version 为 null 时生效，是否包含预发行版本</param>
        /// <returns>解析后的元数据对象</returns>
        public async Task<NuspecMetadata> GetNuspecAsync(string packageName, string version = null, bool includePrerelease = false, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                throw new ArgumentException("包名不能为空", nameof(packageName));

            string lowerName = packageName.ToLowerInvariant();

            // 1. 若未指定版本，获取版本列表并选择最新版（同时获取所有版本的发布时间，供后续使用）
            if (string.IsNullOrWhiteSpace(version))
            {
                var versionTimes = await GetVersionPublishedTimesAsync(lowerName);
                if (versionTimes == null || versionTimes.Count == 0)
                    throw new InvalidOperationException($"未找到包 '{packageName}' 的任何版本。");

                var candidates = versionTimes.Keys
                    .Select(v => NuGetVersion.TryParse(v, out var nv) ? nv : null)
                    .Where(v => v != null);

                if (!includePrerelease)
                    candidates = candidates.Where(v => !v.IsPrerelease);

                var latest = candidates.OrderByDescending(v => v).FirstOrDefault();
                if (latest == null)
                    throw new InvalidOperationException($"包 '{packageName}' 没有符合条件的版本（includePrerelease={includePrerelease}）。");

                version = latest.ToNormalizedString();
            }

            // 2. 构建 nuspec 文件 URL 并下载
            string nuspecUrl = $"https://api.nuget.org/v3-flatcontainer/{lowerName}/{version}/{lowerName}.nuspec";
            var response = await _httpClient.GetAsync(nuspecUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            string xml = await response.Content.ReadAsStringAsync();
            return ParseNuspec(xml);
        }

        /// <summary>
        /// 获取单个插件的 PluginBrowseDataGridModel（用于界面列表绑定）
        /// </summary>
        /// <param name="packageName">包名</param>
        /// <param name="version">版本号，为 null 时获取最新稳定版</param>
        /// <param name="includePrerelease">是否包含预发行版</param>
        /// <param name="index">序号（用于列表显示）</param>
        public async Task<PluginBrowseDataGridModel> GetPluginBrowseDataGridModelAsync(string packageName, string version = null, bool includePrerelease = false, int index = 0, CancellationToken cancellationToken = default)
        {
            // 1. 获取 nuspec 元数据（包含作者、描述、图标等）
            var metadata = await GetNuspecAsync(packageName, version, includePrerelease, cancellationToken);

            // 2. 获取发布时间（利用缓存，GetNuspecAsync 内部已填充）
            DateTime publishedTime = await GetPublishedTimeAsync(packageName, metadata.Version);

            // 3. 下载图标为字节数组
            byte[]? iconBytes = await DownloadIconAsync(packageName, metadata.Version, metadata.IconUrl);

            return new PluginBrowseDataGridModel
            {
                Index = index,
                Icon = iconBytes,               // 字节数组，界面需转换器
                PackName = metadata.Id,
                Version = metadata.Version,
                Describe = metadata.Description ?? metadata.Summary,  // 优先用 Description
                UpdateTime = publishedTime == DateTime.MinValue ? DateTime.Now : publishedTime
            };
        }

        /// <summary>
        /// 批量获取所有预定义插件的 PluginBrowseDataGridModel 列表（并发请求，提升速度）
        /// </summary>
        /// <param name="includePrerelease">是否包含预发行版</param>
        /// <returns>可用于 DataGrid 绑定的模型列表</returns>
        public async Task<List<PluginBrowseDataGridModel>> GetPluginBrowseDataGridModelsAsync(bool includePrerelease = false)
        {
            var semaphore = new SemaphoreSlim(16); // 并发上限，避免被服务器限流
            var tasks = _plugins.Select(async (pkg, idx) =>
            {
                await semaphore.WaitAsync();
                try
                {
                    return await GetPluginBrowseDataGridModelAsync(pkg, version: null, includePrerelease, idx + 1);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"获取 {pkg} 数据失败: {ex.Message}");
                    return null;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks);
            return results.Where(r => r != null).ToList()!;
        }

        /// <summary>
        /// 从 NuGet 注册接口获取指定版本的发布时间
        /// </summary>
        /// <param name="packageName">包名</param>
        /// <param name="version">精确版本号</param>
        /// <returns>发布时间；若未找到则返回 DateTime.MinValue</returns>
        private async Task<DateTime> GetPublishedTimeAsync(string packageName, string version)
        {
            try
            {
                // 优化：直接利用 GetVersionPublishedTimesAsync 获取所有版本时间，避免重复请求
                var allTimes = await GetVersionPublishedTimesAsync(packageName.ToLowerInvariant());
                if (allTimes != null && allTimes.TryGetValue(version, out var time))
                    return time;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取发布时间失败 ({packageName} {version}): {ex.Message}");
            }
            return DateTime.MinValue;
        }

        /// <summary>
        /// 获取指定包的所有版本及其发布时间（通过注册接口）
        /// </summary>
        private async Task<Dictionary<string, DateTime>> GetVersionPublishedTimesAsync(string lowerPackageName)
        {
            if (_versionTimesCache.TryGetValue(lowerPackageName, out var cached))
                return cached;

            string registrationUrl = $"https://api.nuget.org/v3/registration5-gz-semver2/{lowerPackageName}/index.json";
            var response = await _httpClient.GetAsync(registrationUrl);
            response.EnsureSuccessStatusCode();

            using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var dict = new Dictionary<string, DateTime>();

            foreach (var page in jsonDoc.RootElement.GetProperty("items").EnumerateArray())
            {
                if (page.TryGetProperty("items", out var leafs))
                {
                    foreach (var leaf in leafs.EnumerateArray())
                    {
                        if (leaf.TryGetProperty("catalogEntry", out var entry) &&
                            entry.TryGetProperty("version", out var verProp))
                        {
                            string ver = verProp.GetString()!;
                            DateTime pubTime = DateTime.MinValue;
                            if (entry.TryGetProperty("published", out var pubProp))
                            {
                                string pubStr = pubProp.GetString();
                                DateTime.TryParse(pubStr, out pubTime);
                            }
                            dict[ver] = pubTime;
                        }
                    }
                }
            }

            _versionTimesCache[lowerPackageName] = dict;
            return dict;
        }

        /// <summary>
        /// 下载图标字节数组（优先旧式 iconUrl，其次通过 NuGet 图标端点）
        /// </summary>
        private async Task<byte[]?> DownloadIconAsync(string packageName, string version, string? iconUrl)
        {
            // 1. 旧式 iconUrl（直接下载）
            if (!string.IsNullOrWhiteSpace(iconUrl))
            {
                try
                {
                    var resp = await _httpClient.GetAsync(iconUrl);
                    resp.EnsureSuccessStatusCode();
                    return await resp.Content.ReadAsByteArrayAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"旧式 iconUrl 下载失败 ({packageName}): {ex.Message}");
                }
            }

            // 2. 新式内嵌图标（通过 NuGet 统一端点）
            string lowerName = packageName.ToLowerInvariant();
            string iconEndpoint = $"https://api.nuget.org/v3-flatcontainer/{lowerName}/{version}/icon";
            try
            {
                var resp = await _httpClient.GetAsync(iconEndpoint);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsByteArrayAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NuGet 图标端点下载失败 ({packageName} {version}): {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 解析 .nuspec XML 为 NuspecMetadata 对象
        /// 兼容 2010/2013 版本的不同命名空间
        /// </summary>
        /// <param name="xml">原始 nuspec 文件内容</param>
        /// <returns>解析后的元数据</returns>
        private NuspecMetadata ParseNuspec(string xml)
        {
            var doc = XDocument.Parse(xml);
            var metadataEl = doc.Root?.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "metadata");

            if (metadataEl == null)
                throw new FormatException("无效的 .nuspec 文件：缺少 <metadata> 元素。");

            // 一次性扫描所有子元素到字典，避免重复 Linear Scan
            var elementDict = metadataEl.Elements()
                .ToDictionary(e => e.Name.LocalName, e => e.Value, StringComparer.OrdinalIgnoreCase);

            var md = new NuspecMetadata
            {
                Id = elementDict.GetValueOrDefault("id"),
                Version = elementDict.GetValueOrDefault("version"),
                Title = elementDict.GetValueOrDefault("title"),
                Authors = elementDict.GetValueOrDefault("authors"),
                Owners = elementDict.GetValueOrDefault("owners"),
                Description = elementDict.GetValueOrDefault("description"),
                ReleaseNotes = elementDict.GetValueOrDefault("releaseNotes"),
                Summary = elementDict.GetValueOrDefault("summary"),
                Copyright = elementDict.GetValueOrDefault("copyright"),
                Tags = elementDict.GetValueOrDefault("tags"),
                ProjectUrl = elementDict.GetValueOrDefault("projectUrl"),
                IconUrl = elementDict.GetValueOrDefault("iconUrl"),
                LicenseUrl = elementDict.GetValueOrDefault("licenseUrl"),
                RequireLicenseAcceptance = string.Equals(
                    elementDict.GetValueOrDefault("requireLicenseAcceptance"),
                    "true", StringComparison.OrdinalIgnoreCase)
            };

            // 依赖项解析
            var dependenciesEl = metadataEl.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "dependencies");
            if (dependenciesEl != null)
            {
                // 分组依赖（新格式）
                foreach (var group in dependenciesEl.Elements().Where(e => e.Name.LocalName == "group"))
                {
                    var tfm = group.Attribute("targetFramework")?.Value ?? string.Empty;
                    var deps = new List<Dependency>();
                    foreach (var depEl in group.Elements().Where(e => e.Name.LocalName == "dependency"))
                    {
                        deps.Add(new Dependency
                        {
                            Id = depEl.Attribute("id")?.Value,
                            Version = depEl.Attribute("version")?.Value
                        });
                    }
                    md.Dependencies.Add(new DependencyGroup { TargetFramework = tfm, Dependencies = deps });
                }

                // 未分组依赖（旧格式，视为适用于所有框架）
                var flatDeps = dependenciesEl.Elements()
                    .Where(e => e.Name.LocalName == "dependency")
                    .ToList();
                if (flatDeps.Any())
                {
                    var deps = flatDeps.Select(d => new Dependency
                    {
                        Id = d.Attribute("id")?.Value,
                        Version = d.Attribute("version")?.Value
                    }).ToList();
                    md.Dependencies.Add(new DependencyGroup { TargetFramework = "all", Dependencies = deps });
                }
            }

            return md;
        }

        #endregion

        #region 资源释放（支持同步和异步释放）

        /// <summary>
        /// 释放托管资源（HttpClient）
        /// </summary>
        public override void Dispose()
        {
            if (!_disposed)
            {
                _httpClient?.Dispose();
                _disposed = true;
            }
            base.Dispose();
        }

        /// <summary>
        /// 异步释放资源
        /// </summary>
        public override async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                if (_httpClient != null)
                {
                    _httpClient.Dispose();
                }
                _disposed = true;
            }
            await base.DisposeAsync();
        }

        #endregion
    }
}