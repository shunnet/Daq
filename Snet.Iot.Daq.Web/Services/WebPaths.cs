namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// 数据目录与路径（与 WPF 端 GlobalConfigModel 路径常量对齐，全部绝对化）
/// </summary>
public static class WebPaths
{
    #region 路径
    /// <summary>获取应用数据根目录的绝对路径。</summary>
    public static string DataDir { get; private set; } = ".";

    /// <summary>获取插件二进制目录。</summary>
    public static string FilePath => Path.Combine(DataDir, "lib");
    /// <summary>获取全部配置文件的根目录。</summary>
    public static string ConfigPath => Path.Combine(DataDir, "config");
    /// <summary>获取界面配置目录。</summary>
    public static string UiConfigPath => Path.Combine(ConfigPath, "ui");
    /// <summary>获取服务端配置目录。</summary>
    public static string ServerConfigPath => Path.Combine(ConfigPath, "server");
    /// <summary>获取地址 SQLite 数据库文件路径。</summary>
    public static string DbPath => Path.Combine(DataDir, "db", "address.db");

    /// <summary>插件参数文件目录（对齐 WPF：config/daq、config/mq，每配置一个 {文件名}）</summary>
    public static string DaqPluginConfigPath => Path.Combine(ConfigPath, "daq");
    /// <summary>获取消息转发插件参数文件目录。</summary>
    public static string MqPluginConfigPath => Path.Combine(ConfigPath, "mq");

    /// <summary>获取 OPC UA 服务端配置文件路径。</summary>
    public static string UaServerConfigPath => Path.Combine(ServerConfigPath, "UaServerConfig.json");
    /// <summary>获取 MQTT 服务端配置文件路径。</summary>
    public static string MqttServerConfigPath => Path.Combine(ServerConfigPath, "MqttServerConfig.json");
    /// <summary>获取已发现插件清单文件路径。</summary>
    public static string PluginListConfigPath => Path.Combine(UiConfigPath, "PluginList.json");
    /// <summary>获取已安装插件配置文件路径。</summary>
    public static string PluginConfigPath => Path.Combine(UiConfigPath, "PluginConfig.json");
    /// <summary>获取项目树配置文件路径。</summary>
    public static string ProjectConfigPath => Path.Combine(UiConfigPath, "ProjectConfig.json");
    /// <summary>获取插件仓库缓存文件路径。</summary>
    public static string PluginBrowseCachePath => Path.Combine(UiConfigPath, "PluginBrowseCache.json");
    /// <summary>获取 Web 用户凭据文件路径。</summary>
    public static string UserConfigPath => Path.Combine(UiConfigPath, "User.json");

    #endregion

    #region 初始化与迁移
    /// <summary>
    /// 初始化数据目录：环境变量 SNET_IOT_DAQ_DATA > appsettings 配置 > 程序目录（与 WPF DAQ 一致，配置直接放程序根目录）
    /// </summary>
    public static void Init(IConfiguration config)
    {
        var dataDir = Environment.GetEnvironmentVariable("SNET_IOT_DAQ_DATA")
            ?? config["Daq:DataDir"]
            ?? AppContext.BaseDirectory;
        DataDir = Path.GetFullPath(dataDir);
        // 显式指定目录时不能搬走默认实例的历史数据（测试/多实例部署亦如此）。
        if (Environment.GetEnvironmentVariable("SNET_IOT_DAQ_DATA") is null && config["Daq:DataDir"] is null)
            MigrateLegacyDataDir();
        Directory.CreateDirectory(FilePath);
        Directory.CreateDirectory(UiConfigPath);
        Directory.CreateDirectory(ServerConfigPath);
        Directory.CreateDirectory(Path.Combine(DataDir, "db"));
        // 插件参数文件目录（对齐 WPF config/daq、config/mq）
        Directory.CreateDirectory(DaqPluginConfigPath);
        Directory.CreateDirectory(MqPluginConfigPath);
        // LogHelper 默认使用 AppContext.BaseDirectory，改变 CWD 不会改变日志输出路径。
        var logConfig = Snet.Log.LogHelper.Get();
        logConfig.FileLocation = DataDir;
        Snet.Log.LogHelper.Set(logConfig);
    }

    /// <summary>旧版 Web 数据在 BaseDirectory/data/：整体迁移到程序根目录（与 WPF 布局一致），一次性</summary>
    private static void MigrateLegacyDataDir()
    {
        var legacy = Path.Combine(AppContext.BaseDirectory, "data");
        if (!Directory.Exists(legacy)) return;
        // 新目录已有任一数据目录则不动旧数据（避免覆盖用户新数据）
        if (Directory.Exists(FilePath) || Directory.Exists(UiConfigPath) || Directory.Exists(Path.Combine(DataDir, "db"))) return;
        foreach (var dir in new[] { "lib", "config", "db", "cer" })
        {
            var src = Path.Combine(legacy, dir);
            var dst = Path.Combine(DataDir, dir);
            if (Directory.Exists(src) && !Directory.Exists(dst))
            {
                try { Directory.Move(src, dst); } catch { /* 迁移失败不阻断启动 */ }
            }
        }
        // 旧目录已空则清理
        try
        {
            if (!Directory.EnumerateFileSystemEntries(legacy).Any()) Directory.Delete(legacy);
        }
        catch { /* 清理失败不影响主流程 */ }
    }
    #endregion
}
