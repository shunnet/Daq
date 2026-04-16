using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Snet.Iot.Daq.Core.@enum
{
    /// <summary>
    /// 插件类型
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PluginType
    {
        /// <summary>
        /// 数据采集插件
        /// </summary>
        [Description("数据采集插件")]
        Daq,
        /// <summary>
        /// 消息传输队列插件
        /// </summary>
        [Description("消息传输队列插件")]
        Mq
    }
}
