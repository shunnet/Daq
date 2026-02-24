namespace Snet.Iot.Daq.data
{
    /// <summary>
    /// 插件详情模型
    /// </summary>
    public class PluginDetailsModel
    {
        public PluginDetailsModel() { }
        public PluginDetailsModel(string name, string @namespace, string configFormat, string version)
        {
            Name = name;
            Namespace = @namespace;
            ConfigFormat = configFormat;
            Version = version;
        }
        public PluginDetailsModel(string name, string @namespace, string configFormat, string pluginPath, string version)
        {
            Name = name;
            Namespace = @namespace;
            ConfigFormat = configFormat;
            PluginPath = pluginPath;
            Version = version;
        }

        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// 命名空间
        /// </summary>
        public string Namespace { get; set; } = string.Empty;
        /// <summary>
        /// 配置格式
        /// </summary>
        public string ConfigFormat { get; set; } = string.Empty;
        /// <summary>
        /// 插件的路径
        /// </summary>
        public string PluginPath { get; set; } = string.Empty;

        /// <summary>
        /// 版本
        /// </summary>
        public string Version { get; set; } = string.Empty;
    }
}
