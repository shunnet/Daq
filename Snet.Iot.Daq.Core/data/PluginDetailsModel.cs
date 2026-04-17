namespace Snet.Iot.Daq.Core.data
{
    /// <summary>
    /// 插件详情模型
    /// </summary>
    public class PluginDetailsModel
    {
        /// <summary>
        /// 无参构造函数
        /// </summary>
        public PluginDetailsModel() { }

        /// <summary>
        /// 带名称、命名空间、配置格式和版本的构造函数
        /// </summary>
        /// <param name="name">插件名称</param>
        /// <param name="namespace">插件命名空间</param>
        /// <param name="configFormat">配置文件格式模板</param>
        /// <param name="version">插件版本</param>
        public PluginDetailsModel(string name, string @namespace, string configFormat, string version)
        {
            Name = name;
            Namespace = @namespace;
            ConfigFormat = configFormat;
            Version = version;
        }

        /// <summary>
        /// 带名称、命名空间、配置格式、插件路径和版本的构造函数
        /// </summary>
        /// <param name="name">插件名称</param>
        /// <param name="namespace">插件命名空间</param>
        /// <param name="configFormat">配置文件格式模板</param>
        /// <param name="pluginPath">插件所在目录路径</param>
        /// <param name="version">插件版本</param>
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
