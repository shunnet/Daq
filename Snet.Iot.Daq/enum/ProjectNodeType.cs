using System.Text.Json.Serialization;

namespace Snet.Iot.Daq.@enum
{
    /// <summary>
    /// 项目节点类型
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ProjectNodeType
    {
        /// <summary>
        /// 设备
        /// </summary>
        Device,
        /// <summary>
        /// 描述
        /// </summary>
        Describe
    }
}
