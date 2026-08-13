namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// 数据目录与路径（与 WPF 端 GlobalConfigModel 路径常量对齐，全部绝对化）
/// </summary>
public static class WebPaths
{
    public static string DataDir { get; private set; } = ".";

    public static string FilePath => Path.Combine(DataDir, "lib");
    public static string ConfigPath => Path.Combine(DataDir, "config");
    public static string UiConfigPath => Path.Combine(ConfigPath, "ui");
    public static string ServerConfigPath => Path.Combine(ConfigPath, "server");
    public static string DbPath => Path.Combine(DataDir, "db", "address.db");

    /// <summary>插件参数文件目录（对齐 WPF：config/daq、config/mq，每配置一个 {文件名}）</summary>
    public static string DaqPluginConfigPath => Path.Combine(ConfigPath, "daq");
    public static string MqPluginConfigPath => Path.Combine(ConfigPath, "mq");

    public static string UaServerConfigPath => Path.Combine(ServerConfigPath, "UaServerConfig.json");
    public static string MqttServerConfigPath => Path.Combine(ServerConfigPath, "MqttServerConfig.json");
    public static string PluginListConfigPath => Path.Combine(UiConfigPath, "PluginList.json");
    public static string PluginConfigPath => Path.Combine(UiConfigPath, "PluginConfig.json");
    public static string ProjectConfigPath => Path.Combine(UiConfigPath, "ProjectConfig.json");
    public static string PluginBrowseCachePath => Path.Combine(UiConfigPath, "PluginBrowseCache.json");
    public static string UserConfigPath => Path.Combine(UiConfigPath, "User.json");

    /// <summary>
    /// 初始化数据目录：环境变量 SNET_IOT_DAQ_DATA > appsettings 配置 > 程序目录（与 WPF DAQ 一致，配置直接放程序根目录）
    /// </summary>
    public static void Init(IConfiguration config)
    {
        var dataDir = Environment.GetEnvironmentVariable("SNET_IOT_DAQ_DATA")
            ?? config["Daq:DataDir"]
            ?? AppContext.BaseDirectory;
        DataDir = Path.GetFullPath(dataDir);
        MigrateLegacyDataDir();
        Directory.CreateDirectory(FilePath);
        Directory.CreateDirectory(UiConfigPath);
        Directory.CreateDirectory(ServerConfigPath);
        Directory.CreateDirectory(Path.Combine(DataDir, "db"));
        // 插件参数文件目录（对齐 WPF config/daq、config/mq）
        Directory.CreateDirectory(DaqPluginConfigPath);
        Directory.CreateDirectory(MqPluginConfigPath);
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
}
