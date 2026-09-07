using Snet.Model.attribute;
using Snet.Model.data;
using Snet.Utility;
using System.ComponentModel;
using static Snet.Iot.Daq.Core.opc.core.Data;

namespace Snet.Iot.Daq.Core.opc.ua.service
{
    /// <summary>
    /// OPC UA 服务端数据配置类，封装 OPC UA Server 的连接参数（IP 地址、端口、认证类型、地址空间配置等）。
    /// </summary>
    public class OpcUaServiceData
    {
        /// <summary>
        /// 基础数据
        /// </summary>
        public class Basics
        {
            /// <summary>
            /// 唯一标识符
            /// </summary>
            [Category("基础数据")]
            [Description("唯一标识符")]
            public string SN { get; set; } = Guid.NewGuid().ToUpperNString();

            /// <summary>
            /// 标识
            /// </summary>
            [Description("标识")]
            [Display(true, true, false, ParamModel.dataCate.text)]
            public string Tag { get; set; } = "Opc.Ua.Service";

            /// <summary>
            /// Ip地址
            /// </summary>
            [Description("Ip地址")]
            [Verify(@"^((25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(25[0-5]|2[0-4]\d|[01]?\d\d?)$", "输入有误")]
            [Display(true, true, true, ParamModel.dataCate.text)]
            public string? IpAddress { get; set; } = "127.0.0.1";

            /// <summary>
            /// 端口
            /// </summary>
            [Description("端口")]
            [Display(true, true, false, ParamModel.dataCate.unmber)]
            public int Port { get; set; } = 6688;

            /// <summary>
            /// 认证类型
            /// </summary>
            [Description("认证类型")]
            [Display(true, true, false, ParamModel.dataCate.select)]
            public AuType AType { get; set; } = AuType.UserName;

            /// <summary>
            /// 用户
            /// </summary>
            [Description("用户名")]
            [Display(true, true, false, ParamModel.dataCate.text)]
            public string? UserName { get; set; } = string.Empty;

            /// <summary>
            /// 密码
            /// </summary>
            [Description("密码")]
            [Display(true, true, false, ParamModel.dataCate.text)]
            public string? Password { get; set; } = string.Empty;

            /// <summary>
            /// 地址空间名称
            /// </summary>
            [Description("地址空间名称")]
            [Display(true, true, false, ParamModel.dataCate.text)]
            public string? AddressSpaceName { get; set; } = "Snet";

            /// <summary>
            /// 服务端应用证书路径（PFX，含私钥，可选）。
            /// 不配置时服务端自动生成应用证书；配置时使用指定证书，
            /// 需包含监听地址（IpAddress）的 SAN 域名。
            /// </summary>
            [Description("服务端应用证书路径")]
            [Display(true, true, false, ParamModel.dataCate.upload)]
            public string? Cer { get; set; }

            /// <summary>
            /// 服务端应用证书密钥（PFX 私钥密码）
            /// </summary>
            [Description("服务端应用证书密钥")]
            [Display(true, true, false, ParamModel.dataCate.text)]
            public string? SecreKey { get; set; }

            /// <summary>
            /// 受信任用户公钥存储路径
            /// </summary>
            [Description("受信任用户公钥存储路径")]
            [Display(true, true, false, ParamModel.dataCate.text)]
            public string TrustedUserPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "cer", "trusted");

            /// <summary>
            /// 自动创建地址
            /// </summary>
            [Description("自动创建地址")]
            [Display(true, true, false, ParamModel.dataCate.radio)]
            public bool AutoCreateAddress { get; set; } = true;
        }


    }
}
