using System.Text.Json.Serialization;

namespace Snet.Iot.Daq.Core.@enum
{
    /// <summary>
    /// 项目设备节点类型
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ProjectDetailsNodeType
    {
        /// <summary>
        /// 地址
        /// </summary>
        Address,
        /// <summary>
        /// 消息传输
        /// </summary>
        Mq
    }
}
