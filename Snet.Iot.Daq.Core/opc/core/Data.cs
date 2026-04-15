using System.ComponentModel;
using System.IO;
using System.Text.Json.Serialization;

namespace Snet.Iot.Daq.Core.opc.core
{

    /// <summary>
    /// 基础数据
    /// </summary>
    public class Data
    {
        /// <summary>
        /// 认证类型
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum AuType
        {
            /// <summary>
            /// 匿名
            /// </summary>
            [Description("匿名")]
            Anonymous,
            /// <summary>
            /// 用户名
            /// </summary>
            [Description("用户名")]
            UserName,
            /// <summary>
            /// 证书
            /// </summary>
            [Description("证书")]
            Certificate,
        }

        /// <summary>
        /// 证书路径
        /// </summary>
        public static string CerPath = Path.Combine(AppContext.BaseDirectory, "cer");
    }
}
