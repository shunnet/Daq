using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.@enum;
using Snet.Iot.Daq.Core.mvvm;

namespace Snet.Iot.Daq.data
{
    /// <summary>
    /// 插件列表模型
    /// </summary>
    public class PluginListModel : BindNotify
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        public PluginListModel() { }
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="name">名称</param>
        /// <param name="type">类型</param>
        /// <param name="version">版本</param>
        /// <param name="time">添加时间</param>
        /// <param name="pluginDetails">插件详情数据</param>
        public PluginListModel(string name, PluginType type, string version, DateTime time, PluginDetailsModel pluginDetails)
        {
            Name = name;
            Type = type;
            Version = version;
            Time = time;
            PluginDetails = pluginDetails;
        }

        /// <summary>
        /// 名称
        /// </summary>
        public string Name
        {
            get => GetProperty(() => Name);
            set => SetProperty(() => Name, value);
        }

        /// <summary>
        /// 类型
        /// </summary>
        public PluginType Type
        {
            get => GetProperty(() => Type);
            set => SetProperty(() => Type, value);
        }

        /// <summary>
        /// 版本
        /// </summary>
        public string Version
        {
            get => GetProperty(() => Version);
            set => SetProperty(() => Version, value);
        }

        /// <summary>
        /// 添加时间
        /// </summary>
        public DateTime Time
        {
            get => GetProperty(() => Time);
            set => SetProperty(() => Time, value);
        }

        /// <summary>
        /// 插件详情数据
        /// </summary>
        public PluginDetailsModel PluginDetails { get; set; }

    }
}
